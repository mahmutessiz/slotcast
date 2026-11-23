using episodeTracker.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Text.Json;

namespace episodeTracker.Services;

public sealed class WatchlistTransferService(IDbContextFactory<SlotCastDbContext> factory)
{
    private const string Format = "slotcast.watchlist";
    private const int SchemaVersion = 1;
    private const int MaxBytes = 50 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { WriteIndented = true, MaxDepth = 32 };

    public async Task<WatchlistTransferResult> ExportAsync(string destination)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var anime = await db.Anime.AsNoTracking().Include(x => x.WatchedEpisodes).OrderBy(x => x.Route).ToListAsync();
        var backup = new WatchlistBackup
        {
            Format = Format, SchemaVersion = SchemaVersion,
            AppVersion = typeof(WatchlistTransferService).Assembly.GetName().Version?.ToString() ?? "",
            ExportedAtUtc = DateTimeOffset.UtcNow,
            ReleaseSchedule = (await db.Preferences.FindAsync("schedule"))?.Value ?? "sub",
            Anime = anime.Select(x => new WatchlistBackupAnime
            {
                Route = x.Route, Title = x.Title, OriginalTitle = x.OriginalTitle,
                Year = x.Year, TotalEpisodes = x.TotalEpisodes, Status = x.Status,
                Description = x.Description, PosterUrl = x.PosterUrl, AddedAtUtc = AsUtc(x.AddedUtc),
                WatchedEpisodes = x.WatchedEpisodes.OrderBy(w => w.EpisodeNumber).Select(w => new WatchlistBackupEpisode
                    { Number = w.EpisodeNumber, WatchedAtUtc = AsUtc(w.WatchedUtc) }).ToList()
            }).ToList()
        };
        await transaction.CommitAsync();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(backup, JsonOptions);
        var destinationPath = Path.GetFullPath(destination);
        var tempFile = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tempFile, bytes);
            File.Move(tempFile, destinationPath, overwrite: true);
        }
        finally { if (File.Exists(tempFile)) File.Delete(tempFile); }
        return new WatchlistTransferResult(anime.Count, anime.Sum(x => x.WatchedEpisodes.Count));
    }

    public async Task<WatchlistTransferResult> ImportAsync(string source)
    {
        if (new FileInfo(source).Length > MaxBytes) throw new InvalidDataException("The backup exceeds the 50 MB import limit.");
        var bytes = await File.ReadAllBytesAsync(source);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("The backup exceeds the 50 MB import limit.");
        var backup = JsonSerializer.Deserialize<WatchlistBackup>(bytes, JsonOptions)
            ?? throw new InvalidDataException("This file does not contain a SlotCast backup.");
        Validate(backup);
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var existing = await db.Anime.Include(x => x.WatchedEpisodes).ToDictionaryAsync(x => x.Route);
        var addedAnime = 0;
        var addedEpisodes = 0;
        foreach (var imported in backup.Anime!)
        {
            if (!existing.TryGetValue(imported.Route, out var anime))
            {
                anime = new LibraryAnime
                {
                    Route = imported.Route, Title = imported.Title, OriginalTitle = imported.OriginalTitle,
                    Year = imported.Year, TotalEpisodes = imported.TotalEpisodes, Status = imported.Status,
                    Description = imported.Description, PosterUrl = imported.PosterUrl,
                    AddedUtc = imported.AddedAtUtc.UtcDateTime
                };
                db.Anime.Add(anime);
                existing.Add(anime.Route, anime);
                addedAnime++;
            }
            else
            {
                // Keep existing metadata and fill gaps from the backup.
                if (string.IsNullOrWhiteSpace(anime.Title)) anime.Title = imported.Title;
                if (string.IsNullOrWhiteSpace(anime.OriginalTitle)) anime.OriginalTitle = imported.OriginalTitle;
                if (string.IsNullOrWhiteSpace(anime.Description)) anime.Description = imported.Description;
                if (string.IsNullOrWhiteSpace(anime.PosterUrl)) anime.PosterUrl = imported.PosterUrl;
                if (string.IsNullOrWhiteSpace(anime.Status)) anime.Status = imported.Status;
                if (anime.Year == 0) anime.Year = imported.Year;
                anime.TotalEpisodes = Math.Max(anime.TotalEpisodes, imported.TotalEpisodes);
                if (imported.AddedAtUtc.UtcDateTime < anime.AddedUtc) anime.AddedUtc = imported.AddedAtUtc.UtcDateTime;
            }
            var watched = anime.WatchedEpisodes.ToDictionary(x => x.EpisodeNumber);
            foreach (var episode in imported.WatchedEpisodes!)
            {
                if (watched.TryGetValue(episode.Number, out var local))
                {
                    if (episode.WatchedAtUtc.UtcDateTime < local.WatchedUtc) local.WatchedUtc = episode.WatchedAtUtc.UtcDateTime;
                }
                else
                {
                    anime.WatchedEpisodes.Add(new WatchedEpisode
                        { EpisodeNumber = episode.Number, WatchedUtc = episode.WatchedAtUtc.UtcDateTime });
                    addedEpisodes++;
                }
            }
        }
        if (backup.ReleaseSchedule is not null)
        {
            var preference = await db.Preferences.FindAsync("schedule");
            if (preference is null) db.Preferences.Add(new LocalPreference { Key = "schedule", Value = backup.ReleaseSchedule });
            else preference.Value = backup.ReleaseSchedule;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new WatchlistTransferResult(addedAnime, addedEpisodes);
    }

    private static DateTimeOffset AsUtc(DateTime date) => new(DateTime.SpecifyKind(date, DateTimeKind.Utc));

    private static void Validate(WatchlistBackup backup)
    {
        if (backup.Format != Format) throw new InvalidDataException("Choose a JSON file exported by SlotCast.");
        if (backup.SchemaVersion > SchemaVersion)
            throw new InvalidDataException("This backup is from a newer SlotCast version. Update the app before importing it.");
        if (backup.SchemaVersion != SchemaVersion || backup.Anime is null || backup.Anime.Count > 10000)
            throw new InvalidDataException("The backup has an unsupported or incomplete format.");
        if (backup.ReleaseSchedule is not (null or "sub" or "raw" or "dub"))
            throw new InvalidDataException("The backup contains an invalid release schedule preference.");
        var routes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var anime in backup.Anime)
        {
            if (anime is null || string.IsNullOrWhiteSpace(anime.Route) || anime.Route.Length > 500
                || !routes.Add(anime.Route) || string.IsNullOrWhiteSpace(anime.Title) || anime.Title.Length > 1000
                || anime.Year is < 0 or > 9999 || anime.TotalEpisodes is < 0 or > 100000
                || anime.AddedAtUtc.Year < 1900 || anime.WatchedEpisodes is null || anime.WatchedEpisodes.Count > 20000
                || anime.Description is null || anime.Description.Length > 100000 || anime.OriginalTitle is null || anime.Status is null)
                throw new InvalidDataException("The backup contains an invalid or duplicate anime entry.");
            if (!string.IsNullOrWhiteSpace(anime.PosterUrl)
                && (!Uri.TryCreate(anime.PosterUrl, UriKind.Absolute, out var poster) || poster.Scheme != "https"))
                throw new InvalidDataException("The backup contains an invalid poster URL.");
            var numbers = new HashSet<int>();
            if (anime.WatchedEpisodes.Any(x => x is null || x.Number is < 1 or > 100000
                || x.WatchedAtUtc.Year < 1900 || !numbers.Add(x.Number)))
                throw new InvalidDataException("The backup contains invalid or duplicate watched episodes.");
        }
    }
}
