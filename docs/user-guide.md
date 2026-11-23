# Using SlotCast

## First-run token setup

When AnimeSchedule is not configured, Home shows an **AnimeSchedule connection** box. Paste your application token and choose **Save token**. The app verifies it, saves it for this Windows account, and refreshes the feeds without restarting. You can replace a token in Settings. A rejected token brings the setup box back.

The token stays on this PC under `%LOCALAPPDATA%\SlotCast\appsettings.local.json`; app updates retain it, and JSON watchlist backups exclude it. It is masked while entering and stored as plaintext JSON. You do not need to edit a file manually.

## Discover and add anime

Open **Discover**, enter an English or Japanese title, and search. Only TV anime are included. Open a card's **Details** action to view the title inside SlotCast, or choose **+** to save it to your library. Home cards work the same way; clicking a poster opens details.

Home includes anime airing today, the current season, recent premieres, trending titles, and upcoming releases. The current season follows the device's local date: January–March is Winter, April–June Spring, July–September Summer, and October–December Fall. Seasonal lists offer **Load more** when another page is available.

Trending titles from SIMKL may require an explicit search to identify the matching AnimeSchedule title before adding. SlotCast does not guess IDs across providers.

## Calendar

- The calendar shows seven days. **Today** places the current local date in the middle and briefly highlights it.
- Previous and next advance seven days. Scroll sideways if all columns do not fit.
- Choose **My library** for saved titles or **Full TV schedule** for all available TV anime airings.
- Select English subtitles, Japanese broadcasts, or English dubs and choose **Apply**. **Refresh** requests updated data.
- Each entry shows title, episode number, local airing time, and airing season. Hover over a poster for release source and additional information.
- Use the poster's **+** action to add an unsaved title. For saved titles, the watched control changes to a checkmark when watched.

Calendar controls prevent marking future or delayed episodes watched unless already watched. The library also supports manual progress tracking when an episode has no downloaded airing date.

English subtitle listings can fall back to Japanese broadcast times. The displayed source identifies which time is being shown. “Airing: Fall 2026” describes the calendar quarter of that airing, not a numbered franchise season.

Weekly schedules are cached in SQLite and normally reused for six hours. Refresh bypasses that freshness window. If a fetch fails, a saved snapshot can be displayed with a stale indicator. Missing weeks are reported as incomplete. Internet access is needed for uncached searches, metadata, posters, and schedules.

## Watching and Archive

**My library → Watching** contains unfinished titles. Select a title to view its episodes from the top of the list. Each episode explicitly displays **Watched** or **Unwatched**; use its action to change progress.

**Mark completed** marks every known episode watched and moves the title to **Archive**. If the total episode count is unknown, it saves a manual completion state without inventing episodes.

Watching every episode from 1 through the known total automatically completes the series. Watching only the last episode while earlier episodes remain unwatched does not complete it. Unknown totals require manual completion until a total becomes available.

In **Archive**, you can inspect progress and undo watched episodes. Undoing an episode clears manual completion and returns the title to Watching when it is no longer fully watched. If a title with an unknown total was manually completed without watched episodes, mark an episode watched and then undo it to reopen the title.

**Details** opens the selected anime inside SlotCast. **Remove** asks for confirmation and deletes that title and its local watch history.

## Back up and move to another PC

1. Open **Settings** and choose **Export watchlist to JSON**.
2. Save the file and copy it to your other PC.
3. Install SlotCast there and choose **Settings → Import watchlist from JSON**.
4. Select the exported file. Your library updates immediately; schedules refresh when you open the calendar.

An export contains title metadata, poster URLs, added timestamps, watched episode numbers and timestamps, manual completion, and release schedule preference. Poster image files, API tokens, downloaded schedules, and theme settings are not exported.

Imports merge by AnimeSchedule route, and watched episodes merge by episode number. Existing titles and progress are kept. Reimporting the same backup does not duplicate records. Existing metadata is retained where populated; missing metadata can be filled from the backup. The earliest added/watched timestamps are retained.

Invalid backups are rejected before saving. Imports use a database transaction. Files are limited to 50 MB, and a backup using a newer unsupported schema requires a newer app.

The backup schema is identified by `format: "slotcast.watchlist"` and `schemaVersion: 1`. Older schema-1 exports without manual completion remain supported. Treat exported JSON as personal watch-history data.

## Data location and updates

The stable database location is `%LOCALAPPDATA%\SlotCast\slotcast.db`. It is independent of the app's installation folder and version. Installing a new version does not reset the library; the current uninstaller leaves the data directory intact as well.

**Settings → Open data folder** opens this location. Prefer JSON export for transferring data. If making a manual copy of SQLite files, close every SlotCast instance first so a live write or WAL sidecar is not missed.

## App updates

SlotCast checks public releases from `mahmutessiz/slotcast` at startup. When a newer stable version is available, accept the prompt to download and install it, or choose No to continue. You can also use **Settings > Check for updates**, then **Download & install**. No GitHub sign-in is required.

The installer download is verified before launch. SlotCast closes for installation and then restarts; your library and local configuration are kept. A network or verification failure leaves the installed app in place. Updates need internet access and a published release containing the Windows x64 installer and checksum. Older builds without updater support need one manual upgrade first.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| Search or schedule cannot load | Use the token input on Home or in Settings, save a valid token, and check connectivity. |
| Some dates are missing or stale | Check the displayed release source and status, then use Refresh. Providers may not have published the dates yet. |
| Calendar is empty | Switch to Full TV schedule, check the displayed week, or add anime to your library. |
| A title disappeared from Watching | Check Archive; watching all known episodes completes it automatically. |
| Import reports an unsupported version | Update SlotCast before importing the newer backup. |
| SQLite cannot be accessed | Close other SlotCast instances and check folder permissions and free disk space. |

For credential placement and environment variables, see [configuration](configuration.md).
