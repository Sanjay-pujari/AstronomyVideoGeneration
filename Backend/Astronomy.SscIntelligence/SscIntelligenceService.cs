using Astronomy.SscIntelligence.Camera;
using Astronomy.SscIntelligence.Composition;
using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.NightWindow;
using Astronomy.SscIntelligence.Rendering;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;
using Astronomy.SscIntelligence.Visibility;

namespace Astronomy.SscIntelligence;

public sealed class SscIntelligenceService : ISscIntelligenceService
{
    private readonly INightWindowResolver _nightWindowResolver;
    private readonly IVisibilityFilter _visibilityFilter;
    private readonly ICameraCenterCalculator _cameraCenterCalculator;
    private readonly IDynamicFovCalculator _dynamicFovCalculator;
    private readonly IPrimaryTargetResolver _primaryTargetResolver;
    private readonly ICompositionBiasResolver _compositionBiasResolver;
    private readonly IStellariumSscRenderer _renderer;

    public SscIntelligenceService(INightWindowResolver nightWindowResolver, IVisibilityFilter visibilityFilter, ICameraCenterCalculator cameraCenterCalculator, IDynamicFovCalculator dynamicFovCalculator, IPrimaryTargetResolver primaryTargetResolver, ICompositionBiasResolver compositionBiasResolver, IStellariumSscRenderer renderer)
    {
        _nightWindowResolver = nightWindowResolver;
        _visibilityFilter = visibilityFilter;
        _cameraCenterCalculator = cameraCenterCalculator;
        _dynamicFovCalculator = dynamicFovCalculator;
        _primaryTargetResolver = primaryTargetResolver;
        _compositionBiasResolver = compositionBiasResolver;
        _renderer = renderer;
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
        var camera = _dynamicFovCalculator.Calculate(visible, targets.PrimaryTargets, targets.SecondaryTargets, targets.ContextTargets, bias.AltitudeDeg, bias.AzimuthDeg, rules, request.SceneIntent);

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

        return new SscIntelligenceResult(visible, removed, camera.AltitudeDeg, camera.AzimuthDeg, camera.FovDeg, camera.RequiresSplit, rawAltitude, bias.Reason, targets.PrimaryTargets.Select(x => x.Name).ToList(), targets.SecondaryTargets.Select(x => x.Name).ToList(), targets.ContextTargets.Select(x => x.Name).ToList(), script.Script, nightWindow);
    }
}
