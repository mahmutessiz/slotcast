using System.Text.Json.Serialization;

namespace episodeTracker.Models
{
    public class SimklTrendingAnime
    {
        public string Title { get; set; } = "";
        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        public int? Year => DateTime.TryParseExact(
            ReleaseDate, "MM/dd/yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date.Year : null;

        public int? Rank { get; set; }
        public int? Watched { get; set; }
        public string? Overview { get; set; }
        public string? Poster { get; set; }
        [JsonPropertyName("anime_type")]
        public string? AnimeType { get; set; }
        public string Url { get; set; } = "";

        public string SimklUrl => new Uri(new Uri("https://simkl.com/"), Url).AbsoluteUri;

        public SimklAnimeIds? Ids { get; set; }
        public SimklAnimeRatings? Ratings { get; set; }
    }

    public class SimklAnimeIds
    {
        [JsonPropertyName("simkl_id")]
        public int SimklId { get; set; }
        public string Slug { get; set; } = "";
    }

    public class SimklAnimeRatings
    {
        public SimklAnimeRating? Simkl { get; set; }
        public SimklAnimeRating? Mal { get; set; }
    }

    public class SimklAnimeRating
    {
        public double? Rating { get; set; }
        public int? Votes { get; set; }
    }

    public sealed class SimklAnimeDetails : SimklShow
    {
        [JsonPropertyName("total_episodes")]
        public int? TotalEpisodes { get; set; }
        public string? Status { get; set; }
        public List<string>? Genres { get; set; }
        public SimklAnimeRatings? Ratings { get; set; }
    }
}

