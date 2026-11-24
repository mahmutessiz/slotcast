namespace SlotCast.Models;

public sealed class AnimeSearchResponse
{
    public int Page { get; set; }
    public int TotalAmount { get; set; }
    public List<AnimeDetails> Anime { get; set; } = [];
}

public sealed class AnimeDetails
{
    public string Route { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public int Episodes { get; set; }
    public string? Status { get; set; }
    public string? Description { get; set; }
    public string? ImageVersionRoute { get; set; }
    public AnimeWebsites? Websites { get; set; }
    public AnimeNames? Names { get; set; }
    public List<AnimeCategory>? MediaTypes { get; set; }
    public List<AnimeCategory>? Genres { get; set; }
    public List<AnimeCategory>? Studios { get; set; }
    public int LengthMin { get; set; }
    public bool IsTv => MediaTypes?.Any(x => x.Route == "tv") == true;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Names?.English) ? Title : Names.English;
    public string Subtitle => $"{Year} · TV · {(Episodes > 0 ? $"{Episodes} episodes" : "Episodes TBA")} · {Status}";
    public string? PosterUrl => string.IsNullOrWhiteSpace(ImageVersionRoute) ? null
        : "https://img.animeschedule.net/production/assets/public/img/" + ImageVersionRoute;
}

public sealed class AnimeNames
{
    public string? English { get; set; }
}

public sealed class AnimeCategory
{
    public string Route { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class AnimeTimetableEntry
{
    public string Route { get; set; } = "";
    public string Title { get; set; } = "";
    public string? English { get; set; }
    public string? ImageVersionRoute { get; set; }
    public string? AirType { get; set; }
    public string? AiringStatus { get; set; }
    public string? DelayedText { get; set; }
    public DateTimeOffset? EpisodeDate { get; set; }
    public int EpisodeNumber { get; set; }
    public int Episodes { get; set; }
    public string? Status { get; set; }
    public int SubtractedEpisodeNumber { get; set; }
    public List<AnimeCategory>? MediaTypes { get; set; }
    public bool IsTv => MediaTypes?.Any(x => x.Route == "tv") == true;
}

public sealed record ScheduleChoice(string Label, string Value)
{
    public override string ToString() => Label;
}

public sealed record CalendarScopeChoice(string Label, bool ShowAll);

public sealed record TimetableResult(List<AnimeTimetableEntry> Entries, bool IsStale, bool HasMissingWeeks, DateTime? OldestFetchUtc);

public sealed class AnimeWebsites
{
    public string? Mal { get; set; }
}

public sealed record LibraryEpisodeInfo(int Number, string? Title, DateTimeOffset? Aired);
public sealed record LibraryEpisodeResult(List<LibraryEpisodeInfo> Episodes, bool IsUnavailable);
