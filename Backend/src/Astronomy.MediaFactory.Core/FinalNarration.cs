namespace Astronomy.MediaFactory.Core;

public sealed record FinalNarrationRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    string Language = "en",
    int? MaxPlans = 3,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record FinalNarrationResult(
    int PlanCount,
    int GeneratedCount,
    int ReadyForTtsCount,
    int NotReadyCount,
    IReadOnlyList<FinalNarrationDocument> FinalNarrations,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record FinalNarrationDocument(
    string Title,
    string RegionId,
    string ContentGenerationPlanId,
    string ContentCategory,
    string ExecutiveProducerStyle,
    int EstimatedDurationSeconds,
    IReadOnlyList<FinalNarrationSegment> Segments,
    int QualityScore,
    FinalNarrationQualityChecklist QualityChecklist,
    string GenerationSource,
    DateTimeOffset GeneratedUtc);

public sealed record FinalNarrationSegment(
    int SceneNumber,
    string SceneName,
    string FinalNarration,
    string ScenePurpose,
    string RetentionRole,
    string VoiceDirection,
    IReadOnlyList<string> PauseHints,
    IReadOnlyList<string> EmphasisWords,
    string VisualCue,
    string TransitionCue);

public sealed record FinalNarrationQualityChecklist(
    bool TitleNotRead,
    bool NoDuplicateNarration,
    bool UniqueScenePurpose,
    bool StrongHook,
    bool ProfessionalTone,
    bool ScientificallySafe,
    bool VoiceFriendly,
    bool ReadyForTts);

public interface IFinalNarrationService
{
    Task<FinalNarrationResult> GenerateFinalNarrationAsync(FinalNarrationRequest request, CancellationToken cancellationToken);
}
