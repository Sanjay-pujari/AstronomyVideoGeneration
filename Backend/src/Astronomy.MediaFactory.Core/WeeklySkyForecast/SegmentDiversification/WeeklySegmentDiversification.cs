using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentDiversification;

public sealed record DiversifiedSegmentAssignment(
    string SegmentId,
    string SegmentType,
    IReadOnlyList<string> OriginalAssignedObjects,
    string DiversifiedPrimaryFocus,
    IReadOnlyList<string> DiversifiedSecondaryFocus,
    string DiversifiedVisualSource,
    string DiversifiedVisualRole,
    string DiversifiedNarrationRole,
    string PacingRole,
    bool ReuseAllowed,
    string ReuseReason,
    bool AssetExpansionRequired,
    IReadOnlyList<string> RequiredNewAssets,
    IReadOnlyList<string> ExistingReusableAssets,
    int RepetitionRiskScore,
    int RetentionRiskScore,
    string DiversificationReason,
    string ProductionStatus,
    IReadOnlyList<string> Warnings);

public sealed record SegmentVisualSourceBalanceItem(
    string VisualSource,
    double Percent,
    int MinTargetPercent,
    int MaxTargetPercent,
    string Status,
    IReadOnlyList<string> Warnings);

public sealed record WeeklySegmentDiversificationPlan(
    Guid PipelineRunId,
    string RegionId,
    string Language,
    DateOnly WeekStartDate,
    DateTime GeneratedAtUtc,
    IReadOnlyList<DiversifiedSegmentAssignment> LongformAssignments,
    IReadOnlyList<DiversifiedSegmentAssignment> ShortformAssignments,
    IReadOnlyList<SegmentVisualSourceBalanceItem> LongformVisualSourceBalance,
    bool SegmentDiversificationReady,
    int DiversifiedLongformSegmentCount,
    int DiversifiedShortformSegmentCount,
    bool AssetExpansionRequired,
    int HighestRetentionRiskScore,
    int HighestRepetitionRiskScore,
    IReadOnlyList<string> ValidationWarnings);

public sealed class SegmentDiversificationPolicy
{
    private static readonly HashSet<string> PlanetCodes = new(StringComparer.OrdinalIgnoreCase) { "MERCURY", "VENUS", "MARS", "JUPITER", "SATURN" };

    public string GetVisualSource(string segmentType) => segmentType switch
    {
        "OpeningHook" => "Hybrid",
        "WeeklySkyOverview" => "MotionGraphics",
        "HeroEvent" => "Stellarium + Hybrid",
        "MoonHighlights" => "Stellarium + MotionGraphics",
        "PlanetHighlights" => "Stellarium + MotionGraphics",
        "BestObservationWindow" => "MotionGraphics",
        "AstrophotographyTip" => "NASA/JWST + AICinematic",
        "WeeklySummary" => "AICinematic + MotionGraphics",
        "ShortHook" => "AICinematic",
        "StrongestEvent" => "Stellarium + Hybrid",
        "WhereToLook" => "MotionGraphics",
        "BestTime" => "MotionGraphics",
        "CallToAction" => "AICinematic + MotionGraphics",
        _ => "Hybrid"
    };

    public string GetVisualRole(string segmentType) => segmentType switch
    {
        "OpeningHook" => "emotional_opening",
        "WeeklySkyOverview" => "contextual_orientation",
        "HeroEvent" => "primary_observation_story",
        "MoonHighlights" => "lunar_story",
        "PlanetHighlights" => "planetary_story",
        "BestObservationWindow" => "practical_guidance",
        "AstrophotographyTip" => "educational_practical_tip",
        "WeeklySummary" => "closing_recap",
        "ShortHook" => "hook",
        "StrongestEvent" => "event_reveal",
        "WhereToLook" => "where_to_look",
        "BestTime" => "best_time",
        "CallToAction" => "cta",
        _ => "supporting_story"
    };

    public string GetNarrationRole(string segmentType) => segmentType switch
    {
        "OpeningHook" => "emotional_promise",
        "WeeklySkyOverview" => "weekly_orientation",
        "HeroEvent" => "hero_event_observation_story",
        "MoonHighlights" => "moon_only_lunar_guidance",
        "PlanetHighlights" => "planet_only_visibility_guidance",
        "BestObservationWindow" => "date_time_viewing_guidance",
        "AstrophotographyTip" => "camera_tip_education",
        "WeeklySummary" => "recap_checklist_closure",
        "ShortHook" => "short_hook",
        "StrongestEvent" => "short_event_reveal",
        "WhereToLook" => "short_directional_guidance",
        "BestTime" => "short_time_guidance",
        "CallToAction" => "short_cta",
        _ => "supporting_narration"
    };

