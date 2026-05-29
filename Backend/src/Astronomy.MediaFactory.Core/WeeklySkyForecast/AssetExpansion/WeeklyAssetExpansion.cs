using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentDiversification;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetExpansion;

public sealed record ExpandedSegmentAssetPackage(
    string SegmentId,
    string SegmentType,
    string EpisodeType,
    IReadOnlyList<string> AssignedObjects,
    string AssignedEventType,
    string PackageRole,
    string PrimaryVisualSource,
    IReadOnlyList<string> SecondaryVisualSources,
    IReadOnlyList<string> RequiredStellariumScenes,
    IReadOnlyList<string> RequiredAICinematicAssets,
    IReadOnlyList<string> RequiredNASAAssets,
    IReadOnlyList<string> RequiredJWSTAssets,
    IReadOnlyList<string> RequiredMotionGraphics,
    IReadOnlyList<string> RequiredEducationalOverlays,
    IReadOnlyList<string> ReusableExistingImages,
    IReadOnlyList<string> NewAssetsRequired,
    string ProductionReadinessStatus,
    int CoverageScore,
    IReadOnlyList<string> Warnings,
    string? ReuseReason = null);

public sealed record ExpandedRenderSceneRequirement(
    string RenderSceneCode,
    string SourceSegmentId,
    string SourceSegmentType,
    string RenderEngine,
    IReadOnlyList<string> TargetObjects,
    DateTime? PreferredObservationUtc,
    string? PreferredObservationLocal,
    IReadOnlyList<string> RequiredFrameTypes,
    string DesiredCameraIntent,
    string VisualRole,
    int Priority,
    bool GeometryAvailable,
    string GeometrySource,
    string ProductionStatus,
    IReadOnlyList<string> Warnings);

public sealed record WeeklySegmentCoverageReport(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    int LongformVisualPackageCount,
    int ShortformVisualPackageCount,
    int UniqueVisualPackageCount,
    int ReadyForVideoPlanningSegmentCount,
    int NeedsAssetGenerationSegmentCount,
    int NeedsSourceExpansionSegmentCount,
    int NotReadySegmentCount,
    IReadOnlyList<string> RepetitionWarnings,
    IReadOnlyList<string> ValidationWarnings,
    IReadOnlyList<ExpandedSegmentAssetPackage> SegmentPackages);

public sealed record WeeklyAssetExpansionPlan(
    Guid PipelineRunId,
    string RegionId,
    string Language,
    DateOnly WeekStartDate,
    DateTime GeneratedAtUtc,
    string AssetExpansionPlanningMode,
    IReadOnlyList<string> InputArtifactsUsed,
    IReadOnlyList<ExpandedSegmentAssetPackage> LongformSegmentPackages,
    IReadOnlyList<ExpandedSegmentAssetPackage> ShortformSegmentPackages,
    IReadOnlyList<ExpandedRenderSceneRequirement> ExpandedRenderSceneRequirements,
    bool AssetExpansionPlanningReady,
    int LongformVisualPackageCount,
    int ShortformVisualPackageCount,
    int ExpandedRenderSceneRequirementCount,
    int UniqueAstronomySceneRequirementCount,
    int ReadyForVideoPlanningSegmentCount,
    int NeedsAssetGenerationSegmentCount,
    IReadOnlyList<string> ValidationWarnings);

public sealed class AssetExpansionPolicy
{
    public const string PlanningOnlyMode = "PlanningOnly";
    public const string ExecuteExpandedScenesMode = "ExecuteExpandedScenes";

    public static readonly IReadOnlyList<string> RequiredLongformSegmentTypes =
        ["OpeningHook", "WeeklySkyOverview", "HeroEvent", "MoonHighlights", "PlanetHighlights", "BestObservationWindow", "AstrophotographyTip", "WeeklySummary"];

    public static readonly IReadOnlyList<string> RequiredShortformSegmentTypes =
        ["ShortHook", "StrongestEvent", "WhereToLook", "BestTime", "CallToAction"];

