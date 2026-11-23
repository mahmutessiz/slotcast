using SlotCast.Services;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http;
using System.IO;

static class TokenChecks
{
    public static async Task RunAsync(string folder)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception("Token check failed: " + message); Console.WriteLine("PASS: " + message); }
        var handler = new TokenHandler();
        var clients = new TokenClients(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["AnimeScheduleApiToken"] = "fallback-token" }).Build();
        var path = Path.Combine(folder, "credentials", "appsettings.local.json");
        var store = new AnimeScheduleCredentialService(config, clients, path);
        Check(store.IsConfigured && store.GetToken() == "fallback-token", "Existing local/environment token configuration remains supported");
        await store.ValidateAndSaveAsync(" Bearer saved-token ");
        Check(handler.LastToken == "saved-token" && store.IsConfigured && store.Revision == 1,
            "Saving validates the provided token and activates it immediately");
        Check(new AnimeScheduleCredentialService(config, clients, path).GetToken() == "saved-token",
            "Saved token survives restart and takes precedence over fallback configuration");
        handler.Reply = HttpStatusCode.Unauthorized;
        try { await store.ValidateAndSaveAsync("bad-token"); throw new Exception("Invalid token accepted"); }
        catch (InvalidDataException) { Check(store.GetToken() == "saved-token", "Rejected token never replaces the working saved token"); }
        var pipeline = new AnimeScheduleAuthenticationHandler(store) { InnerHandler = handler };
        using var client = new HttpClient(pipeline, false);
        await client.GetAsync("https://animeschedule.net/api/v3/anime");
        Check(handler.LastToken == "saved-token" && !store.IsConfigured, "An authentication rejection reveals token setup again");
        handler.Reply = HttpStatusCode.OK;
        await store.ValidateAndSaveAsync("replacement-token");
        await client.GetAsync("https://animeschedule.net/api/v3/anime");
        Check(handler.LastToken == "replacement-token" && store.IsConfigured,
            "Existing HTTP clients use a replacement token without restarting the app");
        var empty = new AnimeScheduleCredentialService(new ConfigurationBuilder().Build(), clients, Path.Combine(folder, "missing.json"));
        Check(!empty.IsConfigured, "First-run installation reports that a token is needed");
        try { await empty.ValidateAndSaveAsync("   "); throw new Exception("Empty token accepted"); }
        catch (InvalidDataException) { Check(!File.Exists(Path.Combine(folder, "missing.json")), "Empty tokens are rejected without creating a credential file"); }
    }
    private sealed class TokenClients(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class TokenHandler : HttpMessageHandler
    {
        public HttpStatusCode Reply { get; set; } = HttpStatusCode.OK;
        public string? LastToken { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastToken = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(Reply) { Content = new StringContent("{}") });
        }
    }
}