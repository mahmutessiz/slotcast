using episodeTracker.Models;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.RegularExpressions;

namespace episodeTracker.Services;

public sealed class AnimeLibraryService(IDbContextFactory<SlotCastDbContext> factory)
{
    public async Task InitializeAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task<List<LibraryAnime>> GetLibraryAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var anime = await db.Anime.AsNoTracking().Include(x => x.WatchedEpisodes).OrderBy(x => x.Title).ToListAsync();
        var completed = await db.Preferences.Where(x => x.Key.StartsWith("completed:") && x.Value == "true")
            .Select(x => x.Key).ToListAsync();
        foreach (var item in anime) item.ManuallyCompleted = completed.Contains("completed:" + item.Route);
        return anime;
    }

    public async Task SetCompletedAsync(int id, bool completed)
    {
        await using var db = await factory.CreateDbContextAsync();
        var anime = await db.Anime.FindAsync(id);
        if (anime is null) return;
        if (completed && anime.TotalEpisodes > 0)
        {
            var watched = await db.WatchedEpisodes.Where(x => x.LibraryAnimeId == id).Select(x => x.EpisodeNumber).ToListAsync();
            foreach (var number in Enumerable.Range(1, anime.TotalEpisodes).Except(watched))
                db.WatchedEpisodes.Add(new WatchedEpisode { LibraryAnimeId = id, EpisodeNumber = number, WatchedUtc = DateTime.UtcNow });
        }
        var key = "completed:" + anime.Route;
        var preference = await db.Preferences.FindAsync(key);
        if (preference is null) db.Preferences.Add(new LocalPreference { Key = key, Value = completed ? "true" : "false" });
        else preference.Value = completed ? "true" : "false";
        await db.SaveChangesAsync();
    }

    public async Task AddAsync(AnimeDetails anime)
    {
        if (!anime.IsTv || string.IsNullOrWhiteSpace(anime.Route))
            throw new InvalidOperationException("Only TV anime can be added to this calendar.");
        await using var db = await factory.CreateDbContextAsync();
        var item = await db.Anime.SingleOrDefaultAsync(x => x.Route == anime.Route);
        if (item is null)
        {
            item = new LibraryAnime { Route = anime.Route, AddedUtc = DateTime.UtcNow };
            db.Anime.Add(item);
        }
        item.Title = anime.DisplayTitle;
        item.OriginalTitle = anime.Title;
        item.Year = anime.Year;
        item.TotalEpisodes = anime.Episodes;
        item.Status = anime.Status ?? "Unknown";
        item.Description = WebUtility.HtmlDecode(Regex.Replace(anime.Description ?? "", "<[^>]+>", " "));
        item.PosterUrl = anime.PosterUrl;
        await db.SaveChangesAsync();
    }

    public async Task RemoveAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var anime = await db.Anime.FindAsync(id);
        if (anime is not null) await db.Preferences.Where(x => x.Key == "completed:" + anime.Route).ExecuteDeleteAsync();
        await db.Anime.Where(x => x.Id == id).ExecuteDeleteAsync();
    }

    public async Task ToggleWatchedAsync(int animeId, int number)
    {
        if (number < 1) return;
        await using var db = await factory.CreateDbContextAsync();
        var anime = await db.Anime.FindAsync(animeId);
        if (anime is null) return;
        var watched = await db.WatchedEpisodes.SingleOrDefaultAsync(x => x.LibraryAnimeId == animeId && x.EpisodeNumber == number);
        if (watched is null)
            db.WatchedEpisodes.Add(new WatchedEpisode { LibraryAnimeId = animeId, EpisodeNumber = number, WatchedUtc = DateTime.UtcNow });
        else
        {
            db.WatchedEpisodes.Remove(watched);
            var completed = await db.Preferences.FindAsync("completed:" + anime.Route);
            if (completed is not null) completed.Value = "false";
        }
        await db.SaveChangesAsync();
    }

    public async Task<string> GetSchedulePreferenceAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.Preferences.FindAsync("schedule"))?.Value ?? "sub";
    }

    public async Task SetSchedulePreferenceAsync(string value)
    {
        await using var db = await factory.CreateDbContextAsync();
        var preference = await db.Preferences.FindAsync("schedule");
        if (preference is null) db.Preferences.Add(new LocalPreference { Key = "schedule", Value = value });
        else preference.Value = value;
        await db.SaveChangesAsync();
    }
}
