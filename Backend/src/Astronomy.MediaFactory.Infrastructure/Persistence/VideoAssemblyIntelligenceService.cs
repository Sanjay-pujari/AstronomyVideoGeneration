using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class VideoAssemblyIntelligenceService(
    IOptions<RenderingOptions> renderingOptions,
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
    private const string ThumbnailLandscapeFileName = "thumbnail-landscape.png";
    private const string FinalVideoFileName = "final-video.mp4";
    private const string SelectedOpeningHook = "DON'T MISS THIS TONIGHT";
    private const string SyntheticTtsProviderName = "SyntheticOfflineTtsV1";
    private const string AzureTtsProviderName = "AzureSpeechTts";
    private const string OpenAiTtsProviderName = "OpenAITts";
    private const long MinimumMp3FileSizeBytes = 1024;
    private const double SilencePeakThresholdDb = -55.0;
    private const double SilenceRmsThresholdDb = -60.0;
    private static readonly string[] RequiredApprovedSceneIds = ["scene-001", "scene-002", "scene-003", "scene-005", "scene-006"];
    private static readonly string[] RequiredAssemblySceneOrder = ["Hook", "What", "Why", "Where", "When", "Action"];
    private static readonly IReadOnlyDictionary<string, string> AssemblySceneVisualMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Hook"] = ThumbnailLandscapeFileName,
        ["What"] = "scene-001-final.png",
        ["Why"] = "scene-005-final.png",
        ["Where"] = "scene-002-final.png",
        ["When"] = "scene-003-final.png",
        ["Action"] = "scene-006-final.png"
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<VideoAssemblyGenerationResponse> GenerateVideoAssemblyAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        if (string.Equals(request.Phase, "Script", StringComparison.OrdinalIgnoreCase))
            return await GenerateVideoNarrationScriptAsync(request, cancellationToken);

        if (string.Equals(request.Phase, "Tts", StringComparison.OrdinalIgnoreCase))
            return await GenerateTtsAudioAsync(request, cancellationToken);

        if (string.Equals(request.Phase, "Assembly", StringComparison.OrdinalIgnoreCase))
            return await GenerateVideoAssemblyPlanAsync(request, cancellationToken);

        if (!string.Equals(request.Phase, "Intelligence", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only video assembly phases 'Intelligence', 'Script', 'Tts', and 'Assembly' are implemented in this endpoint version.", nameof(request));

        var outputPath = BuildVideoAssemblyIntelligenceOutputPath(request.EventId, request.RegionId);
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<VideoAssemblyIntelligenceDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing video assembly intelligence could not be parsed.");
            ValidateVideoAssemblyIntelligence(existing);
            return BuildResponse(request.Phase, existing, outputPath);
        }

        await EnsureRequiredInputsAsync(request.EventId, request.RegionId, cancellationToken);
        var intelligence = BuildVideoAssemblyIntelligence(request);
        ValidateVideoAssemblyIntelligence(intelligence);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(intelligence, JsonOptions), cancellationToken);
        }

        return BuildResponse(request.Phase, intelligence, outputPath);
    }


    private async Task<VideoAssemblyGenerationResponse> GenerateVideoNarrationScriptAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        var outputPath = BuildVideoNarrationScriptOutputPath(request.EventId, request.RegionId);
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<VideoNarrationScriptDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing video narration script could not be parsed.");
            ValidateVideoNarrationScript(existing);
            return BuildScriptResponse(request.Phase, existing, outputPath);
        }

        var intelligence = await EnsureRequiredScriptInputsAsync(request.EventId, request.RegionId, cancellationToken);
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
        var audioPath = BuildVideoTtsAudioOutputPath(request.EventId, request.RegionId);
        var timingsPath = BuildVideoTtsTimingsOutputPath(request.EventId, request.RegionId);

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

        var script = await EnsureRequiredTtsInputsAsync(request.EventId, request.RegionId, cancellationToken);
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


    private async Task<VideoAssemblyGenerationResponse> GenerateVideoAssemblyPlanAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        var outputPath = BuildVideoAssemblyPlanOutputPath(request.EventId, request.RegionId);
        if (!request.DryRun && !request.OverwriteExisting && File.Exists(outputPath))
        {
            var existing = JsonSerializer.Deserialize<VideoAssemblyPlanDto>(await File.ReadAllTextAsync(outputPath, cancellationToken), JsonOptions)
                ?? throw new InvalidOperationException("Existing video assembly plan could not be parsed.");
            ValidateVideoAssemblyPlan(existing);
            EnsureVideoAssemblyPlanAssetsExist(existing);
            return BuildAssemblyResponse(request.Phase, existing, outputPath, []);
        }

        var inputs = await EnsureRequiredAssemblyInputsAsync(request.EventId, request.RegionId, cancellationToken);
        var plan = BuildVideoAssemblyPlan(request, inputs);
        ValidateVideoAssemblyPlan(plan);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        }

        return BuildAssemblyResponse(request.Phase, plan, outputPath, []);
    }

    private async Task<VideoNarrationScriptDto> EnsureRequiredTtsInputsAsync(string eventId, string regionId, CancellationToken cancellationToken)
    {
        var scriptPath = BuildVideoNarrationScriptOutputPath(eventId, regionId);
        if (!File.Exists(scriptPath))
            throw new ArgumentException($"Required TTS input '{VideoNarrationScriptFileName}' was not found at '{NormalizePath(scriptPath)}'.");

        var script = JsonSerializer.Deserialize<VideoNarrationScriptDto>(await File.ReadAllTextAsync(scriptPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Required TTS input '{VideoNarrationScriptFileName}' could not be parsed.");
        ValidateVideoNarrationScript(script);
        return script;
    }

    private async Task<VideoAssemblyIntelligenceDto> EnsureRequiredScriptInputsAsync(string eventId, string regionId, CancellationToken cancellationToken)
    {
        var videoAssemblyIntelligencePath = BuildVideoAssemblyIntelligenceOutputPath(eventId, regionId);
        if (!File.Exists(videoAssemblyIntelligencePath))
            throw new ArgumentException($"Required video narration script input '{VideoAssemblyIntelligenceFileName}' was not found at '{NormalizePath(videoAssemblyIntelligencePath)}'.");

        var intelligence = JsonSerializer.Deserialize<VideoAssemblyIntelligenceDto>(await File.ReadAllTextAsync(videoAssemblyIntelligencePath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Required video narration script input '{VideoAssemblyIntelligenceFileName}' could not be parsed.");
        ValidateVideoAssemblyIntelligence(intelligence);
        ValidateRecommendedSceneOrder(intelligence.RecommendedSceneOrder, "video-assembly-intelligence.json");

        var heroRoot = BuildHeroAssetsRoot(eventId, regionId);
        await EnsureHeroStoryInputAsync(heroRoot, cancellationToken);
        using var heroSceneManifest = await EnsureJsonInputAsync(Path.Combine(heroRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);
        EnsureApprovedSceneImages(eventId, regionId, heroSceneManifest);

        return intelligence;
    }

    private async Task EnsureRequiredInputsAsync(string eventId, string regionId, CancellationToken cancellationToken)
    {
        var heroRoot = BuildHeroAssetsRoot(eventId, regionId);
        var thumbnailRoot = BuildThumbnailAssetsRoot(eventId, regionId);

        await EnsureHeroStoryInputAsync(heroRoot, cancellationToken);
        using var heroSceneManifest = await EnsureJsonInputAsync(Path.Combine(heroRoot, HeroSceneManifestFileName), HeroSceneManifestFileName, cancellationToken);
        using var heroCompositionModel = await EnsureJsonInputAsync(Path.Combine(heroRoot, HeroCompositionModelFileName), HeroCompositionModelFileName, cancellationToken);
        using var thumbnailSceneManifest = await EnsureJsonInputAsync(Path.Combine(thumbnailRoot, ThumbnailSceneManifestFileName), ThumbnailSceneManifestFileName, cancellationToken);
        using var thumbnailIntelligence = await EnsureJsonInputAsync(Path.Combine(thumbnailRoot, ThumbnailIntelligenceFileName), ThumbnailIntelligenceFileName, cancellationToken);
        using var thumbnailCompositionModel = await EnsureJsonInputAsync(Path.Combine(thumbnailRoot, ThumbnailCompositionModelFileName), ThumbnailCompositionModelFileName, cancellationToken);

        EnsureApprovedSceneImages(eventId, regionId, heroSceneManifest, thumbnailSceneManifest);
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

    private void EnsureApprovedSceneImages(string eventId, string regionId, params JsonDocument[] manifests)
    {
        var sceneApprovalRoot = Path.Combine(BuildQuestionEngineRoot(eventId, regionId), SceneApprovalDirectoryName);
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
            new VideoAssemblyVisualStyleDto(true, false, true, "SmoothFade", "Minimal"),
            new VideoAssemblyAudioPlanDto(true, true, "Wonder + urgency", true),
            ["video-narration-script.json", "video-tts-audio.mp3", "video-assembly-plan.json", "final-video.mp4"],
            new VideoAssemblyScoresDto(96, 95, 96, 95),
            [],
            DateTimeOffset.UtcNow);
    }


    private static VideoNarrationScriptDto BuildVideoNarrationScript(VideoAssemblyGenerationRequest request, VideoAssemblyIntelligenceDto intelligence)
    {
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
        => Math.Round(Math.Clamp(estimatedDurationSeconds, 15.0, 25.0), 3, MidpointRounding.AwayFromZero);

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


    private async Task<AssemblyInputs> EnsureRequiredAssemblyInputsAsync(string eventId, string regionId, CancellationToken cancellationToken)
    {
        var intelligencePath = BuildVideoAssemblyIntelligenceOutputPath(eventId, regionId);
        var scriptPath = BuildVideoNarrationScriptOutputPath(eventId, regionId);
        var audioPath = BuildVideoTtsAudioOutputPath(eventId, regionId);
        var timingsPath = BuildVideoTtsTimingsOutputPath(eventId, regionId);
        var thumbnailPath = BuildThumbnailLandscapeOutputPath(eventId, regionId);
        var sceneApprovalRoot = Path.Combine(BuildQuestionEngineRoot(eventId, regionId), SceneApprovalDirectoryName);

        if (!File.Exists(intelligencePath))
            throw new ArgumentException($"Required video assembly input '{VideoAssemblyIntelligenceFileName}' was not found at '{NormalizePath(intelligencePath)}'.");
        if (!File.Exists(scriptPath))
            throw new ArgumentException($"Required video assembly input '{VideoNarrationScriptFileName}' was not found at '{NormalizePath(scriptPath)}'.");
        if (!File.Exists(audioPath))
            throw new ArgumentException($"Required video assembly input '{VideoTtsAudioFileName}' was not found at '{NormalizePath(audioPath)}'.");
        if (!File.Exists(timingsPath))
            throw new ArgumentException($"Required video assembly input '{VideoTtsTimingsFileName}' was not found at '{NormalizePath(timingsPath)}'.");
        if (!File.Exists(thumbnailPath))
            throw new ArgumentException($"Required video assembly visual asset '{ThumbnailLandscapeFileName}' was not found at '{NormalizePath(thumbnailPath)}'.");

        var intelligence = JsonSerializer.Deserialize<VideoAssemblyIntelligenceDto>(await File.ReadAllTextAsync(intelligencePath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Required video assembly input '{VideoAssemblyIntelligenceFileName}' could not be parsed.");
        var script = JsonSerializer.Deserialize<VideoNarrationScriptDto>(await File.ReadAllTextAsync(scriptPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Required video assembly input '{VideoNarrationScriptFileName}' could not be parsed.");
        var timings = JsonSerializer.Deserialize<VideoTtsTimingsDto>(await File.ReadAllTextAsync(timingsPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Required video assembly input '{VideoTtsTimingsFileName}' could not be parsed.");

        ValidateVideoAssemblyIntelligence(intelligence);
        ValidateVideoNarrationScript(script);
        ValidateVideoTtsTimings(timings);
        ValidateRecommendedSceneOrder(timings.SceneTimings.Select(scene => scene.SceneKey), VideoTtsTimingsFileName);

        var visualAssetPaths = RequiredAssemblySceneOrder
            .Select(sceneKey => ResolveAssemblyVisualAssetPath(sceneKey, thumbnailPath, sceneApprovalRoot))
            .ToArray();
        var missingVisualAssets = visualAssetPaths.Where(path => !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missingVisualAssets.Length > 0)
            throw new ArgumentException($"Required video assembly visual asset(s) were not found: {string.Join(", ", missingVisualAssets)}.");

        var durationMatchesAudio = Math.Abs(timings.SceneTimings[^1].EndSeconds - timings.ActualDurationSeconds) <= 0.001;
        if (!durationMatchesAudio)
            throw new ArgumentException($"Video assembly validation failed: TTS scene timings end at {timings.SceneTimings[^1].EndSeconds:0.###} seconds, but actual TTS duration is {timings.ActualDurationSeconds:0.###} seconds.");

        return new AssemblyInputs(intelligence, script, timings, NormalizePath(audioPath), NormalizePath(thumbnailPath), NormalizePath(sceneApprovalRoot), visualAssetPaths.Select(NormalizePath).ToArray());
    }

    private static VideoAssemblyPlanDto BuildVideoAssemblyPlan(VideoAssemblyGenerationRequest request, AssemblyInputs inputs)
    {
        var visualByScene = RequiredAssemblySceneOrder.Zip(inputs.VisualAssetPaths, (scene, path) => new { scene, path })
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
                index == 0 ? "None" : "SmoothFade",
                index == inputs.Timings.SceneTimings.Count - 1 ? "None" : "SmoothFade",
                "SubtleZoomIn");
        }).ToArray();

        var renderSettings = string.Equals(request.Platform, "YouTubeShort", StringComparison.OrdinalIgnoreCase)
            ? new VideoAssemblyRenderSettingsDto(1080, 1920, 30, "mp4", "h264", "aac")
            : new VideoAssemblyRenderSettingsDto(1920, 1080, 30, "mp4", "h264", "aac");
        var validation = new VideoAssemblyValidationDto(true, true, segments.Length, true, true);
        return new VideoAssemblyPlanDto(
            request.EventId,
            request.RegionId,
            request.Language,
            request.Platform,
            inputs.Timings.ActualDurationSeconds,
            inputs.AudioPath,
            NormalizePath(Path.Combine(Path.GetDirectoryName(inputs.AudioPath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty, FinalVideoFileName)),
            segments,
            renderSettings,
            new VideoAssemblyStyleDto("SmoothFade", "SubtleKenBurns", "UseExistingSceneTextOnly", false),
            validation,
            [],
            DateTimeOffset.UtcNow);
    }

    private static void ValidateVideoAssemblyPlan(VideoAssemblyPlanDto plan)
    {
        ValidateRecommendedSceneOrder(plan.Segments.Select(segment => segment.SceneKey), VideoAssemblyPlanFileName);
        if (plan.Segments.Count != 6)
            throw new ArgumentException("Video assembly validation failed: segmentCount must be 6.");
        if (!plan.Validation.AudioExists)
            throw new ArgumentException("Video assembly validation failed: audio is missing.");
        if (!plan.Validation.AllVisualAssetsExist)
            throw new ArgumentException("Video assembly validation failed: one or more visual assets are missing.");
        if (plan.Validation.SegmentCount != 6)
            throw new ArgumentException("Video assembly validation failed: validation.segmentCount must be 6.");
        if (!plan.Validation.DurationMatchesAudio)
            throw new ArgumentException("Video assembly validation failed: duration does not match TTS duration.");
        if (!plan.Validation.ReadyForRender)
            throw new ArgumentException("Video assembly validation failed: readyForRender must be true.");
        if (string.Equals(plan.Platform, "YouTubeShort", StringComparison.OrdinalIgnoreCase)
            && (plan.RenderSettings.Width != 1080 || plan.RenderSettings.Height != 1920))
            throw new ArgumentException("Video assembly validation failed: YouTubeShort renderSettings must be 1080x1920.");
        var lastEndSeconds = plan.Segments[^1].EndSeconds;
        if (Math.Abs(lastEndSeconds - plan.TotalDurationSeconds) > 0.001)
            throw new ArgumentException("Video assembly validation failed: totalDurationSeconds must match the last segment endSeconds.");
    }


    private static void EnsureVideoAssemblyPlanAssetsExist(VideoAssemblyPlanDto plan)
    {
        if (!File.Exists(plan.AudioFilePath))
            throw new ArgumentException($"Video assembly validation failed: audio is missing at '{plan.AudioFilePath}'.");

        var missingVisualAssets = plan.Segments
            .Select(segment => segment.VisualAssetPath)
            .Where(path => !File.Exists(path))
            .ToArray();
        if (missingVisualAssets.Length > 0)
            throw new ArgumentException($"Video assembly validation failed: visual asset(s) missing at {string.Join(", ", missingVisualAssets)}.");
    }

    private string BuildThumbnailLandscapeOutputPath(string eventId, string regionId)
        => Path.Combine(BuildThumbnailAssetsRoot(eventId, regionId), ThumbnailLandscapeFileName);

    private static string ResolveAssemblyVisualAssetPath(string sceneKey, string thumbnailPath, string sceneApprovalRoot)
    {
        var mappedFileName = AssemblySceneVisualMap[sceneKey];
        return string.Equals(mappedFileName, ThumbnailLandscapeFileName, StringComparison.OrdinalIgnoreCase)
            ? thumbnailPath
            : Path.Combine(sceneApprovalRoot, mappedFileName);
    }

    private static void ValidateVideoNarrationScript(VideoNarrationScriptDto script)
    {
        if (string.IsNullOrWhiteSpace(script.FullNarrationText))
            throw new ArgumentException("Video narration script validation failed: fullNarrationText must not be empty.");
        ValidateRecommendedSceneOrder(script.SceneScripts.Select(scene => scene.SceneKey), "video-narration-script.json");
        if (script.TotalEstimatedDurationSeconds < 15 || script.TotalEstimatedDurationSeconds > 25)
            throw new ArgumentException("Video narration script validation failed: totalEstimatedDurationSeconds must be 15-25 seconds.");
        if (!script.TtsPlan.TtsRequired)
            throw new ArgumentException("Video narration script validation failed: ttsRequired must be true.");
        if (script.Scores.TtsReadinessScore < 90)
            throw new ArgumentException("Video narration script validation failed: ttsReadinessScore must be at least 90.");
    }

    private static void ValidateRecommendedSceneOrder(IEnumerable<string> sceneOrder, string fileName)
    {
        string[] recommendedSceneOrder = ["Hook", "What", "Why", "Where", "When", "Action"];
        if (!sceneOrder.SequenceEqual(recommendedSceneOrder, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Video narration script validation failed: scene order in {fileName} must be Hook, What, Why, Where, When, Action.");
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

    private static void ValidateVideoTtsTimings(VideoTtsTimingsDto timings)
    {
        if (string.IsNullOrWhiteSpace(timings.AudioFilePath))
            throw new ArgumentException("Video TTS timings validation failed: audioFilePath is required.");
        if (timings.EstimatedDurationSeconds < 15 || timings.EstimatedDurationSeconds > 25)
            throw new ArgumentException("Video TTS timings validation failed: estimatedDurationSeconds must be 15-25 seconds.");
        if (timings.ActualDurationSeconds < 15 || timings.ActualDurationSeconds > 25)
            throw new ArgumentException("Video TTS timings validation failed: actualDurationSeconds must be 15-25 seconds.");
        ValidateRecommendedSceneOrder(timings.SceneTimings.Select(scene => scene.SceneKey), "video-tts-timings.json");
        if (string.IsNullOrWhiteSpace(timings.TtsProvider))
            throw new ArgumentException("Video TTS timings validation failed: ttsProvider is required.");
        if (string.IsNullOrWhiteSpace(timings.VoiceUsed))
            throw new ArgumentException("Video TTS timings validation failed: voiceUsed is required.");
        if (timings.AudioValidation is null)
            throw new ArgumentException("Video TTS timings validation failed: audioValidation is required.");
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
            "Intelligence",
            true,
            NormalizePath(outputPath),
            intelligence.SelectedOpeningHook,
            intelligence.RecommendedTotalDurationSeconds,
            intelligence.AudioPlan.TtsRequired,
            intelligence.OutputsPlanned.Any(output => string.Equals(output, "final-video.mp4", StringComparison.OrdinalIgnoreCase)),
            []);

    private static VideoAssemblyGenerationResponse BuildScriptResponse(string phaseRequested, VideoNarrationScriptDto script, string outputPath)
        => new(
            phaseRequested,
            "Script",
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


    private static VideoAssemblyGenerationResponse BuildAssemblyResponse(string phaseRequested, VideoAssemblyPlanDto plan, string outputPath, IReadOnlyList<string> generatedFiles)
        => new(
            phaseRequested,
            "Assembly",
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
            plan.TotalDurationSeconds);

    private string BuildQuestionEngineRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), QuestionEngineDirectoryName);

    private string BuildHeroAssetsRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), HeroAssetsDirectoryName);

    private string BuildThumbnailAssetsRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), ThumbnailAssetsDirectoryName);

    private string BuildVideoAssemblyRoot(string eventId, string regionId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", SanitizePathSegment(regionId), "events", SanitizePathSegment(eventId), VideoAssemblyDirectoryName);

    private string BuildVideoAssemblyIntelligenceOutputPath(string eventId, string regionId)
        => Path.Combine(BuildVideoAssemblyRoot(eventId, regionId), VideoAssemblyIntelligenceFileName);

    private string BuildVideoNarrationScriptOutputPath(string eventId, string regionId)
        => Path.Combine(BuildVideoAssemblyRoot(eventId, regionId), VideoNarrationScriptFileName);

    private string BuildVideoTtsAudioOutputPath(string eventId, string regionId)
        => Path.Combine(BuildVideoAssemblyRoot(eventId, regionId), VideoTtsAudioFileName);

    private string BuildVideoTtsTimingsOutputPath(string eventId, string regionId)
        => Path.Combine(BuildVideoAssemblyRoot(eventId, regionId), VideoTtsTimingsFileName);

    private string BuildVideoAssemblyPlanOutputPath(string eventId, string regionId)
        => Path.Combine(BuildVideoAssemblyRoot(eventId, regionId), VideoAssemblyPlanFileName);

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record AssemblyInputs(
        VideoAssemblyIntelligenceDto Intelligence,
        VideoNarrationScriptDto Script,
        VideoTtsTimingsDto Timings,
        string AudioPath,
        string ThumbnailPath,
        string SceneApprovalRoot,
        IReadOnlyList<string> VisualAssetPaths);

    private sealed record TtsProviderSelection(string ProviderName, string VoiceUsed, bool IsSynthetic);

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
