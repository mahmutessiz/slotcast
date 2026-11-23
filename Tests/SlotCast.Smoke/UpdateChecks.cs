using SlotCast.Services;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class UpdateChecks
{
    public static async Task RunAsync()
    {
        JsonElement Release(string tag, bool prerelease = false, bool checksum = true, string host = "github.com")
        {
            var version = tag.TrimStart('v');
            var name = $"SlotCast-Setup-{version}-win-x64.exe";
            var assets = new List<object> { new { name, size = 9, browser_download_url = $"https://{host}/mahmutessiz/slotcast/releases/download/{tag}/{name}" } };
            if (checksum) assets.Add(new { name = name + ".sha256", size = 100, browser_download_url = $"https://{host}/mahmutessiz/slotcast/releases/download/{tag}/{name}.sha256" });
            return JsonSerializer.SerializeToElement(new { tag_name = tag, draft = false, prerelease, assets });
        }
        void Check(bool ok, string message) { if (!ok) throw new Exception("Update check failed: " + message); Console.WriteLine("PASS: " + message); }
        var installed = new Version(1, 0, 2, 0);
        Check(AppUpdateService.ParseRelease(Release("v1.0.3"), installed)?.Version == "1.0.3", "Updater recognizes newer v-prefixed stable releases");
        Check(AppUpdateService.ParseRelease(Release("1.0.2"), installed) is null, "Three-part release version equals installed four-part version");
        Check(AppUpdateService.ParseRelease(Release("1.0.1"), installed) is null, "Updater never offers a downgrade");
        Check(AppUpdateService.ParseRelease(Release("2.0.0", true), installed) is null, "Updater ignores prereleases");
        Check(AppUpdateService.ParseRelease(Release("v2.0.0-beta"), installed) is null, "Updater ignores unsupported tag formats");
        foreach (var release in new[] { Release("2.0.0", checksum: false), Release("2.0.0", host: "example.com") })
        {
            try { AppUpdateService.ParseRelease(release, installed); throw new Exception("Unsafe release accepted"); }
            catch (System.IO.InvalidDataException) { Check(true, "Updater rejects missing checksum or unexpected download host"); }
        }
        var payload = Encoding.UTF8.GetBytes("MZfixture");
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var update = AppUpdateService.ParseRelease(Release("9.9.9"), installed)!;
        var handler = new UpdateHandler(payload, hash);
        var service = new AppUpdateService(new UpdateClients(handler));
        Check(await service.CheckAsync() is null, "No published GitHub release is handled without an error");
        handler.ReleaseJson = Release("9.9.9").GetRawText();
        Check((await service.CheckAsync())?.Version == "9.9.9", "Updater reads GitHub release JSON over HTTP");
        var destination = await service.DownloadAsync(update);
        Check((await System.IO.File.ReadAllBytesAsync(destination)).SequenceEqual(payload), "Only a complete checksum-verified installer is returned");
        System.IO.File.Delete(destination);
        handler.Checksum = new string('0', 64);
        try { await service.DownloadAsync(update); throw new Exception("Bad checksum accepted"); }
        catch (System.IO.InvalidDataException) { Check(true, "Corrupted installer is rejected before launch"); }
        handler.Checksum = hash;
        handler.Payload = payload[..^1];
        try { await service.DownloadAsync(update); throw new Exception("Incomplete download accepted"); }
        catch (System.IO.InvalidDataException) { Check(true, "Truncated installer is rejected before launch"); }
    }
    private sealed class UpdateClients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private sealed class UpdateHandler(byte[] payload, string checksum) : HttpMessageHandler
    {
        public byte[] Payload { get; set; } = payload;
        public string Checksum { get; set; } = checksum;
        public string? ReleaseJson { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "api.github.com")
                return Task.FromResult(ReleaseJson is null ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReleaseJson) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri.AbsolutePath.EndsWith(".sha256")
                    ? new StringContent(Checksum + "  SlotCast-Setup-9.9.9-win-x64.exe\n") : new ByteArrayContent(Payload)
            });
        }
    }
}