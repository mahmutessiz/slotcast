using SlotCast.Models;
using Microsoft.EntityFrameworkCore;

namespace SlotCast.Services;

public sealed class SlotCastDbContext(DbContextOptions<SlotCastDbContext> options) : DbContext(options)
{
    public DbSet<LibraryAnime> Anime => Set<LibraryAnime>();
    public DbSet<WatchedEpisode> WatchedEpisodes => Set<WatchedEpisode>();
    public DbSet<TimetableCache> Timetables => Set<TimetableCache>();
    public DbSet<LocalPreference> Preferences => Set<LocalPreference>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<LibraryAnime>().HasIndex(x => x.Route).IsUnique();
        model.Entity<LibraryAnime>().HasMany(x => x.WatchedEpisodes).WithOne()
            .HasForeignKey(x => x.LibraryAnimeId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<WatchedEpisode>().HasIndex(x => new { x.LibraryAnimeId, x.EpisodeNumber }).IsUnique();
        model.Entity<TimetableCache>().HasKey(x => x.Key);
        model.Entity<LocalPreference>().HasKey(x => x.Key);
    }
}
