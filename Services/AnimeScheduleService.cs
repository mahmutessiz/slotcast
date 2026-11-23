using episodeTracker.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace episodeTracker.Services;

public sealed class AnimeScheduleService(IHttpClientFactory clients, IDbContextFactory<SlotCastDbContext> factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AnimeSearchResponse> GetSeasonAsync(AnimeSeason season, int page = 1, CancellationToken ct = default)
    {
        if (season.Slug is not ("winter" or "spring" or "summer" or "fall") || page < 1)
            throw new ArgumentException("Invalid anime season or page.");
        using var client = clients.CreateClient("AnimeSchedule");
        using var response = await client.GetAsync(
            $"anime?media-types=tv&mt=all&seasons={season.Slug}&years={season.Year}&st=popularity&page={page}", ct);
        CheckResponse(response);
        var result = await response.Content.ReadFromJsonAsync<AnimeSearchResponse>(JsonOptions, ct)
            ?? throw new JsonException("Empty season response.");
        result.Anime = (result.Anime ?? []).Where(x => x.IsTv).ToList();
        return result;
    }

    public async Task<List<AnimeDetails>> GetReleasesAsync(bool upcoming, CancellationToken ct = default)
    {
        using var client = clients.CreateClient("AnimeSchedule");
        var filter = upcoming ? "upcoming" : "ongoing";
        var direction = upcoming ? "" : "&reverse=true";
        using var response = await client.GetAsync($"anime?media-types=tv&mt=all&airing-statuses={filter}&st=releaseDate{direction}&page=1", ct);
        CheckResponse(response);
        var result = await response.Content.ReadFromJsonAsync<AnimeSearchResponse>(JsonOptions, ct)
            ?? throw new JsonException("Empty releases response.");
        return (result.Anime ?? []).Where(x => x.IsTv).Take(12).ToList();
    }

    public async Task<AnimeSearchResponse> SearchAsync(string query, int page, CancellationToken ct = default)
    {
        using var client = clients.CreateClient("AnimeSchedule");
        using var response = await client.GetAsync($"anime?q={Uri.EscapeDataString(query.Trim())}&media-types=tv&mt=all&page={page}", ct);
        CheckResponse(response);
        var result = await response.Content.ReadFromJsonAsync<AnimeSearchResponse>(JsonOptions, ct)
            ?? throw new JsonException("Empty search response.");
        result.Anime = (result.Anime ?? []).Where(x => x.IsTv).ToList();
        return result;
    }

    public async Task<AnimeDetails> DetailsAsync(string route, CancellationToken ct = default)
    {
        using var client = clients.CreateClient("AnimeSchedule");
        using var response = await client.GetAsync($"anime/{Uri.EscapeDataString(route)}", ct);
        CheckResponse(response);
        return await response.Content.ReadFromJsonAsync<AnimeDetails>(JsonOptions, ct)
            ?? throw new JsonException("Empty anime response.");
    }

    public async Task<TimetableResult> GetTimetableAsync(DateTime startLocal, DateTime endLocal, string airType, bool refresh)
    {
        if (airType is not ("raw" or "sub" or "dub")) throw new ArgumentException("Unknown schedule type.");
        await _gate.WaitAsync();
        try
        {
            var entries = new List<AnimeTimetableEntry>();
            var stale = false;
            var missing = false;
            DateTime? oldest = null;
            var first = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime().Date.AddDays(-1);
            first = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
            var end = DateTime.SpecifyKind(endLocal, DateTimeKind.Local).ToUniversalTime().AddDays(1);
            using var client = clients.CreateClient("AnimeSchedule");
            for (var monday = first; monday < end; monday = monday.AddDays(7))
            {
                var year = ISOWeek.GetYear(monday);
                var week = ISOWeek.GetWeekOfYear(monday);
                var key = $"{airType}/{year}/{week}";
                await using var db = await factory.CreateDbContextAsync();
                var cache = await db.Timetables.FindAsync(key);
                if (refresh || cache is null || DateTime.UtcNow - cache.FetchedUtc > TimeSpan.FromHours(6))
                {
                    try
                    {
                        using var response = await client.GetAsync($"timetables/{airType}?year={year}&week={week}&tz=Etc%2FUTC");
                        CheckResponse(response);
                        var json = await response.Content.ReadAsStringAsync();
                        // Validate before replacing the persisted snapshot.
                        _ = JsonSerializer.Deserialize<List<AnimeTimetableEntry>>(json, JsonOptions) ?? throw new JsonException();
                        if (cache is null)
                        {
                            cache = new TimetableCache { Key = key };
                            db.Timetables.Add(cache);
                        }
                        cache.Json = json;
                        cache.FetchedUtc = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                    {
                        stale = true;
                        if (cache is null) missing = true;
                    }
                }
                if (cache is not null)
                {
                    entries.AddRange(JsonSerializer.Deserialize<List<AnimeTimetableEntry>>(cache.Json, JsonOptions) ?? []);
                    oldest = oldest is null || cache.FetchedUtc < oldest.Value ? cache.FetchedUtc : oldest;
                }
            }
            return new TimetableResult(entries.Where(x => x.IsTv).ToList(), stale, missing, oldest);
        }
        finally { _gate.Release(); }
    }

    private static void CheckResponse(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException("AnimeSchedule rejected the application token. Check your local configuration.", null, response.StatusCode);
        if ((int)response.StatusCode == 429)
            throw new HttpRequestException("AnimeSchedule's request limit was reached. Wait a minute before refreshing.", null, response.StatusCode);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"AnimeSchedule is unavailable (HTTP {(int)response.StatusCode}). Try again later.", null, response.StatusCode);
    }
}
