namespace episodeTracker.Models;

public sealed record AnimeSeason(string Slug, int Year)
{
    public string Label => $"{char.ToUpperInvariant(Slug[0])}{Slug[1..]} {Year}";

    public static AnimeSeason ForDate(DateTime date) => new(date.Month switch
    {
        <= 3 => "winter",
        <= 6 => "spring",
        <= 9 => "summer",
        _ => "fall"
    }, date.Year);
}
