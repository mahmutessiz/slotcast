# Development

## Stack and layout

SlotCast is a Windows WPF app targeting `net10.0-windows`, with nullable reference types and implicit usings enabled. Current dependencies include WPF UI 4.3, CommunityToolkit.Mvvm 8.4, EF Core SQLite 10.0, and Microsoft.Extensions 11. Builds have been validated with the .NET 11 SDK. Use Windows and an SDK that supports this target and dependency set.

| Location | Purpose |
| --- | --- |
| `App.xaml.cs` | Host, configuration, DI, named HTTP clients, and SQLite factory. |
| `Views/Pages/` | Home, calendar, library/archive, discovery, details, and settings views. |
| `ViewModels/Pages/` | MVVM state and commands; `DashboardViewModel` shares library and calendar state. |
| `Services/AnimeScheduleService.cs` | TV-only search, metadata, seasonal feeds, and cached timetables. |
| `Services/SimklService.cs` | Public trending data and title details. |
| `Services/AnimeLibraryService.cs` | Library writes, episode progress, completion, and preferences. |
| `Services/WatchlistTransferService.cs` | Validated, transactional JSON import and atomic export. |
| `Services/SlotCastDbContext.cs` | SQLite entities, unique indexes, and watched-episode cascade delete. |
| `Services/SlotCastPaths.cs` | Version-independent local data location. |
| `Models/WatchlistBackup.cs` | Portable, versioned backup DTOs without machine-specific IDs. |
| `Tests/SlotCast.Smoke/` | Executable integration and WPF rendering checks. |
| `Properties/PublishProfiles/` | Windows x64 self-contained publish profile. |
| `installer/`, `scripts/` | Inno Setup definition and release build script. |

## Build

From the repository root:

```powershell
dotnet restore episodeTracker.csproj
dotnet build episodeTracker.csproj
dotnet run --project episodeTracker.csproj
```

See [configuration](configuration.md) before running provider-backed features. Keep local credentials out of Git.

## Validation

The fixture smoke suite uses temporary SQLite databases and fake HTTP responses. It does not access the real user database or require provider credentials:

```powershell
dotnet run --project Tests/SlotCast.Smoke/SlotCast.Smoke.csproj -p:UseSharedCompilation=false --disable-build-servers
```

It checks library persistence, episode toggles, completion and archive filtering, backup merging and validation, schedule caching/fallback, season boundaries, provider model handling, WPF binding errors, and page layout/scroll behavior. It is an executable harness, not a `dotnet test` project. WPF rendering requires Windows and an interactive desktop environment.

Add `-- --live` to check real endpoints and poster decoding. Live checks require `appsettings.local.json`, provider access, and a working internet connection. They use temporary databases and do not modify your saved library.

## Documentation screenshots

From the repository root, run:

```powershell
dotnet run --project Tests/SlotCast.Smoke/SlotCast.Smoke.csproj -p:UseSharedCompilation=false --disable-build-servers -- --live --screenshots
```

This runs the checks, seeds example progress into an isolated temporary database, and renders Home, Calendar, Watching, and Archive into `docs/screenshots/`. Images are actual 1160 × 780 WPF page renders, excluding the main window/navigation shell. Poster downloads are allowed a short dispatcher interval to complete; inspect each image before committing it. Provider content and dates will vary between captures. `--screenshots` requires `--live` to produce the documentation captures.

## Storage and schema changes

The SQLite database is always `%LOCALAPPDATA%\SlotCast\slotcast.db`. `Anime` has a unique provider route; watched episodes have a unique `(LibraryAnimeId, EpisodeNumber)` pair and cascade when a title is deleted. Preferences and timetable caches live in the same database.

Completion is computed when all episode numbers from 1 through a known total are watched. Explicit completion is stored under a `completed:<route>` preference. This supports manually completed titles with unknown totals. Undoing a watched episode clears explicit completion. Portable backups carry that explicit state independently of database IDs.

Startup currently uses `EnsureCreatedAsync`; there are no EF migrations. It creates missing databases but does not upgrade existing table schemas. If changing mapped entities, introduce an explicit upgrade strategy before distributing the change, or reset only disposable development databases. Never add a startup database deletion against the user's stable data path.

Backup format changes need separate consideration from SQLite schema changes. The current `slotcast.watchlist` schema is version 1; `manuallyCompleted` is optional on older exports. Imports validate the entire file before writing and reject unsupported future versions. Keep imports idempotent and preserve existing progress.

## Self-contained publish and installer

```powershell
dotnet publish episodeTracker.csproj -p:PublishProfile=WindowsSelfContained
```

The profile produces a Windows x64, self-contained, single-file executable plus public `appsettings.json`. Native libraries can extract at runtime; trimming is disabled for WPF. Output goes under `artifacts/publish/win-x64/`.

Install Inno Setup 6, then build:

```powershell
.\scripts\build-installer.ps1
# Or select a release version:
.\scripts\build-installer.ps1 -Version 1.0.3
```

The script creates a fresh staging directory, publishes the app, checks for private credentials, compiles the installer, and writes a SHA-256 checksum alongside it in `artifacts/installer/`. To select a nonstandard compiler location, pass `-InnoCompiler 'C:\path\to\ISCC.exe'`.

Keep the Inno Setup `AppId` stable across versions so upgrades identify the same application. Installation is per-user, normally under `%LOCALAPPDATA%\Programs\SlotCast`, with optional desktop shortcuts. The SQLite database is outside that folder and is neither included nor removed by the installer. The current uninstaller retains user data.

`logo.ico` supplies the executable, window, shortcut, and installer branding. Before a release, review version defaults, run the smoke suite, inspect screenshots, and test installation/upgrading on a separate Windows environment. Compiling an installer does not verify the installed upgrade flow.

## License and attribution

Project code uses `GPL-3.0-or-later`; see [LICENSE](../LICENSE). Dependencies and provider artwork/data retain their own licenses and terms. Screenshots illustrate the application and do not grant ownership of third-party posters.
