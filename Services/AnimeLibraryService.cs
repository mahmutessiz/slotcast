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
        return await db.Anime.AsNoTracking().Include(x => x.WatchedEpisodes).OrderBy(x => x.Title).ToListAsync();
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
        await db.Anime.Where(x => x.Id == id).ExecuteDeleteAsync();
    }

    public async Task ToggleWatchedAsync(int animeId, int number)
    {
        if (number < 1) return;
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Anime.AnyAsync(x => x.Id == animeId)) return;
        var watched = await db.WatchedEpisodes.SingleOrDefaultAsync(x => x.LibraryAnimeId == animeId && x.EpisodeNumber == number);
        if (watched is null)
            db.WatchedEpisodes.Add(new WatchedEpisode { LibraryAnimeId = animeId, EpisodeNumber = number, WatchedUtc = DateTime.UtcNow });
        else db.WatchedEpisodes.Remove(watched);
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
