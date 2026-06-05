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
    internal const double MinimumVisibleAltitudeDegrees = 10d;
    internal const double ConjunctionMaxSeparationDegrees = 5d;
    internal const double GroupingMaxSeparationDegrees = 35d;
    private static readonly HashSet<string> PlanetCodes = new(StringComparer.OrdinalIgnoreCase) { "MERCURY", "VENUS", "MARS", "JUPITER", "SATURN" };
    private static readonly HashSet<string> BrightPlanetCodes = new(StringComparer.OrdinalIgnoreCase) { "MERCURY", "VENUS", "MARS", "JUPITER", "SATURN" };
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
        var candidateReasons = new List<AstronomyEventCandidateReason>();
        var zone = ResolveZone(request.Timezone, warnings);
        var startLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(request.StartUtc, zone).DateTime);
        var endLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(request.EndUtc, zone).DateTime);
        var daysScanned = 0;
        var skyfieldDaysSuccessful = 0;
        var visibleObjectCount = 0;

        for (var date = startLocalDate; date <= endLocalDate; date = date.AddDays(1))
        {
            daysScanned++;
            var visibility = await visibilityService.CalculateVisibilityAsync(new AstronomyVisibilityRequest(
                request.RegionId,
                request.LocationName,
                request.Latitude,
                request.Longitude,
                request.Timezone,
                date,
                null), cancellationToken);
            warnings.AddRange(visibility.Warnings.Select(w => $"{date:yyyy-MM-dd}: {w}"));

            var skyfieldSuccessful = visibility.Warnings.Any(w => w.Contains("Visibility source: Skyfield", StringComparison.OrdinalIgnoreCase));
            if (skyfieldSuccessful) skyfieldDaysSuccessful++;
            visibleObjectCount += visibility.VisibleObjects.Count(o => o.Visible);
            if (skyfieldSuccessful) LogSkyfieldResponseSummary(date, visibility);

            var visiblePlanets = visibility.VisibleObjects
                .Where(IsVisiblePlanet)
                .Where(o => !skyfieldSuccessful || HasMinimumAltitude(o, MinimumVisibleAltitudeDegrees))
                .OrderByDescending(o => o.VisibilityScore)
                .ToList();
            var brightPlanets = visiblePlanets.Where(IsBrightPlanet).Take(5).ToList();

            if (skyfieldSuccessful)
            {
                AddSkyfieldCandidateEvents(request, requestedTypes, visibility, brightPlanets, detected, candidateReasons);
            }
            else
            {
                AddFallbackCandidateEvents(request, requestedTypes, visibility, visiblePlanets, detected, candidateReasons);
            }
        }

        detected = detected
            .OrderByDescending(e => e.ViralPotentialScore)
            .ThenByDescending(e => e.VisibilityScore)
            .ThenBy(e => e.StartUtc)
            .Take(request.MaxEvents ?? int.MaxValue)
            .ToList();

        var retainedEventCodes = detected.Select(e => e.EventCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        candidateReasons = candidateReasons.Where(r => retainedEventCodes.Contains(r.EventCode)).ToList();

        var saved = request.DryRun ? detected : await SaveAsync(detected, cancellationToken);
        var savedCount = request.DryRun ? 0 : saved.Count(e => e.Id.HasValue);
        var diagnostics = request.DryRun
            ? new AstronomyEventDetectionDiagnostics(daysScanned, skyfieldDaysSuccessful, visibleObjectCount, candidateReasons)
            : null;

        logger.LogInformation("Astronomy event detection completed for {RegionId} ({LocationName}). Detected={DetectedCount}, Saved={SavedCount}, DryRun={DryRun}, DaysScanned={DaysScanned}, SkyfieldDaysSuccessful={SkyfieldDaysSuccessful}, VisibleObjectCount={VisibleObjectCount}", request.RegionId, request.LocationName, detected.Count, savedCount, request.DryRun, daysScanned, skyfieldDaysSuccessful, visibleObjectCount);

        return new AstronomyEventDetectionResult(request.RegionId, request.LocationName, request.StartUtc, request.EndUtc, request.DryRun, saved.Count, savedCount, saved, warnings.Distinct().ToArray(), diagnostics);
    }

    private static void AddSkyfieldCandidateEvents(
        AstronomyEventDetectionRequest request,
        HashSet<string> requestedTypes,
        AstronomyVisibilityResult visibility,
        IReadOnlyList<VisibleCelestialObjectResult> brightPlanets,
        List<DetectedAstronomyEventDto> detected,
        List<AstronomyEventCandidateReason> candidateReasons)
    {
        var visibleMoon = visibility.VisibleObjects.FirstOrDefault(IsVisibleMoon);
        if (requestedTypes.Contains("MOON_SPECIAL") && visibleMoon is not null)
        {
            AddDetected(detected, candidateReasons, BuildMoonSpecial(request, visibility, "Moon visible above horizon in successful Skyfield response."), visibility.TargetDate, "Moon visible above horizon in successful Skyfield response.");
        }

        if (requestedTypes.Contains("BRIGHT_PLANET_VISIBILITY") && brightPlanets.Count > 0)
        {
            var reason = $"{brightPlanets.Count} bright planet(s) visible at or above {MinimumVisibleAltitudeDegrees:0.#}° altitude in successful Skyfield response.";
            AddDetected(detected, candidateReasons, BuildBrightPlanetVisibility(request, visibility, brightPlanets, reason), visibility.TargetDate, reason);
        }

        if (requestedTypes.Contains("PLANET_GROUPING") && brightPlanets.Count >= 2)
        {
            var minSeparation = MinimumPairSeparation(brightPlanets);
            if (!minSeparation.HasValue || minSeparation.Value <= GroupingMaxSeparationDegrees)
            {
                var reason = minSeparation.HasValue
                    ? $"{brightPlanets.Count} bright planets visible in the same window; nearest separation {minSeparation.Value:0.#}° <= {GroupingMaxSeparationDegrees:0.#}°."
                    : $"{brightPlanets.Count} bright planets visible in the same observation window; angular separation unavailable.";
                AddDetected(detected, candidateReasons, BuildPlanetGrouping(request, visibility, brightPlanets.Take(4).ToList(), reason, minSeparation), visibility.TargetDate, reason);
            }
        }

        if (requestedTypes.Contains("PLANET_CONJUNCTION") && brightPlanets.Count >= 2)
        {
            var closestPair = ClosestPairWithin(brightPlanets, ConjunctionMaxSeparationDegrees);
            if (closestPair is not null)
            {
                var reason = $"Angular separation {closestPair.Value.SeparationDegrees:0.#}° <= {ConjunctionMaxSeparationDegrees:0.#}° for {closestPair.Value.First.ObjectName} and {closestPair.Value.Second.ObjectName}.";
                AddDetected(detected, candidateReasons, BuildPlanetConjunction(request, visibility, [closestPair.Value.First, closestPair.Value.Second], reason, closestPair.Value.SeparationDegrees), visibility.TargetDate, reason);
            }
        }
    }

    private static void AddFallbackCandidateEvents(
        AstronomyEventDetectionRequest request,
        HashSet<string> requestedTypes,
        AstronomyVisibilityResult visibility,
        IReadOnlyList<VisibleCelestialObjectResult> visiblePlanets,
        List<DetectedAstronomyEventDto> detected,
        List<AstronomyEventCandidateReason> candidateReasons)
    {
        if (requestedTypes.Contains("PLANET_GROUPING") && visiblePlanets.Count >= 3)
            AddDetected(detected, candidateReasons, BuildPlanetGrouping(request, visibility, visiblePlanets.Take(4).ToList(), "Fallback source reported at least 3 visible planets.", null), visibility.TargetDate, "Fallback source reported at least 3 visible planets.");

        if (requestedTypes.Contains("PLANET_CONJUNCTION") && visiblePlanets.Count >= 2)
            AddDetected(detected, candidateReasons, BuildPlanetConjunction(request, visibility, visiblePlanets.Take(2).ToList(), "Fallback source reported at least 2 visible planets; precise separation unavailable.", null), visibility.TargetDate, "Fallback source reported at least 2 visible planets; precise separation unavailable.");

        if (requestedTypes.Contains("MOON_SPECIAL") && IsSpecialMoon(visibility.MoonPhase, visibility.MoonIlluminationPercent))
            AddDetected(detected, candidateReasons, BuildMoonSpecial(request, visibility, "Fallback moon phase/illumination special threshold matched."), visibility.TargetDate, "Fallback moon phase/illumination special threshold matched.");

        var brightPlanets = visiblePlanets.Where(o => IsBrightPlanet(o) || o.VisibilityScore >= 8).Take(3).ToList();
        if (requestedTypes.Contains("BRIGHT_PLANET_VISIBILITY") && brightPlanets.Count > 0)
            AddDetected(detected, candidateReasons, BuildBrightPlanetVisibility(request, visibility, brightPlanets, "Fallback source reported bright visible planet(s)."), visibility.TargetDate, "Fallback source reported bright visible planet(s).");
    }
    private static void AddDetected(List<DetectedAstronomyEventDto> detected, List<AstronomyEventCandidateReason> candidateReasons, DetectedAstronomyEventDto dto, DateOnly targetDate, string candidateReason)
    {
        detected.Add(dto);
        candidateReasons.Add(new AstronomyEventCandidateReason(dto.EventCode, dto.EventType, targetDate, candidateReason));
    }

    private void LogSkyfieldResponseSummary(DateOnly date, AstronomyVisibilityResult visibility)
    {
        var summaries = visibility.VisibleObjects.Select(o => new
        {
            objectName = o.ObjectName,
            altitude = o.MaxAltitudeDegrees,
            azimuth = o.BestViewingAzimuthDegrees,
            magnitude = ResolveMagnitude(o),
            visible = o.Visible,
            angularSeparation = NearestSeparation(o, visibility.VisibleObjects)
        }).ToArray();

        logger.LogInformation("Phase 7B Skyfield response summary for {Date}: {@SkyfieldObjectSummaries}", date, summaries);
    }

    private static decimal? ResolveMagnitude(VisibleCelestialObjectResult obj)
    {
        if (obj.Magnitude.HasValue) return obj.Magnitude.Value;
        if (ApproxPlanetMagnitudes.TryGetValue(obj.ObjectCode, out var codeMagnitude)) return codeMagnitude;
        if (ApproxPlanetMagnitudes.TryGetValue(obj.ObjectName, out var nameMagnitude)) return nameMagnitude;
        return null;
    }

    private static bool IsVisiblePlanet(VisibleCelestialObjectResult obj) =>
        obj.Visible && (IsPlanetCodeOrName(obj) || obj.ObjectType.Equals("Planet", StringComparison.OrdinalIgnoreCase));

    private static bool IsBrightPlanet(VisibleCelestialObjectResult obj) =>
        BrightPlanetCodes.Contains(obj.ObjectCode) || BrightPlanetCodes.Contains(obj.ObjectName);

    private static bool IsPlanetCodeOrName(VisibleCelestialObjectResult obj) =>
        PlanetCodes.Contains(obj.ObjectCode) || PlanetCodes.Contains(obj.ObjectName);

    private static bool IsVisibleMoon(VisibleCelestialObjectResult obj) =>
        obj.Visible && (obj.ObjectCode.Equals("MOON", StringComparison.OrdinalIgnoreCase)
                        || obj.ObjectCode.Equals("Moon", StringComparison.OrdinalIgnoreCase)
                        || obj.ObjectName.Equals("Moon", StringComparison.OrdinalIgnoreCase)
                        || obj.ObjectType.Equals("Moon", StringComparison.OrdinalIgnoreCase));

    private static bool HasMinimumAltitude(VisibleCelestialObjectResult obj, double minimumAltitudeDegrees) =>
        obj.MaxAltitudeDegrees is null || obj.MaxAltitudeDegrees.Value >= minimumAltitudeDegrees;

    private static double? MinimumPairSeparation(IReadOnlyList<VisibleCelestialObjectResult> objects)
    {
        double? minimum = null;
        for (var i = 0; i < objects.Count; i++)
        {
            for (var j = i + 1; j < objects.Count; j++)
            {
                var separation = AngularSeparationDegrees(objects[i], objects[j]);
                if (!separation.HasValue) continue;
                minimum = minimum.HasValue ? Math.Min(minimum.Value, separation.Value) : separation.Value;
            }
        }

        return minimum;
    }

    private static (VisibleCelestialObjectResult First, VisibleCelestialObjectResult Second, double SeparationDegrees)? ClosestPairWithin(IReadOnlyList<VisibleCelestialObjectResult> objects, double maxSeparationDegrees)
    {
        (VisibleCelestialObjectResult First, VisibleCelestialObjectResult Second, double SeparationDegrees)? closest = null;
        for (var i = 0; i < objects.Count; i++)
        {
            for (var j = i + 1; j < objects.Count; j++)
            {
                var separation = AngularSeparationDegrees(objects[i], objects[j]);
                if (!separation.HasValue || separation.Value > maxSeparationDegrees) continue;
                if (closest is null || separation.Value < closest.Value.SeparationDegrees)
                    closest = (objects[i], objects[j], separation.Value);
            }
        }

        return closest;
    }

    private static double? NearestSeparation(VisibleCelestialObjectResult obj, IReadOnlyList<VisibleCelestialObjectResult> objects)
    {
        double? nearest = null;
        foreach (var other in objects)
        {
            if (ReferenceEquals(obj, other) || (obj.ObjectCode.Equals(other.ObjectCode, StringComparison.OrdinalIgnoreCase) && obj.ObjectName.Equals(other.ObjectName, StringComparison.OrdinalIgnoreCase))) continue;
            var separation = AngularSeparationDegrees(obj, other);
            if (!separation.HasValue) continue;
            nearest = nearest.HasValue ? Math.Min(nearest.Value, separation.Value) : separation.Value;
        }

        return nearest;
    }

    private static double? AngularSeparationDegrees(VisibleCelestialObjectResult first, VisibleCelestialObjectResult second)
    {
        if (!first.MaxAltitudeDegrees.HasValue || !first.BestViewingAzimuthDegrees.HasValue || !second.MaxAltitudeDegrees.HasValue || !second.BestViewingAzimuthDegrees.HasValue)
            return null;

        var firstAltitude = DegreesToRadians(first.MaxAltitudeDegrees.Value);
        var secondAltitude = DegreesToRadians(second.MaxAltitudeDegrees.Value);
        var azimuthDelta = DegreesToRadians(first.BestViewingAzimuthDegrees.Value - second.BestViewingAzimuthDegrees.Value);
        var cosSeparation = (Math.Sin(firstAltitude) * Math.Sin(secondAltitude)) + (Math.Cos(firstAltitude) * Math.Cos(secondAltitude) * Math.Cos(azimuthDelta));
        return Math.Round(RadiansToDegrees(Math.Acos(Math.Clamp(cosSeparation, -1d, 1d))), 2);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
    private static double RadiansToDegrees(double radians) => radians * 180d / Math.PI;

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

    private static DetectedAstronomyEventDto BuildPlanetGrouping(AstronomyEventDetectionRequest request, AstronomyVisibilityResult visibility, IReadOnlyList<VisibleCelestialObjectResult> planets, string candidateReason, double? angularSeparationDegrees)
    {
        var names = string.Join(", ", planets.Select(p => p.ObjectName));
        var scores = Score(visibility: 7.5m + planets.Count * 0.5m, rarity: angularSeparationDegrees.HasValue ? 7.0m : 6.0m, story: 7.5m, viral: 7.0m, confidence: angularSeparationDegrees.HasValue ? 7.0m : 6.0m);
        return BuildEvent(request, visibility, "PLANET_GROUPING", "Planet", $"Planet grouping over {request.LocationName}", $"Visible planet grouping candidate: {names}.",
            "Multiple naked-eye bright planets are visible in the same observation window.", scores,
            planets.Select((p, i) => ToObjectDto(p, i == 0 ? "Primary" : "Companion")).ToArray(), new { rule = "bright_visible_planets >= 2", minimumVisibleAltitudeDegrees = MinimumVisibleAltitudeDegrees, groupingMaxSeparationDegrees = GroupingMaxSeparationDegrees, angularSeparationDegrees, candidateReason });
    }

    private static DetectedAstronomyEventDto BuildPlanetConjunction(AstronomyEventDetectionRequest request, AstronomyVisibilityResult visibility, IReadOnlyList<VisibleCelestialObjectResult> planets, string candidateReason, double? angularSeparationDegrees)
    {
        var names = string.Join(" and ", planets.Select(p => p.ObjectName));
        var scores = Score(visibility: 7.0m, rarity: angularSeparationDegrees is <= ConjunctionMaxSeparationDegrees ? 7.5m : 6.5m, story: 7.0m, viral: 7.0m, confidence: angularSeparationDegrees.HasValue ? 7.5m : 5.5m);
        return BuildEvent(request, visibility, "PLANET_CONJUNCTION", "Planet", $"Planet conjunction candidate: {names}", $"{names} are visible close together in the same night window.",
            "Candidate conjunction based on Skyfield visibility geometry. This phase intentionally favors reasonable candidate intelligence over rare-event perfection.", scores,
            planets.Select((p, i) => ToObjectDto(p, i == 0 ? "Primary" : "Companion")).ToArray(), new { rule = "angular_separation <= threshold", conjunctionMaxSeparationDegrees = ConjunctionMaxSeparationDegrees, angularSeparationDegrees, candidateReason });
    }

    private static DetectedAstronomyEventDto BuildMoonSpecial(AstronomyEventDetectionRequest request, AstronomyVisibilityResult visibility, string candidateReason)
    {
        var scores = Score(visibility: visibility.MoonIlluminationPercent >= 90 ? 8.5m : 7.0m, rarity: 5.5m, story: 8.0m, viral: 7.5m, confidence: 7.5m);
        var visibleMoon = visibility.VisibleObjects.FirstOrDefault(IsVisibleMoon);
        var moon = visibleMoon is null
            ? new DetectedAstronomyEventObjectDto(null, "Moon", "Moon", "Primary", "MOON", null, scores.VisibilityScore, JsonSerializer.Serialize(new { visibility.MoonPhase, visibility.MoonIlluminationPercent }, JsonOptions))
            : ToObjectDto(visibleMoon, "Primary");
        return BuildEvent(request, visibility, "MOON_SPECIAL", "Moon", $"{visibility.MoonPhase} Moon over {request.LocationName}", $"Moon special candidate: {visibility.MoonPhase} with {visibility.MoonIlluminationPercent:0.#}% illumination.",
            "The Moon is visible in the observation window and is suitable for candidate astronomy intelligence and content planning.", scores, [moon], new { rule = "moon visible or special moon phase threshold", minimumVisibleAltitudeDegrees = MinimumVisibleAltitudeDegrees, candidateReason });
    }

    private static DetectedAstronomyEventDto BuildBrightPlanetVisibility(AstronomyEventDetectionRequest request, AstronomyVisibilityResult visibility, IReadOnlyList<VisibleCelestialObjectResult> planets, string candidateReason)
    {
        var names = string.Join(", ", planets.Select(p => p.ObjectName));
        var scores = Score(visibility: ClampDecimal((decimal)planets.Average(p => p.VisibilityScore)), rarity: 4.5m + planets.Count, story: 6.5m, viral: 8.0m, confidence: 7.0m);
        return BuildEvent(request, visibility, "BRIGHT_PLANET_VISIBILITY", "Planet", $"Bright planet visibility over {request.LocationName}", $"Bright planet visibility candidate: {names}.",
            "One or more bright naked-eye planets have strong visibility for this location and date.", scores,
            planets.Select((p, i) => ToObjectDto(p, i == 0 ? "Primary" : "Companion")).ToArray(), new { rule = "bright visible planet altitude threshold", minimumVisibleAltitudeDegrees = MinimumVisibleAltitudeDegrees, candidateReason });
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
        var magnitude = result.Magnitude ?? ApproxPlanetMagnitudes.GetValueOrDefault(result.ObjectCode, 1.0m);
        var metadata = JsonSerializer.Serialize(new { result.ObjectCode, result.BestViewingStartUtc, result.BestViewingEndUtc, result.MaxAltitudeDegrees, result.BestViewingAzimuthDegrees, result.AngularSeparationDegrees, result.Reason }, JsonOptions);
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

    private static string NormalizeEventType(string eventType)
    {
        var normalized = eventType.Trim().Replace('-', '_').Replace(' ', '_');
        if (!normalized.Contains('_'))
        {
            normalized = string.Concat(normalized.Select((ch, index) => index > 0 && char.IsUpper(ch) ? $"_{ch}" : ch.ToString()));
        }

        normalized = normalized.ToUpperInvariant();
        return normalized switch
        {
            "MOON_SPECIAL" => "MOON_SPECIAL",
            "BRIGHT_PLANET_VISIBILITY" => "BRIGHT_PLANET_VISIBILITY",
            "PLANET_GROUPING" => "PLANET_GROUPING",
            "PLANET_CONJUNCTION" => "PLANET_CONJUNCTION",
            _ => normalized
        };
    }

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
