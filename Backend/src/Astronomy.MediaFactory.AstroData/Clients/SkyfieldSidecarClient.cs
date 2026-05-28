using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.AstroData.Clients;

public interface ISkyfieldSidecarClient
{
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken);
    Task<SkyfieldDailySkyResponse?> GetDailySkyAsync(SkyfieldDailySkyRequest request, CancellationToken cancellationToken);
    Task<SkyfieldNightPlanResponse?> GetNightVisibilityPlanAsync(SkyfieldNightPlanRequest request, CancellationToken cancellationToken);
    Task<WeeklySkyForecastSkyfieldResponse?> GetWeeklySkyForecastAsync(WeeklySkyForecastSkyfieldRequest request, CancellationToken cancellationToken);
}

public sealed class SkyfieldSidecarClient : ISkyfieldSidecarClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static bool _loggedDailyRawSample;

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

            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!_loggedDailyRawSample && request.Date == "2026-05-25")
            {
                _logger.LogInformation("SKYFIELD_DAILY_RAW_RESPONSE_SAMPLE date={Date} body={Body}", request.Date, rawBody);
                _loggedDailyRawSample = true;
            }

            var payload = JsonSerializer.Deserialize<SkyfieldDailySkyResponse>(rawBody, JsonOptions);
            if (payload is null)
            {
                _logger.LogWarning("Skyfield sidecar returned an empty payload for {Date} at {LocationName}.", request.Date, request.LocationName);
                return null;
            }

            PopulateGeometryEventsFromRawJson(payload, rawBody);

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

    private static void PopulateGeometryEventsFromRawJson(SkyfieldDailySkyResponse payload, string rawBody)
    {
        if (payload.Events.Any(e => e.TimeUtc.HasValue && e.AltitudeDegrees.HasValue && e.AzimuthDegrees.HasValue))
        {
            return;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(rawBody);
        }
        catch
        {
            return;
        }

        var candidateArrays = new[] { "events", "visibleObjects", "visible_objects", "geometry", "geometryRecords", "records", "observations" };
        foreach (var key in candidateArrays)
        {
            if (node?[key] is not JsonArray arr)
                continue;

            foreach (var item in arr.OfType<JsonObject>())
            {
                var objectName = ReadString(item, "objectName", "object_name", "objectCode", "object_code");
                var timeText = ReadString(item, "timeUtc", "time_utc", "observationUtc", "observation_utc", "utcTime", "utc_time", "bestUtcTime", "best_utc_time");
                var altitude = ReadDouble(item, "altitudeDegrees", "altitude_degrees", "altitude", "maxAltitudeDegrees", "max_altitude_degrees");
                var azimuth = ReadDouble(item, "azimuthDegrees", "azimuth_degrees", "azimuth", "bestViewingAzimuthDegrees", "best_viewing_azimuth_degrees");
                var magnitude = ReadDouble(item, "magnitude");
                var eventTime = DateTime.TryParse(timeText, out var parsedTime) ? parsedTime : (DateTime?)null;
                if (string.IsNullOrWhiteSpace(objectName) || !eventTime.HasValue || !altitude.HasValue || !azimuth.HasValue)
                    continue;

                payload.Events.Add(new SkyfieldDailySkyEvent
                {
                    Category = ReadString(item, "category", "objectType", "object_type") ?? "geometry",
                    ObjectName = objectName,
                    VisibilityWindow = ReadString(item, "visibilityWindow", "visibility_window") ?? "Unknown",
                    Direction = ReadString(item, "direction", "directionLabel", "direction_label") ?? "Unknown",
                    ObservationTool = ReadString(item, "observationTool", "observation_tool") ?? "Unknown",
                    Details = ReadString(item, "details", "reason") ?? "Extracted from Skyfield geometry record.",
                    TimeUtc = eventTime,
                    AltitudeDegrees = altitude,
                    AzimuthDegrees = azimuth,
                    Magnitude = magnitude
                });
            }

            if (payload.Events.Any(e => e.TimeUtc.HasValue && e.AltitudeDegrees.HasValue && e.AzimuthDegrees.HasValue))
                return;
        }
    }

    private static string? ReadString(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetPropertyValue(key, out var value))
            {
                var text = value?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        return null;
    }

    private static double? ReadDouble(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!obj.TryGetPropertyValue(key, out var value) || value is null)
                continue;
            if (value.TryGetValue<double>(out var asDouble))
                return asDouble;
            if (value.TryGetValue<string>(out var asText) && double.TryParse(asText, out var parsed))
                return parsed;
        }

        return null;
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skyfield sidecar health check failed.");
            return false;
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

    public async Task<WeeklySkyForecastSkyfieldResponse?> GetWeeklySkyForecastAsync(WeeklySkyForecastSkyfieldRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var healthy = await CheckHealthAsync(cancellationToken);
            if (!healthy)
            {
                _logger.LogWarning("Skyfield sidecar health check did not return success before weekly-sky request.");
                return null;
            }

            var response = await _httpClient.PostAsJsonAsync("/forecast/weekly-sky", request, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Skyfield weekly-sky returned non-success status code {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<WeeklySkyForecastSkyfieldResponse>(JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Skyfield sidecar weekly-sky call failed.");
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

public sealed class WeeklySkyForecastSkyfieldRequest
{
    public string RegionId { get; set; } = "";
    public string LocationName { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string WeekStartDate { get; set; } = "";
    public int Days { get; set; } = 7;
    public string Language { get; set; } = "en";
    public List<string>? PreferredObjectCodes { get; set; } = new();
    public bool IncludeMoonPhases { get; set; } = true;
    public bool IncludePlanets { get; set; } = true;
    public bool IncludeDeepSkyObjects { get; set; } = true;
    public bool IncludeMeteorShowers { get; set; } = true;
    public bool IncludeConjunctions { get; set; } = true;
    public bool IncludeBestViewingWindows { get; set; } = true;
}

public sealed class WeeklySkyForecastSkyfieldResponse
{
    public bool Success { get; set; }
    public string RegionId { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string Timezone { get; set; } = "UTC";
    public string WeekStartDate { get; set; } = "";
    public string WeekEndDate { get; set; } = "";
    public List<DailySkyForecastItem> Days { get; set; } = new();
    public List<WeeklyHighlightItem> WeeklyHighlights { get; set; } = new();
    public List<RecommendedObservationNight> RecommendedNights { get; set; } = new();
    public VisibleObjectForecastItem? BestPlanetOfWeek { get; set; }
    public RecommendedObservationNight? BestMoonNight { get; set; }
    public RecommendedObservationNight? BestPhotographyNight { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public sealed class DailySkyForecastItem
{
    public string Date { get; set; } = "";
    public DateTime SunsetUtc { get; set; }
    public DateTime SunriseUtc { get; set; }
    public string MoonPhase { get; set; } = "";
    public double MoonIlluminationPercent { get; set; }
    public DateTime? MoonRiseUtc { get; set; }
    public DateTime? MoonSetUtc { get; set; }
    public List<VisibleObjectForecastItem> VisibleObjects { get; set; } = new();
    public List<AstronomyEventForecastItem> Events { get; set; } = new();
    public DateTime BestViewingStartUtc { get; set; }
    public DateTime BestViewingEndUtc { get; set; }
    public double OverallViewingScore { get; set; }
    public string ViewingSummary { get; set; } = "";
}

public sealed class VisibleObjectForecastItem
{
    public string ObjectCode { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public bool Visible { get; set; }
    public DateTime? RiseUtc { get; set; }
    public DateTime? SetUtc { get; set; }
    public DateTime? TransitUtc { get; set; }
    public double? MaxAltitudeDegrees { get; set; }
    public double? BestViewingAzimuthDegrees { get; set; }
    public DateTime? BestViewingTimeUtc { get; set; }
    public double VisibilityScore { get; set; }
    public double PhotographyScore { get; set; }
    public string ViewingDirection { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class AstronomyEventForecastItem
{
    public string EventType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime EventTimeUtc { get; set; }
    public double ImportanceScore { get; set; }
    public double ViralityScore { get; set; }
    public string? PrimaryObjectCode { get; set; }
    public string ViewingDirection { get; set; } = "";
    public string ViewingTip { get; set; } = "";
}

public sealed class WeeklyHighlightItem
{
    public int Order { get; set; }
    public string HighlightType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Date { get; set; } = "";
    public DateTime? BestTimeUtc { get; set; }
    public string? ObjectCode { get; set; }
    public double Score { get; set; }
    public string SuggestedSceneType { get; set; } = "";
}

public sealed class RecommendedObservationNight
{
    public string Date { get; set; } = "";
    public double Score { get; set; }
    public string Reason { get; set; } = "";
    public List<string> BestObjects { get; set; } = new();
    public DateTime BestStartUtc { get; set; }
    public DateTime BestEndUtc { get; set; }
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
    public DateTime? TimeUtc { get; set; }
    public double? AltitudeDegrees { get; set; }
    public double? AzimuthDegrees { get; set; }
    public double? Magnitude { get; set; }

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
    private static bool _loggedDailyRawSample;
