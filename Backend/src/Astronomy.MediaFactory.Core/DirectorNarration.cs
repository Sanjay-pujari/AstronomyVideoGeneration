namespace Astronomy.MediaFactory.Core;

public sealed record DirectorNarrationRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    string Language = "en",
    int? MaxPlans = 20,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record DirectorNarrationResult(
    int PlanCount,
    int GeneratedCount,
    IReadOnlyList<DirectorNarrationDocument> DirectorNarrations,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record DirectorNarrationDocument(
    string Title,
    string DirectorStyle,
    int EstimatedDurationSeconds,
    IReadOnlyList<DirectorNarrationSegment> Segments);

public sealed record DirectorNarrationSegment(
    int SceneNumber,
    string SceneName,
    string NarrationDraft,
    string DirectorNarration,
    string RetentionPurpose,
    string Emotion,
    IReadOnlyList<string> PauseHints,
    IReadOnlyList<string> EmphasisWords,
    IReadOnlyList<string> AssetSynchronizationHints);

public interface IDirectorNarrationService
{
    Task<DirectorNarrationResult> GenerateDirectorNarrationAsync(DirectorNarrationRequest request, CancellationToken cancellationToken);
}
