# Internal deployment guide

## Command summary

Run these commands in PowerShell from the repository root (the folder containing `SlotCast.slnx`). Replace `1.0.3` with your release version. Install a compatible .NET SDK and Inno Setup 6 first.

```powershell
# Restore and build the application.
dotnet restore SlotCast.csproj
dotnet build SlotCast.csproj -c Release --no-restore

# Run the fixture smoke checks, including SQLite persistence and WPF rendering.
dotnet run --project Tests/SlotCast.Smoke/SlotCast.Smoke.csproj -p:UseSharedCompilation=false --disable-build-servers

# Publish a self-contained app AND compile its Inno Setup installer.
.\scripts\build-installer.ps1 -Version 1.0.3

# Inspect the installer checksum.
Get-FileHash .\artifacts\installer\SlotCast-Setup-1.0.3-win-x64.exe -Algorithm SHA256
Get-Content .\artifacts\installer\SlotCast-Setup-1.0.3-win-x64.exe.sha256
```

The installer script already runs `dotnet publish`; a separate publish is unnecessary for this workflow. Stop if any command fails. This guide documents manual deployment; it does not upload releases or install the app automatically.

Expected deliverables:

- `artifacts/installer/SlotCast-Setup-<version>-win-x64.exe`
- `artifacts/installer/SlotCast-Setup-<version>-win-x64.exe.sha256`
- A fresh publish folder under `artifacts/publish/win-x64/<version>-<build-stamp>/` containing `SlotCast.exe` and public `appsettings.json`.

## Prepare the build machine

Use Windows with an interactive desktop for WPF smoke checks. The project targets .NET 10 for Windows; the current dependency set has been validated with the .NET 11 SDK. NuGet access is needed for packages and uncached runtime packs.

Install Inno Setup 6. The build script finds `ISCC.exe` on PATH or in the normal Program Files installation directories. Check the available SDK and compiler:

```powershell
dotnet --list-sdks
Test-Path 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

If the compiler is elsewhere, supply its full path:

```powershell
.\scripts\build-installer.ps1 -Version 1.0.3 -InnoCompiler 'C:\Tools\Inno Setup 6\ISCC.exe'
```

If local script execution is blocked, allow it for this invocation only:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -Version 1.0.3
```

## Configure credentials

Keep the tracked `appsettings.json` token empty. Put your development application token in ignored `appsettings.local.json`; see [configuration](configuration.md). A provider token is not required for fixture tests or compilation, but it is required to verify live AnimeSchedule features.

The installer deliberately excludes `appsettings.local.json`. The script also rejects a nonempty AnimeSchedule token in the published public configuration. Never work around that check by including private config in the installer.

For a live provider check before release:

```powershell
dotnet run --project Tests/SlotCast.Smoke/SlotCast.Smoke.csproj -p:UseSharedCompilation=false --disable-build-servers -- --live
```

Live checks use temporary databases and do not modify your library.

## Version and build the installer

Pass `-Version` explicitly for a release. It overrides the app version for that publish and the Inno Setup `AppVersion`; it does not edit source files. Keep the defaults in `SlotCast.csproj`, `scripts/build-installer.ps1`, and `installer/SlotCast.iss` aligned when updating the default development/release version.

The script publishes into a unique staging folder, validates the public config, invokes Inno Setup, and writes a checksum. A repeated build of the same version overwrites that version's installer and checksum, while publish staging folders remain separate. Avoid replacing a release artifact after distributing its checksum.

Keep the installer `AppId` stable. That identity allows a later version to upgrade the existing application instead of registering a separate product. The current build is not configured to code-sign the executable or installer.

### Publish without an installer

For a portable/internal build, publish separately:

```powershell
dotnet publish SlotCast.csproj -p:PublishProfile=WindowsSelfContained -p:Version=1.0.3
```

Default output: `artifacts/publish/win-x64/`. Deploy both `SlotCast.exe` and public `appsettings.json`. This is a Windows x64, self-contained build, so the destination does not need a separate .NET runtime. Trimming is disabled and native libraries can extract at runtime.

### Invoke Inno Setup directly

Prefer the script for its fresh staging folder and credential checks. If compiling manually, publish to an explicit directory and pass that same directory to `ISCC.exe`:

```powershell
$publishDirectory = Join-Path (Get-Location).Path 'artifacts\publish\manual-1.0.3'
$installerDirectory = Join-Path (Get-Location).Path 'artifacts\installer'
dotnet publish SlotCast.csproj -p:PublishProfile=WindowsSelfContained -p:Version=1.0.3 -o $publishDirectory
# Proceed only after publish succeeds and the folder contains no private configuration.
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' '/DAppVersion=1.0.3' "/DPublishDir=$publishDirectory" "/DInstallerDir=$installerDirectory" '.\installer\SlotCast.iss'
```

