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
    private const double LongFormNarrationWordsPerMinute = 150.0;
    private const double LongFormMinimumEstimatedDurationSeconds = 120.0;
    private const double LongFormMaximumEstimatedDurationSeconds = 180.0;
    private const string SelectedOpeningHook = "DON'T MISS THIS TONIGHT";
    private const string SyntheticTtsProviderName = "SyntheticOfflineTtsV1";
    private const string AzureTtsProviderName = "AzureSpeechTts";
    private const string OpenAiTtsProviderName = "OpenAITts";
    private const long MinimumMp3FileSizeBytes = 1024;
    private const double SilencePeakThresholdDb = -55.0;
    private const double SilenceRmsThresholdDb = -60.0;
    private static readonly string[] RequiredApprovedSceneIds = ["scene-001", "scene-002", "scene-003", "scene-004", "scene-005", "scene-006"];
    private static readonly string[] RequiredAssemblySceneOrder = ["Hook", "What", "Why", "Where", "When", "Action"];
    private static readonly string[] LongFormSectionOrder = ["Hook", "WhatIsHappening", "AboutVenus", "AboutJupiter", "WhyTheyAppearClose", "WhereToLook", "WhenToLook", "HowToObserve", "WhatYouWillSee", "InterestingFact", "ObservationTips", "Recap", "Action"];
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
        ["AboutVenus"] = "scene-001-final.png",
        ["AboutJupiter"] = "scene-005-final.png",
        ["WhyTheyAppearClose"] = "scene-005-final.png",
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
            LongForm = request.LongForm
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

        var audioPath = BuildVideoTtsAudioOutputPath(request.EventId, request.RegionId, ResolveRequestProfile(request));
        var timingsPath = BuildVideoTtsTimingsOutputPath(request.EventId, request.RegionId, ResolveRequestProfile(request));

        var syntheticTtsAllowed = request.DryRun || request.AllowSyntheticSilentTts;

        if (!request.DryRun && !request.OverwriteExisting && File.Exists(audioPath) && File.Exists(timingsPath))
        {
            var existing = JsonSerializer.Deserialize<VideoTtsTimingsDto>(await File.ReadAllTextAsync(timingsPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing video TTS timings could not be parsed.");
            ValidateVideoTtsTimings(existing);
            if (IsSyntheticProvider(existing.TtsProvider) && !syntheticTtsAllowed)
                throw new InvalidOperationException("Real TTS provider is not configured. SyntheticOfflineTtsV1 is disabled for dryRun=false.");

            var existingValidation = await ValidateMp3AudioAsync(audioPath, enforceNonSilent: !IsSyntheticProvider(existing.TtsProvider), cancellationToken);
            if (!existingValidation.AudioValidationPassed && !IsSyntheticProvider(existing.TtsProvider))
                throw new InvalidOperationException("Generated TTS audio validation failed: audio is silent or invalid.");

            return BuildTtsResponse(request.Phase, audioPath, timingsPath, existing.ActualDurationSeconds, [], existing.TtsProvider, IsSyntheticProvider(existing.TtsProvider), existingValidation);
        }

        var script = await EnsureRequiredTtsInputsAsync(request.EventId, request.RegionId, ResolveRequestProfile(request), cancellationToken);
        var provider = ResolveTtsProvider(request, script);
        var actualDurationSeconds = NormalizeTtsDuration(script.TotalEstimatedDurationSeconds);

        var generatedFiles = new List<string>();
        VideoTtsAudioValidationDto audioValidation = new(false, 0, 0, request.DryRun);
        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(audioPath) ?? ResolveWorkingDirectoryRoot());
            await WriteTtsAudioAsync(script.FullNarrationText, audioPath, actualDurationSeconds, provider, cancellationToken);
            audioValidation = await ValidateMp3AudioAsync(audioPath, enforceNonSilent: !provider.IsSynthetic, cancellationToken);
            var measuredDurationSeconds = await ProbeDurationSecondsAsync(audioPath, cancellationToken);
            if (!provider.IsSynthetic && measuredDurationSeconds > 0)
                actualDurationSeconds = Math.Round(measuredDurationSeconds, 3, MidpointRounding.AwayFromZero);

            if (!audioValidation.AudioValidationPassed && !provider.IsSynthetic)
                throw new InvalidOperationException("Generated TTS audio validation failed: audio is silent or invalid.");

            generatedFiles.Add(NormalizePath(audioPath));
        }

        var timings = BuildVideoTtsTimings(request, script, audioPath, actualDurationSeconds, provider.ProviderName, provider.VoiceUsed, audioValidation);
        ValidateVideoTtsTimings(timings);

        if (!request.DryRun)
        {
            await File.WriteAllTextAsync(timingsPath, JsonSerializer.Serialize(timings, JsonOptions), cancellationToken);
            generatedFiles.Add(NormalizePath(timingsPath));
        }

        return BuildTtsResponse(request.Phase, audioPath, timingsPath, timings.ActualDurationSeconds, generatedFiles, provider.ProviderName, provider.IsSynthetic, audioValidation);
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
        var voiceUsed = ResolveNeutralEducationalAzureVoice(script);

        Directory.CreateDirectory(Path.GetDirectoryName(audioPath) ?? ResolveWorkingDirectoryRoot());
        await WriteAzureLongFormTtsAudioAsync(script.FullNarrationText, audioPath, cancellationToken);

        var audioValidation = await ValidateMp3AudioAsync(audioPath, enforceNonSilent: true, cancellationToken);
        if (!audioValidation.AudioValidationPassed)
            throw new InvalidOperationException("Generated long-form TTS audio validation failed: audio is silent or invalid.");

        var actualDurationSeconds = Math.Round(await ProbeDurationSecondsAsync(audioPath, cancellationToken), 3, MidpointRounding.AwayFromZero);
        if (actualDurationSeconds < LongFormMinimumEstimatedDurationSeconds || actualDurationSeconds > LongFormMaximumEstimatedDurationSeconds)
            throw new InvalidOperationException($"Generated long-form TTS audio duration must be 120-180 seconds. ActualDurationSeconds={actualDurationSeconds:0.###}.");

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
            DateTimeOffset.UtcNow);
        ValidateLongFormVideoTtsTimings(timings);

        await File.WriteAllTextAsync(timingsPath, JsonSerializer.Serialize(timings, JsonOptions), cancellationToken);

        return BuildLongFormTtsResponse(
            request.Phase,
            audioPath,
            timingsPath,
            timings.ActualDurationSeconds,
            [NormalizePath(audioPath), NormalizePath(timingsPath)],
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
        using var heroSceneManifest = await EnsureJsonInputAsync(Path.Combine(heroRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);
        using var heroCompositionModel = await EnsureJsonInputAsync(Path.Combine(heroRoot, HeroCompositionModelFileName), HeroCompositionModelFileName, cancellationToken);
        using var thumbnailSceneManifest = await EnsureJsonInputAsync(Path.Combine(thumbnailRoot, ThumbnailSceneManifestFileName), ThumbnailSceneManifestFileName, cancellationToken);
        using var thumbnailIntelligence = await EnsureJsonInputAsync(Path.Combine(thumbnailRoot, ThumbnailIntelligenceFileName), ThumbnailIntelligenceFileName, cancellationToken);
        using var thumbnailCompositionModel = await EnsureJsonInputAsync(Path.Combine(thumbnailRoot, ThumbnailCompositionModelFileName), ThumbnailCompositionModelFileName, cancellationToken);

        EnsureApprovedSceneImages(eventId, regionId, ResolveScenePresentationProfile(platform), heroSceneManifest, thumbnailSceneManifest);
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

    private static VideoAssemblyIntelligenceDto BuildVideoAssemblyIntelligence(VideoAssemblyGenerationRequest request)
    {
        if (IsLongFormRequest(request))
            return BuildLongFormVideoAssemblyIntelligence(request);

        var sceneDurations = new[]
        {
            new VideoAssemblySceneDurationDto("Hook", 3.0, "Stop scroll"),
            new VideoAssemblySceneDurationDto("What", 4.0, "Show Venus and Jupiter"),
            new VideoAssemblySceneDurationDto("Why", 4.0, "Explain why it matters"),
            new VideoAssemblySceneDurationDto("Where", 3.0, "Tell where to look"),
            new VideoAssemblySceneDurationDto("When", 3.0, "Tell best viewing time"),
            new VideoAssemblySceneDurationDto("Action", 3.0, "Call to action")
        };

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
            20,
            ScenePresentationProfile.ShortForm,
            "question-engine/scene-approval-v3/short/",
            null);
    }

    private static VideoAssemblyIntelligenceDto BuildLongFormVideoAssemblyIntelligence(VideoAssemblyGenerationRequest request)
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

    private static VideoNarrationScriptDto BuildVideoNarrationScript(VideoAssemblyGenerationRequest request, VideoAssemblyIntelligenceDto intelligence)
    {
        if (IsLongFormRequest(request))
            return BuildLongFormVideoNarrationScript(request, intelligence);

        var durations = intelligence.RecommendedSceneDurations.ToDictionary(scene => scene.SceneKey, scene => scene.DurationSeconds, StringComparer.OrdinalIgnoreCase);
        var sceneScripts = new[]
        {
            new VideoNarrationSceneScriptDto("Hook", GetDuration(durations, "Hook", 3.0), "Don't miss this tonight.", "DON'T MISS THIS TONIGHT"),
            new VideoNarrationSceneScriptDto("What", GetDuration(durations, "What", 4.0), "Venus and Jupiter will shine close together after sunset.", "Venus + Jupiter"),
            new VideoNarrationSceneScriptDto("Why", GetDuration(durations, "Why", 4.0), "Two of the brightest worlds will share the evening sky.", "Two bright worlds"),
            new VideoNarrationSceneScriptDto("Where", GetDuration(durations, "Where", 3.0), "Look toward the western sky.", "Look West"),
            new VideoNarrationSceneScriptDto("When", GetDuration(durations, "When", 3.0), "The best time is shortly after sunset.", "After Sunset"),
            new VideoNarrationSceneScriptDto("Action", GetDuration(durations, "Action", 3.0), "Step outside tonight and look west.", "Step Outside Tonight")
        };

        return new VideoNarrationScriptDto(
            request.EventId,
            request.RegionId,
            request.Language,
            request.Platform,
            sceneScripts.Sum(scene => scene.DurationSeconds),
            new VideoNarrationScriptStyleDto("Excited but clear", "Fast short-form", "Neutral energetic narrator"),
            sceneScripts,
            string.Join(" ", sceneScripts.Select(scene => scene.Narration)),
            new VideoNarrationTtsPlanDto(true, "NeutralEnergetic", "video-tts-audio.mp3"),
            new VideoNarrationScriptScoresDto(96, 95, 96),
            [],
            DateTimeOffset.UtcNow);
    }

    private static VideoNarrationScriptDto BuildLongFormVideoNarrationScript(VideoAssemblyGenerationRequest request, VideoAssemblyIntelligenceDto intelligence)
    {
        var sceneScripts = BuildBalancedLongFormSceneScripts();
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

    private static IReadOnlyList<VideoNarrationSceneScriptDto> BuildBalancedLongFormSceneScripts()
    {
        var scripts = LongFormSectionOrder.Select(section => new VideoNarrationSceneScriptDto(
            section,
            EstimateSpokenDurationSeconds(BuildLongFormNarration(section)),
            BuildLongFormNarration(section),
            ResolveLongFormOnScreenText(section))).ToArray();

        var totalDuration = EstimateSpokenDurationSeconds(string.Join(" ", scripts.Select(scene => scene.Narration)));
        if (totalDuration < LongFormMinimumEstimatedDurationSeconds)
            scripts = ExpandLongFormSceneNarration(scripts);
        else if (totalDuration > LongFormMaximumEstimatedDurationSeconds)
            scripts = ShortenLongFormSceneNarration(scripts);

        totalDuration = EstimateSpokenDurationSeconds(string.Join(" ", scripts.Select(scene => scene.Narration)));
        if (totalDuration < LongFormMinimumEstimatedDurationSeconds || totalDuration > LongFormMaximumEstimatedDurationSeconds)
            throw new ArgumentException("Video narration script validation failed: LongForm narration word-count estimate must be 120-180 seconds.");

        return scripts.Select(scene => scene with { DurationSeconds = EstimateSpokenDurationSeconds(scene.Narration) }).ToArray();
    }

    private static VideoNarrationSceneScriptDto[] ExpandLongFormSceneNarration(IReadOnlyList<VideoNarrationSceneScriptDto> scripts)
        => scripts.Select(scene => scene with { Narration = $"{scene.Narration} {BuildLongFormExpansionSentence(scene.SceneKey)}" }).ToArray();

    private static VideoNarrationSceneScriptDto[] ShortenLongFormSceneNarration(IReadOnlyList<VideoNarrationSceneScriptDto> scripts)
        => scripts.Select(scene => scene with { Narration = KeepFirstTwoSentences(scene.Narration) }).ToArray();

    private static string KeepFirstTwoSentences(string narration)
    {
        var sentences = narration.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return sentences.Length <= 2 ? narration : string.Join(". ", sentences.Take(2)) + ".";
    }

    private static string BuildLongFormExpansionSentence(string section)
        => section switch
        {
            "Hook" => "Keep expectations simple and enjoy the view as a calm evening marker.",
            "WhatIsHappening" => "The event is about line of sight, brightness, and timing.",
            "AboutVenus" => "Its brightness makes it a reliable first target in twilight.",
            "AboutJupiter" => "Its steady glow gives viewers a second point for comparison.",
            "WhyTheyAppearClose" => "This is normal orbital geometry, seen from our moving planet.",
            "WhereToLook" => "A clearer horizon usually makes the pairing easier to notice.",
            "WhenToLook" => "Local sunset time and clouds can change the best minute.",
            "HowToObserve" => "Move slowly and let the sky become darker around you.",
            "WhatYouWillSee" => "The beauty is in the contrast between the two lights.",
            "InterestingFact" => "That slow change is one reason observers return on later nights.",
            "ObservationTips" => "Safety matters too, so choose a comfortable viewing place.",
            "Recap" => "Those steps keep the observation easy and grounded.",
            "Action" => "Even a brief look can make the night sky feel closer.",
            _ => "Keep the observation simple, safe, and grounded in the visible sky."
        };

    private static double EstimateSpokenDurationSeconds(string narration)
        => Math.Round(CountSpokenWords(narration) / LongFormNarrationWordsPerMinute * 60.0, 3, MidpointRounding.AwayFromZero);

    private static int CountSpokenWords(string narration)
        => string.IsNullOrWhiteSpace(narration) ? 0 : SpokenWordRegex().Matches(narration).Count;

    [GeneratedRegex("[\\p{L}\\p{N}]+(?:['’\u2010-\u2015-][\\p{L}\\p{N}]+)?")]
    private static partial Regex SpokenWordRegex();

    private static string ResolveLongFormSectionPurpose(string section)
        => section switch
        {
            "Hook" => "Invite curiosity",
            "WhatIsHappening" => "Explain the sky event in simple terms",
            "AboutVenus" => "Describe Venus as a bright evening object",
            "AboutJupiter" => "Describe Jupiter as a bright planetary target",
            "WhyTheyAppearClose" => "Clarify apparent sky closeness",
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

    private static string BuildLongFormNarration(string section)
        => section switch
        {
            "Hook" => "Tonight's sky offers an easy place to begin: Venus and Jupiter shining in the evening twilight. They look close together from Earth, making a bright, calm target soon after sunset.",
            "WhatIsHappening" => "This guide is about an apparent planetary pairing, not a collision or rare danger. Venus and Jupiter are far apart in space, but they appear in the same region of our sky.",
            "AboutVenus" => "Venus often looks like the brightest steady point after sunset when it is visible. Its thick clouds reflect sunlight well, so beginners can usually spot it before many stars appear.",
            "AboutJupiter" => "Jupiter is much farther away, but it is large enough to shine clearly in a darkening sky. It may look steady and slightly softer than Venus, especially near twilight.",
            "WhyTheyAppearClose" => "The close pairing comes from perspective. Earth, Venus, and Jupiter each follow separate orbits around the Sun, while our viewpoint projects their positions onto the same sky background.",
            "WhereToLook" => "Start by facing the western horizon, because this evening view happens after sunset. Pick a safe open spot with fewer trees, buildings, or bright lights blocking the lower sky.",
            "WhenToLook" => "Begin looking shortly after sunset, as the sky turns deeper blue but before the planets sink too low. If the horizon is hazy, check again a few minutes later.",
            "HowToObserve" => "Use your eyes first, then binoculars if you have them. Hold still, let your eyes adjust, and compare the two points rather than expecting telescope-like detail.",
            "WhatYouWillSee" => "Expect two bright points, not large planet disks. Venus should appear especially brilliant, while Jupiter may look a little dimmer, steady, and close by in the same sky area.",
            "InterestingFact" => "Planet pairings are helpful for learning the sky because they reveal motion from night to night. A small change in spacing can show how planets slowly shift against the stars.",
            "ObservationTips" => "Check for clouds before going outside, and give yourself a clear view toward the west. If you take a photo, steady the phone and keep the scene simple.",
            "Recap" => "Here is the simple plan: go out after sunset, face west, and find the two brightest points near each other. Remember, the closeness is apparent from Earth.",
            "Action" => "If your sky is clear, step outside tonight for a quiet minute and look west. Share the view with someone nearby, and notice how simple astronomy can feel.",
            _ => "This section continues the astronomy viewing guide using only the available event details."
        };

    private static string ResolveLongFormOnScreenText(string section)
        => section switch
        {
            "Hook" => "Venus + Jupiter Tonight",
            "WhatIsHappening" => "Close in our sky",
            "AboutVenus" => "Venus: very bright",
            "AboutJupiter" => "Jupiter: steady glow",
            "WhyTheyAppearClose" => "Perspective from Earth",
            "WhereToLook" => "Face West",
            "WhenToLook" => "After Sunset",
            "HowToObserve" => "Eyes first, binoculars optional",
            "WhatYouWillSee" => "Two bright points",
            "InterestingFact" => "A beginner-friendly sky marker",
            "ObservationTips" => "Clear horizon helps",
            "Recap" => "After sunset • West • Two bright points",
            "Action" => "Step outside tonight",
            _ => section
        };

    private static double GetDuration(IReadOnlyDictionary<string, double> durations, string sceneKey, double fallback)
        => durations.TryGetValue(sceneKey, out var duration) ? duration : fallback;


    private static VideoTtsTimingsDto BuildVideoTtsTimings(VideoAssemblyGenerationRequest request, VideoNarrationScriptDto script, string audioPath, double actualDurationSeconds, string ttsProvider, string voiceUsed, VideoTtsAudioValidationDto audioValidation)
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
            audioValidation);
    }

    private static double NormalizeTtsDuration(double estimatedDurationSeconds)
        => Math.Round(Math.Clamp(estimatedDurationSeconds, estimatedDurationSeconds > 60 ? 120.0 : 15.0, estimatedDurationSeconds > 60 ? 180.0 : 25.0), 3, MidpointRounding.AwayFromZero);

    private static double ResolveLongFormTargetDuration(VideoAssemblyGenerationRequest request)
        => Math.Clamp((request.LongForm?.TargetDurationSeconds ?? 0) > 0 ? request.LongForm!.TargetDurationSeconds : 180, 120, 180);

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
            return new VideoTtsAudioValidationDto(true, -120, -120, false);

        if (new FileInfo(audioPath).Length < MinimumMp3FileSizeBytes)
            return new VideoTtsAudioValidationDto(true, -120, -120, false);

        var duration = await ProbeDurationSecondsAsync(audioPath, cancellationToken);
        if (duration <= 0)
            return new VideoTtsAudioValidationDto(true, -120, -120, false);

        var (peakDb, rmsDb) = await ProbeAudioLevelsAsync(audioPath, cancellationToken);
        var isSilent = double.IsNegativeInfinity(peakDb)
            || double.IsNegativeInfinity(rmsDb)
            || peakDb < SilencePeakThresholdDb
            || rmsDb < SilenceRmsThresholdDb;
        var passed = !isSilent;
        return new VideoTtsAudioValidationDto(isSilent, RoundDb(peakDb), RoundDb(rmsDb), passed);
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
            DateTimeOffset.UtcNow);
    }

    private static VideoAssemblySceneMappingValidationDto BuildSceneMappingValidation(IReadOnlyList<VideoAssemblyPlanSegmentDto> segments, ScenePresentationProfile profile)
    {
        var visualByScene = segments.ToDictionary(segment => segment.SceneKey, segment => NormalizePath(segment.VisualAssetPath), StringComparer.OrdinalIgnoreCase);
        if (profile == ScenePresentationProfile.LongForm)
        {
            var hookUsesScene001 = SegmentUsesScene(visualByScene, "Hook", "scene-001-final.png");
            var whatUsesScene001 = SegmentUsesScene(visualByScene, "WhatIsHappening", "scene-001-final.png")
                && SegmentUsesScene(visualByScene, "WhatYouWillSee", "scene-001-final.png");
            var whyUsesScene005 = SegmentUsesScene(visualByScene, "WhyTheyAppearClose", "scene-005-final.png")
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
            "AboutVenus" => "SlowPan",
            "AboutJupiter" => "SlowZoom",
            "WhyTheyAppearClose" => "SlowZoomOut",
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

    private static void ValidateVideoAssemblyPlan(VideoAssemblyPlanDto plan)
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

            var finalArgs = BuildFinalMuxArguments(silentVideoPath, plan.AudioFilePath, outputPath, plan.TotalDurationSeconds, renderMusicPlan);
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
        => Math.Abs(sceneKey.Aggregate(0, (sum, ch) => sum + ch)) % 4 switch
        {
            0 => (-0.010, 0.003),
            1 => (0.010, -0.003),
            2 => (0.006, 0.004),
            _ => (-0.006, -0.004)
        };

    private IReadOnlyList<string> BuildFinalMuxArguments(string silentVideoPath, string narrationAudioPath, string outputPath, double durationSeconds, VideoAssemblyRenderMusicPlanDto renderMusicPlan)
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

        args.AddRange([
            "-c:v", "libx264", "-preset", renderingOptions.Value.ShortsPreset, "-crf", renderingOptions.Value.ShortsCrf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p", "-r", "30", "-c:a", "aac", "-b:a", renderingOptions.Value.ShortsAudioBitrate,
            "-shortest", "-movflags", "+faststart", outputPath
        ]);
        return args;
    }


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
        => Path.Combine(sceneApprovalRoot, (profile == ScenePresentationProfile.ShortForm ? AssemblySceneVisualMap : LongFormAssemblySceneVisualMap)[sceneKey]);

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

    private static void ValidateVideoNarrationScript(VideoNarrationScriptDto script)
    {
        if (string.IsNullOrWhiteSpace(script.FullNarrationText))
            throw new ArgumentException("Video narration script validation failed: fullNarrationText must not be empty.");
        var profile = ResolveScenePresentationProfile(script.Platform);
        ValidateSceneTimingOrder(script.SceneScripts.Select(scene => scene.SceneKey), profile, "video-narration-script.json");
        if (profile == ScenePresentationProfile.ShortForm && (script.TotalEstimatedDurationSeconds < 15 || script.TotalEstimatedDurationSeconds > 25))
            throw new ArgumentException("Video narration script validation failed: totalEstimatedDurationSeconds must be 15-25 seconds.");
        if (profile == ScenePresentationProfile.LongForm)
        {
            var calculatedDurationSeconds = EstimateSpokenDurationSeconds(script.FullNarrationText);
            if (Math.Abs(script.TotalEstimatedDurationSeconds - calculatedDurationSeconds) > 1.0)
                throw new ArgumentException("Video narration script validation failed: LongForm totalEstimatedDurationSeconds must be calculated from narration word count or TTS estimate.");
            if (script.TotalEstimatedDurationSeconds < LongFormMinimumEstimatedDurationSeconds || script.TotalEstimatedDurationSeconds > LongFormMaximumEstimatedDurationSeconds)
                throw new ArgumentException("Video narration script validation failed: LongForm totalEstimatedDurationSeconds must be 120-180 seconds.");
        }
        if (!script.TtsPlan.TtsRequired)
            throw new ArgumentException("Video narration script validation failed: ttsRequired must be true.");
        if (script.Scores.TtsReadinessScore < 90)
            throw new ArgumentException("Video narration script validation failed: ttsReadinessScore must be at least 90.");
    }

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

    private static void ValidateVideoAssemblyIntelligence(VideoAssemblyIntelligenceDto intelligence)
    {
        if (string.IsNullOrWhiteSpace(intelligence.SelectedOpeningHook))
            throw new ArgumentException("Video assembly intelligence validation failed: selectedOpeningHook is required.");
        if (intelligence.RecommendedSceneOrder.Count == 0)
            throw new ArgumentException("Video assembly intelligence validation failed: recommendedSceneOrder must not be empty.");
        if (string.Equals(intelligence.Platform, "YouTubeShort", StringComparison.OrdinalIgnoreCase)
            && (intelligence.RecommendedTotalDurationSeconds < 15 || intelligence.RecommendedTotalDurationSeconds > 30))
            throw new ArgumentException("Video assembly intelligence validation failed: YouTubeShort total duration must be 15-30 seconds.");
        if (!intelligence.AudioPlan.TtsRequired)
            throw new ArgumentException("Video assembly intelligence validation failed: ttsRequired must be true.");
        if (intelligence.Scores.VideoAssemblyReadinessScore < 90)
            throw new ArgumentException("Video assembly intelligence validation failed: videoAssemblyReadinessScore must be at least 90.");
    }

    private static void ValidateLongFormVideoTtsTimings(LongFormVideoTtsTimingsDto timings)
    {
        if (string.IsNullOrWhiteSpace(timings.AudioFilePath))
            throw new ArgumentException("Long-form video TTS timings validation failed: audioFilePath is required.");
        if (!string.Equals(timings.Platform, "YouTubeLong", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Long-form video TTS timings validation failed: platform must be YouTubeLong.");
        if (timings.EstimatedDurationSeconds < LongFormMinimumEstimatedDurationSeconds || timings.EstimatedDurationSeconds > LongFormMaximumEstimatedDurationSeconds)
            throw new ArgumentException("Long-form video TTS timings validation failed: estimatedDurationSeconds must be 120-180 seconds.");
        if (timings.ActualDurationSeconds < LongFormMinimumEstimatedDurationSeconds || timings.ActualDurationSeconds > LongFormMaximumEstimatedDurationSeconds)
            throw new ArgumentException("Long-form video TTS timings validation failed: actualDurationSeconds must be 120-180 seconds.");
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

    private static void ValidateVideoTtsTimings(VideoTtsTimingsDto timings)
    {
        if (string.IsNullOrWhiteSpace(timings.AudioFilePath))
            throw new ArgumentException("Video TTS timings validation failed: audioFilePath is required.");
        var profile = ResolveScenePresentationProfile(timings.Platform);
        if (profile == ScenePresentationProfile.ShortForm && (timings.EstimatedDurationSeconds < 15 || timings.EstimatedDurationSeconds > 25))
            throw new ArgumentException("Video TTS timings validation failed: estimatedDurationSeconds must be 15-25 seconds.");
        if (profile == ScenePresentationProfile.ShortForm && (timings.ActualDurationSeconds < 15 || timings.ActualDurationSeconds > 25))
            throw new ArgumentException("Video TTS timings validation failed: actualDurationSeconds must be 15-25 seconds.");
        if (profile == ScenePresentationProfile.LongForm && (timings.EstimatedDurationSeconds < 120 || timings.EstimatedDurationSeconds > 180))
            throw new ArgumentException("Video TTS timings validation failed: LongForm estimatedDurationSeconds must be 120-180 seconds.");
        if (profile == ScenePresentationProfile.LongForm && (timings.ActualDurationSeconds < 120 || timings.ActualDurationSeconds > 180))
            throw new ArgumentException("Video TTS timings validation failed: LongForm actualDurationSeconds must be 120-180 seconds.");
        ValidateSceneTimingOrder(timings.SceneTimings.Select(scene => scene.SceneKey), profile, "video-tts-timings.json");
        if (string.IsNullOrWhiteSpace(timings.TtsProvider))
            throw new ArgumentException("Video TTS timings validation failed: ttsProvider is required.");
        if (string.IsNullOrWhiteSpace(timings.VoiceUsed))
            throw new ArgumentException("Video TTS timings validation failed: voiceUsed is required.");
        if (timings.AudioValidation is null)
            throw new ArgumentException("Video TTS timings validation failed: audioValidation is required.");
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

    private static VideoAssemblyGenerationResponse BuildResponse(string phaseRequested, VideoAssemblyIntelligenceDto intelligence, string outputPath)
        => new(
            phaseRequested,
            phaseRequested.StartsWith("LongForm", StringComparison.OrdinalIgnoreCase) ? "LongFormIntelligence" : "Intelligence",
            true,
            NormalizePath(outputPath),
            intelligence.SelectedOpeningHook,
            intelligence.RecommendedTotalDurationSeconds,
            intelligence.AudioPlan.TtsRequired,
            intelligence.OutputsPlanned.Any(output => string.Equals(output, "final-video-short.mp4", StringComparison.OrdinalIgnoreCase) || string.Equals(output, "final-video-long.mp4", StringComparison.OrdinalIgnoreCase)),
            []);

    private static VideoAssemblyGenerationResponse BuildScriptResponse(string phaseRequested, VideoNarrationScriptDto script, string outputPath)
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
            script.Scores.TtsReadinessScore >= 90);

    private static VideoAssemblyGenerationResponse BuildTtsResponse(
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
            audioValidation?.AudioRmsDb ?? 0);


    private static VideoAssemblyGenerationResponse BuildLongFormTtsResponse(
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
            ScenePresentationProfileUsed: ScenePresentationProfile.LongForm);


    private static VideoAssemblyGenerationResponse BuildAssemblyResponse(string phaseRequested, VideoAssemblyPlanDto plan, string outputPath, IReadOnlyList<string> generatedFiles)
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
            plan.RenderMusicPlan.DuckMusicUnderNarration);



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
            plan.GeneratedUtc
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
            warnings);
    }

    private static void EnsureShortFormRenderValidationPassed(VideoRenderValidationDto validation)
    {
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

    private static VideoAssemblyGenerationResponse BuildRenderResponse(
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
            renderPolish.MusicMixApplied);

    private string BuildQuestionEngineRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), QuestionEngineDirectoryName);

    private string BuildHeroAssetsRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), HeroAssetsDirectoryName);

    private string BuildThumbnailAssetsRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), ThumbnailAssetsDirectoryName);

    private string BuildVideoAssemblyRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), VideoAssemblyDirectoryName);

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
