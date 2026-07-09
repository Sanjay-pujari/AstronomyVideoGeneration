namespace Astronomy.MediaFactory.Core;

public sealed record QuestionDrivenNarrationRequest(
    string EventId,
    string RegionId,
    string Language = "en",
    bool DryRun = true,
    bool OverwriteExisting = false,
    ProductionPipelineExecutionContext? ProductionContext = null,
    Guid? PlanId = null,
    string? EventType = null,
    string? Title = null,
    string? ShortTitle = null,
    IReadOnlyList<string>? PrimaryObjects = null,
    IReadOnlyList<string>? SecondaryObjects = null,
    string? LocalPeakTime = null,
    string? SkyDirectionHint = null,
    string? BestViewingWindowLocal = null,
    string? StrategyId = null,
    string? SourceOfEventId = null);

public sealed record QuestionDrivenNarrationResponse(
    string EventId,
    int SceneCount,
    int TotalEstimatedDurationSeconds,
    bool IsValid,
    QuestionDrivenNarrationDto Narration,
    QuestionDrivenNarrationReviewDto Review,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record QuestionDrivenNarrationDto(
    string EventId,
    string RegionId,
    string Language,
    IReadOnlyList<QuestionDrivenNarrationSceneDto> Scenes,
    int TotalEstimatedDurationSeconds,
    DateTimeOffset GeneratedUtc,
    string NarrationVersion = "V3",
    QuestionDrivenNarrationDiagnosticsDto? Diagnostics = null);

public sealed record QuestionDrivenNarrationSceneDto(
    int SceneNumber,
    string QuestionType,
    string ScenePurpose,
    string ViewerQuestion,
    string ViewerTakeaway,
    string SourceAnswer,
    string NarrationIntent,
    string NarrationText,
    int EstimatedDurationSeconds,
    string VoiceDirection,
    string CaptionText,
    string Section = "",
    string SceneType = "");

public sealed record QuestionDrivenNarrationReviewDto(
    string EventId,
    string RegionId,
    string Language,
    bool IsValid,
    int SceneCount,
    int TotalEstimatedDurationSeconds,
    IReadOnlyList<QuestionDrivenNarrationReviewCheckDto> Checks,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedUtc,
    bool RequiredSectionsPresent = false,
    bool RepetitiveSentenceOpenings = true,
    bool StoryStructurePassed = false,
    int CopiedSourceAnswers = 0,
    string NarrationVersion = "V3",
    QuestionDrivenNarrationDiagnosticsDto? Diagnostics = null);

public sealed record QuestionDrivenNarrationDiagnosticsDto(
    bool ColdOpenPresent,
    bool HookPresent,
    bool StoryLayerPresent,
    bool ViewingGuidePresent,
    bool EmotionalClosingPresent,
    string NarrationVersion,
    int EstimatedRetentionScore,
    bool SubtitleFilesGenerated = false,
    string ShortSrtPath = "",
    string LongSrtPath = "",
    string ScriptComposerVersion = "",
    string OpeningStyle = "",
    bool EventDateMentioned = false,
    bool EventNameMentioned = false,
    int DocumentaryScore = 0,
    int StorytellingScore = 0,
    bool SubtitleStyleApplied = false,
    int SubtitleFontSize = 0,
    int SubtitleMaxCharsPerLine = 42,
    int SubtitleMaxLines = 2,
    bool SubtitleCueSplitApplied = false,
    int SubtitleCueCountBeforeSplit = 0,
    int SubtitleCueCountAfterSplit = 0,
    bool DuplicateNarrationDetected = false,
    bool DuplicateNarrationFixed = false,
    bool DuplicateSrtTextDetected = false,
    bool DynamicNarrationGenerated = false,
    bool HardcodedTemplateUsed = false,
    IReadOnlyList<string>? SourceEventFactsUsed = null,
    IReadOnlyList<string>? ScenePurposeUsed = null,
    IReadOnlyDictionary<string, string>? ScenePurposeToNarrationSection = null,
    IReadOnlyDictionary<string, int>? NarrationSectionAppearanceCounts = null,
    IReadOnlyList<string>? V31NarrationKeysUsed = null,
    IReadOnlyList<string>? V31ScenePurposeLookupKeysUsed = null,
    IReadOnlyDictionary<string, string>? V31FormatScenePurposeToSceneId = null,
    IReadOnlyDictionary<string, string>? V31FormatSceneIdToScenePurpose = null,
    int AiRewriteAttemptCount = 0,
    bool FallbackStaticTextUsed = false,
    int DocumentaryVoiceScore = 0,
    int SpokenLanguageScore = 0,
    int ObservationGuidanceScore = 0,
    int ScientificAccuracyScore = 0,
    int EditorialFlowScore = 0,
    int TransitionQualityScore = 0,
    int ViewerRetentionScore = 0,
    int AstroPulseIdentityScore = 0,
    int OverallNarrationScore = 0,
    bool NarrationPostEditorApplied = false,
    bool InstructionLeakageDetected = false,
    bool PromptLeakageDetected = false,
    bool DuplicatedTransformationsDetected = false,
    IReadOnlyList<string>? NarrationPostEditorRewrittenScenes = null);

public sealed record QuestionDrivenNarrationReviewCheckDto(
    string Name,
    bool Passed,
    string Message);

public interface IQuestionDrivenNarrationGenerator
{
    Task<QuestionDrivenNarrationResponse> GenerateQuestionDrivenNarrationAsync(QuestionDrivenNarrationRequest request, CancellationToken cancellationToken);
}
