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
    private readonly IUnifiedCameraComposer _unifiedCameraComposer;
    private readonly ISceneIntentResolver _sceneIntentResolver;
    private readonly IStellariumSscRenderer _renderer;
    private readonly IAstronomicalSpatialCompositionEngine _spatialCompositionEngine;
    private readonly ILogger<SscIntelligenceService> _logger;

    public SscIntelligenceService(INightWindowResolver nightWindowResolver, IVisibilityFilter visibilityFilter, ICameraCenterCalculator cameraCenterCalculator, IDynamicFovCalculator dynamicFovCalculator, IPrimaryTargetResolver primaryTargetResolver, IUnifiedCameraComposer unifiedCameraComposer, ISceneIntentResolver sceneIntentResolver, IStellariumSscRenderer renderer, IAstronomicalSpatialCompositionEngine spatialCompositionEngine, ILogger<SscIntelligenceService> logger)
    {
        _nightWindowResolver = nightWindowResolver;
        _visibilityFilter = visibilityFilter;
        _cameraCenterCalculator = cameraCenterCalculator;
        _dynamicFovCalculator = dynamicFovCalculator;
        _primaryTargetResolver = primaryTargetResolver;
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
        var camera = preliminaryCamera with { AltitudeDeg = composition.FinalCameraAltitudeDeg, AzimuthDeg = composition.FinalCameraAzimuthDeg, FovDeg = fovInput };

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

        var script = _renderer.Render(new SscRenderRequest(nightWindow.BestObservationUtc, request.Longitude, request.Latitude, request.ElevationMeters, request.LocationName, camera.AltitudeDeg, camera.AzimuthDeg, camera.FovDeg, screenshotDirectory ?? ".", screenshotFileNameWithoutExtension ?? "scene"));
        return new SscIntelligenceResult(visible, removed, camera.AltitudeDeg, camera.AzimuthDeg, camera.FovDeg, camera.RequiresSplit, cameraAltitudeRaw, composition.Reason, targets.PrimaryTargets.Select(x => x.Name).ToList(), targets.SecondaryTargets.Select(x => x.Name).ToList(), targets.ContextTargets.Select(x => x.Name).ToList(), script.Script, nightWindow);
    }

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
