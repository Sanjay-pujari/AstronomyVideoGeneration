using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.AstroData.Clients;

public interface ISkyfieldSidecarClient
{
    Task<SkyfieldDailySkyResponse?> GetDailySkyAsync(SkyfieldDailySkyRequest request, CancellationToken cancellationToken);
    Task<SkyfieldNightPlanResponse?> GetNightVisibilityPlanAsync(SkyfieldNightPlanRequest request, CancellationToken cancellationToken);
}

public sealed class SkyfieldSidecarClient : ISkyfieldSidecarClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<SkyfieldSidecarClient> _logger;

    public SkyfieldSidecarClient(HttpClient httpClient, ILogger<SkyfieldSidecarClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<SkyfieldDailySkyResponse?> GetDailySkyAsync(SkyfieldDailySkyRequest request, CancellationToken cancellationToken)
    {
        if (!request.TryValidate(out var requestValidationError))
        {
            _logger.LogWarning("Skyfield sidecar request rejected before send: {ValidationError}", requestValidationError);
            return null;
        }

        try
        {
            // Use Web defaults (camelCase) to match the Python FastAPI sidecar contract.
            var response = await _httpClient.PostAsJsonAsync("/ephemeris/daily-sky", request, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Skyfield sidecar returned non-success status code {StatusCode} for {Date} at {LocationName}.", (int)response.StatusCode, request.Date, request.LocationName);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<SkyfieldDailySkyResponse>(JsonOptions, cancellationToken);
            if (payload is null)
            {
                _logger.LogWarning("Skyfield sidecar returned an empty payload for {Date} at {LocationName}.", request.Date, request.LocationName);
                return null;
            }

            if (!payload.TryNormalizeAndValidate(out var responseValidationError))
            {
                _logger.LogWarning("Skyfield sidecar payload failed contract validation for {Date} at {LocationName}: {ValidationError}", request.Date, request.LocationName, responseValidationError);
                return null;
            }

            return payload;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Skyfield sidecar call failed for {Date} at {LocationName}.", request.Date, request.LocationName);
            return null;
        }
    }

    public async Task<SkyfieldNightPlanResponse?> GetNightVisibilityPlanAsync(SkyfieldNightPlanRequest request, CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"[Skyfield /visibility/night-plan] Request: {JsonSerializer.Serialize(request, JsonOptions)}");
            var response = await _httpClient.PostAsJsonAsync("/visibility/night-plan", request, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Skyfield night-plan returned non-success status code {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<SkyfieldNightPlanResponse>(JsonOptions, cancellationToken);
            Console.WriteLine($"[Skyfield /visibility/night-plan] Response: {JsonSerializer.Serialize(payload, JsonOptions)}");
            return payload;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Skyfield sidecar night-plan call failed.");
            return null;
        }
    }
}

public sealed class SkyfieldNightPlanRequest
{
    public string Date { get; set; } = "";
    public string LocationName { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string? NightWindowStartUtc { get; set; }
    public string? NightWindowEndUtc { get; set; }
    public double MinimumAltitudeDegrees { get; set; } = 10;
    public int StepMinutes { get; set; } = 15;
    public List<SkyfieldVisibilityCandidate> Candidates { get; set; } = new();
}

public sealed class SkyfieldVisibilityCandidate
{
    public string ObjectName { get; set; } = "";
    public string ObjectType { get; set; } = "";
}

public sealed class SkyfieldVisibilitySample
{
    public string LocalTime { get; set; } = "";
    public string UtcTime { get; set; } = "";
    public double AltitudeDegrees { get; set; }
    public double AzimuthDegrees { get; set; }
    public string DirectionLabel { get; set; } = "N";
    public bool IsVisibleCandidate { get; set; }
}

public sealed class SkyfieldObjectVisibility
{
    public string ObjectName { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public bool IsVisible { get; set; }
    public string VisibilityReason { get; set; } = "";
    public string? BestLocalTime { get; set; }
    public string? BestUtcTime { get; set; }
    public double? AltitudeDegrees { get; set; }
    public double? AzimuthDegrees { get; set; }
    public string? DirectionLabel { get; set; }
    public List<SkyfieldVisibilitySample> Samples { get; set; } = new();
}

public sealed class SkyfieldNightPlanResponse
{
    public string LocationName { get; set; } = "";
    public string Timezone { get; set; } = "UTC";
    public string TargetDate { get; set; } = "";
    public string SunsetLocal { get; set; } = "";
    public string SunriseLocal { get; set; } = "";
    public string NightWindowStartUtc { get; set; } = "";
    public string NightWindowEndUtc { get; set; } = "";
    public List<SkyfieldObjectVisibility> VisibleObjects { get; set; } = new();
    public List<SkyfieldObjectVisibility> NotVisibleObjects { get; set; } = new();
}

public sealed class SkyfieldDailySkyRequest
{
    public string Date { get; set; } = "";
    public string LocationName { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Timezone { get; set; } = "UTC";

    public bool TryValidate(out string validationError)
    {
        if (!DateOnly.TryParse(Date, out _))
        {
            validationError = "Date must use yyyy-MM-dd format.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(LocationName))
        {
            validationError = "LocationName is required.";
            return false;
        }

        if (Latitude is < -90 or > 90)
        {
            validationError = "Latitude must be between -90 and 90.";
            return false;
        }

        if (Longitude is < -180 or > 180)
        {
            validationError = "Longitude must be between -180 and 180.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Timezone))
        {
            validationError = "Timezone is required.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}

public sealed class SkyfieldDailySkyResponse
{
    public string Date { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string Timezone { get; set; } = "UTC";
    public List<SkyfieldDailySkyEvent> Events { get; set; } = new();
    public List<SkyfieldVisualIdea> VisualIdeas { get; set; } = new();

    public bool TryNormalizeAndValidate(out string validationError)
    {
        Date = Date?.Trim() ?? string.Empty;
        LocationName = LocationName?.Trim() ?? string.Empty;
        Timezone = Timezone?.Trim() ?? string.Empty;

        if (!DateOnly.TryParse(Date, out _))
        {
            validationError = "Response date must use yyyy-MM-dd format.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(LocationName))
        {
            validationError = "Response locationName is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Timezone))
        {
            validationError = "Response timezone is required.";
            return false;
        }

        Events ??= new();
        VisualIdeas ??= new();

        foreach (var item in Events)
        {
            item.Normalize();
            if (!item.IsValid())
            {
                validationError = "Response contains an event with missing required fields.";
                return false;
            }
        }

        foreach (var item in VisualIdeas)
        {
            item.Normalize();
            if (!item.IsValid())
            {
                validationError = "Response contains a visual idea with missing required fields.";
                return false;
            }
        }

        validationError = string.Empty;
        return true;
    }
}

public sealed class SkyfieldDailySkyEvent
{
    public string Category { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public string VisibilityWindow { get; set; } = "";
    public string Direction { get; set; } = "";
    public string ObservationTool { get; set; } = "";
    public string Details { get; set; } = "";

    public void Normalize()
    {
        Category = Category?.Trim() ?? string.Empty;
        ObjectName = ObjectName?.Trim() ?? string.Empty;
        VisibilityWindow = VisibilityWindow?.Trim() ?? string.Empty;
        Direction = Direction?.Trim() ?? string.Empty;
        ObservationTool = ObservationTool?.Trim() ?? string.Empty;
        Details = Details?.Trim() ?? string.Empty;
    }

    public bool IsValid()
        => !string.IsNullOrWhiteSpace(Category)
           && !string.IsNullOrWhiteSpace(ObjectName)
           && !string.IsNullOrWhiteSpace(VisibilityWindow)
           && !string.IsNullOrWhiteSpace(Direction)
           && !string.IsNullOrWhiteSpace(ObservationTool)
           && !string.IsNullOrWhiteSpace(Details);
}

public sealed class SkyfieldVisualIdea
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    public void Normalize()
    {
        Title = Title?.Trim() ?? string.Empty;
        Description = Description?.Trim() ?? string.Empty;
    }

    public bool IsValid() => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Description);
}
