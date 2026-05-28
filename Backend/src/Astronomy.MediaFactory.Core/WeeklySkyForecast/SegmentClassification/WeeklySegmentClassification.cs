using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification;

public sealed record WeeklySegmentAssignment(
    string SegmentId,
    string SegmentType,
    string AssignedEventType,
    IReadOnlyList<string> AssignedObjects,
    DateOnly? AssignedDateLocal,
    TimeOnly? AssignedBestTimeLocal,
    string VisibilitySummary,
    string ClassificationReason,
    int ConfidenceScore,
    IReadOnlyList<string> RequiredVisualTypes,
    IReadOnlyList<string> SuggestedRenderScenes,
    string ProductionStatus,
    IReadOnlyList<string> Warnings);

public sealed record WeeklySegmentClassificationPlan(
    Guid PipelineRunId,
    string RegionId,
    string Language,
    DateOnly WeekStartDate,
    DateTime GeneratedAtUtc,
    IReadOnlyList<WeeklySegmentAssignment> LongformAssignments,
    IReadOnlyList<WeeklySegmentAssignment> ShortformAssignments,
    bool SegmentClassificationReady,
    int ClassifiedLongformSegmentCount,
    int ClassifiedShortformSegmentCount,
    string? HeroEventSegmentType,
    IReadOnlyList<string> HeroEventObjects,
    IReadOnlyList<string> ValidationWarnings);

public sealed class WeeklySegmentClassificationPolicy
{
    private static readonly string[] PlanetPriority = ["VENUS", "JUPITER", "MARS", "SATURN", "MERCURY"];

    public IReadOnlyList<string> GetRequiredVisualTypes(string segmentType, WeeklyAstronomyEvent? assignedEvent = null) => segmentType switch
    {
        "OpeningHook" => assignedEvent is null ? ["AICinematic", "MotionGraphics"] : ["Hybrid", "AICinematic"],
        "WeeklySkyOverview" => ["Hybrid", "MotionGraphics", "Stellarium"],
        "HeroEvent" => ["Stellarium", "Hybrid"],
        "MoonHighlights" => ["Stellarium", "MotionGraphics"],
        "PlanetHighlights" => ["Stellarium", "MotionGraphics"],
        "BestObservationWindow" => ["MotionGraphics", "Stellarium"],
        "AstrophotographyTip" => ["Hybrid", "Stellarium", "NASA/JWST"],
        "WeeklySummary" => ["MotionGraphics", "AICinematic"],
        "ShortHook" => ["Stellarium", "AICinematic"],
        "StrongestEvent" => ["Stellarium", "Hybrid"],
        "WhereToLook" => ["MotionGraphics", "Stellarium"],
        "BestTime" => ["MotionGraphics"],
        "CallToAction" => ["AICinematic", "MotionGraphics"],
        _ => ["Hybrid"]
    };

    public WeeklyAstronomyEvent? SelectHeroEvent(IReadOnlyList<WeeklyAstronomyEvent> events)
    {
        var ordered = events
            .OrderBy(GetHeroPriority)
            .ThenByDescending(e => e.ImportanceScore)
            .ThenByDescending(e => e.VisibilityScore)
            .ThenByDescending(e => e.ObjectCount)
            .ToList();
        return ordered.FirstOrDefault();
    }

    public WeeklyAstronomyEvent? SelectMoonEvent(IReadOnlyList<WeeklyAstronomyEvent> events) => events
        .Where(e => e.Objects.Any(IsMoon))
        .OrderBy(e => e.EventType is WeeklyAstronomyEventType.Conjunction or WeeklyAstronomyEventType.Grouping ? 0 : 1)
        .ThenByDescending(e => e.ImportanceScore + e.VisibilityScore)
        .FirstOrDefault();

    public WeeklyAstronomyEvent? SelectPlanetEvent(IReadOnlyList<WeeklyAstronomyEvent> events) => events
        .Where(e => e.Objects.Any(IsNakedEyePlanet))
        .OrderBy(e => BestPlanetRank(e.Objects))
        .ThenByDescending(e => e.VisibilityScore)
        .ThenByDescending(e => e.ImportanceScore)
        .FirstOrDefault();

    public WeeklyAstronomyEvent? SelectBestWindowEvent(IReadOnlyList<WeeklyAstronomyEvent> events) => events
        .Where(e => e.EventType == WeeklyAstronomyEventType.BestViewingWindow)
        .OrderByDescending(e => e.ImportanceScore + e.VisibilityScore)
        .FirstOrDefault();