    public IReadOnlyList<string> GetRequiredStellariumScenes(string segmentType, bool hasHeroFrame) => segmentType switch
    {
        "OpeningHook" => hasHeroFrame ? [] : ["hero_event_observation_scene"],
        "WeeklySkyOverview" => ["weekly_overview_wide_scene"],
        "HeroEvent" => ["hero_event_observation_scene"],
        "MoonHighlights" => ["moon_highlight_scene"],
        "PlanetHighlights" => ["planet_highlight_scene"],
        "BestObservationWindow" => ["where_to_look_guidance_scene"],
        "AstrophotographyTip" => ["astrophotography_target_scene"],
        "ShortHook" => [],
        "StrongestEvent" => ["hero_event_observation_scene"],
        "WhereToLook" => ["where_to_look_guidance_scene"],
        _ => []
    };

    public IReadOnlyList<string> GetRequiredAICinematicAssets(string segmentType, int assignedObjectCount) => segmentType switch
    {
        "OpeningHook" => ["emotional_opener"],
        "HeroEvent" when assignedObjectCount > 1 => ["hero_event_establishing", "hero_event_balanced", "hero_event_close_context"],
        "HeroEvent" => ["hero_event_context"],
        "AstrophotographyTip" => ["astrophotography_background"],
        "WeeklySummary" => ["closing_cinematic_background"],
        "ShortHook" => ["short_hook_visual"],
        "CallToAction" => ["cta_background"],
        _ => []
    };

    public IReadOnlyList<string> GetRequiredNASAAssets(string segmentType) => segmentType switch
    {
        "MoonHighlights" => ["optional_nasa_moon_imagery"],
        "PlanetHighlights" => ["optional_nasa_planetary_imagery"],
        "AstrophotographyTip" => ["optional_nasa_context_background"],
        _ => []
    };

    public IReadOnlyList<string> GetRequiredJWSTAssets(string segmentType) => segmentType switch
    {
        "PlanetHighlights" => ["optional_jwst_planetary_context"],
        "AstrophotographyTip" => ["optional_jwst_deep_space_background"],
        _ => []
    };

    public IReadOnlyList<string> GetRequiredMotionGraphics(string segmentType) => segmentType switch
    {
        "WeeklySkyOverview" => ["weekly_sky_map", "visible_object_summary", "week_timeline_graphic"],
        "MoonHighlights" => ["moon_phase_visual"],
        "PlanetHighlights" => ["planet_visibility_path"],
        "BestObservationWindow" => ["observation_clock", "horizon_direction_map", "best_time_timeline"],
        "WeeklySummary" => ["recap_montage_plan", "weekly_checklist_overlay"],
        "WhereToLook" => ["direction_guide"],
        "BestTime" => ["best_time_card"],
        "CallToAction" => ["cta_card"],
        _ => []
    };

    public IReadOnlyList<string> GetRequiredEducationalOverlays(string segmentType) => segmentType switch
    {
        "AstrophotographyTip" => ["camera_settings_card", "tripod_guidance_card", "exposure_tip_card"],
        "WeeklySummary" => ["weekly_checklist_overlay"],
        _ => []
    };

    public string GetShortformVisualRole(string segmentType) => segmentType switch
    {
        "ShortHook" => "hook visual",
        "StrongestEvent" => "event reveal",
        "WhereToLook" => "direction guide",
        "BestTime" => "best time card",
        "CallToAction" => "CTA background",
        _ => "supporting shortform role"
    };
}

public sealed class SegmentCoverageAnalyzer
{
    public int CalculateCoverageScore(ExpandedSegmentAssetPackage package)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(package.PrimaryVisualSource) && package.PrimaryVisualSource != "Unassigned") score += 25;
        if (package.ReusableExistingImages.Count > 0) score += 20;
        if (package.NewAssetsRequired.Count > 0) score += 20;
        if (!string.IsNullOrWhiteSpace(package.PackageRole)) score += 15;
        if (!package.Warnings.Any(x => x.Contains("repetition", StringComparison.OrdinalIgnoreCase))) score += 20;
        return Math.Clamp(score, 0, 100);
    }

    public string ResolveStatus(int coverageScore) => coverageScore switch
    {
        >= 80 => "ReadyForVideoPlanning",
        >= 60 => "NeedsAssetGeneration",
        >= 40 => "NeedsSourceExpansion",
        _ => "NotReady"
    };
}

