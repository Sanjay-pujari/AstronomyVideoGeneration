using System.Net.Http.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;
namespace Astronomy.MediaFactory.AstroData.Clients;
public sealed class NasaNeoWsClient
{
    private readonly HttpClient _httpClient; private readonly AstronomyApiOptions _options;
    public NasaNeoWsClient(HttpClient httpClient, IOptions<AstronomyApiOptions> options) { _httpClient = httpClient; _options = options.Value; }
    public async Task<object?> GetFeedAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        var url = $"{_options.NasaBaseUrl.TrimEnd('/')}/neo/rest/v1/feed?api_key={_options.NasaApiKey}&start_date={startDate:yyyy-MM-dd}&end_date={endDate:yyyy-MM-dd}";
        return await _httpClient.GetFromJsonAsync<object>(url, cancellationToken);
    }
}
