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

    private static ThumbnailGenerationService CreateProductionService(ThumbnailOptions options)
        => new(new ThumbnailStrategyService(), new ThumbnailScoringService(), new ThumbnailHookService(), Options.Create(options), NullLogger<ThumbnailGenerationService>.Instance);

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
