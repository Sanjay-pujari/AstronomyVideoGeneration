using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed partial class VideoAssemblyIntelligenceService(
    IOptions<RenderingOptions> renderingOptions,
    IOptions<VideoAssemblyOptions>? videoAssemblyOptions = null,
    IOptions<AzureSpeechOptions>? azureSpeechOptions = null,
    IAzureSpeechClient? azureSpeechClient = null) : IVideoAssemblyIntelligenceService
{
    private const string HeroAssetsDirectoryName = "hero-assets";
    private const string ThumbnailAssetsDirectoryName = "thumbnail-assets";
    private const string QuestionEngineDirectoryName = "question-engine";
    private const string SceneApprovalDirectoryName = "scene-approval-v3";
    private const string VideoAssemblyDirectoryName = "video-assembly";
    private const string HeroStoryFileName = "hero-story.json";
    private const string HeroAssetStoryFileName = "hero-asset-story.json";
    private const string HeroSceneManifestFileName = "hero-scene-manifest.json";
    private const string HeroCompositionModelFileName = "hero-composition-model.json";
    private const string ThumbnailIntelligenceFileName = "thumbnail-intelligence.json";
    private const string ThumbnailCompositionModelFileName = "thumbnail-composition-model.json";
    private const string ThumbnailSceneManifestFileName = "thumbnail-scene-manifest.json";
    private const string VideoAssemblyIntelligenceFileName = "video-assembly-intelligence.json";
    private const string VideoNarrationScriptFileName = "video-narration-script.json";
    private const string VideoTtsAudioFileName = "video-tts-audio.mp3";
    private const string VideoTtsTimingsFileName = "video-tts-timings.json";
    private const string VideoAssemblyPlanFileName = "video-assembly-plan.json";
    private const string LongVideoAssemblyIntelligenceFileName = "video-assembly-long-intelligence.json";
    private const string LongVideoNarrationScriptFileName = "video-long-narration-script.json";
    private const string LongVideoTtsAudioFileName = "video-long-tts-audio.mp3";
    private const string LongVideoTtsTimingsFileName = "video-long-tts-timings.json";
    private const string LongVideoAssemblyPlanFileName = "video-long-assembly-plan.json";
    private const string LongVideoRenderValidationFileName = "video-long-render-validation.json";
    private const string ThumbnailLandscapeFileName = "thumbnail-landscape.png";
    private const string FinalVideoShortFileName = "final-video-short.mp4";
    private const string FinalVideoLongFileName = "final-video-long.mp4";
    private const string VideoRenderValidationFileName = "video-render-validation.json";
    private const double RenderDurationToleranceSeconds = 0.5;
    private const double ShortFormCrossFadeDurationSeconds = 0.4;
    private const double LongFormCrossFadeDurationSeconds = 0.6;
    private const double HookOptimizationDurationSeconds = 3.218;
    private const double DefaultLongFormNarrationWordsPerMinute = 135.0;
    private const string SelectedOpeningHook = "TONIGHT'S SKY EVENT";
    private const string SyntheticTtsProviderName = "SyntheticOfflineTtsV1";
    private const string AzureTtsProviderName = "AzureSpeechTts";
    private const string OpenAiTtsProviderName = "OpenAITts";
    private const long MinimumMp3FileSizeBytes = 1024;
    private const double SilencePeakThresholdDb = -55.0;
    private const double SilenceRmsThresholdDb = -60.0;
    private static readonly string[] RequiredApprovedSceneIds = ["scene-001", "scene-002", "scene-003", "scene-004", "scene-005", "scene-006"];
    private static readonly string[] RequiredAssemblySceneOrder = ["Hook", "What", "Why", "Where", "When", "Action"];
    private static readonly string[] NarrationAuthoringInstructionPhrases = ["Open with", "Explain", "Describe", "Focus on", "Call out", "Add a distinct", "Give safe", "Close with", "Viewer-friendly terms", "Timing window", "Primary sky objects", "Event experience", "Sky geometry"];
    private static readonly string[] LongFormSectionOrder = ["Hook", "WhatIsHappening", "WhyItMatters", "WhereToLook", "WhenToLook", "HowToObserve", "WhatYouWillSee", "InterestingFact", "ObservationTips", "Recap", "Action"];
    private static readonly IReadOnlyDictionary<string, string> AssemblySceneVisualMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Hook"] = "scene-001-final.png",
        ["What"] = "scene-001-final.png",
        ["Why"] = "scene-005-final.png",
        ["Where"] = "scene-002-final.png",
        ["When"] = "scene-003-final.png",
        ["Action"] = "scene-006-final.png"
    };
    private static readonly IReadOnlyDictionary<string, string> LongFormAssemblySceneVisualMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Hook"] = "scene-001-final.png",
        ["WhatIsHappening"] = "scene-001-final.png",
        ["WhyItMatters"] = "scene-005-final.png",
        ["WhereToLook"] = "scene-002-final.png",
        ["WhenToLook"] = "scene-003-final.png",
        ["HowToObserve"] = "scene-004-final.png",
        ["WhatYouWillSee"] = "scene-001-final.png",
        ["InterestingFact"] = "scene-005-final.png",
        ["ObservationTips"] = "scene-004-final.png",
        ["Recap"] = "scene-003-final.png",
        ["Action"] = "scene-006-final.png"
    };
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private ProductionPipelineExecutionContext? _activeProductionContext;


    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static ScenePresentationProfile ResolveScenePresentationProfile(string platform)
        => platform switch
        {
            var value when string.Equals(value, "YouTubeShort", StringComparison.OrdinalIgnoreCase) => ScenePresentationProfile.ShortForm,
            var value when string.Equals(value, "InstagramReel", StringComparison.OrdinalIgnoreCase) => ScenePresentationProfile.ShortForm,
            var value when string.Equals(value, "FacebookReel", StringComparison.OrdinalIgnoreCase) => ScenePresentationProfile.ShortForm,
            var value when string.Equals(value, "YouTubeLong", StringComparison.OrdinalIgnoreCase) => ScenePresentationProfile.LongForm,
            _ => ScenePresentationProfile.LongForm
        };

    private static string ResolveSceneApprovalProfileDirectoryName(ScenePresentationProfile presentationProfile)
        => presentationProfile == ScenePresentationProfile.ShortForm ? "short" : "long";

    private VideoDurationProfileOptions ResolveDurationProfile(ScenePresentationProfile profile)
        => NormalizeDurationProfile(profile == ScenePresentationProfile.ShortForm
            ? videoAssemblyOptions?.Value.ShortVideo
            : videoAssemblyOptions?.Value.LongVideo, profile);

    private static VideoDurationProfileOptions NormalizeDurationProfile(VideoDurationProfileOptions? configured, ScenePresentationProfile profile)
    {
        var fallback = profile == ScenePresentationProfile.ShortForm
            ? VideoDurationProfileOptions.ShortVideoDefaults()
            : VideoDurationProfileOptions.LongVideoDefaults();
        var value = configured ?? fallback;
        return new VideoDurationProfileOptions
        {
            TargetDurationSecondsMin = value.TargetDurationSecondsMin > 0 ? value.TargetDurationSecondsMin : fallback.TargetDurationSecondsMin,
            TargetDurationSecondsMax = value.TargetDurationSecondsMax > 0 ? value.TargetDurationSecondsMax : fallback.TargetDurationSecondsMax,
            AcceptableDurationSecondsMin = value.AcceptableDurationSecondsMin > 0 ? value.AcceptableDurationSecondsMin : fallback.AcceptableDurationSecondsMin,
            AcceptableDurationSecondsMax = value.AcceptableDurationSecondsMax > 0 ? value.AcceptableDurationSecondsMax : fallback.AcceptableDurationSecondsMax
        };
    }

    private static string ResolveDurationProfileName(ScenePresentationProfile profile)
        => profile == ScenePresentationProfile.ShortForm ? "ShortVideo" : "LongVideo";

    private static string ResolvePlannedFormat(ScenePresentationProfile profile)
        => profile == ScenePresentationProfile.ShortForm ? "ShortVideo" : "LongVideo";

    private double ResolveDurationComparisonToleranceSeconds()
    {
        var configured = videoAssemblyOptions?.Value.DurationComparisonToleranceSeconds ?? VideoAssemblyOptions.DefaultDurationComparisonToleranceSeconds;
        return configured >= 0 ? configured : VideoAssemblyOptions.DefaultDurationComparisonToleranceSeconds;
    }

    private static bool IsWithinDurationRange(double actualDurationSeconds, double minSeconds, double maxSeconds, double toleranceSeconds)
        => actualDurationSeconds >= minSeconds - toleranceSeconds && actualDurationSeconds <= maxSeconds + toleranceSeconds;

    private VideoDurationContractValidationDto BuildDurationValidation(ScenePresentationProfile profile, double actualDurationSeconds, bool useTargetRange, string valueLabel)
    {
        _ = useTargetRange;
        var contract = ResolveDurationProfile(profile);
        var toleranceSeconds = ResolveDurationComparisonToleranceSeconds();
        var passed = IsWithinDurationRange(actualDurationSeconds, contract.AcceptableDurationSecondsMin, contract.AcceptableDurationSecondsMax, toleranceSeconds);
        var withinTargetRange = IsWithinDurationRange(actualDurationSeconds, contract.TargetDurationSecondsMin, contract.TargetDurationSecondsMax, toleranceSeconds);
        var warnings = passed && !withinTargetRange
            ? new[] { "Duration outside target range but inside acceptable range." }
            : Array.Empty<string>();
        var rounded = Math.Round(actualDurationSeconds, 3, MidpointRounding.AwayFromZero);
        var reason = passed
            ? $"{valueLabel} is within the acceptable duration range."
            : $"{valueLabel} must be {contract.AcceptableDurationSecondsMin:0.###}-{contract.AcceptableDurationSecondsMax:0.###} seconds for {ResolveDurationProfileName(profile)}.";
        return new VideoDurationContractValidationDto(
            ResolvePlannedFormat(profile),
            ResolveDurationProfileName(profile),
            new VideoDurationRangeDto(contract.TargetDurationSecondsMin, contract.TargetDurationSecondsMax),
            new VideoDurationRangeDto(contract.AcceptableDurationSecondsMin, contract.AcceptableDurationSecondsMax),
            rounded,
            passed,
            reason,
            warnings);
    }

    private void EnsureDurationValidationPassed(VideoDurationContractValidationDto report, string messagePrefix)
    {
        var passedAcceptableRange = IsWithinDurationRange(report.ActualDurationSeconds, report.AcceptableDurationRange.MinSeconds, report.AcceptableDurationRange.MaxSeconds, ResolveDurationComparisonToleranceSeconds());
        if (!passedAcceptableRange)
            throw new ArgumentException($"{messagePrefix}: {report.Reason} ActualDurationSeconds={report.ActualDurationSeconds:0.###}; plannedFormat={report.PlannedFormat}; profileName={report.ProfileName}; targetDurationRange={report.TargetDurationRange.MinSeconds:0.###}-{report.TargetDurationRange.MaxSeconds:0.###}; acceptableDurationRange={report.AcceptableDurationRange.MinSeconds:0.###}-{report.AcceptableDurationRange.MaxSeconds:0.###}.");
    }

    private static ScenePresentationProfile ResolveRequestProfile(VideoAssemblyGenerationRequest request)
        => request.ScenePresentationProfile ?? ResolveScenePresentationProfile(request.Platform);

    private static bool IsLongFormRequest(VideoAssemblyGenerationRequest request)
        => ResolveRequestProfile(request) == ScenePresentationProfile.LongForm;

    private static bool IsRequestedPhase(string phase, string expected)
        => string.Equals(phase, expected, StringComparison.OrdinalIgnoreCase)
            || string.Equals(phase, $"LongForm{expected}", StringComparison.OrdinalIgnoreCase);

    private static VideoAssemblyGenerationRequest NormalizePhaseRequest(VideoAssemblyGenerationRequest request)
    {
        if (!request.Phase.StartsWith("LongForm", StringComparison.OrdinalIgnoreCase))
            return request;

        return CloneRequest(request, request.Phase, ScenePresentationProfile.LongForm, request.LongForm);
    }

    private static VideoAssemblyGenerationRequest BuildFormRequest(VideoAssemblyGenerationRequest request, ScenePresentationProfile profile, string phase)
        => CloneRequest(request, phase, profile, profile == ScenePresentationProfile.ShortForm ? request.ShortForm : request.LongForm);

    private static VideoAssemblyGenerationRequest CloneRequest(VideoAssemblyGenerationRequest request, string phase, ScenePresentationProfile profile, VideoAssemblyFormRequest? form)
        => new()
        {
            EventId = request.EventId,
            RegionId = request.RegionId,
            Language = request.Language,
            Platform = string.IsNullOrWhiteSpace(form?.Platform) ? (profile == ScenePresentationProfile.ShortForm ? "YouTubeShort" : "YouTubeLong") : form!.Platform,
            Phase = phase,
            OutputMode = request.OutputMode,
            DryRun = request.DryRun,
            OverwriteExisting = request.OverwriteExisting,
            AllowSyntheticSilentTts = request.AllowSyntheticSilentTts,
            BackgroundMusic = form?.BackgroundMusic ?? request.BackgroundMusic,
            MusicMood = string.IsNullOrWhiteSpace(form?.MusicMood) ? request.MusicMood : form!.MusicMood,
            MusicLevelPercent = (form?.MusicLevelPercent ?? 0) > 0 ? form!.MusicLevelPercent : request.MusicLevelPercent,
            DuckMusicUnderNarration = form?.DuckMusicUnderNarration ?? request.DuckMusicUnderNarration,
            ScenePresentationProfile = form?.ScenePresentationProfile ?? profile,
            ShortForm = request.ShortForm,
            LongForm = request.LongForm,
            ProductionContext = request.ProductionContext,
            SourceNotes = request.SourceNotes ?? Array.Empty<string>()
        };

    private static bool ShouldRunShortForm(VideoAssemblyGenerationRequest request)
    {
        var modeAllows = string.Equals(request.OutputMode, "Both", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.OutputMode, "ShortFormOnly", StringComparison.OrdinalIgnoreCase);
        return modeAllows && (request.ShortForm is null || request.ShortForm.Enabled);
    }

    private static bool ShouldRunLongForm(VideoAssemblyGenerationRequest request)
    {
        var modeAllows = string.Equals(request.OutputMode, "Both", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.OutputMode, "LongFormOnly", StringComparison.OrdinalIgnoreCase);
        return modeAllows && (request.LongForm is null || request.LongForm.Enabled);
    }

    private static void AddIfNotEmpty(ICollection<string> paths, string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            paths.Add(NormalizePath(path));
    }

    public async Task<VideoAssemblyGenerationResponse> GenerateVideoAssemblyAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _activeProductionContext = request.ProductionContext;
        ValidateRequest(request);

        if (string.Equals(request.Phase, "FullPipeline", StringComparison.OrdinalIgnoreCase))
            return await GenerateFullPipelineAsync(request, cancellationToken);

        if (string.Equals(request.Phase, "LongFormTts", StringComparison.OrdinalIgnoreCase))
            return await GenerateLongFormTtsAudioAsync(CloneRequest(request, "LongFormTts", ScenePresentationProfile.LongForm, request.LongForm), cancellationToken);

        var normalizedRequest = NormalizePhaseRequest(request);

        if (IsRequestedPhase(normalizedRequest.Phase, "Script"))
            return await GenerateVideoNarrationScriptAsync(normalizedRequest, cancellationToken);

        if (IsRequestedPhase(normalizedRequest.Phase, "Tts"))
            return await GenerateTtsAudioAsync(normalizedRequest, cancellationToken);

        if (IsRequestedPhase(normalizedRequest.Phase, "Assembly"))
            return await GenerateVideoAssemblyPlanAsync(normalizedRequest, cancellationToken);

        if (IsRequestedPhase(normalizedRequest.Phase, "Render"))
            return await GenerateVideoRenderAsync(normalizedRequest, cancellationToken);

        if (!IsRequestedPhase(normalizedRequest.Phase, "Intelligence"))
            throw new ArgumentException("Only video assembly phases 'Intelligence', 'Script', 'Tts', 'Assembly', 'Render', 'LongFormIntelligence', 'LongFormScript', 'LongFormTts', 'LongFormAssembly', 'LongFormRender', and 'FullPipeline' are implemented in this endpoint version.", nameof(request));

        var outputPath = BuildVideoAssemblyIntelligenceOutputPath(normalizedRequest.EventId, normalizedRequest.RegionId, ResolveRequestProfile(normalizedRequest));
        if (!normalizedRequest.DryRun && !normalizedRequest.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<VideoAssemblyIntelligenceDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing video assembly intelligence could not be parsed.");
            ValidateVideoAssemblyIntelligence(existing);
            return BuildResponse(request.Phase, existing, outputPath);
        }

        await EnsureRequiredInputsAsync(normalizedRequest.EventId, normalizedRequest.RegionId, normalizedRequest.Platform, cancellationToken);
        var intelligence = BuildVideoAssemblyIntelligence(normalizedRequest);
        ValidateVideoAssemblyIntelligence(intelligence);

        if (!normalizedRequest.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(intelligence, JsonOptions), cancellationToken);
        }

        return BuildResponse(request.Phase, intelligence, outputPath);
    }

    private async Task<VideoAssemblyGenerationResponse> GenerateFullPipelineAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        var generatedFiles = new List<string>();
        var shortEnabled = ShouldRunShortForm(request);
        var longEnabled = ShouldRunLongForm(request);
        var shortResult = shortEnabled
            ? await RunFullPipelineFormAsync(request, ScenePresentationProfile.ShortForm, cancellationToken)
            : new VideoAssemblyFullPipelineFormResult(false, "Skipped", string.Empty, 0, ScenePresentationProfile.ShortForm, GeneratedFiles: Array.Empty<string>());
        generatedFiles.AddRange(shortResult.GeneratedFiles ?? Array.Empty<string>());

        var longResult = longEnabled
            ? await RunFullPipelineFormAsync(request, ScenePresentationProfile.LongForm, cancellationToken)
            : new VideoAssemblyFullPipelineFormResult(false, "Skipped", string.Empty, 0, ScenePresentationProfile.LongForm, GeneratedFiles: Array.Empty<string>());
        generatedFiles.AddRange(longResult.GeneratedFiles ?? Array.Empty<string>());

        return new VideoAssemblyGenerationResponse(
            request.Phase,
            "FullPipeline",
            false,
            string.Empty,
            string.Empty,
            0,
            true,
            shortResult.Status == "Succeeded" || longResult.Status == "Succeeded",
            generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            OutputMode: request.OutputMode,
            ShortForm: shortResult,
            LongForm: longResult);
    }

    private async Task<VideoAssemblyFullPipelineFormResult> RunFullPipelineFormAsync(VideoAssemblyGenerationRequest rootRequest, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        var generatedFiles = new List<string>();
        var phaseName = string.Empty;
        try
        {
            foreach (var phase in new[] { "Intelligence", "Script", "Tts", "Assembly", "Render" })
            {
                phaseName = profile == ScenePresentationProfile.LongForm ? $"LongForm{phase}" : phase;
                var response = await GenerateVideoAssemblyAsync(BuildFormRequest(rootRequest, profile, phaseName), cancellationToken);
                generatedFiles.AddRange(response.GeneratedFiles);
                AddIfNotEmpty(generatedFiles, response.VideoAssemblyIntelligencePath);
                AddIfNotEmpty(generatedFiles, response.VideoNarrationScriptPath);
                AddIfNotEmpty(generatedFiles, response.AudioFilePath);
                AddIfNotEmpty(generatedFiles, response.TimingsFilePath);
                AddIfNotEmpty(generatedFiles, response.VideoAssemblyPlanPath);
                AddIfNotEmpty(generatedFiles, response.VideoRenderValidationPath);
                AddIfNotEmpty(generatedFiles, response.FinalVideoPath);
                if (string.Equals(phase, "Render", StringComparison.OrdinalIgnoreCase))
                    return new VideoAssemblyFullPipelineFormResult(true, "Succeeded", response.FinalVideoPath, response.FinalVideoDurationSeconds, response.ScenePresentationProfileUsed, GeneratedFiles: generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            }

            return new VideoAssemblyFullPipelineFormResult(true, "Failed", string.Empty, 0, profile, phaseName, "Pipeline did not reach Render.", generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            return new VideoAssemblyFullPipelineFormResult(true, "Failed", string.Empty, 0, profile, phaseName, ex.Message, generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }


    private async Task<VideoAssemblyGenerationResponse> GenerateVideoNarrationScriptAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        var outputPath = BuildVideoNarrationScriptOutputPath(request.EventId, request.RegionId, ResolveRequestProfile(request));
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<VideoNarrationScriptDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing video narration script could not be parsed.");
            ValidateVideoNarrationScript(existing);
            return BuildScriptResponse(request.Phase, existing, outputPath);
        }

        var intelligence = await EnsureRequiredScriptInputsAsync(request.EventId, request.RegionId, ResolveRequestProfile(request), cancellationToken);
        var script = BuildVideoNarrationScript(request, intelligence);
        ValidateVideoNarrationScript(script);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(script, JsonOptions), cancellationToken);
        }

        return BuildScriptResponse(request.Phase, script, outputPath);
    }


    private async Task<VideoAssemblyGenerationResponse> GenerateTtsAudioAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        if (IsLongFormRequest(request))
            return await GenerateLongFormTtsAudioAsync(CloneRequest(request, "LongFormTts", ScenePresentationProfile.LongForm, request.LongForm), cancellationToken);

        var profile = ResolveRequestProfile(request);
        var audioPath = BuildVideoTtsAudioOutputPath(request.EventId, request.RegionId, profile);
        var timingsPath = BuildVideoTtsTimingsOutputPath(request.EventId, request.RegionId, profile);

        var syntheticTtsAllowed = request.DryRun || request.AllowSyntheticSilentTts;

        if (!request.DryRun && !request.OverwriteExisting && File.Exists(audioPath) && File.Exists(timingsPath))
        {
            var existing = JsonSerializer.Deserialize<VideoTtsTimingsDto>(await File.ReadAllTextAsync(timingsPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing video TTS timings could not be parsed.");
            ValidateVideoTtsTimings(existing);
            if (IsSyntheticProvider(existing.TtsProvider) && !syntheticTtsAllowed)
                throw new InvalidOperationException("Real TTS provider is not configured. SyntheticOfflineTtsV1 is disabled for dryRun=false.");

            var existingValidation = await ValidateMp3AudioAsync(audioPath, enforceNonSilent: true, cancellationToken);
            if (!existingValidation.AudioValidationPassed)
                throw new InvalidOperationException("Generated TTS audio validation failed: audio is silent or invalid.");

            return BuildTtsResponse(request.Phase, audioPath, timingsPath, existing.ActualDurationSeconds, [], existing.TtsProvider, IsSyntheticProvider(existing.TtsProvider), existingValidation);
        }

        var script = await EnsureRequiredTtsInputsAsync(request.EventId, request.RegionId, profile, cancellationToken);
        var narrationText = await ReadRequiredNarrationTextAsync(request, profile, script, cancellationToken);
        script = script with { FullNarrationText = narrationText };
        var provider = ResolveTtsProvider(request, script);
        var actualDurationSeconds = NormalizeTtsDuration(profile, script.TotalEstimatedDurationSeconds);
        var narrationWordCount = CountSpokenWords(narrationText);

        var generatedFiles = new List<string>();
        VideoTtsAudioValidationDto audioValidation = new(false, 0, 0, request.DryRun);
        var tempAudioPath = string.Empty;
        var debugPath = BuildTtsDebugOutputPath(request.EventId, request.RegionId, profile);
        if (!request.DryRun)
        {
            var tempRoot = Path.Combine(Path.GetDirectoryName(audioPath) ?? ResolveWorkingDirectoryRoot(), ".tts-temp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            tempAudioPath = Path.Combine(tempRoot, Path.GetFileName(audioPath));

            try
            {
                await WriteTtsAudioAsync(narrationText, tempAudioPath, actualDurationSeconds, provider, cancellationToken);
                audioValidation = await ValidateMp3AudioAsync(tempAudioPath, enforceNonSilent: true, cancellationToken);
                if (audioValidation.AudioDurationSeconds > 0)
                    actualDurationSeconds = audioValidation.AudioDurationSeconds;

                var timings = BuildVideoTtsTimings(request, script, audioPath, actualDurationSeconds, provider.ProviderName, provider.VoiceUsed, audioValidation);
                try
                {
                    ValidateVideoTtsTimings(timings);
                }
                catch (ArgumentException ex)
                {
                    await WriteTtsDebugAsync(debugPath, tempAudioPath, audioPath, timingsPath, actualDurationSeconds, narrationWordCount, audioValidation, timings.DurationValidation, ex.Message, cancellationToken);
                    throw new InvalidOperationException(BuildTtsDiagnosticFailureMessage(ex.Message, actualDurationSeconds, narrationWordCount, audioValidation, tempAudioPath), ex);
                }

                if (!audioValidation.AudioValidationPassed)
                {
                    const string message = "Generated TTS audio validation failed: audio is silent or invalid.";
                    await WriteTtsDebugAsync(debugPath, tempAudioPath, audioPath, timingsPath, actualDurationSeconds, narrationWordCount, audioValidation, BuildDurationValidation(profile, actualDurationSeconds, useTargetRange: false, "actualDurationSeconds"), message, cancellationToken);
                    throw new InvalidOperationException(BuildTtsDiagnosticFailureMessage(message, actualDurationSeconds, narrationWordCount, audioValidation, tempAudioPath));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(audioPath) ?? ResolveWorkingDirectoryRoot());
                File.Copy(tempAudioPath, audioPath, overwrite: true);
                await File.WriteAllTextAsync(timingsPath, JsonSerializer.Serialize(timings, JsonOptions), cancellationToken);
                generatedFiles.Add(NormalizePath(audioPath));
                generatedFiles.Add(NormalizePath(timingsPath));
                generatedFiles.AddRange(await GenerateSubtitlesAsync(timings, ResolveRequestProfile(request), cancellationToken));

                TryDeleteDirectory(tempRoot);
                return BuildTtsResponse(request.Phase, audioPath, timingsPath, timings.ActualDurationSeconds, generatedFiles, provider.ProviderName, provider.IsSynthetic, audioValidation);
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(tempAudioPath) && !File.Exists(debugPath))
                    await WriteTtsDebugAsync(debugPath, tempAudioPath, audioPath, timingsPath, actualDurationSeconds, narrationWordCount, audioValidation, BuildDurationValidation(profile, actualDurationSeconds, useTargetRange: false, "actualDurationSeconds"), "TTS generation failed before final outputs were written.", cancellationToken);
                throw;
            }
        }

        var dryRunTimings = BuildVideoTtsTimings(request, script, audioPath, actualDurationSeconds, provider.ProviderName, provider.VoiceUsed, audioValidation);
        ValidateVideoTtsTimings(dryRunTimings);
        return BuildTtsResponse(request.Phase, audioPath, timingsPath, dryRunTimings.ActualDurationSeconds, generatedFiles, provider.ProviderName, provider.IsSynthetic, audioValidation);
    }


    private async Task<VideoAssemblyGenerationResponse> GenerateLongFormTtsAudioAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        var audioPath = BuildVideoTtsAudioOutputPath(request.EventId, request.RegionId, ScenePresentationProfile.LongForm);
        var timingsPath = BuildVideoTtsTimingsOutputPath(request.EventId, request.RegionId, ScenePresentationProfile.LongForm);

        if (!request.OverwriteExisting && File.Exists(audioPath) && File.Exists(timingsPath))
        {
            var existing = await ReadLongFormVideoTtsTimingsAsync(timingsPath, cancellationToken);
            ValidateLongFormVideoTtsTimings(existing);
            var existingValidation = await ValidateMp3AudioAsync(audioPath, enforceNonSilent: true, cancellationToken);
            if (!existingValidation.AudioValidationPassed)
                throw new InvalidOperationException("Generated long-form TTS audio validation failed: audio is silent or invalid.");

            return BuildLongFormTtsResponse(request.Phase, audioPath, timingsPath, existing.ActualDurationSeconds, [], AzureTtsProviderName, existingValidation);
        }

        EnsureLongFormAzureTtsAvailable();
        var script = await EnsureRequiredTtsInputsAsync(request.EventId, request.RegionId, ScenePresentationProfile.LongForm, cancellationToken);
        var narrationText = await ReadRequiredNarrationTextAsync(request, ScenePresentationProfile.LongForm, script, cancellationToken);
        script = script with { FullNarrationText = narrationText };
        var voiceUsed = ResolveNeutralEducationalAzureVoice(script);

        Directory.CreateDirectory(Path.GetDirectoryName(audioPath) ?? ResolveWorkingDirectoryRoot());
        await WriteAzureLongFormTtsAudioAsync(narrationText, audioPath, cancellationToken);

        var audioValidation = await ValidateMp3AudioAsync(audioPath, enforceNonSilent: true, cancellationToken);
        if (!audioValidation.AudioValidationPassed)
            throw new InvalidOperationException("Generated long-form TTS audio validation failed: audio is silent or invalid.");

        var actualDurationSeconds = Math.Round(await ProbeDurationSecondsAsync(audioPath, cancellationToken), 3, MidpointRounding.AwayFromZero);
        var durationValidation = BuildDurationValidation(ScenePresentationProfile.LongForm, actualDurationSeconds, useTargetRange: false, "actualDurationSeconds");
        try
        {
            EnsureDurationValidationPassed(durationValidation, "Generated long-form TTS audio duration validation failed");
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        var sectionTimings = await BuildActualLongFormSectionTimingsAsync(script, actualDurationSeconds, cancellationToken);
        var timings = new LongFormVideoTtsTimingsDto(
            request.EventId,
            request.RegionId,
            request.Language,
            "YouTubeLong",
            NormalizePath(audioPath),
            script.TotalEstimatedDurationSeconds,
            actualDurationSeconds,
            sectionTimings,
            AzureTtsProviderName,
            voiceUsed,
            audioValidation,
            DateTimeOffset.UtcNow,
            durationValidation);
        ValidateLongFormVideoTtsTimings(timings);

        await File.WriteAllTextAsync(timingsPath, JsonSerializer.Serialize(timings, JsonOptions), cancellationToken);
        var subtitleFiles = await GenerateSubtitlesAsync(new VideoTtsTimingsDto(request.EventId, request.RegionId, request.Language, request.Platform, timings.AudioFilePath, timings.EstimatedDurationSeconds, timings.ActualDurationSeconds, timings.SectionTimings.Select(s => new VideoTtsSceneTimingDto(s.SectionKey, s.StartSeconds, s.EndSeconds, s.Narration)).ToArray(), timings.TtsProvider, timings.VoiceUsed, timings.GeneratedUtc, timings.AudioValidation, timings.DurationValidation), ScenePresentationProfile.LongForm, cancellationToken);

        return BuildLongFormTtsResponse(
            request.Phase,
            audioPath,
            timingsPath,
            timings.ActualDurationSeconds,
            new[] { NormalizePath(audioPath), NormalizePath(timingsPath) }.Concat(subtitleFiles).ToArray(),
            AzureTtsProviderName,
            audioValidation);
    }

    private void EnsureLongFormAzureTtsAvailable()
    {
        if (azureSpeechClient is null || azureSpeechOptions is null || !IsAzureSpeechConfigured(azureSpeechOptions.Value))
            throw new InvalidOperationException("Azure Speech TTS provider is not available for LongFormTts.");
    }

    private async Task WriteAzureLongFormTtsAudioAsync(string narrationText, string audioPath, CancellationToken cancellationToken)
    {
        EnsureLongFormAzureTtsAvailable();
        var audioBytes = await azureSpeechClient!.SynthesizeMp3Async(narrationText, azureSpeechOptions!.Value, cancellationToken);
        await File.WriteAllBytesAsync(audioPath, audioBytes, cancellationToken);
    }

    private string ResolveNeutralEducationalAzureVoice(VideoNarrationScriptDto script)
    {
        if (!string.Equals(script.TtsPlan.RecommendedVoice, "NeutralEducational", StringComparison.OrdinalIgnoreCase))
            return ResolveAzureVoice(script.FullNarrationText);

        return azureSpeechOptions?.Value.GetPreferredVoices("en").FirstOrDefault() ?? "en-US-JennyNeural";
    }

    private async Task<IReadOnlyList<LongFormVideoTtsSectionTimingDto>> BuildActualLongFormSectionTimingsAsync(VideoNarrationScriptDto script, double actualDurationSeconds, CancellationToken cancellationToken)
    {
        var measuredDurations = new List<double>();
        var tempRoot = Path.Combine(Path.GetTempPath(), "astronomy-longform-tts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            for (var index = 0; index < script.SceneScripts.Count; index++)
            {
                var section = script.SceneScripts[index];
                var tempPath = Path.Combine(tempRoot, $"section-{index:000}.mp3");
                await WriteAzureLongFormTtsAudioAsync(section.Narration, tempPath, cancellationToken);
                var measured = await ProbeDurationSecondsAsync(tempPath, cancellationToken);
                if (measured <= 0)
                    throw new InvalidOperationException($"Generated long-form TTS section '{section.SceneKey}' duration could not be measured.");
                measuredDurations.Add(measured);
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var measuredTotal = measuredDurations.Sum();
        if (measuredTotal <= 0)
            throw new InvalidOperationException("Generated long-form TTS section timings could not be measured.");

        var timings = new List<LongFormVideoTtsSectionTimingDto>();
        var cursor = 0.0;
        for (var index = 0; index < script.SceneScripts.Count; index++)
        {
            var section = script.SceneScripts[index];
            var duration = measuredDurations[index] / measuredTotal * actualDurationSeconds;
            var startSeconds = Math.Round(cursor, 3, MidpointRounding.AwayFromZero);
            cursor = index == script.SceneScripts.Count - 1 ? actualDurationSeconds : cursor + duration;
            var endSeconds = Math.Round(cursor, 3, MidpointRounding.AwayFromZero);
            timings.Add(new LongFormVideoTtsSectionTimingDto(section.SceneKey, startSeconds, endSeconds, section.Narration));
        }

        return timings;
    }


    private async Task<VideoAssemblyGenerationResponse> GenerateVideoAssemblyPlanAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        var outputPath = BuildVideoAssemblyPlanOutputPath(request.EventId, request.RegionId, ResolveRequestProfile(request));
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<VideoAssemblyPlanDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing video assembly plan could not be parsed.");
            ValidateVideoAssemblyPlan(existing);
            EnsureVideoAssemblyPlanAssetsExist(existing);
            return BuildAssemblyResponse(request.Phase, existing, outputPath, []);
        }

        var inputs = await EnsureRequiredAssemblyInputsAsync(request.EventId, request.RegionId, request.Platform, ResolveRequestProfile(request), cancellationToken);
        if (request.ScenePresentationProfile.HasValue && request.ScenePresentationProfile.Value != inputs.ScenePresentationProfile)
            throw new ArgumentException("Video assembly validation failed: requested scenePresentationProfile must match the platform scene presentation profile.");

        var plan = BuildVideoAssemblyPlan(request, inputs);
        ValidateVideoAssemblyPlan(plan);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllTextAsync(outputPath, SerializeVideoAssemblyPlan(plan), cancellationToken);
        }

        return BuildAssemblyResponse(request.Phase, plan, outputPath, []);
    }



    private async Task<VideoAssemblyGenerationResponse> GenerateVideoRenderAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        var planPath = BuildVideoAssemblyPlanOutputPath(request.EventId, request.RegionId, ResolveRequestProfile(request));
        if (!File.Exists(planPath))
            throw new ArgumentException($"Required render input '{ResolveVideoAssemblyPlanFileName(ResolveRequestProfile(request))}' was not found at '{NormalizePath(planPath)}'.");

        var plan = JsonSerializer.Deserialize<VideoAssemblyPlanDto>(await File.ReadAllTextAsync(planPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Required render input '{ResolveVideoAssemblyPlanFileName(ResolveRequestProfile(request))}' could not be parsed.");
        ValidateVideoAssemblyPlan(plan);
        EnsureVideoAssemblyPlanAssetsExist(plan);
        var renderMusicPlan = ResolveRenderMusicPlan(plan.RenderMusicPlan, request);

        var timingsPath = BuildVideoTtsTimingsOutputPath(request.EventId, request.RegionId, ResolveRequestProfile(request));
        if (File.Exists(timingsPath))
        {
            var timings = await ReadVideoTtsTimingsForAssemblyAsync(timingsPath, plan.ScenePresentationProfile, cancellationToken);
            ValidateVideoTtsTimings(timings);
            EnsureRenderPlanUsesActualTtsTiming(plan, timings);
        }
        else if (plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm)
        {
            throw new ArgumentException($"Required render input '{ResolveVideoTtsTimingsFileName(plan.ScenePresentationProfile)}' was not found at '{NormalizePath(timingsPath)}'.");
        }

        var outputPath = ResolveFinalVideoOutputPath(plan);
        var validationPath = BuildVideoRenderValidationOutputPath(request.EventId, request.RegionId, ResolveRequestProfile(request));
        var backgroundMusicSource = ResolveBackgroundMusicSource(renderMusicPlan);
        if (!request.DryRun && renderMusicPlan.BackgroundMusic && !backgroundMusicSource.Found && plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm)
            throw new InvalidOperationException("Background music requested but music source file was not found.");
        if (plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm
            && !string.Equals(Path.GetFileName(outputPath), FinalVideoShortFileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Video render validation failed: ShortForm output must be final-video-short.mp4.");
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existingValidation = await ValidateRenderedVideoAsync(outputPath, plan.AudioFilePath, plan, cancellationToken);
            var existingRenderPolish = await WriteVideoRenderValidationAsync(plan, request, renderMusicPlan, outputPath, existingValidation, validationPath, cancellationToken);
            EnsureShortFormRenderValidationPassed(existingRenderPolish);
            var existingGeneratedFiles = File.Exists(validationPath) ? new[] { NormalizePath(validationPath) } : Array.Empty<string>();
            return BuildRenderResponse(request, outputPath, existingValidation.FinalVideoDurationSeconds, existingValidation.OutputResolution, existingValidation.AudioTrackPresent, existingValidation.RenderSucceeded, existingGeneratedFiles, validationPath, existingRenderPolish);
        }

        if (!request.DryRun)
        {
            await RenderFinalVideoAsync(plan, renderMusicPlan, outputPath, cancellationToken);
        }

        var validation = request.DryRun
            ? new RenderValidation(true, File.Exists(plan.AudioFilePath), true, true, plan.TotalDurationSeconds, $"{plan.RenderSettings.Width}x{plan.RenderSettings.Height}", plan.RenderSettings.Fps, true)
            : await ValidateRenderedVideoAsync(outputPath, plan.AudioFilePath, plan, cancellationToken);

        if (!validation.VideoExists)
            throw new InvalidOperationException($"Video render validation failed: video missing at '{outputPath}'.");
        if (!validation.AudioExists)
            throw new InvalidOperationException($"Video render validation failed: audio missing at '{plan.AudioFilePath}'.");
        if (!validation.VideoDurationMatchesAudio)
            throw new InvalidOperationException($"Video render validation failed: video duration does not match TTS audio duration.");
        if (!validation.RenderSucceeded)
            throw new InvalidOperationException("Video render validation failed: render did not succeed.");

        var renderPolish = request.DryRun
            ? BuildVideoRenderValidation(plan, request, renderMusicPlan)
            : await WriteVideoRenderValidationAsync(plan, request, renderMusicPlan, outputPath, validation, validationPath, cancellationToken);
        EnsureShortFormRenderValidationPassed(renderPolish);
        var generatedFiles = request.DryRun ? Array.Empty<string>() : new[] { outputPath, NormalizePath(validationPath) };
        return BuildRenderResponse(request, outputPath, validation.FinalVideoDurationSeconds, validation.OutputResolution, validation.AudioTrackPresent, validation.RenderSucceeded, generatedFiles, validationPath, renderPolish);
    }

    private static async Task<LongFormVideoTtsTimingsDto> ReadLongFormVideoTtsTimingsAsync(string timingsPath, CancellationToken cancellationToken)
    {
        var document = await File.ReadAllTextAsync(timingsPath, cancellationToken);
        var longForm = JsonSerializer.Deserialize<LongFormVideoTtsTimingsDto>(document, JsonOptions);
        if (longForm?.SectionTimings is { Count: > 0 })
            return longForm;

        var legacy = JsonSerializer.Deserialize<VideoTtsTimingsDto>(document, JsonOptions)
            ?? throw new InvalidOperationException("Existing long-form video TTS timings could not be parsed.");
        return new LongFormVideoTtsTimingsDto(
            legacy.EventId,
            legacy.RegionId,
            legacy.Language,
            legacy.Platform,
            legacy.AudioFilePath,
            legacy.EstimatedDurationSeconds,
            legacy.ActualDurationSeconds,
            legacy.SceneTimings.Select(scene => new LongFormVideoTtsSectionTimingDto(scene.SceneKey, scene.StartSeconds, scene.EndSeconds, scene.Narration)).ToArray(),
            legacy.TtsProvider,
            legacy.VoiceUsed,
            legacy.AudioValidation ?? new VideoTtsAudioValidationDto(true, -120, -120, false),
            legacy.GeneratedUtc);
    }

    private async Task<VideoNarrationScriptDto> EnsureRequiredTtsInputsAsync(string eventId, string regionId, ScenePresentationProfile presentationProfile, CancellationToken cancellationToken)
    {
        var scriptPath = BuildVideoNarrationScriptOutputPath(eventId, regionId, presentationProfile);
        var scriptFileName = presentationProfile == ScenePresentationProfile.LongForm ? LongVideoNarrationScriptFileName : VideoNarrationScriptFileName;
        if (!File.Exists(scriptPath))
            throw new ArgumentException($"Required TTS input '{scriptFileName}' was not found at '{NormalizePath(scriptPath)}'.");

        var script = JsonSerializer.Deserialize<VideoNarrationScriptDto>(await File.ReadAllTextAsync(scriptPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Required TTS input '{scriptFileName}' could not be parsed.");
        ValidateVideoNarrationScript(script);
        return script;
    }


    private async Task<string> ReadRequiredNarrationTextAsync(VideoAssemblyGenerationRequest request, ScenePresentationProfile profile, VideoNarrationScriptDto script, CancellationToken cancellationToken)
    {
        var profileFolder = profile == ScenePresentationProfile.ShortForm ? "short" : "long";
        var narrationPath = request.ProductionContext?.NarrationRoot is null
            ? string.Empty
            : Path.Combine(request.ProductionContext.NarrationRoot, profileFolder, "narration.txt");

        var narrationText = !string.IsNullOrWhiteSpace(narrationPath) && File.Exists(narrationPath)
            ? await File.ReadAllTextAsync(narrationPath, cancellationToken)
            : script.FullNarrationText;

        if (string.IsNullOrWhiteSpace(narrationText))
        {
            var source = string.IsNullOrWhiteSpace(narrationPath) ? "video narration script" : NormalizePath(narrationPath);
            throw new ArgumentException($"Required TTS narration text is empty or missing: {source}.");
        }

        return narrationText.Trim();
    }


    private string BuildTtsDebugOutputPath(string eventId, string regionId, ScenePresentationProfile profile)
        => Path.Combine(BuildVideoAssemblyProfileRoot(eventId, regionId, profile), "phase-15-debug.json");

    private static async Task WriteTtsDebugAsync(
        string debugPath,
        string tempAudioPath,
        string finalAudioPath,
        string timingsPath,
        double actualDurationSeconds,
        int narrationWordCount,
        VideoTtsAudioValidationDto audioValidation,
        VideoDurationContractValidationDto? durationValidation,
        string failureReason,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            failureReason,
            tempAudioPath = NormalizePath(tempAudioPath),
            finalAudioPath = NormalizePath(finalAudioPath),
            timingsPath = NormalizePath(timingsPath),
            actualDurationSeconds = Math.Round(actualDurationSeconds, 3, MidpointRounding.AwayFromZero),
            narrationWordCount,
            audioFileSizeBytes = audioValidation.AudioFileSizeBytes,
            peakDb = audioValidation.AudioPeakDb,
            rmsDb = audioValidation.AudioRmsDb,
            isSilent = audioValidation.IsSilentAudio,
            audioValidationPassed = audioValidation.AudioValidationPassed,
            durationValidation,
            generatedUtc = DateTimeOffset.UtcNow
        };

        Directory.CreateDirectory(Path.GetDirectoryName(debugPath) ?? ".");
        await File.WriteAllTextAsync(debugPath, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
    }

    private static string BuildTtsDiagnosticFailureMessage(string failureReason, double actualDurationSeconds, int narrationWordCount, VideoTtsAudioValidationDto audioValidation, string tempAudioPath)
        => $"{failureReason} ActualDurationSeconds={actualDurationSeconds:0.###}; NarrationWordCount={narrationWordCount}; AudioFileSizeBytes={audioValidation.AudioFileSizeBytes}; PeakDb={audioValidation.AudioPeakDb:0.###}; RmsDb={audioValidation.AudioRmsDb:0.###}; IsSilent={audioValidation.IsSilentAudio}; TempAudioPath={NormalizePath(tempAudioPath)}.";

    private static void TryDeleteDirectory(string directoryPath)
    {
        try { if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<VideoAssemblyIntelligenceDto> EnsureRequiredScriptInputsAsync(string eventId, string regionId, ScenePresentationProfile presentationProfile, CancellationToken cancellationToken)
    {
        var videoAssemblyIntelligencePath = BuildVideoAssemblyIntelligenceOutputPath(eventId, regionId, presentationProfile);
        if (!File.Exists(videoAssemblyIntelligencePath))
            throw new ArgumentException($"Required video narration script input '{VideoAssemblyIntelligenceFileName}' was not found at '{NormalizePath(videoAssemblyIntelligencePath)}'.");

        var intelligence = JsonSerializer.Deserialize<VideoAssemblyIntelligenceDto>(await File.ReadAllTextAsync(videoAssemblyIntelligencePath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Required video narration script input '{VideoAssemblyIntelligenceFileName}' could not be parsed.");
        ValidateVideoAssemblyIntelligence(intelligence);
        if (presentationProfile == ScenePresentationProfile.ShortForm)
            ValidateRecommendedSceneOrder(intelligence.RecommendedSceneOrder, "video-assembly-intelligence.json");

        var heroRoot = BuildHeroAssetsRoot(eventId, regionId);
        await EnsureHeroStoryInputAsync(heroRoot, cancellationToken);
        using var heroSceneManifest = await EnsureJsonInputAsync(Path.Combine(heroRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);
        EnsureApprovedSceneImages(eventId, regionId, ResolveScenePresentationProfile(intelligence.Platform), heroSceneManifest);

        return intelligence;
    }

    private async Task EnsureRequiredInputsAsync(string eventId, string regionId, string platform, CancellationToken cancellationToken)
    {
        var heroRoot = BuildHeroAssetsRoot(eventId, regionId);
        var thumbnailRoot = BuildThumbnailAssetsRoot(eventId, regionId);

        await EnsureHeroStoryInputAsync(heroRoot, cancellationToken);
        var heroSceneManifestPath = Path.Combine(heroRoot, HeroSceneManifestFileName);
        var thumbnailSceneManifestPath = Path.Combine(thumbnailRoot, ThumbnailSceneManifestFileName);
        EnsureManifestPairExistsBeforeRead(heroSceneManifestPath, HeroSceneManifestFileName, thumbnailSceneManifestPath, ThumbnailSceneManifestFileName);

        using var heroSceneManifest = await EnsureJsonInputAsync(heroSceneManifestPath, HeroSceneManifestFileName, cancellationToken);
        using var heroCompositionModel = await EnsureJsonInputAsync(Path.Combine(heroRoot, HeroCompositionModelFileName), HeroCompositionModelFileName, cancellationToken);
        using var thumbnailSceneManifest = await EnsureJsonInputAsync(thumbnailSceneManifestPath, ThumbnailSceneManifestFileName, cancellationToken);
        using var thumbnailIntelligence = await EnsureJsonInputAsync(Path.Combine(thumbnailRoot, ThumbnailIntelligenceFileName), ThumbnailIntelligenceFileName, cancellationToken);
        using var thumbnailCompositionModel = await EnsureJsonInputAsync(Path.Combine(thumbnailRoot, ThumbnailCompositionModelFileName), ThumbnailCompositionModelFileName, cancellationToken);

        EnsureApprovedSceneImages(eventId, regionId, ResolveScenePresentationProfile(platform), heroSceneManifest, thumbnailSceneManifest);
    }

    private static void EnsureManifestPairExistsBeforeRead(string heroSceneManifestPath, string heroFileName, string thumbnailSceneManifestPath, string thumbnailFileName)
    {
        var missing = new[]
        {
            (Path: heroSceneManifestPath, FileName: heroFileName),
            (Path: thumbnailSceneManifestPath, FileName: thumbnailFileName)
        }
        .Where(item => !File.Exists(item.Path))
        .Select(item => $"{item.FileName} at '{NormalizePath(item.Path)}'")
        .ToArray();

        if (missing.Length > 0)
            throw new ArgumentException("Required video assembly manifest input(s) missing before narration generation: " + string.Join(", ", missing) + ".");
    }

    private static async Task<JsonDocument> EnsureJsonInputAsync(string path, string fileName, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"Required video assembly intelligence input '{fileName}' was not found at '{NormalizePath(path)}'.");

        return JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
    }

    private static async Task EnsureHeroStoryInputAsync(string heroRoot, CancellationToken cancellationToken)
    {
        var legacyHeroStoryPath = Path.Combine(heroRoot, HeroStoryFileName);
        var heroAssetStoryPath = Path.Combine(heroRoot, HeroAssetStoryFileName);
        var storyPath = File.Exists(legacyHeroStoryPath) ? legacyHeroStoryPath : heroAssetStoryPath;
        if (!File.Exists(storyPath))
            throw new ArgumentException($"Required video assembly intelligence input '{HeroStoryFileName}' was not found at '{NormalizePath(legacyHeroStoryPath)}'.");

        using var _ = JsonDocument.Parse(await File.ReadAllTextAsync(storyPath, cancellationToken));
    }

    private void EnsureApprovedSceneImages(string eventId, string regionId, ScenePresentationProfile presentationProfile, params JsonDocument[] manifests)
    {
        var sceneApprovalRoot = Path.Combine(BuildQuestionEngineRoot(eventId, regionId), SceneApprovalDirectoryName, ResolveSceneApprovalProfileDirectoryName(presentationProfile));
        var sceneIds = RequiredApprovedSceneIds
            .Concat(manifests.SelectMany(ResolveManifestSceneIds))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var missingSceneOutputs = sceneIds
            .Select(sceneId => Path.Combine(sceneApprovalRoot, $"{sceneId}-final.png"))
            .Where(path => !File.Exists(path))
            .Select(NormalizePath)
            .ToArray();

        if (missingSceneOutputs.Length > 0)
            throw new ArgumentException($"Required video assembly approved scene image(s) were not found: {string.Join(", ", missingSceneOutputs)}.");
    }

    private static IReadOnlyList<string> ResolveManifestSceneIds(JsonDocument manifest)
    {
        var sceneIds = new List<string>();
        CollectSceneIds(manifest.RootElement, sceneIds);
        return sceneIds;
    }

    private static void CollectSceneIds(JsonElement element, ICollection<string> sceneIds)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("sceneId", out var sceneIdElement) && !string.IsNullOrWhiteSpace(sceneIdElement.GetString()))
                    sceneIds.Add(sceneIdElement.GetString()!);
                else if (element.TryGetProperty("sceneNumber", out var sceneNumberElement) && sceneNumberElement.TryGetInt32(out var sceneNumber))
                    sceneIds.Add($"scene-{sceneNumber:000}");

                foreach (var property in element.EnumerateObject())
                    CollectSceneIds(property.Value, sceneIds);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectSceneIds(item, sceneIds);
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("scene-", StringComparison.OrdinalIgnoreCase))
                    sceneIds.Add(value);
                break;
        }
    }

    private VideoAssemblyIntelligenceDto BuildVideoAssemblyIntelligence(VideoAssemblyGenerationRequest request)
    {
        if (IsLongFormRequest(request))
            return BuildLongFormVideoAssemblyIntelligence(request);

        var targetDurationSeconds = ResolveShortFormTargetDuration(request);
        var baseSceneDurations = BuildShortFormRecommendedSceneDurations(request);
        var baseTotal = baseSceneDurations.Sum(scene => scene.DurationSeconds);
        var sceneDurations = baseSceneDurations.Select(scene => scene with
        {
            DurationSeconds = Math.Round(targetDurationSeconds * scene.DurationSeconds / baseTotal, 3, MidpointRounding.AwayFromZero)
        }).ToArray();
        sceneDurations[^1] = sceneDurations[^1] with { DurationSeconds = Math.Round(targetDurationSeconds - sceneDurations[..^1].Sum(scene => scene.DurationSeconds), 3, MidpointRounding.AwayFromZero) };

        return new VideoAssemblyIntelligenceDto(
            request.EventId,
            request.RegionId,
            request.Language,
            request.Platform,
            SelectedOpeningHook,
            "ShortFormAstronomyAlert",
            "Curiosity → Clarity → Action",
            ["Hook", "What", "Why", "Where", "When", "Action"],
            sceneDurations,
            sceneDurations.Sum(scene => scene.DurationSeconds),
            new VideoAssemblyNarrationStyleDto("Excited but clear", "Fast short-form", "Neutral energetic narrator"),
            new VideoAssemblyVisualStyleDto(true, false, true, "CrossFade", "Minimal"),
            new VideoAssemblyAudioPlanDto(true, true, string.IsNullOrWhiteSpace(request.MusicMood) ? "WonderCuriosity" : request.MusicMood, request.DuckMusicUnderNarration),
            ["video-narration-script.json", "video-tts-audio.mp3", "video-assembly-plan.json", "final-video-short.mp4", "video-render-validation.json"],
            new VideoAssemblyScoresDto(96, 95, 96, 95),
            [],
            DateTimeOffset.UtcNow,
            targetDurationSeconds,
            ScenePresentationProfile.ShortForm,
            "question-engine/scene-approval-v3/short/",
            null);
    }

    private IReadOnlyList<VideoAssemblySceneDurationDto> BuildShortFormRecommendedSceneDurations(VideoAssemblyGenerationRequest request)
    {
        var eventInfo = request.ProductionContext?.ProductionEventIntelligence;
        var scenesByQuestion = LoadShortFormPurposeSources(request)
            .GroupBy(scene => scene.QuestionType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return RequiredAssemblySceneOrder.Select(section => new VideoAssemblySceneDurationDto(
            section,
            ResolveShortFormBaseDuration(section),
            ResolveShortFormScenePurpose(section, eventInfo, scenesByQuestion))).ToArray();
    }

    private static double ResolveShortFormBaseDuration(string section)
        => section switch
        {
            "Hook" => 3.0,
            "What" => 4.0,
            "Why" => 4.0,
            "Where" => 3.0,
            "When" => 3.0,
            "Action" => 3.0,
            _ => 3.0
        };

    private static string ResolveShortFormScenePurpose(
        string section,
        ProductionEventIntelligence? eventInfo,
        IReadOnlyDictionary<string, VideoAssemblyPurposeSource> scenesByQuestion)
    {
        if (IsMeteorShower(eventInfo))
        {
            var title = FirstNonEmpty(eventInfo?.ShortTitle, eventInfo?.Title, "meteor shower peak");
            var direction = NormalizeMeteorDirection(FirstNonEmpty(eventInfo?.SkyDirectionHint, FindSourceText(scenesByQuestion, AstronomyQuestionTypes.Where), "east-to-overhead viewing direction"));
            var window = FirstNonEmpty(eventInfo?.BestViewingWindowLocal, eventInfo?.LocalPeakTime, FindSourceText(scenesByQuestion, AstronomyQuestionTypes.When), "the best viewing window");
            return section switch
            {
                "Hook" => $"Introduce {title}",
                "What" => $"Show {direction} viewing direction",
                "Why" => $"Show best viewing window {window}",
                "Where" => "Explain dark-sky/no-telescope viewing",
                "When" => "Show meteor streaks/radiant/low moon interference",
                "Action" => "Closing reminder/check weather/dark open place",
                _ => "Explain dark-sky/no-telescope viewing"
            };
        }

        var questionType = SectionToQuestionType(section);
        var source = scenesByQuestion.TryGetValue(questionType, out var scene) ? scene : null;
        var sourceText = FirstNonEmpty(source?.CaptionText, source?.NarrationText, source?.ViewerTakeaway, source?.SourceAnswer, source?.ScenePurpose);
        if (!string.IsNullOrWhiteSpace(sourceText))
            return BuildPurposeFromSource(section, sourceText!);

        var titleFallback = FirstNonEmpty(eventInfo?.ShortTitle, eventInfo?.Title, "the sky event");
        var directionFallback = FirstNonEmpty(eventInfo?.SkyDirectionHint, "the approved sky direction");
        var windowFallback = FirstNonEmpty(eventInfo?.BestViewingWindowLocal, eventInfo?.LocalPeakTime, "the approved viewing window");
        return section switch
        {
            "Hook" => $"Introduce {titleFallback}",
            "What" => $"Show {titleFallback}",
            "Why" => "Explain why it matters",
            "Where" => $"Show where to look: {directionFallback}",
            "When" => $"Show best viewing window {windowFallback}",
            "Action" => "Closing reminder/check weather/open place",
            _ => $"Show {titleFallback}"
        };
    }

    private static string BuildPurposeFromSource(string section, string sourceText)
    {
        var text = TrimPurpose(sourceText);
        return section switch
        {
            "Hook" => $"Introduce {text}",
            "What" => $"Show {text}",
            "Why" => $"Explain {text}",
            "Where" => $"Show {text}",
            "When" => $"Show {text}",
            "Action" => $"Closing reminder: {text}",
            _ => text
        };
    }

    private static string TrimPurpose(string value)
    {
        var normalized = Regex.Replace(value.Trim(), @"\s+", " ").Trim(' ', '.');
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 12 ? normalized : string.Join(' ', words.Take(12));
    }

    private static string SectionToQuestionType(string section)
        => section switch
        {
            "Hook" => AstronomyQuestionTypes.What,
            "What" => AstronomyQuestionTypes.What,
            "Why" => AstronomyQuestionTypes.Why,
            "Where" => AstronomyQuestionTypes.Where,
            "When" => AstronomyQuestionTypes.When,
            "Action" => AstronomyQuestionTypes.Action,
            _ => section
        };

    private static bool IsMeteorShower(ProductionEventIntelligence? eventInfo)
        => string.Equals(eventInfo?.EventType, "MeteorShower", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMeteorDirection(string direction)
    {
        var value = Regex.Replace(direction.Trim(), @"\s+", " ");
        value = value.Replace("east to overhead", "east-to-overhead", StringComparison.OrdinalIgnoreCase)
            .Replace("east-to overhead", "east-to-overhead", StringComparison.OrdinalIgnoreCase)
            .Replace("east to-overhead", "east-to-overhead", StringComparison.OrdinalIgnoreCase);
        return value.Contains("east-to-overhead", StringComparison.OrdinalIgnoreCase) ? "east-to-overhead" : value;
    }

    private static string? FindSourceText(IReadOnlyDictionary<string, VideoAssemblyPurposeSource> scenesByQuestion, string questionType)
        => scenesByQuestion.TryGetValue(questionType, out var scene)
            ? FirstNonEmpty(scene.CaptionText, scene.NarrationText, scene.ViewerTakeaway, scene.SourceAnswer)
            : null;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private IReadOnlyList<VideoAssemblyPurposeSource> LoadShortFormPurposeSources(VideoAssemblyGenerationRequest request)
    {
        var sources = new List<VideoAssemblyPurposeSource>();
        var questionRoot = BuildQuestionEngineRoot(request.EventId, request.RegionId);
        AddNarrationPurposeSources(Path.Combine(questionRoot, "question-driven-narration.json"), sources);
        AddEnrichedPlanPurposeSources(Path.Combine(questionRoot, "question-driven-scene-plan.enriched.json"), sources);
        AddSceneApprovalPurposeSources(Path.Combine(questionRoot, SceneApprovalDirectoryName, "short"), sources);
        AddSceneApprovalPurposeSources(Path.Combine(questionRoot, SceneApprovalDirectoryName, "long"), sources);
        return sources
            .GroupBy(source => source.SceneNumber)
            .OrderBy(group => group.Key)
            .Select(group => group.First())
            .ToArray();
    }

    private static void AddNarrationPurposeSources(string path, List<VideoAssemblyPurposeSource> sources)
    {
        if (!File.Exists(path)) return;
        var narration = TryDeserialize<QuestionDrivenNarrationDto>(path);
        if (narration is null) return;
        sources.AddRange(narration.Scenes.Select(scene => new VideoAssemblyPurposeSource(
            scene.SceneNumber,
            scene.QuestionType,
            scene.ScenePurpose,
            scene.SourceAnswer,
            scene.ViewerTakeaway,
            scene.NarrationText,
            scene.CaptionText)));
    }

    private static void AddEnrichedPlanPurposeSources(string path, List<VideoAssemblyPurposeSource> sources)
    {
        if (!File.Exists(path)) return;
        var plan = TryDeserialize<EnrichedQuestionScenePlanDto>(path);
        if (plan is null) return;
        sources.AddRange(plan.Scenes.Select(scene => new VideoAssemblyPurposeSource(
            scene.SceneNumber,
            scene.QuestionType,
            scene.ScenePurpose,
            scene.SourceAnswer,
            scene.ViewerTakeaway,
            scene.NarrationIntent,
            scene.OverlayIntent)));
    }

    private static void AddSceneApprovalPurposeSources(string root, List<VideoAssemblyPurposeSource> sources)
    {
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            using var doc = TryParseJsonFile(path);
            if (doc is null) continue;
            var sceneNumber = TryGetInt(doc.RootElement, "sceneNumber") ?? TryParseSceneNumberFromPath(path);
            if (sceneNumber is null) continue;
            var questionType = TryGetString(doc.RootElement, "questionType") ?? string.Empty;
            sources.Add(new VideoAssemblyPurposeSource(
                sceneNumber.Value,
                questionType,
                TryGetString(doc.RootElement, "scenePurpose") ?? TryGetString(doc.RootElement, "purpose") ?? string.Empty,
                TryGetString(doc.RootElement, "sourceAnswer") ?? string.Empty,
                TryGetString(doc.RootElement, "viewerTakeaway") ?? TryGetString(doc.RootElement, "description") ?? string.Empty,
                TryGetString(doc.RootElement, "narrationText") ?? TryGetString(doc.RootElement, "narration") ?? string.Empty,
                TryGetString(doc.RootElement, "overlayText") ?? TryGetString(doc.RootElement, "captionText") ?? string.Empty));
        }
    }

    private static T? TryDeserialize<T>(string path)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions); }
        catch (JsonException) { return default; }
        catch (IOException) { return default; }
    }

    private static JsonDocument? TryParseJsonFile(string path)
    {
        try { return JsonDocument.Parse(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
            var nested = property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? TryGetStringRecursive(property.Value, propertyName) : null;
            if (!string.IsNullOrWhiteSpace(nested)) return nested;
        }
        return null;
    }

    private static string? TryGetStringRecursive(JsonElement element, string propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.String)
                        return property.Value.GetString();
                    var nested = TryGetStringRecursive(property.Value, propertyName);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = TryGetStringRecursive(item, propertyName);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
                break;
        }
        return null;
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) && property.Value.TryGetInt32(out var value))
                return value;
        }
        return null;
    }

    private static int? TryParseSceneNumberFromPath(string path)
    {
        var match = Regex.Match(Path.GetFileName(path), @"scene-(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }


    private sealed record LongFormNarrationContext(
        string Title,
        string ShortTitle,
        string EventType,
        string PrimaryObjectsText,
        string SkyDirectionText,
        string LocalPeakTimeText,
        string BestViewingWindowText,
        string ScientificContextText,
        string ViewerInstructionsText,
        IReadOnlyList<string> SourceNotes,
        IReadOnlyList<string> ScenePlanNotes);

    private sealed record VideoAssemblyPurposeSource(int SceneNumber, string QuestionType, string ScenePurpose, string SourceAnswer, string ViewerTakeaway, string NarrationText, string CaptionText);

    private VideoAssemblyIntelligenceDto BuildLongFormVideoAssemblyIntelligence(VideoAssemblyGenerationRequest request)
    {
        var targetDurationSeconds = ResolveLongFormTargetDuration(request);
        var perSection = Math.Round(targetDurationSeconds / LongFormSectionOrder.Length, 3, MidpointRounding.AwayFromZero);
        var sceneDurations = LongFormSectionOrder.Select(section => new VideoAssemblySceneDurationDto(section, perSection, ResolveLongFormSectionPurpose(section))).ToArray();
        var adjusted = sceneDurations[..^1].Append(sceneDurations[^1] with { DurationSeconds = Math.Round(targetDurationSeconds - sceneDurations[..^1].Sum(scene => scene.DurationSeconds), 3, MidpointRounding.AwayFromZero) }).ToArray();

        return new VideoAssemblyIntelligenceDto(
            request.EventId,
            request.RegionId,
            request.Language,
            request.Platform,
            SelectedOpeningHook,
            "EducationalAstronomyGuide",
            "Curiosity → Understanding → Observation → Action",
            LongFormSectionOrder,
            adjusted,
            targetDurationSeconds,
            new VideoAssemblyNarrationStyleDto("Educational but simple", "Measured long-form", "Clear astronomy guide"),
            new VideoAssemblyVisualStyleDto(true, false, true, "CrossFade", "Minimal explanatory captions"),
            new VideoAssemblyAudioPlanDto(true, true, string.IsNullOrWhiteSpace(request.MusicMood) ? "WonderCuriosity" : request.MusicMood, request.DuckMusicUnderNarration),
            ["video-long-narration-script.json", "video-long-tts-audio.mp3", "video-long-assembly-plan.json", "final-video-long.mp4", "video-long-render-validation.json"],
            new VideoAssemblyScoresDto(94, 94, 92, 94),
            [],
            DateTimeOffset.UtcNow,
            targetDurationSeconds,
            ScenePresentationProfile.LongForm,
            "question-engine/scene-approval-v3/long/",
            LongFormSectionOrder);
    }

    private VideoNarrationScriptDto BuildVideoNarrationScript(VideoAssemblyGenerationRequest request, VideoAssemblyIntelligenceDto intelligence)
    {
        if (IsLongFormRequest(request))
            return BuildLongFormVideoNarrationScript(request, intelligence);

        var durations = intelligence.RecommendedSceneDurations.ToDictionary(scene => scene.SceneKey, scene => scene.DurationSeconds, StringComparer.OrdinalIgnoreCase);
        var eventInfo = request.ProductionContext?.ProductionEventIntelligence;
        var title = eventInfo?.ShortTitle ?? eventInfo?.Title ?? "This sky event";
        var objects = string.Join(" + ", (eventInfo?.ResolvedObjectNames ?? eventInfo?.PrimaryObjects ?? []).Take(2));
        if (string.IsNullOrWhiteSpace(objects)) objects = title;
        var direction = eventInfo?.SkyDirectionHint ?? "the approved sky direction";
        var window = eventInfo?.BestViewingWindowLocal ?? eventInfo?.LocalPeakTime ?? "the approved viewing window";
        var sceneScripts = BuildTargetedShortFormSceneScripts(
            durations,
            title,
            objects,
            direction,
            window,
            eventInfo?.ScientificContext);
        var fullNarrationText = string.Join(" ", sceneScripts.Select(scene => scene.Narration));
        var totalEstimatedDurationSeconds = Math.Round(sceneScripts.Sum(scene => scene.DurationSeconds), 3, MidpointRounding.AwayFromZero);

        return new VideoNarrationScriptDto(
            request.EventId,
            request.RegionId,
            request.Language,
            request.Platform,
            totalEstimatedDurationSeconds,
            new VideoNarrationScriptStyleDto("Excited but clear", "Fast short-form", "Neutral energetic narrator"),
            sceneScripts,
            fullNarrationText,
            new VideoNarrationTtsPlanDto(true, "NeutralEnergetic", "video-tts-audio.mp3"),
            new VideoNarrationScriptScoresDto(96, 95, 96),
            [],
            DateTimeOffset.UtcNow);
    }


    private IReadOnlyList<VideoNarrationSceneScriptDto> BuildTargetedShortFormSceneScripts(
        IReadOnlyDictionary<string, double> durations,
        string title,
        string objects,
        string direction,
        string window,
        string? scientificContext)
    {
        var sceneScripts = new[]
        {
            new VideoNarrationSceneScriptDto("Hook", GetDuration(durations, "Hook", 3.0), $"{title} is a sky highlight.", title),
            new VideoNarrationSceneScriptDto("What", GetDuration(durations, "What", 4.0), $"Watch for {objects}.", objects),
            new VideoNarrationSceneScriptDto("Why", GetDuration(durations, "Why", 4.0), "Dark skies make it easier to notice.", "Why it matters"),
            new VideoNarrationSceneScriptDto("Where", GetDuration(durations, "Where", 3.0), $"Look toward {direction}.", direction),
            new VideoNarrationSceneScriptDto("When", GetDuration(durations, "When", 3.0), $"Best viewing is {window}.", window),
            new VideoNarrationSceneScriptDto("Action", GetDuration(durations, "Action", 3.0), "Choose a safe open spot, check clouds, and set a reminder.", "Set a reminder")
        };

        var durationProfile = ResolveDurationProfile(ScenePresentationProfile.ShortForm);
        var estimatedDuration = Math.Clamp(EstimateSpokenDurationSeconds(string.Join(" ", sceneScripts.Select(scene => scene.Narration))), durationProfile.TargetDurationSecondsMin, durationProfile.TargetDurationSecondsMax);
        return NormalizeShortFormSceneDurations(sceneScripts, estimatedDuration);
    }


    private static string BuildShortFormWhyNarration(string? scientificContext)
    {
        if (string.IsNullOrWhiteSpace(scientificContext))
            return "It matters because the view is clear, timely, and easy to recognize.";

        var shortened = KeepFirstTwoSentences(scientificContext.Trim());
        return CountSpokenWords(shortened) <= 18
            ? shortened
            : "It matters because the view is clear, timely, and easy to recognize.";
    }

    private IReadOnlyList<VideoNarrationSceneScriptDto> NormalizeShortFormSceneDurations(IReadOnlyList<VideoNarrationSceneScriptDto> sceneScripts, double targetDurationSeconds)
    {
        var estimatedDurations = sceneScripts.Select(scene => Math.Max(0.5, EstimateSpokenDurationSeconds(scene.Narration))).ToArray();
        var total = estimatedDurations.Sum();
        if (total <= 0)
            return sceneScripts;

        return sceneScripts.Select((scene, index) => scene with
        {
            DurationSeconds = Math.Round(targetDurationSeconds * estimatedDurations[index] / total, 3, MidpointRounding.AwayFromZero)
        }).ToArray();
    }

    private VideoNarrationScriptDto BuildLongFormVideoNarrationScript(VideoAssemblyGenerationRequest request, VideoAssemblyIntelligenceDto intelligence)
    {
        var sceneScripts = BuildBalancedLongFormSceneScripts(request);
        var fullNarrationText = string.Join(" ", sceneScripts.Select(scene => scene.Narration));
        var totalEstimatedDurationSeconds = EstimateSpokenDurationSeconds(fullNarrationText);

        return new VideoNarrationScriptDto(
            request.EventId,
            request.RegionId,
            request.Language,
            request.Platform,
            totalEstimatedDurationSeconds,
            new VideoNarrationScriptStyleDto("Educational but simple", "Measured long-form", "Clear astronomy guide"),
            sceneScripts,
            fullNarrationText,
            new VideoNarrationTtsPlanDto(true, "NeutralEducational", "video-long-tts-audio.mp3"),
            new VideoNarrationScriptScoresDto(95, 90, 95),
            [],
            DateTimeOffset.UtcNow);
    }

    private IReadOnlyList<VideoNarrationSceneScriptDto> BuildBalancedLongFormSceneScripts(VideoAssemblyGenerationRequest request)
    {
        var eventInfo = request.ProductionContext?.ProductionEventIntelligence;
        var context = BuildLongFormNarrationContext(request);
        var scripts = LongFormSectionOrder.Select(section =>
        {
            var narration = BuildLongFormNarration(section, context);
            return new VideoNarrationSceneScriptDto(section, EstimateSpokenDurationSeconds(narration, ResolveLongFormNarrationWordsPerMinute()), narration, ResolveLongFormOnScreenText(section, eventInfo));
        }).ToArray();

        var contract = ResolveDurationProfile(ScenePresentationProfile.LongForm);
        var targetSeconds = ResolveLongFormTargetDuration(request);
        var wordsPerMinute = ResolveLongFormNarrationWordsPerMinute();
        scripts = NormalizeLongFormSceneNarration(scripts, context, contract.TargetDurationSecondsMin, contract.TargetDurationSecondsMax, targetSeconds, wordsPerMinute);

        var totalDuration = EstimateSpokenDurationSeconds(string.Join(" ", scripts.Select(scene => scene.Narration)), wordsPerMinute);
        if (totalDuration < contract.TargetDurationSecondsMin || totalDuration > contract.TargetDurationSecondsMax)
            throw new ArgumentException($"Video narration script validation failed: LongForm narration word-count estimate must be {contract.TargetDurationSecondsMin:0.###}-{contract.TargetDurationSecondsMax:0.###} seconds.");

        return scripts.Select(scene => scene with { DurationSeconds = EstimateSpokenDurationSeconds(scene.Narration, wordsPerMinute) }).ToArray();
    }

    private VideoNarrationSceneScriptDto[] NormalizeLongFormSceneNarration(IReadOnlyList<VideoNarrationSceneScriptDto> scripts, LongFormNarrationContext context, double minSeconds, double maxSeconds, double targetSeconds, double wordsPerMinute)
    {
        var expanded = scripts.ToArray();
        var targetWords = Math.Max((int)Math.Ceiling(targetSeconds * wordsPerMinute / 60.0), (int)Math.Ceiling(minSeconds * wordsPerMinute / 60.0));
        for (var round = 0; round < 12; round++)
        {
            var narration = string.Join(" ", expanded.Select(scene => scene.Narration));
            var duration = EstimateSpokenDurationSeconds(narration, wordsPerMinute);
            var words = CountSpokenWords(narration);
            if (duration >= minSeconds && duration <= maxSeconds && words >= targetWords * 0.9)
                return expanded;
            if (duration > maxSeconds)
            {
                var maxWords = Math.Max(targetWords, (int)Math.Floor(maxSeconds * wordsPerMinute / 60.0));
                expanded = TrimLongFormSceneNarrationToWordBudget(expanded, maxWords);
                continue;
            }

            expanded = ExpandLongFormSceneNarration(expanded, context, round);
        }

        return expanded;
    }

    private static VideoNarrationSceneScriptDto[] ExpandLongFormSceneNarration(IReadOnlyList<VideoNarrationSceneScriptDto> scripts, LongFormNarrationContext context, int round = 0)
        => scripts.Select(scene => scene with { Narration = $"{scene.Narration} {BuildLongFormExpansionSentence(scene.SceneKey, context, round)}" }).ToArray();

    private static VideoNarrationSceneScriptDto[] ShortenLongFormSceneNarration(IReadOnlyList<VideoNarrationSceneScriptDto> scripts)
        => scripts.Select(scene => scene with { Narration = KeepFirstTwoSentences(scene.Narration) }).ToArray();

    private static VideoNarrationSceneScriptDto[] TrimLongFormSceneNarrationToWordBudget(IReadOnlyList<VideoNarrationSceneScriptDto> scripts, int wordBudget)
    {
        if (scripts.Count == 0 || wordBudget <= 0)
            return scripts.ToArray();

        var baseBudget = Math.Max(20, wordBudget / scripts.Count);
        var remainder = Math.Max(0, wordBudget - (baseBudget * scripts.Count));
        return scripts.Select((scene, index) => scene with
        {
            Narration = KeepFirstWords(scene.Narration, baseBudget + (index < remainder ? 1 : 0))
        }).ToArray();
    }

    private static string KeepFirstWords(string narration, int maxWords)
    {
        var matches = SpokenWordRegex().Matches(narration);
        if (matches.Count <= maxWords) return narration;
        var endIndex = matches[Math.Max(0, maxWords - 1)].Index + matches[Math.Max(0, maxWords - 1)].Length;
        var trimmed = narration[..endIndex].Trim().TrimEnd(',', ';', ':', '-');
        return trimmed.EndsWith(".", StringComparison.Ordinal) || trimmed.EndsWith("!", StringComparison.Ordinal) || trimmed.EndsWith("?", StringComparison.Ordinal)
            ? trimmed
            : trimmed + ".";
    }

    private static string KeepFirstTwoSentences(string narration)
    {
        var sentences = narration.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return sentences.Length <= 2 ? narration : string.Join(". ", sentences.Take(2)) + ".";
    }

    private static string BuildLongFormExpansionSentence(string section, LongFormNarrationContext context, int round = 0)
    {
        var sceneNote = context.ScenePlanNotes.Count == 0 ? string.Empty : context.ScenePlanNotes[round % context.ScenePlanNotes.Count];
        var sourceNote = context.SourceNotes.Count == 0 ? string.Empty : context.SourceNotes[round % context.SourceNotes.Count];
        return section switch
        {
            "Hook" => $"For {context.ShortTitle}, the promise is simple: a real sky event, a narrow chance to see it, and a view that rewards anyone who steps outside at the right moment.",
            "WhatIsHappening" => $"Behind the scene, {context.PrimaryObjectsText} follow separate paths that briefly line up from our point of view, turning ordinary motion into something visible and memorable.",
            "WhenToLook" => $"The strongest opportunity gathers around {HumanizeViewingWindow(context.BestViewingWindowText)}, with {HumanizeViewingWindow(context.LocalPeakTimeText)} serving as a useful center point when the sky is clear.",
            "WhereToLook" => $"Let {context.SkyDirectionText} be the starting cue, then move slowly across the nearby sky instead of searching at random.",
            "WhyItMatters" => $"Its value comes from timing: {context.Title} is not just a fact on a calendar, but a visible reminder that the sky is constantly changing above us.",
            "HowToObserve" => "A comfortable view matters as much as the science: stand somewhere safe, dim bright screens, and give the scene enough time to emerge.",
            "WhatYouWillSee" => $"The view may be subtle at first, but {context.PrimaryObjectsText} can anchor the frame as the event slowly becomes easier to recognize.",
            "InterestingFact" => string.IsNullOrWhiteSpace(sourceNote) ? $"That is part of the beauty of {context.ShortTitle}: the same simple checks of time, direction, and patience can turn a distant event into a personal memory." : $"One detail deepens the story: {CleanLongFormInstructionLeakage(sourceNote)}",
            "ObservationTips" => string.IsNullOrWhiteSpace(sceneNote) ? "A small checklist is enough: clear sky, open view, safe footing, and a few quiet minutes away from glare." : $"The scene plan points to a simple takeaway: {CleanLongFormInstructionLeakage(sceneNote)}",
            "Recap" => $"Put it together: {context.ShortTitle}, {context.PrimaryObjectsText}, {context.SkyDirectionText}, and {HumanizeViewingWindow(context.BestViewingWindowText)}.",
            "Action" => "The closing step is quiet and practical: save the window, check the forecast, invite someone nearby, and let the sky do the rest.",
            _ => "The observation remains simple, safe, and grounded in the visible sky."
        };
    }

    private double ResolveLongFormNarrationWordsPerMinute()
    {
        var configured = videoAssemblyOptions?.Value.LongNarrationWordsPerMinute ?? DefaultLongFormNarrationWordsPerMinute;
        return configured > 0 ? configured : DefaultLongFormNarrationWordsPerMinute;
    }

    private double EstimateSpokenDurationSeconds(string narration)
        => EstimateSpokenDurationSeconds(narration, ResolveLongFormNarrationWordsPerMinute());

    private static double EstimateSpokenDurationSeconds(string narration, double wordsPerMinute)
        => Math.Round(CountSpokenWords(narration) / wordsPerMinute * 60.0, 3, MidpointRounding.AwayFromZero);

    private static int CountSpokenWords(string narration)
        => string.IsNullOrWhiteSpace(narration) ? 0 : SpokenWordRegex().Matches(narration).Count;

    [GeneratedRegex("[\\p{L}\\p{N}]+(?:['’\u2010-\u2015-][\\p{L}\\p{N}]+)?")]
    private static partial Regex SpokenWordRegex();

    private static string ResolveLongFormSectionPurpose(string section)
        => section switch
        {
            "Hook" => "Invite curiosity",
            "WhatIsHappening" => "Explain the sky event in simple terms",
            "WhyItMatters" => "Explain why this event matters",
            "WhereToLook" => "Give direction cue",
            "WhenToLook" => "Give timing cue",
            "HowToObserve" => "Give practical observation guidance",
            "WhatYouWillSee" => "Set realistic visual expectations",
            "InterestingFact" => "Add educational interest",
            "ObservationTips" => "Improve viewer success",
            "Recap" => "Summarize the steps",
            "Action" => "Call viewer outside",
            _ => "Educational section"
        };

    private static string BuildLongFormNarration(string section, LongFormNarrationContext context)
        => section switch
        {
            "Hook" => $"{context.ShortTitle} is the kind of sky moment that asks for a little planning and rewards a few minutes of attention.",
            "WhatIsHappening" => $"{context.Title} centers on {context.PrimaryObjectsText}. {CleanLongFormInstructionLeakage(context.ScientificContextText)}",
            "WhyItMatters" => "The reason it matters is not only rarity. It is the chance to feel celestial motion as something immediate, visible, and shared.",
            "WhereToLook" => $"Begin with {context.SkyDirectionText}, then let the brightest landmarks guide your eyes through the surrounding sky.",
            "WhenToLook" => $"The best viewing opportunities gather around {HumanizeViewingWindow(context.BestViewingWindowText)}, with conditions changing quickly as the window opens and fades.",
            "HowToObserve" => $"Keep the setup simple. {CleanLongFormInstructionLeakage(context.ViewerInstructionsText)}",
            "WhatYouWillSee" => $"Expect a real sky view, not a diagram: {context.PrimaryObjectsText} will be the anchor while timing, darkness, and horizon clarity shape what stands out.",
            "InterestingFact" => $"A small astronomical alignment can feel large because it compresses distance, motion, and perspective into a view you can recognize with your own eyes.",
            "ObservationTips" => "Check clouds, avoid bright lights, and arrive early enough for your eyes to settle before the most important minutes arrive.",
            "Recap" => $"Remember the essentials: {context.ShortTitle}, {context.SkyDirectionText}, and {HumanizeViewingWindow(context.BestViewingWindowText)}.",
            "Action" => "If the weather cooperates, step outside with patience. The sky will not hold the moment for long.",
            _ => "The story continues in the visible sky, with timing and patience doing most of the work."
        };

    private static string HumanizeViewingWindow(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "the local viewing window";
        return Regex.IsMatch(value, @"\b\d{4}-\d{2}-\d{2}|\b\d{1,2}:\d{2}\s*(?:[+-]\d{2}:?\d{2}|UTC|GMT)\b", RegexOptions.IgnoreCase)
            ? "the local viewing window"
            : value;
    }

    private static string CleanLongFormInstructionLeakage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleanedSentences = SplitNarrationSentences(value)
            .Select(sentence => RemoveRawTimestampText(sentence).Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .Where(sentence => !ContainsNarrationAuthoringInstruction(sentence))
            .Select(sentence => Regex.Replace(sentence, @"\b(?:during|at|around|on)\s*[,.]?\s*", string.Empty, RegexOptions.IgnoreCase).Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .Select(EnsureTerminalPunctuation)
            .ToArray();

        if (cleanedSentences.Length > 0)
            return string.Join(" ", cleanedSentences);

        var cleaned = RemoveRawTimestampText(value);
        foreach (var phrase in NarrationAuthoringInstructionPhrases)
            cleaned = Regex.Replace(cleaned, @"\b" + Regex.Escape(phrase) + @"\b\s*(?:[:\-–—]|that|what|where|when|why|how)?", string.Empty, RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', ',', ';', ':', '-', '–', '—');
        return string.IsNullOrWhiteSpace(cleaned) ? string.Empty : EnsureTerminalPunctuation(cleaned);
    }

    private static IReadOnlyList<string> SplitNarrationSentences(string value)
        => Regex.Split(value ?? string.Empty, @"(?<=[.!?])\s+")
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

    private static string RemoveRawTimestampText(string value)
        => Regex.Replace(value ?? string.Empty, @"\b\d{4}-\d{2}-\d{2}(?:[ T]\d{1,2}:\d{2})?\s*(?:[+-]\d{2}:?\d{2}|UTC|GMT)?\b|\b\d{1,2}:\d{2}\s*(?:[+-]\d{2}:?\d{2}|UTC|GMT)\b", "the local viewing window", RegexOptions.IgnoreCase);

    private static string EnsureTerminalPunctuation(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.EndsWith(".", StringComparison.Ordinal) || trimmed.EndsWith("!", StringComparison.Ordinal) || trimmed.EndsWith("?", StringComparison.Ordinal)
            ? trimmed
            : trimmed + ".";
    }

    private LongFormNarrationContext BuildLongFormNarrationContext(VideoAssemblyGenerationRequest request)
    {
        var eventInfo = request.ProductionContext?.ProductionEventIntelligence;
        var title = FirstNonEmpty(eventInfo?.Title, request.EventId, "this sky event");
        var shortTitle = FirstNonEmpty(eventInfo?.ShortTitle, title);
        var objects = (eventInfo?.ResolvedObjectNames ?? eventInfo?.PrimaryObjects ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Take(5).ToArray();
        var primaryObjectsText = objects.Length == 0 ? shortTitle : string.Join(", ", objects);
        var sourceNotes = (request.SourceNotes ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Take(4).ToArray();
        var purposeSources = LoadShortFormPurposeSources(request);
        var scenePlanNotes = purposeSources
            .Select(source => FirstNonEmpty(source.ViewerTakeaway, source.ScenePurpose, source.NarrationText, source.CaptionText))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        return new LongFormNarrationContext(
            title,
            shortTitle,
            FirstNonEmpty(eventInfo?.EventType, "AstronomyEvent"),
            primaryObjectsText,
            FirstNonEmpty(eventInfo?.SkyDirectionHint, "the approved sky direction"),
            FirstNonEmpty(eventInfo?.LocalPeakTime, "the approved peak time"),
            FirstNonEmpty(eventInfo?.BestViewingWindowLocal, eventInfo?.PreferredViewingWindow, eventInfo?.LocalPeakTime, "the approved viewing window"),
            FirstNonEmpty(eventInfo?.ScientificContext, "Explain the observable geometry and why the timing matters in plain language."),
            string.Join(" ", eventInfo?.ViewerInstructions ?? ["Choose a safe open location, let your eyes adjust, and avoid bright lights."]),
            sourceNotes,
            scenePlanNotes);
    }

    private static string ResolveLongFormOnScreenText(string section, ProductionEventIntelligence? eventInfo = null)
        => section switch
        {
            "Hook" => eventInfo?.ShortTitle ?? eventInfo?.Title ?? "Sky Event",
            "WhatIsHappening" => eventInfo?.EventType ?? "What’s happening",
            "WhyItMatters" => "Why it matters",
            "WhereToLook" => eventInfo?.SkyDirectionHint ?? "Where to look",
            "WhenToLook" => eventInfo?.BestViewingWindowLocal ?? eventInfo?.LocalPeakTime ?? "When to watch",
            "HowToObserve" => "Eyes first, binoculars optional",
            "WhatYouWillSee" => "Two bright points",
            "InterestingFact" => "A beginner-friendly sky marker",
            "ObservationTips" => "Clear horizon helps",
            "Recap" => "After sunset • West • Two bright points",
            "Action" => "Step outside",
            _ => section
        };

    private static double GetDuration(IReadOnlyDictionary<string, double> durations, string sceneKey, double fallback)
        => durations.TryGetValue(sceneKey, out var duration) ? duration : fallback;



    private async Task<IReadOnlyList<string>> GenerateSubtitlesAsync(VideoTtsTimingsDto timings, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        var subtitleOptions = videoAssemblyOptions?.Value.Subtitles ?? new VideoAssemblySubtitleOptions();
        if (!subtitleOptions.Enabled) return [];
        var folder = profile == ScenePresentationProfile.ShortForm ? "short" : "long";
        var fileStem = profile == ScenePresentationProfile.ShortForm ? "short" : "long";
        var root = Path.Combine(BuildVideoAssemblyRoot(timings.EventId, timings.RegionId), "subtitles", folder);
        Directory.CreateDirectory(root);
        var outputs = new List<string>();
        var blocks = BuildSubtitleBlocks(timings.SceneTimings);
        if (subtitleOptions.GenerateSrt)
        {
            var path = Path.Combine(root, fileStem + ".srt");
            await File.WriteAllTextAsync(path, BuildSrt(blocks), cancellationToken);
            outputs.Add(NormalizePath(path));
        }
        if (subtitleOptions.GenerateAss)
        {
            var path = Path.Combine(root, fileStem + ".ass");
            await File.WriteAllTextAsync(path, BuildAss(blocks), cancellationToken);
            outputs.Add(NormalizePath(path));
        }
        var diagnosticsPath = Path.Combine(root, "subtitle-validation.json");
        var srtPath = Path.Combine(root, fileStem + ".srt");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new { subtitleVersion = "V1", srtGenerated = subtitleOptions.GenerateSrt, assGenerated = subtitleOptions.GenerateAss, burnInEnabled = subtitleOptions.BurnIn, enableSubtitles = subtitleOptions.EnableSubtitles, subtitleFilesGenerated = subtitleOptions.GenerateSrt && File.Exists(srtPath), shortSrtPath = profile == ScenePresentationProfile.ShortForm ? NormalizePath(srtPath) : string.Empty, longSrtPath = profile == ScenePresentationProfile.LongForm ? NormalizePath(srtPath) : string.Empty, timingValid = blocks.All(b => b.EndSeconds > b.StartSeconds), maxTwoLines = blocks.All(b => b.Lines.Count <= 2), blockCount = blocks.Count }, JsonOptions), cancellationToken);
        outputs.Add(NormalizePath(diagnosticsPath));
        return outputs;
    }

    private static IReadOnlyList<SubtitleBlock> BuildSubtitleBlocks(IReadOnlyList<VideoTtsSceneTimingDto> scenes)
    {
        var blocks = new List<SubtitleBlock>();
        var number = 1;
        foreach (var scene in scenes)
        {
            var sentences = Regex.Split(scene.Narration ?? string.Empty, @"(?<=[.!?])\s+").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (sentences.Length == 0) sentences = [scene.Narration ?? string.Empty];
            var duration = Math.Max(0.1, scene.EndSeconds - scene.StartSeconds);
            for (var i = 0; i < sentences.Length; i++)
            {
                var start = scene.StartSeconds + duration * i / sentences.Length;
                var end = i == sentences.Length - 1 ? scene.EndSeconds : scene.StartSeconds + duration * (i + 1) / sentences.Length;
                blocks.Add(new SubtitleBlock(number++, Math.Round(start, 3), Math.Round(end, 3), WrapSubtitle(sentences[i])));
            }
        }
        return blocks;
    }

    private static IReadOnlyList<string> WrapSubtitle(string text)
    {
        var words = Regex.Replace(text ?? string.Empty, "\\s+", " ").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 7) return [string.Join(' ', words)];
        var mid = (int)Math.Ceiling(words.Length / 2.0);
        return [string.Join(' ', words.Take(mid)), string.Join(' ', words.Skip(mid))];
    }

    private static string BuildSrt(IReadOnlyList<SubtitleBlock> blocks)
    {
        var builder = new StringBuilder();
        foreach (var block in blocks)
        {
            builder.AppendLine(block.Number.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine($"{FormatSrtTime(block.StartSeconds)} --> {FormatSrtTime(block.EndSeconds)}");
            foreach (var line in block.Lines.Take(2)) builder.AppendLine(line);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string BuildAss(IReadOnlyList<SubtitleBlock> blocks)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine("PlayResX: 1920");
        builder.AppendLine("PlayResY: 1080");
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        builder.AppendLine("Style: Documentary,Arial,42,&H00FFFFFF,&H000000FF,&H66000000,&H99000000,0,0,0,0,100,100,0,0,4,1,0,2,90,90,70,1");
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (var block in blocks) builder.AppendLine($"Dialogue: 0,{FormatAssTime(block.StartSeconds)},{FormatAssTime(block.EndSeconds)},Documentary,,0,0,0,,{string.Join(@"\N", block.Lines.Take(2)).Replace(",", "，")}");
        return builder.ToString();
    }

    private static string FormatSrtTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);
    private static string FormatAssTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"h\:mm\:ss\.ff", CultureInfo.InvariantCulture);
    private sealed record SubtitleBlock(int Number, double StartSeconds, double EndSeconds, IReadOnlyList<string> Lines);

    private VideoTtsTimingsDto BuildVideoTtsTimings(VideoAssemblyGenerationRequest request, VideoNarrationScriptDto script, string audioPath, double actualDurationSeconds, string ttsProvider, string voiceUsed, VideoTtsAudioValidationDto audioValidation)
    {
        var estimatedDurationSeconds = script.TotalEstimatedDurationSeconds;
        var sceneTimings = new List<VideoTtsSceneTimingDto>();
        var estimatedSceneTotal = script.SceneScripts.Sum(scene => scene.DurationSeconds);
        var cursor = 0.0;

        for (var index = 0; index < script.SceneScripts.Count; index++)
        {
            var scene = script.SceneScripts[index];
            var sceneDuration = estimatedSceneTotal <= 0
                ? actualDurationSeconds / script.SceneScripts.Count
                : actualDurationSeconds * scene.DurationSeconds / estimatedSceneTotal;
            var startSeconds = Math.Round(cursor, 3, MidpointRounding.AwayFromZero);
            cursor = index == script.SceneScripts.Count - 1 ? actualDurationSeconds : cursor + sceneDuration;
            var endSeconds = Math.Round(cursor, 3, MidpointRounding.AwayFromZero);
            sceneTimings.Add(new VideoTtsSceneTimingDto(scene.SceneKey, startSeconds, endSeconds, scene.Narration));
        }

        return new VideoTtsTimingsDto(
            request.EventId,
            request.RegionId,
            request.Language,
            request.Platform,
            NormalizePath(audioPath),
            estimatedDurationSeconds,
            actualDurationSeconds,
            sceneTimings,
            ttsProvider,
            voiceUsed,
            DateTimeOffset.UtcNow,
            audioValidation,
            BuildDurationValidation(ResolveRequestProfile(request), actualDurationSeconds, useTargetRange: false, "actualDurationSeconds"));
    }

    private double NormalizeTtsDuration(ScenePresentationProfile profile, double estimatedDurationSeconds)
    {
        var contract = ResolveDurationProfile(profile);
        return Math.Round(Math.Clamp(estimatedDurationSeconds, contract.AcceptableDurationSecondsMin, contract.AcceptableDurationSecondsMax), 3, MidpointRounding.AwayFromZero);
    }

    private double ResolveShortFormTargetDuration(VideoAssemblyGenerationRequest request)
    {
        var contract = ResolveDurationProfile(ScenePresentationProfile.ShortForm);
        var requested = request.ShortForm?.TargetDurationSeconds ?? 0;
        var fallback = contract.TargetDurationSecondsMin;
        return Math.Clamp(requested > 0 ? requested : fallback, contract.TargetDurationSecondsMin, contract.TargetDurationSecondsMax);
    }

    private double ResolveLongFormTargetDuration(VideoAssemblyGenerationRequest request)
    {
        var contract = ResolveDurationProfile(ScenePresentationProfile.LongForm);
        var requested = request.LongForm?.TargetDurationSeconds ?? 0;
        var fallback = contract.TargetDurationSecondsMax;
        return Math.Clamp(requested > 0 ? requested : fallback, contract.TargetDurationSecondsMin, contract.TargetDurationSecondsMax);
    }

    private async Task WriteTtsAudioAsync(string narrationText, string audioPath, double durationSeconds, TtsProviderSelection provider, CancellationToken cancellationToken)
    {
        if (provider.ProviderName == AzureTtsProviderName)
        {
            if (azureSpeechClient is null || azureSpeechOptions is null)
                throw new InvalidOperationException("Azure Speech TTS provider is not available.");

            var audioBytes = await azureSpeechClient.SynthesizeMp3Async(narrationText, azureSpeechOptions.Value, cancellationToken);
            await File.WriteAllBytesAsync(audioPath, audioBytes, cancellationToken);
            return;
        }

        if (provider.ProviderName == OpenAiTtsProviderName)
        {
            var audioBytes = await SynthesizeOpenAiMp3Async(narrationText, provider.VoiceUsed, cancellationToken);
            await File.WriteAllBytesAsync(audioPath, audioBytes, cancellationToken);
            return;
        }

        if (await TryWriteSilentMp3WithFfmpegAsync(audioPath, durationSeconds, cancellationToken))
            return;

        await File.WriteAllBytesAsync(audioPath, BuildFallbackMp3Placeholder(narrationText, durationSeconds), cancellationToken);
    }

    private TtsProviderSelection ResolveTtsProvider(VideoAssemblyGenerationRequest request, VideoNarrationScriptDto script)
    {
        var voice = string.IsNullOrWhiteSpace(script.TtsPlan.RecommendedVoice) ? "NeutralEnergetic" : script.TtsPlan.RecommendedVoice;
        if (request.DryRun || request.AllowSyntheticSilentTts)
            return new TtsProviderSelection(SyntheticTtsProviderName, voice, true);

        if (IsAzureSpeechConfigured(azureSpeechOptions?.Value))
            return new TtsProviderSelection(AzureTtsProviderName, ResolveAzureVoice(script.FullNarrationText), false);

        if (IsOpenAiTtsConfigured())
            return new TtsProviderSelection(OpenAiTtsProviderName, Environment.GetEnvironmentVariable("OPENAI_TTS_VOICE") ?? "alloy", false);

        throw new InvalidOperationException("Real TTS provider is not configured. SyntheticOfflineTtsV1 is disabled for dryRun=false.");
    }

    private string ResolveAzureVoice(string narrationText)
    {
        if (azureSpeechOptions is null)
            return "";

        var language = DetectNarrationLanguage(narrationText);
        return azureSpeechOptions.Value.GetPreferredVoices(language).FirstOrDefault() ?? azureSpeechOptions.Value.DefaultVoiceName ?? "";
    }

    private static bool IsAzureSpeechConfigured(AzureSpeechOptions? options)
    {
        if (options is null)
            return false;

        if (options.UseManagedIdentity)
            return !string.IsNullOrWhiteSpace(options.Region) && !string.IsNullOrWhiteSpace(options.ResourceId);

        return !string.IsNullOrWhiteSpace(options.Key)
            && (!string.IsNullOrWhiteSpace(options.Region) || !string.IsNullOrWhiteSpace(options.Endpoint));
    }

    private static bool IsOpenAiTtsConfigured()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

    private static bool IsSyntheticProvider(string provider)
        => string.Equals(provider, SyntheticTtsProviderName, StringComparison.OrdinalIgnoreCase);

    private static string DetectNarrationLanguage(string text)
        => text.Any(ch => ch >= '\u0900' && ch <= '\u097F') ? "hi" : "en";

    private static async Task<byte[]> SynthesizeOpenAiMp3Async(string narrationText, string voice, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI TTS configuration is missing OPENAI_API_KEY.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var endpoint = Environment.GetEnvironmentVariable("OPENAI_TTS_ENDPOINT") ?? "https://api.openai.com/v1/audio/speech";
        var model = Environment.GetEnvironmentVariable("OPENAI_TTS_MODEL") ?? "gpt-4o-mini-tts";
        var payload = JsonSerializer.Serialize(new { model, voice, input = narrationText, response_format = "mp3" }, JsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"OpenAI TTS synthesis failed. Status={(int)response.StatusCode}; Details={error}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<bool> TryWriteSilentMp3WithFfmpegAsync(string audioPath, double durationSeconds, CancellationToken cancellationToken)
    {
        var ffmpegPath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfmpegPath) ? "ffmpeg" : renderingOptions.Value.FfmpegPath;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("lavfi");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add("anullsrc=channel_layout=mono:sample_rate=24000");
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add(durationSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-codec:a");
            startInfo.ArgumentList.Add("libmp3lame");
            startInfo.ArgumentList.Add("-b:a");
            startInfo.ArgumentList.Add("96k");
            startInfo.ArgumentList.Add(audioPath);

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 && File.Exists(audioPath) && new FileInfo(audioPath).Length > 0;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<VideoTtsAudioValidationDto> ValidateMp3AudioAsync(string audioPath, bool enforceNonSilent, CancellationToken cancellationToken)
    {
        if (!File.Exists(audioPath))
            return new VideoTtsAudioValidationDto(true, -120, -120, false, 0, 0);

        var fileSizeBytes = new FileInfo(audioPath).Length;
        if (fileSizeBytes < MinimumMp3FileSizeBytes)
            return new VideoTtsAudioValidationDto(true, -120, -120, false, 0, fileSizeBytes);

        var duration = await ProbeDurationSecondsAsync(audioPath, cancellationToken);
        if (duration <= 0)
            return new VideoTtsAudioValidationDto(true, -120, -120, false, 0, fileSizeBytes);

        var (peakDb, rmsDb) = await ProbeAudioLevelsAsync(audioPath, cancellationToken);
        var isSilent = double.IsNegativeInfinity(peakDb)
            || double.IsNegativeInfinity(rmsDb)
            || peakDb < SilencePeakThresholdDb
            || rmsDb < SilenceRmsThresholdDb;
        var passed = enforceNonSilent ? !isSilent : true;
        return new VideoTtsAudioValidationDto(isSilent, RoundDb(peakDb), RoundDb(rmsDb), passed, Math.Round(duration, 3, MidpointRounding.AwayFromZero), fileSizeBytes);
    }

    private async Task<double> ProbeDurationSecondsAsync(string audioPath, CancellationToken cancellationToken)
    {
        var ffprobePath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfprobePath) ? "ffprobe" : renderingOptions.Value.FfprobePath;
        var result = await RunProcessAsync(ffprobePath,
            ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", audioPath],
            cancellationToken);
        return result.ExitCode == 0 && double.TryParse(result.Output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
            ? duration
            : 0;
    }

    private async Task<(double PeakDb, double RmsDb)> ProbeAudioLevelsAsync(string audioPath, CancellationToken cancellationToken)
    {
        var ffmpegPath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfmpegPath) ? "ffmpeg" : renderingOptions.Value.FfmpegPath;
        var result = await RunProcessAsync(ffmpegPath, ["-hide_banner", "-i", audioPath, "-af", "astats=metadata=1:reset=0", "-f", "null", "-"], cancellationToken);
        var output = result.Output + "\n" + result.Error;
        return (ParseLastDbValue(output, "Peak level dB"), ParseLastDbValue(output, "RMS level dB"));
    }

    private static double ParseLastDbValue(string output, string label)
    {
        var value = double.NegativeInfinity;
        foreach (var line in output.Split('\n'))
        {
            var index = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            var colon = line.IndexOf(':', index);
            if (colon < 0)
                continue;

            var raw = line[(colon + 1)..].Trim();
            if (raw.Equals("-inf", StringComparison.OrdinalIgnoreCase))
                value = double.NegativeInfinity;
            else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                value = parsed;
        }

        return value;
    }

    private async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return new ProcessResult(-1, string.Empty, string.Empty);

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProcessResult(-1, string.Empty, string.Empty);
        }
    }

    private static double RoundDb(double value)
        => double.IsNegativeInfinity(value) || double.IsNaN(value) ? -120 : double.IsPositiveInfinity(value) ? 0 : Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private static byte[] BuildFallbackMp3Placeholder(string narrationText, double durationSeconds)
    {
        var comment = $"Synthetic offline TTS placeholder. DurationSeconds={durationSeconds:0.###}. Narration={narrationText}";
        var payload = Encoding.UTF8.GetBytes(comment);
        var size = payload.Length;
        var header = new byte[]
        {
            (byte)'I', (byte)'D', (byte)'3', 4, 0, 0,
            (byte)((size >> 21) & 0x7F),
            (byte)((size >> 14) & 0x7F),
            (byte)((size >> 7) & 0x7F),
            (byte)(size & 0x7F)
        };

        return header.Concat(payload).ToArray();
    }


    private async Task<AssemblyInputs> EnsureRequiredAssemblyInputsAsync(string eventId, string regionId, string platform, ScenePresentationProfile presentationProfile, CancellationToken cancellationToken)
    {
        var intelligencePath = BuildVideoAssemblyIntelligenceOutputPath(eventId, regionId, presentationProfile);
        var scriptPath = BuildVideoNarrationScriptOutputPath(eventId, regionId, presentationProfile);
        var audioPath = BuildVideoTtsAudioOutputPath(eventId, regionId, presentationProfile);
        var timingsPath = BuildVideoTtsTimingsOutputPath(eventId, regionId, presentationProfile);
        var thumbnailPath = BuildThumbnailLandscapeOutputPath(eventId, regionId);
        var sceneApprovalRoot = Path.Combine(BuildQuestionEngineRoot(eventId, regionId), SceneApprovalDirectoryName, ResolveSceneApprovalProfileDirectoryName(presentationProfile));

        if (!File.Exists(intelligencePath))
            throw new ArgumentException($"Required video assembly input '{ResolveVideoAssemblyIntelligenceFileName(presentationProfile)}' was not found at '{NormalizePath(intelligencePath)}'.");
        if (!File.Exists(scriptPath))
            throw new ArgumentException($"Required video assembly input '{ResolveVideoNarrationScriptFileName(presentationProfile)}' was not found at '{NormalizePath(scriptPath)}'.");
        if (!File.Exists(audioPath))
            throw new ArgumentException($"Required video assembly input '{ResolveVideoTtsAudioFileName(presentationProfile)}' was not found at '{NormalizePath(audioPath)}'.");
        if (!File.Exists(timingsPath))
            throw new ArgumentException($"Required video assembly input '{ResolveVideoTtsTimingsFileName(presentationProfile)}' was not found at '{NormalizePath(timingsPath)}'.");
        var intelligence = JsonSerializer.Deserialize<VideoAssemblyIntelligenceDto>(await File.ReadAllTextAsync(intelligencePath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Required video assembly input '{ResolveVideoAssemblyIntelligenceFileName(presentationProfile)}' could not be parsed.");
        var script = JsonSerializer.Deserialize<VideoNarrationScriptDto>(await File.ReadAllTextAsync(scriptPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Required video assembly input '{ResolveVideoNarrationScriptFileName(presentationProfile)}' could not be parsed.");
        var timings = await ReadVideoTtsTimingsForAssemblyAsync(timingsPath, presentationProfile, cancellationToken);

        ValidateVideoAssemblyIntelligence(intelligence);
        ValidateVideoNarrationScript(script);
        ValidateVideoTtsTimings(timings);
        ValidateSceneTimingOrder(timings.SceneTimings.Select(scene => scene.SceneKey), presentationProfile, ResolveVideoTtsTimingsFileName(presentationProfile));
        if (presentationProfile == ScenePresentationProfile.LongForm && timings.SceneTimings.Count != LongFormSectionOrder.Length)
            throw new ArgumentException("Video assembly validation failed: LongForm section count must be 13.");

        if (!Directory.Exists(sceneApprovalRoot))
            throw new ArgumentException($"Required video assembly scene directory was not found at '{NormalizePath(sceneApprovalRoot)}'.");

        var sceneOrder = presentationProfile == ScenePresentationProfile.ShortForm ? RequiredAssemblySceneOrder : LongFormSectionOrder;
        var visualAssetPaths = sceneOrder
            .Select(sceneKey => ResolveAssemblyVisualAssetPath(sceneKey, thumbnailPath, sceneApprovalRoot, presentationProfile))
            .ToArray();
        var missingVisualAssets = visualAssetPaths.Where(path => !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missingVisualAssets.Length > 0)
            throw new ArgumentException($"Required video assembly visual asset(s) were not found: {string.Join(", ", missingVisualAssets)}.");

        var durationMatchesAudio = Math.Abs(timings.SceneTimings[^1].EndSeconds - timings.ActualDurationSeconds) <= 0.001;
        if (!durationMatchesAudio)
            throw new ArgumentException($"Video assembly validation failed: TTS scene timings end at {timings.SceneTimings[^1].EndSeconds:0.###} seconds, but actual TTS duration is {timings.ActualDurationSeconds:0.###} seconds.");

        return new AssemblyInputs(intelligence, script, timings, NormalizePath(audioPath), NormalizePath(thumbnailPath), NormalizeDirectoryPath(sceneApprovalRoot), presentationProfile, visualAssetPaths.Select(NormalizePath).ToArray());
    }

    private async Task<VideoTtsTimingsDto> ReadVideoTtsTimingsForAssemblyAsync(string timingsPath, ScenePresentationProfile presentationProfile, CancellationToken cancellationToken)
    {
        if (presentationProfile == ScenePresentationProfile.LongForm)
        {
            var longForm = await ReadLongFormVideoTtsTimingsAsync(timingsPath, cancellationToken);
            ValidateLongFormVideoTtsTimings(longForm);
            return new VideoTtsTimingsDto(
                longForm.EventId,
                longForm.RegionId,
                longForm.Language,
                longForm.Platform,
                longForm.AudioFilePath,
                longForm.EstimatedDurationSeconds,
                longForm.ActualDurationSeconds,
                longForm.SectionTimings.Select(section => new VideoTtsSceneTimingDto(section.SectionKey, section.StartSeconds, section.EndSeconds, section.Narration)).ToArray(),
                longForm.TtsProvider,
                longForm.VoiceUsed,
                longForm.GeneratedUtc,
                longForm.AudioValidation);
        }

        return JsonSerializer.Deserialize<VideoTtsTimingsDto>(await File.ReadAllTextAsync(timingsPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Required video assembly input '{ResolveVideoTtsTimingsFileName(presentationProfile)}' could not be parsed.");
    }

    private VideoAssemblyPlanDto BuildVideoAssemblyPlan(VideoAssemblyGenerationRequest request, AssemblyInputs inputs)
    {
        var sceneOrder = inputs.ScenePresentationProfile == ScenePresentationProfile.ShortForm ? RequiredAssemblySceneOrder : LongFormSectionOrder;
        var visualByScene = sceneOrder.Zip(inputs.VisualAssetPaths, (scene, path) => new { scene, path })
            .ToDictionary(item => item.scene, item => item.path, StringComparer.OrdinalIgnoreCase);
        var segments = inputs.Timings.SceneTimings.Select((timing, index) =>
        {
            var duration = Math.Round(timing.EndSeconds - timing.StartSeconds, 3, MidpointRounding.AwayFromZero);
            return new VideoAssemblyPlanSegmentDto(
                timing.SceneKey,
                timing.StartSeconds,
                timing.EndSeconds,
                duration,
                visualByScene[timing.SceneKey],
                timing.Narration,
                index == 0 ? "None" : "CrossFade",
                index == inputs.Timings.SceneTimings.Count - 1 ? "None" : "CrossFade",
                ResolveSegmentMotion(timing.SceneKey, inputs.ScenePresentationProfile));
        }).ToArray();

        var renderSettings = inputs.ScenePresentationProfile == ScenePresentationProfile.ShortForm
            ? new VideoAssemblyRenderSettingsDto(1080, 1920, 30, "mp4", "h264", "aac")
            : new VideoAssemblyRenderSettingsDto(1920, 1080, 30, "mp4", "h264", "aac");
        var validation = new VideoAssemblyValidationDto(true, true, segments.Length, true, true);
        return new VideoAssemblyPlanDto(
            request.EventId,
            request.RegionId,
            request.Language,
            request.Platform,
            inputs.ScenePresentationProfile,
            inputs.SceneApprovalRoot,
            inputs.VisualAssetPaths.Count,
            inputs.VisualAssetPaths,
            inputs.Timings.ActualDurationSeconds,
            inputs.AudioPath,
            NormalizePath(Path.Combine(Path.GetDirectoryName(inputs.AudioPath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty, ResolveFinalVideoFileName(inputs.ScenePresentationProfile))),
            segments,
            renderSettings,
            true,
            new VideoAssemblyStyleDto("CrossFade", "SubtleKenBurns", "UseExistingSceneTextOnly", true),
            validation,
            BuildSceneMappingValidation(segments, inputs.ScenePresentationProfile),
            BuildRenderMusicPlan(request),
            [],
            DateTimeOffset.UtcNow,
            BuildDurationValidation(inputs.ScenePresentationProfile, inputs.Timings.ActualDurationSeconds, useTargetRange: false, "actualDurationSeconds"));
    }

    private static VideoAssemblySceneMappingValidationDto BuildSceneMappingValidation(IReadOnlyList<VideoAssemblyPlanSegmentDto> segments, ScenePresentationProfile profile)
    {
        var visualByScene = segments.ToDictionary(segment => segment.SceneKey, segment => NormalizePath(segment.VisualAssetPath), StringComparer.OrdinalIgnoreCase);
        if (profile == ScenePresentationProfile.LongForm)
        {
            var hookUsesScene001 = SegmentUsesScene(visualByScene, "Hook", "scene-001-final.png");
            var whatUsesScene001 = SegmentUsesScene(visualByScene, "WhatIsHappening", "scene-001-final.png")
                && SegmentUsesScene(visualByScene, "WhatYouWillSee", "scene-001-final.png");
            var whyUsesScene005 = SegmentUsesScene(visualByScene, "WhyItMatters", "scene-005-final.png")
                && SegmentUsesScene(visualByScene, "InterestingFact", "scene-005-final.png");
            var whereUsesScene002 = SegmentUsesScene(visualByScene, "WhereToLook", "scene-002-final.png");
            var whenUsesScene003 = SegmentUsesScene(visualByScene, "WhenToLook", "scene-003-final.png")
                && SegmentUsesScene(visualByScene, "Recap", "scene-003-final.png");
            var actionUsesScene006 = SegmentUsesScene(visualByScene, "Action", "scene-006-final.png");
            return new VideoAssemblySceneMappingValidationDto(
                hookUsesScene001,
                whatUsesScene001,
                whyUsesScene005,
                whereUsesScene002,
                whenUsesScene003,
                actionUsesScene006,
                hookUsesScene001 && whatUsesScene001 && whyUsesScene005 && whereUsesScene002 && whenUsesScene003 && actionUsesScene006);
        }

        var shortHookUsesScene001 = SegmentUsesScene(visualByScene, "Hook", "scene-001-final.png");
        var shortWhatUsesScene001 = SegmentUsesScene(visualByScene, "What", "scene-001-final.png");
        var shortWhyUsesScene005 = SegmentUsesScene(visualByScene, "Why", "scene-005-final.png");
        var shortWhereUsesScene002 = SegmentUsesScene(visualByScene, "Where", "scene-002-final.png");
        var shortWhenUsesScene003 = SegmentUsesScene(visualByScene, "When", "scene-003-final.png");
        var shortActionUsesScene006 = SegmentUsesScene(visualByScene, "Action", "scene-006-final.png");

        return new VideoAssemblySceneMappingValidationDto(
            shortHookUsesScene001,
            shortWhatUsesScene001,
            shortWhyUsesScene005,
            shortWhereUsesScene002,
            shortWhenUsesScene003,
            shortActionUsesScene006,
            shortHookUsesScene001 && shortWhatUsesScene001 && shortWhyUsesScene005 && shortWhereUsesScene002 && shortWhenUsesScene003 && shortActionUsesScene006);
    }

    private static bool SegmentUsesScene(IReadOnlyDictionary<string, string> visualByScene, string sceneKey, string expectedFileName)
        => visualByScene.TryGetValue(sceneKey, out var visualAssetPath)
            && visualAssetPath.EndsWith(expectedFileName, StringComparison.OrdinalIgnoreCase);

    private VideoAssemblyRenderMusicPlanDto BuildRenderMusicPlan(VideoAssemblyGenerationRequest request)
    {
        var backgroundMusicOptions = videoAssemblyOptions?.Value.BackgroundMusic;
        var defaultLevelPercent = ResolveRequestProfile(request) == ScenePresentationProfile.LongForm
            ? 18
            : backgroundMusicOptions?.DefaultLevelPercent ?? 12;
        return new VideoAssemblyRenderMusicPlanDto(
            true,
            string.IsNullOrWhiteSpace(request.MusicMood) ? "WonderCuriosity" : request.MusicMood,
            request.MusicLevelPercent <= 0 ? defaultLevelPercent : request.MusicLevelPercent,
            request.DuckMusicUnderNarration);
    }

    private static string ResolveSegmentMotion(string sceneKey, ScenePresentationProfile profile)
        => profile == ScenePresentationProfile.LongForm
            ? ResolveLongFormSegmentMotion(sceneKey)
            : sceneKey switch
            {
                "Hook" => "HookThumbnailZoomIn100To105",
                "What" => "SubtleKenBurnsZoomTowardVenusJupiter",
                "Why" => "SubtleKenBurnsZoomTowardSignificanceArea",
                "Where" => "SubtleKenBurnsPanTowardWesternHorizon",
                "When" => "SubtleKenBurnsZoomTowardTimeline",
                "Action" => "SubtleKenBurnsZoomTowardCta",
                _ => "SubtleKenBurns"
            };

    private static string ResolveLongFormSegmentMotion(string sectionKey)
        => sectionKey switch
        {
            "Hook" => "SlowZoom",
            "WhatIsHappening" => "SubtleKenBurns",

            "WhyItMatters" => "SlowZoomOut",
            "WhereToLook" => "SlowPan",
            "WhenToLook" => "SubtleKenBurns",
            "HowToObserve" => "SlowZoom",
            "WhatYouWillSee" => "SlowZoomOut",
            "InterestingFact" => "SlowPan",
            "ObservationTips" => "SubtleKenBurns",
            "Recap" => "SlowZoomOut",
            "Action" => "SlowZoom",
            _ => "SubtleKenBurns"
        };

    private void ValidateVideoAssemblyPlan(VideoAssemblyPlanDto plan)
    {
        ValidateSceneTimingOrder(plan.Segments.Select(segment => segment.SceneKey), plan.ScenePresentationProfile, VideoAssemblyPlanFileName);
        if (plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm && plan.Segments.Count != 6)
            throw new ArgumentException("Video assembly validation failed: segmentCount must be 6.");
        if (plan.ScenePresentationProfile == ScenePresentationProfile.LongForm && plan.Segments.Count != LongFormSectionOrder.Length)
            throw new ArgumentException("Video assembly validation failed: LongForm segmentCount must be 13.");
        if (!plan.Validation.AudioExists)
            throw new ArgumentException("Video assembly validation failed: audio is missing.");
        if (!plan.Validation.AllVisualAssetsExist)
            throw new ArgumentException("Video assembly validation failed: one or more visual assets are missing.");
        if (plan.Validation.SegmentCount != plan.Segments.Count)
            throw new ArgumentException("Video assembly validation failed: validation.segmentCount must match segment count.");
        if (!plan.Validation.DurationMatchesAudio)
            throw new ArgumentException("Video assembly validation failed: duration does not match TTS duration.");
        EnsureDurationValidationPassed(plan.DurationValidation ?? BuildDurationValidation(plan.ScenePresentationProfile, plan.TotalDurationSeconds, useTargetRange: false, "actualDurationSeconds"), "Video assembly validation failed");
        if (!plan.Validation.ReadyForRender)
            throw new ArgumentException("Video assembly validation failed: readyForRender must be true.");
        if (plan.SceneMappingValidation is null)
            throw new ArgumentException("Video assembly validation failed: sceneMappingValidation is required.");
        if (!plan.SceneMappingValidation.SceneMappingValid)
            throw new ArgumentException("Video assembly validation failed: sceneMappingValid must be true.");
        var backgroundMusicExpected = plan.BackgroundMusic || plan.Style.BackgroundMusic;
        if (backgroundMusicExpected && plan.RenderMusicPlan is null)
            throw new ArgumentException("Video assembly validation failed: renderMusicPlan is required when backgroundMusic is expected.");
        if (backgroundMusicExpected && !plan.RenderMusicPlan.BackgroundMusic)
            throw new ArgumentException("Video assembly validation failed: renderMusicPlan.backgroundMusic must be true when backgroundMusic is expected.");
        if (plan.ScenePresentationProfile != ResolveScenePresentationProfile(plan.Platform))
            throw new ArgumentException("Video assembly validation failed: scenePresentationProfile must match the requested platform.");
        if (string.Equals(plan.Platform, "YouTubeShort", StringComparison.OrdinalIgnoreCase) && plan.ScenePresentationProfile != ScenePresentationProfile.ShortForm)
            throw new ArgumentException("Video assembly validation failed: YouTubeShort requires scenePresentationProfile ShortForm.");
        if (plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm)
        {
            if (plan.RenderSettings.Width != 1080 || plan.RenderSettings.Height != 1920)
                throw new ArgumentException("Video assembly validation failed: ShortForm renderSettings must be 1080x1920.");
            ValidateShortFormSceneAssetSelection(plan);
        }
        else
        {
            if (plan.RenderSettings.Width != 1920 || plan.RenderSettings.Height != 1080)
                throw new ArgumentException("Video assembly validation failed: LongForm renderSettings must be 1920x1080.");
            var longDirectory = NormalizePath(plan.SceneImageBaseDirectory).TrimEnd('/') + "/";
            if (!longDirectory.EndsWith("/scene-approval-v3/long/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Video assembly validation failed: LongForm sceneImageBaseDirectory must resolve to scene-approval-v3/long/.");
            if (plan.Segments.Any(segment => !IsLongFormDocumentaryMotion(segment.Motion)))
                throw new ArgumentException("Video assembly validation failed: LongForm motion must be subtle documentary motion only.");
        }
        var lastEndSeconds = plan.Segments[^1].EndSeconds;
        if (Math.Abs(lastEndSeconds - plan.TotalDurationSeconds) > 0.001)
            throw new ArgumentException("Video assembly validation failed: totalDurationSeconds must match the last segment endSeconds.");
    }


    private static void ValidateShortFormSceneAssetSelection(VideoAssemblyPlanDto plan)
    {
        var shortDirectory = NormalizePath(plan.SceneImageBaseDirectory).TrimEnd('/') + "/";
        if (!shortDirectory.EndsWith("/scene-approval-v3/short/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Video assembly validation failed: ShortForm sceneImageBaseDirectory must resolve to scene-approval-v3/short/.");
        if (plan.SceneCount != 6 || plan.SceneImages.Count != 6)
            throw new ArgumentException("Video assembly validation failed: ShortForm sceneCount must be 6.");
        if (plan.Segments.Count != 6)
            throw new ArgumentException("Video assembly validation failed: ShortForm segment count must be 6.");

        var longScenePaths = plan.SceneImages.Concat(plan.Segments.Select(segment => segment.VisualAssetPath))
            .Where(IsLongSceneApprovalPath)
            .ToArray();
        if (longScenePaths.Length > 0)
            throw new ArgumentException($"Video assembly validation failed: ShortForm render cannot use long scene assets: {string.Join(", ", longScenePaths)}.");

        if (!plan.SceneImages.All(path => IsShortSceneApprovalPath(path))
            || !plan.Segments.All(segment => IsShortSceneApprovalPath(segment.VisualAssetPath)))
            throw new ArgumentException("Video assembly validation failed: ShortForm render must use only scene-approval-v3/short/ assets.");

        var allSegmentPathsContainShortDirectory = plan.Segments.All(segment => NormalizePath(segment.VisualAssetPath).Contains("/short/", StringComparison.OrdinalIgnoreCase));
        if (!allSegmentPathsContainShortDirectory)
            throw new ArgumentException("Video assembly validation failed: ShortForm visualAssetPath values must contain /short/.");
    }

    private static bool IsLongFormDocumentaryMotion(string motion)
        => motion is "SubtleKenBurns" or "SlowPan" or "SlowZoom" or "SlowZoomOut";

    private static bool IsShortSceneApprovalPath(string path)
        => NormalizePath(path).Contains("/scene-approval-v3/short/", StringComparison.OrdinalIgnoreCase);

    private static bool IsLongSceneApprovalPath(string path)
        => NormalizePath(path).Contains("/scene-approval-v3/long/", StringComparison.OrdinalIgnoreCase);


    private static void EnsureVideoAssemblyPlanAssetsExist(VideoAssemblyPlanDto plan)
    {
        if (!File.Exists(plan.AudioFilePath))
            throw new ArgumentException($"Video assembly validation failed: audio is missing at '{plan.AudioFilePath}'.");

        var missingVisualAssets = plan.SceneImages
            .Concat(plan.Segments.Select(segment => segment.VisualAssetPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !File.Exists(path))
            .ToArray();
        if (missingVisualAssets.Length > 0)
            throw new ArgumentException($"Video assembly validation failed: visual asset(s) missing at {string.Join(", ", missingVisualAssets)}.");
    }



    private async Task RenderFinalVideoAsync(VideoAssemblyPlanDto plan, VideoAssemblyRenderMusicPlanDto renderMusicPlan, string finalOutputPath, CancellationToken cancellationToken)
    {
        var outputPath = finalOutputPath.Replace('/', Path.DirectorySeparatorChar);
        var outputDirectory = Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot();
        Directory.CreateDirectory(outputDirectory);
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        var tempDirectory = Path.Combine(outputDirectory, "render-temp");
        Directory.CreateDirectory(tempDirectory);
        var segmentPaths = new List<string>();
        try
        {
            for (var index = 0; index < plan.Segments.Count; index++)
            {
                var segment = plan.Segments[index];
                var segmentPath = Path.Combine(tempDirectory, $"segment-{segmentPaths.Count + 1:000}.mp4");
                var transitionPadding = index == 0 ? 0 : ResolveCrossFadeDurationSeconds(plan.ScenePresentationProfile);
                await RenderVisualSegmentAsync(segment, plan.RenderSettings, segmentPath, segment.DurationSeconds + transitionPadding, cancellationToken);
                segmentPaths.Add(segmentPath);
            }

            var silentVideoPath = Path.Combine(tempDirectory, "visual-track.mp4");
            await RenderCrossFadedVisualTrackAsync(segmentPaths, plan, silentVideoPath, cancellationToken);

            var subtitlePath = ResolveBurnInSubtitlePath(plan);
            var finalArgs = BuildFinalMuxArguments(silentVideoPath, plan.AudioFilePath, outputPath, plan.TotalDurationSeconds, renderMusicPlan, subtitlePath);
            var muxOperation = renderMusicPlan.BackgroundMusic
                ? "mux rendered video with narration audio and background music"
                : "mux rendered video with narration audio";
            await RunFfmpegOrThrowAsync(finalArgs, muxOperation, cancellationToken);
        }
        finally
        {
            if (!renderingOptions.Value.KeepIntermediateFiles && Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private async Task RenderVisualSegmentAsync(VideoAssemblyPlanSegmentDto segment, VideoAssemblyRenderSettingsDto renderSettings, string segmentPath, double renderDurationSeconds, CancellationToken cancellationToken)
    {
        var duration = Math.Max(0.1, renderDurationSeconds);
        var frameCount = Math.Max(1, (int)Math.Ceiling(duration * renderSettings.Fps));
        var escapedFrameCount = frameCount.ToString(CultureInfo.InvariantCulture);
        var zoomTarget = ResolveKenBurnsZoomTarget(segment).ToString("0.###", CultureInfo.InvariantCulture);
        var zoomExpression = segment.Motion.Equals("SlowZoomOut", StringComparison.OrdinalIgnoreCase)
            ? $"max(1.0,{zoomTarget}-({zoomTarget}-1.0)*on/{escapedFrameCount})"
            : $"min(1.0+({zoomTarget}-1.0)*on/{escapedFrameCount},{zoomTarget})";
        var pan = ResolveKenBurnsPan(segment);
        var panX = pan.X.ToString("0.###", CultureInfo.InvariantCulture);
        var panY = pan.Y.ToString("0.###", CultureInfo.InvariantCulture);
        var vf = string.Join(',',
            $"scale={renderSettings.Width}:{renderSettings.Height}:force_original_aspect_ratio=increase",
            $"crop={renderSettings.Width}:{renderSettings.Height}",
            $"zoompan=z='{zoomExpression}':d=1:x='iw/2-(iw/zoom/2)+(iw*{panX})*on/{escapedFrameCount}':y='ih/2-(ih/zoom/2)+(ih*{panY})*on/{escapedFrameCount}':s={renderSettings.Width}x{renderSettings.Height}:fps={renderSettings.Fps}",
            $"trim=duration={duration.ToString("0.###", CultureInfo.InvariantCulture)}",
            "format=yuv420p");

        await RunFfmpegOrThrowAsync([
            "-hide_banner", "-y", "-loop", "1", "-framerate", renderSettings.Fps.ToString(CultureInfo.InvariantCulture), "-t", duration.ToString("0.###", CultureInfo.InvariantCulture), "-i", segment.VisualAssetPath,
            "-vf", vf,
            "-an", "-c:v", "libx264", "-preset", renderingOptions.Value.ShortsPreset, "-crf", renderingOptions.Value.ShortsCrf.ToString(CultureInfo.InvariantCulture),
            "-r", renderSettings.Fps.ToString(CultureInfo.InvariantCulture), "-movflags", "+faststart", segmentPath
        ], $"render visual segment {segment.SceneKey}", cancellationToken);
    }

    private async Task RenderCrossFadedVisualTrackAsync(IReadOnlyList<string> segmentPaths, VideoAssemblyPlanDto plan, string silentVideoPath, CancellationToken cancellationToken)
    {
        if (segmentPaths.Count == 1)
        {
            File.Copy(segmentPaths[0], silentVideoPath, overwrite: true);
            return;
        }

        var args = new List<string> { "-hide_banner", "-y" };
        foreach (var segmentPath in segmentPaths)
            args.AddRange(["-i", segmentPath]);

        var filterParts = new List<string>();
        var crossFadeDurationSeconds = ResolveCrossFadeDurationSeconds(plan.ScenePresentationProfile);
        var accumulatedDuration = plan.Segments[0].DurationSeconds;
        var previousLabel = "[0:v]";
        for (var index = 1; index < segmentPaths.Count; index++)
        {
            var outputLabel = index == segmentPaths.Count - 1 ? "[vout]" : $"[v{index}]";
            var offset = Math.Max(0, accumulatedDuration - crossFadeDurationSeconds).ToString("0.###", CultureInfo.InvariantCulture);
            filterParts.Add($"{previousLabel}[{index}:v]xfade=transition=fade:duration={crossFadeDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}:offset={offset}{outputLabel}");
            previousLabel = outputLabel;
            accumulatedDuration += plan.Segments[index].DurationSeconds;
        }

        args.AddRange([
            "-filter_complex", string.Join(';', filterParts),
            "-map", "[vout]", "-an", "-c:v", "libx264", "-preset", renderingOptions.Value.ShortsPreset,
            "-crf", renderingOptions.Value.ShortsCrf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p",
            "-r", plan.RenderSettings.Fps.ToString(CultureInfo.InvariantCulture), "-movflags", "+faststart", silentVideoPath
        ]);
        await RunFfmpegOrThrowAsync(args, "crossfade rendered video segments", cancellationToken);
    }

    private static double ResolveCrossFadeDurationSeconds(ScenePresentationProfile profile)
        => profile == ScenePresentationProfile.LongForm ? LongFormCrossFadeDurationSeconds : ShortFormCrossFadeDurationSeconds;

    private static double ResolveKenBurnsZoomTarget(VideoAssemblyPlanSegmentDto segment)
        => segment.Motion switch
        {
            "SlowZoomOut" => 1.04,
            "SlowPan" => 1.025,
            "SlowZoom" => 1.04,
            "SubtleKenBurns" => 1.035,
            _ when segment.SceneKey.Equals("Hook", StringComparison.OrdinalIgnoreCase) => 1.05,
            _ when segment.SceneKey.Equals("What", StringComparison.OrdinalIgnoreCase) => 1.055,
            _ when segment.SceneKey.Equals("Why", StringComparison.OrdinalIgnoreCase) => 1.04,
            _ when segment.SceneKey.Equals("Where", StringComparison.OrdinalIgnoreCase) => 1.035,
            _ when segment.SceneKey.Equals("When", StringComparison.OrdinalIgnoreCase) => 1.045,
            _ when segment.SceneKey.Equals("Action", StringComparison.OrdinalIgnoreCase) => 1.05,
            _ => 1.04
        };

    private static (double X, double Y) ResolveKenBurnsPan(VideoAssemblyPlanSegmentDto segment)
        => segment.Motion switch
        {
            "SlowPan" => ResolveDocumentaryPan(segment.SceneKey),
            "SlowZoomOut" => (0.0, 0.0),
            _ when segment.SceneKey.Equals("Where", StringComparison.OrdinalIgnoreCase) => (-0.012, 0.004),
            _ when segment.SceneKey.Equals("When", StringComparison.OrdinalIgnoreCase) => (0.006, 0.0),
            _ when segment.SceneKey.Equals("Action", StringComparison.OrdinalIgnoreCase) => (0.0, -0.006),
            _ when segment.SceneKey.Equals("Why", StringComparison.OrdinalIgnoreCase) => (0.004, -0.004),
            _ when segment.SceneKey.Equals("What", StringComparison.OrdinalIgnoreCase) => (0.006, -0.002),
            _ => (0.0, 0.0)
        };

    private static (double X, double Y) ResolveDocumentaryPan(string sceneKey)
        => (Math.Abs(sceneKey.Aggregate(0, (sum, ch) => sum + ch)) % 4) switch
        {
            0 => (-0.010, 0.003),
            1 => (0.010, -0.003),
            2 => (0.006, 0.004),
            _ => (-0.006, -0.004)
        };

    private IReadOnlyList<string> BuildFinalMuxArguments(string silentVideoPath, string narrationAudioPath, string outputPath, double durationSeconds, VideoAssemblyRenderMusicPlanDto renderMusicPlan, string? subtitlePath = null)
    {
        var args = new List<string> { "-hide_banner", "-y", "-i", silentVideoPath, "-i", narrationAudioPath };
        var backgroundMusicSource = ResolveBackgroundMusicSource(renderMusicPlan);
        if (renderMusicPlan.BackgroundMusic)
        {
            if (!backgroundMusicSource.Found)
            {
                args.AddRange(["-map", "0:v:0", "-map", "1:a:0"]);
            }
            else
            {
                args.AddRange(["-stream_loop", "-1", "-i", backgroundMusicSource.Path]);

                var audioFilter = BuildFfmpegAudioFilter(renderMusicPlan);
                args.AddRange(["-filter_complex", audioFilter, "-map", "0:v:0", "-map", "[aout]"]);
            }

        }
        else
        {
            args.AddRange(["-map", "0:v:0", "-map", "1:a:0"]);
        }

        if (!string.IsNullOrWhiteSpace(subtitlePath))
            args.AddRange(["-vf", $"subtitles=\'{EscapeSubtitleFilterPath(subtitlePath)}\'"]);

        args.AddRange([
            "-c:v", "libx264", "-preset", renderingOptions.Value.ShortsPreset, "-crf", renderingOptions.Value.ShortsCrf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p", "-r", "30", "-c:a", "aac", "-b:a", renderingOptions.Value.ShortsAudioBitrate,
            "-shortest", "-movflags", "+faststart", outputPath
        ]);
        return args;
    }


    private string? ResolveBurnInSubtitlePath(VideoAssemblyPlanDto plan)
    {
        var subtitleOptions = videoAssemblyOptions?.Value.Subtitles ?? new VideoAssemblySubtitleOptions();
        if (!subtitleOptions.Enabled || !subtitleOptions.EnableSubtitles || !subtitleOptions.BurnIn)
            return null;
        var folder = plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm ? "short" : "long";
        var fileStem = plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm ? "short" : "long";
        var srtPath = Path.Combine(BuildVideoAssemblyRoot(plan.EventId, plan.RegionId), "subtitles", folder, fileStem + ".srt");
        return File.Exists(srtPath) ? srtPath : null;
    }

    private static string EscapeSubtitleFilterPath(string path)
        => path.Replace("\\", "/").Replace("'", "\\'").Replace(":", "\\:");


    private static VideoAssemblyRenderMusicPlanDto ResolveRenderMusicPlan(VideoAssemblyRenderMusicPlanDto planRenderMusicPlan, VideoAssemblyGenerationRequest request)
    {
        var requestedMusicLevelPercent = request.MusicLevelPercent <= 0
            ? planRenderMusicPlan.MusicLevelPercent
            : request.MusicLevelPercent;
        var effectiveMusicLevelPercent = ResolveEffectiveMusicLevelPercent(requestedMusicLevelPercent);
        var backgroundMusic = planRenderMusicPlan.BackgroundMusic || request.BackgroundMusic;
        var musicMood = string.IsNullOrWhiteSpace(request.MusicMood)
            ? planRenderMusicPlan.MusicMood
            : request.MusicMood;

        return new VideoAssemblyRenderMusicPlanDto(
            backgroundMusic,
            string.IsNullOrWhiteSpace(musicMood) ? "WonderCuriosity" : musicMood,
            effectiveMusicLevelPercent,
            request.DuckMusicUnderNarration);
    }

    private static int ResolveEffectiveMusicLevelPercent(int musicLevelPercent)
        => Math.Clamp(musicLevelPercent <= 0 ? 12 : musicLevelPercent, 0, 100);

    private static double ResolveMusicMixLevel(VideoAssemblyRenderMusicPlanDto renderMusicPlan)
    {
        if (!renderMusicPlan.BackgroundMusic)
            return 0;

        return ResolveEffectiveMusicLevelPercent(renderMusicPlan.MusicLevelPercent) / 100d;
    }

    private static string BuildFfmpegAudioFilter(VideoAssemblyRenderMusicPlanDto renderMusicPlan)
    {
        if (!renderMusicPlan.BackgroundMusic)
            return string.Empty;

        var volume = ResolveMusicMixLevel(renderMusicPlan).ToString("0.00", CultureInfo.InvariantCulture);
        return $"[2:a]volume={volume}[music];[1:a][music]amix=inputs=2:duration=first:normalize=0[aout]";
    }

    private static string ResolveFinalVideoFileName(ScenePresentationProfile profile)
        => profile == ScenePresentationProfile.ShortForm ? FinalVideoShortFileName : FinalVideoLongFileName;

    private static string ResolveFinalVideoOutputPath(VideoAssemblyPlanDto plan)
    {
        var planPath = plan.RenderOutputPath.Replace('/', Path.DirectorySeparatorChar);
        var outputDirectory = Path.GetDirectoryName(planPath) ?? string.Empty;
        return NormalizePath(Path.Combine(outputDirectory, ResolveFinalVideoFileName(plan.ScenePresentationProfile)));
    }

    private (bool Found, string Path) ResolveBackgroundMusicSource(VideoAssemblyRenderMusicPlanDto renderMusicPlan)
    {
        if (!renderMusicPlan.BackgroundMusic)
            return (false, string.Empty);

        var candidates = new List<string?>();
        var backgroundMusicOptions = videoAssemblyOptions?.Value.BackgroundMusic;
        if (backgroundMusicOptions is not null && backgroundMusicOptions.Enabled)
        {
            if (string.Equals(renderMusicPlan.MusicMood, "WonderCuriosity", StringComparison.OrdinalIgnoreCase))
                candidates.Add(backgroundMusicOptions.WonderCuriosityPath);
            candidates.Add(backgroundMusicOptions.DefaultPath);
        }

        candidates.Add(renderingOptions.Value.BackgroundMusicPath);

        var musicPath = candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        return string.IsNullOrWhiteSpace(musicPath) ? (false, string.Empty) : (true, musicPath!);
    }

    private async Task RunFfmpegOrThrowAsync(IReadOnlyList<string> arguments, string operation, CancellationToken cancellationToken)
    {
        var ffmpegPath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfmpegPath) ? "ffmpeg" : renderingOptions.Value.FfmpegPath;
        var result = await RunProcessAsync(ffmpegPath, arguments, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Video render failed while attempting to {operation}. ffmpeg exit code {result.ExitCode}. {result.Error}{result.Output}");
    }

    private async Task<RenderValidation> ValidateRenderedVideoAsync(string outputPath, string audioPath, VideoAssemblyPlanDto plan, CancellationToken cancellationToken)
    {
        var videoExists = File.Exists(outputPath);
        var audioExists = File.Exists(audioPath);
        if (!videoExists || !audioExists)
            return new RenderValidation(videoExists, audioExists, false, false, 0, string.Empty, 0, false);

        var finalDuration = await ProbeDurationSecondsAsync(outputPath, cancellationToken);
        var audioDuration = await ProbeDurationSecondsAsync(audioPath, cancellationToken);
        if (audioDuration <= 0)
            audioDuration = plan.TotalDurationSeconds;
        var metadata = await ProbeVideoMetadataAsync(outputPath, cancellationToken);
        var durationMatchesAudio = finalDuration > 0 && Math.Abs(finalDuration - audioDuration) <= RenderDurationToleranceSeconds;
        var resolution = metadata.Width > 0 && metadata.Height > 0 ? $"{metadata.Width}x{metadata.Height}" : string.Empty;
        var fpsMatches = Math.Abs(metadata.Fps - plan.RenderSettings.Fps) <= 0.1;
        var resolutionMatches = resolution == $"{plan.RenderSettings.Width}x{plan.RenderSettings.Height}";
        var renderSucceeded = videoExists && audioExists && durationMatchesAudio && metadata.AudioTrackPresent && resolutionMatches && fpsMatches;
        return new RenderValidation(videoExists, audioExists, durationMatchesAudio, renderSucceeded, Math.Round(finalDuration, 3, MidpointRounding.AwayFromZero), resolution, (int)Math.Round(metadata.Fps), metadata.AudioTrackPresent);
    }

    private async Task<VideoProbeMetadata> ProbeVideoMetadataAsync(string videoPath, CancellationToken cancellationToken)
    {
        var ffprobePath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfprobePath) ? "ffprobe" : renderingOptions.Value.FfprobePath;
        var result = await RunProcessAsync(ffprobePath,
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,r_frame_rate", "-of", "json", videoPath],
            cancellationToken);
        var audioResult = await RunProcessAsync(ffprobePath,
            ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=index", "-of", "csv=p=0", videoPath],
            cancellationToken);
        if (result.ExitCode != 0)
            return new VideoProbeMetadata(0, 0, 0, false);

        using var document = JsonDocument.Parse(result.Output);
        var stream = document.RootElement.TryGetProperty("streams", out var streams) && streams.GetArrayLength() > 0 ? streams[0] : default;
        var width = stream.ValueKind == JsonValueKind.Object && stream.TryGetProperty("width", out var widthElement) ? widthElement.GetInt32() : 0;
        var height = stream.ValueKind == JsonValueKind.Object && stream.TryGetProperty("height", out var heightElement) ? heightElement.GetInt32() : 0;
        var fps = stream.ValueKind == JsonValueKind.Object && stream.TryGetProperty("r_frame_rate", out var fpsElement) ? ParseFrameRate(fpsElement.GetString()) : 0;
        return new VideoProbeMetadata(width, height, fps, audioResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(audioResult.Output));
    }

    private static double ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        var parts = value.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            && denominator != 0)
            return numerator / denominator;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) ? fps : 0;
    }

    private static void EnsureRenderPlanUsesActualTtsTiming(VideoAssemblyPlanDto plan, VideoTtsTimingsDto timings)
    {
        if (Math.Abs(plan.TotalDurationSeconds - timings.ActualDurationSeconds) > 0.001)
            throw new ArgumentException("Video render validation failed: video assembly plan duration must match actual TTS timing duration.");
        if (plan.Segments.Count != timings.SceneTimings.Count)
            throw new ArgumentException("Video render validation failed: video assembly plan segment count must match TTS timing segment count.");

        foreach (var (segment, timing) in plan.Segments.Zip(timings.SceneTimings))
        {
            if (!string.Equals(segment.SceneKey, timing.SceneKey, StringComparison.OrdinalIgnoreCase)
                || Math.Abs(segment.StartSeconds - timing.StartSeconds) > 0.001
                || Math.Abs(segment.EndSeconds - timing.EndSeconds) > 0.001)
                throw new ArgumentException("Video render validation failed: video assembly plan segments must use actual TTS timings from video-tts-timings.json.");
        }
    }

    private string BuildThumbnailLandscapeOutputPath(string eventId, string regionId)
        => Path.Combine(BuildThumbnailAssetsRoot(eventId, regionId), ThumbnailLandscapeFileName);

    private static string ResolveAssemblyVisualAssetPath(string sceneKey, string thumbnailPath, string sceneApprovalRoot, ScenePresentationProfile profile)
    {
        var visualMap = profile == ScenePresentationProfile.ShortForm ? AssemblySceneVisualMap : LongFormAssemblySceneVisualMap;
        if (!visualMap.TryGetValue(sceneKey, out var visualFileName))
            throw new ArgumentException($"Video assembly validation failed: no approved visual asset mapping exists for scene '{sceneKey}' in the {profile} profile.");

        return Path.Combine(sceneApprovalRoot, visualFileName);
    }

    private static string ResolveVideoAssemblyIntelligenceFileName(ScenePresentationProfile profile)
        => profile == ScenePresentationProfile.ShortForm ? VideoAssemblyIntelligenceFileName : LongVideoAssemblyIntelligenceFileName;

    private static string ResolveVideoNarrationScriptFileName(ScenePresentationProfile profile)
        => profile == ScenePresentationProfile.ShortForm ? VideoNarrationScriptFileName : LongVideoNarrationScriptFileName;

    private static string ResolveVideoTtsAudioFileName(ScenePresentationProfile profile)
        => profile == ScenePresentationProfile.ShortForm ? VideoTtsAudioFileName : LongVideoTtsAudioFileName;

    private static string ResolveVideoTtsTimingsFileName(ScenePresentationProfile profile)
        => profile == ScenePresentationProfile.ShortForm ? VideoTtsTimingsFileName : LongVideoTtsTimingsFileName;

    private static string ResolveVideoAssemblyPlanFileName(ScenePresentationProfile profile)
        => profile == ScenePresentationProfile.ShortForm ? VideoAssemblyPlanFileName : LongVideoAssemblyPlanFileName;

    private void ValidateVideoNarrationScript(VideoNarrationScriptDto script)
    {
        if (string.IsNullOrWhiteSpace(script.FullNarrationText))
            throw new ArgumentException("Video narration script validation failed: fullNarrationText must not be empty.");
        var profile = ResolveScenePresentationProfile(script.Platform);
        ValidateSceneTimingOrder(script.SceneScripts.Select(scene => scene.SceneKey), profile, "video-narration-script.json");
        var durationValidation = script.DurationValidation ?? BuildDurationValidation(profile, script.TotalEstimatedDurationSeconds, useTargetRange: true, "totalEstimatedDurationSeconds");
        EnsureDurationValidationPassed(durationValidation, "Video narration script validation failed");
        if (profile == ScenePresentationProfile.LongForm)
        {
            var calculatedDurationSeconds = EstimateSpokenDurationSeconds(script.FullNarrationText);
            if (Math.Abs(script.TotalEstimatedDurationSeconds - calculatedDurationSeconds) > 1.0)
                throw new ArgumentException("Video narration script validation failed: LongForm totalEstimatedDurationSeconds must be calculated from narration word count or TTS estimate.");
        }
        var sceneNarrations = script.SceneScripts.Select(scene => scene.Narration).ToArray();
        if (sceneNarrations.Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sceneNarrations.Length)
            throw new ArgumentException("Video narration script validation failed: duplicate scene narration is not allowed.");
        if (sceneNarrations.Any(n => n.Trim().Length < 30))
            throw new ArgumentException("Video narration script validation failed: every scene narration must be at least 30 characters.");
        if (sceneNarrations.Any(ContainsNarrationAuthoringInstruction))
            throw new ArgumentException("Video narration script validation failed: narration contains authoring instruction text.");
        if (sceneNarrations.Any(ContainsRawSpokenTimestamp))
            throw new ArgumentException("Video narration script validation failed: narration contains raw timestamps.");
        if (!script.TtsPlan.TtsRequired)
            throw new ArgumentException("Video narration script validation failed: ttsRequired must be true.");
        if (script.Scores.TtsReadinessScore < 90)
            throw new ArgumentException("Video narration script validation failed: ttsReadinessScore must be at least 90.");
    }

    private static bool ContainsNarrationAuthoringInstruction(string value)
        => NarrationAuthoringInstructionPhrases.Any(phrase => !string.IsNullOrWhiteSpace(value) && value.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsRawSpokenTimestamp(string value)
        => Regex.IsMatch(value ?? string.Empty, @"\b\d{4}-\d{2}-\d{2}(?:[ T]\d{1,2}:\d{2})?|\b\d{1,2}:\d{2}\s*(?:[+-]\d{2}:?\d{2}|UTC|GMT)\b", RegexOptions.IgnoreCase);

    private static void ValidateRecommendedSceneOrder(IEnumerable<string> sceneOrder, string fileName)
    {
        if (!sceneOrder.SequenceEqual(RequiredAssemblySceneOrder, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Video narration script validation failed: scene order in {fileName} must be Hook, What, Why, Where, When, Action.");
    }

    private static void ValidateSceneTimingOrder(IEnumerable<string> sceneOrder, ScenePresentationProfile profile, string fileName)
    {
        if (profile == ScenePresentationProfile.ShortForm)
        {
            ValidateRecommendedSceneOrder(sceneOrder, fileName);
            return;
        }

        if (!sceneOrder.SequenceEqual(LongFormSectionOrder, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Video narration script validation failed: LongForm scene order in {fileName} must match the longFormSections plan.");
    }

    private void ValidateVideoAssemblyIntelligence(VideoAssemblyIntelligenceDto intelligence)
    {
        if (string.IsNullOrWhiteSpace(intelligence.SelectedOpeningHook))
            throw new ArgumentException("Video assembly intelligence validation failed: selectedOpeningHook is required.");
        if (intelligence.RecommendedSceneOrder.Count == 0)
            throw new ArgumentException("Video assembly intelligence validation failed: recommendedSceneOrder must not be empty.");
        var profile = ResolveScenePresentationProfile(intelligence.Platform);
        EnsureDurationValidationPassed(BuildDurationValidation(profile, intelligence.RecommendedTotalDurationSeconds, useTargetRange: true, "recommendedTotalDurationSeconds"), "Video assembly intelligence validation failed");
        if (!intelligence.AudioPlan.TtsRequired)
            throw new ArgumentException("Video assembly intelligence validation failed: ttsRequired must be true.");
        if (intelligence.Scores.VideoAssemblyReadinessScore < 90)
            throw new ArgumentException("Video assembly intelligence validation failed: videoAssemblyReadinessScore must be at least 90.");
    }

    private void ValidateLongFormVideoTtsTimings(LongFormVideoTtsTimingsDto timings)
    {
        if (string.IsNullOrWhiteSpace(timings.AudioFilePath))
            throw new ArgumentException("Long-form video TTS timings validation failed: audioFilePath is required.");
        if (!string.Equals(timings.Platform, "YouTubeLong", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Long-form video TTS timings validation failed: platform must be YouTubeLong.");
        EnsureDurationValidationPassed(BuildDurationValidation(ScenePresentationProfile.LongForm, timings.EstimatedDurationSeconds, useTargetRange: true, "estimatedDurationSeconds"), "Long-form video TTS timings validation failed");
        EnsureDurationValidationPassed(timings.DurationValidation ?? BuildDurationValidation(ScenePresentationProfile.LongForm, timings.ActualDurationSeconds, useTargetRange: false, "actualDurationSeconds"), "Long-form video TTS timings validation failed");
        if (!string.Equals(timings.TtsProvider, AzureTtsProviderName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Long-form video TTS timings validation failed: ttsProvider must be AzureSpeechTts.");
        if (string.IsNullOrWhiteSpace(timings.VoiceUsed))
            throw new ArgumentException("Long-form video TTS timings validation failed: voiceUsed is required.");
        if (timings.AudioValidation is null || !timings.AudioValidation.AudioValidationPassed || timings.AudioValidation.IsSilentAudio)
            throw new ArgumentException("Long-form video TTS timings validation failed: audioValidation must pass and must not be silent.");
        if (timings.SectionTimings.Count == 0)
            throw new ArgumentException("Long-form video TTS timings validation failed: sectionTimings are required.");
        ValidateSceneTimingOrder(timings.SectionTimings.Select(section => section.SectionKey), ScenePresentationProfile.LongForm, "video-long-tts-timings.json");
        ValidateTimingContinuity(timings.SectionTimings.Select(section => (section.StartSeconds, section.EndSeconds)).ToArray(), timings.ActualDurationSeconds, "Long-form video TTS timings validation failed");
    }

    private void ValidateVideoTtsTimings(VideoTtsTimingsDto timings)
    {
        if (string.IsNullOrWhiteSpace(timings.AudioFilePath))
            throw new ArgumentException("Video TTS timings validation failed: audioFilePath is required.");
        var profile = ResolveScenePresentationProfile(timings.Platform);
        EnsureDurationValidationPassed(BuildDurationValidation(profile, timings.EstimatedDurationSeconds, useTargetRange: true, "estimatedDurationSeconds"), "Video TTS timings validation failed");
        EnsureDurationValidationPassed(timings.DurationValidation ?? BuildDurationValidation(profile, timings.ActualDurationSeconds, useTargetRange: false, "actualDurationSeconds"), "Video TTS timings validation failed");
        ValidateSceneTimingOrder(timings.SceneTimings.Select(scene => scene.SceneKey), profile, "video-tts-timings.json");
        if (string.IsNullOrWhiteSpace(timings.TtsProvider))
            throw new ArgumentException("Video TTS timings validation failed: ttsProvider is required.");
        if (string.IsNullOrWhiteSpace(timings.VoiceUsed))
            throw new ArgumentException("Video TTS timings validation failed: voiceUsed is required.");
        if (timings.AudioValidation is null)
            throw new ArgumentException("Video TTS timings validation failed: audioValidation is required.");
        if (!timings.AudioValidation.AudioValidationPassed || timings.AudioValidation.IsSilentAudio)
            throw new ArgumentException("Video TTS timings validation failed: audioValidation must pass and must not be silent.");
    }

    private static void ValidateTimingContinuity(IReadOnlyList<(double StartSeconds, double EndSeconds)> timings, double actualDurationSeconds, string messagePrefix)
    {
        var cursor = 0.0;
        for (var index = 0; index < timings.Count; index++)
        {
            var (startSeconds, endSeconds) = timings[index];
            if (Math.Abs(startSeconds - cursor) > 0.02)
                throw new ArgumentException($"{messagePrefix}: timing section {index + 1} must start at the previous section end.");
            if (endSeconds <= startSeconds)
                throw new ArgumentException($"{messagePrefix}: timing section {index + 1} must have positive duration.");
            cursor = endSeconds;
        }

        if (Math.Abs(cursor - actualDurationSeconds) > 0.02)
            throw new ArgumentException($"{messagePrefix}: final section end must match actualDurationSeconds.");
    }

    private static void ValidateRequest(VideoAssemblyGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
            throw new ArgumentException("eventId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RegionId))
            throw new ArgumentException("regionId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("language is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Platform))
            throw new ArgumentException("platform is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Phase))
            throw new ArgumentException("phase is required.", nameof(request));
    }

    private VideoAssemblyGenerationResponse BuildResponse(string phaseRequested, VideoAssemblyIntelligenceDto intelligence, string outputPath)
        => new(
            phaseRequested,
            phaseRequested.StartsWith("LongForm", StringComparison.OrdinalIgnoreCase) ? "LongFormIntelligence" : "Intelligence",
            true,
            NormalizePath(outputPath),
            intelligence.SelectedOpeningHook,
            intelligence.RecommendedTotalDurationSeconds,
            intelligence.AudioPlan.TtsRequired,
            intelligence.OutputsPlanned.Any(output => string.Equals(output, "final-video-short.mp4", StringComparison.OrdinalIgnoreCase) || string.Equals(output, "final-video-long.mp4", StringComparison.OrdinalIgnoreCase)),
            [],
            DurationValidation: BuildDurationValidation(ResolveScenePresentationProfile(intelligence.Platform), intelligence.RecommendedTotalDurationSeconds, useTargetRange: true, "recommendedTotalDurationSeconds"));

    private VideoAssemblyGenerationResponse BuildScriptResponse(string phaseRequested, VideoNarrationScriptDto script, string outputPath)
        => new(
            phaseRequested,
            phaseRequested.StartsWith("LongForm", StringComparison.OrdinalIgnoreCase) ? "LongFormScript" : "Script",
            false,
            string.Empty,
            string.Empty,
            0,
            script.TtsPlan.TtsRequired,
            false,
            [],
            true,
            NormalizePath(outputPath),
            script.TotalEstimatedDurationSeconds,
            script.Scores.TtsReadinessScore >= 90,
            DurationValidation: script.DurationValidation ?? BuildDurationValidation(ResolveScenePresentationProfile(script.Platform), script.TotalEstimatedDurationSeconds, useTargetRange: true, "totalEstimatedDurationSeconds"));

    private VideoAssemblyGenerationResponse BuildTtsResponse(
        string phaseRequested,
        string audioPath,
        string timingsPath,
        double actualDurationSeconds,
        IReadOnlyList<string> generatedFiles,
        string ttsProvider = "",
        bool isSyntheticTts = false,
        VideoTtsAudioValidationDto? audioValidation = null)
        => new(
            phaseRequested,
            "Tts",
            false,
            string.Empty,
            string.Empty,
            0,
            true,
            false,
            generatedFiles,
            false,
            string.Empty,
            0,
            true,
            true,
            true,
            NormalizePath(audioPath),
            NormalizePath(timingsPath),
            actualDurationSeconds,
            ttsProvider,
            isSyntheticTts,
            audioValidation?.IsSilentAudio ?? false,
            audioValidation?.AudioValidationPassed ?? false,
            audioValidation?.AudioPeakDb ?? 0,
            audioValidation?.AudioRmsDb ?? 0,
            DurationValidation: BuildDurationValidation(phaseRequested.StartsWith("LongForm", StringComparison.OrdinalIgnoreCase) ? ScenePresentationProfile.LongForm : ScenePresentationProfile.ShortForm, actualDurationSeconds, useTargetRange: false, "actualDurationSeconds"));


    private VideoAssemblyGenerationResponse BuildLongFormTtsResponse(
        string phaseRequested,
        string audioPath,
        string timingsPath,
        double actualDurationSeconds,
        IReadOnlyList<string> generatedFiles,
        string ttsProvider,
        VideoTtsAudioValidationDto audioValidation)
        => new(
            phaseRequested,
            "LongFormTts",
            false,
            string.Empty,
            string.Empty,
            0,
            true,
            false,
            generatedFiles,
            TtsReady: true,
            TtsAudioGenerated: true,
            TtsTimingsGenerated: true,
            AudioFilePath: NormalizePath(audioPath),
            TimingsFilePath: NormalizePath(timingsPath),
            ActualDurationSeconds: actualDurationSeconds,
            TtsProvider: ttsProvider,
            IsSyntheticTts: false,
            IsSilentAudio: audioValidation.IsSilentAudio,
            AudioValidationPassed: audioValidation.AudioValidationPassed,
            AudioPeakDb: audioValidation.AudioPeakDb,
            AudioRmsDb: audioValidation.AudioRmsDb,
            ScenePresentationProfileUsed: ScenePresentationProfile.LongForm,
            DurationValidation: BuildDurationValidation(ScenePresentationProfile.LongForm, actualDurationSeconds, useTargetRange: false, "actualDurationSeconds"));


    private VideoAssemblyGenerationResponse BuildAssemblyResponse(string phaseRequested, VideoAssemblyPlanDto plan, string outputPath, IReadOnlyList<string> generatedFiles)
        => new(
            phaseRequested,
            phaseRequested.StartsWith("LongForm", StringComparison.OrdinalIgnoreCase) ? "LongFormAssembly" : "Assembly",
            false,
            string.Empty,
            string.Empty,
            0,
            true,
            false,
            generatedFiles,
            false,
            string.Empty,
            0,
            true,
            false,
            false,
            plan.AudioFilePath,
            string.Empty,
            0,
            string.Empty,
            false,
            false,
            false,
            0,
            0,
            true,
            NormalizePath(outputPath),
            plan.Validation.ReadyForRender,
            plan.Segments.Count,
            plan.TotalDurationSeconds,
            plan.ScenePresentationProfile,
            plan.SceneImageBaseDirectory,
            plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm,
            plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm ? plan.SceneImages.Count : 0,
            false,
            string.Empty,
            0,
            string.Empty,
            false,
            false,
            false,
            string.Empty,
            0,
            0,
            plan.SceneImages.Any(IsLongSceneApprovalPath) || plan.Segments.Any(segment => IsLongSceneApprovalPath(segment.VisualAssetPath)),
            plan.SceneMappingValidation.SceneMappingValid,
            plan.RenderMusicPlan.BackgroundMusic,
            plan.RenderMusicPlan.MusicLevelPercent,
            plan.RenderMusicPlan.BackgroundMusic,
            string.Empty,
            plan.RenderMusicPlan.DuckMusicUnderNarration,
            DurationValidation: plan.DurationValidation ?? BuildDurationValidation(plan.ScenePresentationProfile, plan.TotalDurationSeconds, useTargetRange: false, "actualDurationSeconds"));



    private static string SerializeVideoAssemblyPlan(VideoAssemblyPlanDto plan)
    {
        if (plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm)
            return JsonSerializer.Serialize(plan, JsonOptions);

        var document = new
        {
            plan.EventId,
            plan.RegionId,
            plan.Language,
            plan.Platform,
            plan.ScenePresentationProfile,
            plan.SceneImageBaseDirectory,
            plan.SceneCount,
            plan.SceneImages,
            plan.TotalDurationSeconds,
            plan.AudioFilePath,
            plan.RenderOutputPath,
            Segments = plan.Segments.Select(segment => new
            {
                SectionKey = segment.SceneKey,
                segment.SceneKey,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.DurationSeconds,
                segment.VisualAssetPath,
                segment.Narration,
                segment.TransitionIn,
                segment.TransitionOut,
                segment.Motion
            }).ToArray(),
            plan.RenderSettings,
            plan.BackgroundMusic,
            plan.Style,
            plan.Validation,
            plan.SceneMappingValidation,
            plan.RenderMusicPlan,
            plan.Warnings,
            plan.GeneratedUtc,
            plan.DurationValidation
        };
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    private async Task<VideoRenderValidationDto> WriteVideoRenderValidationAsync(VideoAssemblyPlanDto plan, VideoAssemblyGenerationRequest request, VideoAssemblyRenderMusicPlanDto renderMusicPlan, string outputPath, RenderValidation renderValidation, string validationPath, CancellationToken cancellationToken)
    {
        var validation = BuildVideoRenderValidation(plan, request, renderMusicPlan, outputPath, renderValidation);
        Directory.CreateDirectory(Path.GetDirectoryName(validationPath) ?? ResolveWorkingDirectoryRoot());
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        return validation;
    }

    private VideoRenderValidationDto BuildVideoRenderValidation(VideoAssemblyPlanDto plan, VideoAssemblyGenerationRequest request, VideoAssemblyRenderMusicPlanDto renderMusicPlan, string? outputPath = null, RenderValidation? renderValidation = null)
    {
        var documentaryMotionApplied = plan.ScenePresentationProfile == ScenePresentationProfile.LongForm
            && plan.Segments.All(segment => IsLongFormDocumentaryMotion(segment.Motion));
        var kenBurnsApplied = documentaryMotionApplied
            || (plan.Style.MotionStyle.Equals("SubtleKenBurns", StringComparison.OrdinalIgnoreCase)
                && plan.Segments.All(segment => segment.Motion.Contains("SubtleKenBurns", StringComparison.OrdinalIgnoreCase) || segment.Motion.Contains("HookThumbnailZoomIn100To105", StringComparison.OrdinalIgnoreCase)));
        var crossFadeApplied = plan.Style.TransitionStyle.Equals("CrossFade", StringComparison.OrdinalIgnoreCase)
            && plan.Segments.Skip(1).All(segment => segment.TransitionIn.Equals("CrossFade", StringComparison.OrdinalIgnoreCase))
            && plan.Segments.Take(plan.Segments.Count - 1).All(segment => segment.TransitionOut.Equals("CrossFade", StringComparison.OrdinalIgnoreCase));
        var hook = plan.Segments.FirstOrDefault(segment => segment.SceneKey.Equals("Hook", StringComparison.OrdinalIgnoreCase));
        var hookOptimizationApplied = plan.ScenePresentationProfile == ScenePresentationProfile.LongForm
            || (hook is not null
                && hook.VisualAssetPath.EndsWith("scene-001-final.png", StringComparison.OrdinalIgnoreCase)
                && hook.Motion.Equals("HookThumbnailZoomIn100To105", StringComparison.OrdinalIgnoreCase)
                && Math.Abs(hook.DurationSeconds - HookOptimizationDurationSeconds) <= 0.01);
        var musicVolumeMultiplier = ResolveMusicMixLevel(renderMusicPlan);
        var ffmpegAudioFilter = BuildFfmpegAudioFilter(renderMusicPlan);
        var musicMixValidated = !renderMusicPlan.BackgroundMusic
            || (musicVolumeMultiplier > 0 && ffmpegAudioFilter.Contains("normalize=0", StringComparison.OrdinalIgnoreCase));
        var renderPolishScore = kenBurnsApplied && crossFadeApplied && hookOptimizationApplied && musicMixValidated ? 96 : 0;
        var videoFinalReadinessScore = renderPolishScore >= 90 ? 98 : 0;
        var resolution = $"{plan.RenderSettings.Width}x{plan.RenderSettings.Height}";
        var renderUsedShortScenes = plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm
            && plan.SceneImages.Count == 6
            && plan.SceneImages.All(IsShortSceneApprovalPath)
            && plan.Segments.All(segment => IsShortSceneApprovalPath(segment.VisualAssetPath));
        var renderUsedLongScenes = plan.SceneImages.Any(IsLongSceneApprovalPath)
            || plan.Segments.Any(segment => IsLongSceneApprovalPath(segment.VisualAssetPath));
        var shortFormSceneCount = plan.ScenePresentationProfile == ScenePresentationProfile.ShortForm ? plan.SceneImages.Count : 0;
        var ttsAudioPresent = File.Exists(plan.AudioFilePath);
        var backgroundMusicSource = ResolveBackgroundMusicSource(renderMusicPlan);
        var backgroundMusicPresent = renderMusicPlan.BackgroundMusic && backgroundMusicSource.Found;
        var audioTrackPresent = renderValidation?.AudioTrackPresent ?? ttsAudioPresent;
        var renderSucceeded = renderValidation?.RenderSucceeded ?? true;
        var longFormValidationPassed = plan.ScenePresentationProfile != ScenePresentationProfile.LongForm
            || (renderUsedLongScenes
                && !renderUsedShortScenes
                && string.Equals(resolution, "1920x1080", StringComparison.OrdinalIgnoreCase)
                && ttsAudioPresent
                && documentaryMotionApplied
                && string.Equals(Path.GetFileName(outputPath ?? ResolveFinalVideoOutputPath(plan)), FinalVideoLongFileName, StringComparison.OrdinalIgnoreCase));
        var renderValidationPassed = (plan.ScenePresentationProfile != ScenePresentationProfile.ShortForm
                || (renderUsedShortScenes
                    && !renderUsedLongScenes
                    && shortFormSceneCount == 6
                    && string.Equals(resolution, "1080x1920", StringComparison.OrdinalIgnoreCase)
                    && ttsAudioPresent))
            && longFormValidationPassed
            && (request.DryRun || plan.ScenePresentationProfile == ScenePresentationProfile.LongForm || !renderMusicPlan.BackgroundMusic || backgroundMusicSource.Found)
            && (!renderMusicPlan.BackgroundMusic || string.Equals(Path.GetFileName(outputPath ?? ResolveFinalVideoOutputPath(plan)), FinalVideoShortFileName, StringComparison.OrdinalIgnoreCase) || plan.ScenePresentationProfile != ScenePresentationProfile.ShortForm);
        var backgroundMusicMixed = renderMusicPlan.BackgroundMusic && backgroundMusicSource.Found && musicMixValidated && audioTrackPresent && renderSucceeded;
        var warnings = renderMusicPlan.BackgroundMusic && !backgroundMusicSource.Found
            ? new[] { "Background music requested but music source file was not found." }
            : Array.Empty<string>();

        return new VideoRenderValidationDto(
            plan.ScenePresentationProfile,
            plan.SceneImageBaseDirectory,
            renderUsedShortScenes,
            renderUsedLongScenes,
            shortFormSceneCount,
            resolution,
            ttsAudioPresent,
            backgroundMusicPresent,
            renderValidationPassed,
            kenBurnsApplied,
            crossFadeApplied,
            hookOptimizationApplied,
            musicMixValidated,
            renderPolishScore,
            videoFinalReadinessScore,
            Path.GetFileName(outputPath ?? ResolveFinalVideoOutputPath(plan)),
            renderMusicPlan.BackgroundMusic,
            backgroundMusicSource.Found,
            backgroundMusicMixed,
            renderMusicPlan.MusicMood,
            renderMusicPlan.MusicLevelPercent <= 0 ? 12 : renderMusicPlan.MusicLevelPercent,
            renderMusicPlan.DuckMusicUnderNarration,
            audioTrackPresent,
            audioTrackPresent,
            backgroundMusicMixed,
            renderSucceeded,
            NormalizePath(backgroundMusicSource.Path),
            request.MusicLevelPercent <= 0 ? renderMusicPlan.MusicLevelPercent : request.MusicLevelPercent,
            renderMusicPlan.MusicLevelPercent,
            musicVolumeMultiplier,
            ffmpegAudioFilter,
            backgroundMusicMixed,
            renderValidation?.VideoExists ?? File.Exists(outputPath ?? ResolveFinalVideoOutputPath(plan)),
            renderValidation?.AudioExists ?? ttsAudioPresent,
            renderValidation?.VideoDurationMatchesAudio ?? request.DryRun,
            renderValidation?.FinalVideoDurationSeconds ?? (request.DryRun ? plan.TotalDurationSeconds : 0),
            renderValidation?.OutputResolution ?? resolution,
            renderValidation?.Fps ?? plan.RenderSettings.Fps,
            BuildDurationValidation(plan.ScenePresentationProfile, renderValidation?.FinalVideoDurationSeconds ?? (request.DryRun ? plan.TotalDurationSeconds : 0), useTargetRange: false, "actualDurationSeconds"),
            warnings);
    }

    private void EnsureShortFormRenderValidationPassed(VideoRenderValidationDto validation)
    {
        if (validation.DurationValidation is not null
            && !IsWithinDurationRange(validation.DurationValidation.ActualDurationSeconds, validation.DurationValidation.AcceptableDurationRange.MinSeconds, validation.DurationValidation.AcceptableDurationRange.MaxSeconds, ResolveDurationComparisonToleranceSeconds()))
            throw new InvalidOperationException($"Video render validation failed: {validation.DurationValidation.Reason}");
        if (validation.ScenePresentationProfileUsed != ScenePresentationProfile.ShortForm)
            return;
        if (!validation.RenderUsedShortScenes)
            throw new InvalidOperationException("Video render validation failed: ShortForm render did not use short scene assets.");
        if (validation.RenderUsedLongScenes)
            throw new InvalidOperationException("Video render validation failed: ShortForm render used long scene assets.");
        if (validation.ShortFormSceneCount != 6)
            throw new InvalidOperationException("Video render validation failed: ShortForm scene count must be 6.");
        if (!string.Equals(validation.VideoResolution, "1080x1920", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Video render validation failed: ShortForm video resolution must be 1080x1920.");
        if (!validation.RenderValidationPassed)
            throw new InvalidOperationException("Video render validation failed: ShortForm render validation did not pass.");
    }

    private VideoAssemblyGenerationResponse BuildRenderResponse(
        VideoAssemblyGenerationRequest request,
        string finalVideoPath,
        double finalVideoDurationSeconds,
        string outputResolution,
        bool audioTrackPresent,
        bool renderSucceeded,
        IReadOnlyList<string> generatedFiles,
        string videoRenderValidationPath,
        VideoRenderValidationDto renderPolish)
        => new(
            request.Phase,
            request.Phase.StartsWith("LongForm", StringComparison.OrdinalIgnoreCase) ? "LongFormRender" : "Render",
            false,
            string.Empty,
            string.Empty,
            0,
            true,
            true,
            generatedFiles,
            false,
            string.Empty,
            0,
            true,
            false,
            false,
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            false,
            false,
            false,
            0,
            0,
            false,
            string.Empty,
            false,
            0,
            0,
            renderPolish.ScenePresentationProfileUsed,
            renderPolish.SceneImageSourceDirectory,
            renderPolish.RenderUsedShortScenes,
            renderPolish.ShortFormSceneCount,
            renderSucceeded,
            NormalizePath(finalVideoPath),
            finalVideoDurationSeconds,
            outputResolution,
            audioTrackPresent,
            renderPolish.BackgroundMusicMixed,
            renderSucceeded,
            NormalizePath(videoRenderValidationPath),
            renderPolish.RenderPolishScore,
            renderPolish.VideoFinalReadinessScore,
            renderPolish.RenderUsedLongScenes,
            false,
            renderPolish.BackgroundMusicPresent,
            renderPolish.MusicLevelPercent,
            renderPolish.BackgroundMusicRequested,
            renderPolish.BackgroundMusicSourcePath,
            renderPolish.DuckMusicUnderNarration,
            renderPolish.RequestedMusicLevelPercent,
            renderPolish.EffectiveMusicLevelPercent,
            renderPolish.MusicVolumeMultiplier,
            renderPolish.FfmpegAudioFilter,
            renderPolish.MusicMixApplied,
            DurationValidation: renderPolish.DurationValidation ?? BuildDurationValidation(renderPolish.ScenePresentationProfileUsed, finalVideoDurationSeconds, useTargetRange: false, "actualDurationSeconds"));

    private string BuildQuestionEngineRoot(string eventId, string regionId)
        => !string.IsNullOrWhiteSpace(_activeProductionContext?.QuestionRoot) ? _activeProductionContext!.QuestionRoot! : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), QuestionEngineDirectoryName);

    private string BuildHeroAssetsRoot(string eventId, string regionId)
        => !string.IsNullOrWhiteSpace(_activeProductionContext?.HeroRoot) ? _activeProductionContext!.HeroRoot! : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), HeroAssetsDirectoryName);

    private string BuildThumbnailAssetsRoot(string eventId, string regionId)
        => !string.IsNullOrWhiteSpace(_activeProductionContext?.ThumbnailRoot) ? _activeProductionContext!.ThumbnailRoot! : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), ThumbnailAssetsDirectoryName);

    private string BuildVideoAssemblyRoot(string eventId, string regionId)
        => !string.IsNullOrWhiteSpace(_activeProductionContext?.VideoAssemblyRoot) ? _activeProductionContext!.VideoAssemblyRoot! : Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), VideoAssemblyDirectoryName);

    private string BuildVideoAssemblyProfileRoot(string eventId, string regionId, ScenePresentationProfile profile)
        => Path.Combine(BuildVideoAssemblyRoot(eventId, regionId), profile == ScenePresentationProfile.ShortForm ? "short" : "long");

    private string BuildVideoAssemblyIntelligenceOutputPath(string eventId, string regionId, ScenePresentationProfile profile)
        => Path.Combine(BuildVideoAssemblyProfileRoot(eventId, regionId, profile), profile == ScenePresentationProfile.ShortForm ? VideoAssemblyIntelligenceFileName : LongVideoAssemblyIntelligenceFileName);

    private string BuildVideoNarrationScriptOutputPath(string eventId, string regionId, ScenePresentationProfile profile)
        => Path.Combine(BuildVideoAssemblyProfileRoot(eventId, regionId, profile), profile == ScenePresentationProfile.ShortForm ? VideoNarrationScriptFileName : LongVideoNarrationScriptFileName);

    private string BuildVideoTtsAudioOutputPath(string eventId, string regionId, ScenePresentationProfile profile)
        => Path.Combine(BuildVideoAssemblyProfileRoot(eventId, regionId, profile), profile == ScenePresentationProfile.ShortForm ? VideoTtsAudioFileName : LongVideoTtsAudioFileName);

    private string BuildVideoTtsTimingsOutputPath(string eventId, string regionId, ScenePresentationProfile profile)
        => Path.Combine(BuildVideoAssemblyProfileRoot(eventId, regionId, profile), profile == ScenePresentationProfile.ShortForm ? VideoTtsTimingsFileName : LongVideoTtsTimingsFileName);

    private string BuildVideoAssemblyPlanOutputPath(string eventId, string regionId, ScenePresentationProfile profile)
        => Path.Combine(BuildVideoAssemblyProfileRoot(eventId, regionId, profile), profile == ScenePresentationProfile.ShortForm ? VideoAssemblyPlanFileName : LongVideoAssemblyPlanFileName);

    private string BuildVideoRenderValidationOutputPath(string eventId, string regionId, ScenePresentationProfile profile)
        => Path.Combine(BuildVideoAssemblyProfileRoot(eventId, regionId, profile), profile == ScenePresentationProfile.ShortForm ? VideoRenderValidationFileName : LongVideoRenderValidationFileName);

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string NormalizeDirectoryPath(string path) => NormalizePath(path).TrimEnd('/') + "/";

    private sealed record AssemblyInputs(
        VideoAssemblyIntelligenceDto Intelligence,
        VideoNarrationScriptDto Script,
        VideoTtsTimingsDto Timings,
        string AudioPath,
        string ThumbnailPath,
        string SceneApprovalRoot,
        ScenePresentationProfile ScenePresentationProfile,
        IReadOnlyList<string> VisualAssetPaths);

    private sealed record TtsProviderSelection(string ProviderName, string VoiceUsed, bool IsSynthetic);

    private sealed record RenderValidation(
        bool VideoExists,
        bool AudioExists,
        bool VideoDurationMatchesAudio,
        bool RenderSucceeded,
        double FinalVideoDurationSeconds,
        string OutputResolution,
        int Fps,
        bool AudioTrackPresent);

    private sealed record VideoProbeMetadata(int Width, int Height, double Fps, bool AudioTrackPresent);

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
