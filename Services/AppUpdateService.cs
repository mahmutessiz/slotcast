using System.IO;
using System.Net;
using System.Net.Http;


using System.Text.Json;
using System.Text.RegularExpressions;

namespace SlotCast.Services;

public sealed record AppUpdate(string Version, Uri InstallerUrl, long Size);

public sealed class AppUpdateService(IHttpClientFactory clients)
{
    public const string Repository = "mahmutessiz/slotcast";
    private const long MaxInstallerSize = 300 * 1024 * 1024;
    public static Version InstalledVersion => typeof(AppUpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    public async Task<AppUpdate?> CheckAsync(CancellationToken ct = default)
    {
        using var client = clients.CreateClient("GitHubUpdates");
        using var response = await client.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return ParseRelease(json.RootElement, InstalledVersion);
    }

    public static AppUpdate? ParseRelease(JsonElement release, Version installed)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        var versionText = tag.StartsWith('v') ? tag[1..] : tag;
        if (!Regex.IsMatch(versionText, @"^\d+\.\d+\.\d+(\.\d+)?$") || !Version.TryParse(versionText, out var version)) return null;
        static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        if (Normalize(version) <= Normalize(installed)) return null;
        var name = $"SlotCast-Setup-{versionText}-win-x64.exe";
        Uri? installer = null;
        long size = 0;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString();
            if (assetName != name) continue;
            if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url)
                || url.Scheme != "https" || url.Host != "github.com" || !string.IsNullOrEmpty(url.UserInfo)
                || !url.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The release has an unexpected download address.");
            installer = url; size = asset.GetProperty("size").GetInt64();
        }
        if (installer is null || size <= 0 || size > MaxInstallerSize)
            throw new InvalidDataException("The latest release needs its Windows x64 installer attachment.");
        return new AppUpdate(versionText, installer, size);
    }

    public async Task<string> DownloadAsync(AppUpdate update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        ct = timeout.Token;
        using var client = clients.CreateClient("GitHubUpdates");
        var fileName = $"SlotCast-Setup-{update.Version}-win-x64.exe";
        var folder = Path.Combine(Path.GetTempPath(), "SlotCast-Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var partial = Path.Combine(folder, fileName + ".partial");
        var destination = Path.Combine(folder, fileName);
        try
        {
            using var response = await client.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) != 0)
                {
                    total += count;
                    if (total > update.Size || total > MaxInstallerSize) throw new InvalidDataException("The installer size does not match the release.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    progress?.Report(100d * total / update.Size);
                }
                if (total != update.Size) throw new InvalidDataException("The installer download is incomplete.");
            }
            File.Move(partial, destination);
            return destination;
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            throw;
        }
    }
}