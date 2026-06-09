using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

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
    public async Task GenerateThumbnailAssetsAsync_RejectsNonIntelligencePhase()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteHeroInputFilesAsync(workingDirectory);
        var service = CreateService(workingDirectory);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest
        {
            EventId = EventId,
            RegionId = RegionId,
            Language = "en",
            Phase = "Images",
            DryRun = false,
            OverwriteExisting = true
        }, CancellationToken.None));

        Assert.Contains("Intelligence", error.Message);
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

        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-asset-story.json"), JsonSerializer.Serialize(heroStory, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-scene-manifest.json"), "{\"primaryScene\":\"scene-001\"}");
        await File.WriteAllTextAsync(Path.Combine(heroAssetsRoot, "hero-composition-model.json"), JsonSerializer.Serialize(compositionModel, JsonOptions));
    }

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