Direct Inno compilation does not perform the script's token checks or generate its checksum file. Opening `installer/SlotCast.iss` in the Inno Setup GUI uses the defaults declared in that file; the required publish files must already exist at its default publish path.

## Install on an internal PC

Run the generated setup executable on the destination PC. The installer normally installs per-user to `%LOCALAPPDATA%\Programs\SlotCast`; administrator privileges are not required. It supports an optional desktop shortcut and offers to close a running app during an update. Save your work and close SlotCast before upgrading.

After installation, use the token input on Home or in Settings and choose Save token. The app validates and stores the token under `%LOCALAPPDATA%\SlotCast\appsettings.local.json` and applies it immediately. Manual app-folder configuration remains supported. Configure an application token there or through the environment; credentials are not part of the installer or watchlist backup. Without configuration, saved local data remains available, but provider-backed features need the token.

Test Home, search, details, the weekly calendar, watched toggles, Archive, and JSON export/import. Building the installer alone does not verify installation behavior.

## Verify upgrades and data retention

Before testing an upgrade, export a JSON backup from Settings. Install the previous version, add a sample title and watch history, then install the new version into the same location. Confirm the library, completion state, watched episodes, schedule preference, and local credential file still work.

The database lives at `%LOCALAPPDATA%\SlotCast\slotcast.db`, outside the install directory. Keep that path stable. The current installer and uninstaller leave it intact. Use Settings export/import to transfer a library between PCs; copying the app folder does not transfer the database.

Startup currently uses EF Core `EnsureCreatedAsync`, not migrations. It cannot upgrade existing table schemas. If a release changes mapped entities, add and verify an upgrade strategy before deploying against data you need to retain. See [development](development.md#storage-and-schema-changes).

To recover, retain the JSON backup and use a compatible build. Do not assume an older executable can read a newer database schema. Close every app instance before manually copying SQLite files.

## Release handoff

Share the installer, configuration instructions, and the corresponding source revision. The matching checksum is optional for clients running 1.0.6 or later. Keep private tokens, local databases, and personal JSON backups out of release attachments. For public redistribution, retain the GPL license and provide the corresponding source under the project's license; see [LICENSE](../LICENSE).

`artifacts/` is ignored by Git. Building an installer does not commit, push, create a GitHub release, or upload attachments. Those are separate release actions.

## Publish releases for in-app updates

SlotCast 1.0.3 and later check `mahmutessiz/slotcast` for a newer stable GitHub release at startup. Settings also has **Check for updates** and **Download & install**. The startup check offers a Yes/No prompt when an update is available; it does not force an installation. Checking public releases requires no GitHub login or token.

To distribute the next update (for example, 1.0.4):

```powershell
.\scripts\build-installer.ps1 -Version 1.0.4
```

Create a published, non-prerelease GitHub release at https://github.com/mahmutessiz/slotcast/releases/new with tag `v1.0.4` (or `1.0.4`). Attach the generated installer, keeping its name unchanged. The generated checksum file is optional:

- `SlotCast-Setup-1.0.4-win-x64.exe`
- Optional: `SlotCast-Setup-1.0.4-win-x64.exe.sha256` (not used by the updater in 1.0.6 and later)

Mark it as the latest stable release. A Git tag alone, a draft, or GitHub's automatic source ZIP is not an installable update. Version tags must be numeric `major.minor.patch` with an optional fourth numeric component; prerelease tags are skipped. The tag's numeric version must match the installer filename and build version. Do not prefix the installer filename with `v`.

The updater compares numeric versions, ignores older/equal versions, validates asset addresses against this repository, checks download length, and launches the Inno Setup installer without requesting a checksum file. The user sees download progress in Settings. The installer runs with a progress window, keeps the current application directory, and restarts SlotCast after completion when launched by the updater. It does not request a Windows reboot. SQLite data and local config are retained.

Download failures leave the running app in place and report a status in Settings. Startup network errors do not display an interruption dialog. GitHub rate limits can delay checks. Downloads are staged in a unique folder under `%TEMP%\SlotCast-Updates` and limited to 300 MB with a five-minute download deadline. Completed installer files can remain there after an update; close any installer before removing old download folders.

The first build containing the updater must be installed manually; older builds cannot gain this feature until upgraded. Smoke checks verify release parsing, version comparison, allowed download addresses, valid downloads, and rejection of incomplete files without executing an installer. Test an actual update between two builds on a separate Windows account/VM before public rollout.

See the official [GitHub Releases API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release) and [Inno Setup command-line reference](https://jrsoftware.org/ishelp/topic_setupcmdline.htm) for the upstream update protocol and installer switches.

Checksum-free updates are supported starting with SlotCast 1.0.6. Older updater builds still require the checksum attachment, so retain it on releases used to upgrade those clients. After installing 1.0.6, installer-only releases are supported.
