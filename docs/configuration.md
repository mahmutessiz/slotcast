# Configuration

## Files and precedence

SlotCast uses the .NET Generic Host configuration system. The app explicitly loads optional `appsettings.local.json` from `AppContext.BaseDirectory` and then environment variables, so environment variables override the local file. Configuration is read at startup; restart after changing credentials.

The tracked `appsettings.json` contains public configuration and an empty AnimeSchedule token. Keep private values in ignored `appsettings.local.json`:

```json
{
  "AnimeScheduleApiToken": "YOUR_APPLICATION_TOKEN",
  "Simkl": {
    "ClientId": "YOUR_SIMKL_CLIENT_ID"
  }
}
```

Omit the `Simkl` section to retain the repository's existing client ID. For a separately registered application, use its own client ID.

When developing, put the local file in the repository root; the project copies it to the build output. For an installed app, put it next to `SlotCast.exe`, normally `%LOCALAPPDATA%\Programs\SlotCast\appsettings.local.json`. A `.env` file is not loaded by this project.

## AnimeSchedule application token

Obtain an application API token from [AnimeSchedule](https://animeschedule.net/). SlotCast sends it as a Bearer token to `https://animeschedule.net/api/v3/`. It is an application credential, not an end user's SIMKL account token.

Search, seasonal lists, metadata, and schedules require this configured token. The user-facing app has no provider login flow. The public installer does not embed the developer's token, so each deployment needs configuration.

For a PowerShell session, environment variables can replace the local file:

```powershell
$env:AnimeScheduleApiToken = 'YOUR_APPLICATION_TOKEN'
$env:Simkl__ClientId = 'YOUR_SIMKL_CLIENT_ID'
dotnet run --project SlotCast.csproj
```

The double underscore maps nested configuration keys. These assignments affect processes started from that shell; they do not automatically configure an app launched separately from the Start menu.

## SIMKL

SlotCast uses SIMKL's public data service at `https://data.simkl.in/` for trending data and a named client targeting `https://api.simkl.com/` with the `simkl-api-key` header for supported details. It does not request an end user's access token, authorize private account endpoints, or sync provider account watchlists.

A client ID is public application identification. It is not an AnimeSchedule token or a client secret.

## Packaging and credentials

- `appsettings.local.json` is ignored by Git and never copied to publish output.
- The installer script rejects a nonempty `AnimeScheduleApiToken` in published `appsettings.json`.
- The installer includes only `SlotCast.exe` and public `appsettings.json`; it preserves an existing installed public config.
- JSON watchlist backups never contain credentials.
- The local credential file is plaintext. The app does not encrypt it or provide a credential editor.

Do not commit or ship a private application token. The current implementation does not provide a backend proxy or a managed credential distribution service.
