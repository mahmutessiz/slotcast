using episodeTracker.Models;
using episodeTracker.Services;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui;
using episodeTracker.Views.Pages;

namespace episodeTracker.ViewModels.Pages;

public partial class DashboardViewModel(AnimeScheduleService api, AnimeLibraryService library, INavigationService? navigation = null,
    AnimeDetailsNavigationService? detailsNavigation = null) : ObservableObject, INavigationAware
{
    private bool _initialized;
    private bool _refreshAfterImport;
    private List<AnimeTimetableEntry> _timetable = [];
    private List<CalendarEpisode> _calendarEpisodes = [];
    private string _lastQuery = "";
    private int _totalResults;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isBusy;
    public bool IsIdle => !IsBusy;
    [ObservableProperty] private string _busyMessage = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _notice = "Add a TV anime to start your personal calendar.";
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _searchSummary = "Search by English or Japanese title. TV series only.";
    [ObservableProperty] private List<AnimeDetails> _searchResults = [];
    [ObservableProperty] private int _searchPage = 1;
    [ObservableProperty] private List<LibraryAnime> _libraryItems = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(LibrarySectionTitle))] private bool _showArchive;
    public string LibrarySectionTitle => ShowArchive ? "Archive — completed series" : "Watching";
    [ObservableProperty] private List<LibraryAnime> _visibleLibraryItems = [];
    partial void OnShowArchiveChanged(bool value) => FilterLibrary();
    private void FilterLibrary()
    {
        var id = SelectedAnime?.Id;
        VisibleLibraryItems = LibraryItems.Where(x => x.IsCompleted == ShowArchive).ToList();
        SelectedAnime = VisibleLibraryItems.FirstOrDefault(x => x.Id == id) ?? VisibleLibraryItems.FirstOrDefault();
    }
    [RelayCommand] private void ShowActiveLibrary() => ShowArchive = false;
    [RelayCommand] private void ShowCompletedLibrary() => ShowArchive = true;
    [RelayCommand] private async Task MarkCompletedAsync(LibraryAnime? anime)
    {
        if (anime is null) return;
        await RunAsync(async () =>
        {
            await library.SetCompletedAsync(anime.Id, true);
            await ReloadLibraryAsync();
            RebuildCalendar();
        }, "Archiving completed anime…");
    }
    [ObservableProperty] private LibraryAnime? _selectedAnime;
    [ObservableProperty] private List<EpisodeRow> _episodeRows = [];
    [ObservableProperty] private string _episodeSummary = "Select an anime in your library.";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(WeekLabel))]
    private DateTime _displayWeek = DateTime.Today.AddDays(-3);
    public string WeekLabel => $"{DisplayWeek:d MMM yyyy} – {DisplayWeek.AddDays(6):d MMM yyyy}";
    [ObservableProperty] private int _todayFocusVersion;
    [ObservableProperty] private bool _showFullSchedule;
    public List<CalendarScopeChoice> CalendarScopes { get; } = [new("My library", false), new("Full TV schedule", true)];
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _selectedDateLabel = DateTime.Today.ToString("dddd, d MMMM");
    [ObservableProperty] private List<CalendarDay> _calendarDays = [];
    [ObservableProperty] private List<CalendarEpisode> _selectedDayEpisodes = [];
    [ObservableProperty] private string _daySummary = "No episodes scheduled for this day.";
    [ObservableProperty] private string _librarySummary = "Your library is empty.";
    [ObservableProperty] private string _syncStatus = "Schedules have not been downloaded yet.";
    public List<ScheduleChoice> ScheduleChoices { get; } =
        [new("English subtitles", "sub"), new("Japanese broadcast", "raw"), new("English dub", "dub")];
    [ObservableProperty] private ScheduleChoice _selectedSchedule = new("English subtitles", "sub");

    partial void OnSelectedAnimeChanged(LibraryAnime? value) => BuildEpisodeRows();
    partial void OnSelectedDateChanged(DateTime value) => BuildDays();
    partial void OnShowFullScheduleChanged(bool value)
    {
        if (_initialized) ChangeCalendarScopeCommand.Execute(null);
    }

    public async Task OnNavigatedToAsync()
    {
        if (_initialized)
        {
            if (_refreshAfterImport)
            {
                _refreshAfterImport = false;
                await RunAsync(() => LoadCalendarAsync(false), "Loading imported calendar…");
            }
            return;
        }
        await RunAsync(async () =>
        {
            await library.InitializeAsync();
            var preference = await library.GetSchedulePreferenceAsync();
            SelectedSchedule = ScheduleChoices.FirstOrDefault(x => x.Value == preference) ?? ScheduleChoices[0];
            await ReloadLibraryAsync();
            _initialized = true;
            await LoadCalendarAsync(false);
        }, "Opening your library…");
    }
    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    public async Task ReloadAfterImportAsync() => await RunAsync(async () =>
    {
        await library.InitializeAsync();
        var preference = await library.GetSchedulePreferenceAsync();
        if (SelectedSchedule.Value != preference) _timetable = [];
        SelectedSchedule = ScheduleChoices.FirstOrDefault(x => x.Value == preference) ?? ScheduleChoices[0];
        await ReloadLibraryAsync();
        RebuildCalendar();
        _initialized = true;
        _refreshAfterImport = true;
        SyncStatus = "Imported library loaded. Airing schedules will refresh when you open the calendar.";
    }, "Loading imported library…");

    [RelayCommand]
    private async Task SearchAsync() => await RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            SearchResults = [];
            SearchSummary = "Enter an anime title to search.";
            return;
        }
        _lastQuery = SearchQuery.Trim();
        SearchPage = 1;
        await FetchSearchAsync();
    }, "Searching TV anime…");

    private async Task FetchSearchAsync()
    {
        var result = await api.SearchAsync(_lastQuery, SearchPage);
        _totalResults = result.TotalAmount;
        SearchResults = result.Anime;
        SearchSummary = _totalResults == 0 ? "No TV anime found. Try another title."
            : $"{_totalResults} matches · Page {SearchPage} of {Math.Max(1, (int)Math.Ceiling(_totalResults / 18d))}";
    }

    [RelayCommand]
    private async Task NextSearchAsync() => await RunAsync(async () =>
    {
        if (SearchPage * 18 >= _totalResults || _lastQuery.Length == 0) return;
        SearchPage++;
        try { await FetchSearchAsync(); } catch { SearchPage--; throw; }
    }, "Loading more anime…");

    [RelayCommand]
    private async Task PreviousSearchAsync() => await RunAsync(async () =>
    {
        if (SearchPage <= 1 || _lastQuery.Length == 0) return;
        SearchPage--;
        try { await FetchSearchAsync(); } catch { SearchPage++; throw; }
    }, "Loading anime…");

    [RelayCommand]
    private async Task AddAnimeAsync(AnimeDetails? anime)
    {
        if (anime is null) return;
        await RunAsync(async () =>
        {
            await library.AddAsync(anime);
            await ReloadLibraryAsync();
            ShowArchive = LibraryItems.First(x => x.Route == anime.Route).IsCompleted;
            SelectedAnime = LibraryItems.First(x => x.Route == anime.Route);
            Notice = $"{anime.DisplayTitle} is in your library.";
            await LoadCalendarAsync(false);
        }, "Adding to your library…");
    }

    [RelayCommand]
    private async Task RemoveAnimeAsync(LibraryAnime? anime)
    {
        if (anime is null) return;
        if (MessageBox.Show($"Remove {anime.Title} and its watched history from this device?", "Remove anime",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunAsync(async () =>
        {
            await library.RemoveAsync(anime.Id);
            await ReloadLibraryAsync();
            RebuildCalendar();
            Notice = "Anime removed from your library.";
        }, "Updating your library…");
    }

    [RelayCommand]
    private async Task RefreshAsync() => await RunAsync(async () =>
    {
        foreach (var anime in LibraryItems.ToList())
            await library.AddAsync(await api.DetailsAsync(anime.Route));
        await ReloadLibraryAsync();
        await LoadCalendarAsync(true);
    }, "Refreshing anime and episode schedules…");

    [RelayCommand]
    private async Task ChangeScheduleAsync() => await RunAsync(async () =>
    {
        await library.SetSchedulePreferenceAsync(SelectedSchedule.Value);
        await LoadCalendarAsync(false);
    }, "Loading the selected schedule…");

    [RelayCommand]
    private async Task ChangeCalendarScopeAsync() => await RunAsync(async () =>
    {
        await LoadCalendarAsync(false);
    }, "Loading calendar…");

    [RelayCommand] private async Task PreviousWeekAsync() => await MoveWeekAsync(-1);
    [RelayCommand] private async Task NextWeekAsync() => await MoveWeekAsync(1);
    private async Task MoveWeekAsync(int direction) => await RunAsync(async () =>
    {
        DisplayWeek = DisplayWeek.AddDays(direction * 7);
        SelectedDate = SelectedDate.AddDays(direction * 7);
        _timetable = [];
        RebuildCalendar();
        await LoadCalendarAsync(false);
    }, "Loading week…");

    [RelayCommand]
    private async Task TodayAsync() => await RunAsync(async () =>
    {
        DisplayWeek = DateTime.Today.AddDays(-3);
        SelectedDate = DateTime.Today;
        _timetable = [];
        RebuildCalendar();
        await LoadCalendarAsync(false);
        TodayFocusVersion++;
    }, "Loading this week…");

    [RelayCommand] private void SelectDay(CalendarDay? day) { if (day is not null) SelectedDate = day.Date; }
    [RelayCommand] private void Discover() => navigation?.Navigate(typeof(DiscoverPage));
    [RelayCommand] private void OpenAnimeDetails(AnimeDetails? anime)
    {
        if (anime is not null) detailsNavigation?.Open(HomeAnimeCard.FromAnime(anime));
    }
    [RelayCommand] private void OpenLibraryDetails(LibraryAnime? anime)
    {
        if (anime is null) return;
        detailsNavigation?.Open(new HomeAnimeCard
        {
            Title = anime.Title, PosterUrl = anime.PosterUrl, Subtitle = anime.Subtitle,
            SourceUrl = "https://animeschedule.net/anime/" + Uri.EscapeDataString(anime.Route),
            Anime = new AnimeDetails { Route = anime.Route, Title = anime.Title, Description = anime.Description,
                Year = anime.Year, Episodes = anime.TotalEpisodes, Status = anime.Status,
                MediaTypes = [new AnimeCategory { Route = "tv" }] }
        });
    }
    [RelayCommand] private async Task ToggleCalendarEpisodeAsync(CalendarEpisode? episode)
    {
        if (episode is null) return;
        if (!episode.IsTracked)
        {
            await RunAsync(async () =>
            {
                await library.AddAsync(await api.DetailsAsync(episode.Route));
                await ReloadLibraryAsync();
                RebuildCalendar();
                Notice = $"{episode.Title} added to your library.";
            }, "Adding anime…");
            return;
        }
        if (!episode.CanMark) return;
        await ToggleAsync(episode.AnimeId, episode.Number);
    }
    [RelayCommand] private async Task ToggleEpisodeAsync(EpisodeRow? episode)
    {
        if (episode is null || !episode.CanMark) return;
        await ToggleAsync(episode.AnimeId, episode.Number);
    }
    private async Task ToggleAsync(int animeId, int number) => await RunAsync(async () =>
    {
        await library.ToggleWatchedAsync(animeId, number);
        await ReloadLibraryAsync();
        RebuildCalendar();
    }, "Saving episode progress…");

    private async Task ReloadLibraryAsync()
    {
        LibraryItems = await library.GetLibraryAsync();
        FilterLibrary();
        LibrarySummary = $"{LibraryItems.Count} TV anime · {LibraryItems.Sum(x => x.WatchedEpisodes.Count)} episodes watched";
    }
    private DateTime GridStart => DisplayWeek;

    private async Task LoadCalendarAsync(bool refresh)
    {
        if (LibraryItems.Count == 0 && !ShowFullSchedule)
        {
            _timetable = [];
            SyncStatus = "Add an anime from Discover to populate your calendar.";
            RebuildCalendar();
            return;
        }
        var result = await api.GetTimetableAsync(GridStart, GridStart.AddDays(7), SelectedSchedule.Value, refresh);
        _timetable = result.Entries;
        SyncStatus = result.HasMissingWeeks ? "Some weeks could not be loaded. The calendar is incomplete; try Refresh when connected."
            : result.IsStale ? "Offline or provider unavailable — showing saved schedules. Dates may have changed."
            : $"Schedule snapshots updated {result.OldestFetchUtc?.ToLocalTime():g} · {TimeZoneInfo.Local.DisplayName}";
        RebuildCalendar();
    }

    private void RebuildCalendar()
    {
        var tracked = LibraryItems.ToDictionary(x => x.Route);
        _calendarEpisodes = _timetable.Where(x => (ShowFullSchedule || tracked.ContainsKey(x.Route))
                && x.EpisodeDate is { Year: > 1900 } && x.EpisodeNumber > 0)
            .SelectMany(x =>
            {
                var anime = tracked.GetValueOrDefault(x.Route);
                var first = x.SubtractedEpisodeNumber > 0 && x.SubtractedEpisodeNumber <= x.EpisodeNumber
                    ? x.SubtractedEpisodeNumber : x.EpisodeNumber;
                return Enumerable.Range(first, Math.Min(x.EpisodeNumber - first + 1, 1000)).Select(number => new CalendarEpisode
                {
                    AnimeId = anime?.Id ?? 0, Route = x.Route,
                    Title = anime?.Title ?? (string.IsNullOrWhiteSpace(x.English) ? x.Title : x.English), Number = number,
                    BannerUrl = anime?.PosterUrl ?? (string.IsNullOrWhiteSpace(x.ImageVersionRoute) ? null
                        : "https://img.animeschedule.net/production/assets/public/img/" + x.ImageVersionRoute),
                    AirsAt = x.EpisodeDate!.Value, AirType = x.AirType ?? SelectedSchedule.Value,
                    IsDelayed = x.AiringStatus == "delayed-air" || !string.IsNullOrWhiteSpace(x.DelayedText),
                    DelayNote = x.DelayedText,
                    IsWatched = anime?.WatchedEpisodes.Any(w => w.EpisodeNumber == number) == true
                });
            }).GroupBy(x => new { x.Route, x.Number }).Select(x => x.Last()).OrderBy(x => x.AirsAt).ToList();
        BuildDays();
        BuildEpisodeRows();
    }

    private void BuildDays()
    {
        var byDay = _calendarEpisodes.GroupBy(x => x.AirsAt.ToLocalTime().Date).ToDictionary(x => x.Key, x => x.ToList());
        CalendarDays = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = GridStart.AddDays(offset);
            return new CalendarDay { Date = date, IsCurrentMonth = true,
                IsSelected = date == SelectedDate.Date, Episodes = byDay.GetValueOrDefault(date) ?? [] };
        }).ToList();
        SelectedDateLabel = SelectedDate.ToString("dddd, d MMMM yyyy");
        SelectedDayEpisodes = byDay.GetValueOrDefault(SelectedDate.Date) ?? [];
        DaySummary = SelectedDayEpisodes.Count == 0 ? (ShowFullSchedule ? "No TV anime scheduled for this day." : "No episodes scheduled for your library on this day.")
            : $"{SelectedDayEpisodes.Count} episodes · Times shown in your device's timezone";
    }

    private void BuildEpisodeRows()
    {
        if (SelectedAnime is not { } anime)
        {
            EpisodeRows = [];
            EpisodeSummary = "Select an anime in your library.";
            return;
        }
        var dates = _calendarEpisodes.Where(x => x.AnimeId == anime.Id).ToDictionary(x => x.Number);
        var max = Math.Max(anime.TotalEpisodes, Math.Max(dates.Keys.DefaultIfEmpty(0).Max(),
            anime.WatchedEpisodes.Select(x => x.EpisodeNumber).DefaultIfEmpty(0).Max()));
        var watched = anime.WatchedEpisodes.Select(x => x.EpisodeNumber).ToHashSet();
        if (anime.TotalEpisodes == 0) max = Math.Max(max, watched.DefaultIfEmpty(0).Max() + 1);
        EpisodeRows = Enumerable.Range(1, Math.Min(max, 5000)).Select(number =>
        {
            dates.TryGetValue(number, out var date);
            return new EpisodeRow { AnimeId = anime.Id, Number = number, IsWatched = watched.Contains(number),
                CanMark = watched.Contains(number) || date is null || date.CanMark,
                ScheduleLabel = date is null ? "No date loaded · manually track progress" : $"{date.AirsAt.ToLocalTime():g} · {date.SourceLabel} · {date.StateLabel}" };
        }).ToList();
        EpisodeSummary = $"{anime.ProgressLabel} · Dates use downloaded weekly schedules. Unknown dates are not predicted.";
    }

    [RelayCommand]
    private void OpenSource(string? route)
    {
        try
        {
            var url = "https://animeschedule.net/" + (string.IsNullOrWhiteSpace(route) ? "" : "anime/" + Uri.EscapeDataString(route));
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception) { ErrorMessage = "Could not open your browser."; }
    }

    private async Task RunAsync(Func<Task> work, string message)
    {
        if (IsBusy) return;
        IsBusy = true;
        BusyMessage = message;
        ErrorMessage = "";
        try { await work(); }
        catch (HttpRequestException ex) { ErrorMessage = ex.StatusCode is not null ? ex.Message : "Could not connect to AnimeSchedule. Your saved library is still available."; }
        catch (TaskCanceledException) { ErrorMessage = "The request timed out. Try again when connected."; }
        catch (JsonException) { ErrorMessage = "The provider returned an unexpected response. Your saved progress has been kept."; }
        catch (InvalidOperationException ex) { ErrorMessage = ex.Message; }
        catch (DbUpdateException) { ErrorMessage = "Could not save the change to SQLite. Please check available disk space."; }
        catch (Microsoft.Data.Sqlite.SqliteException) { ErrorMessage = "Could not open the local database. Check folder permissions and disk space."; }
        finally { IsBusy = false; BusyMessage = ""; }
    }
}
