using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public sealed class ScreenSpaceFramingSolver : IScreenSpaceFramingSolver
{
    public ScreenSpaceFramingResult Solve(SceneIntentType sceneIntent, double cameraAltitudeDeg, double cameraAzimuthDeg, double fovDeg, IReadOnlyList<SkyObjectPosition> primaryTargets, IReadOnlyList<SkyObjectPosition> secondaryTargets)
    {
        if (primaryTargets.Count == 0)
        {
            return new ScreenSpaceFramingResult(Math.Clamp(cameraAltitudeDeg, 12d, 82d), cameraAzimuthDeg, false, $"{sceneIntent}: no primary targets");
        }

        var halfFov = fovDeg / 2d;
        var topMargin = Math.Max(5d, fovDeg * 0.15d);
        var bottomMargin = Math.Max(5d, fovDeg * 0.18d);

        var workingAltitude = cameraAltitudeDeg;
        var maxPrimary = primaryTargets.Max(x => x.AltitudeDeg);
        var minPrimary = primaryTargets.Min(x => x.AltitudeDeg);
        var topSafeLimit = workingAltitude + halfFov - topMargin;
        var bottomSafeLimit = workingAltitude - halfFov + bottomMargin;
        var reasons = new List<string>();

        // Alt/Az screen-space approximation: targetScreenOffset ~= targetAltitude - cameraAltitude.
        // Large positive offset means near top edge; reducing that requires increasing camera altitude.
        if (maxPrimary > topSafeLimit)
        {
            var delta = maxPrimary - topSafeLimit;
            workingAltitude += delta;
            reasons.Add($"raised camera {delta:0.##}° to avoid top clipping");
        }

        topSafeLimit = workingAltitude + halfFov - topMargin;
        bottomSafeLimit = workingAltitude - halfFov + bottomMargin;
        if (minPrimary < bottomSafeLimit)
        {
            var delta = bottomSafeLimit - minPrimary;
            workingAltitude -= delta;
            reasons.Add($"lowered camera {delta:0.##}° to avoid bottom clipping");
        }

        var clamped = Math.Clamp(workingAltitude, 12d, 82d);
        if (Math.Abs(clamped - workingAltitude) > 0.001d)
        {
            reasons.Add($"clamped to {clamped:0.##}°");
        }

        return new ScreenSpaceFramingResult(clamped, cameraAzimuthDeg, Math.Abs(clamped - cameraAltitudeDeg) > 0.001d, reasons.Count == 0 ? $"{sceneIntent}: framing unchanged" : string.Join("; ", reasons));
    }
}
