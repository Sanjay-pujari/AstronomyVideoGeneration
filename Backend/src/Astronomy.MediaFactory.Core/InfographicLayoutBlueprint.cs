namespace Astronomy.MediaFactory.Core;

public sealed record InfographicLayoutBlueprintRequest(
    string EventId,
    string RegionId,
    string Language = "en",
    string ViewerPersona = "CasualSkyWatcher",
    string KnowledgeLevel = "Beginner",
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record InfographicLayoutBlueprintResponse(
    string EventId,
    int SceneCount,
    bool IsValid,
    IReadOnlyList<InfographicLayoutBlueprint> LayoutBlueprints,
    IReadOnlyList<string> Warnings);

public sealed record InfographicLayoutBlueprint(
    int SceneNumber,
    string SceneKey,
    string LayoutTemplate,
    int VisualCoveragePercent,
    int TextCoveragePercent,
    InfographicLayoutZones LayoutZones,
    IReadOnlyList<string> RequiredLayers,
    IReadOnlyList<string> ForbiddenPatterns);

public sealed record InfographicLayoutZones(
    IReadOnlyDictionary<string, string> TitleZone,
    IReadOnlyDictionary<string, string> HeroZone,
    IReadOnlyDictionary<string, string> SubtitleZone,
    IReadOnlyDictionary<string, string> AnnotationZone,
    IReadOnlyDictionary<string, string> ConstellationZone,
    IReadOnlyDictionary<string, string> ReferenceStarZone,
    IReadOnlyDictionary<string, string> CelestialObjectZone,
    IReadOnlyDictionary<string, string> HorizonZone,
    IReadOnlyDictionary<string, string> AltitudeGuideZone,
    IReadOnlyDictionary<string, string> TimelineZone,
    IReadOnlyDictionary<string, string> ViewingWindowZone,
    IReadOnlyDictionary<string, string> StepZone,
    IReadOnlyDictionary<string, string> SkyGuidanceZone,
    IReadOnlyDictionary<string, string> SignificanceZone,
    IReadOnlyDictionary<string, string> CtaZone);

public interface IInfographicLayoutBlueprintGenerator
{
    Task<InfographicLayoutBlueprintResponse> GenerateInfographicLayoutBlueprintAsync(InfographicLayoutBlueprintRequest request, CancellationToken cancellationToken);
}
