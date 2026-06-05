using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyEventDetectionService(
    MediaFactoryDbContext db,
    IAstronomyVisibilityService visibilityService,
    ILogger<AstronomyEventDetectionService> logger) : IAstronomyEventDetectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> PlanetCodes = new(StringComparer.OrdinalIgnoreCase) { "MERCURY", "VENUS", "MARS", "JUPITER", "SATURN" };
    private static readonly HashSet<string> BrightPlanetCodes = new(StringComparer.OrdinalIgnoreCase) { "VENUS", "JUPITER", "MARS", "SATURN" };
    private static readonly Dictionary<string, decimal> ApproxPlanetMagnitudes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VENUS"] = -4.0m,
        ["JUPITER"] = -2.2m,
        ["MARS"] = -1.0m,
        ["SATURN"] = 0.6m,
        ["MERCURY"] = 0.4m
    };

    public async Task<AstronomyEventDetectionResult> DetectEventsAsync(AstronomyEventDetectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var requestedTypes = request.EventTypes is { Count: > 0 }
            ? request.EventTypes.Where(t => !string.IsNullOrWhiteSpace(t)).Select(NormalizeEventType).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "PLANET_GROUPING",
                "PLANET_CONJUNCTION",
                "MOON_SPECIAL",
                "BRIGHT_PLANET_VISIBILITY"
            };

        logger.LogInformation("Astronomy event detection started for {RegionId} ({LocationName}) from {StartUtc} to {EndUtc}. DryRun={DryRun}", request.RegionId, request.LocationName, request.StartUtc, request.EndUtc, request.DryRun);

        var warnings = new List<string>();
        var detected = new List<DetectedAstronomyEventDto>();
        var zone = ResolveZone(request.Timezone, warnings);
        var startLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(request.StartUtc, zone).DateTime);
        var endLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(request.EndUtc, zone).DateTime);

        for (var date = startLocalDate; date <= endLocalDate; date = date.AddDays(1))
        {
            var visibility = await visibilityService.CalculateVisibilityAsync(new AstronomyVisibilityRequest(
                request.RegionId,
                request.LocationName,
                request.Latitude,
                request.Longitude,
                request.Timezone,
                date,
                null), cancellationToken);
            warnings.AddRange(visibility.Warnings.Select(w => $"{date:yyyy-MM-dd}: {w}"));

            var visiblePlanets = visibility.VisibleObjects
                .Where(o => o.Visible && (PlanetCodes.Contains(o.ObjectCode) || o.ObjectType.Equals("Planet", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(o => o.VisibilityScore)
                .ToList();

            if (requestedTypes.Contains("PLANET_GROUPING") && visiblePlanets.Count >= 3)
                detected.Add(BuildPlanetGrouping(request, visibility, visiblePlanets.Take(4).ToList()));

            if (requestedTypes.Contains("PLANET_CONJUNCTION") && visiblePlanets.Count >= 2)
                detected.Add(BuildPlanetConjunction(request, visibility, visiblePlanets.Take(2).ToList()));

            if (requestedTypes.Contains("MOON_SPECIAL") && IsSpecialMoon(visibility.MoonPhase, visibility.MoonIlluminationPercent))
                detected.Add(BuildMoonSpecial(request, visibility));

            var brightPlanets = visiblePlanets.Where(o => BrightPlanetCodes.Contains(o.ObjectCode) || o.VisibilityScore >= 8).Take(3).ToList();
            if (requestedTypes.Contains("BRIGHT_PLANET_VISIBILITY") && brightPlanets.Count > 0)
                detected.Add(BuildBrightPlanetVisibility(request, visibility, brightPlanets));
        }

        detected = detected
            .OrderByDescending(e => e.ViralPotentialScore)
            .ThenByDescending(e => e.VisibilityScore)
            .ThenBy(e => e.StartUtc)
            .Take(request.MaxEvents ?? int.MaxValue)
            .ToList();

        var saved = request.DryRun ? detected : await SaveAsync(detected, cancellationToken);
        var savedCount = request.DryRun ? 0 : saved.Count(e => e.Id.HasValue);

        logger.LogInformation("Astronomy event detection completed for {RegionId} ({LocationName}). Detected={DetectedCount}, Saved={SavedCount}, DryRun={DryRun}", request.RegionId, request.LocationName, detected.Count, savedCount, request.DryRun);

        return new AstronomyEventDetectionResult(request.RegionId, request.LocationName, request.StartUtc, request.EndUtc, request.DryRun, saved.Count, savedCount, saved, warnings.Distinct().ToArray());
    }

    private async Task<IReadOnlyList<DetectedAstronomyEventDto>> SaveAsync(IReadOnlyList<DetectedAstronomyEventDto> detected, CancellationToken cancellationToken)
    {
        if (detected.Count == 0) return detected;

        var codes = detected.Select(e => e.EventCode).ToArray();
        var existingRows = await db.AstronomyEventIntelligences
            .Include(e => e.Objects)
            .Where(e => codes.Contains(e.EventCode))
            .ToListAsync(cancellationToken);
        var existing = existingRows.ToDictionary(e => e.EventCode, StringComparer.OrdinalIgnoreCase);

        var saved = new List<DetectedAstronomyEventDto>(detected.Count);
        foreach (var dto in detected)
        {
            if (!existing.TryGetValue(dto.EventCode, out var entity))
            {
                entity = new AstronomyEventIntelligence { EventCode = dto.EventCode };
                db.AstronomyEventIntelligences.Add(entity);
            }
            else
            {
                db.AstronomyEventObjects.RemoveRange(entity.Objects);
                entity.Objects.Clear();
                entity.Touch();
            }

            Apply(dto, entity);
            saved.Add(dto with { Id = entity.Id, Objects = entity.Objects.Select(ToDto).ToArray() });
        }

        await db.SaveChangesAsync(cancellationToken);
        return saved;
    }

    private static void Apply(DetectedAstronomyEventDto dto, AstronomyEventIntelligence entity)
    {
        entity.EventType = dto.EventType;
        entity.Title = dto.Title;
        entity.Summary = dto.Summary;
        entity.Description = dto.Description;
        entity.StartUtc = dto.StartUtc;
        entity.PeakUtc = dto.PeakUtc;
        entity.EndUtc = dto.EndUtc;
        entity.RegionId = dto.RegionId;
        entity.LocationName = dto.LocationName;
        entity.TimeZone = dto.TimeZone;
        entity.RecommendedCategory = dto.RecommendedCategory;
        entity.Status = dto.Status;
        entity.VisibilityScore = dto.VisibilityScore;
        entity.RarityScore = dto.RarityScore;
        entity.AudienceInterestScore = dto.StoryScore;
        entity.TimingUrgencyScore = 5.00m;
        entity.ContentOpportunityScore = Clamp((dto.StoryScore + dto.ViralPotentialScore) / 2m);
        entity.ConfidenceScore = dto.ConfidenceScore;
        entity.RawDataJson = dto.RawDataJson;
        entity.RulesAppliedJson = dto.RulesAppliedJson;
        entity.MetadataJson = dto.MetadataJson;
        foreach (var obj in dto.Objects)
        {
            entity.Objects.Add(new AstronomyEventObject
            {
                ObjectName = obj.ObjectName,
                ObjectType = obj.ObjectType,
                ObjectRole = obj.ObjectRole,
                CatalogId = obj.CatalogId,
                Magnitude = obj.Magnitude,
                VisibilityScore = obj.VisibilityScore,
                MetadataJson = obj.MetadataJson
            });
        }
    }

    private static DetectedAstronomyEventDto BuildPlanetGrouping(AstronomyEventDetectionRequest request, AstronomyVisibilityResult visibility, IReadOnlyList<VisibleCelestialObjectResult> planets)
    {
        var names = string.Join(", ", planets.Select(p => p.ObjectName));
        var scores = Score(visibility: 7.5m + planets.Count * 0.5m, rarity: 7.0m, story: 7.5m, viral: 7.0m, confidence: 6.0m);
        return BuildEvent(request, visibility, "PLANET_GROUPING", "Planet", $"Planet grouping over {request.LocationName}", $"Visible planet grouping candidate: {names}.",
            "Multiple naked-eye planets are visible in the same night window. Angular separation scoring is reserved for the next refinement phase.", scores,
            planets.Select((p, i) => ToObjectDto(p, i == 0 ? "Primary" : "Companion")).ToArray(), new { rule = "visible_planets >= 3", angularSeparation = "placeholder" });
    }

    private static DetectedAstronomyEventDto BuildPlanetConjunction(AstronomyEventDetectionRequest request, AstronomyVisibilityResult visibility, IReadOnlyList<VisibleCelestialObjectResult> planets)
    {
        var names = string.Join(" and ", planets.Select(p => p.ObjectName));
        var scores = Score(visibility: 7.0m, rarity: 6.5m, story: 7.0m, viral: 7.0m, confidence: 5.5m);
        return BuildEvent(request, visibility, "PLANET_CONJUNCTION", "Planet", $"Planet conjunction candidate: {names}", $"{names} are visible in the same night window.",
            "Candidate conjunction based on shared visibility window. Exact angular separation can be added when geometry is available.", scores,
            planets.Select((p, i) => ToObjectDto(p, i == 0 ? "Primary" : "Companion")).ToArray(), new { rule = "visible_planets >= 2", angularSeparation = "placeholder" });
    }

    private static DetectedAstronomyEventDto BuildMoonSpecial(AstronomyEventDetectionRequest request, AstronomyVisibilityResult visibility)
    {
        var scores = Score(visibility: visibility.MoonIlluminationPercent >= 90 ? 8.5m : 7.0m, rarity: 5.5m, story: 8.0m, viral: 7.5m, confidence: 7.5m);
        var moon = new DetectedAstronomyEventObjectDto(null, "Moon", "Moon", "Primary", "MOON", null, scores.VisibilityScore, JsonSerializer.Serialize(new { visibility.MoonPhase, visibility.MoonIlluminationPercent }, JsonOptions));
        return BuildEvent(request, visibility, "MOON_SPECIAL", "Moon", $"{visibility.MoonPhase} Moon over {request.LocationName}", $"Moon special candidate: {visibility.MoonPhase} with {visibility.MoonIlluminationPercent:0.#}% illumination.",
            "Moon phase and illumination make this a candidate for astronomy intelligence and content planning.", scores, [moon], new { rule = "special moon phase or illumination threshold" });
    }

    private static DetectedAstronomyEventDto BuildBrightPlanetVisibility(AstronomyEventDetectionRequest request, AstronomyVisibilityResult visibility, IReadOnlyList<VisibleCelestialObjectResult> planets)
    {
        var names = string.Join(", ", planets.Select(p => p.ObjectName));
        var scores = Score(visibility: ClampDecimal((decimal)planets.Average(p => p.VisibilityScore)), rarity: 4.5m + planets.Count, story: 6.5m, viral: 8.0m, confidence: 7.0m);
        return BuildEvent(request, visibility, "BRIGHT_PLANET_VISIBILITY", "Planet", $"Bright planet visibility over {request.LocationName}", $"Bright planet visibility candidate: {names}.",
            "One or more bright naked-eye planets have strong visibility for this location and date.", scores,
            planets.Select((p, i) => ToObjectDto(p, i == 0 ? "Primary" : "Companion")).ToArray(), new { rule = "bright visible planet detected" });
    }

    private static DetectedAstronomyEventDto BuildEvent(AstronomyEventDetectionRequest request, AstronomyVisibilityResult visibility, string eventType, string category, string title, string summary, string description, Scores scores, IReadOnlyList<DetectedAstronomyEventObjectDto> objects, object rule)
    {
        var eventCode = $"{eventType}_{visibility.TargetDate:yyyyMMdd}_{SanitizeCodePart(request.RegionId)}";
        var raw = new { visibility.RegionId, visibility.LocationName, visibility.TargetDate, visibility.MoonPhase, visibility.MoonIlluminationPercent, visibility.BestViewingStartUtc, visibility.BestViewingEndUtc };
        var metadata = new { request.Latitude, request.Longitude, request.Timezone, detectionVersion = "Phase7B-foundation" };
        return new DetectedAstronomyEventDto(null, eventCode, eventType, title, summary, description,
            AsUtcOffset(visibility.BestViewingStartUtc), AsUtcOffset(visibility.BestViewingStartUtc), AsUtcOffset(visibility.BestViewingEndUtc),
            request.RegionId, request.LocationName, request.Timezone, category, "Candidate", scores.VisibilityScore, scores.RarityScore, scores.StoryScore, scores.ViralPotentialScore, scores.ConfidenceScore,
            objects, JsonSerializer.Serialize(raw, JsonOptions), JsonSerializer.Serialize(rule, JsonOptions), JsonSerializer.Serialize(metadata, JsonOptions));
    }

    private static DetectedAstronomyEventObjectDto ToObjectDto(VisibleCelestialObjectResult result, string role)
    {
        var magnitude = ApproxPlanetMagnitudes.GetValueOrDefault(result.ObjectCode, 1.0m);
        var metadata = JsonSerializer.Serialize(new { result.ObjectCode, result.BestViewingStartUtc, result.BestViewingEndUtc, result.Reason }, JsonOptions);
        return new DetectedAstronomyEventObjectDto(null, result.ObjectName, result.ObjectType, role, result.ObjectCode, magnitude, ClampDecimal((decimal)result.VisibilityScore), metadata);
    }

    private static DetectedAstronomyEventObjectDto ToDto(AstronomyEventObject obj) => new(obj.Id, obj.ObjectName, obj.ObjectType, obj.ObjectRole, obj.CatalogId, obj.Magnitude, obj.VisibilityScore, obj.MetadataJson);

    private static DateTimeOffset AsUtcOffset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static bool IsSpecialMoon(string phase, double illumination) =>
        phase.Contains("Full", StringComparison.OrdinalIgnoreCase)
        || phase.Contains("New", StringComparison.OrdinalIgnoreCase)
        || phase.Contains("Quarter", StringComparison.OrdinalIgnoreCase)
        || illumination is >= 90 or <= 5;

    private static Scores Score(decimal visibility, decimal rarity, decimal story, decimal viral, decimal confidence) =>
        new(Clamp(visibility), Clamp(rarity), Clamp(story), Clamp(viral), Clamp(confidence));

    private static decimal Clamp(decimal score) => Math.Clamp(Math.Round(score, 2), 0m, 10m);
    private static decimal ClampDecimal(decimal score) => Clamp(score);

    private static string NormalizeEventType(string eventType) => eventType.Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');

    private static string SanitizeCodePart(string value)
    {
        var chars = value.Trim().ToUpperInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "REGION" : sanitized;
    }

    private static TimeZoneInfo ResolveZone(string timezone, List<string> warnings)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { warnings.Add($"timezone '{timezone}' not found; defaulted to UTC."); return TimeZoneInfo.Utc; }
    }

    private static void Validate(AstronomyEventDetectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("regionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.LocationName)) throw new ArgumentException("locationName is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Timezone)) throw new ArgumentException("timezone is required.", nameof(request));
        if (request.Latitude is < -90 or > 90) throw new ArgumentOutOfRangeException(nameof(request.Latitude));
        if (request.Longitude is < -180 or > 180) throw new ArgumentOutOfRangeException(nameof(request.Longitude));
        if (request.EndUtc < request.StartUtc) throw new ArgumentException("endUtc must be greater than or equal to startUtc.", nameof(request));
        if (request.MaxEvents is <= 0) throw new ArgumentOutOfRangeException(nameof(request.MaxEvents), "maxEvents must be greater than zero when provided.");
    }

    private sealed record Scores(decimal VisibilityScore, decimal RarityScore, decimal StoryScore, decimal ViralPotentialScore, decimal ConfidenceScore);
}
