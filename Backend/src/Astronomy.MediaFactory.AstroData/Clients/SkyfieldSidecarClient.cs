using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.AstroData.Clients;

public interface ISkyfieldSidecarClient
{
    Task<SkyfieldDailySkyResponse?> GetDailySkyAsync(SkyfieldDailySkyRequest request, CancellationToken cancellationToken);
}

public sealed class SkyfieldSidecarClient : ISkyfieldSidecarClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SkyfieldSidecarClient> _logger;

    public SkyfieldSidecarClient(HttpClient httpClient, ILogger<SkyfieldSidecarClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<SkyfieldDailySkyResponse?> GetDailySkyAsync(SkyfieldDailySkyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/ephemeris/daily-sky", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Skyfield sidecar returned non-success status code {StatusCode} for {Date} at {LocationName}.", (int)response.StatusCode, request.Date, request.LocationName);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<SkyfieldDailySkyResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Skyfield sidecar call failed for {Date} at {LocationName}.", request.Date, request.LocationName);
            return null;
        }
    }
}

public sealed class SkyfieldDailySkyRequest
{
    public string Date { get; set; } = "";
    public string LocationName { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Timezone { get; set; } = "UTC";
}

public sealed class SkyfieldDailySkyResponse
{
    public string Date { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string Timezone { get; set; } = "UTC";
    public List<SkyfieldDailySkyEvent> Events { get; set; } = new();
    public List<SkyfieldVisualIdea> VisualIdeas { get; set; } = new();
}

public sealed class SkyfieldDailySkyEvent
{
    public string Category { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public string VisibilityWindow { get; set; } = "";
    public string Direction { get; set; } = "";
    public string ObservationTool { get; set; } = "";
    public string Details { get; set; } = "";
}

public sealed class SkyfieldVisualIdea
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}
