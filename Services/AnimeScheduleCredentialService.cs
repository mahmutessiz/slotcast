using Microsoft.Extensions.Configuration;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SlotCast.Services;

public sealed class AnimeScheduleCredentialService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _clients;
    private readonly string _path;
    private string? _savedToken;
    private bool _rejected;
    public event Action? Changed;
    public int Revision { get; private set; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetToken()) && !_rejected;

    public AnimeScheduleCredentialService(IConfiguration configuration, IHttpClientFactory clients, string? storagePath = null)
    {
        _configuration = configuration;
        _clients = clients;
        _path = storagePath ?? Path.Combine(SlotCastPaths.DataDirectory, "appsettings.local.json");
        try
        {
            if (File.Exists(_path))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(_path));
                if (json.RootElement.TryGetProperty("AnimeScheduleApiToken", out var token) && token.ValueKind == JsonValueKind.String)
                    _savedToken = token.GetString();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
    }

    public string GetToken() => string.IsNullOrWhiteSpace(_savedToken)
        ? _configuration["AnimeScheduleApiToken"]?.Trim() ?? "" : _savedToken;

    public void MarkRejected()
    {
        if (_rejected) return;
        _rejected = true;
        Changed?.Invoke();
    }

    public async Task ValidateAndSaveAsync(string token, CancellationToken ct = default)
    {
        token = token.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192 || token.Any(char.IsWhiteSpace))
            throw new InvalidDataException("Enter your AnimeSchedule API token without spaces.");
        using var client = _clients.CreateClient("AnimeScheduleTokenValidation");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://animeschedule.net/api/v3/anime?media-types=tv&mt=all&page=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidDataException("AnimeSchedule rejected this token. Check it and try again. Your previous token has been kept.");
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { AnimeScheduleApiToken = token }), ct);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        _savedToken = token;
        _rejected = false;
        Revision++;
        Changed?.Invoke();
    }
}

public sealed class AnimeScheduleAuthenticationHandler(AnimeScheduleCredentialService credentials) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = credentials.GetToken();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Add your AnimeSchedule token using the setup box on Home or in Settings.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) credentials.MarkRejected();
        return response;
    }
}