namespace episodeTracker.Models;

public sealed class WatchlistBackup
{
    public string Format { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string AppVersion { get; set; } = "";
    public DateTimeOffset ExportedAtUtc { get; set; }
    public string? ReleaseSchedule { get; set; }
    public List<WatchlistBackupAnime>? Anime { get; set; }
}

public sealed class WatchlistBackupAnime
{
    // Database IDs are machine-specific and intentionally excluded.
    public string Route { get; set; } = "";
    public string Title { get; set; } = "";
    public string OriginalTitle { get; set; } = "";
    public int Year { get; set; }
    public int TotalEpisodes { get; set; }
    public string Status { get; set; } = "";
    public string Description { get; set; } = "";
    public string? PosterUrl { get; set; }
    public DateTimeOffset AddedAtUtc { get; set; }
    public List<WatchlistBackupEpisode>? WatchedEpisodes { get; set; }
}

public sealed class WatchlistBackupEpisode
{
    public int Number { get; set; }
    public DateTimeOffset WatchedAtUtc { get; set; }
}

public sealed record WatchlistTransferResult(int AnimeCount, int WatchedEpisodeCount);
