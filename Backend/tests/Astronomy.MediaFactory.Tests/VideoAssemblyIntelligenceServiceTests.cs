using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class VideoAssemblyIntelligenceServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string EventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateVideoAssemblyAsync_IntelligenceNonDryRunWritesVideoAssemblyIntelligenceOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Intelligence",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var outputPath = Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-assembly-intelligence.json");
        Assert.True(result.VideoAssemblyIntelligenceGenerated);
        Assert.Equal("Intelligence", result.PhaseRequested);
        Assert.Equal("Intelligence", result.PhaseExecuted);
        Assert.Equal(outputPath.Replace('\\', '/'), result.VideoAssemblyIntelligencePath);
        Assert.Equal("DON'T MISS THIS TONIGHT", result.SelectedOpeningHook);
        Assert.Equal(20.0, result.RecommendedTotalDurationSeconds);
        Assert.True(result.TtsRequired);
        Assert.True(result.FinalVideoPlanned);
        Assert.Empty(result.GeneratedFiles);
        Assert.True(File.Exists(outputPath));
        Assert.DoesNotContain(Directory.GetFiles(BuildVideoAssemblyRoot(workingDirectory)), path => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Directory.GetFiles(BuildVideoAssemblyRoot(workingDirectory)), path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase));

        var saved = JsonSerializer.Deserialize<VideoAssemblyIntelligenceDto>(await File.ReadAllTextAsync(outputPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(EventId, saved!.EventId);
        Assert.Equal(RegionId, saved.RegionId);
        Assert.Equal("en", saved.Language);
        Assert.Equal("YouTubeShort", saved.Platform);
        Assert.Equal("DON'T MISS THIS TONIGHT", saved.SelectedOpeningHook);
        Assert.Equal("ShortFormAstronomyAlert", saved.VideoIntent);
        Assert.Equal("Curiosity → Clarity → Action", saved.EmotionalArc);
        Assert.Equal(new[] { "Hook", "What", "Why", "Where", "When", "Action" }, saved.RecommendedSceneOrder);
        Assert.Equal(6, saved.RecommendedSceneDurations.Count);
        Assert.Equal(20.0, saved.RecommendedTotalDurationSeconds);
        Assert.True(saved.AudioPlan.TtsRequired);
        Assert.True(saved.OutputsPlanned.Contains("final-video.mp4"));
        Assert.True(saved.Scores.VideoAssemblyReadinessScore >= 90);
        Assert.Empty(saved.Warnings);
    }


    [Fact]
    public async Task GenerateVideoAssemblyAsync_ScriptNonDryRunWritesNarrationScriptOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Intelligence",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Script",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var outputPath = Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-narration-script.json");
        Assert.Equal("Script", result.PhaseRequested);
        Assert.Equal("Script", result.PhaseExecuted);
        Assert.True(result.VideoNarrationScriptGenerated);
        Assert.Equal(outputPath.Replace('\\', '/'), result.VideoNarrationScriptPath);
        Assert.Equal(20.0, result.TotalEstimatedDurationSeconds);
        Assert.True(result.TtsReady);
        Assert.Empty(result.GeneratedFiles);
        Assert.True(File.Exists(outputPath));
        Assert.DoesNotContain(Directory.GetFiles(BuildVideoAssemblyRoot(workingDirectory)), path => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Directory.GetFiles(BuildVideoAssemblyRoot(workingDirectory)), path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase));

        var saved = JsonSerializer.Deserialize<VideoNarrationScriptDto>(await File.ReadAllTextAsync(outputPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(EventId, saved!.EventId);
        Assert.Equal(RegionId, saved.RegionId);
        Assert.Equal("en", saved.Language);
        Assert.Equal("YouTubeShort", saved.Platform);
        Assert.Equal(20.0, saved.TotalEstimatedDurationSeconds);
        Assert.Equal("Excited but clear", saved.ScriptStyle.Tone);
        Assert.Equal(new[] { "Hook", "What", "Why", "Where", "When", "Action" }, saved.SceneScripts.Select(scene => scene.SceneKey));
        Assert.Equal("Don't miss this tonight.", saved.SceneScripts[0].Narration);
        Assert.Equal("Step outside tonight and look west.", saved.SceneScripts[^1].Narration);
        Assert.Equal("Don't miss this tonight. Venus and Jupiter will shine close together after sunset. Two of the brightest worlds will share the evening sky. Look toward the western sky. The best time is shortly after sunset. Step outside tonight and look west.", saved.FullNarrationText);
        Assert.True(saved.TtsPlan.TtsRequired);
        Assert.Equal("NeutralEnergetic", saved.TtsPlan.RecommendedVoice);
        Assert.Equal("video-tts-audio.mp3", saved.TtsPlan.OutputFileName);
        Assert.True(saved.Scores.TtsReadinessScore >= 90);
        Assert.Empty(saved.Warnings);
    }

    [Fact]
    public async Task GenerateVideoAssemblyAsync_DryRunReturnsPreviewPathWithoutWriting()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Intelligence",
            DryRun = true,
            OverwriteExisting = true
        }, CancellationToken.None);

        var outputPath = Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-assembly-intelligence.json");
        Assert.True(result.VideoAssemblyIntelligenceGenerated);
        Assert.Equal("DON'T MISS THIS TONIGHT", result.SelectedOpeningHook);
        Assert.Equal(outputPath.Replace('\\', '/'), result.VideoAssemblyIntelligencePath);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task GenerateVideoAssemblyAsync_TtsNonDryRunWritesAudioAndTimingsOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Intelligence",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Script",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var result = await service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Tts",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var audioPath = Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-tts-audio.mp3");
        var timingsPath = Path.Combine(BuildVideoAssemblyRoot(workingDirectory), "video-tts-timings.json");
        Assert.Equal("Tts", result.PhaseRequested);
        Assert.Equal("Tts", result.PhaseExecuted);
        Assert.True(result.TtsAudioGenerated);
        Assert.True(result.TtsTimingsGenerated);
        Assert.Equal(audioPath.Replace('\\', '/'), result.AudioFilePath);
        Assert.Equal(timingsPath.Replace('\\', '/'), result.TimingsFilePath);
        Assert.Equal(20.0, result.ActualDurationSeconds);
        Assert.True(File.Exists(audioPath));
        Assert.True(File.Exists(timingsPath));
        Assert.DoesNotContain(Directory.GetFiles(BuildVideoAssemblyRoot(workingDirectory)), path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase));

        var saved = JsonSerializer.Deserialize<VideoTtsTimingsDto>(await File.ReadAllTextAsync(timingsPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(EventId, saved!.EventId);
        Assert.Equal(RegionId, saved.RegionId);
        Assert.Equal("en", saved.Language);
        Assert.Equal("YouTubeShort", saved.Platform);
        Assert.Equal(audioPath.Replace('\\', '/'), saved.AudioFilePath);
        Assert.Equal(20.0, saved.EstimatedDurationSeconds);
        Assert.Equal(20.0, saved.ActualDurationSeconds);
        Assert.Equal(new[] { "Hook", "What", "Why", "Where", "When", "Action" }, saved.SceneTimings.Select(scene => scene.SceneKey));
        Assert.Equal(0.0, saved.SceneTimings[0].StartSeconds);
        Assert.Equal(3.0, saved.SceneTimings[0].EndSeconds);
        Assert.Equal("Don't miss this tonight.", saved.SceneTimings[0].Narration);
        Assert.Equal(20.0, saved.SceneTimings[^1].EndSeconds);
        Assert.Equal("SyntheticOfflineTtsV1", saved.TtsProvider);
        Assert.Equal("NeutralEnergetic", saved.VoiceUsed);
    }

    [Fact]
    public async Task GenerateVideoAssemblyAsync_RejectsUnimplementedPhases()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteRequiredInputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Platform = "YouTubeShort",
            Phase = "Render",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None));

        Assert.Contains("Only video assembly phases 'Intelligence', 'Script', and 'Tts'", error.Message);
    }

    private static VideoAssemblyIntelligenceService CreateService(string workingDirectory)
        => new(Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }));

    private static async Task WriteRequiredInputsAsync(string workingDirectory)
    {
        var heroAssetsRoot = BuildHeroAssetsRoot(workingDirectory);
        var thumbnailRoot = BuildThumbnailAssetsRoot(workingDirectory);
        Directory.CreateDirectory(heroAssetsRoot);
        Directory.CreateDirectory(thumbnailRoot);

        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-story.json"), JsonSerializer.Serialize(new
        {
            eventId = EventId,
            heroHook = "LOOK WEST TONIGHT",
            summary = "Venus and Jupiter appear close together after sunset."
        }, JsonOptions));

        var heroSceneManifest = new HeroSceneManifestDto(
            EventId,
            new HeroSceneManifestEntryDto(1, "What", BuildApprovedScenePath(workingDirectory, "scene-001"), "PrimaryVisual"),
            new HeroSceneManifestEntryDto(6, "Action", BuildApprovedScenePath(workingDirectory, "scene-006"), "CallToAction"),
            new HeroSceneManifestEntryDto(2, "Where", BuildApprovedScenePath(workingDirectory, "scene-002"), "DirectionCue"),
            "Use What scene as visual anchor, Action scene for CTA, and Where scene for direction cue.");

        var heroCompositionModel = new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto("LOOK WEST TONIGHT"),
            new HeroCompositionSceneBlockDto("scene-001"),
            new HeroCompositionTextBlockDto("scene-001", "WEST"),
            new HeroCompositionTextBlockDto("scene-001", "AFTER SUNSET"),
            new HeroCompositionTextBlockDto("scene-001", "LOOK WEST"),
            new HeroCompositionValidationDto(true, true, true, true, true, 100));

        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-scene-manifest.json"), JsonSerializer.Serialize(heroSceneManifest, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-composition-model.json"), JsonSerializer.Serialize(heroCompositionModel, JsonOptions));

        var thumbnailIntelligence = new ThumbnailIntelligenceDto(
            EventId,
            RegionId,
            "en",
            "DON'T MISS THIS TONIGHT",
            ["VENUS AND JUPITER TONIGHT"],
            [new ThumbnailHookScoreDto("DON'T MISS THIS TONIGHT", 100, 95, 91, 87, 94)],
            "Curiosity + Wonder",
            "High",
            "A time-sensitive sky moment that feels easy to miss unless the viewer clicks now.",
            "Large Venus and Jupiter close together above twilight horizon.",
            "Bold emotional astronomy thumbnail with minimal text and twilight contrast.",
            "HeroCompositionModel + PrimaryScene",
            "scene-001",
            [],
            new ThumbnailCopyDto("DON'T MISS THIS TONIGHT", "Venus + Jupiter", "After Sunset"),
            [new ThumbnailPlatformTargetDto("YouTube", "1280x720", "Click")],
            new ThumbnailReadinessScoresDto(100, 95, 91, 87, 94),
            [],
            DateTimeOffset.UtcNow);

        var thumbnailCompositionModel = new ThumbnailCompositionModelDto(
            EventId,
            RegionId,
            "en",
            "DON'T MISS THIS TONIGHT",
            "Venus + Jupiter",
            "After Sunset",
            "Curiosity",
            "High",
            "ScrollStoppingAstronomyThumbnail",
            "Large Venus and Jupiter close together above twilight horizon.",
            new ThumbnailCompositionBlocksDto(
                new ThumbnailCompositionTextBlockDto("DON'T MISS THIS TONIGHT", 1),
                new ThumbnailCompositionVisualBlockDto("HeroCompositionModel + PrimaryScene", 2),
                new ThumbnailCompositionTextBlockDto("Venus + Jupiter", 3),
                new ThumbnailCompositionTextBlockDto("After Sunset", 4)),
            [new ThumbnailCompositionPlatformVariantDto("Landscape", "1280x720", "YouTubeThumbnail")],
            new ThumbnailCompositionValidationDto(true, true, 3, 94),
            DateTimeOffset.UtcNow);

        var thumbnailSceneManifest = new ThumbnailSceneManifestDto(
            EventId,
            new ThumbnailSceneManifestEntryDto(1, "What", BuildApprovedScenePath(workingDirectory, "scene-001"), "PrimaryVisual"),
            new ThumbnailSceneManifestEntryDto(5, "Why", BuildApprovedScenePath(workingDirectory, "scene-005"), "EmotionalSignificance"),
            new ThumbnailSceneManifestEntryDto(6, "Action", BuildApprovedScenePath(workingDirectory, "scene-006"), "UrgencyCue"),
            "Use What scene for visual focus, Why scene for emotional pull, and Action scene for urgency.");

        await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, "thumbnail-intelligence.json"), JsonSerializer.Serialize(thumbnailIntelligence, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, "thumbnail-composition-model.json"), JsonSerializer.Serialize(thumbnailCompositionModel, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, "thumbnail-scene-manifest.json"), JsonSerializer.Serialize(thumbnailSceneManifest, JsonOptions));

        await WriteApprovedSceneOutputsAsync(workingDirectory);
    }

    private static async Task WriteApprovedSceneOutputsAsync(string workingDirectory)
    {
        var sceneApprovalRoot = Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine", "scene-approval-v3");
        Directory.CreateDirectory(sceneApprovalRoot);
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        foreach (var sceneId in new[] { "scene-001", "scene-002", "scene-003", "scene-005", "scene-006" })
            await File.WriteAllBytesAsync(Path.Combine(sceneApprovalRoot, $"{sceneId}-final.png"), pngBytes);
    }

    private static string BuildApprovedScenePath(string workingDirectory, string sceneId)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine", "scene-approval-v3", $"{sceneId}-final.png").Replace('\\', '/');

    private static string BuildHeroAssetsRoot(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "hero-assets");

    private static string BuildThumbnailAssetsRoot(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "thumbnail-assets");

    private static string BuildVideoAssemblyRoot(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "video-assembly");

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "video-assembly-intelligence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
