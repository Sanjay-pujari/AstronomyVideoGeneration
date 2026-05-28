using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Camera;

public sealed record HorizonCompositionRefinement(double RefinedCameraAltitude, double RefinedFov, double HorizonWeightApplied, IReadOnlyList<string> Warnings, string Reason);
public sealed record ConstellationOverlayPolicyResult(bool ShowConstellationLines, bool ShowConstellationLabels, bool ShowEquatorialGrid, bool ShowAzimuthGrid, string OverlayDensity, string Reason);
public sealed record ObjectEmphasisPolicyResult(string PrimaryObject, IReadOnlyList<string> SecondaryObjects, string BrightestObject, string VisualAnchorObject, string EmphasisMode);
public sealed record NarrativeSignificanceResult(int SignificanceScore, string SignificanceClass, string Reason);
public sealed record CinematicQualitySceneReport(string SceneCode, string CinematicStyle, CinematicCameraPlan CameraPlan, HorizonCompositionRefinement HorizonRefinement, ConstellationOverlayPolicyResult ConstellationOverlayPolicy, ObjectEmphasisPolicyResult ObjectEmphasisPolicy, NarrativeSignificanceResult NarrativeSignificance, IReadOnlyList<string> Warnings);

public static class CinematicRefinementEngine
{
    public static HorizonCompositionRefinement RefineHorizon(CinematicCameraPlan plan, string? sceneCode, IReadOnlyList<SkyObjectPosition> objects, bool preserveHorizon)
    {
        var alts = objects.Select(x => x.AltitudeDeg).ToArray();
        var minAlt = alts.Length == 0 ? plan.CameraAltitude : alts.Min();
        var maxAlt = alts.Length == 0 ? plan.CameraAltitude : alts.Max();
        var targetAlt = plan.CameraAltitude;
        var targetFov = plan.FovDegrees;
        var weight = preserveHorizon ? 0.6 : 0.2;
        var warnings = new List<string>();
        var reason = "baseline";
        if (preserveHorizon)
        {
            targetAlt = Math.Min(targetAlt, 38d);
            reason = "preserveHorizon=true";
        }
        if (minAlt < 25d)
        {
            targetAlt = Math.Min(targetAlt, 30d);
            targetFov = Math.Min(95d, targetFov + 6d);
            weight = Math.Max(weight, 0.85d);
            reason = "low-altitude-horizon-protection";
        }
        if (minAlt > 45d)
        {
            weight = Math.Min(weight, 0.25d);
            reason = "high-altitude-reduced-horizon-weight";
        }
        if (targetAlt < 5d || targetAlt > 85d) warnings.Add("refined camera altitude near practical bounds");
        return new HorizonCompositionRefinement(targetAlt, targetFov, weight, warnings, reason + $" altRange={minAlt:0.#}-{maxAlt:0.#}");
    }
}