public sealed class UniqueSceneRequirementBuilder
{
    public IReadOnlyList<ExpandedRenderSceneRequirement> Build(
        IReadOnlyList<ExpandedSegmentAssetPackage> packages,
        WeeklySegmentClassificationPlan classificationPlan,
        WeeklyHybridScenePlanPackage? scenePlan,
        WeeklySkyForecastV2IntelligenceResponse weeklyContext)
    {
        var classificationById = classificationPlan.LongformAssignments.Concat(classificationPlan.ShortformAssignments)
            .ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        var eventObjectsByCode = BuildEventObjectMap(weeklyContext);
        var existingNeedsByObject = scenePlan?.StellariumNeeds
            .SelectMany(need => need.ObjectCodes.Select(code => (code, need)))
            .GroupBy(x => x.code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(v => v.need).ToList(), StringComparer.OrdinalIgnoreCase)
            ?? [];
        var requirements = new List<ExpandedRenderSceneRequirement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages)
        {
            foreach (var sceneCode in package.RequiredStellariumScenes)
            {
                if (!seen.Add(sceneCode))
                    continue;

                var assignment = classificationById.GetValueOrDefault(package.SegmentId);
                var targetObjects = ResolveTargetObjects(package, classificationPlan, sceneCode);
                var geometry = ResolveGeometry(targetObjects, eventObjectsByCode, existingNeedsByObject);
                var preferredUtc = ResolvePreferredUtc(assignment, weeklyContext, scenePlan, targetObjects);
                var warnings = new List<string>();
                if (targetObjects.Count == 0)
                    warnings.Add("No target objects available; requirement is retained as planning metadata only and must not be executed until geometry is available.");
                if (!geometry.Available)
                    warnings.Add("No verified astronomy geometry found for this segment/object focus; do not execute or fabricate Stellarium geometry.");

                requirements.Add(new ExpandedRenderSceneRequirement(
                    sceneCode,
                    package.SegmentId,
                    package.SegmentType,
                    "Stellarium",
                    targetObjects,
                    preferredUtc,
                    BuildPreferredLocal(assignment),
                    GetFrameTypes(sceneCode),
                    GetCameraIntent(sceneCode),
                    package.PackageRole,
                    GetPriority(package.SegmentType),
                    geometry.Available,
                    geometry.Source,
                    geometry.Available ? "RequirementReadyForPlanningOnly" : "GeometryMissingDoNotExecute",
                    warnings));
            }
        }

