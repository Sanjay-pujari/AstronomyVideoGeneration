using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentDiversification;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualAssetSourceType
{
    Stellarium,
    AICinematic,
    NASA,
    JWST,
    MotionGraphics,
    EducationalOverlay,
    Hybrid
}

public sealed record VisualAssetRequirement(
    string RequirementId,
    string AssetCategory,
    string AssetIntent,
    string UsageRole,
    VisualAssetSourceType PreferredSource,
    bool CanReuseFrameScreenshot,
    bool RequiresFutureGeneration,
    string ProductionInstruction);

public sealed record VisualAssetSourcePlan(
    VisualAssetSourceType SourceType,
    string UsageRole,
    string AssetIntent,
    string? TargetNasaAssetCategory = null,
    string? TargetJwstAssetCategory = null,
    string? CinematicSceneIntent = null,
    string? EmotionalTone = null,
    string? LightingStyle = null,
    string? CompositionStyle = null,
    string? CinematicPurpose = null,
    IReadOnlyList<string>? MotionGraphicElements = null,
    IReadOnlyList<string>? EducationalOverlayElements = null);

public sealed record SegmentRetentionMetadata(
    bool PacingResetCandidate,
    bool EmotionalResetCandidate,
    bool RetentionBoostCandidate,
    bool ViralClipCandidate,
    bool ThumbnailCandidate);

public sealed record SegmentVisualAssetPlan(
    string SegmentId,
    string SegmentType,
    VisualAssetSourceType PrimaryVisualSource,
    VisualAssetSourceType? SecondaryVisualSource,
    VisualAssetSourceType? TertiaryVisualSource,
    IReadOnlyList<VisualAssetRequirement> RequiredVisualAssets,
    IReadOnlyList<string> ReusableFrameScreenshots,
    bool RequiresNewAssets,
    int AssetPriority,
    int EducationalImportance,
    int EmotionalImportance,
    int CinematicImportance,
    int RetentionImportance,
    int EstimatedScreenTimeSeconds,
    string TransitionStyle,
    string VisualComplexity,
    string ProductionStatus,
    IReadOnlyList<string> AssignedObjects,
    IReadOnlyList<VisualAssetSourcePlan> SourcePlans,
    SegmentRetentionMetadata RetentionMetadata,
    IReadOnlyList<string> Warnings);

public sealed record VisualSourceMixItem(string VisualSource, double Percent, double TargetMinPercent, double TargetMaxPercent, string Status, IReadOnlyList<string> Warnings);

public sealed record WeeklyVisualBalanceReport(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<VisualSourceMixItem> LongformMixPercentages,
    IReadOnlyList<VisualSourceMixItem> ShortformMixPercentages,
    IReadOnlyList<string> OverusedSourceDetection,
    IReadOnlyList<string> UnderusedSourceDetection,
    IReadOnlyList<string> PacingFatigueIndicators,
    IReadOnlyList<string> VisualRepetitionIndicators,
    IReadOnlyList<string> MissingAssetCategories,
    IReadOnlyList<string> RetentionImprovementSuggestions,
    bool VisualBalanceHealthy,
    int AICinematicAssetsPlanned = 0,
    int AICinematicAssetsGenerated = 0,
    int AICinematicProductionReadyCount = 0,
    int RemainingAICinematicGap = 0,
    string VisualBalanceAfterAICinematicAssets = "NotEvaluated");

public sealed record WeeklyVisualAssetPlan(
    Guid PipelineRunId,
    string RegionId,
    string Language,
    DateOnly WeekStartDate,
    DateTime GeneratedAtUtc,
    IReadOnlyList<string> InputArtifactsUsed,
    IReadOnlyList<SegmentVisualAssetPlan> LongformSegmentVisualPlans,
    IReadOnlyList<SegmentVisualAssetPlan> ShortformSegmentVisualPlans,
    bool VisualAssetPlanningReady,
    int PlannedVisualAssetCount,
    int PlannedMotionGraphicsCount,
    int PlannedEducationalOverlayCount,
    int PlannedAICinematicCount,
    int PlannedNASAAssetCount,
    int PlannedJWSTAssetCount,
    IReadOnlyList<string> ValidationWarnings);

public sealed class VisualAssetPriorityScorer
{
    public int Score(WeeklySegmentAssignment assignment, DiversifiedSegmentAssignment diversified, bool isShortForm)
    {
        var heroImportance = assignment.SegmentType is "HeroEvent" or "StrongestEvent" ? 24 : assignment.SegmentType is "OpeningHook" or "ShortHook" ? 20 : 10;
        var rarity = assignment.AssignedEventType.Contains("Conjunction", StringComparison.OrdinalIgnoreCase) || assignment.AssignedEventType.Contains("Grouping", StringComparison.OrdinalIgnoreCase) ? 16 : 8;
        var emotional = assignment.SegmentType is "OpeningHook" or "WeeklySummary" or "ShortHook" or "CallToAction" ? 16 : 10;
        var education = assignment.SegmentType is "WeeklySkyOverview" or "MoonHighlights" or "PlanetHighlights" or "BestObservationWindow" or "AstrophotographyTip" or "WhereToLook" or "BestTime" ? 14 : 7;
        var beauty = RequiresSource(assignment.SegmentType, VisualAssetSourceType.AICinematic) || RequiresSource(assignment.SegmentType, VisualAssetSourceType.Stellarium) ? 13 : 8;
        var retention = diversified.RetentionRiskScore > 60 ? 15 : assignment.SegmentType is "HeroEvent" or "ShortHook" or "StrongestEvent" ? 14 : 9;
        var viral = isShortForm ? 14 : assignment.SegmentType is "OpeningHook" or "HeroEvent" ? 10 : 5;
        return Math.Clamp(heroImportance + rarity + emotional + education + beauty + retention + viral, 0, 100);
    }

