using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScenePresentationProfile
{
    LongForm,
    ShortForm
}

public sealed class VideoAssemblyGenerationRequest
{
    public string EventId { get; set; } = string.Empty;

    public string RegionId { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    public string Platform { get; set; } = "YouTubeShort";

    public string Phase { get; set; } = "Intelligence";

    public bool DryRun { get; set; } = true;

    public bool OverwriteExisting { get; set; }

    public bool AllowSyntheticSilentTts { get; set; }

    public bool BackgroundMusic { get; set; }

    public string MusicMood { get; set; } = "WonderCuriosity";

    public int MusicLevelPercent { get; set; } = 0;

    public bool DuckMusicUnderNarration { get; set; } = true;

    public ScenePresentationProfile? ScenePresentationProfile { get; set; }
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
    double ActualDurationSeconds = 0,
    string TtsProvider = "",
    bool IsSyntheticTts = false,
    bool IsSilentAudio = false,
    bool AudioValidationPassed = false,
    double AudioPeakDb = 0,
    double AudioRmsDb = 0,
    bool VideoAssemblyPlanGenerated = false,
    string VideoAssemblyPlanPath = "",
    bool ReadyForRender = false,
    int SegmentCount = 0,
    double TotalDurationSeconds = 0,
    ScenePresentationProfile ScenePresentationProfileUsed = ScenePresentationProfile.LongForm,
    string SceneImageSourceDirectory = "",
    bool RenderUsedShortScenes = false,
    int ShortFormSceneCount = 0,
    bool VideoRendered = false,
    string FinalVideoPath = "",
    double FinalVideoDurationSeconds = 0,
    string OutputResolution = "",
    bool AudioTrackPresent = false,
    bool BackgroundMusicApplied = false,
    bool RenderSucceeded = false,
    string VideoRenderValidationPath = "",
    int RenderPolishScore = 0,
    int VideoFinalReadinessScore = 0,
    bool RenderUsedLongScenes = false,
    bool SceneMappingValid = false,
    bool BackgroundMusicPlanned = false,
    int MusicLevelPercent = 0,
    bool BackgroundMusicRequested = false,
    string BackgroundMusicSourcePath = "",
    bool DuckMusicUnderNarration = false,
    int RequestedMusicLevelPercent = 0,
    int EffectiveMusicLevelPercent = 0,
    double MusicVolumeMultiplier = 0,
    string FfmpegAudioFilter = "",
    bool MusicMixApplied = false);

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
    DateTimeOffset GeneratedUtc,
    VideoTtsAudioValidationDto? AudioValidation = null);

public sealed record VideoTtsAudioValidationDto(
    bool IsSilentAudio,
    double AudioPeakDb,
    double AudioRmsDb,
    bool AudioValidationPassed = true);

public sealed record VideoTtsSceneTimingDto(
    string SceneKey,
    double StartSeconds,
    double EndSeconds,
    string Narration);


public sealed record VideoAssemblyPlanDto(
    string EventId,
    string RegionId,
    string Language,
    string Platform,
    ScenePresentationProfile ScenePresentationProfile,
    string SceneImageBaseDirectory,
    int SceneCount,
    IReadOnlyList<string> SceneImages,
    double TotalDurationSeconds,
    string AudioFilePath,
    string RenderOutputPath,
    IReadOnlyList<VideoAssemblyPlanSegmentDto> Segments,
    VideoAssemblyRenderSettingsDto RenderSettings,
    bool BackgroundMusic,
    VideoAssemblyStyleDto Style,
    VideoAssemblyValidationDto Validation,
    VideoAssemblySceneMappingValidationDto SceneMappingValidation,
    VideoAssemblyRenderMusicPlanDto RenderMusicPlan,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedUtc);

public sealed record VideoAssemblyPlanSegmentDto(
    string SceneKey,
    double StartSeconds,
    double EndSeconds,
    double DurationSeconds,
    string VisualAssetPath,
    string Narration,
    string TransitionIn,
    string TransitionOut,
    string Motion);

public sealed record VideoAssemblyRenderSettingsDto(
    int Width,
    int Height,
    int Fps,
    string Format,
    string Codec,
    string AudioCodec);

public sealed record VideoAssemblyStyleDto(
    string TransitionStyle,
    string MotionStyle,
    string TextOverlayStyle,
    bool BackgroundMusic);

public sealed record VideoAssemblyValidationDto(
    bool AudioExists,
    bool AllVisualAssetsExist,
    int SegmentCount,
    bool DurationMatchesAudio,
    bool ReadyForRender);

public sealed record VideoAssemblySceneMappingValidationDto(
    bool HookUsesScene001,
    bool WhatUsesScene001,
    bool WhyUsesScene005,
    bool WhereUsesScene002,
    bool WhenUsesScene003,
    bool ActionUsesScene006,
    bool SceneMappingValid);

public sealed record VideoAssemblyRenderMusicPlanDto(
    bool BackgroundMusic,
    string MusicMood,
    int MusicLevelPercent,
    bool DuckMusicUnderNarration);

public sealed record VideoRenderValidationDto(
    ScenePresentationProfile ScenePresentationProfileUsed,
    string SceneImageSourceDirectory,
    bool RenderUsedShortScenes,
    bool RenderUsedLongScenes,
    int ShortFormSceneCount,
    string VideoResolution,
    bool TtsAudioPresent,
    bool BackgroundMusicPresent,
    bool RenderValidationPassed,
    bool KenBurnsApplied,
    bool CrossFadeApplied,
    bool HookOptimizationApplied,
    bool MusicMixValidated,
    int RenderPolishScore,
    int VideoFinalReadinessScore,
    string OutputFileName = "",
    bool BackgroundMusicRequested = false,
    bool BackgroundMusicSourceFound = false,
    bool BackgroundMusicMixed = false,
    string MusicMood = "WonderCuriosity",
    int MusicLevelPercent = 0,
    bool DuckMusicUnderNarration = false,
    bool AudioTrackPresent = false,
    bool FinalAudioContainsNarration = false,
    bool FinalAudioContainsMusic = false,
    bool RenderSucceeded = false,
    string BackgroundMusicSourcePath = "",
    int RequestedMusicLevelPercent = 0,
    int EffectiveMusicLevelPercent = 0,
    double MusicVolumeMultiplier = 0,
    string FfmpegAudioFilter = "",
    bool MusicMixApplied = false,
    IReadOnlyList<string>? Warnings = null);
