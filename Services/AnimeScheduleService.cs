using SlotCast.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace SlotCast.Services;

public sealed class AnimeScheduleService(IHttpClientFactory clients, IDbContextFactory<SlotCastDbContext> factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new();
    public async Task<T?> ReadCacheAsync<T>(string key)
    {
        await using var db = await factory.CreateDbContextAsync();
        var cache = await db.Timetables.FindAsync("data/" + key);
        if (cache is null) return default;
        try { return JsonSerializer.Deserialize<T>(cache.Json, JsonOptions); }
        catch (JsonException) { return default; }
    }
    public async Task SaveCacheAsync<T>(string key, T value)
    {
        await using var db = await factory.CreateDbContextAsync();
        var cache = await db.Timetables.FindAsync("data/" + key);
        if (cache is null) { cache = new TimetableCache { Key = "data/" + key }; db.Timetables.Add(cache); }
        cache.Json = JsonSerializer.Serialize(value, JsonOptions);
        cache.FetchedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
    public async Task<T> CachedAsync<T>(string key, TimeSpan lifetime, Func<Task<T>> fetch, bool refresh = false)
    {
        var gate = _cacheLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var cache = await db.Timetables.FindAsync("data/" + key);
            T? saved = default;
            var hasSaved = false;
            if (cache is not null)
            {
                try { saved = JsonSerializer.Deserialize<T>(cache.Json, JsonOptions); hasSaved = saved is not null; }
                catch (JsonException) { }
                if (!refresh && hasSaved && DateTime.UtcNow - cache.FetchedUtc < lifetime) return saved!;
            }
            try
            {
                var value = await fetch();
                await SaveCacheAsync(key, value);
                return value;
            }
            catch (Exception ex) when (hasSaved && ex is HttpRequestException or TaskCanceledException or JsonException)
            { return saved!; }
        }
        finally { gate.Release(); }
    }
    public Task<AnimeSearchResponse> GetSeasonAsync(AnimeSeason season, int page = 1, CancellationToken ct = default, bool refresh = false) =>
        CachedAsync($"season/{season.Year}/{season.Slug}/{page}", TimeSpan.FromHours(24), () => FetchSeasonAsync(season, page, ct), refresh);
    public Task<List<AnimeDetails>> GetReleasesAsync(bool upcoming, CancellationToken ct = default, bool refresh = false) =>
        CachedAsync($"releases/{upcoming}", TimeSpan.FromHours(6), () => FetchReleasesAsync(upcoming, ct), refresh);
    public Task<AnimeSearchResponse> SearchAsync(string query, int page, CancellationToken ct = default) =>
        CachedAsync($"search/{query.Trim().ToLowerInvariant()}/{page}", TimeSpan.FromHours(6), () => FetchSearchAsync(query, page, ct));
    public Task<AnimeDetails> DetailsAsync(string route, CancellationToken ct = default, bool refresh = false) =>
        CachedAsync("details/" + route, TimeSpan.FromDays(1), () => FetchDetailsAsync(route, ct), refresh);

    private async Task<AnimeSearchResponse> FetchSeasonAsync(AnimeSeason season, int page = 1, CancellationToken ct = default)
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

    private async Task<List<AnimeDetails>> FetchReleasesAsync(bool upcoming, CancellationToken ct = default)
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

    private async Task<AnimeSearchResponse> FetchSearchAsync(string query, int page, CancellationToken ct = default)
    {
        using var client = clients.CreateClient("AnimeSchedule");
        using var response = await client.GetAsync($"anime?q={Uri.EscapeDataString(query.Trim())}&media-types=tv&mt=all&page={page}", ct);
        CheckResponse(response);
        var result = await response.Content.ReadFromJsonAsync<AnimeSearchResponse>(JsonOptions, ct)
            ?? throw new JsonException("Empty search response.");
        result.Anime = (result.Anime ?? []).Where(x => x.IsTv).ToList();
        return result;
    }

    private async Task<AnimeDetails> FetchDetailsAsync(string route, CancellationToken ct = default)
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
                if (refresh || cache is null || DateTime.UtcNow - cache.FetchedUtc > (monday.AddDays(7) < DateTime.UtcNow.AddDays(-14) ? TimeSpan.FromDays(30) : TimeSpan.FromHours(1)))
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

    private readonly SemaphoreSlim _episodeGate = new(1, 1);
    private DateTimeOffset _lastEpisodeRequest;

    public async Task<LibraryEpisodeResult> GetLibraryEpisodesAsync(string route, bool refresh = false)
    {
        await _episodeGate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var key = "episodes/" + route;
            var cache = await db.Timetables.FindAsync(key);
            if (!refresh && cache is not null && DateTime.UtcNow - cache.FetchedUtc < TimeSpan.FromDays(7))
                return new(JsonSerializer.Deserialize<List<LibraryEpisodeInfo>>(cache.Json, JsonOptions) ?? [], false);
            try
            {
                var anime = await DetailsAsync(route);
                if (!Uri.TryCreate(anime.Websites?.Mal, UriKind.Absolute, out var mal)
                    || mal.Host is not ("myanimelist.net" or "www.myanimelist.net")
                    || mal.Segments.Length < 3 || mal.Segments[1] != "anime/"
                    || !int.TryParse(mal.Segments[2].Trim('/'), out var id) || id <= 0)
                    return new([], false);
                var episodes = new List<LibraryEpisodeInfo>();
                using var client = clients.CreateClient("EpisodeMetadata");
                for (var page = 1; ; page++)
                {
                    var wait = TimeSpan.FromMilliseconds(400) - (DateTimeOffset.UtcNow - _lastEpisodeRequest);
                    if (wait > TimeSpan.Zero) await Task.Delay(wait);
                    _lastEpisodeRequest = DateTimeOffset.UtcNow;
                    using var response = await client.GetAsync($"https://api.jikan.moe/v4/anime/{id}/episodes?page={page}");
                    response.EnsureSuccessStatusCode();
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    foreach (var item in json.RootElement.GetProperty("data").EnumerateArray())
                    {
                        var title = item.TryGetProperty("title", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
                        var aired = item.TryGetProperty("aired", out var date) && date.ValueKind == JsonValueKind.String
                            && date.TryGetDateTimeOffset(out var value) ? value : (DateTimeOffset?)null;
                        episodes.Add(new(item.GetProperty("mal_id").GetInt32(), title, aired));
                    }
                    if (!json.RootElement.GetProperty("pagination").GetProperty("has_next_page").GetBoolean()) break;
                }
                if (cache is null) { cache = new TimetableCache { Key = key }; db.Timetables.Add(cache); }
                cache.Json = JsonSerializer.Serialize(episodes, JsonOptions);
                cache.FetchedUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return new(episodes, false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                return new(cache is null ? [] : JsonSerializer.Deserialize<List<LibraryEpisodeInfo>>(cache.Json, JsonOptions) ?? [], true);
            }
        }
        finally { _episodeGate.Release(); }
    }

    public async Task LoadAnimeScheduleHistoryAsync(string route, string airType, Action<List<AnimeTimetableEntry>> onLoaded, bool refresh = false)
    {
        // Always publish saved dates first, independently of optional episode-title providers.
        onLoaded(await GetSavedTimetableAsync(airType));
        var anime = await DetailsAsync(route);
        var premiere = airType == "dub" ? anime.DubPremier : airType == "sub" ? anime.SubPremier ?? anime.Premier : anime.Premier;
        if (premiere is null) return;
        var start = premiere.Value.ToLocalTime().Date;
        // Query real timetable records; never manufacture dates by adding seven days per episode.
        var end = DateTime.Today.AddDays(14);
        if (anime.Episodes > 0) end = new[] { end, start.AddDays(anime.Episodes * 8d + 90) }.Min();
        for (var chunk = start; chunk < end; chunk = chunk.AddDays(56))
        {
            var result = await GetTimetableAsync(chunk, new[] { chunk.AddDays(56), end }.Min(), airType, refresh);
            onLoaded(result.Entries);
            if (result.HasMissingWeeks) break;
        }
    }

    public async Task<List<AnimeTimetableEntry>> GetSavedTimetableAsync(string airType)
    {
        await using var db = await factory.CreateDbContextAsync();
        var snapshots = await db.Timetables.Where(x => x.Key.StartsWith(airType + "/")).OrderBy(x => x.FetchedUtc).ToListAsync();
        return snapshots.SelectMany(x => JsonSerializer.Deserialize<List<AnimeTimetableEntry>>(x.Json, JsonOptions) ?? []).ToList();
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
