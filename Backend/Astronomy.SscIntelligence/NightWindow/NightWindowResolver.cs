using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.NightWindow;

public sealed class NightWindowResolver : INightWindowResolver
{
    public NightWindowResult Resolve(DateTime observationUtc, VisibilityRules rules, double? sunAltitudeDeg = null)
    {
        var isNight = !sunAltitudeDeg.HasValue || sunAltitudeDeg.Value <= rules.TwilightSunAltitudeThresholdDeg;
        return new NightWindowResult(isNight, observationUtc, sunAltitudeDeg);
    }
}
