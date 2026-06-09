using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Tests;

public sealed class ThumbnailAssetIntelligenceServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string EventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateThumbnailAssetsAsync_IntelligenceNonDryRunWritesThumbnailIntelligenceOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteHeroInputFilesAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var result = await service.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Phase = "Intelligence",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var thumbnailRoot = BuildThumbnailAssetsRoot(workingDirectory);
        var outputPath = Path.Combine(thumbnailRoot, "thumbnail-intelligence.json");
        Assert.True(result.ThumbnailIntelligenceGenerated);
        Assert.Equal("Intelligence", result.PhaseRequested);
        Assert.Equal("Intelligence", result.PhaseExecuted);
        Assert.Equal("DON'T MISS THIS TONIGHT", result.SelectedThumbnailHook);
        Assert.True(result.ThumbnailReadinessScore >= 90);
        Assert.Equal(outputPath.Replace('\\', '/'), result.ThumbnailIntelligencePath);
        Assert.Empty(result.GeneratedFiles);
        Assert.True(File.Exists(outputPath));
        Assert.DoesNotContain(Directory.GetFiles(thumbnailRoot), path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase));

        var saved = JsonSerializer.Deserialize<ThumbnailIntelligenceDto>(await File.ReadAllTextAsync(outputPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(EventId, saved!.EventId);
        Assert.Equal(RegionId, saved.RegionId);
        Assert.Equal("en", saved.Language);
        Assert.Equal("DON'T MISS THIS TONIGHT", saved.SelectedThumbnailHook);
        Assert.Contains("TWO BRIGHT PLANETS TOGETHER", saved.AlternativeThumbnailHooks);
        Assert.Contains("VENUS AND JUPITER TONIGHT", saved.AlternativeThumbnailHooks);
        Assert.Equal("Curiosity + Wonder", saved.Emotion);
        Assert.Equal("High", saved.ClickIntent);
        Assert.Equal("Large Venus and Jupiter close together above twilight horizon.", saved.VisualFocus);
        Assert.Equal("HeroCompositionModel + PrimaryScene", saved.RecommendedVisualSource);
        Assert.Equal("scene-001", saved.RecommendedSourceScene);
        Assert.Equal("DON'T MISS THIS TONIGHT", saved.ThumbnailCopy.PrimaryText);
        Assert.Equal("Venus + Jupiter", saved.ThumbnailCopy.SecondaryText);
        Assert.Equal("After Sunset", saved.ThumbnailCopy.MicroText);
        Assert.Equal(3, saved.PlatformTargets.Count);
        Assert.Empty(saved.Warnings);
        Assert.True(saved.Scores.ClickabilityScore >= 90);
        Assert.True(saved.Scores.ThumbnailReadinessScore >= 90);
    }

    [Fact]
    public async Task GenerateThumbnailAssetsAsync_DryRunReturnsPreviewPathWithoutWriting()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteHeroInputFilesAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var result = await service.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Phase = "Intelligence",
            DryRun = true,
            OverwriteExisting = true
        }, CancellationToken.None);

        var outputPath = Path.Combine(BuildThumbnailAssetsRoot(workingDirectory), "thumbnail-intelligence.json");
        Assert.True(result.ThumbnailIntelligenceGenerated);
        Assert.Equal("DON'T MISS THIS TONIGHT", result.SelectedThumbnailHook);
        Assert.Empty(result.GeneratedFiles);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task GenerateThumbnailAssetsAsync_CompositionWritesReusableThumbnailCompositionModel()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteHeroInputFilesAsync(workingDirectory);
        await WriteThumbnailIntelligenceInputAsync(workingDirectory);
        await WriteApprovedSceneOutputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var result = await service.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Phase = "Composition",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var thumbnailRoot = BuildThumbnailAssetsRoot(workingDirectory);
        var outputPath = Path.Combine(thumbnailRoot, "thumbnail-composition-model.json");
        Assert.True(result.ThumbnailCompositionGenerated);
        Assert.Equal("Composition", result.PhaseRequested);
        Assert.Equal("Composition", result.PhaseExecuted);
        Assert.True(result.ThumbnailCompositionReadinessScore >= 90);
        Assert.Equal(outputPath.Replace('\\', '/'), result.ThumbnailCompositionPath);
        Assert.Empty(result.GeneratedFiles);
        Assert.True(File.Exists(outputPath));
        Assert.DoesNotContain(Directory.GetFiles(thumbnailRoot), path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase));

        var saved = JsonSerializer.Deserialize<ThumbnailCompositionModelDto>(await File.ReadAllTextAsync(outputPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(EventId, saved!.EventId);
        Assert.Equal(RegionId, saved.RegionId);
        Assert.Equal("en", saved.Language);
        Assert.Equal("DON\'T MISS THIS TONIGHT", saved.PrimaryHook);
        Assert.Equal("Venus + Jupiter", saved.SecondaryText);
        Assert.Equal("After Sunset", saved.MicroText);
        Assert.Equal("Curiosity", saved.Emotion);
        Assert.Equal("High", saved.ClickIntent);
        Assert.Equal("ScrollStoppingAstronomyThumbnail", saved.LayoutStyle);
        Assert.Equal("Large Venus and Jupiter close together above twilight horizon.", saved.VisualFocus);
        Assert.Equal("DON\'T MISS THIS TONIGHT", saved.CompositionBlocks.HookBlock.Text);
        Assert.Equal(1, saved.CompositionBlocks.HookBlock.Priority);
        Assert.Equal("HeroCompositionModel + PrimaryScene", saved.CompositionBlocks.VisualBlock.Source);
        Assert.Equal(2, saved.CompositionBlocks.VisualBlock.Priority);
        Assert.Equal(3, saved.PlatformVariants.Count);
        Assert.True(saved.Validation.HookPresent);
        Assert.True(saved.Validation.VisualFocusPresent);
        Assert.Equal(3, saved.Validation.TextElementCount);
        Assert.True(saved.Validation.ThumbnailCompositionReadinessScore >= 90);
    }

    [Fact]
    public async Task GenerateThumbnailAssetsAsync_SceneSelectionWritesThumbnailSceneManifestOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteHeroInputFilesAsync(workingDirectory);
        await WriteThumbnailIntelligenceInputAsync(workingDirectory);
        await WriteThumbnailCompositionInputAsync(workingDirectory);
        await WriteApprovedSceneOutputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var result = await service.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Phase = "SceneSelection",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var thumbnailRoot = BuildThumbnailAssetsRoot(workingDirectory);
        var outputPath = Path.Combine(thumbnailRoot, "thumbnail-scene-manifest.json");
        Assert.True(result.ThumbnailSceneManifestGenerated);
        Assert.Equal("SceneSelection", result.PhaseRequested);
        Assert.Equal("SceneSelection", result.PhaseExecuted);
        Assert.Equal(outputPath.Replace('\\', '/'), result.ThumbnailSceneManifestPath);
        Assert.Equal("scene-001", result.PrimaryScene);
        Assert.Equal("scene-005", result.SecondaryScene);
        Assert.Equal("scene-006", result.SupportScene);
        Assert.Empty(result.GeneratedFiles);
        Assert.True(File.Exists(outputPath));
        Assert.DoesNotContain(Directory.GetFiles(thumbnailRoot), path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase));

        var saved = JsonSerializer.Deserialize<ThumbnailSceneManifestDto>(await File.ReadAllTextAsync(outputPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(EventId, saved!.EventId);
        Assert.Equal(1, saved.PrimaryScene.SceneNumber);
        Assert.Equal("What", saved.PrimaryScene.SceneKey);
        Assert.EndsWith("scene-001-final.png", saved.PrimaryScene.ImagePath);
        Assert.Equal("PrimaryVisual", saved.PrimaryScene.Role);
        Assert.Equal(5, saved.SecondaryScene.SceneNumber);
        Assert.Equal("Why", saved.SecondaryScene.SceneKey);
        Assert.EndsWith("scene-005-final.png", saved.SecondaryScene.ImagePath);
        Assert.Equal("EmotionalSignificance", saved.SecondaryScene.Role);
        Assert.Equal(6, saved.SupportScene.SceneNumber);
        Assert.Equal("Action", saved.SupportScene.SceneKey);
        Assert.EndsWith("scene-006-final.png", saved.SupportScene.ImagePath);
        Assert.Equal("UrgencyCue", saved.SupportScene.Role);
        Assert.Equal("Use What scene for visual focus, Why scene for emotional pull, and Action scene for urgency.", saved.SelectionReason);
    }

    [Fact]
    public async Task GenerateThumbnailAssetsAsync_ImageGenerationWritesThumbnailPngsAndValidationOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteHeroInputFilesAsync(workingDirectory);
        await WriteThumbnailIntelligenceInputAsync(workingDirectory);
        await WriteThumbnailCompositionInputAsync(workingDirectory);
        await WriteApprovedSceneOutputsAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var sceneSelection = await service.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Phase = "SceneSelection",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);
        Assert.True(sceneSelection.ThumbnailSceneManifestGenerated);

        var result = await service.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Phase = "ImageGeneration",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None);

        var thumbnailRoot = BuildThumbnailAssetsRoot(workingDirectory);
        var landscapePath = Path.Combine(thumbnailRoot, "thumbnail-landscape.png");
        var squarePath = Path.Combine(thumbnailRoot, "thumbnail-square.png");
        var portraitPath = Path.Combine(thumbnailRoot, "thumbnail-portrait.png");
        var validationPath = Path.Combine(thumbnailRoot, "thumbnail-layout-validation.json");

        Assert.Equal("ImageGeneration", result.PhaseExecuted);
        Assert.True(result.ThumbnailLayoutValidationGenerated);
        Assert.Equal(validationPath.Replace('\\', '/'), result.ThumbnailLayoutValidationPath);
        Assert.Contains(landscapePath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.Contains(squarePath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.Contains(portraitPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.Contains(validationPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.True(File.Exists(landscapePath));
        Assert.True(File.Exists(squarePath));
        Assert.True(File.Exists(portraitPath));
        Assert.True(File.Exists(validationPath));

        using var landscape = await Image.LoadAsync(landscapePath);
        using var square = await Image.LoadAsync(squarePath);
        using var portrait = await Image.LoadAsync(portraitPath);
        Assert.Equal(1280, landscape.Width);
        Assert.Equal(720, landscape.Height);
        Assert.Equal(1080, square.Width);
        Assert.Equal(1080, square.Height);
        Assert.Equal(1080, portrait.Width);
        Assert.Equal(1920, portrait.Height);

        var validation = JsonSerializer.Deserialize<ThumbnailLayoutValidationDto>(await File.ReadAllTextAsync(validationPath), JsonOptions);
        Assert.NotNull(validation);
        Assert.True(validation!.HookVisible);
        Assert.True(validation.VisualFocusVisible);
        Assert.Equal(3, validation.TextElementCount);
        Assert.True(validation.ThumbnailClickabilityScore >= 95);
        Assert.True(validation.ThumbnailCuriosityScore >= 95);
    }

    [Fact]
    public async Task GenerateThumbnailAssetsAsync_RejectsUnsupportedPhase()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteHeroInputFilesAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Phase = "NotAThumbnailPhase",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None));

        Assert.Contains("ImageGeneration", error.Message);
    }

    private static ThumbnailAssetIntelligenceService CreateService(string workingDirectory)
        => new(Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }));

    private static async Task WriteHeroInputFilesAsync(string workingDirectory)
    {
        var heroAssetsRoot = BuildHeroAssetsRoot(workingDirectory);
        Directory.CreateDirectory(heroAssetsRoot);

        var heroStory = new HeroAssetStoryDto(
            EventId,
            RegionId,
            "en",
            "LOOK WEST TONIGHT",
            "Venus and Jupiter will appear close together after sunset in Udaipur’s western sky.",
            "Look west shortly after sunset.",
            "Venus and Jupiter above the western horizon.",
            "Wonder",
            "ScrollStoppingHeroAsset",
            new HeroStorySourceDto(
                "Venus and Jupiter will appear close together in Udaipur’s evening sky.",
                "Look toward the western sky, about one-third above the horizon.",
                "Best viewing is around 7:23 PM IST, shortly after sunset.",
                "Venus and Jupiter appear only 1.63° apart, creating a striking planetary pairing."),
            new HeroAssetStoryScoresDto(95, 95, 90, 95),
            94,
            DateTimeOffset.UtcNow);

        var compositionModel = new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto("LOOK WEST TONIGHT"),
            new HeroCompositionSceneBlockDto("scene-001"),
            new HeroCompositionTextBlockDto("scene-001", "WEST"),
            new HeroCompositionTextBlockDto("scene-001", "AFTER SUNSET"),
            new HeroCompositionTextBlockDto("scene-001", "LOOK WEST"),
            new HeroCompositionValidationDto(true, true, true, true, true, 100));

        var sceneManifest = new HeroSceneManifestDto(
            EventId,
            new HeroSceneManifestEntryDto(1, "What", BuildApprovedScenePath(workingDirectory, "scene-001"), "PrimaryVisual"),
            new HeroSceneManifestEntryDto(6, "Action", BuildApprovedScenePath(workingDirectory, "scene-006"), "CallToAction"),
            new HeroSceneManifestEntryDto(2, "Where", BuildApprovedScenePath(workingDirectory, "scene-002"), "DirectionCue"),
            "Use What scene as visual anchor, Action scene for CTA, and Where scene for direction cue.");

        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-asset-story.json"), JsonSerializer.Serialize(heroStory, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-scene-manifest.json"), JsonSerializer.Serialize(sceneManifest, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-composition-model.json"), JsonSerializer.Serialize(compositionModel, JsonOptions));
    }

    private static async Task WriteThumbnailIntelligenceInputAsync(string workingDirectory)
    {
        var thumbnailRoot = BuildThumbnailAssetsRoot(workingDirectory);
        Directory.CreateDirectory(thumbnailRoot);

        var intelligence = new ThumbnailIntelligenceDto(
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
            ["too much explanation", "long sentences", "exact paragraph CTA", "small unreadable labels"],
            new ThumbnailCopyDto("DON'T MISS THIS TONIGHT", "Venus + Jupiter", "After Sunset"),
            [
                new ThumbnailPlatformTargetDto("YouTube", "1280x720", "Click"),
                new ThumbnailPlatformTargetDto("Facebook", "1200x630", "Share"),
                new ThumbnailPlatformTargetDto("Instagram", "1080x1080", "StopScroll")
            ],
            new ThumbnailReadinessScoresDto(100, 95, 91, 87, 94),
            [],
            DateTimeOffset.UtcNow);

        await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, "thumbnail-intelligence.json"), JsonSerializer.Serialize(intelligence, JsonOptions));
    }

    private static async Task WriteThumbnailCompositionInputAsync(string workingDirectory)
    {
        var thumbnailRoot = BuildThumbnailAssetsRoot(workingDirectory);
        Directory.CreateDirectory(thumbnailRoot);

        var model = new ThumbnailCompositionModelDto(
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

        await File.WriteAllTextAsync(Path.Combine(thumbnailRoot, "thumbnail-composition-model.json"), JsonSerializer.Serialize(model, JsonOptions));
    }

    private static async Task WriteApprovedSceneOutputsAsync(string workingDirectory)
    {
        var sceneApprovalRoot = Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine", "scene-approval-v3");
        Directory.CreateDirectory(sceneApprovalRoot);
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(Path.Combine(sceneApprovalRoot, "scene-001-final.png"), pngBytes);
        await File.WriteAllBytesAsync(Path.Combine(sceneApprovalRoot, "scene-002-final.png"), pngBytes);
        await File.WriteAllBytesAsync(Path.Combine(sceneApprovalRoot, "scene-003-final.png"), pngBytes);
        await File.WriteAllBytesAsync(Path.Combine(sceneApprovalRoot, "scene-005-final.png"), pngBytes);
        await File.WriteAllBytesAsync(Path.Combine(sceneApprovalRoot, "scene-006-final.png"), pngBytes);
    }

    private static string BuildApprovedScenePath(string workingDirectory, string sceneId)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine", "scene-approval-v3", $"{sceneId}-final.png").Replace('\\', '/');

    private static string BuildHeroAssetsRoot(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "hero-assets");

    private static string BuildThumbnailAssetsRoot(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "thumbnail-assets");

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "thumbnail-asset-intelligence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
