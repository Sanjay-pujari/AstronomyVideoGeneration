using Astronomy.SscIntelligence.Camera;
using Astronomy.SscIntelligence.Composition;
using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.NightWindow;
using Astronomy.SscIntelligence.Rendering;
using Astronomy.SscIntelligence.SceneIntent;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;
using Astronomy.SscIntelligence.Spatial;
using Astronomy.SscIntelligence.Visibility;
using Microsoft.Extensions.Logging;

namespace Astronomy.SscIntelligence;

public sealed class SscIntelligenceService : ISscIntelligenceService
{
    private readonly INightWindowResolver _nightWindowResolver;
    private readonly IVisibilityFilter _visibilityFilter;
    private readonly ICameraCenterCalculator _cameraCenterCalculator;
    private readonly IDynamicFovCalculator _dynamicFovCalculator;
    private readonly IPrimaryTargetResolver _primaryTargetResolver;
    private readonly ICinematicCameraPlanner _cinematicCameraPlanner;
    private readonly IUnifiedCameraComposer _unifiedCameraComposer;
    private readonly ISceneIntentResolver _sceneIntentResolver;
    private readonly IStellariumSscRenderer _renderer;
    private readonly IAstronomicalSpatialCompositionEngine _spatialCompositionEngine;
    private readonly ILogger<SscIntelligenceService> _logger;

    public SscIntelligenceService(INightWindowResolver nightWindowResolver, IVisibilityFilter visibilityFilter, ICameraCenterCalculator cameraCenterCalculator, IDynamicFovCalculator dynamicFovCalculator, IPrimaryTargetResolver primaryTargetResolver, ICinematicCameraPlanner cinematicCameraPlanner, IUnifiedCameraComposer unifiedCameraComposer, ISceneIntentResolver sceneIntentResolver, IStellariumSscRenderer renderer, IAstronomicalSpatialCompositionEngine spatialCompositionEngine, ILogger<SscIntelligenceService> logger)
    {
        _nightWindowResolver = nightWindowResolver;
        _visibilityFilter = visibilityFilter;
        _cameraCenterCalculator = cameraCenterCalculator;
        _dynamicFovCalculator = dynamicFovCalculator;
        _primaryTargetResolver = primaryTargetResolver;
        _cinematicCameraPlanner = cinematicCameraPlanner;
        _unifiedCameraComposer = unifiedCameraComposer;
        _sceneIntentResolver = sceneIntentResolver;
        _renderer = renderer;
        _spatialCompositionEngine = spatialCompositionEngine;
        _logger = logger;
    }

