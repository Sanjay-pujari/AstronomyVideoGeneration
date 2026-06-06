namespace Astronomy.MediaFactory.Core;

public sealed record PolishedNarrationRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    string Language = "en",
    int? MaxPlans = 3,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record PolishedNarrationResult(
    int PlanCount,
    int PolishedCount,
    int ReadyForTtsCount,
    int NotReadyCount,
    IReadOnlyList<PolishedNarrationDocument> PolishedNarrations,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record PolishedNarrationDocument(
    string Title,
    string RegionId,
    string ContentGenerationPlanId,
    string ContentCategory,
    string ExecutiveProducerStyle,
    int EstimatedDurationSeconds,
    IReadOnlyList<PolishedNarrationSegment> Segments,
    int QualityScore,
    PolishedNarrationQualityBreakdown QualityBreakdown,
    FinalNarrationQualityChecklist QualityChecklist,
    PolishedNarrationTtsReadiness TtsReadiness,
    PolishedNarrationDurationValidation DurationValidation,
    string GenerationSource,
    string InputGenerationSource,
    DateTimeOffset GeneratedUtc);

public sealed record PolishedNarrationSegment(
    int SceneNumber,
    string SceneName,
    string FinalNarration,
    string ScenePurpose,
    string RetentionRole,
    string VoiceDirection,
    IReadOnlyList<string> PauseHints,
    IReadOnlyList<string> EmphasisWords,
    string VisualCue,
    string TransitionCue,
    VoicePerformanceMetadata VoicePerformance);

public sealed record VoicePerformanceMetadata(
    string SpeechRate,
    string Energy,
    string Tone,
    IReadOnlyList<string> DramaticPauseAfter,
    string MusicIntensity);

public sealed record PolishedNarrationTtsReadiness(
    bool ReadyForTts,
    string RecommendedVoice,
    string RecommendedStyle,
    string RecommendedSpeechRate,
    string RecommendedPitch,
    string RecommendedMusicMood,
    bool RequiresHumanReview);

public sealed record PolishedNarrationDurationValidation(
    int WordCount,
    int EstimatedDurationSeconds,
    int SpeechRateWpm,
    string DurationConfidence);

public sealed record PolishedNarrationQualityBreakdown(
    int HookQuality,
    int ScientificSafety,
    int SceneUniqueness,
    int VoiceFriendliness,
    int RetentionFlow,
    int EmotionalClose,
    int Total);

public interface IPolishedNarrationService
{
    Task<PolishedNarrationResult> PolishFinalNarrationAsync(PolishedNarrationRequest request, CancellationToken cancellationToken);
}
