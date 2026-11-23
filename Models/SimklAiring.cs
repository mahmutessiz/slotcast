using System.Text.Json.Serialization;

namespace SlotCast.Models
{
    public class SimklAiring
    {
        public string Title { get; set; } = "";
        public int? Year { get; set; }
        public DateTimeOffset? Date { get; set; }
        public string? Poster { get; set; }
        public int? Rank { get; set; }
        public string Url { get; set; } = "";

        public SimklAiringIds Ids { get; set; } = new();
        public SimklAiringEpisode Episode { get; set; } = new();

    }

    public class SimklAiringIds
    {
        [JsonPropertyName("simkl_id")]
        public int SimklId { get; set; }

        public string Slug { get; set; } = "";
    }

    public class SimklAiringEpisode
    {
        public int? Season { get; set; }
        public int Episode { get; set; }
        public string? Title { get; set; }
        public int? Runtime { get; set; }
        public string Url { get; set; } = "";
    }
}