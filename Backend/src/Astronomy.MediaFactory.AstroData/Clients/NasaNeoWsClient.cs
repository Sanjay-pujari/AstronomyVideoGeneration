using System.Net;
using System.Net.Http.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace Astronomy.MediaFactory.AstroData.Clients;
public sealed class NasaNeoWsClient
{
    private readonly HttpClient _httpClient; private readonly AstronomyApiOptions _options;
    private readonly ILogger<NasaNeoWsClient> _logger;

    public NasaNeoWsClient(HttpClient httpClient, IOptions<AstronomyApiOptions> options, ILogger<NasaNeoWsClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }
    public async Task<object?> GetFeedAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        var url = $"{_options.NasaBaseUrl.TrimEnd('/')}/neo/rest/v1/feed?api_key={_options.NasaApiKey}&start_date={startDate:yyyy-MM-dd}&end_date={endDate:yyyy-MM-dd}";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.StatusCode == (HttpStatusCode)429)
            {
                _logger.LogWarning("NASA NeoWs rate-limited (429) for {StartDate} - {EndDate}.", startDate, endDate);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("NASA NeoWs returned {StatusCode} for {StartDate} - {EndDate}.", (int)response.StatusCode, startDate, endDate);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<object>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NASA NeoWs call failed for {StartDate} - {EndDate}.", startDate, endDate);
            return null;
        }
    }
}