    private static bool RequiresSource(string segmentType, VisualAssetSourceType source) => VisualAssetPlanningRules.GetSources(segmentType).Primary == source
        || VisualAssetPlanningRules.GetSources(segmentType).Secondary == source
        || VisualAssetPlanningRules.GetSources(segmentType).Tertiary == source;
}

public sealed class VisualAssetMixAnalyzer
{
    private static readonly Dictionary<VisualAssetSourceType, (double Min, double Max)> LongformTargets = new()
    {
        [VisualAssetSourceType.Stellarium] = (45, 60),
        [VisualAssetSourceType.AICinematic] = (15, 25),
        [VisualAssetSourceType.NASA] = (5, 10),
        [VisualAssetSourceType.JWST] = (5, 10),
        [VisualAssetSourceType.MotionGraphics] = (10, 15)
    };

    private static readonly Dictionary<VisualAssetSourceType, (double Min, double Max)> ShortformTargets = new()
    {
        [VisualAssetSourceType.Stellarium] = (40, 50),
        [VisualAssetSourceType.AICinematic] = (25, 35),
        [VisualAssetSourceType.MotionGraphics] = (15, 20),
        [VisualAssetSourceType.NASA] = (2.5, 5),
        [VisualAssetSourceType.JWST] = (2.5, 5)
    };

    public WeeklyVisualBalanceReport Analyze(Guid pipelineRunId, IReadOnlyList<SegmentVisualAssetPlan> longform, IReadOnlyList<SegmentVisualAssetPlan> shortform)
    {
        var longformMix = CalculateMix(longform, LongformTargets);
        var shortformMix = CalculateMix(shortform, ShortformTargets);
        var overused = longformMix.Concat(shortformMix).Where(x => x.Status == "Overused").Select(x => $"{x.VisualSource} is above target at {x.Percent:0.##}%.").Distinct().ToList();
        var underused = longformMix.Concat(shortformMix).Where(x => x.Status == "Underused").Select(x => $"{x.VisualSource} is below target at {x.Percent:0.##}%.").Distinct().ToList();
        var fatigue = DetectPacingFatigue(longform).Concat(DetectPacingFatigue(shortform)).ToList();
        var repetition = DetectRepetition(longform).Concat(DetectRepetition(shortform)).ToList();
        var missing = DetectMissingCategories(longform.Concat(shortform).ToList());
        var suggestions = BuildSuggestions(overused, underused, fatigue, repetition, missing);
        var healthy = overused.Count == 0 && underused.Count == 0 && fatigue.Count == 0 && repetition.Count == 0 && missing.Count == 0;
        return new WeeklyVisualBalanceReport(pipelineRunId, DateTime.UtcNow, longformMix, shortformMix, overused, underused, fatigue, repetition, missing, suggestions, healthy);
    }

    private static IReadOnlyList<VisualSourceMixItem> CalculateMix(IReadOnlyList<SegmentVisualAssetPlan> plans, IReadOnlyDictionary<VisualAssetSourceType, (double Min, double Max)> targets)
    {
        var weights = targets.Keys.ToDictionary(x => x, _ => 0d);
        foreach (var plan in plans)
        {
            Add(weights, plan.PrimaryVisualSource, plan.EstimatedScreenTimeSeconds * 0.7);
            if (plan.SecondaryVisualSource is { } secondary) Add(weights, secondary, plan.EstimatedScreenTimeSeconds * 0.2);
            if (plan.TertiaryVisualSource is { } tertiary) Add(weights, tertiary, plan.EstimatedScreenTimeSeconds * 0.1);
        }

        var total = weights.Values.Sum();
        return targets.Select(target =>
        {
            var percent = total <= 0 ? 0 : Math.Round(weights.GetValueOrDefault(target.Key) / total * 100, 2);
            var warnings = new List<string>();
            var status = "OnTarget";
            if (percent < target.Value.Min) { status = "Underused"; warnings.Add($"Target minimum is {target.Value.Min:0.##}%."); }
            if (percent > target.Value.Max) { status = "Overused"; warnings.Add($"Target maximum is {target.Value.Max:0.##}%."); }
            return new VisualSourceMixItem(target.Key.ToString(), percent, target.Value.Min, target.Value.Max, status, warnings);
        }).ToList();
    }

    private static void Add(Dictionary<VisualAssetSourceType, double> weights, VisualAssetSourceType source, double value)
    {
        if (source == VisualAssetSourceType.Hybrid)
        {
            weights[VisualAssetSourceType.Stellarium] = weights.GetValueOrDefault(VisualAssetSourceType.Stellarium) + value * 0.5;
            weights[VisualAssetSourceType.AICinematic] = weights.GetValueOrDefault(VisualAssetSourceType.AICinematic) + value * 0.5;
            return;
        }
        if (!weights.ContainsKey(source)) return;
        weights[source] += value;
    }

