using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public sealed class CinematicAnchorSolver : ICinematicAnchorSolver
{
    public CinematicAnchorResult Solve(SceneIntentType sceneIntent, double currentCameraAltitudeDeg, double cameraAzimuthDeg, double fovDeg, IReadOnlyList<SkyObjectPosition> visibleObjects, IReadOnlyList<SkyObjectPosition> primaryTargets, IReadOnlyList<SkyObjectPosition> secondaryTargets, IReadOnlyList<SkyObjectPosition> contextTargets)
    {
        var profile = CinematicAnchorProfile.For(sceneIntent);
        var anchorTargets = ResolveAnchorTargets(sceneIntent, visibleObjects, primaryTargets, secondaryTargets, contextTargets);
        var targetAltitude = ComputeWeightedAltitude(anchorTargets);

        var rawAnchoredAltitude = targetAltitude + ((profile.DesiredY - 0.5d) * fovDeg);
        var anchoredCameraAltitude = Math.Clamp(rawAnchoredAltitude, 12d, 82d);
        var delta = anchoredCameraAltitude - currentCameraAltitudeDeg;

        var reason = $"{sceneIntent}: targetAlt={targetAltitude:0.##}°, desiredY={profile.DesiredY:0.##}, desiredX={profile.DesiredX:0.##}, fov={fovDeg:0.##}°";
        if (Math.Abs(rawAnchoredAltitude - anchoredCameraAltitude) > 0.001d)
        {
            reason += $"; clamped {rawAnchoredAltitude:0.##}° to {anchoredCameraAltitude:0.##}°";
        }

        return new CinematicAnchorResult(anchoredCameraAltitude, profile.DesiredY, profile.DesiredX, targetAltitude, delta, reason);
    }

    private static IReadOnlyList<SkyObjectPosition> ResolveAnchorTargets(SceneIntentType sceneIntent, IReadOnlyList<SkyObjectPosition> visibleObjects, IReadOnlyList<SkyObjectPosition> primaryTargets, IReadOnlyList<SkyObjectPosition> secondaryTargets, IReadOnlyList<SkyObjectPosition> contextTargets)
    {
        List<SkyObjectPosition> candidates = sceneIntent switch
        {
            SceneIntentType.HeroShot or SceneIntentType.CloseUp => [.. primaryTargets],
            SceneIntentType.Grouping or SceneIntentType.Educational => [.. primaryTargets, .. secondaryTargets],
            SceneIntentType.WideNight => [.. primaryTargets, .. secondaryTargets, .. contextTargets],
            _ => [.. primaryTargets]
        };

        if (candidates.Count == 0)
        {
            return visibleObjects;
        }

        return candidates;
    }

    private static double ComputeWeightedAltitude(IReadOnlyList<SkyObjectPosition> positions)
    {
        if (positions.Count == 0)
        {
            return 45d;
        }

        var totalWeight = positions.Sum(x => Math.Max(0.1d, Math.Abs(x.Magnitude) <= 0.001d ? 1d : 1d / Math.Max(0.25d, Math.Abs(x.Magnitude))));
        if (totalWeight <= 0.001d)
        {
            return positions.Average(x => x.AltitudeDeg);
        }

        return positions.Sum(x => x.AltitudeDeg * Math.Max(0.1d, Math.Abs(x.Magnitude) <= 0.001d ? 1d : 1d / Math.Max(0.25d, Math.Abs(x.Magnitude)))) / totalWeight;
    }
}