        return requirements.Where(x => x.GeometryAvailable).ToList();
    }

    private static Dictionary<string, WeeklyAstronomyEventObject> BuildEventObjectMap(WeeklySkyForecastV2IntelligenceResponse weeklyContext)
    {
        var map = new Dictionary<string, WeeklyAstronomyEventObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var ev in weeklyContext.EventExtractionResult?.ExtractedEvents ?? [])
        foreach (var obj in ev.Objects)
        {
            if (!string.IsNullOrWhiteSpace(obj.ObjectCode) && (obj.AltitudeDegrees.HasValue || obj.AzimuthDegrees.HasValue))
                map.TryAdd(obj.ObjectCode, obj);
        }

        return map;
    }

    private static IReadOnlyList<string> ResolveTargetObjects(ExpandedSegmentAssetPackage package, WeeklySegmentClassificationPlan classificationPlan, string sceneCode)
    {
        var assigned = package.AssignedObjects.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sceneCode.Contains("moon", StringComparison.OrdinalIgnoreCase))
            return assigned.Where(x => x.Equals("MOON", StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty("MOON").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sceneCode.Contains("planet", StringComparison.OrdinalIgnoreCase))
        {
            var planets = assigned.Where(IsPlanet).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return planets.Count > 0 ? planets : [];
        }
        if (assigned.Count > 0)
            return assigned;
        return classificationPlan.HeroEventObjects.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (bool Available, string Source) ResolveGeometry(IReadOnlyList<string> targetObjects, Dictionary<string, WeeklyAstronomyEventObject> eventObjectsByCode, Dictionary<string, List<WeeklyStellariumNeed>> existingNeedsByObject)
    {
        if (targetObjects.Count == 0)
            return (false, "NoTargetObjects");
        if (targetObjects.Any(eventObjectsByCode.ContainsKey))
            return (true, "skyfield-weekly-response.json/EventExtractionResult object alt-az");
        if (targetObjects.Any(existingNeedsByObject.ContainsKey))
            return (true, "cinematic-frame-plan.json existing Stellarium need for object focus");
        return (false, "No verified Skyfield geometry found");
    }

    private static DateTime? ResolvePreferredUtc(WeeklySegmentAssignment? assignment, WeeklySkyForecastV2IntelligenceResponse weeklyContext, WeeklyHybridScenePlanPackage? scenePlan, IReadOnlyList<string> targetObjects)
    {
        var matchingNeed = scenePlan?.StellariumNeeds.FirstOrDefault(x => targetObjects.Count == 0 || x.ObjectCodes.Any(targetObjects.Contains));
        if (matchingNeed?.BestTimeUtc is not null)
            return matchingNeed.BestTimeUtc.Value;
        var matchingEvent = weeklyContext.EventIntelligence.FirstOrDefault(x => x.ObjectCodes.Any(targetObjects.Contains));
        if (matchingEvent?.BestTimeUtc is not null)
            return matchingEvent.BestTimeUtc.Value;
        if (assignment?.AssignedDateLocal is null)
            return null;
        return assignment.AssignedDateLocal.Value.ToDateTime(assignment.AssignedBestTimeLocal ?? new TimeOnly(21, 0), DateTimeKind.Local).ToUniversalTime();
    }

    private static string? BuildPreferredLocal(WeeklySegmentAssignment? assignment)
    {
        if (assignment?.AssignedDateLocal is null)
            return null;
        return assignment.AssignedBestTimeLocal is null
            ? assignment.AssignedDateLocal.Value.ToString("yyyy-MM-dd")
            : $"{assignment.AssignedDateLocal.Value:yyyy-MM-dd} {assignment.AssignedBestTimeLocal.Value:HH:mm}";
    }

    private static IReadOnlyList<string> GetFrameTypes(string sceneCode) => sceneCode switch
    {
        "hero_event_observation_scene" => ["establishing", "balanced", "hero_close_context"],
        "weekly_overview_wide_scene" => ["wide_context"],
        "where_to_look_guidance_scene" => ["directional_context", "horizon_guide"],
        _ => ["primary_still", "context_still"]
    };

    private static string GetCameraIntent(string sceneCode) => sceneCode switch
    {
        "hero_event_observation_scene" => "Frame the verified hero object grouping with observation-realistic horizon context.",
        "moon_highlight_scene" => "Isolate the Moon as the lunar story anchor without reusing the full hero grouping.",
        "planet_highlight_scene" => "Center verified naked-eye planet visibility with labels/horizon orientation.",
        "weekly_overview_wide_scene" => "Wide weekly sky orientation frame for map/timeline support.",
        "where_to_look_guidance_scene" => "Practical compass/horizon guidance view.",
        "astrophotography_target_scene" => "Composition-friendly verified target frame for camera tip education.",
        _ => "Verified astronomy support frame."
    };

    private static int GetPriority(string segmentType) => segmentType switch
    {
        "HeroEvent" or "StrongestEvent" => 100,
        "MoonHighlights" or "PlanetHighlights" => 90,
        "WeeklySkyOverview" or "BestObservationWindow" or "WhereToLook" => 80,
        "AstrophotographyTip" => 70,
        _ => 60
    };

    private static bool IsPlanet(string code) => code is "MERCURY" or "VENUS" or "MARS" or "JUPITER" or "SATURN";
}

public sealed class AssetExpansionValidator
{
    public IReadOnlyList<string> Validate(WeeklyAssetExpansionPlan plan)
    {
        var warnings = new List<string>();
        if (plan.LongformVisualPackageCount != 8) warnings.Add($"Expected 8 longform segment packages but found {plan.LongformVisualPackageCount}.");
        if (plan.ShortformVisualPackageCount != 5) warnings.Add($"Expected 5 shortform segment packages but found {plan.ShortformVisualPackageCount}.");
        if (plan.LongformSegmentPackages.Concat(plan.ShortformSegmentPackages).Select(x => x.PackageRole).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 8)
            warnings.Add("Unique visual packages below target minimum of 8.");
        if (plan.ExpandedRenderSceneRequirementCount < 5) warnings.Add($"Expanded render scene requirements below target minimum of 5: {plan.ExpandedRenderSceneRequirementCount}.");
        if (plan.ExpandedRenderSceneRequirements.Any(x => !x.GeometryAvailable)) warnings.Add("At least one render scene requirement lacks verified geometry and must not execute.");
        if (plan.LongformSegmentPackages.Concat(plan.ShortformSegmentPackages).Any(x => x.NewAssetsRequired.Count == 0 && x.ReusableExistingImages.Count == 0))
            warnings.Add("At least one segment package lacks both reusable assets and explicit new asset requirements.");
        return warnings;
    }
}

