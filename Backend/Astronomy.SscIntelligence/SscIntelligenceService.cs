using Astronomy.SscIntelligence.Camera;
using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.NightWindow;
using Astronomy.SscIntelligence.Rendering;
using Astronomy.SscIntelligence.SceneIntent;
using Astronomy.SscIntelligence.Visibility;

namespace Astronomy.SscIntelligence;

public sealed class SscIntelligenceService : ISscIntelligenceService
{
    private readonly INightWindowResolver _nightWindowResolver;
    private readonly IVisibilityFilter _visibilityFilter;
    private readonly ICameraCenterCalculator _cameraCenterCalculator;
    private readonly IDynamicFovCalculator _dynamicFovCalculator;
    private readonly IStellariumSscRenderer _renderer;

    public SscIntelligenceService(INightWindowResolver nightWindowResolver, IVisibilityFilter visibilityFilter, ICameraCenterCalculator cameraCenterCalculator, IDynamicFovCalculator dynamicFovCalculator, IStellariumSscRenderer renderer)
    {
        _nightWindowResolver = nightWindowResolver;
        _visibilityFilter = visibilityFilter;
        _cameraCenterCalculator = cameraCenterCalculator;
        _dynamicFovCalculator = dynamicFovCalculator;
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

        var (rawAltitude, azimuth) = _cameraCenterCalculator.CalculateCenter(visible);
        var altitude = Math.Clamp(rawAltitude + GetAltitudeBias(request.SceneIntent), 12d, 80d);
        var camera = _dynamicFovCalculator.Calculate(visible, altitude, azimuth, rules, request.SceneIntent);

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

        return new SscIntelligenceResult(visible, removed, camera.AltitudeDeg, camera.AzimuthDeg, camera.FovDeg, camera.RequiresSplit, script.Script, nightWindow);
    }

    private static double GetAltitudeBias(SceneIntent intent) => intent switch
    {
        SceneIntent.HeroShot => 6d,
        SceneIntent.WideNight => 8d,
        SceneIntent.Educational => 5d,
        SceneIntent.CloseUp => 2d,
        _ => 4d
    };
}
