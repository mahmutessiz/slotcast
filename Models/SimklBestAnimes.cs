using System.Text.Json.Serialization;

namespace episodeTracker.Models
{
    public class SimklBestAnimes
    {
        public string Title { get; set; } = "";
        public int? Year { get; set; }
        public string? Poster { get; set; }
        public string Url { get; set; } = "";

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
}

/*
 [
  {
    "title": "Sousou no Frieren",
    "year": 2023,
    "poster": "14/1430387937a6ef6167",
    "url": "/anime/1990194/sousou-no-frieren",
    "ids": {
      "simkl_id": 1990194,
      "slug": "sousou-no-frieren"
    },
    "ratings": {
      "simkl": {
        "rating": 9.2,
        "votes": 4636
      },
      "mal": {
        "rating": 9.3,
        "votes": 886825
      }
    }
  }
]
 */