public sealed class AssetExpansionPersister
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public async Task<(string PlanPath, string CoverageReportPath, string RenderScenePlanPath)> WriteAsync(WeeklyAssetExpansionPlan plan, WeeklySegmentCoverageReport coverageReport, string workingDirectoryRoot, CancellationToken cancellationToken)
    {
        var episodeDirectory = Path.Combine(workingDirectoryRoot, "episode");
        Directory.CreateDirectory(episodeDirectory);
        var planPath = Path.Combine(episodeDirectory, "weekly-asset-expansion-plan.json");
        var coveragePath = Path.Combine(episodeDirectory, "weekly-segment-coverage-report.json");
        var renderScenePath = Path.Combine(episodeDirectory, "weekly-expanded-render-scene-plan.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(coveragePath, JsonSerializer.Serialize(coverageReport, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(renderScenePath, JsonSerializer.Serialize(new
        {
            plan.PipelineRunId,
            plan.GeneratedAtUtc,
            plan.AssetExpansionPlanningMode,
            renderEngine = "Stellarium",
            requirements = plan.ExpandedRenderSceneRequirements,
            requirementCount = plan.ExpandedRenderSceneRequirementCount,
            uniqueAstronomySceneRequirementCount = plan.UniqueAstronomySceneRequirementCount,
            planningOnly = plan.AssetExpansionPlanningMode == AssetExpansionPolicy.PlanningOnlyMode,
            warnings = plan.ValidationWarnings
        }, JsonOptions), cancellationToken);
        return (planPath, coveragePath, renderScenePath);
    }
}

