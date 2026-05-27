using Astronomy.SscIntelligence.Camera;
using Astronomy.SscIntelligence.Composition;
using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.NightWindow;
using Astronomy.SscIntelligence.Rendering;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;
using Astronomy.SscIntelligence.SceneIntent;
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
    private readonly ILogger<SscIntelligenceService> _logger;

    public SscIntelligenceService(INightWindowResolver nightWindowResolver, IVisibilityFilter visibilityFilter, ICameraCenterCalculator cameraCenterCalculator, IDynamicFovCalculator dynamicFovCalculator, IPrimaryTargetResolver primaryTargetResolver, IUnifiedCameraComposer unifiedCameraComposer, ISceneIntentResolver sceneIntentResolver, IStellariumSscRenderer renderer, ILogger<SscIntelligenceService> logger)
    {
        _nightWindowResolver = nightWindowResolver;
        _visibilityFilter = visibilityFilter;
        _cameraCenterCalculator = cameraCenterCalculator;
        _dynamicFovCalculator = dynamicFovCalculator;
        _primaryTargetResolver = primaryTargetResolver;
        _unifiedCameraComposer = unifiedCameraComposer;
        _sceneIntentResolver = sceneIntentResolver;
        _renderer = renderer;
        _logger = logger;
    }

    public SscIntelligenceResult Generate(SscIntelligenceRequest request, string? screenshotDirectory = null, string? screenshotFileNameWithoutExtension = null)
    {
        var rules = request.VisibilityRules ?? new VisibilityRules();
        var nightWindow = _nightWindowResolver.Resolve(request.ObservationUtc, request.Timezone, request.Latitude, request.Longitude, rules, request.AstronomicalNightStartUtc, request.AstronomicalNightEndUtc, request.SunAltitudeDeg);

        var (visible, removed) = _visibilityFilter.Filter(request.SkyObjectPositions, rules, request.SunAltitudeDeg);
        if (visible.Count == 0)
        {
            throw new InvalidOperationException("No visible objects were available after filtering.");
        }

        var sceneIntent = !string.IsNullOrWhiteSpace(request.SceneCode) || !string.IsNullOrWhiteSpace(request.SceneTitle)
            ? _sceneIntentResolver.Resolve(request.SceneCode ?? string.Empty, request.SceneTitle)
            : request.SceneIntent;

        var targets = _primaryTargetResolver.Resolve(visible, request.SceneCode, request.SceneTitle, request.ExplicitTargetObjectNames);
        var weighted = targets.AllTargets;
        var (rawAltitude, azimuth) = _cameraCenterCalculator.CalculateCenter(weighted);
        var preliminaryCamera = _dynamicFovCalculator.Calculate(visible, targets.PrimaryTargets, targets.SecondaryTargets, targets.ContextTargets, rawAltitude, azimuth, rules, sceneIntent);
        var composition = _unifiedCameraComposer.Compose(sceneIntent, rawAltitude, azimuth, preliminaryCamera.FovDeg, visible, targets.PrimaryTargets, targets.SecondaryTargets, targets.ContextTargets);
        var camera = preliminaryCamera with { AltitudeDeg = composition.FinalCameraAltitudeDeg, AzimuthDeg = composition.FinalCameraAzimuthDeg };

        var sceneSemantics = ResolveSceneSemantics(request.SceneCode, request.SceneTitle);
        _logger.LogInformation("SSC unified composition: sceneCode={SceneCode}, sceneIntent={SceneIntent}, anchorTargetNames={AnchorTargetNames}, desiredY={DesiredY:0.##}, targetAltitude={TargetAltitude:0.##}, rawCameraAltitude={RawCameraAltitude:0.##}, finalCameraAltitude={FinalCameraAltitude:0.##}, rawCameraAzimuth={RawCameraAzimuth:0.##}, finalCameraAzimuth={FinalCameraAzimuth:0.##}, fov={Fov:0.##}, reason={Reason}, sceneSemantics={SceneSemantics}", request.SceneCode, sceneIntent, string.Join(",", composition.AnchorTargetNames), composition.DesiredY, composition.TargetAltitudeDeg, composition.RawCameraAltitudeDeg, composition.FinalCameraAltitudeDeg, composition.RawCameraAzimuthDeg, composition.FinalCameraAzimuthDeg, composition.FovDeg, composition.Reason, sceneSemantics);

        var script = _renderer.Render(new SscRenderRequest(
            nightWindow.BestObservationUtc,
            request.Longitude,
            request.Latitude,
            request.ElevationMeters,
            request.LocationName,
            camera.AltitudeDeg,
            camera.AzimuthDeg,
            camera.FovDeg,
            screenshotDirectory ?? ".",
            screenshotFileNameWithoutExtension ?? "scene"));

        return new SscIntelligenceResult(visible, removed, camera.AltitudeDeg, camera.AzimuthDeg, camera.FovDeg, camera.RequiresSplit, rawAltitude, composition.Reason, targets.PrimaryTargets.Select(x => x.Name).ToList(), targets.SecondaryTargets.Select(x => x.Name).ToList(), targets.ContextTargets.Select(x => x.Name).ToList(), script.Script, nightWindow);
    }

    private static string ResolveSceneSemantics(string? sceneCode, string? sceneTitle)
    {
        var text = $"{sceneCode} {sceneTitle}".ToLowerInvariant();
        var flags = new List<string>();
        if (text.Contains("grouping")) flags.Add("grouping");
        if (text.Contains("conjunction")) flags.Add("conjunction");
        if (text.Contains("planetaryapparel") || text.Contains("planetary apparel")) flags.Add("planetaryApparel");
        if (text.Contains("planetaryparade") || text.Contains("planetary parade")) flags.Add("planetaryParade");
        if (text.Contains("constellation")) flags.Add("constellation");
        if (text.Contains("educational")) flags.Add("educational");
        return flags.Count == 0 ? "none" : string.Join(",", flags);
    }
}
