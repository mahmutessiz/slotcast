using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using SlotCast.Models;

namespace SlotCast.Services;

public class SimklService(IHttpClientFactory clientFactory, IConfiguration configuration)
{
    public async Task<SimklAnimeDetails?> GetAnimeDetailsAsync(int id, CancellationToken ct = default)
    {
        using var client = clientFactory.CreateClient("Simkl");
        return await client.GetFromJsonAsync<SimklAnimeDetails>(
            $"anime/{id}?app-name=slotcast&app-version=1.0", ct);
    }
    public async Task<SimklShow?> GetShowAsync(
        int simklId,
        CancellationToken cancellationToken = default)
    {
        using var client = clientFactory.CreateClient("Simkl");

        return await client.GetFromJsonAsync<SimklShow>(
            $"tv/{simklId}?app-name=slotcast&app-version=1.0",
            cancellationToken);
    }

    public async Task<List<SimklTrendingAnime>> GetTrendingAnimesAsync(
        CancellationToken cancellationToken = default)
    {
        var clientId = configuration["Simkl:ClientId"];

        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("SIMKL Client ID is missing.");

        using var client = clientFactory.CreateClient("SimklPublic");

        var path =
            "discover/trending/anime/today_100.json" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            "&app-name=slotcast&app-version=1.0";

        return await client.GetFromJsonAsync<List<SimklTrendingAnime>>(
            path, cancellationToken) ?? [];
    }
    
}
