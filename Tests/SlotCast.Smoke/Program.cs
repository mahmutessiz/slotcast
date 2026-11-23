using episodeTracker.Models;
using episodeTracker.Services;
using episodeTracker.ViewModels.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using Wpf.Ui.Markup;
using Wpf.Ui.Appearance;
using System.Windows.Controls;

var folder = Path.Combine(Path.GetTempPath(), "SlotCast-Smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var database = Path.Combine(folder, "test.db");
var livePosters = new List<(string Label, byte[] Bytes)>();
var handler = new FixtureHandler();
using (var source = CreateServices(Path.Combine(folder, "backup-source.db"), handler))
using (var target = CreateServices(Path.Combine(folder, "backup-target.db"), handler))
{
    var stamp = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    await using (var db = await source.GetRequiredService<IDbContextFactory<SlotCastDbContext>>().CreateDbContextAsync())
    {
        await db.Database.EnsureCreatedAsync();
        db.Anime.Add(new LibraryAnime { Route = "portable-tv", Title = "Portable TV", Year = 2026,
            TotalEpisodes = 12, AddedUtc = stamp, PosterUrl = "https://example.com/poster.jpg",
            WatchedEpisodes = [new() { EpisodeNumber = 1, WatchedUtc = stamp }, new() { EpisodeNumber = 2, WatchedUtc = stamp }] });
        db.Preferences.Add(new LocalPreference { Key = "schedule", Value = "dub" });
        db.Timetables.Add(new TimetableCache { Key = "private-cache", Json = "[]", FetchedUtc = stamp });
        await db.SaveChangesAsync();
    }
    var backupFile = Path.Combine(folder, "watchlist.json");
    var exporter = source.GetRequiredService<WatchlistTransferService>();
    Check(await exporter.ExportAsync(backupFile) == new WatchlistTransferResult(1, 2), "JSON exports titles and watched episodes");
    await exporter.ExportAsync(backupFile);
    var json = await File.ReadAllTextAsync(backupFile);
    Check(!json.Contains("private-cache") && !json.Contains("libraryAnimeId") && !json.Contains("apiToken"),
        "Portable backup excludes caches, local IDs and credentials");
    await using (var db = await target.GetRequiredService<IDbContextFactory<SlotCastDbContext>>().CreateDbContextAsync())
    {
        await db.Database.EnsureCreatedAsync();
        db.Anime.Add(new LibraryAnime { Route = "local-only", Title = "Local only", AddedUtc = stamp });
        await db.SaveChangesAsync();
    }
    var importer = target.GetRequiredService<WatchlistTransferService>();
    Check(await importer.ImportAsync(backupFile) == new WatchlistTransferResult(1, 2), "Backup restores onto another database with different local IDs");
    await using (var db = await target.GetRequiredService<IDbContextFactory<SlotCastDbContext>>().CreateDbContextAsync())
    {
        var restored = await db.Anime.Include(x => x.WatchedEpisodes).SingleAsync(x => x.Route == "portable-tv");
        Check(restored.Id != 1 && restored.PosterUrl == "https://example.com/poster.jpg" && restored.AddedUtc == stamp
            && restored.WatchedEpisodes.All(x => x.WatchedUtc == stamp), "Import preserves metadata, timestamps and correct episode ownership");
        restored.WatchedEpisodes.Add(new WatchedEpisode { EpisodeNumber = 3, WatchedUtc = stamp });
        await db.SaveChangesAsync();
    }
    Check(await importer.ImportAsync(backupFile) == new WatchlistTransferResult(0, 0), "Repeated import is idempotent and preserves newer local progress");
    var badFile = Path.Combine(folder, "invalid.json");
    await File.WriteAllTextAsync(badFile, json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99"));
    try { await importer.ImportAsync(badFile); throw new Exception("Future backup was accepted"); }
    catch (InvalidDataException) { Check(true, "Future backup versions are rejected before database changes"); }
    await File.WriteAllTextAsync(badFile, json.Replace("\"number\": 2", "\"number\": 1"));
    try { await importer.ImportAsync(badFile); throw new Exception("Duplicate episode was accepted"); }
    catch (InvalidDataException) { Check(true, "Invalid duplicate episodes are rejected before database changes"); }
    var store = target.GetRequiredService<AnimeLibraryService>();
    await store.InitializeAsync();
    var saved = await store.GetLibraryAsync();
    Check(saved.Count == 2 && saved.Single(x => x.Route == "portable-tv").WatchedEpisodes.Count == 3,
        "Startup initialization and rejected imports preserve all existing library data");
    Check(await store.GetSchedulePreferenceAsync() == "dub", "Import restores release schedule preference");
    var portable = saved.Single(x => x.Route == "portable-tv");
    await store.SetCompletedAsync(portable.Id, true);
    var completedTitle = (await store.GetLibraryAsync()).Single(x => x.Id == portable.Id);
    Check(completedTitle.IsCompleted && completedTitle.WatchedEpisodes.Count == 12,
        "Mark completed archives a series and marks its known episodes watched");
    await store.ToggleWatchedAsync(portable.Id, 12);
    Check(!(await store.GetLibraryAsync()).Single(x => x.Id == portable.Id).IsCompleted,
        "Undoing a watched episode reopens a completed series");
    await store.ToggleWatchedAsync(portable.Id, 12);
    Check((await store.GetLibraryAsync()).Single(x => x.Id == portable.Id).IsCompleted,
        "Watching the final remaining episode automatically completes the series");
    var localOnly = saved.Single(x => x.Route == "local-only");
    await store.SetCompletedAsync(localOnly.Id, true);
    await store.InitializeAsync();
    Check((await store.GetLibraryAsync()).Single(x => x.Id == localOnly.Id).IsCompleted,
        "Manual completion persists for series with unknown episode totals");
    var archiveVm = new DashboardViewModel(target.GetRequiredService<AnimeScheduleService>(), store);
    await archiveVm.ReloadAfterImportAsync();
    Check(archiveVm.VisibleLibraryItems.Count == 0, "Completed series leave the Watching section");
    archiveVm.ShowArchive = true;
    archiveVm.SelectedAnime = archiveVm.VisibleLibraryItems.Single(x => x.Route == "portable-tv");
    Check(archiveVm.VisibleLibraryItems.Count == 2 && archiveVm.EpisodeRows.All(x => x.WatchedStatus == "Watched"),
        "Archive shows completed series and explicit watched status per episode");
    var archiveFile = Path.Combine(folder, "archive.json");
    await importer.ExportAsync(archiveFile);
    using var newPc = CreateServices(Path.Combine(folder, "archive-restore.db"), handler);
    await newPc.GetRequiredService<WatchlistTransferService>().ImportAsync(archiveFile);
    Check((await newPc.GetRequiredService<AnimeLibraryService>().GetLibraryAsync()).All(x => x.IsCompleted),
        "JSON transfer preserves automatic and manual completion across PCs");
}
foreach (var (date, slug, year) in new[]
{
    (new DateTime(2026, 1, 1), "winter", 2026), (new DateTime(2026, 3, 31), "winter", 2026),
    (new DateTime(2026, 4, 1), "spring", 2026), (new DateTime(2026, 6, 30), "spring", 2026),
    (new DateTime(2026, 7, 1), "summer", 2026), (new DateTime(2026, 9, 30), "summer", 2026),
    (new DateTime(2026, 10, 1), "fall", 2026), (new DateTime(2026, 12, 31), "fall", 2026),
    (new DateTime(2027, 1, 1), "winter", 2027)
})
    Check(AnimeSeason.ForDate(date) == new AnimeSeason(slug, year), $"Anime season boundary {date:yyyy-MM-dd}");
using var services = CreateServices(database, handler);
var library = services.GetRequiredService<AnimeLibraryService>();
var api = services.GetRequiredService<AnimeScheduleService>();
var vm = new DashboardViewModel(api, library);
await vm.OnNavigatedToAsync();
Check(vm.ErrorMessage == "" && vm.LibraryItems.Count == 0, "Fresh SQLite database opens");
vm.SearchQuery = "test";
await vm.SearchCommand.ExecuteAsync(null);
Check(vm.SearchResults.Count == 1 && vm.SearchResults[0].Route == "test-tv", "Search excludes OVA results");
await vm.AddAnimeCommand.ExecuteAsync(vm.SearchResults[0]);
Check(vm.LibraryItems.Count == 1, "Anime is saved to library");
await vm.AddAnimeCommand.ExecuteAsync(vm.SearchResults[0]);
Check(vm.LibraryItems.Count == 1, "Duplicate add does not duplicate library entry");
Check(vm.CalendarDays.Count == 7 && vm.CalendarDays[3].Date == DateTime.Today,
    "Seven-day calendar places today in the middle");
var episode = vm.CalendarDays.SelectMany(x => x.Episodes).Single(x => x.Number == 1);
Check(episode.AirType == "raw", "Subtitle fallback retains Japanese broadcast label");
Check(vm.CalendarDays.Single(x => x.Episodes.Contains(episode)).Date == episode.AirsAt.ToLocalTime().Date,
    "Calendar assigns episode to local date");
await vm.ToggleCalendarEpisodeCommand.ExecuteAsync(episode);
Check(vm.LibraryItems[0].WatchedEpisodes.Count == 1, "Watched progress is saved");
var watchedCard = vm.CalendarDays.SelectMany(x => x.Episodes).Single(x => x.Number == 1);
Check(watchedCard.WatchIcon == "✓" && watchedCard.IsWatched, "Watched poster overlay displays a checkmark");
await vm.ToggleCalendarEpisodeCommand.ExecuteAsync(watchedCard);
var unwatchedCard = vm.CalendarDays.SelectMany(x => x.Episodes).Single(x => x.Number == 1);
Check(unwatchedCard.WatchIcon == "○" && !unwatchedCard.IsWatched, "Poster overlay can undo watched state");
await vm.ToggleCalendarEpisodeCommand.ExecuteAsync(unwatchedCard);
Check(unwatchedCard.AiringSeasonLabel.Contains(AnimeSeason.ForDate(unwatchedCard.AirsAt.ToLocalTime().DateTime).Label),
    "Calendar displays the local airing season alongside the episode number");
var count = handler.TimetableRequests;
await vm.TodayCommand.ExecuteAsync(null);
Check(handler.TimetableRequests == count, "Reopening week uses SQLite timetable cache");
handler.DelayHours = 2;
await vm.RefreshCommand.ExecuteAsync(null);
Check(vm.LibraryItems[0].WatchedEpisodes.Count == 1, "Schedule refresh preserves watched history");
Check(vm.CalendarDays.SelectMany(x => x.Episodes).Single(x => x.Number == 1).AirsAt == handler.AirDate.AddHours(2),
    "Refresh replaces changed airing date");
var batch = vm.CalendarDays.SelectMany(x => x.Episodes).Where(x => x.Number is 2 or 3).ToList();
Check(batch.Count == 2, "Batch release produces each episode");
var future = vm.CalendarDays.SelectMany(x => x.Episodes).Single(x => x.Number == 4);
Check(!future.CanMark, "Future episodes cannot be marked from calendar");
var originalWeek = vm.DisplayWeek;
await vm.NextWeekCommand.ExecuteAsync(null);
Check(vm.DisplayWeek == originalWeek.AddDays(7) && vm.CalendarDays[0].Date == vm.DisplayWeek,
    "Next week advances exactly seven days");
await vm.PreviousWeekCommand.ExecuteAsync(null);
Check(vm.DisplayWeek == originalWeek, "Previous week restores the original week");
await vm.TodayCommand.ExecuteAsync(null);
Check(vm.SelectedDate == DateTime.Today && vm.CalendarDays.Any(x => x.Date == DateTime.Today),
    "Today returns to the current week and selects today");
await api.GetTimetableAsync(new DateTime(2027, 1, 1), new DateTime(2027, 1, 5), "sub", false);
Check(handler.Requests.Any(x => x.Contains("year=2026&week=53")), "January uses the correct ISO week-year across New Year");
handler.Offline = true;
await vm.RefreshCommand.ExecuteAsync(null); // Detail refresh fails first; no data should be lost.
Check(vm.LibraryItems[0].WatchedEpisodes.Count == 1, "Offline refresh does not lose progress");
var cached = await api.GetTimetableAsync(DateTime.Today.AddDays(-3), DateTime.Today.AddDays(3), "sub", true);
Check(cached.IsStale && cached.Entries.Count > 0, "Failed refresh falls back to persisted timetable with stale indicator");
var missing = await api.GetTimetableAsync(new DateTime(2020, 1, 1), new DateTime(2020, 1, 5), "raw", false);
Check(missing.HasMissingWeeks && missing.Entries.Count == 0, "Unavailable uncached weeks are reported as incomplete");
using (var reopened = CreateServices(database, handler))
{
    var saved = await reopened.GetRequiredService<AnimeLibraryService>().GetLibraryAsync();
    Check(saved.Count == 1 && saved[0].WatchedEpisodes.Count == 1, "Library and progress survive reopening database");
    var store = reopened.GetRequiredService<AnimeLibraryService>();
    await store.RemoveAsync(saved[0].Id);
    await using var db = await reopened.GetRequiredService<IDbContextFactory<SlotCastDbContext>>().CreateDbContextAsync();
    Check(!await db.WatchedEpisodes.AnyAsync(), "Removing anime cascades watched history");
}
handler.Offline = false;
var fixtureConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Simkl:ClientId"] = "test-client" }).Build();
var home = new HomeViewModel(api, new SimklService(new FixtureFactory(handler), fixtureConfig), vm);
await home.RefreshHomeCommand.ExecuteAsync(null);
Check(home.NewReleases.Count == 1 && home.Upcoming.Count == 1, "Home release feeds exclude OVA entries");
Check(home.CurrentSeasonLabel == AnimeSeason.ForDate(DateTime.Today).Label && home.SeasonAnime.Count == 1,
    "Home determines the current anime season from local date and excludes OVAs");
Check(home.HasMoreSeasonAnime, "Season section offers additional pages");
await home.LoadMoreSeasonCommand.ExecuteAsync(null);
Check(home.SeasonAnime.Count == 2 && !home.HasMoreSeasonAnime, "Load more appends remaining seasonal TV anime");
var expectedSeason = AnimeSeason.ForDate(DateTime.Today);
Check(handler.Requests.Any(x => x.Contains($"seasons={expectedSeason.Slug}&years={expectedSeason.Year}") && x.EndsWith("page=2")),
    "Season requests include correct year, quarter, and page");
Check(home.AiringToday.Count == 1 && home.AiringToday[0].AirsAt?.ToLocalTime().Date == DateTime.Today
    && home.AiringToday[0].Anime?.Route == "today-tv", "Airing today contains only today's local TV-anime schedule");
Check(home.Trending.Count == 1 && home.Trending[0].Anime is null, "Home uses public TV-only trending and requires explicit season matching");
await home.AddFromHomeCommand.ExecuteAsync(home.NewReleases[0]);
Check(vm.LibraryItems.Count == 1, "New releases can be added to saved library");
vm.ShowFullSchedule = true;
if (vm.ChangeCalendarScopeCommand.ExecutionTask is { } fullLoad) await fullLoad;
var fullSchedule = vm.CalendarDays.SelectMany(x => x.Episodes).ToList();
Check(fullSchedule.Any(x => x.Route == "untracked-tv") && fullSchedule.Any(x => x.Route == "another-tv"),
    "Full schedule includes distinct unsaved TV anime even when episode numbers match");
var unsaved = fullSchedule.First(x => x.Route == "untracked-tv");
Check(!unsaved.CanMark && unsaved.CanAct && unsaved.ActionLabel == "+" && unsaved.HasBanner,
    "Unsaved schedule cards expose add action and timetable artwork");
await vm.ToggleCalendarEpisodeCommand.ExecuteAsync(unsaved);
Check(vm.LibraryItems.Count == 2 && vm.LibraryItems.Any(x => x.Route == unsaved.Route),
    "Full schedule plus action saves anime without marking it watched");
vm.ShowFullSchedule = false;
if (vm.ChangeCalendarScopeCommand.ExecutionTask is { } libraryLoad) await libraryLoad;
Check(vm.CalendarDays.SelectMany(x => x.Episodes).All(x => x.IsTracked), "Library calendar filters out unsaved titles");
await home.AddFromHomeCommand.ExecuteAsync(home.AiringToday[0]);
Check(vm.LibraryItems.Any(x => x.Route == "today-tv" && x.Year == 2026 && x.TotalEpisodes == 12),
    "Adding an airing-today card saves complete title metadata");
using (var emptyServices = CreateServices(Path.Combine(folder, "empty-library.db"), handler))
{
    var emptyVm = new DashboardViewModel(emptyServices.GetRequiredService<AnimeScheduleService>(),
        emptyServices.GetRequiredService<AnimeLibraryService>());
    await emptyVm.OnNavigatedToAsync();
    emptyVm.ShowFullSchedule = true;
    if (emptyVm.ChangeCalendarScopeCommand.ExecutionTask is { } emptyLoad) await emptyLoad;
    Check(emptyVm.LibraryItems.Count == 0 && emptyVm.CalendarDays.SelectMany(x => x.Episodes).Any(),
        "Full schedule works with an empty library");
    Check(emptyVm.CalendarDays.SelectMany(x => x.Episodes).All(x => x.Route != "untracked-ova"),
        "Full schedule still excludes OVAs");
}
await home.AddFromHomeCommand.ExecuteAsync(home.Trending[0]);
Check(vm.SearchQuery == home.Trending[0].Title && vm.SearchResults.Count == 1, "Trending card opens title search without guessing provider IDs");
var detailState = new AnimeDetailsState { Card = home.NewReleases[0] };
var detailVm = new AnimeDetailsViewModel(detailState, api,
    new SimklService(new FixtureFactory(handler), fixtureConfig), vm);
await detailVm.OnNavigatedToAsync();
Check(detailVm.Title == "Test TV" && detailVm.Description == "An anime synopsis.",
    "Internal details page loads AnimeSchedule data and removes HTML");
detailState.Card = home.Trending[0];
await detailVm.OnNavigatedToAsync();
Check(detailVm.Title == "SIMKL TV" && detailVm.Description == "Trending synopsis.",
    "Internal details page loads public SIMKL anime details without guessing season IDs");
detailState.Card = home.NewReleases[0];
await detailVm.OnNavigatedToAsync();
if (args.Contains("--live"))
{
    var configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json").AddJsonFile("appsettings.local.json").Build();
    var token = configuration["AnimeScheduleApiToken"];
    Check(!string.IsNullOrWhiteSpace(token), "Local application token is configured");
    var liveServices = new ServiceCollection();
    liveServices.AddDbContextFactory<SlotCastDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(folder, "live.db")}"));
    liveServices.AddSingleton<AnimeLibraryService>();
    liveServices.AddSingleton<AnimeScheduleService>();
    liveServices.AddSingleton<IConfiguration>(configuration);
    liveServices.AddSingleton<SimklService>();
    liveServices.AddHttpClient("SimklPublic", client =>
    {
        client.BaseAddress = new Uri("https://data.simkl.in/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SlotCast/1.0");
    });
    liveServices.AddHttpClient("Simkl", client =>
    {
        client.BaseAddress = new Uri("https://api.simkl.com/");
        client.DefaultRequestHeaders.Add("simkl-api-key", configuration["Simkl:ClientId"]);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SlotCast/1.0");
    });
    liveServices.AddHttpClient("AnimeSchedule", client =>
    {
        client.BaseAddress = new Uri("https://animeschedule.net/api/v3/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SlotCast/1.0");
        client.Timeout = TimeSpan.FromSeconds(25);
    });
    using var live = liveServices.BuildServiceProvider();
    await live.GetRequiredService<AnimeLibraryService>().InitializeAsync();
    var liveApi = live.GetRequiredService<AnimeScheduleService>();
    var search = await liveApi.SearchAsync("frieren", 1);
    Check(search.Anime.Count > 0 && search.Anime.All(x => x.IsTv), "Live search deserializes TV-only results");
    var details = await liveApi.DetailsAsync(search.Anime[0].Route);
    Check(details.Route == search.Anime[0].Route, "Live anime detail request deserializes");
    var timetable = await liveApi.GetTimetableAsync(DateTime.Today, DateTime.Today.AddDays(7), "sub", false);
    Check(!timetable.IsStale && !timetable.HasMissingWeeks && timetable.Entries.Count > 0,
        "Live subtitle timetable supplies episode dates");
    var liveHome = new HomeViewModel(liveApi, live.GetRequiredService<SimklService>(), vm);
    await liveHome.RefreshHomeCommand.ExecuteAsync(null);
    Check(liveHome.ErrorMessage == "" && liveHome.NewReleases.Count > 0 && liveHome.Upcoming.Count > 0
        && liveHome.Trending.Count > 0, "Live Home loads new releases, upcoming premieres, and public trending");
    Check(liveHome.SeasonAnime.Count > 0 && liveHome.CurrentSeasonLabel == AnimeSeason.ForDate(DateTime.Today).Label,
        "Live current-season TV anime feed loads with the correct season heading");
    var liveDetailsState = new AnimeDetailsState { Card = liveHome.NewReleases[0] };
    var liveDetails = new AnimeDetailsViewModel(liveDetailsState, liveApi, live.GetRequiredService<SimklService>(), vm);
    await liveDetails.OnNavigatedToAsync();
    Check(liveDetails.ErrorMessage == "" && liveDetails.Title.Length > 0,
        "Live AnimeSchedule details load inside the app");
    liveDetailsState.Card = liveHome.Trending[0];
    await liveDetails.OnNavigatedToAsync();
    Check(liveDetails.ErrorMessage == "" && liveDetails.Description != "No synopsis available.",
        "Live public SIMKL anime details include the synopsis");
    using var imageClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    foreach (var card in new[] { liveHome.NewReleases.First(x => x.PosterUrl is not null), liveHome.Trending.First(x => x.PosterUrl is not null) })
        livePosters.Add((card.Anime is null ? "SIMKL" : "AnimeSchedule", await imageClient.GetByteArrayAsync(card.PosterUrl)));
}
Exception? renderError = null;
var uiThread = new Thread(() =>
{
    try
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
        application.Resources.MergedDictionaries.Add(new ControlsDictionary());
        foreach (var poster in livePosters)
        {
            using var stream = new MemoryStream(poster.Bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(decoder.Frames[0].PixelWidth > 0, $"{poster.Label} poster decodes in WPF");
        }
        var trace = new StringWriter();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(trace));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        home.NewReleases = Enumerable.Range(1, 10).Select(i => new HomeAnimeCard
        {
            Title = $"A longer TV anime title {i}", Subtitle = "2026 · TV · 12 episodes", Anime = home.NewReleases[0].Anime
        }).ToList();
        Page[] pages = [new episodeTracker.Views.Pages.DashboardPage(home),
            new episodeTracker.Views.Pages.CalendarPage(vm), new episodeTracker.Views.Pages.LibraryPage(vm),
            new episodeTracker.Views.Pages.DiscoverPage(vm), new episodeTracker.Views.Pages.AnimeDetailsPage(detailVm),
            new episodeTracker.Views.Pages.SettingsPage(new SettingsViewModel(services.GetRequiredService<WatchlistTransferService>(), vm))];
        foreach (var page in pages)
        {
            page.Width = 1160;
            page.Height = 780;
            page.Measure(new Size(1160, 780));
            page.Arrange(new Rect(0, 0, 1160, 780));
            page.UpdateLayout();
            var image = new RenderTargetBitmap(1160, 780, 96, 96, PixelFormats.Pbgra32);
            image.Render(page);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(folder, $"{page.GetType().Name}-preview.png"))) encoder.Save(stream);
            Check(VisualTreeHelper.GetChildrenCount(page) > 0, $"{page.GetType().Name} loads and lays out");
        }
        var calendar = pages[1];
        var weekScroller = (ScrollViewer)calendar.FindName("WeekScroller");
        Check(weekScroller.ScrollableWidth > 0, "Wide weekly columns enable horizontal scrolling");
        Check(((ItemsControl)calendar.FindName("WeekItems")).ActualWidth / 7 >= 240,
            "Compact calendar day columns retain readable widths");
        Check(Visuals<System.Windows.Controls.Image>(calendar).All(x => x.Stretch == Stretch.Uniform),
            "Calendar posters preserve the full image without cropping");
        var episodePoster = Visuals<Border>(calendar).First(x => x.ToolTip is ToolTip);
        Check(((ToolTip)episodePoster.ToolTip).ContentTemplate is not null,
            "Calendar posters provide rich episode information on hover");
        weekScroller.ScrollToRightEnd();
        calendar.UpdateLayout();
        Check(weekScroller.HorizontalOffset > 0, "Weekly calendar scrolls to the last days");
        weekScroller.ScrollToLeftEnd();
        var releaseList = Visuals<ItemsControl>(pages[0]).First(x => ReferenceEquals(x.ItemsSource, home.NewReleases));
        var firstCard = (FrameworkElement)releaseList.ItemContainerGenerator.ContainerFromIndex(0);
        var lastCard = (FrameworkElement)releaseList.ItemContainerGenerator.ContainerFromIndex(9);
        Check(lastCard.TranslatePoint(new Point(), pages[0]).Y > firstCard.TranslatePoint(new Point(), pages[0]).Y,
            "Home poster cards wrap onto additional rows");
        Check(Visuals<ScrollViewer>(pages[0]).All(x => x.ScrollableWidth == 0), "Home has no sideways scrolling");
        var framedHome = new episodeTracker.Views.Pages.DashboardPage(home);
        var presenter = new Wpf.Ui.Controls.NavigationViewContentPresenter { Width = 1160, Height = 780, TransitionDuration = 0 };
        var hostWindow = new Window { Content = presenter, Width = 1160, Height = 780,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        hostWindow.Show();
        presenter.Navigate(framedHome);
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        presenter.Measure(new Size(1160, 780));
        presenter.Arrange(new Rect(0, 0, 1160, 780));
        presenter.UpdateLayout();
        var homeScroll = (ScrollViewer)framedHome.FindName("HomeScroller");
        Console.WriteLine($"Navigation scroll measurement: page={framedHome.ActualHeight}, viewport={homeScroll.ViewportHeight}, extent={homeScroll.ExtentHeight}, loaded={framedHome.IsLoaded}");
        Check(!presenter.IsDynamicScrollViewerEnabled, "Navigation frame does not wrap Home in a second scroll container");
        Check(homeScroll.ScrollableHeight > 0, "Home has a vertical scroll range inside the real navigation frame");
        homeScroll.ScrollToBottom();
        presenter.UpdateLayout();
        Check(homeScroll.VerticalOffset > 0, "Home can scroll vertically to trending and upcoming sections");
        var framedCalendar = new episodeTracker.Views.Pages.CalendarPage(vm);
        presenter.Navigate(framedCalendar);
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        presenter.UpdateLayout();
        var centeredScroller = (ScrollViewer)framedCalendar.FindName("WeekScroller");
        var weekItems = (ItemsControl)framedCalendar.FindName("WeekItems");
        var todayCenter = 3.5 * weekItems.ActualWidth / 7 - centeredScroller.HorizontalOffset;
        Check(Math.Abs(todayCenter - centeredScroller.ViewportWidth / 2) < 2, "Loaded calendar centers today's column in the viewport");
        var todayHeader = Visuals<Button>(framedCalendar).First(x => x.DataContext is CalendarDay day && day.IsToday);
        var todayPulse = (Border)todayHeader.Template.FindName("TodayFlash", todayHeader);
        Check(todayPulse.HasAnimatedProperties, "Today receives a brief highlight animation");
        hostWindow.Close();
        calendar.UpdateLayout();
        Check(weekScroller.ViewportWidth > 1100, "Calendar uses the full page width without a sidebar");
        var navigation = new episodeTracker.ViewModels.Windows.MainWindowViewModel();
        var targets = navigation.MenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>().Select(x => x.TargetPageType).ToList();
        Check(targets.Count == 4 && targets.Distinct().Count() == 4, "Side navigation has four distinct page destinations");
        if (trace.ToString().Contains("Error:")) Console.WriteLine(trace.ToString());
        Check(!trace.ToString().Contains("Error:"), "Home, calendar, library, and search have no binding errors");
        application.Shutdown();
    }
    catch (Exception ex) { renderError = ex; }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
uiThread.Join();
if (renderError is not null) throw new Exception("WPF render check failed", renderError);
Console.WriteLine("All checks passed. Temporary databases and preview: " + folder);

static ServiceProvider CreateServices(string database, FixtureHandler handler)
{
    var collection = new ServiceCollection();
    collection.AddDbContextFactory<SlotCastDbContext>(o => o.UseSqlite($"Data Source={database}"));
    collection.AddSingleton<AnimeLibraryService>();
    collection.AddSingleton<WatchlistTransferService>();
    collection.AddSingleton<AnimeScheduleService>();
    collection.AddSingleton<IHttpClientFactory>(new FixtureFactory(handler));
    return collection.BuildServiceProvider();
}
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    Console.WriteLine("PASS: " + message);
}
static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
{
    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
    {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is T value) yield return value;
        foreach (var descendant in Visuals<T>(child)) yield return descendant;
    }
}
sealed class FixtureFactory(FixtureHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        { BaseAddress = new Uri("https://animeschedule.net/api/v3/") };
}
sealed class FixtureHandler : HttpMessageHandler
{
    public int TimetableRequests { get; private set; }
    public int DelayHours { get; set; }
    public bool Offline { get; set; }
    public List<string> Requests { get; } = [];
    public DateTimeOffset AirDate { get; } = new DateTimeOffset(DateTime.Today.AddDays(-1)).ToUniversalTime();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Offline) throw new HttpRequestException("Offline fixture");
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add(request.RequestUri.PathAndQuery);
        var tv = new AnimeDetails { Route = "test-tv", Title = "Test TV", Description = "<b>An anime synopsis.</b>", Episodes = 4, Year = 2026,
            Status = "Ongoing", MediaTypes = [new AnimeCategory { Route = "tv" }] };
        string json;
        if (path.Contains("timetables"))
        {
            TimetableRequests++;
            json = JsonSerializer.Serialize(new[]
            {
                new AnimeTimetableEntry { Route = tv.Route, EpisodeNumber = 1, EpisodeDate = AirDate.AddHours(DelayHours), AirType = "raw", MediaTypes = tv.MediaTypes },
                new AnimeTimetableEntry { Route = tv.Route, EpisodeNumber = 3, SubtractedEpisodeNumber = 2, EpisodeDate = AirDate, AirType = "sub", MediaTypes = tv.MediaTypes },
                new AnimeTimetableEntry { Route = tv.Route, EpisodeNumber = 4, EpisodeDate = new DateTimeOffset(DateTime.Today.AddDays(2)), AirType = "sub", MediaTypes = tv.MediaTypes },
                new AnimeTimetableEntry { Route = "untracked-tv", Title = "Unsaved anime", EpisodeNumber = 1, EpisodeDate = AirDate, AirType = "sub", MediaTypes = tv.MediaTypes, ImageVersionRoute = "anime/jpg/default/test.jpg" },
                new AnimeTimetableEntry { Route = "another-tv", Title = "Another unsaved anime", EpisodeNumber = 1, EpisodeDate = AirDate, AirType = "sub", MediaTypes = tv.MediaTypes },
                new AnimeTimetableEntry { Route = "today-tv", Title = "Today's anime", EpisodeNumber = 6, EpisodeDate = new DateTimeOffset(DateTime.Today.AddHours(12)), AirType = "sub", MediaTypes = tv.MediaTypes },
                new AnimeTimetableEntry { Route = "untracked-ova", Title = "OVA", EpisodeNumber = 1, EpisodeDate = AirDate, AirType = "sub", MediaTypes = [new AnimeCategory { Route = "ova" }] }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        else if (request.RequestUri.Query.Contains("seasons="))
        {
            var nextPage = request.RequestUri.Query.Contains("page=2");
            json = JsonSerializer.Serialize(new AnimeSearchResponse { Page = nextPage ? 2 : 1, TotalAmount = 19,
                Anime = nextPage
                    ? [new AnimeDetails { Route = "season-tv-2", Title = "Season TV 2", MediaTypes = tv.MediaTypes }]
                    : [tv, new AnimeDetails { Route = "season-ova", MediaTypes = [new AnimeCategory { Route = "ova" }] }] });
        }
        else if (path.Contains("/discover/trending/")) json = """
            [{"title":"Test TV","anime_type":"tv","url":"/anime/123/test-tv","watched":12,"ids":{"simkl_id":123}},
             {"title":"Test OVA","anime_type":"ova","url":"/anime/124/test-ova"}]
            """;
        else if (path.EndsWith("/anime/123")) json = """{"title":"SIMKL TV","overview":"Trending synopsis.","total_episodes":12,"genres":["Adventure"]}""";
        else if (path.EndsWith("/untracked-tv")) json = JsonSerializer.Serialize(new AnimeDetails
        {
            Route = "untracked-tv", Title = "Unsaved anime", Episodes = 12, MediaTypes = tv.MediaTypes
        });
        else if (path.EndsWith("/today-tv")) json = JsonSerializer.Serialize(new AnimeDetails
        {
            Route = "today-tv", Title = "Today's anime", Year = 2026, Episodes = 12, MediaTypes = tv.MediaTypes
        });
        else if (path.EndsWith("/test-tv")) json = JsonSerializer.Serialize(tv);
        else json = JsonSerializer.Serialize(new AnimeSearchResponse { Page = 1, TotalAmount = 2,
            Anime = [tv, new AnimeDetails { Route = "ova", MediaTypes = [new AnimeCategory { Route = "ova" }] }] });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
