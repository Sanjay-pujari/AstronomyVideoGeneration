namespace Astronomy.MediaFactory.Core;

public sealed record DirectorTimelineRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    int? MaxPlans = 20,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record DirectorTimelineResult(
    int PlanCount,
    int GeneratedCount,
    int ReadyForRenderCount,
    int NotReadyCount,
    IReadOnlyList<DirectorTimelineDocument> Timelines,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record DirectorTimelineDocument(
    string ContentGenerationPlanId,
    string RegionId,
    string ContentCategory,
    string PlannedFormat,
    string Title,
    double EstimatedDurationSeconds,
    DirectorTimelineAudio Audio,
    IReadOnlyList<DirectorTimelineScene> Scenes,
    DirectorTimelineRenderReadiness RenderReadiness,
    string GenerationSource,
    DateTimeOffset GeneratedUtc);

public sealed record DirectorTimelineAudio(
    string CombinedNarrationPath,
    double TotalNarrationDurationSeconds,
    string VoiceName,
    string MusicMood,
    string MusicIntensity);

public sealed record DirectorTimelineScene(
    int SceneNumber,
    string SceneName,
    double StartSecond,
    double EndSecond,
    double DurationSeconds,
    string NarrationText,
    string AudioPath,
    DirectorTimelineAsset PrimaryAsset,
    IReadOnlyList<DirectorTimelineAsset> SecondaryAssets,
    DirectorTimelineOverlayPlan OverlayPlan,
    string CameraMotion,
    string TransitionIn,
    string TransitionOut,
    string VisualMood,
    string MusicCue,
    IReadOnlyList<string> QualityNotes);

public sealed record DirectorTimelineAsset(
    string AssetType,
    string Path,
    string Usage);

public sealed record DirectorTimelineOverlayPlan(
    string TextOverlayPath,
    string CaptionSafeZone,
    bool ShowLabels);

public sealed record DirectorTimelineRenderReadiness(
    bool ReadyForRender,
    IReadOnlyList<string> MissingRequiredAssets,
    IReadOnlyList<string> Warnings);

public interface IDirectorTimelineService
{
    Task<DirectorTimelineResult> GenerateDirectorTimelinesAsync(DirectorTimelineRequest request, CancellationToken cancellationToken);
}
