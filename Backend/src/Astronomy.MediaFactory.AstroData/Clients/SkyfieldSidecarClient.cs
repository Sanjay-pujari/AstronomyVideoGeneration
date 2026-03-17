using System.Net.Http.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;
namespace Astronomy.MediaFactory.AstroData.Clients;

public sealed class SkyfieldSidecarClient
{
    private readonly HttpClient _httpClient; private readonly AstronomyApiOptions _options;
    public SkyfieldSidecarClient(HttpClient httpClient, IOptions<AstronomyApiOptions> options) { _httpClient = httpClient; _options = options.Value; }
    public async Task<SkyfieldVisibilityResponse?> GetVisibilityAsync(SkyfieldVisibilityRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_options.SkyfieldServiceUrl.TrimEnd('/')}/api/visibility", request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SkyfieldVisibilityResponse>(cancellationToken: cancellationToken);
    }
}
public sealed class SkyfieldVisibilityRequest { public string Date { get; set; } = ""; public double Latitude { get; set; } public double Longitude { get; set; } public double ElevationM { get; set; } public List<string> Targets { get; set; } = new(); }
public sealed class SkyfieldVisibilityResponse { public string Date { get; set; } = ""; public List<SkyfieldVisibilityItem> Items { get; set; } = new(); }
public sealed class SkyfieldVisibilityItem { public string Target { get; set; } = ""; public string BestTimeLocal { get; set; } = ""; public double AltitudeDegrees { get; set; } public double AzimuthDegrees { get; set; } public string Visibility { get; set; } = ""; public string Notes { get; set; } = ""; }