    public WeeklyAstronomyEvent? SelectAstrophotographyEvent(IReadOnlyList<WeeklyAstronomyEvent> events)
    {
        return events.FirstOrDefault(e => e.Objects.Any(IsMoon))
               ?? events.FirstOrDefault(e => e.EventType is WeeklyAstronomyEventType.Grouping or WeeklyAstronomyEventType.Conjunction)
               ?? events.FirstOrDefault(e => e.EventType == WeeklyAstronomyEventType.DeepSkyHighlight)
               ?? SelectHeroEvent(events);
    }

    public int ScoreDirectEvent(WeeklyAstronomyEvent? ev, string segmentType)
    {
        if (ev is null)
            return segmentType is "WeeklySkyOverview" or "WeeklySummary" or "OpeningHook" or "CallToAction" ? 55 : 35;

        var baseScore = ev.EventType switch
        {
            WeeklyAstronomyEventType.Grouping or WeeklyAstronomyEventType.Conjunction => 96,
            WeeklyAstronomyEventType.BestViewingWindow => 90,
            WeeklyAstronomyEventType.HeroObject => 86,
            WeeklyAstronomyEventType.DeepSkyHighlight => 82,
            WeeklyAstronomyEventType.TelescopeOpportunity => 78,
            WeeklyAstronomyEventType.DirectionalObservation => 74,
            _ => 70
        };

        if (segmentType.Contains("Moon", StringComparison.OrdinalIgnoreCase) && ev.Objects.Any(IsMoon))
            baseScore = Math.Max(baseScore, 92);
        if (segmentType.Contains("Planet", StringComparison.OrdinalIgnoreCase) && ev.Objects.Any(IsNakedEyePlanet))
            baseScore = Math.Max(baseScore, 90);
        if (segmentType == "BestObservationWindow" && ev.EventType == WeeklyAstronomyEventType.BestViewingWindow)
            baseScore = 96;

        return Math.Clamp(baseScore, 0, 100);
    }

    public static bool IsMoon(WeeklyAstronomyEventObject obj) => obj.ObjectCode.Equals("MOON", StringComparison.OrdinalIgnoreCase) || obj.ObjectName.Contains("moon", StringComparison.OrdinalIgnoreCase);
    public static bool IsNakedEyePlanet(WeeklyAstronomyEventObject obj) => PlanetPriority.Contains(obj.ObjectCode, StringComparer.OrdinalIgnoreCase);

    private static int GetHeroPriority(WeeklyAstronomyEvent ev)
    {
        if (ev.EventType is WeeklyAstronomyEventType.Conjunction or WeeklyAstronomyEventType.Grouping)
            return 1;
        if (ev.Objects.Any(IsMoon) && ev.Objects.Any(IsNakedEyePlanet))
            return 2;
        if (ev.Objects.Any(IsNakedEyePlanet))
            return 3;
        if (ev.Objects.Any(IsMoon))
            return 4;
        if (ev.EventType == WeeklyAstronomyEventType.BestViewingWindow)
            return 5;
        return 9;
    }

    private static int BestPlanetRank(IEnumerable<WeeklyAstronomyEventObject> objects)
    {
        var ranks = objects
            .Select(o => Array.FindIndex(PlanetPriority, p => p.Equals(o.ObjectCode, StringComparison.OrdinalIgnoreCase)))
            .Where(rank => rank >= 0)
            .ToList();
        return ranks.Count == 0 ? int.MaxValue : ranks.Min();
    }
}