    private static IEnumerable<string> DetectPacingFatigue(IReadOnlyList<SegmentVisualAssetPlan> plans)
    {
        for (var i = 1; i < plans.Count; i++)
            if (plans[i - 1].PrimaryVisualSource == plans[i].PrimaryVisualSource)
                yield return $"Adjacent segments {plans[i - 1].SegmentId} and {plans[i].SegmentId} share primary source {plans[i].PrimaryVisualSource}.";
    }

    private static IEnumerable<string> DetectRepetition(IReadOnlyList<SegmentVisualAssetPlan> plans) => plans
        .Where(x => x.ReusableFrameScreenshots.Count > 2)
        .Select(x => $"{x.SegmentId} has {x.ReusableFrameScreenshots.Count} reusable screenshots; curate later to avoid repeated hero frames.");

    private static IReadOnlyList<string> DetectMissingCategories(IReadOnlyList<SegmentVisualAssetPlan> plans)
    {
        var present = plans.SelectMany(p => p.SourcePlans.Select(s => s.SourceType)).ToHashSet();
        return Enum.GetValues<VisualAssetSourceType>().Where(x => !present.Contains(x)).Select(x => $"No planned {x} source plan exists.").ToList();
    }

    private static IReadOnlyList<string> BuildSuggestions(IReadOnlyList<string> overused, IReadOnlyList<string> underused, IReadOnlyList<string> fatigue, IReadOnlyList<string> repetition, IReadOnlyList<string> missing)
    {
        var suggestions = new List<string>();
        if (overused.Count > 0) suggestions.Add("Shift lower-priority repeated sources into motion graphics, AI cinematic resets, or educational cards during future asset generation.");
        if (underused.Count > 0) suggestions.Add("Add source-specific supporting assets only where segment astronomy is already verified; do not invent new targets.");
        if (fatigue.Count > 0) suggestions.Add("Insert pacing resets between adjacent same-source segments using transitions or overlay-first visuals.");
        if (repetition.Count > 0) suggestions.Add("Limit repeated frameScreenshot reuse to hero/short segments and create planned replacement assets later.");
        if (missing.Count > 0) suggestions.Add("Keep missing categories as future optional asset expansion, not generation work in this phase.");
        if (suggestions.Count == 0) suggestions.Add("Visual mix is healthy; preserve current planned source balance during generation.");
        return suggestions;
    }
}

public sealed class VisualAssetPlanningPersister
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public async Task<(string PlanPath, string BalanceReportPath)> WriteAsync(WeeklyVisualAssetPlan plan, WeeklyVisualBalanceReport balanceReport, string workingDirectoryRoot, CancellationToken cancellationToken)
    {
        var episodeDirectory = Path.Combine(workingDirectoryRoot, "episode");
        Directory.CreateDirectory(episodeDirectory);
        var planPath = Path.Combine(episodeDirectory, "weekly-visual-asset-plan.json");
        var balancePath = Path.Combine(episodeDirectory, "weekly-visual-balance-report.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(balancePath, JsonSerializer.Serialize(balanceReport, JsonOptions), cancellationToken);
        return (planPath, balancePath);
    }
}

