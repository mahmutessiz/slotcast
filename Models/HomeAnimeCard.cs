namespace SlotCast.Models;

public sealed partial class HomeAnimeCard : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string? PosterUrl { get; init; }
    public string SourceUrl { get; init; } = "";
    public int? SimklId { get; init; }
    public string? Overview { get; init; }
    public AnimeDetails? Anime { get; set; }
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty, CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(ActionLabel))]
    private bool _isInWatchlist;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private List<AnimeDetails> _addCandidates = [];
    public DateTimeOffset? AirsAt { get; init; }
    public string OverviewText => string.IsNullOrWhiteSpace(Overview) ? ""
        : System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(Overview, "<[^>]+>", " ")).Trim();
    public string ActionLabel => IsInWatchlist ? "In watchlist" : "Add to watchlist";
    public static HomeAnimeCard FromAnime(AnimeDetails anime) => new()
    {
        Title = anime.DisplayTitle, Subtitle = anime.Subtitle, PosterUrl = anime.PosterUrl,
        SourceUrl = "https://animeschedule.net/anime/" + Uri.EscapeDataString(anime.Route), Anime = anime, Overview = anime.Description
    };
    public static HomeAnimeCard FromTimetable(AnimeTimetableEntry entry)
    {
        var anime = new AnimeDetails { Route = entry.Route, Title = entry.Title,
            Names = new AnimeNames { English = entry.English }, Episodes = entry.Episodes, Status = entry.Status,
            ImageVersionRoute = entry.ImageVersionRoute, MediaTypes = entry.MediaTypes };
        var source = entry.AirType switch { "raw" => "Japanese broadcast", "dub" => "English dub", _ => "English subtitles" };
        var episode = entry.SubtractedEpisodeNumber > 0 && entry.SubtractedEpisodeNumber < entry.EpisodeNumber
            ? $"Episodes {entry.SubtractedEpisodeNumber}–{entry.EpisodeNumber}" : $"Episode {entry.EpisodeNumber}";
        return new HomeAnimeCard { Title = anime.DisplayTitle, PosterUrl = anime.PosterUrl, Anime = anime,
            AirsAt = entry.EpisodeDate, Subtitle = $"{entry.EpisodeDate?.ToLocalTime():HH:mm} · {episode} · {source}",
            Overview = entry.DelayedText, SourceUrl = "https://animeschedule.net/anime/" + Uri.EscapeDataString(entry.Route) };
    }
}