    public IReadOnlyList<string> GetRequiredNewAssets(string segmentType) => segmentType switch
    {
        "OpeningHook" => ["opening_cinematic_background", "hero_event_title_treatment"],
        "WeeklySkyOverview" => ["weekly_sky_map", "weekly_timeline_graphic"],
        "HeroEvent" => [],
        "MoonHighlights" => ["moon_phase_card", "moon_visibility_timeline"],
        "PlanetHighlights" => ["planet_visibility_card", "planet_labels_overlay"],
        "BestObservationWindow" => ["observation_window_clock", "horizon_direction_map"],
        "AstrophotographyTip" => ["camera_settings_card", "moon_photo_tip_visual"],
        "WeeklySummary" => ["weekly_checklist_overlay", "closing_cinematic_background"],
        "ShortHook" => ["short_hook_title_card"],
        "StrongestEvent" => [],
        "WhereToLook" => ["short_direction_arrow_overlay"],
        "BestTime" => ["short_best_time_card"],
        "CallToAction" => ["short_cta_end_card"],
        _ => []
    };

    public bool AllowsReuse(string segmentType) => segmentType is "HeroEvent" or "ShortHook" or "StrongestEvent" or "WhereToLook" or "BestTime" or "CallToAction";

    public string BuildPrimaryFocus(WeeklySegmentAssignment assignment, WeeklySkyForecastV2IntelligenceResponse weeklyContext)
    {
        var objects = assignment.AssignedObjects;
        var objectText = objects.Count == 0 ? "weekly sky context" : string.Join(" + ", objects);
        return assignment.SegmentType switch
        {
            "OpeningHook" => $"Emotional promise around the strongest verified weekly sky event: {objectText}",
            "WeeklySkyOverview" => $"Full-week orientation from Skyfield: {weeklyContext.SkyfieldSummary.VisibleObjectCount} visible objects, {weeklyContext.SkyfieldSummary.RecommendedNightsCount} recommended nights",
            "HeroEvent" => $"Strongest verified hero event: {objectText}",
            "MoonHighlights" => $"Moon-only visibility, phase, and viewing emphasis for {ResolveMoonFocus(objects)}",
            "PlanetHighlights" => $"Visible planet-only context for {ResolvePlanetFocus(objects)}",
            "BestObservationWindow" => "Best verified date, time, and viewing-window guidance from Skyfield recommended nights",
            "AstrophotographyTip" => $"Practical photo opportunity using verified weekly target context: {objectText}",
            "WeeklySummary" => "Recap checklist of verified weekly observing opportunities",
            "ShortHook" => $"Short-form attention hook for {objectText}",
            "StrongestEvent" => $"Short-form reveal of {objectText}",
            "WhereToLook" => $"Short-form directional guidance for {objectText}",
            "BestTime" => "Short-form best-time card for the verified hero event",
            "CallToAction" => "Short-form closing CTA tied to this week's verified sky opportunity",
            _ => objectText
        };
    }

    public IReadOnlyList<string> BuildSecondaryFocus(WeeklySegmentAssignment assignment, WeeklySkyForecastV2IntelligenceResponse weeklyContext) => assignment.SegmentType switch
    {
        "WeeklySkyOverview" => ["weekly timeline", "visible-object categories", "recommended observing nights"],
        "MoonHighlights" => ["Moon isolation", "phase/visibility context", "avoid full hero grouping as primary visual"],
        "PlanetHighlights" => ["planet labels", "horizon direction", "avoid Moon-led hero grouping as primary visual"],
        "BestObservationWindow" => ["time window", "date confidence", "directional orientation"],
        "AstrophotographyTip" => ["camera settings", "composition concept", "visual reset"],
        "WeeklySummary" => ["checklist", "montage", "closing emotional reset"],
        "ShortHook" => ["rapid curiosity", "hero promise"],
        "StrongestEvent" => ["event reveal", "verified object grouping"],
        "WhereToLook" => ["direction", "horizon cue"],
        "BestTime" => ["time", "date"],
        "CallToAction" => ["follow-up", "reminder"],
        _ => assignment.AssignedObjects.Count > 0 ? assignment.AssignedObjects : [weeklyContext.Region]
    };

