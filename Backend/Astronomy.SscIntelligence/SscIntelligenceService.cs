using Astronomy.SscIntelligence.Camera;
using Astronomy.SscIntelligence.Composition;
using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.NightWindow;
using Astronomy.SscIntelligence.Rendering;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;
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
    private readonly ICompositionBiasResolver _compositionBiasResolver;
    private readonly IDynamicBiasLimiter _dynamicBiasLimiter;
    private readonly IScreenSpaceFramingSolver _screenSpaceFramingSolver;
    private readonly IStellariumSscRenderer _renderer;
    private readonly ILogger<SscIntelligenceService> _logger;

    public SscIntelligenceService(INightWindowResolver nightWindowResolver, IVisibilityFilter visibilityFilter, ICameraCenterCalculator cameraCenterCalculator, IDynamicFovCalculator dynamicFovCalculator, IPrimaryTargetResolver primaryTargetResolver, ICompositionBiasResolver compositionBiasResolver, IDynamicBiasLimiter dynamicBiasLimiter, IScreenSpaceFramingSolver screenSpaceFramingSolver, IStellariumSscRenderer renderer, ILogger<SscIntelligenceService> logger)
    {
        _nightWindowResolver = nightWindowResolver;
        _visibilityFilter = visibilityFilter;
        _cameraCenterCalculator = cameraCenterCalculator;
        _dynamicFovCalculator = dynamicFovCalculator;
        _primaryTargetResolver = primaryTargetResolver;
        _compositionBiasResolver = compositionBiasResolver;
        _dynamicBiasLimiter = dynamicBiasLimiter;
        _screenSpaceFramingSolver = screenSpaceFramingSolver;
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

        var targets = _primaryTargetResolver.Resolve(visible, request.SceneCode, request.SceneTitle, request.ExplicitTargetObjectNames);
        var weighted = targets.AllTargets;
        var (rawAltitude, azimuth) = _cameraCenterCalculator.CalculateCenter(weighted);
        var spread = weighted.Count > 1 ? weighted.Max(x => x.AltitudeDeg) - weighted.Min(x => x.AltitudeDeg) : 0d;
        var bias = _compositionBiasResolver.Resolve(request.SceneIntent, rawAltitude, azimuth, spread, (weighted.Min(x => x.AltitudeDeg), weighted.Max(x => x.AltitudeDeg)));
        var baseBiasDeg = bias.AltitudeDeg - rawAltitude;
        var preliminaryCamera = _dynamicFovCalculator.Calculate(visible, targets.PrimaryTargets, targets.SecondaryTargets, targets.ContextTargets, bias.AltitudeDeg, bias.AzimuthDeg, rules, request.SceneIntent);
        var limitedBias = _dynamicBiasLimiter.Limit(request.SceneIntent, rawAltitude, baseBiasDeg, preliminaryCamera.FovDeg, targets.PrimaryTargets);
        var altitudeAfterBias = rawAltitude + limitedBias.LimitedBiasDeg;
        var framing = _screenSpaceFramingSolver.Solve(request.SceneIntent, altitudeAfterBias, bias.AzimuthDeg, preliminaryCamera.FovDeg, targets.PrimaryTargets, targets.SecondaryTargets);
        var camera = preliminaryCamera with { AltitudeDeg = framing.FinalCameraAltitudeDeg, AzimuthDeg = framing.CameraAzimuthDeg };

        var sceneSemantics = ResolveSceneSemantics(request.SceneCode, request.SceneTitle);
        _logger.LogInformation("SSC composition diagnostics: rawCameraAltitude={RawCameraAltitude:0.##}, baseBiasDeg={BaseBiasDeg:0.##}, limitedBiasDeg={LimitedBiasDeg:0.##}, biasWasLimited={BiasWasLimited}, biasLimitReason={BiasLimitReason}, altitudeAfterBias={AltitudeAfterBias:0.##}, finalCameraAltitude={FinalCameraAltitude:0.##}, screenSpaceFramingReason={ScreenSpaceFramingReason}, maxPrimaryAltitude={MaxPrimaryAltitude:0.##}, minPrimaryAltitude={MinPrimaryAltitude:0.##}, fov={Fov:0.##}, sceneSemantics={SceneSemantics}", rawAltitude, baseBiasDeg, limitedBias.LimitedBiasDeg, limitedBias.WasLimited, limitedBias.Reason, altitudeAfterBias, camera.AltitudeDeg, framing.Reason, limitedBias.MaxPrimaryAltitudeDeg, limitedBias.MinPrimaryAltitudeDeg, preliminaryCamera.FovDeg, sceneSemantics);

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

        return new SscIntelligenceResult(visible, removed, camera.AltitudeDeg, camera.AzimuthDeg, camera.FovDeg, camera.RequiresSplit, rawAltitude, $"{bias.Reason}; {limitedBias.Reason}; {framing.Reason}", targets.PrimaryTargets.Select(x => x.Name).ToList(), targets.SecondaryTargets.Select(x => x.Name).ToList(), targets.ContextTargets.Select(x => x.Name).ToList(), script.Script, nightWindow);
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
