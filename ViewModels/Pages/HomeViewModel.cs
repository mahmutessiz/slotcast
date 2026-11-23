using SlotCast.Models;
using SlotCast.Services;
using SlotCast.Views.Pages;
using System.Net.Http;
using System.Text.Json;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace SlotCast.ViewModels.Pages;

public partial class HomeViewModel(AnimeScheduleService api, SimklService simkl,
    DashboardViewModel workspace, INavigationService? navigation = null,
    AnimeDetailsNavigationService? detailsNavigation = null) : ObservableObject, INavigationAware
{
    private DateTime _loadedUtc = DateTime.MinValue;
    private DateTime _loadedDate;
    private string _loadedAirType = "";
    private AnimeSeason _season = AnimeSeason.ForDate(DateTime.Today);
    private int _seasonPage;
    private int _seasonTotal;
    public DashboardViewModel Workspace => workspace;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isBusy;
    public bool IsIdle => !IsBusy;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _actionMessage = "";
    [ObservableProperty] private string _feedStatus = "Loading anime…";
    [ObservableProperty] private List<HomeAnimeCard> _newReleases = [];
    [ObservableProperty] private List<HomeAnimeCard> _trending = [];
    [ObservableProperty] private List<HomeAnimeCard> _upcoming = [];
    [ObservableProperty] private List<HomeAnimeCard> _airingToday = [];
    [ObservableProperty] private string _airingTodayStatus = "Today's episode schedule";
    [ObservableProperty] private string _currentSeasonLabel = AnimeSeason.ForDate(DateTime.Today).Label;
    [ObservableProperty] private List<HomeAnimeCard> _seasonAnime = [];
    [ObservableProperty] private bool _hasMoreSeasonAnime;
    [ObservableProperty] private string _seasonStatus = "TV anime premiering this season";

    public async Task OnNavigatedToAsync()
    {
        await workspace.OnNavigatedToAsync();
        if (DateTime.UtcNow - _loadedUtc > TimeSpan.FromHours(1) || _loadedDate != DateTime.Today
            || _loadedAirType != workspace.SelectedSchedule.Value) await RefreshHomeAsync();
    }
    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task RefreshHomeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = "";
        var failed = new List<string>();
        try
        {
            try
            {
                var schedule = await api.GetTimetableAsync(DateTime.Today, DateTime.Today.AddDays(1), workspace.SelectedSchedule.Value, false);
                AiringToday = schedule.Entries.Where(x => x.EpisodeDate is { Year: > 1900 }
                        && x.EpisodeDate.Value.ToLocalTime().Date == DateTime.Today && x.EpisodeNumber > 0)
                    .GroupBy(x => new { x.Route, x.EpisodeNumber }).Select(x => x.Last())
                    .OrderBy(x => x.EpisodeDate).Select(HomeAnimeCard.FromTimetable).ToList();
                AiringTodayStatus = schedule.IsStale || schedule.HasMissingWeeks ? "Saved schedule · some updates are unavailable"
                    : AiringToday.Count == 0 ? "No TV anime scheduled for today" : $"{AiringToday.Count} airings · Times shown in your device's timezone";
                if (schedule.IsStale || schedule.HasMissingWeeks) failed.Add("today's schedule");
            }
            catch (Exception ex) when (IsFeedError(ex)) { failed.Add("today's schedule"); AiringTodayStatus = "Today's schedule could not be loaded"; }
            try
            {
                var season = AnimeSeason.ForDate(DateTime.Today);
                var result = await api.GetSeasonAsync(season);
                _season = season;
                CurrentSeasonLabel = season.Label;
                _seasonPage = 1;
                _seasonTotal = result.TotalAmount;
                SeasonAnime = result.Anime.Select(HomeAnimeCard.FromAnime).ToList();
                HasMoreSeasonAnime = _seasonPage * 18 < _seasonTotal;
                SeasonStatus = _seasonTotal == 0 ? "No TV anime found for this season" : $"{_seasonTotal} TV anime · Sorted by popularity";
            }
            catch (Exception ex) when (IsFeedError(ex)) { failed.Add("this season's anime"); }
            try { NewReleases = (await api.GetReleasesAsync(false)).Select(HomeAnimeCard.FromAnime).ToList(); }
            catch (Exception ex) when (IsFeedError(ex)) { failed.Add("new releases"); }
            try
            {
                Trending = (await simkl.GetTrendingAnimesAsync()).Where(x => x.AnimeType == "tv").Take(12)
                    .Select(x => new HomeAnimeCard
                    {
                        Title = x.Title,
                        Subtitle = $"{x.Year} · {x.Watched:N0} watched today",
                        // WPF's built-in image decoder does not support WebP on every device.
                        PosterUrl = string.IsNullOrWhiteSpace(x.Poster) ? null
                            : $"https://wsrv.nl/?url={Uri.EscapeDataString("https://simkl.in/posters/" + x.Poster + "_m.webp")}&output=jpg&q=90",
                        SourceUrl = x.SimklUrl, SimklId = x.Ids?.SimklId, Overview = x.Overview
                    }).ToList();
            }
            catch (Exception ex) when (IsFeedError(ex)) { failed.Add("trending anime"); }
            try { Upcoming = (await api.GetReleasesAsync(true)).Select(HomeAnimeCard.FromAnime).ToList(); }
            catch (Exception ex) when (IsFeedError(ex)) { failed.Add("upcoming premieres"); }
            _loadedUtc = failed.Count == 0 ? DateTime.UtcNow : DateTime.MinValue;
            _loadedDate = DateTime.Today;
            _loadedAirType = workspace.SelectedSchedule.Value;
            FeedStatus = NewReleases.Count + Trending.Count + Upcoming.Count + AiringToday.Count + SeasonAnime.Count == 0
                ? "No anime loaded. Check your connection and application configuration, then refresh."
                : "TV anime only · Choose a title to add it to your library";
            if (failed.Count > 0) ErrorMessage = $"Could not refresh {string.Join(", ", failed)}. Try again when connected.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoadMoreSeasonAsync()
    {
        if (IsBusy || !HasMoreSeasonAnime) return;
        IsBusy = true;
        ErrorMessage = "";
        try
        {
            var nextPage = _seasonPage + 1;
            var result = await api.GetSeasonAsync(_season, nextPage);
            SeasonAnime = SeasonAnime.Concat(result.Anime.Select(HomeAnimeCard.FromAnime))
                .DistinctBy(x => x.Anime?.Route).ToList();
            _seasonPage = nextPage;
            _seasonTotal = result.TotalAmount;
            HasMoreSeasonAnime = _seasonPage * 18 < _seasonTotal;
        }
        catch (Exception ex) when (IsFeedError(ex)) { ErrorMessage = "Could not load more seasonal anime. Please try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task AddFromHomeAsync(HomeAnimeCard? card)
    {
        if (card is null) return;
        await workspace.OnNavigatedToAsync();
        if (card.Anime is not null)
        {
            try
            {
                IsBusy = true;
                // Timetable cards have lightweight metadata. Fetch the complete title before saving it.
                var anime = card.AirsAt is null ? card.Anime : await api.DetailsAsync(card.Anime.Route);
                await workspace.AddAnimeCommand.ExecuteAsync(anime);
                ActionMessage = workspace.ErrorMessage.Length == 0 ? $"{card.Title} added to your library." : "";
            }
            catch (Exception ex) when (IsFeedError(ex)) { ErrorMessage = "Could not add this anime. Please try again when connected."; }
            finally { IsBusy = false; }
        }
        else
        {
            // Do not guess a cross-provider ID or season from a fuzzy title match.
            workspace.SearchQuery = card.Title;
            navigation?.Navigate(typeof(DiscoverPage));
            await workspace.SearchCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void OpenCardDetails(HomeAnimeCard? card)
    {
        if (card is not null) detailsNavigation?.Open(card);
    }

    private static bool IsFeedError(Exception ex) => ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException;
}
