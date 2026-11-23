using System.ComponentModel.DataAnnotations.Schema;

namespace episodeTracker.Models;

public sealed class LibraryAnime
{
    public int Id { get; set; }
    public string Route { get; set; } = "";
    public string Title { get; set; } = "";
    public string OriginalTitle { get; set; } = "";
    public int Year { get; set; }
    public int TotalEpisodes { get; set; }
    public string Status { get; set; } = "";
    public string Description { get; set; } = "";
    public string? PosterUrl { get; set; }
    public DateTime AddedUtc { get; set; }
    public List<WatchedEpisode> WatchedEpisodes { get; set; } = [];
    [NotMapped] public string ProgressLabel => $"{WatchedEpisodes.Count} / {(TotalEpisodes > 0 ? TotalEpisodes.ToString() : "?")} watched";
    [NotMapped] public string Subtitle => $"{Year} · TV · {Status}";
}

public sealed class WatchedEpisode
{
    public int Id { get; set; }
    public int LibraryAnimeId { get; set; }
    public int EpisodeNumber { get; set; }
    public DateTime WatchedUtc { get; set; }
}

public sealed class TimetableCache
{
    public string Key { get; set; } = "";
    public string Json { get; set; } = "[]";
    public DateTime FetchedUtc { get; set; }
}

public sealed class LocalPreference
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class CalendarEpisode
{
    public int AnimeId { get; init; }
    public string Route { get; init; } = "";
    public string Title { get; init; } = "";
    public string? BannerUrl { get; init; }
    public bool HasBanner => !string.IsNullOrWhiteSpace(BannerUrl);
    public bool IsTracked => AnimeId > 0;
    public int Number { get; init; }
    public DateTimeOffset AirsAt { get; init; }
    public bool IsWatched { get; init; }
    public bool IsDelayed { get; init; }
    public string? DelayNote { get; init; }
    public string AirType { get; init; } = "";
    public string TimeLabel => AirsAt.ToLocalTime().ToString("HH:mm");
    public string EpisodeLabel => $"Episode {Number}";
    public string AiringSeasonLabel => $"Airing: {AnimeSeason.ForDate(AirsAt.ToLocalTime().DateTime).Label}";
    public string WatchIcon => !IsTracked ? "+" : IsWatched ? "✓" : "○";
    public string StateLabel => IsWatched ? "Watched" : IsDelayed ? (DelayNote ?? "Delayed — time may change")
        : AirsAt > DateTimeOffset.UtcNow ? "Upcoming" : "Unwatched";
    public string SourceLabel => AirType switch { "raw" => "Japanese broadcast", "dub" => "English dub", _ => "English subtitles" };
    public string ActionLabel => !IsTracked ? "+" : IsWatched ? "Undo watched" : "Mark watched";
    public bool CanMark => IsTracked && (IsWatched || (!IsDelayed && AirsAt <= DateTimeOffset.UtcNow));
    public bool CanAct => !IsTracked || CanMark;
}

public sealed class CalendarDay
{
    public DateTime Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsSelected { get; init; }
    public bool IsToday => Date.Date == DateTime.Today;
    public int Number => Date.Day;
    public List<CalendarEpisode> Episodes { get; init; } = [];
    public string Summary => Episodes.Count == 0 ? "" : $"{Episodes.Count} airing{(Episodes.Count == 1 ? "" : "s")}";
    public string PreviewTitle => Episodes.FirstOrDefault()?.Title ?? "";
}

public sealed class EpisodeRow
{
    public int AnimeId { get; init; }
    public int Number { get; init; }
    public bool IsWatched { get; init; }
    public string ScheduleLabel { get; init; } = "Schedule unavailable";
    public bool CanMark { get; init; } = true;
    public string Label => $"Episode {Number}";
    public string ActionLabel => IsWatched ? "Undo watched" : "Mark watched";
}