public sealed class WeeklyAssetExpansionService(
    AssetExpansionPolicy policy,
    SegmentCoverageAnalyzer coverageAnalyzer,
    UniqueSceneRequirementBuilder sceneRequirementBuilder,
    AssetExpansionPersister persister,
    AssetExpansionValidator validator,
    ILogger<WeeklyAssetExpansionService> logger)
{
    public async Task<(WeeklyAssetExpansionPlan Plan, WeeklySegmentCoverageReport CoverageReport, string PlanPath, string CoverageReportPath, string RenderScenePlanPath)> ExpandAndPersistAsync(
        WeeklyEpisodeArchitectureResult episodeArchitecture,
        WeeklySegmentClassificationPlan classificationPlan,
        WeeklySegmentDiversificationPlan diversificationPlan,
        WeeklyVisualAssetPlan visualAssetPlan,
        WeeklyHybridScenePlanPackage? scenePlan,
        ImageSequencePlan? imageSequencePlan,
        IReadOnlyList<CinematicSceneFramePlan>? cinematicFramePlans,
        WeeklySkyForecastV2IntelligenceResponse weeklyContext,
        AICinematicAssetGenerationSummary? aiCinematicAssets,
        string workingDirectoryRoot,
        CancellationToken cancellationToken,
        string planningMode = AssetExpansionPolicy.PlanningOnlyMode)
    {
        logger.LogInformation("ASSET_EXPANSION_START pipelineRunId={PipelineRunId} planningMode={PlanningMode}", classificationPlan.PipelineRunId, planningMode);
        var warnings = new List<string>();
        var reusableImages = ResolveReusableImages(imageSequencePlan, cinematicFramePlans, aiCinematicAssets);
        var hasHeroFrame = reusableImages.Any(x => x.Contains("hero", StringComparison.OrdinalIgnoreCase)) || (scenePlan?.StellariumNeeds.Count ?? 0) > 0;
        var longform = BuildPackages(classificationPlan.LongformAssignments, diversificationPlan.LongformAssignments, visualAssetPlan.LongformSegmentVisualPlans, "Longform", reusableImages, hasHeroFrame, warnings).ToList();
        var shortform = BuildPackages(classificationPlan.ShortformAssignments, diversificationPlan.ShortformAssignments, visualAssetPlan.ShortformSegmentVisualPlans, "Shortform", reusableImages, hasHeroFrame, warnings).ToList();
        var allPackages = longform.Concat(shortform).ToList();
        var sceneRequirements = sceneRequirementBuilder.Build(allPackages, classificationPlan, scenePlan, weeklyContext);
        foreach (var requirement in sceneRequirements)
        {
            logger.LogInformation("EXPANDED_RENDER_SCENE_REQUIREMENT_CREATED renderSceneCode={RenderSceneCode} segmentId={SegmentId} segmentType={SegmentType} geometryAvailable={GeometryAvailable}", requirement.RenderSceneCode, requirement.SourceSegmentId, requirement.SourceSegmentType, requirement.GeometryAvailable);
        }

        warnings.AddRange(BuildReuseWarnings(longform));
        var generatedAt = DateTime.UtcNow;
        var coverageReport = new WeeklySegmentCoverageReport(
            classificationPlan.PipelineRunId,
            generatedAt,
            longform.Count,
            shortform.Count,
            allPackages.Select(x => x.PackageRole).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            allPackages.Count(x => x.ProductionReadinessStatus == "ReadyForVideoPlanning"),
            allPackages.Count(x => x.ProductionReadinessStatus == "NeedsAssetGeneration"),
            allPackages.Count(x => x.ProductionReadinessStatus == "NeedsSourceExpansion"),
            allPackages.Count(x => x.ProductionReadinessStatus == "NotReady"),
            warnings.Where(x => x.Contains("repetition", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            allPackages);

        var plan = new WeeklyAssetExpansionPlan(
            classificationPlan.PipelineRunId,
            classificationPlan.RegionId,
            classificationPlan.Language,
            classificationPlan.WeekStartDate,
            generatedAt,
            planningMode,
            BuildInputArtifactSummary(episodeArchitecture, classificationPlan, diversificationPlan, visualAssetPlan, scenePlan, imageSequencePlan, cinematicFramePlans, weeklyContext, aiCinematicAssets),
            longform,
            shortform,
            sceneRequirements,
            AssetExpansionPlanningReady: longform.Count == 8 && shortform.Count == 5 && sceneRequirements.Count >= 5,
            LongformVisualPackageCount: longform.Count,
            ShortformVisualPackageCount: shortform.Count,
            ExpandedRenderSceneRequirementCount: sceneRequirements.Count,
            UniqueAstronomySceneRequirementCount: sceneRequirements.Select(x => x.RenderSceneCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            ReadyForVideoPlanningSegmentCount: coverageReport.ReadyForVideoPlanningSegmentCount,
            NeedsAssetGenerationSegmentCount: coverageReport.NeedsAssetGenerationSegmentCount,
            ValidationWarnings: []);

        var validationWarnings = validator.Validate(plan).Concat(warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        plan = plan with { ValidationWarnings = validationWarnings, AssetExpansionPlanningReady = plan.AssetExpansionPlanningReady && validationWarnings.All(x => !x.StartsWith("Expected", StringComparison.OrdinalIgnoreCase)) };
        var paths = await persister.WriteAsync(plan, coverageReport with { ValidationWarnings = validationWarnings }, workingDirectoryRoot, cancellationToken);
        logger.LogInformation("SEGMENT_COVERAGE_REPORT_WRITTEN path={Path}", paths.CoverageReportPath);
        logger.LogInformation("ASSET_EXPANSION_PLAN_WRITTEN path={Path} assetExpansionPlanningReady={Ready}", paths.PlanPath, plan.AssetExpansionPlanningReady);
        logger.LogInformation("EXPANDED_RENDER_SCENE_PLAN_WRITTEN path={Path} requirements={RequirementCount}", paths.RenderScenePlanPath, plan.ExpandedRenderSceneRequirementCount);
        logger.LogInformation("ASSET_EXPANSION_COMPLETE longformPackages={LongformCount} shortformPackages={ShortformCount} expandedRenderScenes={RenderSceneCount} readySegments={ReadySegments}", plan.LongformVisualPackageCount, plan.ShortformVisualPackageCount, plan.ExpandedRenderSceneRequirementCount, plan.ReadyForVideoPlanningSegmentCount);
        return (plan, coverageReport with { ValidationWarnings = validationWarnings }, paths.PlanPath, paths.CoverageReportPath, paths.RenderScenePlanPath);
    }

    private IEnumerable<ExpandedSegmentAssetPackage> BuildPackages(
        IReadOnlyList<WeeklySegmentAssignment> assignments,
        IReadOnlyList<DiversifiedSegmentAssignment> diversifiedAssignments,
        IReadOnlyList<SegmentVisualAssetPlan> visualPlans,
        string episodeType,
        IReadOnlyList<string> reusableImages,
        bool hasHeroFrame,
        List<string> validationWarnings)
    {
        var diversifiedById = diversifiedAssignments.ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        var visualPlanById = visualPlans.ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in assignments)
        {
            diversifiedById.TryGetValue(assignment.SegmentId, out var diversified);
            visualPlanById.TryGetValue(assignment.SegmentId, out var visualPlan);
            var role = episodeType == "Shortform" ? policy.GetShortformVisualRole(assignment.SegmentType) : diversified?.DiversifiedVisualRole ?? assignment.SegmentType;
            var packageWarnings = new List<string>();
            if (diversified?.RepetitionRiskScore > 50)
                packageWarnings.Add($"Segment repetition risk {diversified.RepetitionRiskScore} exceeds reuse threshold; package requires differentiated assets.");
            if (assignment.AssignedObjects.Count == 0 && RequiresAstronomyFocus(assignment.SegmentType))
                packageWarnings.Add("Segment has no assigned astronomy objects; Stellarium requirements may be withheld unless verified geometry exists.");

            var package = new ExpandedSegmentAssetPackage(
                assignment.SegmentId,
                assignment.SegmentType,
                episodeType,
                assignment.AssignedObjects,
                assignment.AssignedEventType,
                role,
                ResolvePrimaryVisualSource(assignment, diversified, visualPlan),
                ResolveSecondarySources(visualPlan, diversified),
                policy.GetRequiredStellariumScenes(assignment.SegmentType, hasHeroFrame),
                policy.GetRequiredAICinematicAssets(assignment.SegmentType, assignment.AssignedObjects.Count),
                policy.GetRequiredNASAAssets(assignment.SegmentType),
                policy.GetRequiredJWSTAssets(assignment.SegmentType),
                policy.GetRequiredMotionGraphics(assignment.SegmentType),
                policy.GetRequiredEducationalOverlays(assignment.SegmentType),
                ResolveReusableImages(assignment, reusableImages, diversified),
                ResolveNewAssets(assignment, diversified),
                "NotScored",
                0,
                packageWarnings,
                diversified?.ReuseAllowed == true ? diversified.ReuseReason : null);
            var score = coverageAnalyzer.CalculateCoverageScore(package);
            var status = coverageAnalyzer.ResolveStatus(score);
            package = package with { CoverageScore = score, ProductionReadinessStatus = status };
            logger.LogInformation("SEGMENT_COVERAGE_SCORE_CALCULATED segmentId={SegmentId} segmentType={SegmentType} coverageScore={CoverageScore} status={Status}", package.SegmentId, package.SegmentType, package.CoverageScore, package.ProductionReadinessStatus);
            logger.LogInformation("SEGMENT_ASSET_PACKAGE_CREATED segmentId={SegmentId} segmentType={SegmentType} episodeType={EpisodeType} primaryVisualSource={PrimaryVisualSource} newAssets={NewAssetsCount}", package.SegmentId, package.SegmentType, package.EpisodeType, package.PrimaryVisualSource, package.NewAssetsRequired.Count);
            yield return package;
        }
    }

    private static IReadOnlyList<string> ResolveReusableImages(ImageSequencePlan? imageSequencePlan, IReadOnlyList<CinematicSceneFramePlan>? cinematicFramePlans, AICinematicAssetGenerationSummary? aiCinematicAssets)
    {
        var images = new List<string>();
        images.AddRange(imageSequencePlan?.Sequences.Where(x => x.ImageExists || !string.IsNullOrWhiteSpace(x.ImagePath)).Select(x => x.ImagePath) ?? []);
        images.AddRange(cinematicFramePlans?.SelectMany(x => x.FramePlans.Select(f => f.ImagePath)).Where(x => !string.IsNullOrWhiteSpace(x)) ?? []);
        images.AddRange(aiCinematicAssets?.Results.Where(x => x.ProductionReady).Select(x => x.ImagePath) ?? []);
        return images.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ResolveReusableImages(WeeklySegmentAssignment assignment, IReadOnlyList<string> reusableImages, DiversifiedSegmentAssignment? diversified)
    {
        var filtered = reusableImages.Where(image => assignment.AssignedObjects.Any(obj => image.Contains(obj, StringComparison.OrdinalIgnoreCase)) || image.Contains(assignment.SegmentType, StringComparison.OrdinalIgnoreCase)).Take(3).ToList();
        if (filtered.Count == 0 && diversified?.ExistingReusableAssets.Count > 0)
            filtered.AddRange(diversified.ExistingReusableAssets.Take(2));
        if (filtered.Count == 0)
            filtered.AddRange(reusableImages.Take(1));
        return filtered.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ResolveNewAssets(WeeklySegmentAssignment assignment, DiversifiedSegmentAssignment? diversified)
    {
        var assets = new List<string>();
        assets.AddRange(diversified?.RequiredNewAssets ?? []);
        assets.AddRange(assignment.RequiredVisualTypes.Select(x => $"required_visual_type:{x}"));
        return assets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ResolvePrimaryVisualSource(WeeklySegmentAssignment assignment, DiversifiedSegmentAssignment? diversified, SegmentVisualAssetPlan? visualPlan)
    {
        if (visualPlan is not null) return visualPlan.PrimaryVisualSource.ToString();
        if (!string.IsNullOrWhiteSpace(diversified?.DiversifiedVisualSource)) return diversified.DiversifiedVisualSource;
        return assignment.RequiredVisualTypes.FirstOrDefault() ?? "Unassigned";
    }

    private static IReadOnlyList<string> ResolveSecondarySources(SegmentVisualAssetPlan? visualPlan, DiversifiedSegmentAssignment? diversified)
    {
        var sources = new List<string>();
        if (visualPlan?.SecondaryVisualSource is not null) sources.Add(visualPlan.SecondaryVisualSource.Value.ToString());
        if (visualPlan?.TertiaryVisualSource is not null) sources.Add(visualPlan.TertiaryVisualSource.Value.ToString());
        if (sources.Count == 0 && !string.IsNullOrWhiteSpace(diversified?.DiversifiedVisualSource)) sources.AddRange(diversified.DiversifiedVisualSource.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Skip(1));
        return sources.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool RequiresAstronomyFocus(string segmentType) => segmentType is "HeroEvent" or "MoonHighlights" or "PlanetHighlights" or "StrongestEvent" or "WhereToLook";

    private static IReadOnlyList<string> BuildReuseWarnings(IReadOnlyList<ExpandedSegmentAssetPackage> longform)
    {
        return longform.SelectMany(x => x.ReusableExistingImages.Select(image => (image, x.SegmentId)))
            .GroupBy(x => x.image, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Select(v => v.SegmentId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 2)
            .Select(x => $"Image reuse repetition warning: {x.Key} is reused across more than 2 longform segments.")
            .ToList();
    }

    private static IReadOnlyList<string> BuildInputArtifactSummary(
        WeeklyEpisodeArchitectureResult episodeArchitecture,
        WeeklySegmentClassificationPlan classificationPlan,
        WeeklySegmentDiversificationPlan diversificationPlan,
        WeeklyVisualAssetPlan visualAssetPlan,
        WeeklyHybridScenePlanPackage? scenePlan,
        ImageSequencePlan? imageSequencePlan,
        IReadOnlyList<CinematicSceneFramePlan>? cinematicFramePlans,
        WeeklySkyForecastV2IntelligenceResponse weeklyContext,
        AICinematicAssetGenerationSummary? aiCinematicAssets) =>
        [
            $"weekly-episode-plan.json ready={episodeArchitecture.EpisodeArchitectureReady}",
            $"weekly-segment-classification-plan.json longform={classificationPlan.ClassifiedLongformSegmentCount} shortform={classificationPlan.ClassifiedShortformSegmentCount}",
            $"weekly-segment-diversification-plan.json assetExpansionRequired={diversificationPlan.AssetExpansionRequired}",
            $"weekly-visual-asset-plan.json plannedAssets={visualAssetPlan.PlannedVisualAssetCount}",
            $"cinematic-frame-plan.json renderScenes={cinematicFramePlans?.Count ?? 0}",
            $"image-sequence-plan.json selectedImages={imageSequencePlan?.TotalImages ?? 0}",
            $"skyfield-weekly-response.json events={weeklyContext.EventExtractionResult?.ExtractedEvents.Count ?? 0}",
            aiCinematicAssets is null ? "ai-cinematic-asset-results.json unavailable" : $"ai-cinematic-asset-results.json productionReady={aiCinematicAssets.ProductionReadyCount}",
            $"weekly-scene-plan.json stellariumNeeds={scenePlan?.StellariumNeeds.Count ?? 0}"
        ];
}
