using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class SkyfieldVisibilityClient(HttpClient httpClient, IOptions<SkyfieldSidecarOptions> options, ILogger<SkyfieldVisibilityClient> logger) : ISkyfieldVisibilityClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SkyfieldVisibilityResponse> CalculateAsync(SkyfieldVisibilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var cfg = options.Value;
        if (!cfg.Enabled)
        {
            return new SkyfieldVisibilityResponse(false, null, null, null, null, [], ["Skyfield disabled by configuration; using fallback visibility approximation."], "Skyfield disabled by configuration.");
        }

        if (!Uri.TryCreate(cfg.BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("SkyfieldSidecar:BaseUrl must be an absolute URI when SkyfieldSidecar:Enabled=true.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, cfg.TimeoutSeconds)));

        try
        {
            IReadOnlyList<SkyfieldVisibilityCandidateRequest> candidates = request.Candidates ?? [];
            var candidateMap = candidates
                .GroupBy(candidate => ResolveSidecarObjectName(candidate), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var sidecarRequest = BuildNightPlanRequest(request, candidates);

            using var response = await httpClient.PostAsJsonAsync("/visibility/night-plan", sidecarRequest, JsonOptions, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await ReadResponseBodyAsync(response, timeoutCts.Token);
                var errorMessage = BuildSidecarStatusError(response.StatusCode, responseBody);
                if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    logger.LogWarning("Skyfield sidecar returned validation status {StatusCode} for {Date} at {LocationName}. Response body: {ResponseBody}", (int)response.StatusCode, sidecarRequest.Date, sidecarRequest.LocationName, responseBody);
                }
                else
                {
                    logger.LogWarning("Skyfield sidecar returned non-success status {StatusCode} for {Date} at {LocationName}. Response body: {ResponseBody}", (int)response.StatusCode, sidecarRequest.Date, sidecarRequest.LocationName, responseBody);
                }

                return new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], errorMessage);
            }

            var payload = await response.Content.ReadFromJsonAsync<SkyfieldNightPlanResponse>(JsonOptions, timeoutCts.Token);
            if (payload is null)
                return new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], "Skyfield sidecar returned empty payload.");

            if (!TryParseUtc(payload.SunsetLocal, out var sunsetUtc) || !TryParseUtc(payload.SunriseLocal, out var sunriseUtc))
            {
                return new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], "Skyfield sidecar payload was invalid: sunsetLocal/sunriseLocal could not be parsed.");
            }

            if (!TryParseUtc(payload.NightWindowStartUtc, out var nightStartUtc) || !TryParseUtc(payload.NightWindowEndUtc, out var nightEndUtc))
            {
                return new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], "Skyfield sidecar payload was invalid: nightWindowStartUtc/nightWindowEndUtc could not be parsed.");
            }

            var (moonPhase, moonIlluminationPercent) = ApproxMoonPhase(request.TargetDate);
            var objects = MapObjects(payload, candidateMap, nightStartUtc, nightEndUtc);
            return new SkyfieldVisibilityResponse(true, sunsetUtc, sunriseUtc, moonPhase, moonIlluminationPercent, objects, [], null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], "Skyfield sidecar timed out.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Skyfield visibility request failed.");
            return new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], ex.Message);
        }
    }

    private static SkyfieldNightPlanRequest BuildNightPlanRequest(SkyfieldVisibilityRequest request, IReadOnlyList<SkyfieldVisibilityCandidateRequest> candidates) => new()
    {
        Date = request.TargetDate.ToString("yyyy-MM-dd"),
        LocationName = request.LocationName,
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        Timezone = request.Timezone,
        MinimumAltitudeDegrees = 10,
        StepMinutes = 15,
        Candidates = candidates
            .Select(candidate => new SkyfieldVisibilityCandidate
            {
                ObjectName = ResolveSidecarObjectName(candidate),
                ObjectType = candidate.ObjectType
            })
            .ToList()
    };

    private static string ResolveSidecarObjectName(SkyfieldVisibilityCandidateRequest candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ObjectName)) return candidate.ObjectName;
        return candidate.ObjectCode;
    }

    private static IReadOnlyList<SkyfieldVisibilityObjectResult> MapObjects(
        SkyfieldNightPlanResponse payload,
        IReadOnlyDictionary<string, SkyfieldVisibilityCandidateRequest> candidateMap,
        DateTime nightStartUtc,
        DateTime nightEndUtc)
    {
        List<SkyfieldObjectVisibility> visibleObjects = payload.VisibleObjects ?? [];
        List<SkyfieldObjectVisibility> notVisibleObjects = payload.NotVisibleObjects ?? [];
        var allObjects = visibleObjects
            .Concat(notVisibleObjects)
            .ToList();
        return allObjects.Select(obj => MapObject(obj, candidateMap, nightStartUtc, nightEndUtc)).ToArray();
    }

    private static SkyfieldVisibilityObjectResult MapObject(
        SkyfieldObjectVisibility obj,
        IReadOnlyDictionary<string, SkyfieldVisibilityCandidateRequest> candidateMap,
        DateTime nightStartUtc,
        DateTime nightEndUtc)
    {
        var objectCode = candidateMap.TryGetValue(obj.ObjectName, out var candidate)
            ? candidate.ObjectCode
            : obj.ObjectName;
        var bestUtc = TryParseUtc(obj.BestUtcTime, out var parsedBestUtc) ? parsedBestUtc : (DateTime?)null;
        List<SkyfieldVisibilitySample> samples = obj.Samples ?? [];
        var maxAltitudeDegrees = obj.AltitudeDegrees ?? samples.Select(sample => (double?)sample.AltitudeDegrees).Max() ?? 0;
        var altitudeScore = AltitudeScoreFor(maxAltitudeDegrees);
        var visibleStartUtc = obj.IsVisible ? bestUtc ?? nightStartUtc : (DateTime?)null;
        var visibleEndUtc = obj.IsVisible ? bestUtc?.AddMinutes(45) ?? nightEndUtc : (DateTime?)null;
        if (visibleEndUtc.HasValue && visibleEndUtc.Value > nightEndUtc) visibleEndUtc = nightEndUtc;
        if (visibleStartUtc.HasValue && visibleEndUtc.HasValue && visibleEndUtc.Value < visibleStartUtc.Value) visibleEndUtc = visibleStartUtc.Value;

        return new SkyfieldVisibilityObjectResult(
            objectCode,
            obj.IsVisible,
            null,
            null,
            bestUtc,
            maxAltitudeDegrees,
            visibleStartUtc,
            visibleEndUtc,
            altitudeScore,
            obj.VisibilityReason);
    }

    private static async Task<string> ReadResponseBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? "<empty>" : body;
    }

    private static string BuildSidecarStatusError(HttpStatusCode statusCode, string responseBody)
    {
        var status = (int)statusCode;
        return statusCode == HttpStatusCode.UnprocessableEntity
            ? $"Skyfield sidecar returned status {status}. Validation details: {responseBody}"
            : $"Skyfield sidecar returned status {status}. Response body: {responseBody}";
    }

    private static bool TryParseUtc(string? value, out DateTime utc)
    {
        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            utc = parsed.UtcDateTime;
            return true;
        }

        utc = default;
        return false;
    }

    private static double AltitudeScoreFor(double maxAltitude) => maxAltitude switch { >= 60 => 10, >= 45 => 8, >= 30 => 6, >= 15 => 4, _ => 2 };

    private static (string, double) ApproxMoonPhase(DateOnly date)
    {
        const double synodicMonth = 29.53058867;
        var knownNewMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
        var days = (date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - knownNewMoon).TotalDays;
        var age = ((days % synodicMonth) + synodicMonth) % synodicMonth;
        var illumination = (1 - Math.Cos(2 * Math.PI * age / synodicMonth)) / 2 * 100;
        var phase = age switch
        {
            < 1.84566 => "New Moon",
            < 5.53699 => "Waxing Crescent",
            < 9.22831 => "First Quarter",
            < 12.91963 => "Waxing Gibbous",
            < 16.61096 => "Full Moon",
            < 20.30228 => "Waning Gibbous",
            < 23.99361 => "Last Quarter",
            < 27.68493 => "Waning Crescent",
            _ => "New Moon"
        };
        return (phase, Math.Round(illumination, 2));
    }
}
