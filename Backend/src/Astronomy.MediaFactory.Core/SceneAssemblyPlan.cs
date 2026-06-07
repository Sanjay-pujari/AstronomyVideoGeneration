namespace Astronomy.MediaFactory.Core;

public sealed record SceneAssemblyPlanRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    int? MaxPlans = 20,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record SceneAssemblyPlanResult(
    int PlanCount,
    int GeneratedCount,
    int ReadyForSceneRenderCount,
    int NotReadyCount,
    IReadOnlyList<SceneAssemblyPlanDocument> AssemblyPlans,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record SceneAssemblyPlanDocument(
    string ContentGenerationPlanId,
    string RegionId,
    string ContentCategory,
    string PlannedFormat,
    string Title,
    string OutputAspectRatio,
    SceneAssemblyResolution OutputResolution,
    int FrameRate,
    double TotalDurationSeconds,
    SceneAssemblyAudio Audio,
    IReadOnlyList<SceneAssemblyScene> Scenes,
    SceneAssemblyRenderReadiness RenderReadiness,
    string GenerationSource,
    DateTimeOffset GeneratedUtc);

public sealed record SceneAssemblyResolution(int Width, int Height);

public sealed record SceneAssemblyAudio(
    string CombinedNarrationPath,
    string MusicMood,
    string MusicIntensity,
    bool RequiresMusicBed);

public sealed record SceneAssemblyScene(
    int SceneNumber,
    string SceneName,
    double StartSecond,
    double EndSecond,
    double DurationSeconds,
    string OutputSceneVideoPath,
    string AudioPath,
    IReadOnlyList<SceneAssemblyLayer> Layers,
    SceneAssemblyMotion Motion,
    SceneAssemblyTransition Transition,
    SceneAssemblyCaptions Captions,
    IReadOnlyList<string> RenderNotes);

public sealed record SceneAssemblyLayer(
    string LayerType,
    string AssetType,
    string AssetPath,
    string RenderMode,
    string? FitMode,
    double? Opacity,
    string? SafeZone,
    int ZIndex);

public sealed record SceneAssemblyMotion(
    string Type,
    string Intensity,
    string Direction,
    double StartScale,
    double EndScale);

public sealed record SceneAssemblyTransition(
    string In,
    string Out,
    double DurationSeconds);

public sealed record SceneAssemblyCaptions(
    bool Enabled,
    string Source,
    string SafeZone,
    string Style);

public sealed record SceneAssemblyRenderReadiness(
    bool ReadyForSceneRender,
    IReadOnlyList<string> MissingInputs,
    IReadOnlyList<string> Warnings);

public interface ISceneAssemblyPlanService
{
    Task<SceneAssemblyPlanResult> GenerateSceneAssemblyPlansAsync(SceneAssemblyPlanRequest request, CancellationToken cancellationToken);
}
