using System.Net.Http;
using System.Net.Http.Json;
using episodeTracker.Models;

namespace episodeTracker.Services;

public class SimklService(IHttpClientFactory clientFactory)
{
    public async Task<SimklShow?> GetShowAsync(
        int simklId,
        CancellationToken cancellationToken = default)
    {
        using var client = clientFactory.CreateClient("Simkl");

        return await client.GetFromJsonAsync<SimklShow>(
            $"tv/{simklId}?app-name=episode-tracker&app-version=1.0",
            cancellationToken);
    }
}