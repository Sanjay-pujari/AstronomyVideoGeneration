using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class VideoAssemblyIntelligenceService(IOptions<RenderingOptions> renderingOptions) : IVideoAssemblyIntelligenceService
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
    private const string SelectedOpeningHook = "DON'T MISS THIS TONIGHT";
    private static readonly string[] RequiredApprovedSceneIds = ["scene-001", "scene-002", "scene-003", "scene-005", "scene-006"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<VideoAssemblyGenerationResponse> GenerateVideoAssemblyAsync(VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        if (string.Equals(request.Phase, "Script", StringComparison.OrdinalIgnoreCase))
            return await GenerateVideoNarrationScriptAsync(request, cancellationToken);

        if (!string.Equals(request.Phase, "Intelligence", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only video assembly phases 'Intelligence' and 'Script' are implemented in this endpoint version.", nameof(request));

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

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