    public string BuildReuseReason(string segmentType, bool reuseAllowed) => segmentType switch
    {
        "OpeningHook" => "May reuse the hero event concept, but not the same Stellarium frame as the sole main visual.",
        "WeeklySkyOverview" => "Hero closeup reuse is not allowed as main visual; segment needs a map/timeline orientation reset.",
        "HeroEvent" => "Hero event reuse is allowed because this segment is the primary observation story.",
        "MoonHighlights" => "Reuse is limited to Moon-isolated assets, not the full hero grouping.",
        "PlanetHighlights" => "Reuse is limited to planet-only context and labels.",
        "BestObservationWindow" => "Primary reuse is not allowed; practical guidance needs clock/map graphics.",
        "AstrophotographyTip" => "Requires educational or cinematic reset assets rather than another hero screenshot.",
        "WeeklySummary" => "Requires montage/checklist closure rather than a single hero frame.",
        _ when reuseAllowed => "Short-form may strongly reuse hero event material while changing narrative function.",
        _ => "Reuse restricted to avoid repeated hero framing."
    };

    public bool IsAssetExpansionDefaultRequired(string segmentType) => GetRequiredNewAssets(segmentType).Count > 0 && segmentType != "HeroEvent" && segmentType != "StrongestEvent";

    public static IReadOnlyList<string> FilterMoonOnly(IReadOnlyList<string> objects) => objects.Where(x => x.Equals("MOON", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public static IReadOnlyList<string> FilterPlanets(IReadOnlyList<string> objects) => objects.Where(PlanetCodes.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string ResolveMoonFocus(IReadOnlyList<string> objects) => FilterMoonOnly(objects).Count > 0 ? "MOON" : "Moon visibility fallback";
    private static string ResolvePlanetFocus(IReadOnlyList<string> objects)
    {
        var planets = FilterPlanets(objects);
        return planets.Count == 0 ? "visible planets" : string.Join(" + ", planets);
    }
}

public sealed class SegmentVisualDiversityAnalyzer
{
    private static readonly (string Source, int Min, int Max)[] LongformTargets =
    [
        ("Stellarium", 45, 60),
        ("AICinematic", 15, 25),
        ("NASA/JWST", 10, 15),
        ("MotionGraphics", 10, 15)
    ];

    public int ScoreRepetition(WeeklySegmentAssignment current, WeeklySegmentAssignment? previous, WeeklyEpisodeSegment episodeSegment, WeeklyEpisodeSegment? previousEpisodeSegment)
    {
        if (previous is null)
            return 0;

        var score = 0;
        if (SameSet(current.AssignedObjects, previous.AssignedObjects)) score += 20;
        if (previousEpisodeSegment is not null && episodeSegment.VisualSourcePreference == previousEpisodeSegment.VisualSourcePreference) score += 15;
        if (SameSet(current.SuggestedRenderScenes, previous.SuggestedRenderScenes)) score += 20;
        if (episodeSegment.NarrationStyle == previousEpisodeSegment?.NarrationStyle) score += 15;
        if (current.SuggestedRenderScenes.Count > 0 && current.SuggestedRenderScenes.Any(x => previous.SuggestedRenderScenes.Contains(x, StringComparer.OrdinalIgnoreCase))) score += 30;
        return Math.Clamp(score, 0, 100);
    }

    public IReadOnlyList<SegmentVisualSourceBalanceItem> AnalyzeLongformBalance(IReadOnlyList<DiversifiedSegmentAssignment> assignments)
    {
        var weights = LongformTargets.ToDictionary(x => x.Source, _ => 0d, StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in assignments)
        {
            var components = ResolveComponents(assignment.DiversifiedVisualSource);
            if (components.Count == 0)
                continue;
            var contribution = 1d / components.Count;
            foreach (var component in components)
            {
                if (weights.ContainsKey(component))
                    weights[component] += contribution;
            }
        }

        var denominator = Math.Max(1, assignments.Count);
        return LongformTargets.Select(target =>
        {
            var percent = Math.Round(weights[target.Source] / denominator * 100d, 1);
            var warnings = new List<string>();
            var status = percent >= target.Min && percent <= target.Max ? "WithinTarget" : "NeedsAssetExpansion";
            if (status != "WithinTarget")
                warnings.Add($"{target.Source} mix {percent:0.#}% is outside approved {target.Min}-{target.Max}% range; plan should expand/reset assets but not fail this phase.");
            return new SegmentVisualSourceBalanceItem(target.Source, percent, target.Min, target.Max, status, warnings);
        }).ToList();
    }

    public static string RiskBand(int score) => score <= 30 ? "Low" : score <= 60 ? "Medium" : "High";

    private static IReadOnlyList<string> ResolveComponents(string source)
    {
        var components = new List<string>();
        if (source.Contains("Stellarium", StringComparison.OrdinalIgnoreCase)) components.Add("Stellarium");
        if (source.Contains("AICinematic", StringComparison.OrdinalIgnoreCase) || source.Contains("AI", StringComparison.OrdinalIgnoreCase)) components.Add("AICinematic");
        if (source.Contains("NASA", StringComparison.OrdinalIgnoreCase) || source.Contains("JWST", StringComparison.OrdinalIgnoreCase)) components.Add("NASA/JWST");
        if (source.Contains("MotionGraphics", StringComparison.OrdinalIgnoreCase)) components.Add("MotionGraphics");
        if (source.Equals("Hybrid", StringComparison.OrdinalIgnoreCase)) components.AddRange(["Stellarium", "AICinematic"]);
        return components.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool SameSet(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        if (first.Count == 0 && second.Count == 0)
            return true;
        if (first.Count != second.Count)
            return false;
        return first.All(x => second.Contains(x, StringComparer.OrdinalIgnoreCase));
    }
}

public sealed class SegmentPacingDiversityAnalyzer
{
    public int ScoreRetention(
        DiversifiedSegmentAssignment current,
        IReadOnlyList<DiversifiedSegmentAssignment> priorAssignments,
        WeeklyEpisodeSegment episodeSegment,
        IReadOnlyList<WeeklyEpisodeSegment> episodeSegments,
        IReadOnlyList<string> heroObjects)
    {
        var score = 0;
        if (CountConsecutiveStellariumHeavy(priorAssignments.Append(current).ToList()) > 2) score += 25;
        if (heroObjects.Count > 0 && priorAssignments.Append(current).Count(x => UsesHeroEvent(x.OriginalAssignedObjects, heroObjects)) > 2) score += 25;
        if (SecondsSinceEmotionalReset(episodeSegment, episodeSegments, priorAssignments.Append(current).ToList()) > 120) score += 20;
        if (current.SegmentType is "BestObservationWindow" or "WhereToLook" or "BestTime" && !HasRecentMotionContext(priorAssignments)) score += 15;
        if (!HasRecentVisualReset(priorAssignments.Append(current).ToList())) score += 15;
        return Math.Clamp(score, 0, 100);
    }

    private static int CountConsecutiveStellariumHeavy(IReadOnlyList<DiversifiedSegmentAssignment> assignments)
    {
        var count = 0;
        for (var i = assignments.Count - 1; i >= 0; i--)
        {
            if (!assignments[i].DiversifiedVisualSource.Contains("Stellarium", StringComparison.OrdinalIgnoreCase))
                break;
            count++;
        }
        return count;
    }

    private static bool UsesHeroEvent(IReadOnlyList<string> objects, IReadOnlyList<string> heroObjects) =>
        heroObjects.Count > 0 && objects.Count == heroObjects.Count && objects.All(x => heroObjects.Contains(x, StringComparer.OrdinalIgnoreCase));

    private static int SecondsSinceEmotionalReset(WeeklyEpisodeSegment current, IReadOnlyList<WeeklyEpisodeSegment> episodeSegments, IReadOnlyList<DiversifiedSegmentAssignment> assignments)
    {
        var currentIndex = episodeSegments.ToList().FindIndex(x => x.SegmentId.Equals(current.SegmentId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            return 0;

        var seconds = 0;
        for (var i = currentIndex; i >= 0; i--)
        {
            var segment = episodeSegments[i];
            var diversified = assignments.FirstOrDefault(x => x.SegmentId.Equals(segment.SegmentId, StringComparison.OrdinalIgnoreCase));
            if (diversified is not null && diversified.DiversifiedVisualRole is "emotional_opening" or "closing_recap")
                return seconds;
            seconds += segment.TargetDurationSeconds;
        }
        return seconds;
    }

    private static bool HasRecentMotionContext(IEnumerable<DiversifiedSegmentAssignment> priorAssignments) => priorAssignments.TakeLast(2).Any(x =>
        x.DiversifiedVisualSource.Contains("MotionGraphics", StringComparison.OrdinalIgnoreCase)
        || x.DiversifiedVisualRole.Equals("contextual_orientation", StringComparison.OrdinalIgnoreCase));

    private static bool HasRecentVisualReset(IReadOnlyList<DiversifiedSegmentAssignment> assignments) => assignments.TakeLast(4).Any(x =>
        x.DiversifiedVisualSource.Contains("AI", StringComparison.OrdinalIgnoreCase)
        || x.DiversifiedVisualSource.Contains("NASA", StringComparison.OrdinalIgnoreCase)
        || x.DiversifiedVisualSource.Contains("JWST", StringComparison.OrdinalIgnoreCase));
}

public sealed class SegmentDiversificationPersister
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<string> WriteAsync(WeeklySegmentDiversificationPlan plan, string workingDirectoryRoot, CancellationToken cancellationToken)
    {
        var episodeDirectory = Path.Combine(workingDirectoryRoot, "episode");
        Directory.CreateDirectory(episodeDirectory);
        var path = Path.Combine(episodeDirectory, "weekly-segment-diversification-plan.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        return path;
    }
}

public sealed class WeeklySegmentDiversificationService(
    SegmentDiversificationPolicy policy,
    SegmentVisualDiversityAnalyzer visualAnalyzer,
    SegmentPacingDiversityAnalyzer pacingAnalyzer,
    SegmentDiversificationPersister persister,
    ILogger<WeeklySegmentDiversificationService> logger)
{
    public async Task<(WeeklySegmentDiversificationPlan Plan, string Path)> DiversifyAndPersistAsync(
        WeeklySegmentClassificationPlan classificationPlan,
        WeeklyEpisodeArchitectureResult episodeArchitecture,
        WeeklyHybridScenePlanPackage? scenePlan,
        ImageSequencePlan? imageSequencePlan,
        IReadOnlyList<CinematicSceneFramePlan>? cinematicFramePlans,
        WeeklySkyForecastV2IntelligenceResponse weeklyContext,
        string workingDirectoryRoot,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("SEGMENT_DIVERSIFICATION_START pipelineRunId={PipelineRunId} longformSegments={LongformSegments} shortformSegments={ShortformSegments}", classificationPlan.PipelineRunId, classificationPlan.LongformAssignments.Count, classificationPlan.ShortformAssignments.Count);

        var warnings = new List<string>();
        var longform = DiversifyEpisode(classificationPlan.LongformAssignments, episodeArchitecture.LongFormPlan, classificationPlan.HeroEventObjects, scenePlan, imageSequencePlan, cinematicFramePlans, weeklyContext, warnings);
        var shortform = DiversifyEpisode(classificationPlan.ShortformAssignments, episodeArchitecture.ShortFormPlan, classificationPlan.HeroEventObjects, scenePlan, imageSequencePlan, cinematicFramePlans, weeklyContext, warnings);
        var balance = visualAnalyzer.AnalyzeLongformBalance(longform);
        foreach (var item in balance)
        {
            logger.LogInformation("VISUAL_SOURCE_BALANCE_ANALYSIS visualSource={VisualSource} percent={Percent} minTargetPercent={MinTargetPercent} maxTargetPercent={MaxTargetPercent} status={Status}", item.VisualSource, item.Percent, item.MinTargetPercent, item.MaxTargetPercent, item.Status);
            warnings.AddRange(item.Warnings);
        }

        if (longform.Count != 8) warnings.Add($"Expected 8 diversified longform segments but found {longform.Count}.");
        if (shortform.Count != 5) warnings.Add($"Expected 5 diversified shortform segments but found {shortform.Count}.");
        warnings.AddRange(classificationPlan.ValidationWarnings.Select(x => $"Classification validation carried forward: {x}"));
        warnings.AddRange(scenePlan?.SceneWarnings.Select(x => $"Scene plan warning carried forward: {x}") ?? []);

        var all = longform.Concat(shortform).ToList();
        var assetExpansionRequired = all.Any(x => x.AssetExpansionRequired) || balance.Any(x => x.Status == "NeedsAssetExpansion");
        var plan = new WeeklySegmentDiversificationPlan(
            classificationPlan.PipelineRunId,
            classificationPlan.RegionId,
            classificationPlan.Language,
            classificationPlan.WeekStartDate,
            DateTime.UtcNow,
            longform,
            shortform,
            balance,
            SegmentDiversificationReady: longform.Count == 8 && shortform.Count == 5,
            DiversifiedLongformSegmentCount: longform.Count,
            DiversifiedShortformSegmentCount: shortform.Count,
            AssetExpansionRequired: assetExpansionRequired,
            HighestRetentionRiskScore: all.Count == 0 ? 0 : all.Max(x => x.RetentionRiskScore),
            HighestRepetitionRiskScore: all.Count == 0 ? 0 : all.Max(x => x.RepetitionRiskScore),
            ValidationWarnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        var path = await persister.WriteAsync(plan, workingDirectoryRoot, cancellationToken);
        logger.LogInformation("SEGMENT_DIVERSIFICATION_PLAN_WRITTEN path={Path} segmentDiversificationReady={SegmentDiversificationReady} assetExpansionRequired={AssetExpansionRequired}", path, plan.SegmentDiversificationReady, plan.AssetExpansionRequired);
        logger.LogInformation("SEGMENT_DIVERSIFICATION_COMPLETE longformSegments={LongformSegments} shortformSegments={ShortformSegments} highestRepetitionRiskScore={HighestRepetitionRiskScore} highestRetentionRiskScore={HighestRetentionRiskScore} assetExpansionRequired={AssetExpansionRequired}", plan.DiversifiedLongformSegmentCount, plan.DiversifiedShortformSegmentCount, plan.HighestRepetitionRiskScore, plan.HighestRetentionRiskScore, plan.AssetExpansionRequired);
        return (plan, path);
    }

    private List<DiversifiedSegmentAssignment> DiversifyEpisode(
        IReadOnlyList<WeeklySegmentAssignment> assignments,
        WeeklyEpisodePlan episodePlan,
        IReadOnlyList<string> heroObjects,
        WeeklyHybridScenePlanPackage? scenePlan,
        ImageSequencePlan? imageSequencePlan,
        IReadOnlyList<CinematicSceneFramePlan>? cinematicFramePlans,
        WeeklySkyForecastV2IntelligenceResponse weeklyContext,
        List<string> validationWarnings)
    {
        var diversified = new List<DiversifiedSegmentAssignment>();
        var segmentsById = episodePlan.Segments.ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];
            if (!segmentsById.TryGetValue(assignment.SegmentId, out var episodeSegment))
            {
                validationWarnings.Add($"Episode segment not found for classification segmentId={assignment.SegmentId}.");
                continue;
            }

            var previousAssignment = i > 0 ? assignments[i - 1] : null;
            var previousEpisodeSegment = i > 0 ? episodePlan.Segments[i - 1] : null;
            var repetitionRisk = visualAnalyzer.ScoreRepetition(assignment, previousAssignment, episodeSegment, previousEpisodeSegment);
            var reuseAllowed = policy.AllowsReuse(assignment.SegmentType);
            var visualSource = policy.GetVisualSource(assignment.SegmentType);
            var requiredAssets = policy.GetRequiredNewAssets(assignment.SegmentType).ToList();
            var existingReusableAssets = ResolveExistingReusableAssets(assignment, scenePlan, imageSequencePlan, cinematicFramePlans);
            var warnings = BuildAssignmentWarnings(assignment, visualSource, repetitionRisk, requiredAssets, existingReusableAssets, heroObjects).ToList();
            var assetExpansionRequired = policy.IsAssetExpansionDefaultRequired(assignment.SegmentType) || repetitionRisk > 60 || requiredAssets.Except(existingReusableAssets, StringComparer.OrdinalIgnoreCase).Any();

            var draft = new DiversifiedSegmentAssignment(
                assignment.SegmentId,
                assignment.SegmentType,
                assignment.AssignedObjects,
                policy.BuildPrimaryFocus(assignment, weeklyContext),
                policy.BuildSecondaryFocus(assignment, weeklyContext),
                visualSource,
                policy.GetVisualRole(assignment.SegmentType),
                policy.GetNarrationRole(assignment.SegmentType),
                episodeSegment.PacingRole.ToString(),
                reuseAllowed,
                policy.BuildReuseReason(assignment.SegmentType, reuseAllowed),
                assetExpansionRequired,
                requiredAssets,
                existingReusableAssets,
                repetitionRisk,
                0,
                BuildDiversificationReason(assignment, visualSource),
                assetExpansionRequired ? "NeedsAssetExpansion" : "Diversified",
                warnings);

            var retentionRisk = pacingAnalyzer.ScoreRetention(draft, diversified, episodeSegment, episodePlan.Segments, heroObjects);
            if (retentionRisk > 60)
            {
                warnings.Add($"High retention risk ({retentionRisk}) from repeated source/event pacing; asset reset is required.");
                assetExpansionRequired = true;
            }

            var final = draft with
            {
                RetentionRiskScore = retentionRisk,
                AssetExpansionRequired = assetExpansionRequired,
                ProductionStatus = assetExpansionRequired ? "NeedsAssetExpansion" : "Diversified",
                Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
            diversified.Add(final);

            logger.LogInformation("SEGMENT_DIVERSIFIED segmentType={SegmentType} diversifiedPrimaryFocus={DiversifiedPrimaryFocus} diversifiedVisualSource={DiversifiedVisualSource} repetitionRiskScore={RepetitionRiskScore} retentionRiskScore={RetentionRiskScore} assetExpansionRequired={AssetExpansionRequired}", final.SegmentType, final.DiversifiedPrimaryFocus, final.DiversifiedVisualSource, final.RepetitionRiskScore, final.RetentionRiskScore, final.AssetExpansionRequired);
            logger.LogInformation("SEGMENT_REPETITION_RISK segmentType={SegmentType} diversifiedPrimaryFocus={DiversifiedPrimaryFocus} diversifiedVisualSource={DiversifiedVisualSource} repetitionRiskScore={RepetitionRiskScore} retentionRiskScore={RetentionRiskScore} riskBand={RiskBand} assetExpansionRequired={AssetExpansionRequired}", final.SegmentType, final.DiversifiedPrimaryFocus, final.DiversifiedVisualSource, final.RepetitionRiskScore, final.RetentionRiskScore, SegmentVisualDiversityAnalyzer.RiskBand(final.RepetitionRiskScore), final.AssetExpansionRequired);
            logger.LogInformation("SEGMENT_RETENTION_RISK segmentType={SegmentType} diversifiedPrimaryFocus={DiversifiedPrimaryFocus} diversifiedVisualSource={DiversifiedVisualSource} repetitionRiskScore={RepetitionRiskScore} retentionRiskScore={RetentionRiskScore} assetExpansionRequired={AssetExpansionRequired}", final.SegmentType, final.DiversifiedPrimaryFocus, final.DiversifiedVisualSource, final.RepetitionRiskScore, final.RetentionRiskScore, final.AssetExpansionRequired);
        }

        return diversified;
    }

    private static IReadOnlyList<string> ResolveExistingReusableAssets(WeeklySegmentAssignment assignment, WeeklyHybridScenePlanPackage? scenePlan, ImageSequencePlan? imageSequencePlan, IReadOnlyList<CinematicSceneFramePlan>? cinematicFramePlans)
    {
        var assets = new List<string>();
        assets.AddRange(assignment.SuggestedRenderScenes.Select(x => $"scene:{x}"));
        if (scenePlan is not null)
        {
            assets.AddRange(scenePlan.ScenePlans
                .Where(scene => assignment.SuggestedRenderScenes.Contains(scene.SceneCode, StringComparer.OrdinalIgnoreCase)
                                || assignment.AssignedObjects.Count > 0 && scene.ObjectCodes.Any(x => assignment.AssignedObjects.Contains(x, StringComparer.OrdinalIgnoreCase)))
                .Select(scene => $"scene:{scene.SceneCode}"));
            assets.AddRange(scenePlan.AssetNeeds
                .Where(asset => assignment.AssignedObjects.Contains(asset.ObjectCode, StringComparer.OrdinalIgnoreCase))
                .Select(asset => $"asset:{asset.AssetCode}"));
        }

        if (imageSequencePlan is not null)
        {
            assets.AddRange(imageSequencePlan.Sequences
                .Where(sequence => assignment.SuggestedRenderScenes.Contains(sequence.RenderSceneCode, StringComparer.OrdinalIgnoreCase))
                .Select(sequence => $"frameScreenshot:{sequence.FrameId}"));
        }

        if (cinematicFramePlans is not null)
        {
            assets.AddRange(cinematicFramePlans
                .SelectMany(plan => plan.FramePlans)
                .Where(frame => assignment.SuggestedRenderScenes.Contains(frame.RenderSceneCode, StringComparer.OrdinalIgnoreCase)
                                || assignment.AssignedObjects.Count > 0 && frame.TargetObjects.Any(x => assignment.AssignedObjects.Contains(x, StringComparer.OrdinalIgnoreCase)))
                .Select(frame => $"cinematicFrame:{frame.FrameId}"));
        }

        return assets.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> BuildAssignmentWarnings(
        WeeklySegmentAssignment assignment,
        string visualSource,
        int repetitionRisk,
        IReadOnlyList<string> requiredAssets,
        IReadOnlyList<string> existingReusableAssets,
        IReadOnlyList<string> heroObjects)
    {
        if (repetitionRisk > 60)
            yield return $"High repetition risk ({repetitionRisk}) from adjacent object/source/scene reuse; mark assetExpansionRequired=true.";
        if (assignment.SegmentType == "WeeklySkyOverview" && existingReusableAssets.Any(x => x.Contains("hero", StringComparison.OrdinalIgnoreCase)))
            yield return "WeeklySkyOverview must not use a hero closeup as the main visual; use weekly map/timeline assets.";
        if (assignment.SegmentType == "MoonHighlights" && assignment.AssignedObjects.Except(["MOON"], StringComparer.OrdinalIgnoreCase).Any())
            yield return "MoonHighlights must isolate Moon-focused visuals instead of replaying the full hero grouping.";
        if (assignment.SegmentType == "PlanetHighlights" && assignment.AssignedObjects.Any(x => x.Equals("MOON", StringComparison.OrdinalIgnoreCase)))
            yield return "PlanetHighlights must prioritize planet-only context and avoid Moon-led hero grouping.";
        if (assignment.SegmentType == "BestObservationWindow" && visualSource.Contains("Stellarium", StringComparison.OrdinalIgnoreCase))
            yield return "BestObservationWindow primary visual must be MotionGraphics, not the hero event screenshot.";
        if (requiredAssets.Count > 0 && requiredAssets.Except(existingReusableAssets, StringComparer.OrdinalIgnoreCase).Any())
            yield return $"New planning assets required: {string.Join(", ", requiredAssets.Except(existingReusableAssets, StringComparer.OrdinalIgnoreCase))}.";
        if (heroObjects.Count > 0 && assignment.AssignedObjects.Count == heroObjects.Count && assignment.AssignedObjects.All(x => heroObjects.Contains(x, StringComparer.OrdinalIgnoreCase)) && assignment.SegmentType is not "HeroEvent" and not "StrongestEvent" and not "ShortHook")
            yield return "This segment shares the hero event object set; diversify focus, narration role, or visual source to avoid pacing fatigue.";
    }

    private static string BuildDiversificationReason(WeeklySegmentAssignment assignment, string visualSource) => assignment.SegmentType switch
    {
        "OpeningHook" => "Separates the emotional promise from literal hero-frame reuse by using a cinematic/hybrid visual reset.",
        "WeeklySkyOverview" => "Reframes the same verified week as a map/timeline orientation rather than another closeup event shot.",
        "HeroEvent" => "Preserves deterministic astronomy truth by keeping the classified strongest event as the primary observation story.",
        "MoonHighlights" => "Narrows the classified event to a lunar-only storytelling lane so Moon content does not replay the complete hero grouping.",
        "PlanetHighlights" => "Narrows the classified event to visible planet context and labels so planet guidance has a distinct visual role.",
        "BestObservationWindow" => "Moves from object spectacle to practical date/time/direction graphics for viewer utility.",
        "AstrophotographyTip" => "Adds an educational visual reset and asset-expansion signal without creating or downloading assets.",
        "WeeklySummary" => "Ends with checklist/montage planning instead of a single repeated hero frame.",
        _ => $"Short-form role is diversified as {visualSource} while preserving the verified hero-event source."
    };
}
