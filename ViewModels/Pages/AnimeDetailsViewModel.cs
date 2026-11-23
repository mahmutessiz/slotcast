using episodeTracker.Models;
using episodeTracker.Services;
using episodeTracker.Views.Pages;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace episodeTracker.ViewModels.Pages;

public partial class AnimeDetailsViewModel(AnimeDetailsState state, AnimeScheduleService api, SimklService simkl,
    DashboardViewModel workspace, INavigationService? navigation = null) : ObservableObject, INavigationAware
{
    private AnimeDetails? _anime;
    public DashboardViewModel Workspace => workspace;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _title = "Anime details";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string? _posterUrl;
    [ObservableProperty] private string _description = "No synopsis available.";
    [ObservableProperty] private string _genres = "Not provided";
    [ObservableProperty] private string _studios = "Not provided";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _actionMessage = "";
    [ObservableProperty] private string _sourceLabel = "Source";
    [ObservableProperty] private string _addLabel = "+";
    [ObservableProperty] private string _sourceUrl = "";

    public async Task OnNavigatedToAsync()
    {
        if (state.Card is not { } card || IsBusy) return;
        IsBusy = true;
        ErrorMessage = "";
        ActionMessage = "";
        Title = card.Title;
        Subtitle = card.Subtitle;
        PosterUrl = card.PosterUrl;
        SourceUrl = card.SourceUrl;
        SourceLabel = card.Anime is null ? "Open on SIMKL" : "Open on AnimeSchedule";
        Description = PlainText(card.Anime?.Description ?? card.Overview);
        Genres = "Not provided";
        Studios = "Not provided";
        _anime = card.Anime;
        AddLabel = _anime is null ? "Find on AnimeSchedule" : "+";
        try
        {
            if (card.Anime is { } anime)
            {
                _anime = await api.DetailsAsync(anime.Route);
                Title = _anime.DisplayTitle;
                Subtitle = _anime.Subtitle;
                PosterUrl = _anime.PosterUrl ?? card.PosterUrl;
                Description = PlainText(_anime.Description);
                Genres = JoinNames(_anime.Genres);
                Studios = JoinNames(_anime.Studios);
            }
            else if (card.SimklId is > 0)
            {
                var animeDetails = await simkl.GetAnimeDetailsAsync(card.SimklId.Value);
                if (animeDetails is not null)
                {
                    Title = animeDetails.Title;
                    Subtitle = $"{animeDetails.Year} · TV · {animeDetails.TotalEpisodes?.ToString() ?? "?"} episodes · {animeDetails.Status}";
                    Description = PlainText(animeDetails.Overview);
                    Genres = animeDetails.Genres?.Count > 0 ? string.Join(" · ", animeDetails.Genres) : "Not provided";
                }
            }
        }
        catch (HttpRequestException) { ErrorMessage = "Could not refresh the details. Showing available information."; }
        catch (TaskCanceledException) { ErrorMessage = "The details request timed out. Try opening this title again."; }
        catch (JsonException) { ErrorMessage = "The provider returned an unexpected details response."; }
        catch (InvalidOperationException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task AddAsync()
    {
        if (IsBusy || !workspace.IsIdle) return;
        await workspace.OnNavigatedToAsync();
        if (_anime is not null)
        {
            await workspace.AddAnimeCommand.ExecuteAsync(_anime);
            ActionMessage = workspace.ErrorMessage.Length == 0 ? $"{Title} added to your library." : "";
        }
        else
        {
            workspace.SearchQuery = Title;
            navigation?.Navigate(typeof(DiscoverPage));
            await workspace.SearchCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand] private void Back() => navigation?.GoBack();
    [RelayCommand] private void OpenExternalSource()
    {
        if (!Uri.TryCreate(SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host is not ("simkl.com" or "animeschedule.net")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { ErrorMessage = "Could not open your browser."; }
    }
    private static string PlainText(string? html) => string.IsNullOrWhiteSpace(html) ? "No synopsis available."
        : WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")).Trim();
    private static string JoinNames(List<AnimeCategory>? values) => values?.Count > 0
        ? string.Join(" · ", values.Select(x => x.Name)) : "Not provided";
}
