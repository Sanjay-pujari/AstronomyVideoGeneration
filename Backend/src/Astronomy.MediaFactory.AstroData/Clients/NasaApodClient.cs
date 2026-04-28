using System.Net;
using System.Net.Http.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace Astronomy.MediaFactory.AstroData.Clients;

public sealed class NasaApodClient
{
    private readonly HttpClient _httpClient;
    private readonly AstronomyApiOptions _options;
    private readonly ILogger<NasaApodClient> _logger;

    public NasaApodClient(HttpClient httpClient, IOptions<AstronomyApiOptions> options, ILogger<NasaApodClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }
    public async Task<NasaApodResponse?> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var url = $"{_options.NasaBaseUrl.TrimEnd('/')}/planetary/apod?api_key={_options.NasaApiKey}&date={date:yyyy-MM-dd}";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.StatusCode == (HttpStatusCode)429)
            {
                _logger.LogWarning("NASA APOD rate-limited (429) for {Date}. Consider setting a real NASA API key or retry later.", date);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("NASA APOD returned {StatusCode} for {Date}.", (int)response.StatusCode, date);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<NasaApodResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NASA APOD call failed for {Date}.", date);
            return null;
        }
    }
}
public sealed class NasaApodResponse { public string? Title { get; set; } public string? Explanation { get; set; } public string? Url { get; set; } public string? Hdurl { get; set; } public string? Media_type { get; set; } }
