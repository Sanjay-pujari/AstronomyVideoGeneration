namespace Astronomy.MediaFactory.Core;

public sealed class VideoAssemblyGenerationRequest
{
    public string EventId { get; set; } = string.Empty;

    public string RegionId { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    public string Platform { get; set; } = "YouTubeShort";

    public string Phase { get; set; } = "Intelligence";

    public bool DryRun { get; set; } = true;

    public bool OverwriteExisting { get; set; }
}

public sealed record VideoAssemblyGenerationResponse(
    string PhaseRequested,
    string PhaseExecuted,
    bool VideoAssemblyIntelligenceGenerated,
    string VideoAssemblyIntelligencePath,
    string SelectedOpeningHook,
    double RecommendedTotalDurationSeconds,
    bool TtsRequired,
    bool FinalVideoPlanned,
    IReadOnlyList<string> GeneratedFiles,
    bool VideoNarrationScriptGenerated = false,
    string VideoNarrationScriptPath = "",
    double TotalEstimatedDurationSeconds = 0,
    bool TtsReady = false,
    bool TtsAudioGenerated = false,
    bool TtsTimingsGenerated = false,
    string AudioFilePath = "",
    string TimingsFilePath = "",
    double ActualDurationSeconds = 0);

public sealed record VideoAssemblyIntelligenceDto(
    string EventId,
    string RegionId,
    string Language,
    string Platform,
    string SelectedOpeningHook,
    string VideoIntent,
    string EmotionalArc,
    IReadOnlyList<string> RecommendedSceneOrder,
    IReadOnlyList<VideoAssemblySceneDurationDto> RecommendedSceneDurations,
    double RecommendedTotalDurationSeconds,
    VideoAssemblyNarrationStyleDto NarrationStyle,
    VideoAssemblyVisualStyleDto VisualStyle,
    VideoAssemblyAudioPlanDto AudioPlan,
    IReadOnlyList<string> OutputsPlanned,
    VideoAssemblyScoresDto Scores,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedUtc);

public sealed record VideoAssemblySceneDurationDto(
    string SceneKey,
    double DurationSeconds,
    string Purpose);

public sealed record VideoAssemblyNarrationStyleDto(
    string Tone,
    string Pace,
    string VoiceType);

public sealed record VideoAssemblyVisualStyleDto(
    bool UseSceneImages,
    bool UseHeroAssetAsOpening,
    bool UseThumbnailOnlyForPublishing,
    string TransitionStyle,
    string TextOverlayStyle);

public sealed record VideoAssemblyAudioPlanDto(
    bool TtsRequired,
    bool BackgroundMusicRecommended,
    string MusicMood,
    bool DuckMusicUnderNarration);

public sealed record VideoAssemblyScoresDto(
    int HookStrengthScore,
    int SceneFlowScore,
    int ShortFormReadinessScore,
    int VideoAssemblyReadinessScore);

public interface IVideoAssemblyIntelligenceService
{
    Task<VideoAssemblyGenerationResponse> GenerateVideoAssemblyAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken);
}


public sealed record VideoNarrationScriptDto(
    string EventId,
    string RegionId,
    string Language,
    string Platform,
    double TotalEstimatedDurationSeconds,
    VideoNarrationScriptStyleDto ScriptStyle,
    IReadOnlyList<VideoNarrationSceneScriptDto> SceneScripts,
    string FullNarrationText,
    VideoNarrationTtsPlanDto TtsPlan,
    VideoNarrationScriptScoresDto Scores,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedUtc);

public sealed record VideoNarrationScriptStyleDto(
    string Tone,
    string Pace,
    string VoiceType);

public sealed record VideoNarrationSceneScriptDto(
    string SceneKey,
    double DurationSeconds,
    string Narration,
    string OnScreenText);

public sealed record VideoNarrationTtsPlanDto(
    bool TtsRequired,
    string RecommendedVoice,
    string OutputFileName);

public sealed record VideoNarrationScriptScoresDto(
    int ClarityScore,
    int ShortFormPaceScore,
    int TtsReadinessScore);


public sealed record VideoTtsTimingsDto(
    string EventId,
    string RegionId,
    string Language,
    string Platform,
    string AudioFilePath,
    double EstimatedDurationSeconds,
    double ActualDurationSeconds,
    IReadOnlyList<VideoTtsSceneTimingDto> SceneTimings,
    string TtsProvider,
    string VoiceUsed,
    DateTimeOffset GeneratedUtc);

public sealed record VideoTtsSceneTimingDto(
    string SceneKey,
    double StartSeconds,
    double EndSeconds,
    string Narration);
