using System.Net.Http;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class HeroAssetStoryGeneratorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string EventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateHeroAssetStoryAsync_DryRunReturnsPreviewOnlyAndUsesWhatWhereWhenWhySources()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);

        var result = await generator.GenerateHeroAssetStoryAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(EventId, result.EventId);
        Assert.Empty(result.GeneratedFiles);
        Assert.Empty(result.Warnings);
        Assert.False(File.Exists(BuildOutputPath(workingDirectory)));
        Assert.Equal("LOOK WEST TONIGHT", result.HeroStory.HeroHook);
        Assert.Equal("Venus and Jupiter will appear close together after sunset in Udaipur’s western sky.", result.HeroStory.HeroMessage);
        Assert.Equal("Look west shortly after sunset.", result.HeroStory.HeroAction);
        Assert.Equal("Venus and Jupiter above the western horizon.", result.HeroStory.HeroVisualFocus);
        Assert.Equal("Wonder", result.HeroStory.HeroEmotion);
        Assert.Equal("ScrollStoppingHeroAsset", result.HeroStory.HeroPlatformIntent);
        Assert.Equal(95, result.HeroStory.Scores.ScrollStoppingScore);
        Assert.Equal(95, result.HeroStory.Scores.ClickabilityScore);
        Assert.Equal(90, result.HeroStory.Scores.ShareabilityScore);
        Assert.Equal(95, result.HeroStory.Scores.UnderstandabilityScore);
        Assert.True(result.HeroStory.StoryScore >= 90);
        Assert.Equal("Venus and Jupiter will appear close together in Udaipur’s evening sky.", result.HeroStory.HeroStorySource.What);
        Assert.Equal("Look toward the western sky, about one-third above the horizon.", result.HeroStory.HeroStorySource.Where);
        Assert.Equal("Best viewing is around 7:23 PM IST, shortly after sunset.", result.HeroStory.HeroStorySource.When);
        Assert.Equal("Venus and Jupiter appear only 1.63° apart, creating a striking planetary pairing.", result.HeroStory.HeroStorySource.Why);
    }

    [Fact]
    public async Task GenerateHeroAssetStoryAsync_NonDryRunWritesHeroStoryJsonOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);

        var result = await generator.GenerateHeroAssetStoryAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: false,
            Phase: HeroAssetGenerationPhase.Story), CancellationToken.None);

        var outputPath = BuildOutputPath(workingDirectory);
        Assert.True(result.IsValid);
        Assert.Single(result.GeneratedFiles);
        Assert.Equal(outputPath.Replace('\\', '/'), result.GeneratedFiles.Single());
        Assert.True(File.Exists(outputPath));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(outputPath)!, "hero-landscape.png")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(outputPath)!, "hero-square.png")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(outputPath)!, "hero-portrait.png")));

        var saved = JsonSerializer.Deserialize<HeroAssetStoryDto>(await File.ReadAllTextAsync(outputPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(result.HeroStory.HeroHook, saved!.HeroHook);
    }



    [Fact]
    public async Task GenerateHeroAssetsAsync_StoryDryRunDoesNotRequireExistingHeroFiles()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);

        var result = await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false,
            Phase: HeroAssetGenerationPhase.Story), CancellationToken.None);

        var heroAssetsRoot = Path.GetDirectoryName(BuildOutputPath(workingDirectory))!;
        Assert.True(result.IsValid);
        Assert.Empty(result.GeneratedFiles);
        Assert.Empty(result.Warnings);
        Assert.Equal("LOOK WEST TONIGHT", result.HeroStory.HeroHook);
        Assert.Equal(result.HookScores.OrderByDescending(score => score.TotalScore).ThenBy(score => score.Hook).First().Hook, result.SelectedHook);
        Assert.Equal("LOOK WEST TONIGHT", result.SelectedHook);
        Assert.True(result.HookScores.Count >= 5);
        Assert.NotEmpty(result.AlternativeHooks);
        Assert.Equal(result.HookScores.Count - 1, result.AlternativeHooks.Count);
        AssertRequiredHookCandidates(result.HookScores);
        AssertHookScoresAreValidAndSorted(result.HookScores);
        Assert.DoesNotContain(result.SelectedHook, result.AlternativeHooks);
        Assert.Equal(
            result.HookScores.Skip(1).Select(score => score.Hook),
            result.AlternativeHooks);
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-asset-story.json")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-asset-blueprint.json")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-review.json")));
    }

    [Fact]
    public async Task GenerateHeroAssetsAsync_StoryNonDryRunWritesHeroStoryOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);

        var result = await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            Phase: HeroAssetGenerationPhase.Story), CancellationToken.None);

        var heroAssetsRoot = Path.GetDirectoryName(BuildOutputPath(workingDirectory))!;
        var storyPath = BuildOutputPath(workingDirectory);
        Assert.True(result.IsValid);
        Assert.Single(result.GeneratedFiles);
        Assert.Contains(storyPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.True(File.Exists(storyPath));
        Assert.Equal("LOOK WEST TONIGHT", result.SelectedHook);
        Assert.True(result.HookScores.Count >= 5);
        Assert.NotEmpty(result.AlternativeHooks);
        AssertRequiredHookCandidates(result.HookScores);
        AssertHookScoresAreValidAndSorted(result.HookScores);
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-asset-blueprint.json")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-review.json")));
    }

    [Fact]
    public async Task GenerateHeroAssetsAsync_HookSelectionDryRunReturnsHooksWithoutGeneratingBlueprintOrImages()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);
        await generator.GenerateHeroAssetStoryAsync(new HeroAssetStoryGenerationRequest(EventId, RegionId, "en", DryRun: false, OverwriteExisting: false), CancellationToken.None);

        var result = await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false,
            Phase: HeroAssetGenerationPhase.HookSelection), CancellationToken.None);

        var heroAssetsRoot = Path.GetDirectoryName(BuildOutputPath(workingDirectory))!;
        Assert.True(result.IsValid);
        Assert.Empty(result.GeneratedFiles);
        Assert.Empty(result.Warnings);
        Assert.Equal("LOOK WEST TONIGHT", result.SelectedHook);
        Assert.Contains("DON'T MISS THIS TONIGHT", result.AlternativeHooks);
        Assert.True(result.HookScores.Count >= 5);
        Assert.NotEmpty(result.AlternativeHooks);
        AssertRequiredHookCandidates(result.HookScores);
        AssertHookScoresAreValidAndSorted(result.HookScores);
        Assert.Equal("", result.HeroBlueprint.LayoutStyle);
        Assert.Empty(result.PlatformVariants);
        Assert.Equal(0, result.ReviewScores.HeroAssetReadinessScore);
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-asset-blueprint.json")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-review.json")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-landscape.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-square.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-portrait.png")));
    }

    [Fact]
    public async Task GenerateHeroAssetsAsync_HookSelectionNonDryRunDoesNotWriteBlueprintOrImages()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);
        await generator.GenerateHeroAssetStoryAsync(new HeroAssetStoryGenerationRequest(EventId, RegionId, "en", DryRun: false, OverwriteExisting: false), CancellationToken.None);

        var result = await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: false,
            Phase: HeroAssetGenerationPhase.HookSelection), CancellationToken.None);

        var heroAssetsRoot = Path.GetDirectoryName(BuildOutputPath(workingDirectory))!;
        var storyPath = BuildOutputPath(workingDirectory);
        var blueprintPath = Path.Combine(heroAssetsRoot, "hero-asset-blueprint.json");
        var reviewPath = Path.Combine(heroAssetsRoot, "hero-review.json");
        Assert.True(result.IsValid);
        Assert.Single(result.GeneratedFiles);
        Assert.Contains(storyPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.True(File.Exists(storyPath));
        Assert.Equal("LOOK WEST TONIGHT", result.SelectedHook);
        Assert.True(result.HookScores.Count >= 5);
        Assert.NotEmpty(result.AlternativeHooks);
        AssertRequiredHookCandidates(result.HookScores);
        AssertHookScoresAreValidAndSorted(result.HookScores);
        Assert.False(File.Exists(blueprintPath));
        Assert.False(File.Exists(reviewPath));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-landscape.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-square.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-portrait.png")));

    }

    [Fact]
    public async Task GenerateHeroAssetsAsync_BlueprintDryRunReturnsBlueprintWithoutGeneratingImages()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);
        await generator.GenerateHeroAssetStoryAsync(new HeroAssetStoryGenerationRequest(EventId, RegionId, "en", DryRun: false, OverwriteExisting: false), CancellationToken.None);

        var result = await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false,
            Phase: HeroAssetGenerationPhase.Blueprint), CancellationToken.None);

        var heroAssetsRoot = Path.GetDirectoryName(BuildOutputPath(workingDirectory))!;
        Assert.True(result.IsValid);
        Assert.Empty(result.GeneratedFiles);
        Assert.Empty(result.Warnings);
        Assert.Equal("LOOK WEST TONIGHT", result.SelectedHook);
        Assert.Equal("LOOK WEST TONIGHT", result.HeroStory.HeroHook);
        Assert.Equal("Wonder", result.HeroBlueprint.HeroEmotion);
        Assert.Equal("CinematicHeroPoster", result.HeroBlueprint.LayoutStyle);
        Assert.Equal("Dominant Venus and Jupiter with cinematic twilight sky.", result.HeroBlueprint.VisualFocus);
        Assert.Equal("Two bright planets together after sunset. Look west to see the pairing.", result.HeroBlueprint.VisualNarrative);
        Assert.Equal(3, result.PlatformVariants.Count);
        Assert.Equal(result.PlatformVariants, result.HeroBlueprint.PlatformVariants);
        Assert.All(result.PlatformVariants, variant => Assert.Equal("Twilight", variant.LayoutBlueprint.Atmosphere));
        Assert.Equal("Landscape", result.PlatformVariants[0].Variant);
        Assert.Equal("1280x720", result.PlatformVariants[0].Size);
        Assert.Equal("YouTube", result.PlatformVariants[0].Purpose);
        Assert.Equal("Top-left: LOOK WEST TONIGHT", result.PlatformVariants[0].LayoutBlueprint.PrimaryTextPlacement);
        Assert.Equal("Center: Venus + Jupiter", result.PlatformVariants[0].LayoutBlueprint.CenterVisual);
        Assert.Equal("Bottom-right: West marker", result.PlatformVariants[0].LayoutBlueprint.SupportingTextPlacement);
        Assert.Equal("Square", result.PlatformVariants[1].Variant);
        Assert.Equal("1080x1080", result.PlatformVariants[1].Size);
        Assert.Equal("Facebook/Instagram", result.PlatformVariants[1].Purpose);
        Assert.Equal("Top: LOOK WEST TONIGHT", result.PlatformVariants[1].LayoutBlueprint.PrimaryTextPlacement);
        Assert.Equal("Bottom: After Sunset", result.PlatformVariants[1].LayoutBlueprint.SupportingTextPlacement);
        Assert.Equal("Portrait", result.PlatformVariants[2].Variant);
        Assert.Equal("1080x1920", result.PlatformVariants[2].Size);
        Assert.Equal("Stories/Reels/Shorts", result.PlatformVariants[2].Purpose);
        Assert.Equal("Top: LOOK WEST TONIGHT", result.PlatformVariants[2].LayoutBlueprint.PrimaryTextPlacement);
        Assert.Equal("Bottom: Look West After Sunset", result.PlatformVariants[2].LayoutBlueprint.SupportingTextPlacement);
        Assert.Equal(95, result.ReviewScores.ScrollStoppingScore);
        Assert.Equal(95, result.ReviewScores.ClickabilityScore);
        Assert.Equal(90, result.ReviewScores.ShareabilityScore);
        Assert.Equal(95, result.ReviewScores.UnderstandabilityScore);
        Assert.Equal(94, result.ReviewScores.HeroAssetReadinessScore);
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-asset-blueprint.json")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-landscape.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-square.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-portrait.png")));
    }

    [Fact]
    public async Task GenerateHeroAssetsAsync_BlueprintStringPhaseTrimsNormalizesAndGeneratesStoryWhenMissing()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);

        var result = await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false)
        {
            Phase = " Blueprint "
        }, CancellationToken.None);

        var heroAssetsRoot = Path.GetDirectoryName(BuildOutputPath(workingDirectory))!;
        Assert.True(result.IsValid);
        Assert.Empty(result.GeneratedFiles);
        Assert.Empty(result.Warnings);
        Assert.Equal("LOOK WEST TONIGHT", result.SelectedHook);
        Assert.Equal("LOOK WEST TONIGHT", result.HeroStory.HeroHook);
        Assert.Equal("Wonder", result.HeroBlueprint.HeroEmotion);
        Assert.Equal("CinematicHeroPoster", result.HeroBlueprint.LayoutStyle);
        Assert.Equal("Dominant Venus and Jupiter with cinematic twilight sky.", result.HeroBlueprint.VisualFocus);
        Assert.Equal("Two bright planets together after sunset. Look west to see the pairing.", result.HeroBlueprint.VisualNarrative);
        Assert.Equal(3, result.PlatformVariants.Count);
        Assert.Equal(result.PlatformVariants, result.HeroBlueprint.PlatformVariants);
        Assert.Equal(95, result.ReviewScores.ScrollStoppingScore);
        Assert.Equal(95, result.ReviewScores.ClickabilityScore);
        Assert.Equal(90, result.ReviewScores.ShareabilityScore);
        Assert.Equal(95, result.ReviewScores.UnderstandabilityScore);
        Assert.Equal(94, result.ReviewScores.HeroAssetReadinessScore);
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-asset-story.json")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-asset-blueprint.json")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-review.json")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-landscape.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-square.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-portrait.png")));
    }

    [Fact]
    public async Task GenerateHeroAssetsAsync_BlueprintSaveModeWritesBlueprintJsonOnlyAndUpdatesHeroStoryHook()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);
        await generator.GenerateHeroAssetStoryAsync(new HeroAssetStoryGenerationRequest(EventId, RegionId, "en", DryRun: false, OverwriteExisting: false), CancellationToken.None);

        var result = await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            Phase: HeroAssetGenerationPhase.Blueprint), CancellationToken.None);

        var heroAssetsRoot = Path.GetDirectoryName(BuildOutputPath(workingDirectory))!;
        var storyPath = BuildOutputPath(workingDirectory);
        var blueprintPath = Path.Combine(heroAssetsRoot, "hero-asset-blueprint.json");
        Assert.True(result.IsValid);
        Assert.Contains(storyPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.Contains(blueprintPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.True(File.Exists(storyPath));
        Assert.True(File.Exists(blueprintPath));

        using var blueprintDocument = JsonDocument.Parse(await File.ReadAllTextAsync(blueprintPath));
        Assert.Equal(EventId, blueprintDocument.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("LOOK WEST TONIGHT", blueprintDocument.RootElement.GetProperty("selectedHook").GetString());
        var heroBlueprint = blueprintDocument.RootElement.GetProperty("heroBlueprint");
        Assert.Equal("CinematicHeroPoster", heroBlueprint.GetProperty("layoutStyle").GetString());
        Assert.Equal("Dominant Venus and Jupiter with cinematic twilight sky.", heroBlueprint.GetProperty("visualFocus").GetString());
        Assert.Equal("Two bright planets together after sunset. Look west to see the pairing.", heroBlueprint.GetProperty("visualNarrative").GetString());
        Assert.Equal(3, heroBlueprint.GetProperty("platformVariants").GetArrayLength());

        var savedStory = JsonSerializer.Deserialize<HeroAssetStoryDto>(await File.ReadAllTextAsync(storyPath), JsonOptions);
        Assert.NotNull(savedStory);
        Assert.Equal("LOOK WEST TONIGHT", savedStory!.HeroHook);
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-landscape.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-square.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-portrait.png")));
    }

    [Fact]
    public async Task GenerateHeroAssetsAsync_ImagesNonDryRunLoadsStoryAndBlueprintGeneratesImagesAndReviewDiagnostics()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);

        await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            Phase: HeroAssetGenerationPhase.Blueprint), CancellationToken.None);

        var result = await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            Phase: HeroAssetGenerationPhase.Images), CancellationToken.None);

        var heroAssetsRoot = Path.GetDirectoryName(BuildOutputPath(workingDirectory))!;
        var heroPath = Path.Combine(heroAssetsRoot, "hero.png");
        var landscapePath = Path.Combine(heroAssetsRoot, "hero-landscape.png");
        var squarePath = Path.Combine(heroAssetsRoot, "hero-square.png");
        var portraitPath = Path.Combine(heroAssetsRoot, "hero-portrait.png");
        var reviewPath = Path.Combine(heroAssetsRoot, "hero-review.json");
        var sceneManifestPath = Path.Combine(heroAssetsRoot, "hero-scene-manifest.json");
        var compositionModelPath = Path.Combine(heroAssetsRoot, "hero-composition-model.json");
        var layoutValidationPath = Path.Combine(heroAssetsRoot, "hero-layout-validation.json");

        Assert.True(result.IsValid);
        Assert.Equal("Images", result.PhaseRequested);
        Assert.Equal("Images", result.PhaseExecuted);
        Assert.True(result.StoryExecuted);
        Assert.True(result.BlueprintExecuted);
        Assert.True(result.ImageGenerationExecuted);
        Assert.True(result.HeroSceneSelectorExecuted);
        Assert.True(result.HeroSceneManifestGenerated);
        Assert.Equal(sceneManifestPath.Replace('\\', '/'), result.HeroSceneManifestPath);
        Assert.Equal("scene-001", result.PrimaryScene);
        Assert.Equal("scene-006", result.SecondaryScene);
        Assert.Equal("scene-002", result.SupportScene);
        Assert.Equal(7, result.GeneratedFiles.Count);
        Assert.True(result.HeroCompositionModelGenerated);
        Assert.True(result.LayoutValidationGenerated);
        Assert.False(result.DuplicateBlocksDetected);
        Assert.False(result.TextOverlapDetected);
        Assert.True(result.ObjectsVisible);
        Assert.Contains(sceneManifestPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.Contains(compositionModelPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.Contains(layoutValidationPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.Contains(heroPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.Contains(landscapePath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.Contains(squarePath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.Contains(portraitPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.DoesNotContain(reviewPath.Replace('\\', '/'), result.GeneratedFiles);
        Assert.True(File.Exists(sceneManifestPath));
        Assert.True(File.Exists(compositionModelPath));
        Assert.True(File.Exists(layoutValidationPath));
        Assert.True(File.Exists(heroPath));
        Assert.True(File.Exists(landscapePath));
        Assert.True(File.Exists(squarePath));
        Assert.True(File.Exists(portraitPath));
        Assert.True(File.Exists(reviewPath));
        using var compositionDocument = JsonDocument.Parse(await File.ReadAllTextAsync(compositionModelPath));
        var composition = compositionDocument.RootElement;
        Assert.Equal("LOOK WEST TONIGHT", composition.GetProperty("hookBlock").GetProperty("text").GetString());
        Assert.Equal("scene-001", composition.GetProperty("visualBlock").GetProperty("sourceScene").GetString());
        Assert.Equal("WEST", composition.GetProperty("directionBlock").GetProperty("text").GetString());
        Assert.Equal("7:23 PM IST", composition.GetProperty("timingBlock").GetProperty("text").GetString());
        Assert.Equal("STEP OUTSIDE TONIGHT", composition.GetProperty("ctaBlock").GetProperty("text").GetString());
        Assert.True(composition.GetProperty("validation").GetProperty("hookPresent").GetBoolean());
        Assert.True(composition.GetProperty("validation").GetProperty("visualPresent").GetBoolean());
        Assert.True(composition.GetProperty("validation").GetProperty("directionPresent").GetBoolean());
        Assert.True(composition.GetProperty("validation").GetProperty("timingPresent").GetBoolean());
        Assert.True(composition.GetProperty("validation").GetProperty("ctaPresent").GetBoolean());
        Assert.Equal(100, composition.GetProperty("validation").GetProperty("compositionCompletenessScore").GetInt32());
        using var validationDocument = JsonDocument.Parse(await File.ReadAllTextAsync(layoutValidationPath));
        var layoutValidation = validationDocument.RootElement;
        Assert.False(layoutValidation.GetProperty("duplicateBlocksDetected").GetBoolean());
        Assert.False(layoutValidation.GetProperty("textOverlapDetected").GetBoolean());
        Assert.True(layoutValidation.GetProperty("objectsVisible").GetBoolean());
        Assert.Equal(5, layoutValidation.GetProperty("renderedBlocks").GetArrayLength());
        Assert.Equal("GuideHero", layoutValidation.GetProperty("heroContract").GetString());
        Assert.Equal("GuideHero", layoutValidation.GetProperty("validatorContract").GetString());
        Assert.Equal("GuideHero", layoutValidation.GetProperty("rendererContract").GetString());
        Assert.False(layoutValidation.GetProperty("contractMismatch").GetBoolean());
        Assert.Equal("GuideHero", layoutValidation.GetProperty("validationProfileUsed").GetString());
        using var reviewDocument = JsonDocument.Parse(await File.ReadAllTextAsync(reviewPath));
        var review = reviewDocument.RootElement;
        Assert.True(review.GetProperty("usesSharedAstronomyVisualComposer").GetBoolean());
        Assert.True(review.GetProperty("usesRealCelestialAssets").GetBoolean());
        Assert.False(review.GetProperty("usesPlaceholderDots").GetBoolean());
        Assert.False(review.GetProperty("usesManualCirclePlanets").GetBoolean());
        Assert.True(review.GetProperty("matchesApprovedSceneVisualBaseline").GetBoolean());
        Assert.Equal(3, review.GetProperty("platformVariantCount").GetInt32());
        Assert.True(new FileInfo(heroPath).Length > 0);
        Assert.True(new FileInfo(landscapePath).Length > 0);
        Assert.True(new FileInfo(squarePath).Length > 0);
        Assert.True(new FileInfo(portraitPath).Length > 0);
    }



    [Fact]
    public void Phase11PlanetGroupingHero_CompactsDirectionToFooterOnlyMetadata()
    {
        var composition = new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto("GROUPED PLANETS OVER UDAIPUR, RAJASTHAN"),
            new HeroCompositionSceneBlockDto("planet grouping background"),
            new HeroCompositionTextBlockDto("direction-panel", "Look toward the eastern sky, then scan the arc above the horizon where the grouped planets appear."),
            new HeroCompositionTextBlockDto("date-time-panel", "7:23 PM IST"),
            new HeroCompositionTextBlockDto("", ""),
            new HeroCompositionValidationDto(true, true, true, true, false, 100));

        var method = typeof(HeroAssetStoryGenerator).GetMethod("ResolveHeroRenderedText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var rendered = method!.Invoke(null, [composition]);
        Assert.NotNull(rendered);
        var renderedTuple = (System.Runtime.CompilerServices.ITuple)rendered!;
        var renderedDirectionText = (string)renderedTuple[2]!;

        Assert.Equal("DIRECTION  EASTERN SKY", renderedDirectionText);
        Assert.DoesNotContain("SCAN THE ARC", renderedDirectionText);
        Assert.True(renderedDirectionText.Length <= 30);
    }

    [Fact]
    public void Phase11HeroLayoutValidation_NormalizesLongDirectionToCompactFooter()
    {
        var composition = new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto("GROUPED PLANETS OVER UDAIPUR, RAJASTHAN"),
            new HeroCompositionSceneBlockDto("planet grouping background"),
            new HeroCompositionTextBlockDto("direction-panel", "Look across the broad upper twilight arc above the whole city horizon"),
            new HeroCompositionTextBlockDto("date-time-panel", "7:23 PM IST"),
            new HeroCompositionTextBlockDto("", ""),
            new HeroCompositionValidationDto(true, true, true, true, false, 100));

        var method = typeof(HeroAssetStoryGenerator).GetMethod("BuildHeroLayoutValidation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var validation = (HeroLayoutValidationDto)method!.Invoke(null, [composition, Array.Empty<string>(), true, "PLANET_GROUPING", false])!;

        Assert.True(validation.IsValid);
        Assert.Equal("BEST VIEWING SKY", validation.CompactDirectionText);
        Assert.DoesNotContain(validation.Errors, error => error.Contains("directionBlock.text contains more than 5 words", StringComparison.OrdinalIgnoreCase));
    }



    [Fact]
    public void Phase11HeroLayoutValidation_GenericSolarEclipseNormalizesLongSafetyDirectionAndKeepsVariants()
    {
        var composition = new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto("TOTAL SOLAR ECLIPSE"),
            new HeroCompositionSceneBlockDto("solar eclipse background"),
            new HeroCompositionTextBlockDto("direction-panel", "Use certified solar eclipse glasses and never look directly at the Sun without approved protection."),
            new HeroCompositionTextBlockDto("date-time-panel", "2:18 PM EDT"),
            new HeroCompositionTextBlockDto("", ""),
            new HeroCompositionValidationDto(true, true, true, true, false, 100));

        var method = typeof(HeroAssetStoryGenerator).GetMethod("BuildHeroLayoutValidation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var validation = (HeroLayoutValidationDto)method!.Invoke(null, [composition, Array.Empty<string>(), false, "SolarEclipse", false])!;

        Assert.True(validation.IsValid);
        Assert.True(validation.GenericRendererApplied);
        Assert.False(validation.PlanetGroupingRendererApplied);
        Assert.Equal("GenericHeroRenderer", validation.RendererPathSelected);
        Assert.True(validation.SharedFooterRendererUsed);
        Assert.False(validation.PlanetGroupingPromptApplied);
        Assert.False(validation.PlanetGroupingSubtitleFormatterApplied);
        Assert.Equal(["Landscape", "Square", "Portrait"], validation.ExpectedVariants);
        Assert.Equal(["Landscape", "Square", "Portrait"], validation.GeneratedVariants);
        Assert.Empty(validation.MissingVariants);
        Assert.Equal("SAFE SOLAR VIEWING", validation.CompactDirectionText);
        Assert.DoesNotContain(validation.Errors, error => error.Contains("directionBlock.text contains more than 5 words", StringComparison.OrdinalIgnoreCase));
        Assert.True(validation.FooterTextCompactValidationPassed);
    }

    [Fact]
    public void AzureHeroPromptBuilderV2_SolarEclipseBuildsCinematicDominantBackgroundPrompts()
    {
        var composition = new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto("TOTAL SOLAR ECLIPSE"),
            new HeroCompositionSceneBlockDto("solar eclipse background"),
            new HeroCompositionTextBlockDto("direction-panel", "Safe solar viewing"),
            new HeroCompositionTextBlockDto("date-time-panel", "2:18 PM EDT"),
            new HeroCompositionTextBlockDto("", ""),
            new HeroCompositionValidationDto(true, true, true, true, false, 100));
        var story = new HeroAssetStoryDto(
            EventId,
            RegionId,
            "en",
            "TOTAL SOLAR ECLIPSE",
            "A total solar eclipse crosses the sky.",
            "Use certified eclipse glasses.",
            "Large corona ring silhouette.",
            "Wonder",
            "ScrollStoppingHeroAsset",
            new HeroStorySourceDto("Total Solar Eclipse", "southwest sky", "2:18 PM EDT", "rare alignment"),
            new HeroAssetStoryScoresDto(95, 95, 95, 95),
            95,
            DateTimeOffset.Parse("2026-06-27T00:00:00Z"));
        var builder = typeof(HeroAssetStoryGenerator).GetNestedType("AzureHeroPromptBuilderV2", System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(builder);
        var method = builder!.GetMethod("Build", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var prompts = (System.Collections.IEnumerable)method!.Invoke(null, [composition, story, "TOTAL SOLAR ECLIPSE", BuildSolarEclipseProductionEventIntelligence(), null])!;
        var promptTexts = prompts.Cast<object>()
            .Select(prompt => (string)((System.Runtime.CompilerServices.ITuple)prompt)[4]!)
            .ToArray();

        Assert.Equal(3, promptTexts.Length);
        Assert.All(promptTexts, prompt =>
        {
            Assert.Contains("CinematicHero", prompt);
            Assert.Contains("occupy 45–65% of the frame", prompt);
            Assert.Contains("no guide panels", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no labels", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Stellarium", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("screenshot", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("eclipse must dominate", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("large corona", prompt, StringComparison.OrdinalIgnoreCase);
        });
    }



    [Theory]
    [InlineData("PlanetConjunction", "Venus Jupiter Planetary Conjunction", "Planetary Conjunction enrichment", "large visible planets", "visually dominant")]
    [InlineData("MeteorShower", "Geminid Meteor Shower", "Meteor Shower enrichment", "dramatic meteor streaks", "bright fireballs")]
    [InlineData("LunarEclipse", "Total Lunar Eclipse Blood Moon", "Lunar Eclipse enrichment", "large red/orange Blood Moon", "45–65%")]
    [InlineData("Comet", "Comet C/2026 A1", "Comet enrichment", "glowing coma", "long tail")]
    [InlineData("PlanetaryAlignment", "Planetary Alignment", "Planetary Alignment enrichment", "multiple bright planets", "larger than real naked-eye scale")]
    public void AzureHeroPromptBuilderV2_EventFamiliesBuildCinematicEnrichedPrompts(string eventType, string title, string enrichmentHeader, string requiredPhrase, string secondRequiredPhrase)
    {
        var composition = new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto(title.ToUpperInvariant()),
            new HeroCompositionSceneBlockDto($"{title} cinematic background"),
            new HeroCompositionTextBlockDto("direction-panel", "Eastern sky"),
            new HeroCompositionTextBlockDto("date-time-panel", "9:00 PM"),
            new HeroCompositionTextBlockDto("", ""),
            new HeroCompositionValidationDto(true, true, true, true, false, 100));
        var story = new HeroAssetStoryDto(
            EventId,
            RegionId,
            "en",
            title.ToUpperInvariant(),
            $"{title} crosses the sky.",
            "Watch the sky.",
            $"Cinematic {title}.",
            "Wonder",
            "ScrollStoppingHeroAsset",
            new HeroStorySourceDto(title, "eastern sky", "9:00 PM", "rare sky event"),
            new HeroAssetStoryScoresDto(95, 95, 95, 95),
            95,
            DateTimeOffset.Parse("2026-06-27T00:00:00Z"));
        var builder = typeof(HeroAssetStoryGenerator).GetNestedType("AzureHeroPromptBuilderV2", System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(builder);
        var method = builder!.GetMethod("Build", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var prompts = (System.Collections.IEnumerable)method!.Invoke(null, [composition, story, title.ToUpperInvariant(), BuildProductionEventIntelligence(eventType, title), null])!;
        var promptTexts = prompts.Cast<object>()
            .Select(prompt => (string)((System.Runtime.CompilerServices.ITuple)prompt)[4]!)
            .ToArray();

        Assert.Equal(3, promptTexts.Length);
        Assert.All(promptTexts, prompt =>
        {
            Assert.Contains("CinematicHero", prompt);
            Assert.Contains(enrichmentHeader, prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(requiredPhrase, prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(secondRequiredPhrase, prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("observing-guide", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Stellarium", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("labels", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("UI", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("embedded text", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("safe space", prompt, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData("PlanetConjunction", "Venus Jupiter Planetary Conjunction", "PlanetaryConjunction")]
    [InlineData("MeteorShower", "Geminid Meteor Shower", "MeteorShower")]
    [InlineData("LunarEclipse", "Total Lunar Eclipse Blood Moon", "LunarEclipse")]
    [InlineData("Comet", "Comet C/2026 A1", "Comet")]
    [InlineData("PlanetaryAlignment", "Planetary Alignment", "PlanetaryAlignment")]
    public void AzureHeroPromptBuilderV2_EventFamiliesExposeDiagnosticsEnrichmentShape(string eventType, string title, string expectedFamily)
    {
        var builder = typeof(HeroAssetStoryGenerator).GetNestedType("AzureHeroPromptBuilderV2", System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(builder);
        var method = builder!.GetMethod("ResolveEventFamilyPromptEnrichment", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var enrichment = (System.Runtime.CompilerServices.ITuple)method!.Invoke(null, [eventType, title])!;

        Assert.Equal(expectedFamily, (string)enrichment[0]!);
        Assert.Contains("no", (string)enrichment[1]!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase11HeroLayoutValidation_PlanetGroupingKeepsGenericRendererWithIsolatedCustomizations()
    {
        var composition = new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto("GROUPED PLANETS"),
            new HeroCompositionSceneBlockDto("planet grouping background"),
            new HeroCompositionTextBlockDto("direction-panel", "Eastern sky"),
            new HeroCompositionTextBlockDto("date-time-panel", "7:23 PM IST"),
            new HeroCompositionTextBlockDto("", ""),
            new HeroCompositionValidationDto(true, true, true, true, false, 100));

        var method = typeof(HeroAssetStoryGenerator).GetMethod("BuildHeroLayoutValidation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var validation = (HeroLayoutValidationDto)method!.Invoke(null, [composition, new[] { "Venus", "Mars", "Jupiter" }, true, "PlanetGrouping", false])!;

        Assert.True(validation.IsValid);
        Assert.Equal("PlanetGrouping", validation.EventFamily);
        Assert.Equal("GenericHeroRenderer", validation.RendererPathSelected);
        Assert.True(validation.GenericRendererApplied);
        Assert.False(validation.PlanetGroupingRendererApplied);
        Assert.True(validation.PlanetGroupingPromptApplied);
        Assert.True(validation.PlanetGroupingSubtitleFormatterApplied);
        Assert.True(validation.SharedFooterRendererUsed);
        Assert.Equal(["Landscape", "Square", "Portrait"], validation.ExpectedVariants);
        Assert.Equal(["Landscape", "Square", "Portrait"], validation.GeneratedVariants);
        Assert.Empty(validation.MissingVariants);
    }


    [Fact]
    public async Task GenerateHeroAssetsAsync_SceneSelectionNonDryRunWritesHeroSceneManifestOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);

        await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            Phase: HeroAssetGenerationPhase.Blueprint), CancellationToken.None);

        var result = await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            Phase: HeroAssetGenerationPhase.SceneSelection), CancellationToken.None);

        var heroAssetsRoot = Path.GetDirectoryName(BuildOutputPath(workingDirectory))!;
        var sceneManifestPath = Path.Combine(heroAssetsRoot, "hero-scene-manifest.json");

        Assert.True(result.IsValid);
        Assert.Equal("SceneSelection", result.PhaseRequested);
        Assert.Equal("SceneSelection", result.PhaseExecuted);
        Assert.True(result.StoryExecuted);
        Assert.True(result.BlueprintExecuted);
        Assert.False(result.ImageGenerationExecuted);
        Assert.Single(result.GeneratedFiles);
        Assert.Equal(sceneManifestPath.Replace('\\', '/'), result.GeneratedFiles.Single());
        Assert.NotNull(result.HeroSceneManifest);
        Assert.Equal("scene-001", result.HeroSceneManifest!.PrimaryScene.SceneId);
        Assert.Equal("scene-006", result.HeroSceneManifest.SecondaryScene.SceneId);
        Assert.Equal("scene-002", result.HeroSceneManifest.SupportScene.SceneId);
        Assert.True(File.Exists(sceneManifestPath));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-landscape.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-square.png")));
        Assert.False(File.Exists(Path.Combine(heroAssetsRoot, "hero-portrait.png")));

        using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(sceneManifestPath));
        var manifest = manifestDocument.RootElement;
        Assert.Equal(EventId, manifest.GetProperty("eventId").GetString());
        Assert.Equal(1, manifest.GetProperty("primaryScene").GetProperty("sceneNumber").GetInt32());
        Assert.Equal("What", manifest.GetProperty("primaryScene").GetProperty("sceneKey").GetString());
        Assert.Equal("PrimaryVisual", manifest.GetProperty("primaryScene").GetProperty("role").GetString());
        Assert.EndsWith("scene-001-final.png", manifest.GetProperty("primaryScene").GetProperty("imagePath").GetString());
        Assert.Equal(6, manifest.GetProperty("secondaryScene").GetProperty("sceneNumber").GetInt32());
        Assert.Equal("Action", manifest.GetProperty("secondaryScene").GetProperty("sceneKey").GetString());
        Assert.Equal("CallToAction", manifest.GetProperty("secondaryScene").GetProperty("role").GetString());
        Assert.Equal(2, manifest.GetProperty("supportScene").GetProperty("sceneNumber").GetInt32());
        Assert.Equal("Where", manifest.GetProperty("supportScene").GetProperty("sceneKey").GetString());
        Assert.Equal("DirectionCue", manifest.GetProperty("supportScene").GetProperty("role").GetString());
        Assert.Equal("Use What scene as visual anchor, Action scene for CTA, and Where scene for direction cue.", manifest.GetProperty("selectionReason").GetString());
    }



    [Fact]
    public async Task GenerateHeroAssetsAsync_SceneSelectionPrefersNormalizedLongSceneAssetsOverStagedFinalAssets()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        await WriteNormalizedSceneAssetsAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);

        await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            Phase: HeroAssetGenerationPhase.Blueprint), CancellationToken.None);

        var result = await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            Phase: HeroAssetGenerationPhase.SceneSelection), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.NotNull(result.HeroSceneManifest);
        Assert.EndsWith("scene-approval-v3/long/scene-001.png", result.HeroSceneManifest!.PrimaryScene.ImagePath);
        Assert.EndsWith("scene-approval-v3/long/scene-006.png", result.HeroSceneManifest.SecondaryScene.ImagePath);
        Assert.EndsWith("scene-approval-v3/long/scene-002.png", result.HeroSceneManifest.SupportScene.ImagePath);
    }

    [Fact]
    public async Task GenerateHeroAssetsAsync_MeteorStrategyDoesNotRequireGeminidsOrMeteorsAssetFiles()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);
        var productionContext = new ProductionPipelineExecutionContext(
            true,
            null,
            null,
            null,
            false,
            ProductionEventIntelligence: BuildMeteorProductionEventIntelligence());

        await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            Phase: HeroAssetGenerationPhase.Blueprint), CancellationToken.None);

        var result = await generator.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: true,
            Phase: HeroAssetGenerationPhase.Images,
            ProductionContext: productionContext), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Required strategy celestial assets", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(BuildOutputPath(workingDirectory))!, "hero.png")));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(BuildOutputPath(workingDirectory))!, "hero-scene-manifest.json")));
    }

    [Fact]
    public void HeroAssetSceneSelector_SelectHeroScenesUsesReusableRoleScoring()
    {
        var selector = new HeroAssetSceneSelector();
        var story = new HeroAssetStoryDto(
            EventId,
            RegionId,
            "en",
            "LOOK WEST TONIGHT",
            "Venus and Jupiter will appear close together after sunset in Udaipur’s western sky.",
            "Look west shortly after sunset.",
            "Venus and Jupiter above the western horizon.",
            "Wonder",
            "ScrollStoppingHeroAsset",
            new HeroStorySourceDto("what", "where", "when", "why"),
            new HeroAssetStoryScoresDto(95, 95, 90, 95),
            95,
            DateTimeOffset.Parse("2026-06-07T14:05:00Z"));
        var blueprint = new HeroAssetBlueprintDto(
            "Wonder",
            "AstronomyPoster",
            "Venus and Jupiter above the western horizon during twilight.",
            "Two bright planets together after sunset. Look west to see the pairing.",
            []);

        var manifest = selector.SelectHeroScenes(story, blueprint,
        [
            new ApprovedHeroSceneCandidate("scene-001", AstronomyQuestionTypes.What, "Introduce the event.", "Hero visual of two bright planets together.", "Venus and Jupiter appear close together tonight.", "/approved/scene-001-final.png"),
            new ApprovedHeroSceneCandidate("scene-002", AstronomyQuestionTypes.Where, "Orient the viewer.", "Show west horizon and where to look.", "Look west above the horizon for Venus and Jupiter.", "/approved/scene-002-final.png"),
            new ApprovedHeroSceneCandidate("scene-006", AstronomyQuestionTypes.Action, "Call the viewer to action.", "Closing action scene: step outside tonight and look west.", "Step outside tonight and look west for Venus and Jupiter.", "/approved/scene-006-final.png")
        ]);

        Assert.Equal("scene-001", manifest.PrimaryScene.SceneId);
        Assert.Equal("scene-006", manifest.SecondaryScene.SceneId);
        Assert.Equal("scene-002", manifest.SupportScene.SceneId);
    }

    private static void AssertRequiredHookCandidates(IReadOnlyList<HeroHookScoreDto> hookScores)
    {
        var hooks = hookScores.Select(score => score.Hook).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("LOOK WEST TONIGHT", hooks);
        Assert.Contains("DON'T MISS THIS TONIGHT", hooks);
        Assert.Contains("TWO BRIGHT PLANETS TOGETHER", hooks);
        Assert.Contains("LOOK UP AFTER SUNSET", hooks);
        Assert.Contains("EVENING SKY HIGHLIGHT", hooks);
    }

    private static void AssertHookScoresAreValidAndSorted(IReadOnlyList<HeroHookScoreDto> hookScores)
    {
        Assert.All(hookScores, score =>
        {
            Assert.False(string.IsNullOrWhiteSpace(score.Hook));
            Assert.InRange(score.ScrollStoppingScore, 0, 100);
            Assert.InRange(score.ClickabilityScore, 0, 100);
            Assert.InRange(score.ShareabilityScore, 0, 100);
            Assert.InRange(score.UnderstandabilityScore, 0, 100);
            Assert.True(score.TotalScore > 0);
            Assert.Equal(
                CalculateExpectedTotalScore(score.ScrollStoppingScore, score.ClickabilityScore, score.ShareabilityScore, score.UnderstandabilityScore),
                score.TotalScore);
        });

        Assert.Equal(
            hookScores.OrderByDescending(score => score.TotalScore).ThenBy(score => score.Hook).Select(score => score.Hook),
            hookScores.Select(score => score.Hook));
    }

    private static int CalculateExpectedTotalScore(int scrollStoppingScore, int clickabilityScore, int shareabilityScore, int understandabilityScore)
        => (int)Math.Round(
            (scrollStoppingScore * 0.35)
            + (clickabilityScore * 0.35)
            + (shareabilityScore * 0.15)
            + (understandabilityScore * 0.15),
            MidpointRounding.AwayFromZero);

    private static HeroAssetStoryGenerator CreateGenerator(string workingDirectory)
        => new(Options.Create(new RenderingOptions
        {
            WorkingDirectory = workingDirectory,
            CelestialAssetsRoot = Path.Combine(workingDirectory, "assets", "celestial")
        }), Options.Create(new AzureOpenAIForImageOptions()), new TestHttpClientFactory(), NullLogger<HeroAssetStoryGenerator>.Instance, new HeroAssetSceneSelector(), new HeroCompositionEngine(), Options.Create(new ThumbnailFontOptions()), new RuntimeAssetPathResolver());

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }



    private static async Task WriteNormalizedSceneAssetsAsync(string workingDirectory)
    {
        var eventRoot = Path.Combine(workingDirectory, "assets", RegionId, "events", EventId);
        var approvedScenePng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        foreach (var profile in new[] { "short", "long" })
        {
            var profileRoot = Path.Combine(eventRoot, "scene-approval-v3", profile);
            Directory.CreateDirectory(profileRoot);
            for (var sceneNumber = 1; sceneNumber <= 6; sceneNumber++)
                await File.WriteAllBytesAsync(Path.Combine(profileRoot, $"scene-{sceneNumber:000}.png"), approvedScenePng);
        }
    }


    private static ProductionEventIntelligence BuildProductionEventIntelligence(string eventType, string title)
        => new(
            "Astronomy",
            eventType,
            title,
            title,
            DateTimeOffset.Parse("2026-06-27T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-27T21:00:00Z"),
            "9:00 PM local",
            "Evening sky",
            "Eastern sky",
            "United States",
            [title],
            [],
            "Cinematic hero",
            "dark sky",
            12,
            $"{title} is visible in the sky.",
            ["Watch during the viewing window"],
            [title],
            ["Open on cinematic event"],
            [],
            eventType,
            RequiredVisualObjects: [title],
            HeroCopyCandidates: [title.ToUpperInvariant()]);

    private static HeroAssetStoryDto BuildHeroStory(string language, string title, string hook)
        => new(
            EventId,
            RegionId,
            language,
            hook,
            "A concise astronomy hero message.",
            "Look toward the eastern sky",
            "Cinematic astronomy hero background.",
            "Wonder + urgency",
            "ScrollStoppingHeroAsset",
            new HeroStorySourceDto(title, "eastern sky", "9:00 PM local", "bright astronomy event"),
            new HeroAssetStoryScoresDto(95, 95, 95, 95),
            95,
            DateTimeOffset.Parse("2026-06-27T00:00:00Z"));

    private static HeroCompositionModelDto BuildComposition(string hook)
        => new(
            new HeroCompositionHookBlockDto(hook),
            new HeroCompositionSceneBlockDto("cinematic sky"),
            new HeroCompositionTextBlockDto("direction", "Eastern sky"),
            new HeroCompositionTextBlockDto("timing", "9:00 PM local"),
            new HeroCompositionTextBlockDto("cta", hook),
            new HeroCompositionValidationDto(true, true, true, true, true, 100));

    private static (string RenderedTitleText, string RenderedTitleSource, string HookText, bool HookUsedAsTitle, bool TitleMatchedLocalizedTitle, bool TitleResolverUsed) InvokeHeroV65TitleResolver(
        HeroAssetStoryDto story,
        string selectedHook,
        HeroCompositionModelDto composition,
        ProductionEventIntelligence intelligence)
    {
        var method = typeof(HeroAssetIntelligenceEngine).GetMethod("ResolveHeroV6OverlayTitleAndSubtitle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var tuple = (System.Runtime.CompilerServices.ITuple)method!.Invoke(null, [story, selectedHook, composition, intelligence])!;
        return (
            (string)tuple[0]!,
            (string)tuple[1]!,
            (string)tuple[6]!,
            (bool)tuple[7]!,
            (bool)tuple[8]!,
            (bool)tuple[9]!);
    }

    private static ProductionEventIntelligence BuildMeteorProductionEventIntelligence()
        => new(
            "Astronomy",
            "MeteorShower",
            "Geminid meteor shower viewing window",
            "Geminids peak tonight",
            DateTimeOffset.Parse("2026-12-14T00:00:00Z"),
            DateTimeOffset.Parse("2026-12-14T02:00:00Z"),
            "2:00 AM local",
            "Midnight to dawn",
            "Radiant high in the dark sky",
            "Udaipur",
            ["Geminids"],
            ["Meteors"],
            "Clear dark sky",
            "low moon interference",
            12,
            "Meteor showers are stream debris crossing Earth’s orbit.",
            ["Find a dark sky", "Watch during the viewing window"],
            ["meteor streaks", "radiant hint", "dark sky", "low moon interference", "viewing window"],
            ["Open on meteor streaks", "Show radiant hint", "Close with viewing window"],
            [],
            "MeteorShower",
            RequiredVisualObjects: ["meteor streaks", "radiant hint", "dark sky", "low moon interference", "viewing window"],
            HeroCopyCandidates: ["METEORS PEAK TONIGHT", "WATCH AFTER MIDNIGHT"]);

    private static ProductionEventIntelligence BuildSolarEclipseProductionEventIntelligence()
        => new(
            "Astronomy",
            "SolarEclipse",
            "Total Solar Eclipse",
            "Total eclipse",
            DateTimeOffset.Parse("2026-08-12T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-12T18:18:00Z"),
            "2:18 PM EDT",
            "During totality",
            "Southwest sky",
            "United States",
            ["Sun", "Moon"],
            ["Corona"],
            "Cinematic totality",
            "safe solar viewing",
            12,
            "The Moon covers the Sun during a solar eclipse.",
            ["Use certified solar glasses"],
            ["large corona", "ring silhouette", "dramatic sky glow"],
            ["Open on totality"],
            [],
            "SolarEclipse",
            RequiredVisualObjects: ["large corona", "ring silhouette"],
            HeroCopyCandidates: ["TOTAL SOLAR ECLIPSE"]);

    private static async Task WriteInputFilesAsync(string workingDirectory)
    {
        var questionEngineRoot = BuildQuestionEnginePath(workingDirectory);
        Directory.CreateDirectory(questionEngineRoot);
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-answer-set.json"), JsonSerializer.Serialize(BuildQuestionAnswerSet(), JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-scene-plan.enriched.json"), JsonSerializer.Serialize(BuildEnrichedPlan(), JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-narration.json"), JsonSerializer.Serialize(BuildNarration(), JsonOptions));

        var sceneApprovalRoot = Path.Combine(questionEngineRoot, "scene-approval-v3");
        var approvedScenePng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        foreach (var profile in new[] { "short", "long" })
        {
            var profileRoot = Path.Combine(sceneApprovalRoot, profile);
            Directory.CreateDirectory(profileRoot);
            for (var sceneNumber = 1; sceneNumber <= 6; sceneNumber++)
                await File.WriteAllBytesAsync(Path.Combine(profileRoot, $"scene-{sceneNumber:000}-final.png"), approvedScenePng);
        }

        await WriteCelestialTestAssetAsync(workingDirectory, "venus");
        await WriteCelestialTestAssetAsync(workingDirectory, "jupiter");
    }

    private static async Task WriteCelestialTestAssetAsync(string workingDirectory, string objectName)
    {
        var assetDirectory = Path.Combine(workingDirectory, "assets", "celestial", objectName);
        Directory.CreateDirectory(assetDirectory);
        var transparentOnePixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(Path.Combine(assetDirectory, "hero-transparent.png"), transparentOnePixelPng);
    }

    private static QuestionAnswerSetDto BuildQuestionAnswerSet() => new(
        null,
        Guid.Parse(EventId),
        "VENUS_JUPITER_CLOSE_PAIRING",
        "Venus and Jupiter Close Pairing",
        "PlanetConjunction",
        RegionId,
        "en",
        "v1",
        "Approved",
        DateTimeOffset.Parse("2026-06-07T14:00:00Z"),
        [
            new QuestionAnswerDto(null, AstronomyQuestionTypes.What, "What is happening?", "What", "Venus and Jupiter will appear close together in Udaipur’s evening sky.", 1),
            new QuestionAnswerDto(null, AstronomyQuestionTypes.Where, "Where should I look?", "Where", "Look toward the western sky, about one-third above the horizon.", 2),
            new QuestionAnswerDto(null, AstronomyQuestionTypes.When, "When is best?", "When", "Best viewing is around 7:23 PM IST, shortly after sunset.", 3),
            new QuestionAnswerDto(null, AstronomyQuestionTypes.How, "How can I find it?", "How", "Find bright Venus first, then look slightly nearby for Jupiter.", 4),
            new QuestionAnswerDto(null, AstronomyQuestionTypes.Why, "Why does it matter?", "Why", "Venus and Jupiter appear only 1.63° apart, creating a striking planetary pairing.", 5),
            new QuestionAnswerDto(null, AstronomyQuestionTypes.Action, "What should I do?", "Action", "If skies are clear, step outside after sunset and enjoy the view.", 6)
        ]);

    private static EnrichedQuestionScenePlanDto BuildEnrichedPlan() => new(
        EventId,
        RegionId,
        "en",
        "CasualSkyWatcher",
        "Beginner",
        [
            BuildScene(1, AstronomyQuestionTypes.What, "Venus and Jupiter appear close together tonight."),
            BuildScene(2, AstronomyQuestionTypes.Where, "Look west above the horizon for Venus and Jupiter."),
            BuildScene(3, AstronomyQuestionTypes.When, "The best time is around 7:23 PM IST after sunset."),
            BuildScene(4, AstronomyQuestionTypes.How, "Find Venus first, then look nearby for Jupiter while facing west."),
            BuildScene(5, AstronomyQuestionTypes.Why, "Venus and Jupiter form a close bright planetary pairing."),
            BuildScene(6, AstronomyQuestionTypes.Action, "Step outside tonight and look west for Venus and Jupiter.")
        ],
        true,
        DateTimeOffset.Parse("2026-06-07T14:00:00Z"));

    private static EnrichedQuestionSceneDto BuildScene(int sceneNumber, string questionType, string sourceAnswer)
        => new(
            sceneNumber,
            questionType,
            "Hero story input scene.",
            $"Viewer question for {questionType}.",
            sourceAnswer,
            "CasualSkyWatcher",
            "Beginner",
            sourceAnswer,
            $"Narration intent for {questionType}.",
            $"Visual intent for {questionType}.",
            $"Image prompt intent for {questionType}.",
            $"Overlay intent for {questionType}.",
            $"Accessibility intent for {questionType}.",
            true);

    private static QuestionDrivenNarrationDto BuildNarration() => new(
        EventId,
        RegionId,
        "en",
        [
            BuildNarrationScene(1, AstronomyQuestionTypes.What, "Tonight, Venus and Jupiter appear close in Udaipur’s western sky."),
            BuildNarrationScene(2, AstronomyQuestionTypes.Where, "Face west and scan above the horizon to spot Venus and Jupiter."),
            BuildNarrationScene(3, AstronomyQuestionTypes.When, "The best viewing time is around 7:23 PM IST, shortly after sunset."),
            BuildNarrationScene(4, AstronomyQuestionTypes.How, "Find Venus first, then look nearby for Jupiter while you face west."),
            BuildNarrationScene(5, AstronomyQuestionTypes.Why, "It matters because two bright planets make a close, beautiful pairing."),
            BuildNarrationScene(6, AstronomyQuestionTypes.Action, "If skies are clear, step outside tonight and look west for Venus and Jupiter.")
        ],
        54,
        DateTimeOffset.Parse("2026-06-07T14:05:00Z"));

    private static QuestionDrivenNarrationSceneDto BuildNarrationScene(int sceneNumber, string questionType, string narrationText)
        => new(
            sceneNumber,
            questionType,
            "Hero story narration source.",
            $"Viewer question for {questionType}.",
            narrationText,
            narrationText,
            $"Narration intent for {questionType}.",
            narrationText,
            9,
            "Warm and clear.",
            narrationText);

    private static string BuildQuestionEnginePath(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine");

    private static string BuildOutputPath(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "hero-assets", "hero-asset-story.json");

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hero-asset-story-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    [Fact]
    public void HeroMetadataNormalizer_CompactsPhase11SolarEclipseFooterMetadata()
    {
        Assert.Equal("11:30 PM IST", HeroMetadataNormalizer.NormalizeTime("Watch during 2026-08-12 23:30 +05:30 for maximum eclipse.", "SolarEclipse", "en"));
        Assert.Equal("MAX ECLIPSE", HeroMetadataNormalizer.NormalizeTime("Exact eclipse time unknown", "SolarEclipse", "en"));
        Assert.Equal("SAFE SOLAR VIEWING", HeroMetadataNormalizer.NormalizeDirection("Use local sky visibility for IN-RJ-UDAIPUR and keep the Sun safely filtered.", "SolarEclipse", "en"));
        Assert.Equal("TOTAL SOLAR ECLIPSE", HeroMetadataNormalizer.NormalizeTitle("Total Solar Eclipse, IN-RJ-UDAIPUR", "SolarEclipse", "en"));
        Assert.Equal("SUN + MOON", HeroMetadataNormalizer.NormalizeSubtitle(["Sun", "Moon"], "Solar Eclipse", "SolarEclipse", "en"));
    }

    [Fact]
    public void HeroMetadataNormalizer_CompactsPhase11PlanetAndMoonFooterMetadata()
    {
        Assert.Equal("7:23 PM IST", HeroMetadataNormalizer.NormalizeTime("Best viewing is 7:23 PM IST, shortly after sunset.", "PlanetGrouping", "en"));
        Assert.Equal("NORTHEAST", HeroMetadataNormalizer.NormalizeDirection("Northeast after midnight", "MeteorShower", "en"));
        Assert.Equal("SOUTHEAST", HeroMetadataNormalizer.NormalizeDirection("Look toward the southeastern horizon before sunrise", "PlanetPairing", "en"));
        Assert.Equal("EASTERN SKY", HeroMetadataNormalizer.NormalizeDirection("Look toward the eastern sky above the horizon.", "PlanetGrouping", "en"));
        Assert.Equal("EASTERN SKY", HeroMetadataNormalizer.NormalizeDirection("Eastern sky near moonrise", "NamedFullMoon", "en"));
        Assert.Equal("SATURN + MARS + JUPITER + VENUS", HeroMetadataNormalizer.NormalizeSubtitle(["Saturn", "Mars", "Jupiter", "Venus"], "Grouped planets", "PlanetGrouping", "en"));
        Assert.Equal("SATURN + MARS + JUPITER + 2 MORE", HeroMetadataNormalizer.NormalizeSubtitle(["Saturn", "Mars", "Jupiter", "Venus", "Mercury"], "Grouped planets", "PlanetGrouping", "en"));
        Assert.Equal("FULL MOON", HeroMetadataNormalizer.NormalizeSubtitle(["Moon"], "Wolf Moon", "NamedFullMoon", "en"));
    }



    [Theory]
    [InlineData("PlanetConjunction", "Jupiter Mars Conjunction", "बृहस्पति और मंगल")]
    [InlineData("SolarEclipse", "Total Solar Eclipse", "पूर्ण सूर्य ग्रहण")]
    [InlineData("LunarEclipse", "Total Lunar Eclipse", "पूर्ण चंद्र ग्रहण")]
    [InlineData("MeteorShower", "Geminids Meteor Shower Peak", "जेमिनिड्स उल्का वर्षा")]
    public void BuildHeroOverlayLines_HindiUsesEventSpecificTitleInsteadOfGenericHook(string eventType, string title, string expectedTitle)
    {
        var story = new HeroAssetStoryDto(
            EventId,
            RegionId,
            "hi",
            "चरम क्षण को न चूकें",
            "आज रात आसमान का खास दृश्य अपनी पूरी चमक पर होगा।",
            "सूर्यास्त के बाद पश्चिम की ओर देखें",
            "नाटकीय सांध्य आकाश में चमकता खगोलीय दृश्य।",
            "आश्चर्य + तत्परता",
            "ScrollStoppingHeroAsset",
            new HeroStorySourceDto(title, "western sky", "7:23 PM IST", "close sky event"),
            new HeroAssetStoryScoresDto(95, 95, 95, 95),
            95,
            DateTimeOffset.Parse("2026-06-27T00:00:00Z"));
        var intelligence = BuildProductionEventIntelligence(eventType, title) with
        {
            PrimaryObjects = eventType == "PlanetConjunction" ? ["Jupiter"] : eventType == "MeteorShower" ? ["Geminids"] : ["Sun"],
            SecondaryObjects = eventType == "PlanetConjunction" ? ["Mars"] : eventType == "SolarEclipse" ? ["Moon"] : []
        };
        var method = typeof(HeroAssetStoryGenerator).GetMethod("BuildHeroOverlayLines", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var result = ((string Title, string Subtitle))method!.Invoke(null, [story, "चरम क्षण को न चूकें", intelligence])!;

        Assert.Equal(expectedTitle, result.Title);
        Assert.NotEqual("चरम क्षण को न चूकें", result.Title);
        Assert.Equal("चरम क्षण को न चूकें", result.Subtitle);
    }

    [Theory]
    [InlineData("SolarEclipse", "Total Solar Eclipse", "Sun", "Moon", "पूर्ण सूर्य ग्रहण")]
    [InlineData("MeteorShower", "Perseids Meteor Shower Peak", "Perseids", "", "पर्सिड्स उल्का वर्षा")]
    [InlineData("MeteorShower", "Geminids Meteor Shower Peak", "Geminids", "", "जेमिनिड्स उल्का वर्षा")]
    [InlineData("PlanetConjunction", "Jupiter Venus Conjunction", "Jupiter", "Venus", "बृहस्पति और शुक्र")]
    [InlineData("PlanetConjunction", "Jupiter Mars Conjunction", "Jupiter", "Mars", "बृहस्पति और मंगल")]
    public void HeroV65TitleResolver_HindiUsesLocalizedEventTitleInsteadOfGenericHook(string eventType, string title, string primaryObject, string secondaryObject, string expectedTitle)
    {
        var story = BuildHeroStory("hi", title, "चरम क्षण को न चूकें");
        var intelligence = BuildProductionEventIntelligence(eventType, title) with
        {
            PrimaryObjects = string.IsNullOrWhiteSpace(primaryObject) ? [] : [primaryObject],
            SecondaryObjects = string.IsNullOrWhiteSpace(secondaryObject) ? [] : [secondaryObject]
        };
        var composition = BuildComposition("चरम क्षण को न चूकें") with
        {
            TitleBlock = new HeroCompositionTextBlockDto("", "")
        };

        var resolved = InvokeHeroV65TitleResolver(story, "चरम क्षण को न चूकें", composition, intelligence);

        Assert.Equal(expectedTitle, resolved.RenderedTitleText);
        Assert.Equal("localizedTitleText", resolved.RenderedTitleSource);
        Assert.Equal("चरम क्षण को न चूकें", resolved.HookText);
        Assert.False(resolved.HookUsedAsTitle);
        Assert.True(resolved.TitleMatchedLocalizedTitle);
        Assert.True(resolved.TitleResolverUsed);
    }

    [Theory]
    [InlineData("SolarEclipse", "Total Solar Eclipse", "TOTAL SOLAR ECLIPSE")]
    [InlineData("MeteorShower", "Perseids Meteor Shower Peak", "PERSEIDS METEOR SHOWER PEAK")]
    [InlineData("PlanetConjunction", "Jupiter Venus Conjunction", "JUPITER VENUS CONJUNCTION")]
    public void HeroV65TitleResolver_EnglishKeepsEventSpecificTitleBehavior(string eventType, string title, string expectedTitle)
    {
        var story = BuildHeroStory("en", title, "DON'T MISS THE PEAK");
        var composition = BuildComposition("DON'T MISS THE PEAK");

        var resolved = InvokeHeroV65TitleResolver(story, "DON'T MISS THE PEAK", composition, BuildProductionEventIntelligence(eventType, title));

        Assert.Equal(expectedTitle, resolved.RenderedTitleText);
        Assert.NotEqual("DON'T MISS THE PEAK", resolved.RenderedTitleText);
        Assert.True(resolved.TitleResolverUsed);
    }

    [Fact]
    public void Phase11HeroLayoutValidation_ReportsCompositionBeforeHookValidation()
    {
        var composition = new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto("VENUS AND JUPITER"),
            new HeroCompositionSceneBlockDto("planet conjunction background"),
            new HeroCompositionTextBlockDto("direction-panel", "Western sky"),
            new HeroCompositionTextBlockDto("date-time-panel", "7:23 PM IST"),
            new HeroCompositionTextBlockDto("", ""),
            new HeroCompositionValidationDto(true, true, true, true, false, 100));
        var method = typeof(HeroAssetStoryGenerator).GetMethod("BuildHeroLayoutValidation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var validation = (HeroLayoutValidationDto)method!.Invoke(null, [composition, new[] { "Venus", "Jupiter" }, true, "PlanetConjunction", false])!;

        Assert.Equal("Background generation", validation.ValidationOrder[0]);
        Assert.Equal("Composition quality", validation.ValidationOrder[5]);
        Assert.Equal("Overlay layout", validation.ValidationOrder[6]);
        Assert.Equal("Hook validation", validation.ValidationOrder[7]);
        Assert.All(validation.CompositionReports, report => Assert.Equal("PASS", report.Status));
    }

    [Fact]
    public void AzureHeroPromptBuilderV2_ConjunctionPromptsAreVariantSpecific()
    {
        var composition = new HeroCompositionModelDto(
            new HeroCompositionHookBlockDto("VENUS JUPITER CONJUNCTION"),
            new HeroCompositionSceneBlockDto("planet conjunction background"),
            new HeroCompositionTextBlockDto("direction-panel", "Western sky"),
            new HeroCompositionTextBlockDto("date-time-panel", "7:23 PM IST"),
            new HeroCompositionTextBlockDto("", ""),
            new HeroCompositionValidationDto(true, true, true, true, false, 100));
        var story = new HeroAssetStoryDto(
            EventId,
            RegionId,
            "en",
            "VENUS JUPITER CONJUNCTION",
            "Venus and Jupiter appear close together.",
            "Look west after sunset.",
            "Two bright planets in twilight.",
            "Wonder",
            "ScrollStoppingHeroAsset",
            new HeroStorySourceDto("Venus Jupiter conjunction", "western sky", "7:23 PM IST", "close planet pairing"),
            new HeroAssetStoryScoresDto(95, 95, 95, 95),
            95,
            DateTimeOffset.Parse("2026-06-27T00:00:00Z"));
        var builder = typeof(HeroAssetStoryGenerator).GetNestedType("AzureHeroPromptBuilderV2", System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(builder);
        var method = builder!.GetMethod("Build", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var prompts = ((System.Collections.IEnumerable)method!.Invoke(null, [composition, story, "VENUS JUPITER CONJUNCTION", BuildProductionEventIntelligence("PlanetConjunction", "Venus Jupiter Planetary Conjunction"), null])!)
            .Cast<object>()
            .Select(prompt => ((string)((System.Runtime.CompilerServices.ITuple)prompt)[0]!, (string)((System.Runtime.CompilerServices.ITuple)prompt)[4]!))
            .ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("side-by-side composition", prompts["landscape"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vertical composition", prompts["portrait"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not crop the secondary planet", prompts["portrait"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("centered composition", prompts["square"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("both planets fully inside", prompts["square"], StringComparison.OrdinalIgnoreCase);
    }

}