    public SscIntelligenceResult Generate(SscIntelligenceRequest request, string? screenshotDirectory = null, string? screenshotFileNameWithoutExtension = null)
    {
        var rules = request.VisibilityRules ?? new VisibilityRules();
        var nightWindow = _nightWindowResolver.Resolve(request.ObservationUtc, request.Timezone, request.Latitude, request.Longitude, rules, request.AstronomicalNightStartUtc, request.AstronomicalNightEndUtc, request.SunAltitudeDeg);

        var (visible, removed) = _visibilityFilter.Filter(request.SkyObjectPositions, rules, request.SunAltitudeDeg);
        if (visible.Count == 0) throw new InvalidOperationException("No visible objects were available after filtering.");

        var sceneIntent = !string.IsNullOrWhiteSpace(request.SceneCode) || !string.IsNullOrWhiteSpace(request.SceneTitle)
            ? _sceneIntentResolver.Resolve(request.SceneCode ?? string.Empty, request.SceneTitle)
            : request.SceneIntent;

        var targets = _primaryTargetResolver.Resolve(visible, request.SceneCode, request.SceneTitle, request.ExplicitTargetObjectNames);
        var compositionObjects = targets.PrimaryTargets.Concat(targets.SecondaryTargets).DefaultIfEmpty().Any() ? targets.PrimaryTargets.Concat(targets.SecondaryTargets).ToList() : visible.ToList();
        var spatialAnalysis = _spatialCompositionEngine.Analyze(compositionObjects);
        var cameraObjects = spatialAnalysis.CompositionClass == SpatialCompositionClass.ImpossibleGrouping ? spatialAnalysis.DominantCluster.Objects : compositionObjects;

        var (cameraAltitudeRaw, cameraAzimuthRaw) = _cameraCenterCalculator.CalculateCenter(cameraObjects);
        var preliminaryCamera = _dynamicFovCalculator.Calculate(cameraObjects, cameraObjects, Array.Empty<SkyObjectPosition>(), Array.Empty<SkyObjectPosition>(), cameraAltitudeRaw, cameraAzimuthRaw, rules, sceneIntent);
        var fovInput = spatialAnalysis.RecommendedFovRange is { } range ? (range.MinDeg + range.MaxDeg) / 2d : preliminaryCamera.FovDeg;

        var composition = _unifiedCameraComposer.Compose(sceneIntent, cameraAltitudeRaw, cameraAzimuthRaw, fovInput, visible, cameraObjects, Array.Empty<SkyObjectPosition>(), Array.Empty<SkyObjectPosition>());
        var cameraPlan = _cinematicCameraPlanner.Plan(request.SceneCode, sceneIntent, cameraObjects, composition.FinalCameraAltitudeDeg, composition.FinalCameraAzimuthDeg, fovInput, request.LocationName, nightWindow.BestObservationUtc);
        var preserveHorizon = request.SceneCode?.Contains("wide", StringComparison.OrdinalIgnoreCase) == true || request.SceneTitle?.Contains("horizon", StringComparison.OrdinalIgnoreCase) == true;
        var horizonRefinement = CinematicRefinementEngine.RefineHorizon(cameraPlan, request.SceneCode, cameraObjects, preserveHorizon);
        _logger.LogInformation("HORIZON_COMPOSITION_REFINEMENT sceneCode={SceneCode} preserveHorizon={PreserveHorizon} targetAltitudeRange={TargetAltitudeRange} originalCameraAlt={OriginalCameraAlt:0.##} refinedCameraAlt={RefinedCameraAlt:0.##} originalFov={OriginalFov:0.##} refinedFov={RefinedFov:0.##} reason={Reason}",
            request.SceneCode,
            preserveHorizon,
            $"{cameraObjects.Min(x=>x.AltitudeDeg):0.#}-{cameraObjects.Max(x=>x.AltitudeDeg):0.#}",
            cameraPlan.CameraAltitude,
            horizonRefinement.RefinedCameraAltitude,
            cameraPlan.FovDegrees,
            horizonRefinement.RefinedFov,
            horizonRefinement.Reason);
        var camera = preliminaryCamera with { AltitudeDeg = horizonRefinement.RefinedCameraAltitude, AzimuthDeg = cameraPlan.CameraAzimuth, FovDeg = horizonRefinement.RefinedFov };

        _logger.LogInformation("AZIMUTH_WRAP_CALCULATION: sceneCode={SceneCode}, inputAzimuths={InputAzimuths}, normalizedCenter={NormalizedCenter:0.##}, normalizedSpread={NormalizedSpread:0.##}, computedFov={ComputedFov:0.##}",
            request.SceneCode,
            string.Join(",", cameraObjects.Select(x => $"{x.Name}:{NormalizeDegrees(x.AzimuthDeg):0.##}")),
            NormalizeDegrees(cameraAzimuthRaw),
            spatialAnalysis.AzimuthSpreadDeg,
            camera.FovDeg);

        _logger.LogInformation("SPATIAL_COMPOSITION_ANALYSIS: sceneCode={SceneCode}, objectNames={ObjectNames}, pairDistances={PairDistances}, maxAngularSeparation={MaxAngularSeparation:0.##}, altitudeSpread={AltitudeSpread:0.##}, azimuthSpread={AzimuthSpread:0.##}, compositionClass={CompositionClass}, recommendedFov={RecommendedFov}, splitRecommended={SplitRecommended}, clusters={Clusters}, dominantCluster={DominantCluster}, deferredObjects={DeferredObjects}",
            request.SceneCode,
            string.Join(",", compositionObjects.Select(x => x.Name)),
            string.Join(";", spatialAnalysis.PairDistances.Select(p => $"{p.ObjectA}-{p.ObjectB}:{p.AngularDistanceDeg:0.##}")),
            spatialAnalysis.MaxAngularSeparationDeg,
            spatialAnalysis.AltitudeSpreadDeg,
            spatialAnalysis.AzimuthSpreadDeg,
            spatialAnalysis.CompositionClass,
            spatialAnalysis.RecommendedFovRange is { } rf ? $"{rf.MinDeg:0.#}-{rf.MaxDeg:0.#}" : "split",
            spatialAnalysis.SplitRecommended,
            string.Join(" | ", spatialAnalysis.Clusters.Select(c => $"[{string.Join(',', c.ObjectNames)}]")),
            $"[{string.Join(',', spatialAnalysis.DominantCluster.ObjectNames)}]",
            string.Join(",", spatialAnalysis.DeferredObjects.Select(x => x.Name)));


        _logger.LogInformation("CINEMATIC_CAMERA_PLAN sceneCode={SceneCode} intent={Intent} objects={Objects} inputAltAz={InputAltAz} cameraAz={CameraAz:0.##} cameraAlt={CameraAlt:0.##} fov={Fov:0.##} framingMode={FramingMode} reason={Reason}",
            request.SceneCode,
            sceneIntent,
            string.Join(",", cameraObjects.Select(o => o.Name)),
            string.Join(";", cameraObjects.Select(o => $"{o.Name}@{o.AltitudeDeg:0.##}/{NormalizeDegrees(o.AzimuthDeg):0.##}")),
            cameraPlan.CameraAzimuth,
            cameraPlan.CameraAltitude,
            cameraPlan.FovDegrees,
            cameraPlan.FramingMode,
            cameraPlan.Reason);

        var shotType = cameraPlan.FramingMode;
        var overlayPolicy = new ConstellationOverlayPolicyResult(true, true, false, false, "medium", "default-policy-preserve-context");
        _logger.LogInformation("CONSTELLATION_OVERLAY_POLICY sceneCode={SceneCode} sceneIntent={SceneIntent} showLines={ShowLines} showLabels={ShowLabels} overlayDensity={OverlayDensity} reason={Reason}", request.SceneCode, sceneIntent, overlayPolicy.ShowConstellationLines, overlayPolicy.ShowConstellationLabels, overlayPolicy.OverlayDensity, overlayPolicy.Reason);
        var sortedByMag = cameraObjects.OrderBy(x => x.Magnitude).ToList();
        var primary = targets.PrimaryTargets.FirstOrDefault()?.Name ?? cameraObjects.First().Name;
        var secondaries = targets.SecondaryTargets.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var brightest = sortedByMag.FirstOrDefault()?.Name ?? primary;
        var emphasis = new ObjectEmphasisPolicyResult(primary, secondaries, brightest, primary, shotType == "HeroObject" ? "Hero" : "Context");
        _logger.LogInformation("OBJECT_EMPHASIS_POLICY sceneCode={SceneCode} primary={Primary} secondary={Secondary} brightest={Brightest} visualAnchor={VisualAnchor} emphasisMode={EmphasisMode}", request.SceneCode, emphasis.PrimaryObject, string.Join(',', emphasis.SecondaryObjects), emphasis.BrightestObject, emphasis.VisualAnchorObject, emphasis.EmphasisMode);
        var significanceScore = 0;
        if (cameraObjects.Any(x => x.Name.Contains("moon", StringComparison.OrdinalIgnoreCase))) significanceScore += 35;
        if (cameraObjects.Any(x => x.Name.Contains("venus", StringComparison.OrdinalIgnoreCase)) && cameraObjects.Any(x => x.Name.Contains("jupiter", StringComparison.OrdinalIgnoreCase))) significanceScore += 45;
        if (cameraObjects.Count > 2) significanceScore += 20;
        if (cameraObjects.Any(x => x.AltitudeDeg > 20d)) significanceScore += 10;
        if (spatialAnalysis.MaxAngularSeparationDeg is >= 5 and <= 30) significanceScore += 10;
        if (preserveHorizon) significanceScore += 5;
        if (cameraObjects.Any(x => x.AltitudeDeg < 10d)) significanceScore -= 20;
        if (spatialAnalysis.MaxAngularSeparationDeg > 70d) significanceScore -= 20;
        significanceScore = Math.Clamp(significanceScore, 0, 100);
        var significanceClass = significanceScore >= 85 ? "Hero" : significanceScore >= 65 ? "High" : significanceScore >= 40 ? "Medium" : "Low";
        var significance = new NarrativeSignificanceResult(significanceScore, significanceClass, $"intent={sceneIntent};separation={spatialAnalysis.MaxAngularSeparationDeg:0.#}");
        _logger.LogInformation("NARRATIVE_SIGNIFICANCE_SCORE sceneCode={SceneCode} score={Score} class={Class} reason={Reason}", request.SceneCode, significance.SignificanceScore, significance.SignificanceClass, significance.Reason);
        var paddingMultiplier = shotType switch
        {
            "HeroObject" => 1.6d,
            "PlanetGrouping" => 1.8d,
            "OrientationWide" => 2.2d,
            _ => sceneIntent == SceneIntentType.WideNight ? 2.5d : 1.8d
        };
        _logger.LogInformation("WEEKLY_V2_STELLARIUM_SCENE_DEBUG: sceneId={SceneId}, shotType={ShotType}, targetObjects={TargetObjects}, cameraAz={CameraAz:0.##}, cameraAlt={CameraAlt:0.##}, computedFov={ComputedFov:0.##}, paddingMultiplier={PaddingMultiplier:0.##}, safeFrameMarginApplied={SafeFrameMarginApplied}, verticalBiasApplied={VerticalBiasApplied:0.##}, geometrySource={GeometrySource}, fallbackUsed={FallbackUsed}",
            request.SceneCode ?? "unknown",
            shotType,
            string.Join(",", cameraObjects.Select(o => o.Name)),
            cameraPlan.CameraAzimuth,
            cameraPlan.CameraAltitude,
            cameraPlan.FovDegrees,
            paddingMultiplier,
            true,
            cameraPlan.VerticalBias,
            "skyfield-nearest-time-resolver",
            false);

        var script = _renderer.Render(new SscRenderRequest(nightWindow.BestObservationUtc, request.Longitude, request.Latitude, request.ElevationMeters, request.LocationName, camera.AltitudeDeg, camera.AzimuthDeg, camera.FovDeg, screenshotDirectory ?? ".", screenshotFileNameWithoutExtension ?? "scene", visible));
        var qualityReport = new CinematicQualitySceneReport(request.SceneCode ?? "unknown", shotType, cameraPlan with { CameraAltitude = horizonRefinement.RefinedCameraAltitude, FovDegrees = horizonRefinement.RefinedFov }, horizonRefinement, overlayPolicy, emphasis, significance, horizonRefinement.Warnings);
        return new SscIntelligenceResult(visible, removed, camera.AltitudeDeg, camera.AzimuthDeg, camera.FovDeg, camera.RequiresSplit, cameraAltitudeRaw, composition.Reason, targets.PrimaryTargets.Select(x => x.Name).ToList(), targets.SecondaryTargets.Select(x => x.Name).ToList(), targets.ContextTargets.Select(x => x.Name).ToList(), script.Script, nightWindow, qualityReport);
    }

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
