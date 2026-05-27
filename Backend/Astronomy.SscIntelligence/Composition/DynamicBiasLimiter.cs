using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public sealed class DynamicBiasLimiter : IDynamicBiasLimiter
{
    public DynamicBiasLimitResult Limit(SceneIntentType sceneIntent, double rawCameraAltitudeDeg, double proposedBiasDeg, double fovDeg, IReadOnlyList<SkyObjectPosition> primaryTargets)
    {
        if (primaryTargets.Count == 0)
        {
            return new DynamicBiasLimitResult(proposedBiasDeg, proposedBiasDeg, false, $"{sceneIntent}: no primary targets", 0, 0);
        }

        var maxPrimaryAltitude = primaryTargets.Max(x => x.AltitudeDeg);
        var minPrimaryAltitude = primaryTargets.Min(x => x.AltitudeDeg);
        var limitedBias = proposedBiasDeg;

        var halfVerticalFov = fovDeg / 2d;
        var topMarginDeg = Math.Max(5d, fovDeg * 0.15d);
        var bottomMarginDeg = Math.Max(5d, fovDeg * 0.18d);

        var proposedCameraAltitude = rawCameraAltitudeDeg + limitedBias;
        var frameTopAltitude = proposedCameraAltitude + halfVerticalFov;
        var frameBottomAltitude = proposedCameraAltitude - halfVerticalFov;

        var topSafeLimit = frameTopAltitude - topMarginDeg;
        var bottomSafeLimit = frameBottomAltitude + bottomMarginDeg;
        var reasons = new List<string>();

        if (maxPrimaryAltitude > topSafeLimit)
        {
            var delta = maxPrimaryAltitude - topSafeLimit;
            limitedBias -= delta;
            reasons.Add($"reduced bias {delta:0.##}° for top safety");
        }

        proposedCameraAltitude = rawCameraAltitudeDeg + limitedBias;
        frameBottomAltitude = proposedCameraAltitude - halfVerticalFov;
        bottomSafeLimit = frameBottomAltitude + bottomMarginDeg;

        if (minPrimaryAltitude < bottomSafeLimit)
        {
            var delta = bottomSafeLimit - minPrimaryAltitude;
            limitedBias += delta;
            reasons.Add($"increased bias {delta:0.##}° for bottom safety");
        }

        return new DynamicBiasLimitResult(limitedBias, proposedBiasDeg, Math.Abs(limitedBias - proposedBiasDeg) > 0.001d, reasons.Count == 0 ? $"{sceneIntent}: no bias limit" : string.Join("; ", reasons), maxPrimaryAltitude, minPrimaryAltitude);
    }
}
