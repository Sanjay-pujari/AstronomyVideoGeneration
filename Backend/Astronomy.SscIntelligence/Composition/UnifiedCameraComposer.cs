using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public sealed class UnifiedCameraComposer : IUnifiedCameraComposer
{
    public UnifiedCameraCompositionResult Compose(SceneIntentType sceneIntent, double rawCameraAltitudeDeg, double rawCameraAzimuthDeg, double fovDeg, IReadOnlyList<SkyObjectPosition> visibleObjects, IReadOnlyList<SkyObjectPosition> primaryTargets, IReadOnlyList<SkyObjectPosition> secondaryTargets, IReadOnlyList<SkyObjectPosition> contextTargets)
    {
        var profile = UnifiedCameraCompositionProfile.For(sceneIntent);
        var anchorTargets = ResolveAnchorTargets(sceneIntent, visibleObjects, primaryTargets, secondaryTargets, contextTargets);
        var targetAltitude = ComputeWeightedAltitude(anchorTargets);
        var cameraAltitude = targetAltitude + ((profile.DesiredY - 0.5d) * fovDeg);
        var rawSolvedAltitude = cameraAltitude;

        var halfFov = fovDeg / 2d;
        var topSafeMargin = Math.Max(5d, fovDeg * 0.15d);
        var bottomSafeMargin = Math.Max(5d, fovDeg * 0.18d);

        var maxTargetAltitude = anchorTargets.Max(x => x.AltitudeDeg);
        var minTargetAltitude = anchorTargets.Min(x => x.AltitudeDeg);

        var topSafeAltitude = cameraAltitude + halfFov - topSafeMargin;
        if (maxTargetAltitude > topSafeAltitude)
        {
            cameraAltitude += maxTargetAltitude - topSafeAltitude;
        }

        var bottomSafeAltitude = cameraAltitude - halfFov + bottomSafeMargin;
        if (minTargetAltitude < bottomSafeAltitude)
        {
            cameraAltitude -= bottomSafeAltitude - minTargetAltitude;
        }

        var unclampedFinalAltitude = cameraAltitude;
        cameraAltitude = Math.Clamp(cameraAltitude, 12d, 82d);
        topSafeAltitude = cameraAltitude + halfFov - topSafeMargin;
        bottomSafeAltitude = cameraAltitude - halfFov + bottomSafeMargin;

        var adjustment = cameraAltitude - rawSolvedAltitude;
        var reason = $"{sceneIntent}: targetAlt={targetAltitude:0.##}°, desiredY={profile.DesiredY:0.##}, fov={fovDeg:0.##}°, rawSolvedAlt={rawSolvedAltitude:0.##}°";
        if (Math.Abs(unclampedFinalAltitude - cameraAltitude) > 0.001d)
        {
            reason += $"; clamped {unclampedFinalAltitude:0.##}° to {cameraAltitude:0.##}°";
        }

        return new UnifiedCameraCompositionResult(
            cameraAltitude,
            rawCameraAzimuthDeg,
            rawCameraAltitudeDeg,
            rawCameraAzimuthDeg,
            fovDeg,
            profile.DesiredY,
            profile.DesiredX,
            anchorTargets.Select(x => x.Name).ToList(),
            targetAltitude,
            topSafeAltitude,
            bottomSafeAltitude,
            adjustment,
            0d,
            reason);
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

        return candidates.Count == 0 ? visibleObjects : candidates;
    }

    private static double ComputeWeightedAltitude(IReadOnlyList<SkyObjectPosition> positions)
    {
        var totalWeight = positions.Sum(x => Math.Max(0.1d, Math.Abs(x.Magnitude) <= 0.001d ? 1d : 1d / Math.Max(0.25d, Math.Abs(x.Magnitude))));
        return totalWeight <= 0.001d
            ? positions.Average(x => x.AltitudeDeg)
            : positions.Sum(x => x.AltitudeDeg * Math.Max(0.1d, Math.Abs(x.Magnitude) <= 0.001d ? 1d : 1d / Math.Max(0.25d, Math.Abs(x.Magnitude)))) / totalWeight;
    }
}
