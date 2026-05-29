namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetExpansion;

public sealed record ExpandedStellariumSceneRequirement(
    string RenderSceneCode,
    string SourceSegmentId,
    string SourceSegmentType,
    IReadOnlyList<string> TargetObjects,
    DateTime PreferredObservationUtc,
    string? PreferredObservationLocal,
    IReadOnlyList<string> RequiredFrameTypes,
    string DesiredCameraIntent,
    string VisualRole,
    int Priority,
    string GeometrySource,
    IReadOnlyList<string> Warnings);

public sealed class ExpandedRenderSceneRequirementToStellariumSceneAdapter
{
    public ExpandedStellariumSceneRequirement Adapt(ExpandedRenderSceneRequirement requirement, DateTime fallbackObservationUtc)
    {
        var renderSceneCode = NormalizeSceneCode(requirement.RenderSceneCode);
        var targetObjects = requirement.TargetObjects
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var requiredFrameTypes = requirement.RequiredFrameTypes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ExpandedStellariumSceneRequirement(
            renderSceneCode,
            requirement.SourceSegmentId,
            requirement.SourceSegmentType,
            targetObjects,
            DateTime.SpecifyKind(requirement.PreferredObservationUtc ?? fallbackObservationUtc, DateTimeKind.Utc),
            requirement.PreferredObservationLocal,
            requiredFrameTypes,
            requirement.DesiredCameraIntent,
            requirement.VisualRole,
            requirement.Priority,
            requirement.GeometrySource,
            requirement.Warnings);
    }

    private static string NormalizeSceneCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "expanded_stellarium_scene";
        var normalized = new string(value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        while (normalized.Contains("__", StringComparison.Ordinal)) normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        return normalized.Trim('_');
    }
}
