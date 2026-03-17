using System.Net.Http.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;
namespace Astronomy.MediaFactory.AstroData.Clients;

public sealed class NasaApodClient
{
    private readonly HttpClient _httpClient;
    private readonly AstronomyApiOptions _options;
    public NasaApodClient(HttpClient httpClient, IOptions<AstronomyApiOptions> options) { _httpClient = httpClient; _options = options.Value; }
    public async Task<NasaApodResponse?> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var url = $"{_options.NasaBaseUrl.TrimEnd('/')}/planetary/apod?api_key={_options.NasaApiKey}&date={date:yyyy-MM-dd}";
        return await _httpClient.GetFromJsonAsync<NasaApodResponse>(url, cancellationToken);
    }
}
public sealed class NasaApodResponse { public string? Title { get; set; } public string? Explanation { get; set; } public string? Url { get; set; } public string? Hdurl { get; set; } public string? Media_type { get; set; } }
