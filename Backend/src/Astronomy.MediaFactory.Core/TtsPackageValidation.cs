namespace Astronomy.MediaFactory.Core;

public sealed record TtsPackageValidationRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    string Language = "en",
    int? MaxPlans = 20,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record TtsPackageValidationResult(
    int PlanCount,
    int ValidCount,
    int FixedCount,
    int InvalidCount,
    IReadOnlyList<CleanTtsPackageDocument> CleanPackages,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record TtsSegmentValidationResult(
    int SceneNumber,
    bool IsValid,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> FixesApplied);

public sealed record CleanTtsPackageDocument(
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
    IReadOnlyList<TtsSegmentValidationResult> SegmentValidationResults);

public interface ITtsPackageValidationService
{
    Task<TtsPackageValidationResult> ValidateTtsPackagesAsync(TtsPackageValidationRequest request, CancellationToken cancellationToken);
}