public sealed class WeeklySegmentClassifier(WeeklySegmentClassificationPolicy policy)
{
    public WeeklySegmentClassificationPlan Classify(
        WeeklyEpisodeArchitectureResult episodeArchitecture,
        WeeklySkyForecastV2IntelligenceResponse weeklyContext,
        WeeklyHybridScenePlanPackage? scenePlan,
        DateTime generatedAtUtc)
    {
        var events = weeklyContext.EventExtractionResult?.ExtractedEvents ?? [];
        var hero = policy.SelectHeroEvent(events) ?? weeklyContext.EventExtractionResult?.SelectedPrimaryEvent;
        var validationWarnings = new List<string>();
        if (weeklyContext.EventExtractionResult is null)
            validationWarnings.Add("Skyfield event extraction result is missing; classifications use context-only fallback without invented geometry.");
        foreach (var missing in weeklyContext.EventExtractionResult?.MissingData ?? [])
            validationWarnings.Add($"Skyfield missing data: {missing}");

        var longform = episodeArchitecture.LongFormPlan.Segments
            .Select(segment => ClassifySegment(segment, weeklyContext, scenePlan, events, hero, isShortForm: false))
            .ToList();
        var shortform = episodeArchitecture.ShortFormPlan.Segments
            .Select(segment => ClassifySegment(segment, weeklyContext, scenePlan, events, hero, isShortForm: true))
            .ToList();

        Validate(longform, shortform, validationWarnings);
        var heroAssignment = longform.FirstOrDefault(x => x.SegmentType == "HeroEvent") ?? shortform.FirstOrDefault(x => x.SegmentType == "StrongestEvent");
        var ready = longform.Count == 8 && shortform.Count == 5 && heroAssignment is not null
                    && longform.Any(x => x.SegmentType == "MoonHighlights")
                    && longform.Any(x => x.SegmentType == "PlanetHighlights")
                    && longform.Any(x => x.SegmentType == "BestObservationWindow");

        return new WeeklySegmentClassificationPlan(
            episodeArchitecture.LongFormPlan.PipelineRunId,
            episodeArchitecture.LongFormPlan.RegionId,
            episodeArchitecture.LongFormPlan.Language,
            episodeArchitecture.LongFormPlan.WeekStartDate,
            generatedAtUtc,
            longform,
            shortform,
            ready,
            longform.Count,
            shortform.Count,
            heroAssignment?.SegmentType,
            heroAssignment?.AssignedObjects ?? [],
            validationWarnings);
    }

