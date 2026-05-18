using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ThumbnailGenerationTests
{
    [Fact]
    public void ThumbnailStrategy_GeneratesContentSpecificTextAndLayout()
    {
        var service = new ThumbnailStrategyService();
        var context = new AstronomyContext
        {
            Events = [new AstronomyEventModel { ObjectName = "Jupiter", Score = 0.95 }],
            NewsItems = [new NewsItemModel { Headline = "Scientists discover unusual exoplanet", PublishedDate = DateOnly.FromDateTime(DateTime.UtcNow) }]
        };

        var dailyPlan = service.BuildPlan(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = context,
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = "."
        });

        var newsPlan = service.BuildPlan(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.SpaceNews,
            Context = context,
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = "."
        });

        Assert.Contains("TONIGHT'S SKY", dailyPlan.PrimaryThumbnailText);
        Assert.Equal(ThumbnailLayoutType.TopBanner, dailyPlan.LayoutType);
        Assert.Equal(dailyPlan.LayoutType, dailyPlan.LayoutCandidates.First());
        Assert.NotEmpty(dailyPlan.Variants);
        Assert.Contains("DISCOVERY", newsPlan.PrimaryThumbnailText);
        Assert.Equal(ThumbnailLayoutType.CenteredTitleOverlay, newsPlan.LayoutType);
        Assert.Equal(newsPlan.LayoutType, newsPlan.LayoutCandidates.First());
    }

    [Fact]
    public void ThumbnailStrategy_UsesFeedbackSignalsToPromoteLayouts()
    {
        var service = new ThumbnailStrategyService();

        var plan = service.BuildPlan(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.SpaceNews,
            Context = new AstronomyContext(),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = ".",
            FeedbackSignals = new FeedbackSignals
            {
                TopKeywords = ["tonight guide", "tonight viewing"]
            }
        });

        Assert.Equal(ThumbnailLayoutType.TopBanner, plan.LayoutType);
        Assert.Equal(3, plan.LayoutCandidates.Count);
    }


    [Fact]
    public void ThumbnailStrategy_UsesExperimentFeedbackHintsForVariants()
    {
        var service = new ThumbnailStrategyService();

        var plan = service.BuildPlan(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = new AstronomyContext
            {
                PromptFeedbackContext = new PromptFeedbackContext
                {
                    ThumbnailStrategyHints = ["Recent winning thumbnail pattern: TopBanner: BIG MOON"]
                }
            },
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = "."
        });

        Assert.Contains(plan.AlternateThumbnailTexts, x => x.Contains("BIG MOON", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ThumbnailLayoutType.TopBanner, plan.LayoutCandidates.First());
    }

    [Fact]
    public async Task ThumbnailGeneration_FallsBack_WhenNoVisualsAvailable()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"thumb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        var service = new ThumbnailGenerationService(new ThumbnailStrategyService(), NullLogger<ThumbnailGenerationService>.Instance);
        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.TelescopeTargets,
            Context = new AstronomyContext(),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        Assert.NotNull(plan.ThumbnailPath);
        Assert.True(File.Exists(plan.ThumbnailPath));
        Assert.Single(plan.ThumbnailVariantPaths);
        Assert.All(plan.ThumbnailVariantPaths, path => Assert.True(File.Exists(path)));
        Assert.All(plan.ThumbnailVariantPaths, path => Assert.EndsWith(".jpg", path));
    }

    [Fact]
    public async Task ThumbnailGeneration_ReturnsSourceVisual_WhenCompositionFails()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"thumb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        var invalidVisual = Path.Combine(outputDir, "not-an-image.png");
        await File.WriteAllTextAsync(invalidVisual, "not-image");

        var service = new ThumbnailGenerationService(new ThumbnailStrategyService(), NullLogger<ThumbnailGenerationService>.Instance);
        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.AstrophotographyTips,
            Context = new AstronomyContext(),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [invalidVisual],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        Assert.Equal(invalidVisual, plan.ThumbnailPath);
        Assert.Single(plan.ThumbnailVariantPaths);
    }

    [Fact]
    public void ThumbnailStrategy_GeneratesSpecialEventText()
    {
        var service = new ThumbnailStrategyService();
        var plan = service.BuildPlan(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.SpecialEventGuide,
            Context = new AstronomyContext
            {
                SpecialEvent = new SpecialEventContext
                {
                    EventId = "moon-full-moon-20260504",
                    EventType = "full_moon",
                    EventTitle = "Full Moon Tonight",
                    EventDescription = "Full moon event."
                }
            },
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.GetTempPath()
        });

        Assert.Contains("FULL MOON", plan.PrimaryThumbnailText);
        Assert.Equal(ThumbnailLayoutType.CenteredTitleOverlay, plan.LayoutType);
    }

    [Fact]
    public async Task ThumbnailScoring_RejectsBlackFrames()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"thumb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        var black = Path.Combine(outputDir, "black.jpg");
        using (var image = new Image<Rgba32>(320, 180, Color.Black))
            await image.SaveAsJpegAsync(black);

        var service = new ThumbnailScoringService();
        var score = await service.ScoreAsync(black, new ThumbnailScoringContext
        {
            RejectDarkFrames = true,
            MaxBlackPixelPercentage = 0.40,
            MinimumBrightnessScore = 0.35
        }, CancellationToken.None);

        Assert.True(score.IsRejected);
        Assert.True(score.BlackPixelPercentage > 0.40);
    }

    [Fact]
    public async Task ThumbnailGeneration_AvoidsFadeFrames_WhenBuildingCandidates()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"thumb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        var source = Path.Combine(outputDir, "moon.jpg");
        await WriteMoonImageAsync(source, 1280, 720);

        var service = CreateProductionService(new ThumbnailOptions { FadeAvoidanceSeconds = 1.0 });
        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [source],
            OutputDirectory = outputDir,
            Scenes = [new RenderScene { SceneId = "moon", VisualPath = source, DurationSeconds = 12 }]
        }, CancellationToken.None);

        Assert.All(plan.CandidateScores, score => Assert.InRange(score.TimestampSeconds, 1.0, 11.0));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(outputDir, "thumbnails", "candidates"), "*.jpg").Length);
    }

    [Fact]
    public async Task ThumbnailGeneration_ExportsLongAndShortJpegsWithExpectedDimensions()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"thumb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        var source = Path.Combine(outputDir, "jupiter.jpg");
        await WriteMoonImageAsync(source, 1280, 720);
        var service = CreateProductionService(new ThumbnailOptions());

        var longPlan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.SpecialEventGuide,
            Context = BuildContext("en"),
            Metadata = new OptimizedVideoMetadata { HookLine = "Rare Alignment Tonight" },
            AvailableVisuals = [source],
            OutputDirectory = outputDir,
            Scenes = [new RenderScene { SceneId = "jupiter", VisualPath = source, DurationSeconds = 10 }]
        }, CancellationToken.None);

        var shortDir = Path.Combine(outputDir, "shorts");
        var shortPlan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.SpecialEventGuide,
            Context = BuildContext("en"),
            Metadata = new OptimizedVideoMetadata { HookLine = "Visible Tonight" },
            AvailableVisuals = [source],
            OutputDirectory = shortDir,
            IsShortForm = true,
            Scenes = [new RenderScene { SceneId = "jupiter", VisualPath = source, DurationSeconds = 6 }]
        }, CancellationToken.None);

        Assert.EndsWith("thumbnail-long.jpg", longPlan.ThumbnailPath);
        Assert.EndsWith("thumbnail-short.jpg", shortPlan.ThumbnailPath);
        using var longImage = await Image.LoadAsync(longPlan.ThumbnailPath!);
        using var shortImage = await Image.LoadAsync(shortPlan.ThumbnailPath!);
        Assert.Equal(1280, longImage.Width);
        Assert.Equal(720, longImage.Height);
        Assert.Equal(1080, shortImage.Width);
        Assert.Equal(1920, shortImage.Height);
        Assert.True(File.Exists(Path.Combine(outputDir, "thumbnail-analysis-report.json")));
        Assert.True(File.Exists(Path.Combine(shortDir, "thumbnail-analysis-report.json")));
    }

    [Fact]
    public void ThumbnailHookService_GeneratesEnglishAndHindiHooks()
    {
        var service = new ThumbnailHookService();
        var english = service.GenerateHook(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildContext("en"),
            Metadata = new OptimizedVideoMetadata { HookLine = "Astronomy Sky Guide for 2026-05-14" },
            AvailableVisuals = [],
            OutputDirectory = "."
        }, 5);
        var hindi = service.GenerateHook(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildContext("hi"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = "."
        }, 5);

        Assert.InRange(english.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 2, 5);
        Assert.DoesNotContain("Guide", english, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("Visible Tonight", english);
        Assert.InRange(hindi.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 2, 5);
        Assert.Contains("आज", hindi);
    }

    [Fact]
    public async Task ThumbnailGeneration_AppliesBrandingAndReportsDiagnostics()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"thumb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        var source = Path.Combine(outputDir, "moon.jpg");
        await WriteMoonImageAsync(source, 1280, 720);
        var service = CreateProductionService(new ThumbnailOptions { BrandText = "AstroPulse" });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [source],
            OutputDirectory = outputDir,
            Scenes = [new RenderScene { SceneId = "moon", VisualPath = source, DurationSeconds = 10 }]
        }, CancellationToken.None);

        Assert.True(File.Exists(plan.ThumbnailPath));
        var report = await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnail-analysis-report.json"));
        Assert.Contains("AstroPulse", report);
        Assert.Contains("candidateScores", report);
    }


    [Fact]
    public async Task LocalAssetCollage_CreatesLongAndShortThumbnails_FromCuratedAssets()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "jupiter", "hero.png"), Color.Orange);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "venus", "hero.png"), Color.Gold);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot });

        var longPlan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);
        var shortPlan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir,
            IsShortForm = true
        }, CancellationToken.None);

        Assert.EndsWith("thumbnail-long.jpg", longPlan.LongThumbnailPath);
        Assert.EndsWith("thumbnail-short.jpg", shortPlan.ShortThumbnailPath);
        Assert.True(File.Exists(longPlan.LongThumbnailPath));
        Assert.True(File.Exists(shortPlan.ShortThumbnailPath));
        using var longImage = await Image.LoadAsync(longPlan.LongThumbnailPath!);
        using var shortImage = await Image.LoadAsync(shortPlan.ShortThumbnailPath!);
        Assert.Equal(1280, longImage.Width);
        Assert.Equal(720, longImage.Height);
        Assert.Equal(1080, shortImage.Width);
        Assert.Equal(1920, shortImage.Height);
    }

    [Fact]
    public async Task LocalAssetCollage_SelectsHeroAndCapsSupportObjects()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        foreach (var key in new[] { "jupiter", "venus", "mars", "saturn", "milky-way" })
            await WriteCuratedAssetAsync(Path.Combine(assetRoot, key, "hero.png"), Color.White);
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot, MaxSupportObjectsLong = 2, MaxSupportObjectsShort = 1 });

        var longPlan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}")
        }, CancellationToken.None);
        var shortPlan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}"),
            IsShortForm = true
        }, CancellationToken.None);

        Assert.Equal("Jupiter", longPlan.CelestialSelection?.HeroObject);
        Assert.Equal(2, longPlan.CelestialSelection?.SupportObjects.Count);
        Assert.Single(shortPlan.CelestialSelection!.SupportObjects);
    }

    [Fact]
    public async Task LocalAssetCollage_FallsBackSafely_WhenHeroAssetMissing()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot });
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        Assert.True(File.Exists(plan.ThumbnailPath));
        Assert.True(plan.FallbackUsed);
        Assert.True(new FileInfo(plan.ThumbnailPath!).Length <= 2_097_152);
    }

    [Fact]
    public async Task LocalAssetCollage_GeneratesEnglishAndHindiHooks()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "jupiter", "hero.png"), Color.Orange);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot });

        var english = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}")
        }, CancellationToken.None);
        var hindi = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("hi"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}")
        }, CancellationToken.None);

        Assert.Equal("Jupiter Tonight", english.CelestialSelection?.SelectedHook);
        Assert.Equal("आज रात बृहस्पति", hindi.CelestialSelection?.SelectedHook);
    }




    [Fact]
    public async Task LocalAssetCollage_EnglishThumbnailUsesMontserratTextfileDrawtext()
    {
        var (assetRoot, englishFont, hindiFont) = await CreateThumbnailTextRenderAssetsAsync();
        var runner = new CapturingThumbnailTextProcessRunner();
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot }, englishFont, hindiFont, runner);

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}")
        }, CancellationToken.None);

        Assert.True(File.Exists(plan.LongThumbnailPath));
        Assert.Contains("drawtext=fontfile=", runner.LastArguments);
        Assert.Contains("Montserrat-ExtraBold.ttf", runner.LastArguments);
        Assert.Contains("textfile=", runner.LastArguments);
        Assert.Contains("thumbnail-title-en.txt", runner.LastArguments);
        Assert.DoesNotContain("drawtext=text=", runner.LastArguments);
        Assert.DoesNotContain("□", runner.LastTextFileContents);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(plan.ThumbnailPath!)!, "thumbnail-font-report.json")));
        Assert.Equal("en", report.RootElement.GetProperty("language").GetString());
        Assert.Equal("textfile", report.RootElement.GetProperty("drawTextMode").GetString());
        Assert.True(report.RootElement.GetProperty("fontExists").GetBoolean());
        Assert.Equal(englishFont, report.RootElement.GetProperty("selectedFontConfigPath").GetString());
        Assert.Equal(englishFont, report.RootElement.GetProperty("selectedFontResolvedPath").GetString());
        Assert.Contains("drawtext=", report.RootElement.GetProperty("drawTextFilter").GetString() ?? string.Empty);
        Assert.Equal(0, report.RootElement.GetProperty("ffmpegExitCode").GetInt32());
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(plan.ThumbnailPath!)!, "thumbnail-text-debug-command.txt")));
    }

    [Fact]
    public async Task LocalAssetCollage_HindiLanguageSelectsDevanagariFontAndRendersWithoutBoxes()
    {
        var (assetRoot, englishFont, hindiFont) = await CreateThumbnailTextRenderAssetsAsync();
        var runner = new CapturingThumbnailTextProcessRunner();
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot }, englishFont, hindiFont, runner);

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("hi"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}"),
            IsShortForm = true
        }, CancellationToken.None);

        Assert.True(File.Exists(plan.ShortThumbnailPath));
        Assert.Contains("NotoSansDevanagari-Bold.ttf", runner.LastArguments);
        Assert.Contains("fontfile=", runner.LastArguments);
        Assert.Contains("textfile=", runner.LastArguments);
        Assert.Contains("thumbnail-title-hi.txt", runner.LastArguments);
        Assert.Contains("आज रात बृहस्पति", runner.LastTextFileContents);
        Assert.DoesNotContain("□", runner.LastTextFileContents);

        var thumbnailsDir = Path.GetDirectoryName(plan.ShortThumbnailPath!)!;
        using var analysis = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(thumbnailsDir, "thumbnail-analysis-report.json")));
        Assert.Equal("hi", analysis.RootElement.GetProperty("language").GetString());
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(thumbnailsDir, "thumbnail-font-report.json")));
        Assert.Equal("hi", report.RootElement.GetProperty("language").GetString());
        Assert.Equal("आज रात बृहस्पति", report.RootElement.GetProperty("selectedHook").GetString());
        Assert.True(report.RootElement.GetProperty("containsDevanagari").GetBoolean());
        Assert.Contains("NotoSansDevanagari-Bold.ttf", report.RootElement.GetProperty("selectedFontPath").GetString() ?? string.Empty);
        Assert.Contains("NotoSansDevanagari-Bold.ttf", report.RootElement.GetProperty("selectedFontResolvedPath").GetString() ?? string.Empty);
        Assert.True(report.RootElement.GetProperty("fontExists").GetBoolean());
        Assert.True(report.RootElement.GetProperty("textFileExists").GetBoolean());
        Assert.True(report.RootElement.GetProperty("textFileSizeBytes").GetInt64() > 0);
        Assert.Equal("UTF-8", report.RootElement.GetProperty("textFileEncoding").GetString());
        Assert.Equal("textfile", report.RootElement.GetProperty("drawTextMode").GetString());
        Assert.Contains("drawtext=", report.RootElement.GetProperty("drawTextFilter").GetString() ?? string.Empty);
    }

    [Fact]
    public void LocalAssetCollage_DrawtextEscapesWindowsDriveAndSingleQuotes()
    {
        var escaped = FfmpegPathEscaper.ToDrawTextPath(@"D:\Astronomy Workspace\fonts\John's Font.ttf");

        Assert.Equal("D\\\\:/Astronomy Workspace/fonts/John\\'s Font.ttf", escaped);
    }

    [Fact]
    public void LocalAssetCollage_DrawtextFilterAppendsEscapedFontAndTextfilePaths()
    {
        var filter = LocalAssetCollageThumbnailService.BuildDrawTextFilter(
            @"D:\AstronomyWorkspace\assets\fonts\Montserrat-ExtraBold.ttf",
            @"D:\AstronomyWorkspace\out\thumbnail-title-en.txt",
            84,
            new RectangleF(48, 120, 500, 260),
            portrait: false);

        Assert.Contains("drawtext=fontfile='D\\\\:/AstronomyWorkspace/assets/fonts/Montserrat-ExtraBold.ttf'", filter);
        Assert.Contains("textfile='D\\\\:/AstronomyWorkspace/out/thumbnail-title-en.txt'", filter);
        Assert.Contains("fontcolor=white@0.97", filter);
        Assert.Contains("fontsize=84", filter);
        Assert.Contains("x=48", filter);
        Assert.Contains("y=120", filter);
    }

    [Fact]
    public void LocalAssetCollage_DevanagariTextSelectsHindiRenderingEvenWhenLanguageIsEnglish()
    {
        Assert.True(LocalAssetCollageThumbnailService.IsHindiThumbnailText("en", "आज रात बृहस्पति"));
        Assert.False(LocalAssetCollageThumbnailService.IsHindiThumbnailText("en", "Jupiter Tonight"));
    }

    [Fact]
    public async Task LocalAssetCollage_Utf8TextfileRenderingCleansTemporaryFiles()
    {
        var (assetRoot, englishFont, hindiFont) = await CreateThumbnailTextRenderAssetsAsync();
        var runner = new CapturingThumbnailTextProcessRunner();
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot }, englishFont, hindiFont, runner);

        await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("hi"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}"),
            IsShortForm = true
        }, CancellationToken.None);

        Assert.NotNull(runner.LastTextFilePath);
        Assert.EndsWith("thumbnail-title-hi.txt", runner.LastTextFilePath);
        Assert.True(File.Exists(runner.LastTextFilePath));
        Assert.True(runner.LastTextFileBytes!.Length > 0);
        Assert.False(runner.LastTextFileBytes![0] == 0xEF && runner.LastTextFileBytes.Length > 2 && runner.LastTextFileBytes[1] == 0xBB && runner.LastTextFileBytes[2] == 0xBF);
        Assert.DoesNotContain(Directory.GetFiles(Path.GetDirectoryName(runner.LastTextFilePath!)!), path => Path.GetFileName(path).StartsWith("temp-thumbnail-title-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LocalAssetCollage_MissingHindiFontFailsClearly()
    {
        var (assetRoot, englishFont, hindiFont) = await CreateThumbnailTextRenderAssetsAsync();
        File.Delete(hindiFont);
        var runner = new CapturingThumbnailTextProcessRunner();
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot }, englishFont, hindiFont, runner);

        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("hi"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir,
            IsShortForm = true
        }, CancellationToken.None));

        Assert.Contains("Thumbnail font not found:", exception.Message);
        Assert.Contains("NotoSansDevanagari-Bold.ttf", exception.Message);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-font-report.json")));
        Assert.Equal("hi", report.RootElement.GetProperty("language").GetString());
        Assert.False(report.RootElement.GetProperty("fontExists").GetBoolean());
    }

    [Fact]
    public async Task LocalAssetCollage_PrefersTransparentHeroPngOverOtherAssets()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        var transparentPath = Path.Combine(assetRoot, "jupiter", "hero-transparent.png");
        await WriteCuratedAssetAsync(transparentPath, Color.Orange);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "jupiter", "hero.png"), Color.Brown);
        await WriteLegacyAssetAsync(Path.Combine(assetRoot, "jupiter", "jupiter-gsfc.jpg"), Color.Brown);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        var selected = Assert.Single(plan.CelestialSelection!.AssetSources.Where(a => a.Category == "jupiter"));
        Assert.Equal(transparentPath, selected.LocalPath);
        Assert.Equal("hero-transparent.png", Path.GetFileName(selected.LocalPath));
        Assert.Equal("AssetPack", selected.Source);
        Assert.Equal(transparentPath, plan.SelectedVisualPath);
        Assert.True(File.Exists(plan.ThumbnailPath));
        using var thumbnail = await Image.LoadAsync<Rgba32>(plan.ThumbnailPath!);
        Assert.Equal(1280, thumbnail.Width);
        Assert.Equal(720, thumbnail.Height);
    }

    [Fact]
    public async Task LocalAssetCollage_WritesTransparentAssetSelectionDiagnostics()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        var transparentPath = Path.Combine(assetRoot, "jupiter", "hero-transparent.png");
        await WriteCuratedAssetAsync(transparentPath, Color.Orange);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "jupiter", "hero.png"), Color.Brown);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "venus", "hero-transparent.png"), Color.Gold);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot, PreferredAssetFileNames = ["hero.png"] });

        await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        using var selection = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-selection.json")));
        Assert.Equal("hero-transparent.png", selection.RootElement.GetProperty("selectedAssetFileName").GetString());
        Assert.Equal("AssetPack", selection.RootElement.GetProperty("selectedAssetSource").GetString());
        Assert.True(selection.RootElement.GetProperty("transparentAssetUsed").GetBoolean());
        Assert.Equal("LandscapeHeroRightTextLeft", selection.RootElement.GetProperty("layoutUsed").GetString());
        Assert.InRange(selection.RootElement.GetProperty("heroObjectScale").GetDouble(), 0.38, 0.55);
    }

    [Fact]
    public async Task LocalAssetCollage_RemovesCardStyleAndReportsCinematicObjectAnalysis()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        foreach (var key in new[] { "jupiter", "venus", "mars" })
            await WriteCuratedAssetAsync(Path.Combine(assetRoot, key, "hero-transparent.png"), Color.White);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot, MaxSupportObjectsLong = 2 });

        await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-analysis-report.json")));
        Assert.True(report.RootElement.GetProperty("cardStyleRemoved").GetBoolean());
        Assert.Equal(3, report.RootElement.GetProperty("objectCount").GetInt32());
        Assert.Empty(report.RootElement.GetProperty("layoutWarnings").EnumerateArray());
        Assert.True(report.RootElement.GetProperty("transparentAssetsUsed").GetInt32() >= 1);
    }

    [Fact]
    public async Task LocalAssetCollage_UsesPortraitLayoutAndCapsSupportObjectForShorts()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        foreach (var key in new[] { "jupiter", "venus", "mars", "saturn" })
            await WriteCuratedAssetAsync(Path.Combine(assetRoot, key, "hero-transparent.png"), Color.White);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot, MaxSupportObjectsShort = 1 });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir,
            IsShortForm = true
        }, CancellationToken.None);

        Assert.Single(plan.CelestialSelection!.SupportObjects);
        using var selection = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-selection.json")));
        Assert.Equal("PortraitObjectUpperTextLowerThird", selection.RootElement.GetProperty("layoutUsed").GetString());
        Assert.InRange(selection.RootElement.GetProperty("heroObjectScale").GetDouble(), 0.38, 0.42);
        Assert.Single(selection.RootElement.GetProperty("supportObjectScales").EnumerateArray());
    }

    [Fact]
    public async Task LocalAssetCollage_PenalizesDeepSpaceHeroAndReportsCinematicCleanupMetrics()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "jupiter", "hero-transparent.png"), Color.Orange);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "andromeda-galaxy", "hero-transparent.png"), Color.DarkBlue, 1600, 900);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero-transparent.png"), Color.Navy, 1600, 900);
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot, MaxSupportObjectsLong = 2, DeepSpaceHeroPenalty = 0.45 });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildPlanetAndDeepSpaceContext(),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        Assert.Equal("Jupiter", plan.CelestialSelection?.HeroObject);
        Assert.DoesNotContain(plan.CelestialSelection!.SupportObjects, s => s.Contains("Andromeda", StringComparison.OrdinalIgnoreCase) || s.Contains("Milky", StringComparison.OrdinalIgnoreCase));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-cinematic-report.json")));
        Assert.True(report.RootElement.GetProperty("deepSpacePenaltyApplied").GetBoolean());
        Assert.True(report.RootElement.GetProperty("glowIntensity").GetDouble() <= 0.13);
        Assert.True(report.RootElement.GetProperty("foregroundObjectAreaPercent").GetDouble() <= 30);
        Assert.False(report.RootElement.GetProperty("overlapPenaltyApplied").GetBoolean());
        Assert.True(report.RootElement.GetProperty("compositionBalanceScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("depthScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("atmosphericBlendScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("negativeSpaceScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("heroIsolationScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("cinematicRealismScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("organicAtmosphereScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("naturalLightingScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("visualArtifactPenalty").GetDouble() >= 0);
        Assert.True(report.RootElement.GetProperty("compositingVisibilityPenalty").GetDouble() >= 0);
        Assert.True(report.RootElement.GetProperty("cinematicSubtletyScore").GetDouble() > 0);
        Assert.Equal("Premium Documentary", report.RootElement.GetProperty("visualPreset").GetString());
        Assert.Contains(report.RootElement.GetProperty("candidateScores").EnumerateArray(), score => score.GetProperty("objectKey").GetString() == "andromeda-galaxy" && score.GetProperty("DeepSpacePenalty").GetDouble() < 0);
    }

    [Fact]
    public async Task LocalAssetCollage_FallsBackToHeroPng_WhenTransparentHeroMissing()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        var heroPath = Path.Combine(assetRoot, "jupiter", "hero.png");
        await WriteCuratedAssetAsync(heroPath, Color.Orange);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}")
        }, CancellationToken.None);

        var selected = Assert.Single(plan.CelestialSelection!.AssetSources.Where(a => a.Category == "jupiter"));
        Assert.Equal(heroPath, selected.LocalPath);
        Assert.Equal("hero.png", Path.GetFileName(selected.LocalPath));
        Assert.True(File.Exists(plan.ThumbnailPath));
    }

    [Fact]
    public async Task LocalAssetCollage_PrefersExtractedHeroPngOverLegacyJpg()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "jupiter", "hero.png"), Color.Orange);
        await WriteLegacyAssetAsync(Path.Combine(assetRoot, "jupiter", "jupiter-gsfc.jpg"), Color.Brown);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        var selected = Assert.Single(plan.CelestialSelection!.AssetSources.Where(a => a.Category == "jupiter"));
        Assert.Equal("hero.png", Path.GetFileName(selected.LocalPath));
        Assert.Equal("AssetPack", selected.Source);
        var report = await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-analysis-report.json"));
        Assert.Contains("\"selectedAssetFileName\": \"hero.png\"", report);
        Assert.Contains("\"selectedAssetSource\": \"AssetPack\"", report);
        Assert.Contains("\"oldAssetIgnoredBecauseHeroExists\": true", report);
    }

    [Fact]
    public async Task LocalAssetCollage_UsesHeroPngAsSelectedVisual()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        var heroPath = Path.Combine(assetRoot, "jupiter", "hero.png");
        await WriteCuratedAssetAsync(heroPath, Color.Orange);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}")
        }, CancellationToken.None);

        Assert.Equal(heroPath, plan.SelectedVisualPath);
        Assert.Equal("LocalAssetCollage", plan.Mode);
    }

    [Fact]
    public async Task LocalAssetCollage_FallsBackToLegacyJpg_WhenHeroPngMissing()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        var legacyPath = Path.Combine(assetRoot, "jupiter", "jupiter-gsfc.jpg");
        await WriteLegacyAssetAsync(legacyPath, Color.Brown);
        await WriteCuratedAssetAsync(Path.Combine(assetRoot, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}")
        }, CancellationToken.None);

        var selected = Assert.Single(plan.CelestialSelection!.AssetSources.Where(a => a.Category == "jupiter"));
        Assert.Equal(legacyPath, selected.LocalPath);
        Assert.Equal("LegacyJpgAsset", selected.Source);
        Assert.True(File.Exists(plan.ThumbnailPath));
    }

    [Fact]
    public async Task LocalAssetCollage_Succeeds_WhenExtractionHasNotRun()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        var stellariumFrame = Path.Combine(Path.GetTempPath(), $"stellarium-{Guid.NewGuid():N}.jpg");
        await WriteLegacyAssetAsync(stellariumFrame, Color.DarkBlue, 1280, 720);
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [stellariumFrame],
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}")
        }, CancellationToken.None);

        Assert.Equal("LocalAssetCollage", plan.Mode);
        Assert.True(plan.FallbackUsed);
        Assert.True(File.Exists(plan.ThumbnailPath));
    }


    [Fact]
    public async Task LocalAssetCollage_CinematicIntelligence_SelectsHeroModeAndDiagnostics()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        foreach (var key in new[] { "jupiter", "neptune", "milky-way" })
            await WriteCuratedAssetAsync(Path.Combine(assetRoot, key, "hero-transparent.png"), key == "jupiter" ? Color.Orange : Color.Blue);
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot, MaxSupportObjectsLong = 2 });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildJupiterNeptuneContext(),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        Assert.Equal("Jupiter", plan.CelestialSelection?.HeroObject);
        foreach (var generic in new[] { "TONIGHT'S SKY", "LOOK W TONIGHT", "PLANETS VISIBLE" })
            Assert.NotEqual(generic, plan.PrimaryThumbnailText, StringComparer.OrdinalIgnoreCase);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-cinematic-report.json")));
        Assert.Equal("EpicPlanetFocus", report.RootElement.GetProperty("cinematicMode").GetString());
        Assert.Equal("Jupiter", report.RootElement.GetProperty("heroObject").GetString());
        Assert.True(report.RootElement.GetProperty("compositionScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("readabilityScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("clickabilityScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("cinematicRealismScore").GetDouble() > 0);
    }

    [Fact]
    public async Task LocalAssetCollage_CinematicIntelligence_DetectsConjunctionAndSafeTextBounds()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        foreach (var key in new[] { "moon", "venus", "milky-way" })
            await WriteCuratedAssetAsync(Path.Combine(assetRoot, key, "hero-transparent.png"), key == "moon" ? Color.White : Color.Gold);
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot, MaxSupportObjectsLong = 4 });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildConjunctionContext(),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        Assert.True(plan.CelestialSelection!.SupportObjects.Count <= 4);
        Assert.True(plan.PrimaryThumbnailText.Length <= 28);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-cinematic-report.json")));
        Assert.Equal("ConjunctionMode", report.RootElement.GetProperty("cinematicMode").GetString());
        Assert.Contains("Near", report.RootElement.GetProperty("hookText").GetString(), StringComparison.OrdinalIgnoreCase);
        var bounds = report.RootElement.GetProperty("textBounds");
        Assert.True(bounds.GetProperty("x").GetDouble() >= 0);
        Assert.True(bounds.GetProperty("y").GetDouble() >= 0);
        Assert.True(bounds.GetProperty("x").GetDouble() + bounds.GetProperty("width").GetDouble() <= 1280);
        Assert.True(bounds.GetProperty("y").GetDouble() + bounds.GetProperty("height").GetDouble() <= 720);
    }

    [Fact]
    public async Task LocalAssetCollage_CinematicIntelligence_EventModesAndDeterministicReport()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        foreach (var key in new[] { "meteor-shower", "jupiter", "venus", "mars", "milky-way" })
            await WriteCuratedAssetAsync(Path.Combine(assetRoot, key, "hero-transparent.png"), Color.White);
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot, MaxSupportObjectsLong = 2 });
        var outputA = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var outputB = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var requestContext = BuildMeteorContext();

        var first = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.SpecialEventGuide,
            Context = requestContext,
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputA
        }, CancellationToken.None);
        await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.SpecialEventGuide,
            Context = requestContext,
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputB
        }, CancellationToken.None);

        Assert.Equal("Meteor Shower Peaks", first.PrimaryThumbnailText);
        Assert.True(first.CelestialSelection!.SupportObjects.Count <= 2);
        var reportA = await File.ReadAllTextAsync(Path.Combine(outputA, "thumbnails", "thumbnail-cinematic-report.json"));
        var reportB = await File.ReadAllTextAsync(Path.Combine(outputB, "thumbnails", "thumbnail-cinematic-report.json"));
        using var parsed = JsonDocument.Parse(reportA);
        Assert.Equal("MeteorShowerMode", parsed.RootElement.GetProperty("cinematicMode").GetString());
        Assert.Equal(reportA, reportB);
    }


    [Fact]
    public async Task LocalAssetCollage_CinematicAtmosphereEngine_RendersLayersAndReportsMetrics()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        foreach (var key in new[] { "jupiter", "venus", "mars", "milky-way" })
            await WriteCuratedAssetAsync(Path.Combine(assetRoot, key, "hero-transparent.png"), key == "jupiter" ? Color.Orange : Color.White);
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var service = CreateLocalAssetService(new ThumbnailOptions
        {
            AssetRootPath = assetRoot,
            MaxSupportObjectsLong = 2,
            Atmosphere = new ThumbnailAtmosphereOptions
            {
                ProceduralShapeOpacity = 0.06,
                ProceduralShapeBlur = 120,
                ProceduralShapeContrast = 0.35
            }
        });

        var plan = await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir
        }, CancellationToken.None);

        Assert.True(File.Exists(plan.ThumbnailPath));
        Assert.True(plan.CelestialSelection!.SupportObjects.Count <= 2);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-cinematic-report.json")));
        Assert.True(report.RootElement.GetProperty("atmosphereDepthScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("fogBlendScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("edgeIntegrationScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("proceduralArtifactPenalty").GetDouble() <= 0.35);
        Assert.True(report.RootElement.GetProperty("cinematicSoftnessScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("atmosphericRealismScore").GetDouble() > 0);
        Assert.True(report.RootElement.GetProperty("readabilityScore").GetDouble() >= 0.65);
    }

    [Fact]
    public async Task LocalAssetCollage_PortraitAtmosphere_KeepsTextReadableAndBalanced()
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), $"celestial-assets-{Guid.NewGuid():N}");
        foreach (var key in new[] { "jupiter", "venus", "mars", "saturn", "milky-way" })
            await WriteCuratedAssetAsync(Path.Combine(assetRoot, key, "hero-transparent.png"), Color.White);
        var outputDir = Path.Combine(Path.GetTempPath(), $"local-thumb-{Guid.NewGuid():N}");
        var service = CreateLocalAssetService(new ThumbnailOptions { AssetRootPath = assetRoot, MaxSupportObjectsShort = 1 });

        await service.GenerateAsync(new ThumbnailGenerationRequest
        {
            ContentType = ContentType.DailySkyGuide,
            Context = BuildVisiblePlanetContext("en"),
            Metadata = new OptimizedVideoMetadata(),
            AvailableVisuals = [],
            OutputDirectory = outputDir,
            IsShortForm = true
        }, CancellationToken.None);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-cinematic-report.json")));
        Assert.Equal("PortraitObjectUpperTextLowerThird", report.RootElement.GetProperty("layoutUsed").GetString());
        Assert.Single(report.RootElement.GetProperty("supportObjects").EnumerateArray());
        Assert.True(report.RootElement.GetProperty("readabilityScore").GetDouble() >= 0.65);
        var textBounds = report.RootElement.GetProperty("textBounds");
        Assert.True(textBounds.GetProperty("y").GetDouble() < 1920 * 0.66);
        Assert.True(textBounds.GetProperty("y").GetDouble() + textBounds.GetProperty("height").GetDouble() < 1920 * 0.86);
        Assert.True(report.RootElement.GetProperty("fogBlendScore").GetDouble() > 0);
    }

    private static ThumbnailGenerationService CreateProductionService(ThumbnailOptions options)
        => new(new ThumbnailStrategyService(), new ThumbnailScoringService(), new ThumbnailHookService(), Options.Create(options), NullLogger<ThumbnailGenerationService>.Instance);


    private static LocalAssetCollageThumbnailService CreateLocalAssetService(ThumbnailOptions options)
        => new(new ThumbnailStrategyService(), Options.Create(options), NullLogger<LocalAssetCollageThumbnailService>.Instance);

    private static LocalAssetCollageThumbnailService CreateLocalAssetService(ThumbnailOptions options, string englishFont, string hindiFont, IProcessRunner processRunner)
        => new(
            new ThumbnailStrategyService(),
            Options.Create(options),
            NullLogger<LocalAssetCollageThumbnailService>.Instance,
            Options.Create(new ThumbnailFontOptions { DefaultEnglishFont = englishFont, HindiFont = hindiFont }),
            Options.Create(new RenderingOptions { FfmpegPath = "ffmpeg" }),
            processRunner);

    private static AstronomyContext BuildVisiblePlanetContext(string language)
        => new()
        {
            Date = new DateOnly(2026, 5, 14),
            LocationName = "Udaipur, India",
            Localization = new LocalizationContext(language, string.Empty, language, false),
            SceneObservationContexts =
            [
                new SceneObservationContext { SceneId = "jupiter", ObjectName = "Jupiter", ObjectType = "Planet", IsVisible = true, AltitudeDegrees = 70, DirectionLabel = "South" },
                new SceneObservationContext { SceneId = "venus", ObjectName = "Venus", ObjectType = "Planet", IsVisible = true, AltitudeDegrees = 35, DirectionLabel = "West" },
                new SceneObservationContext { SceneId = "mars", ObjectName = "Mars", ObjectType = "Planet", IsVisible = true, AltitudeDegrees = 25, DirectionLabel = "East" },
                new SceneObservationContext { SceneId = "saturn", ObjectName = "Saturn", ObjectType = "Planet", IsVisible = true, AltitudeDegrees = 18, DirectionLabel = "East" }
            ]
        };


    private static AstronomyContext BuildJupiterNeptuneContext()
        => new()
        {
            Date = new DateOnly(2026, 5, 14),
            LocationName = "Udaipur, India",
            Localization = LocalizationContext.English,
            SceneObservationContexts =
            [
                new SceneObservationContext { SceneId = "jupiter", ObjectName = "Jupiter", ObjectType = "Planet", IsVisible = true, AltitudeDegrees = 62, DirectionLabel = "South" },
                new SceneObservationContext { SceneId = "neptune", ObjectName = "Neptune", ObjectType = "Planet", IsVisible = true, AltitudeDegrees = 72, DirectionLabel = "East" }
            ]
        };

    private static AstronomyContext BuildPlanetAndDeepSpaceContext()
        => new()
        {
            Date = new DateOnly(2026, 5, 14),
            LocationName = "Udaipur, India",
            Localization = LocalizationContext.English,
            SceneObservationContexts =
            [
                new SceneObservationContext { SceneId = "jupiter", ObjectName = "Jupiter", ObjectType = "Planet", IsVisible = true, AltitudeDegrees = 48, DirectionLabel = "South" },
                new SceneObservationContext { SceneId = "andromeda", ObjectName = "Andromeda Galaxy", ObjectType = "Galaxy", IsVisible = true, AltitudeDegrees = 82, DirectionLabel = "North" },
                new SceneObservationContext { SceneId = "milky-way", ObjectName = "Milky Way", ObjectType = "DeepSky", IsVisible = true, AltitudeDegrees = 88, DirectionLabel = "Overhead" }
            ]
        };

    private static AstronomyContext BuildConjunctionContext()
        => new()
        {
            Date = new DateOnly(2026, 5, 14),
            LocationName = "Udaipur, India",
            Localization = LocalizationContext.English,
            SceneObservationContexts =
            [
                new SceneObservationContext { SceneId = "moon", ObjectName = "Moon", ObjectType = "Moon", IsVisible = true, AltitudeDegrees = 50, VisibilityReason = "Moon near Venus after sunset" },
                new SceneObservationContext { SceneId = "venus", ObjectName = "Venus", ObjectType = "Planet", IsVisible = true, AltitudeDegrees = 46, VisibilityReason = "Close conjunction with Moon" }
            ],
            Events = [new AstronomyEventModel { Category = "Conjunction", ObjectName = "Venus", Details = "Moon near Venus", Score = 0.96 }]
        };

    private static AstronomyContext BuildMeteorContext()
        => new()
        {
            Date = new DateOnly(2026, 5, 14),
            LocationName = "Udaipur, India",
            Localization = LocalizationContext.English,
            SpecialEvent = new SpecialEventContext { EventType = "Meteor shower", EventTitle = "Meteor shower peaks tonight", ContentOpportunityScore = 1 },
            Events = [new AstronomyEventModel { Category = "Meteor Shower", ObjectName = "Meteor Shower", Details = "Meteor shower peaks tonight", Score = 0.99 }],
            SceneObservationContexts =
            [
                new SceneObservationContext { SceneId = "jupiter", ObjectName = "Jupiter", ObjectType = "Planet", IsVisible = true, AltitudeDegrees = 65 },
                new SceneObservationContext { SceneId = "venus", ObjectName = "Venus", ObjectType = "Planet", IsVisible = true, AltitudeDegrees = 42 },
                new SceneObservationContext { SceneId = "mars", ObjectName = "Mars", ObjectType = "Planet", IsVisible = true, AltitudeDegrees = 38 }
            ]
        };

    private static async Task WriteCuratedAssetAsync(string path, Color color, int width = 1000, int height = 1000)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Image<Rgba32>(width, height, Color.Transparent);
        image.Mutate(ctx => ctx.Fill(color, new SixLabors.ImageSharp.Drawing.EllipsePolygon(width / 2f, height / 2f, Math.Min(width, height) * 0.38f)));
        await image.SaveAsPngAsync(path);
    }

    private static async Task WriteLegacyAssetAsync(string path, Color color, int width = 1000, int height = 1000)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Image<Rgba32>(width, height, color);
        await image.SaveAsJpegAsync(path);
    }



    private static async Task<(string AssetRoot, string EnglishFont, string HindiFont)> CreateThumbnailTextRenderAssetsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"thumbnail-text-assets-{Guid.NewGuid():N}");
        await WriteCuratedAssetAsync(Path.Combine(root, "jupiter", "hero.png"), Color.Orange);
        await WriteCuratedAssetAsync(Path.Combine(root, "milky-way", "hero.png"), Color.Navy, 1600, 900);
        var fontRoot = Path.Combine(root, "fonts");
        Directory.CreateDirectory(fontRoot);
        var englishFont = Path.Combine(fontRoot, "Montserrat-ExtraBold.ttf");
        var hindiFont = Path.Combine(fontRoot, "NotoSansDevanagari-Bold.ttf");
        await File.WriteAllTextAsync(englishFont, "test-font");
        await File.WriteAllTextAsync(hindiFont, "test-font");
        return (root, englishFont, hindiFont);
    }

    private sealed class CapturingThumbnailTextProcessRunner : IProcessRunner
    {
        public string LastArguments { get; private set; } = string.Empty;
        public string LastTextFileContents { get; private set; } = string.Empty;
        public string? LastTextFilePath { get; private set; }
        public byte[]? LastTextFileBytes { get; private set; }

        public async Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            LastArguments = arguments;
            LastTextFilePath = ExtractDrawTextPath(arguments, "textfile='");
            LastTextFileBytes = await File.ReadAllBytesAsync(LastTextFilePath, cancellationToken);
            LastTextFileContents = await File.ReadAllTextAsync(LastTextFilePath, cancellationToken);

            var paths = ExtractQuotedPaths(arguments);
            File.Copy(paths[0], paths[^1], overwrite: true);
            var now = DateTimeOffset.UtcNow;
            return new ProcessExecutionResult(0, string.Empty, string.Empty, now, now, fileName, arguments, string.Empty, false);
        }

        private static string ExtractDrawTextPath(string arguments, string prefix)
        {
            var start = arguments.IndexOf(prefix, StringComparison.Ordinal);
            Assert.True(start >= 0);
            start += prefix.Length;
            var end = arguments.IndexOf('\'', start);
            Assert.True(end > start);
            return arguments[start..end].Replace('/', Path.DirectorySeparatorChar);
        }

        private static List<string> ExtractQuotedPaths(string arguments)
        {
            var paths = new List<string>();
            var index = 0;
            while (index < arguments.Length)
            {
                var start = arguments.IndexOf('\"', index);
                if (start < 0) break;
                var end = arguments.IndexOf('\"', start + 1);
                if (end < 0) break;
                var value = arguments[(start + 1)..end];
                if (!value.Contains("drawtext=", StringComparison.Ordinal))
                    paths.Add(value.Replace('/', Path.DirectorySeparatorChar));
                index = end + 1;
            }
            return paths;
        }
    }

    private static AstronomyContext BuildContext(string language)
        => new()
        {
            Date = new DateOnly(2026, 5, 14),
            LocationName = "Udaipur, India",
            Localization = new LocalizationContext(language, string.Empty, language, false),
            SceneObservationContexts =
            [
                new SceneObservationContext
                {
                    SceneId = "moon",
                    ObjectName = "Moon",
                    ObjectType = "Moon",
                    AltitudeDegrees = 58,
                    DirectionLabel = "West",
                    LocationName = "Udaipur, India"
                }
            ]
        };

    private static async Task WriteMoonImageAsync(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(10, 20, 55));
        image.Mutate(ctx => ctx.Fill(Color.White, new SixLabors.ImageSharp.Drawing.EllipsePolygon(width * 0.58f, height * 0.38f, width * 0.08f)));
        await image.SaveAsJpegAsync(path);
    }

}
