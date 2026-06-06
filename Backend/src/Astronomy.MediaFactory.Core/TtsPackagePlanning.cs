namespace Astronomy.MediaFactory.Core;

public sealed record TtsPackagePlanningRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    string Language = "en",
    int? MaxPlans = 20,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record TtsPackagePlanningResult(
    int PlanCount,
    int GeneratedCount,
    int ReadyForAudioCount,
    IReadOnlyList<TtsPackageDocument> TtsPackages,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record TtsPackageDocument(
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
    DateTimeOffset GeneratedUtc);

public sealed record TtsVoiceProfile(
    string RecommendedVoice,
    string VoiceName,
    string Style,
    string Pitch,
    string Rate,
    string Volume);

public sealed record TtsMusicProfile(
    string Mood,
    string Intensity,
    string SuggestedCategory);

public sealed record TtsPackageSegment(
    int SceneNumber,
    string SceneName,
    string Text,
    string Ssml,
    int EstimatedDurationSeconds,
    IReadOnlyList<string> PauseHints,
    IReadOnlyList<string> EmphasisWords,
    VoicePerformanceMetadata? VoicePerformance,
    string OutputAudioPath);

public interface ITtsPackagePlanningService
{
    Task<TtsPackagePlanningResult> GenerateTtsPackagesAsync(TtsPackagePlanningRequest request, CancellationToken cancellationToken);
}