    private WeeklySegmentAssignment ClassifySegment(
        WeeklyEpisodeSegment segment,
        WeeklySkyForecastV2IntelligenceResponse weeklyContext,
        WeeklyHybridScenePlanPackage? scenePlan,
        IReadOnlyList<WeeklyAstronomyEvent> events,
        WeeklyAstronomyEvent? hero,
        bool isShortForm)
    {
        var warnings = new List<string>();
        WeeklyAstronomyEvent? assignedEvent;
        string eventType;
        string reason;

        switch (segment.SegmentType)
        {
            case "OpeningHook":
                assignedEvent = hero;
                eventType = assignedEvent?.EventType.ToString() ?? "WeeklyTheme";
                reason = assignedEvent is null
                    ? "No direct Skyfield event was available, so the hook uses the weekly theme only without fake object geometry."
                    : "Selected the highest emotional and visual Skyfield-derived event for the opening promise.";
                break;
            case "WeeklySkyOverview":
                assignedEvent = null;
                eventType = "WeeklySummary";
                reason = "Assigned the full weekly Skyfield summary and all major visible categories rather than one invented event.";
                break;
            case "HeroEvent":
                assignedEvent = hero;
                eventType = assignedEvent?.EventType.ToString() ?? "Unavailable";
                reason = assignedEvent is null
                    ? "No HeroEvent candidate was available from Skyfield extraction."
                    : "Selected strongest visible event using priority order: grouping, Moon plus planet, bright planet, Moon highlight, best observing night.";
                break;
            case "MoonHighlights":
                assignedEvent = policy.SelectMoonEvent(events);
                eventType = assignedEvent?.EventType.ToString() ?? "MoonVisibilityUnavailable";
                reason = assignedEvent is null
                    ? "Skyfield extraction did not provide Moon phase or Moon visibility data; no Moon geometry was invented."
                    : "Assigned Skyfield-derived Moon visibility or Moon-in-event highlight.";
                break;
            case "PlanetHighlights":
                assignedEvent = policy.SelectPlanetEvent(events);
                eventType = assignedEvent?.EventType.ToString() ?? "PlanetVisibilityUnavailable";
                reason = assignedEvent is null
                    ? "Skyfield extraction did not provide naked-eye planet visibility; no planet visibility was invented."
                    : "Assigned visible naked-eye planets, prioritized Venus, Jupiter, Mars, Saturn, and Mercury.";
                break;
            case "BestObservationWindow":
                assignedEvent = policy.SelectBestWindowEvent(events);
                eventType = assignedEvent?.EventType.ToString() ?? "BestWindowUnavailable";
                reason = assignedEvent is null
                    ? "Skyfield recommended nights did not yield a best observing window event."
                    : "Assigned best night/time from Skyfield recommended observing window.";
                break;
            case "AstrophotographyTip":
                assignedEvent = policy.SelectAstrophotographyEvent(events);
                eventType = assignedEvent?.EventType.ToString() ?? "WideSkyFallback";
                reason = assignedEvent is null
                    ? "No Moon, grouping, or deep-sky photo target was available; suggest wide-sky visual fallback without inventing targets."
                    : "Assigned the best available photo opportunity from Moon, grouping, deep-sky, then wide-sky priority.";
                break;
            case "WeeklySummary":
                assignedEvent = null;
                eventType = "ClassifiedSegmentRecap";
                reason = "Assigned recap role using all classified segment outcomes.";
                break;
            case "ShortHook":
            case "StrongestEvent":
            case "WhereToLook":
            case "BestTime":
            case "CallToAction":
                assignedEvent = hero;
                eventType = assignedEvent?.EventType.ToString() ?? "ShortFormFallback";
                reason = assignedEvent is null
                    ? "No HeroEvent candidate was available; short form must use a theme-only fallback without fake events."
                    : "ShortForm assigned strongest single event from the HeroEvent candidate.";
                break;
            default:
                assignedEvent = null;
                eventType = "UnknownSegment";
                reason = "Unknown segment type; classification fell back to contextual visual planning.";
                break;
        }

        var contextAddon = BuildContextualReasonAddon(segment, assignedEvent, weeklyContext, scenePlan);
        if (!string.IsNullOrWhiteSpace(contextAddon))
            reason = $"{reason} {contextAddon}";

        if (assignedEvent is null && segment.SegmentType is not "WeeklySkyOverview" and not "WeeklySummary")
            warnings.Add("Missing direct Skyfield source for this segment; classification does not invent astronomical events or geometry.");

        var confidence = policy.ScoreDirectEvent(assignedEvent, segment.SegmentType);
        var productionStatus = confidence < 50 || assignedEvent is null && segment.SegmentType is not "WeeklySkyOverview" and not "WeeklySummary"
            ? "NeedsAssetExpansion"
            : "Classified";
        var scenes = SuggestScenes(segment, assignedEvent, scenePlan, isShortForm);
        if (scenes.Count == 0)
            warnings.Add("No matching current render scene found; downstream visual planning should add or reuse assets.");

        return new WeeklySegmentAssignment(
            segment.SegmentId,
            segment.SegmentType,
            eventType,
            ResolveObjects(segment, assignedEvent, events, weeklyContext),
            assignedEvent?.BestDateLocal ?? ResolveFallbackDate(segment, weeklyContext),
            assignedEvent?.BestTimeLocal,
            BuildVisibilitySummary(assignedEvent, weeklyContext, segment.SegmentType),
            reason,
            confidence,
            policy.GetRequiredVisualTypes(segment.SegmentType, assignedEvent),
            scenes,
            productionStatus,
            warnings);
    }


    private static string BuildContextualReasonAddon(WeeklyEpisodeSegment segment, WeeklyAstronomyEvent? assignedEvent, WeeklySkyForecastV2IntelligenceResponse context, WeeklyHybridScenePlanPackage? scenePlan)
    {
        var assignedCodes = assignedEvent?.Objects.Select(o => o.ObjectCode).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var narrationSegment = context.NarrationPlan?.LongFormPlan.Segments
            .FirstOrDefault(s => s.SegmentCode.Equals(segment.SegmentType, StringComparison.OrdinalIgnoreCase)
                                 || s.SegmentTitle.Contains(segment.Title, StringComparison.OrdinalIgnoreCase)
                                 || assignedCodes.Count > 0 && s.TargetObjects.Any(assignedCodes.Contains));
        var narrativeBeat = context.NarrativeAbstractionPackage?.NarrativeFlow
            .FirstOrDefault(b => assignedCodes.Count > 0 && b.TargetObjects.Any(assignedCodes.Contains));
        var cinematicVisual = context.NarrativeAbstractionPackage?.CinematicVisualPlan
            .OrderByDescending(v => v.CinematicPriority)
            .FirstOrDefault(v => assignedCodes.Count > 0 && v.ObjectCodes.Any(assignedCodes.Contains));
        var scene = scenePlan?.ScenePlans
            .OrderBy(s => s.SceneOrder)
            .FirstOrDefault(s => assignedCodes.Count > 0 && s.ObjectCodes.Any(assignedCodes.Contains));

        var parts = new List<string>();
        if (narrationSegment is not null)
            parts.Add($"Narration plan supports this with strategy '{narrationSegment.RecommendedVisualStrategy}'.");
        if (narrativeBeat is not null)
            parts.Add($"Story beat '{narrativeBeat.BeatCode}' contributes '{narrativeBeat.VisualIntent}'.");
        if (cinematicVisual is not null)
            parts.Add($"Cinematic direction '{cinematicVisual.VisualCode}' is available for visual role '{cinematicVisual.VisualNarrativeRole}'.");
        if (scene is not null)
            parts.Add($"Current scene plan can reuse '{scene.SceneCode}' ({scene.VisualSourceType}).");

        return string.Join(" ", parts);
    }

