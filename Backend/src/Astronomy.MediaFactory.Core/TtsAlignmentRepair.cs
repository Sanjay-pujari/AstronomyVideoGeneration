namespace Astronomy.MediaFactory.Core;

public sealed record TtsAlignmentRepairRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    string Language = "en",
    int? MaxPlans = 20,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record TtsAlignmentRepairResult(
    int PlanCount,
    int RepairedCount,
    int AlreadyValidCount,
    int FailedCount,
    int ReadyForAudioCount,
    IReadOnlyList<FinalTtsPackageDocument> FinalPackages,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record FinalTtsPackageDocument(
    string ContentGenerationPlanId,
    string RegionId,
    string Language,
    string ContentCategory,
    string PlannedFormat,
    string Title,
    string TtsProvider,
    TtsVoiceProfile VoiceProfile,
    TtsMusicProfile MusicProfile,
    IReadOnlyList<TtsPackageSegment> Segments,
    int TotalEstimatedDurationSeconds,
    bool ReadyForAudioGeneration,
    string GenerationSource,
    DateTimeOffset GeneratedUtc,
    string SsmlValidationStatus,
    DateTimeOffset SsmlValidatedUtc,
    bool ReadyForTts,
    string AlignmentRepairStatus,
    DateTimeOffset AlignmentRepairedUtc,
    IReadOnlyList<TtsSegmentValidationResult> SegmentValidationResults);

public interface ITtsAlignmentRepairService
{
    Task<TtsAlignmentRepairResult> RepairTtsAlignmentAsync(TtsAlignmentRepairRequest request, CancellationToken cancellationToken);
}