public sealed class WeeklyVisualAssetPlanningService(
    VisualAssetPriorityScorer scorer,
    VisualAssetMixAnalyzer mixAnalyzer,
    VisualAssetPlanningPersister persister,
    ILogger<WeeklyVisualAssetPlanningService> logger)
{
    public async Task<(WeeklyVisualAssetPlan Plan, WeeklyVisualBalanceReport BalanceReport, string PlanPath, string BalanceReportPath)> PlanAndPersistAsync(
        WeeklySegmentDiversificationPlan diversificationPlan,
        WeeklySegmentClassificationPlan classificationPlan,
        WeeklyEpisodeArchitectureResult episodeArchitecture,
        WeeklyHybridScenePlanPackage? scenePlan,
        ImageSequencePlan? imageSequencePlan,
        IReadOnlyList<CinematicSceneFramePlan>? cinematicFramePlans,
        WeeklySkyForecastV2IntelligenceResponse weeklyContext,
        string workingDirectoryRoot,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("VISUAL_ASSET_PLANNING_START pipelineRunId={PipelineRunId} longformSegments={LongformSegments} shortformSegments={ShortformSegments}", diversificationPlan.PipelineRunId, diversificationPlan.LongformAssignments.Count, diversificationPlan.ShortformAssignments.Count);
        var warnings = new List<string>();
        var frameScreenshots = imageSequencePlan?.Sequences.Select(x => x.ImagePath).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        var longform = BuildPlans(diversificationPlan.LongformAssignments, classificationPlan.LongformAssignments, episodeArchitecture.LongFormPlan, frameScreenshots, scenePlan, cinematicFramePlans, isShortForm: false, warnings);
        var shortform = BuildPlans(diversificationPlan.ShortformAssignments, classificationPlan.ShortformAssignments, episodeArchitecture.ShortFormPlan, frameScreenshots, scenePlan, cinematicFramePlans, isShortForm: true, warnings);
        if (longform.Count != diversificationPlan.LongformAssignments.Count) warnings.Add("Not every longform diversified segment received a visual plan.");
        if (shortform.Count != diversificationPlan.ShortformAssignments.Count) warnings.Add("Not every shortform diversified segment received a visual plan.");
        warnings.AddRange(diversificationPlan.ValidationWarnings.Select(x => $"Diversification validation carried forward: {x}"));
        warnings.AddRange(classificationPlan.ValidationWarnings.Select(x => $"Classification validation carried forward: {x}"));
        warnings.AddRange(weeklyContext.Warnings.Select(x => $"Skyfield response warning carried forward: {x}"));
        if (imageSequencePlan?.ProductionImageSource != "frameScreenshots") warnings.Add("Image sequence production source is not frameScreenshots; visual planning still avoids modifying image pipeline outputs.");

        var balance = mixAnalyzer.Analyze(diversificationPlan.PipelineRunId, longform, shortform);
        logger.LogInformation("VISUAL_BALANCE_ANALYZED visualBalanceHealthy={VisualBalanceHealthy} longformSources={LongformSources} shortformSources={ShortformSources}", balance.VisualBalanceHealthy, balance.LongformMixPercentages.Count, balance.ShortformMixPercentages.Count);
        warnings.AddRange(balance.OverusedSourceDetection.Select(x => $"Visual balance overuse: {x}"));
        warnings.AddRange(balance.UnderusedSourceDetection.Select(x => $"Visual balance underuse: {x}"));

        var allPlans = longform.Concat(shortform).ToList();
        var sourcePlans = allPlans.SelectMany(x => x.SourcePlans).ToList();
        var plan = new WeeklyVisualAssetPlan(
            diversificationPlan.PipelineRunId,
            diversificationPlan.RegionId,
            diversificationPlan.Language,
            diversificationPlan.WeekStartDate,
            DateTime.UtcNow,
            BuildInputArtifactSummary(classificationPlan, diversificationPlan, episodeArchitecture, scenePlan, imageSequencePlan, cinematicFramePlans, weeklyContext, frameScreenshots.Count),
            longform,
            shortform,
            VisualAssetPlanningReady: allPlans.Count == diversificationPlan.DiversifiedLongformSegmentCount + diversificationPlan.DiversifiedShortformSegmentCount && balance.LongformMixPercentages.Count > 0 && balance.ShortformMixPercentages.Count > 0,
            PlannedVisualAssetCount: allPlans.Sum(x => x.RequiredVisualAssets.Count),
            PlannedMotionGraphicsCount: sourcePlans.Count(x => x.SourceType == VisualAssetSourceType.MotionGraphics),
            PlannedEducationalOverlayCount: sourcePlans.Count(x => x.SourceType == VisualAssetSourceType.EducationalOverlay),
            PlannedAICinematicCount: sourcePlans.Count(x => x.SourceType == VisualAssetSourceType.AICinematic),
            PlannedNASAAssetCount: sourcePlans.Count(x => x.SourceType == VisualAssetSourceType.NASA),
            PlannedJWSTAssetCount: sourcePlans.Count(x => x.SourceType == VisualAssetSourceType.JWST),
            ValidationWarnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        var paths = await persister.WriteAsync(plan, balance, workingDirectoryRoot, cancellationToken);
        logger.LogInformation("VISUAL_ASSET_PLAN_WRITTEN planPath={PlanPath} balanceReportPath={BalanceReportPath} visualAssetPlanningReady={VisualAssetPlanningReady}", paths.PlanPath, paths.BalanceReportPath, plan.VisualAssetPlanningReady);
        logger.LogInformation("VISUAL_ASSET_PLANNING_COMPLETE plannedVisualAssetCount={PlannedVisualAssetCount} visualBalanceHealthy={VisualBalanceHealthy}", plan.PlannedVisualAssetCount, balance.VisualBalanceHealthy);
        return (plan, balance, paths.PlanPath, paths.BalanceReportPath);
    }

    private static IReadOnlyList<string> BuildInputArtifactSummary(WeeklySegmentClassificationPlan classificationPlan, WeeklySegmentDiversificationPlan diversificationPlan, WeeklyEpisodeArchitectureResult episodeArchitecture, WeeklyHybridScenePlanPackage? scenePlan, ImageSequencePlan? imageSequencePlan, IReadOnlyList<CinematicSceneFramePlan>? cinematicFramePlans, WeeklySkyForecastV2IntelligenceResponse weeklyContext, int frameScreenshotCount) =>
    [
        $"weekly-segment-diversification-plan: longform={diversificationPlan.LongformAssignments.Count}, shortform={diversificationPlan.ShortformAssignments.Count}",
        $"weekly-segment-classification-plan: longform={classificationPlan.LongformAssignments.Count}, shortform={classificationPlan.ShortformAssignments.Count}",
        $"weekly-episode-plan: longformSegments={episodeArchitecture.LongFormPlan.Segments.Count}, shortformSegments={episodeArchitecture.ShortFormPlan.Segments.Count}",
        $"cinematic-frame-plan: scenePlans={cinematicFramePlans?.Count ?? 0}",
        $"image-sequence-plan: selectedImages={imageSequencePlan?.TotalImages ?? 0}, productionSource={imageSequencePlan?.ProductionImageSource ?? "missing"}",
        $"frameScreenshots: count={frameScreenshotCount}",
        $"Skyfield response: success={weeklyContext.Success}, events={weeklyContext.EventExtractionResult?.ExtractedEvents.Count ?? 0}",
        $"story beats: storyboardPresent={weeklyContext.Storyboard is not null}, narrativeAbstractionPresent={weeklyContext.NarrativeAbstractionPackage is not null}"
    ];

    private IReadOnlyList<SegmentVisualAssetPlan> BuildPlans(IReadOnlyList<DiversifiedSegmentAssignment> diversifiedAssignments, IReadOnlyList<WeeklySegmentAssignment> classifiedAssignments, WeeklyEpisodePlan episodePlan, IReadOnlyList<string> frameScreenshots, WeeklyHybridScenePlanPackage? scenePlan, IReadOnlyList<CinematicSceneFramePlan>? cinematicFramePlans, bool isShortForm, List<string> planWarnings)
    {
        var bySegment = classifiedAssignments.ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        var episodeBySegment = episodePlan.Segments.ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        var plans = new List<SegmentVisualAssetPlan>();
        foreach (var diversified in diversifiedAssignments)
        {
            if (!bySegment.TryGetValue(diversified.SegmentId, out var classified))
            {
                planWarnings.Add($"Missing classification for diversified segment {diversified.SegmentId}.");
                continue;
            }
            episodeBySegment.TryGetValue(diversified.SegmentId, out var episodeSegment);
            var sources = VisualAssetPlanningRules.GetSources(diversified.SegmentType);
            logger.LogInformation("VISUAL_SOURCE_ASSIGNED segmentId={SegmentId} segmentType={SegmentType} primary={PrimaryVisualSource} secondary={SecondaryVisualSource} tertiary={TertiaryVisualSource}", diversified.SegmentId, diversified.SegmentType, sources.Primary, sources.Secondary, sources.Tertiary);
            var requirements = VisualAssetPlanningRules.BuildRequirements(diversified.SegmentId, diversified.SegmentType, sources);
            var reusable = SelectReusableFrameScreenshots(classified, diversified, frameScreenshots, scenePlan, cinematicFramePlans);
            var priority = scorer.Score(classified, diversified, isShortForm);
            logger.LogInformation("VISUAL_PRIORITY_SCORED segmentId={SegmentId} assetPriority={AssetPriority}", diversified.SegmentId, priority);
            var importances = VisualAssetPlanningRules.GetImportanceScores(diversified.SegmentType, isShortForm);
            var sourcePlans = VisualAssetPlanningRules.BuildSourcePlans(diversified.SegmentType, classified.AssignedObjects, sources);
            var warnings = diversified.Warnings.Concat(classified.Warnings).ToList();
            if (reusable.Count == 0 && sources.Primary == VisualAssetSourceType.Stellarium) warnings.Add("No reusable frameScreenshots matched this Stellarium-led segment; future capture planning may be needed, but no capture is attempted now.");
            if (sources.Secondary is VisualAssetSourceType.NASA or VisualAssetSourceType.JWST || sources.Tertiary is VisualAssetSourceType.NASA or VisualAssetSourceType.JWST) warnings.Add("NASA/JWST assets are planned as metadata only; no download is attempted in this phase.");
            var plan = new SegmentVisualAssetPlan(
                diversified.SegmentId,
                diversified.SegmentType,
                sources.Primary,
                sources.Secondary,
                sources.Tertiary,
                requirements,
                reusable,
                RequiresNewAssets: requirements.Any(x => x.RequiresFutureGeneration) || diversified.AssetExpansionRequired,
                AssetPriority: priority,
                importances.Educational,
                importances.Emotional,
                importances.Cinematic,
                importances.Retention,
                episodeSegment?.TargetDurationSeconds ?? EstimateDuration(diversified.SegmentType),
                VisualAssetPlanningRules.GetTransitionStyle(diversified.SegmentType),
                VisualAssetPlanningRules.GetComplexity(diversified.SegmentType, requirements.Count),
                "PlannedOnly_NoMediaGenerated",
                classified.AssignedObjects,
                sourcePlans,
                VisualAssetPlanningRules.GetRetentionMetadata(diversified.SegmentType, priority, isShortForm),
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            logger.LogInformation("SEGMENT_VISUAL_PLAN_CREATED segmentId={SegmentId} segmentType={SegmentType} requiredAssets={RequiredAssets} reusableFrameScreenshots={ReusableFrameScreenshots}", plan.SegmentId, plan.SegmentType, plan.RequiredVisualAssets.Count, plan.ReusableFrameScreenshots.Count);
            plans.Add(plan);
        }
        return plans;
    }

    private static IReadOnlyList<string> SelectReusableFrameScreenshots(WeeklySegmentAssignment classified, DiversifiedSegmentAssignment diversified, IReadOnlyList<string> frameScreenshots, WeeklyHybridScenePlanPackage? scenePlan, IReadOnlyList<CinematicSceneFramePlan>? cinematicFramePlans)
    {
        if (frameScreenshots.Count == 0) return [];
        var suggestedCodes = classified.SuggestedRenderScenes.Concat(scenePlan?.SegmentSceneMappings.Where(x => x.SegmentCode.Equals(classified.SegmentId, StringComparison.OrdinalIgnoreCase)).Select(x => x.SceneCode) ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renderCodes = cinematicFramePlans?.Where(x => suggestedCodes.Contains(x.RenderSceneCode) || suggestedCodes.Contains(x.SourceSceneCode)).SelectMany(x => x.FramePlans.Select(f => f.ImagePath)).ToList() ?? [];
        var matched = frameScreenshots.Where(path => suggestedCodes.Any(code => path.Contains(code, StringComparison.OrdinalIgnoreCase))).Concat(renderCodes.Where(x => frameScreenshots.Contains(x, StringComparer.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        if (matched.Count > 0) return matched;
        return diversified.ReuseAllowed || classified.SegmentType is "HeroEvent" or "StrongestEvent" or "ShortHook" ? frameScreenshots.Take(2).ToList() : [];
    }

    private static int EstimateDuration(string segmentType) => segmentType switch
    {
        "OpeningHook" => 20, "WeeklySkyOverview" => 35, "HeroEvent" => 50, "MoonHighlights" => 40, "PlanetHighlights" => 40, "BestObservationWindow" => 30, "AstrophotographyTip" => 30, "WeeklySummary" => 25,
        "ShortHook" => 5, "StrongestEvent" => 10, "WhereToLook" => 6, "BestTime" => 5, "CallToAction" => 4,
        _ => 20
    };
}

internal static class VisualAssetPlanningRules
{
    public static (VisualAssetSourceType Primary, VisualAssetSourceType? Secondary, VisualAssetSourceType? Tertiary) GetSources(string segmentType) => segmentType switch
    {
        "OpeningHook" => (VisualAssetSourceType.AICinematic, VisualAssetSourceType.Stellarium, null),
        "WeeklySkyOverview" => (VisualAssetSourceType.MotionGraphics, null, null),
        "HeroEvent" => (VisualAssetSourceType.Stellarium, VisualAssetSourceType.Hybrid, null),
        "MoonHighlights" => (VisualAssetSourceType.Stellarium, VisualAssetSourceType.NASA, null),
        "PlanetHighlights" => (VisualAssetSourceType.Stellarium, VisualAssetSourceType.NASA, VisualAssetSourceType.JWST),
        "BestObservationWindow" => (VisualAssetSourceType.MotionGraphics, null, null),
        "AstrophotographyTip" => (VisualAssetSourceType.EducationalOverlay, VisualAssetSourceType.NASA, VisualAssetSourceType.AICinematic),
        "WeeklySummary" => (VisualAssetSourceType.AICinematic, VisualAssetSourceType.MotionGraphics, null),
        "ShortHook" => (VisualAssetSourceType.AICinematic, VisualAssetSourceType.Stellarium, null),
        "StrongestEvent" => (VisualAssetSourceType.Stellarium, VisualAssetSourceType.Hybrid, null),
        "WhereToLook" => (VisualAssetSourceType.MotionGraphics, VisualAssetSourceType.Stellarium, null),
        "BestTime" => (VisualAssetSourceType.MotionGraphics, null, null),
        "CallToAction" => (VisualAssetSourceType.AICinematic, VisualAssetSourceType.MotionGraphics, null),
        _ => (VisualAssetSourceType.Hybrid, null, null)
    };

    public static IReadOnlyList<VisualAssetRequirement> BuildRequirements(string segmentId, string segmentType, (VisualAssetSourceType Primary, VisualAssetSourceType? Secondary, VisualAssetSourceType? Tertiary) sources) => GetRequirementNames(segmentType)
        .Select((name, index) => new VisualAssetRequirement($"{segmentId}-visual-{index + 1:00}", name, GetAssetIntent(name), GetUsageRole(name), PickRequirementSource(name, sources), CanReuse(name), RequiresFutureGeneration(name), "Planning metadata only; do not render, generate, download, or composite in this phase."))
        .ToList();

    private static IReadOnlyList<string> GetRequirementNames(string segmentType) => segmentType switch
    {
        "OpeningHook" => ["cinematic Milky Way", "dramatic sky reveal", "atmospheric horizon", "Stellarium hero frame"],
        "WeeklySkyOverview" => ["weekly sky map", "visible object map", "timeline graphic", "weekly overview animation plan"],
        "HeroEvent" => ["hero observation frame", "cinematic zoom sequence", "constellation overlay option"],
        "MoonHighlights" => ["Moon phase closeups", "crater imagery", "moon motion explanation"],
        "PlanetHighlights" => ["planetary comparison visuals", "orbital context graphics", "visibility path overlays"],
        "BestObservationWindow" => ["clock timeline", "observation window graphic", "horizon direction indicator", "compass guidance"],
        "AstrophotographyTip" => ["camera setting card", "tripod guidance", "exposure recommendation", "sample target framing"],
        "WeeklySummary" => ["recap montage", "checklist visuals", "cinematic closing sky"],
        "ShortHook" => ["fast cinematic hook", "hero frame flash", "emotional title card"],
        "StrongestEvent" => ["hero observation frame", "rapid zoom cue", "target label overlay"],
        "WhereToLook" => ["directional arrows", "horizon guidance", "constellation labels"],
        "BestTime" => ["visibility timeline", "date and clock card", "urgency cue"],
        "CallToAction" => ["cinematic closing sky", "short recap card", "follow prompt overlay"],
        _ => ["hybrid visual plan"]
    };

    public static IReadOnlyList<VisualAssetSourcePlan> BuildSourcePlans(string segmentType, IReadOnlyList<string> assignedObjects, (VisualAssetSourceType Primary, VisualAssetSourceType? Secondary, VisualAssetSourceType? Tertiary) sources)
    {
        var ordered = new[] { sources.Primary, sources.Secondary, sources.Tertiary }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        return ordered.Select(source => BuildSourcePlan(source, segmentType, assignedObjects)).ToList();
    }

    private static VisualAssetSourcePlan BuildSourcePlan(VisualAssetSourceType source, string segmentType, IReadOnlyList<string> assignedObjects) => source switch
    {
        VisualAssetSourceType.NASA => new(source, "supporting_reference_asset", GetNasaIntent(segmentType, assignedObjects), TargetNasaAssetCategory: GetNasaCategory(segmentType, assignedObjects)),
        VisualAssetSourceType.JWST => new(source, "supporting_deep_space_or_planetary_asset", GetJwstIntent(segmentType, assignedObjects), TargetJwstAssetCategory: GetJwstCategory(segmentType, assignedObjects)),
        VisualAssetSourceType.AICinematic => new(source, "emotional_cinematic_reset", GetCinematicIntent(segmentType), CinematicSceneIntent: GetCinematicIntent(segmentType), EmotionalTone: GetEmotionalTone(segmentType), LightingStyle: GetLightingStyle(segmentType), CompositionStyle: GetCompositionStyle(segmentType), CinematicPurpose: GetCinematicPurpose(segmentType)),
        VisualAssetSourceType.MotionGraphics => new(source, "explanatory_motion_plan", "Explain verified timing, direction, object paths, or weekly structure.", MotionGraphicElements: GetMotionElements(segmentType)),
        VisualAssetSourceType.EducationalOverlay => new(source, "educational_guidance_overlay", "Turn observing or camera advice into readable cards and labels.", EducationalOverlayElements: GetEducationalElements(segmentType)),
        VisualAssetSourceType.Stellarium => new(source, "verified_sky_frame", $"Use existing/planned Stellarium framing for {ObjectText(assignedObjects)} without inventing astronomy."),
        VisualAssetSourceType.Hybrid => new(source, "composite_planning_bridge", "Combine verified Stellarium context with overlays or cinematic motion in a future phase."),
        _ => new(source, "supporting_visual", "General visual support.")
    };

    public static (int Educational, int Emotional, int Cinematic, int Retention) GetImportanceScores(string segmentType, bool isShortForm) => segmentType switch
    {
        "OpeningHook" => (45, 95, 95, 90), "WeeklySkyOverview" => (85, 45, 55, 70), "HeroEvent" => (80, 90, 85, 95), "MoonHighlights" => (80, 65, 70, 75), "PlanetHighlights" => (80, 70, 75, 78), "BestObservationWindow" => (90, 45, 45, 82), "AstrophotographyTip" => (95, 55, 65, 76), "WeeklySummary" => (70, 82, 82, 72),
        "ShortHook" => (30, 95, 95, 98), "StrongestEvent" => (55, 90, 90, 96), "WhereToLook" => (78, 55, 45, 88), "BestTime" => (76, 55, 40, 86), "CallToAction" => (40, 80, 85, 78),
        _ => isShortForm ? (45, 75, 75, 85) : (65, 65, 65, 70)
    };

    public static string GetTransitionStyle(string segmentType) => segmentType switch
    {
        "OpeningHook" => "dramatic_reveal", "WeeklySkyOverview" => "timeline_transition", "HeroEvent" => "slow_zoom", "MoonHighlights" => "sky_rotation", "PlanetHighlights" => "orbital_transition", "BestObservationWindow" => "timeline_transition", "AstrophotographyTip" => "educational_overlay", "WeeklySummary" => "recap_montage",
        "ShortHook" => "dramatic_reveal", "StrongestEvent" => "slow_zoom", "WhereToLook" => "constellation_highlight", "BestTime" => "timeline_transition", "CallToAction" => "cinematic_fade",
        _ => "cinematic_fade"
    };

    public static string GetComplexity(string segmentType, int requirementCount) => segmentType is "HeroEvent" or "PlanetHighlights" or "WeeklySkyOverview" ? "High" : requirementCount >= 4 ? "MediumHigh" : "Medium";

    public static SegmentRetentionMetadata GetRetentionMetadata(string segmentType, int priority, bool isShortForm) => new(
        PacingResetCandidate: segmentType is "OpeningHook" or "AstrophotographyTip" or "WeeklySummary" or "CallToAction",
        EmotionalResetCandidate: segmentType is "OpeningHook" or "WeeklySummary" or "ShortHook" or "CallToAction",
        RetentionBoostCandidate: priority >= 75,
        ViralClipCandidate: isShortForm || segmentType is "OpeningHook" or "HeroEvent",
        ThumbnailCandidate: segmentType is "OpeningHook" or "HeroEvent" or "ShortHook" or "StrongestEvent");

    private static VisualAssetSourceType PickRequirementSource(string name, (VisualAssetSourceType Primary, VisualAssetSourceType? Secondary, VisualAssetSourceType? Tertiary) sources)
    {
        if (name.Contains("crater", StringComparison.OrdinalIgnoreCase)) return VisualAssetSourceType.NASA;
        if (name.Contains("card", StringComparison.OrdinalIgnoreCase) || name.Contains("guidance", StringComparison.OrdinalIgnoreCase) || name.Contains("checklist", StringComparison.OrdinalIgnoreCase)) return sources.Primary == VisualAssetSourceType.EducationalOverlay ? VisualAssetSourceType.EducationalOverlay : VisualAssetSourceType.MotionGraphics;
        if (name.Contains("map", StringComparison.OrdinalIgnoreCase) || name.Contains("timeline", StringComparison.OrdinalIgnoreCase) || name.Contains("arrow", StringComparison.OrdinalIgnoreCase) || name.Contains("graphic", StringComparison.OrdinalIgnoreCase)) return VisualAssetSourceType.MotionGraphics;
        if (name.Contains("cinematic", StringComparison.OrdinalIgnoreCase) || name.Contains("Milky Way", StringComparison.OrdinalIgnoreCase) || name.Contains("reveal", StringComparison.OrdinalIgnoreCase)) return VisualAssetSourceType.AICinematic;
        if (name.Contains("frame", StringComparison.OrdinalIgnoreCase) || name.Contains("constellation", StringComparison.OrdinalIgnoreCase)) return VisualAssetSourceType.Stellarium;
        return sources.Primary;
    }

    private static bool CanReuse(string name) => name.Contains("frame", StringComparison.OrdinalIgnoreCase) || name.Contains("Stellarium", StringComparison.OrdinalIgnoreCase);
    private static bool RequiresFutureGeneration(string name) => !CanReuse(name);
    private static string GetAssetIntent(string name) => $"Plan {name} for a future asset-generation phase.";
    private static string GetUsageRole(string name) => name.Contains("timeline", StringComparison.OrdinalIgnoreCase) ? "time_guidance" : name.Contains("card", StringComparison.OrdinalIgnoreCase) ? "educational_guidance" : name.Contains("cinematic", StringComparison.OrdinalIgnoreCase) ? "emotional_impact" : "segment_visual_support";
    private static string GetNasaCategory(string segmentType, IReadOnlyList<string> objects) => segmentType == "MoonHighlights" || objects.Any(x => x.Equals("MOON", StringComparison.OrdinalIgnoreCase)) ? "Moon crater imagery" : objects.Any(x => x.Contains("JUPITER", StringComparison.OrdinalIgnoreCase)) ? "Jupiter atmosphere imagery" : objects.Any(x => x.Contains("SATURN", StringComparison.OrdinalIgnoreCase)) ? "Saturn ring imagery" : "Milky Way or planetary archive imagery";
    private static string GetJwstCategory(string segmentType, IReadOnlyList<string> objects) => segmentType == "PlanetHighlights" ? "JWST planetary or deep-space context imagery" : "Deep space cinematic backdrop";
    private static string GetNasaIntent(string segmentType, IReadOnlyList<string> objects) => $"Reference imagery supporting {segmentType} for {ObjectText(objects)}; metadata only, no download.";
    private static string GetJwstIntent(string segmentType, IReadOnlyList<string> objects) => $"JWST category planning for {segmentType} when deep-space or planet context improves scale; metadata only.";
    private static string GetCinematicIntent(string segmentType) => segmentType switch { "OpeningHook" or "ShortHook" => "Fast awe-driven sky reveal", "WeeklySummary" or "CallToAction" => "Warm cinematic closing sky", "AstrophotographyTip" => "Inspirational example framing", _ => "Cosmic wonder visual reset" };
    private static string GetEmotionalTone(string segmentType) => segmentType is "OpeningHook" or "ShortHook" ? "awe" : segmentType is "WeeklySummary" or "CallToAction" ? "hopeful wonder" : "educational inspiration";
    private static string GetLightingStyle(string segmentType) => segmentType is "OpeningHook" or "ShortHook" ? "dramatic twilight-to-night contrast" : "soft night-sky glow";
    private static string GetCompositionStyle(string segmentType) => segmentType is "OpeningHook" or "ShortHook" ? "wide horizon with dominant sky" : "clean subject-centered astronomy composition";
    private static string GetCinematicPurpose(string segmentType) => segmentType is "OpeningHook" or "ShortHook" ? "hook speed and emotional impact" : "emotional pacing reset";
    private static IReadOnlyList<string> GetMotionElements(string segmentType) => segmentType switch { "WeeklySkyOverview" => ["sky map overlays", "visible object map", "visibility timelines", "constellation labels"], "BestObservationWindow" or "BestTime" => ["clock timeline", "horizon guidance", "compass guidance", "directional arrows"], "PlanetHighlights" => ["object path graphics", "orbital explanation", "visibility path overlays"], "WhereToLook" => ["directional arrows", "horizon guidance", "constellation labels"], _ => ["timeline graphic", "safe-area labels"] };
    private static IReadOnlyList<string> GetEducationalElements(string segmentType) => segmentType switch { "AstrophotographyTip" => ["camera settings card", "tripod guidance", "exposure recommendation", "sample target framing", "weather consideration reminder"], "WeeklySummary" => ["observation checklist"], _ => ["viewing tip overlays", "binocular guidance"] };
    private static string ObjectText(IReadOnlyList<string> objects) => objects.Count == 0 ? "verified weekly sky context" : string.Join(" + ", objects);
}