    private static IReadOnlyList<string> ResolveObjects(WeeklyEpisodeSegment segment, WeeklyAstronomyEvent? assignedEvent, IReadOnlyList<WeeklyAstronomyEvent> events, WeeklySkyForecastV2IntelligenceResponse context)
    {
        if (assignedEvent is not null)
            return assignedEvent.Objects.Select(o => o.ObjectCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (segment.SegmentType == "WeeklySkyOverview" || segment.SegmentType == "WeeklySummary")
            return events.SelectMany(e => e.Objects).Select(o => o.ObjectCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        if (segment.SegmentType == "AstrophotographyTip" && context.SkyfieldSummary.BestPhotographyNight.HasValue)
            return ["WIDE_SKY"];
        return [];
    }

    private static DateOnly? ResolveFallbackDate(WeeklyEpisodeSegment segment, WeeklySkyForecastV2IntelligenceResponse context)
    {
        if (segment.SegmentType == "MoonHighlights")
            return context.SkyfieldSummary.BestMoonNight;
        if (segment.SegmentType == "AstrophotographyTip")
            return context.SkyfieldSummary.BestPhotographyNight;
        return context.WeekStartDate;
    }

    private static string BuildVisibilitySummary(WeeklyAstronomyEvent? assignedEvent, WeeklySkyForecastV2IntelligenceResponse context, string segmentType)
    {
        if (assignedEvent is not null)
        {
            var objects = assignedEvent.Objects.Count == 0 ? "no object list" : string.Join(", ", assignedEvent.Objects.Select(o => $"{o.ObjectName} visibilityScore={o.VisibilityScore:0.#}"));
            var time = assignedEvent.BestDateLocal.HasValue ? $" on {assignedEvent.BestDateLocal:yyyy-MM-dd}" : string.Empty;
            return $"{assignedEvent.Summary}{time}; {objects}.";
        }

        if (segmentType == "WeeklySkyOverview" || segmentType == "WeeklySummary")
            return $"Week summary from Skyfield: {context.SkyfieldSummary.VisibleObjectCount} visible objects, {context.SkyfieldSummary.WeeklyHighlightsCount} weekly highlights, {context.SkyfieldSummary.RecommendedNightsCount} recommended nights.";
        return "No direct Skyfield visibility source available for this segment.";
    }

    private static IReadOnlyList<string> SuggestScenes(WeeklyEpisodeSegment segment, WeeklyAstronomyEvent? assignedEvent, WeeklyHybridScenePlanPackage? scenePlan, bool isShortForm)
    {
        if (scenePlan is null)
            return [];

        var objectCodes = assignedEvent?.Objects.Select(o => o.ObjectCode).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = scenePlan.ScenePlans.AsEnumerable();
        if (objectCodes.Count > 0)
            candidates = candidates.Where(scene => scene.ObjectCodes.Any(objectCodes.Contains));
        else
            candidates = candidates.Where(scene => scene.VisualStrategy.Contains(segment.SegmentType, StringComparison.OrdinalIgnoreCase) || scene.RenderIntent.Contains(segment.SegmentType, StringComparison.OrdinalIgnoreCase));

        return candidates
            .OrderBy(scene => scene.SceneOrder)
            .Take(isShortForm ? 2 : 4)
            .Select(scene => scene.SceneCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Validate(IReadOnlyList<WeeklySegmentAssignment> longform, IReadOnlyList<WeeklySegmentAssignment> shortform, List<string> warnings)
    {
        if (longform.Count != 8) warnings.Add($"Expected 8 longform segment classifications but found {longform.Count}.");
        if (shortform.Count != 5) warnings.Add($"Expected 5 shortform segment classifications but found {shortform.Count}.");
        foreach (var required in new[] { "HeroEvent", "MoonHighlights", "PlanetHighlights", "BestObservationWindow" })
        {
            if (!longform.Any(x => x.SegmentType == required))
                warnings.Add($"Required segment type missing from longform classification: {required}.");
        }
    }
}

public sealed class WeeklySegmentClassificationPersister
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<string> WriteAsync(WeeklySegmentClassificationPlan plan, string workingDirectoryRoot, CancellationToken cancellationToken)
    {
        var episodeDirectory = Path.Combine(workingDirectoryRoot, "episode");
        Directory.CreateDirectory(episodeDirectory);
        var path = Path.Combine(episodeDirectory, "weekly-segment-classification-plan.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        return path;
    }
}

public sealed class WeeklySegmentClassificationService(
    WeeklySegmentClassifier classifier,
    WeeklySegmentClassificationPersister persister,
    ILogger<WeeklySegmentClassificationService> logger)
{
    public async Task<(WeeklySegmentClassificationPlan Plan, string Path)> ClassifyAndPersistAsync(
        WeeklyEpisodeArchitectureResult episodeArchitecture,
        WeeklySkyForecastV2IntelligenceResponse weeklyContext,
        WeeklyHybridScenePlanPackage? scenePlan,
        string workingDirectoryRoot,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("SEGMENT_CLASSIFICATION_START segmentType={SegmentType} assignedEventType={AssignedEventType} assignedObjects={AssignedObjects} confidenceScore={ConfidenceScore} productionStatus={ProductionStatus}", "All", "Pending", Array.Empty<string>(), 0, "Pending");

        var plan = classifier.Classify(episodeArchitecture, weeklyContext, scenePlan, DateTime.UtcNow);
        foreach (var assignment in plan.LongformAssignments.Concat(plan.ShortformAssignments))
        {
            logger.LogInformation(
                "SEGMENT_CLASSIFIED segmentType={SegmentType} assignedEventType={AssignedEventType} assignedObjects={AssignedObjects} confidenceScore={ConfidenceScore} productionStatus={ProductionStatus}",
                assignment.SegmentType,
                assignment.AssignedEventType,
                assignment.AssignedObjects,
                assignment.ConfidenceScore,
                assignment.ProductionStatus);

            foreach (var warning in assignment.Warnings)
            {
                logger.LogWarning(
                    "SEGMENT_CLASSIFICATION_WARNING segmentType={SegmentType} assignedEventType={AssignedEventType} assignedObjects={AssignedObjects} confidenceScore={ConfidenceScore} productionStatus={ProductionStatus} warning={Warning}",
                    assignment.SegmentType,
                    assignment.AssignedEventType,
                    assignment.AssignedObjects,
                    assignment.ConfidenceScore,
                    assignment.ProductionStatus,
                    warning);
            }
        }

        foreach (var warning in plan.ValidationWarnings)
            logger.LogWarning("SEGMENT_CLASSIFICATION_WARNING segmentType={SegmentType} assignedEventType={AssignedEventType} assignedObjects={AssignedObjects} confidenceScore={ConfidenceScore} productionStatus={ProductionStatus} warning={Warning}", "Plan", "Validation", plan.HeroEventObjects, 0, plan.SegmentClassificationReady ? "Classified" : "NeedsAssetExpansion", warning);

        var path = await persister.WriteAsync(plan, workingDirectoryRoot, cancellationToken);
        logger.LogInformation("SEGMENT_CLASSIFICATION_PLAN_WRITTEN segmentType={SegmentType} assignedEventType={AssignedEventType} assignedObjects={AssignedObjects} confidenceScore={ConfidenceScore} productionStatus={ProductionStatus} path={Path}", "Plan", plan.HeroEventSegmentType ?? "None", plan.HeroEventObjects, 0, plan.SegmentClassificationReady ? "Classified" : "NeedsAssetExpansion", path);
        logger.LogInformation("SEGMENT_CLASSIFICATION_COMPLETE segmentType={SegmentType} assignedEventType={AssignedEventType} assignedObjects={AssignedObjects} confidenceScore={ConfidenceScore} productionStatus={ProductionStatus}", "All", plan.HeroEventSegmentType ?? "None", plan.HeroEventObjects, 0, plan.SegmentClassificationReady ? "Classified" : "NeedsAssetExpansion");
        return (plan, path);
    }
}
