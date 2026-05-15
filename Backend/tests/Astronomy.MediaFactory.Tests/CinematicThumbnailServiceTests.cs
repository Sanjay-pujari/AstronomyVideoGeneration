using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using System.Reflection;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class CinematicThumbnailServiceTests
{
    [Fact]
    public async Task CinematicThumbnail_GeneratesLongAndShortOutputsInThumbnailsDirectory()
    {
        var outputDir = CreateTempDirectory();
        var source = Path.Combine(outputDir, "source.jpg");
        await WriteAstronomyImageAsync(source, 1280, 720);
        var service = CreateService(new ThumbnailOptions());

        var longPlan = await service.GenerateAsync(BuildRequest(outputDir, source, "en", isShort: false), CancellationToken.None);
        var shortOutput = Path.Combine(outputDir, "shorts");
        var shortPlan = await service.GenerateAsync(BuildRequest(shortOutput, source, "en", isShort: true), CancellationToken.None);

        Assert.EndsWith(Path.Combine("thumbnails", "thumbnail-long.jpg"), longPlan.ThumbnailPath);
        Assert.EndsWith(Path.Combine("thumbnails", "thumbnail-short.jpg"), shortPlan.ThumbnailPath);
        using var longImage = await Image.LoadAsync(longPlan.ThumbnailPath!);
        using var shortImage = await Image.LoadAsync(shortPlan.ThumbnailPath!);
        Assert.Equal(1280, longImage.Width);
        Assert.Equal(720, longImage.Height);
        Assert.Equal(1080, shortImage.Width);
        Assert.Equal(1920, shortImage.Height);
        Assert.True(new FileInfo(longPlan.ThumbnailPath!).Length <= 2 * 1024 * 1024);
        Assert.True(File.Exists(Path.Combine(outputDir, "thumbnails", "thumbnail-selection.json")));
        Assert.True(File.Exists(Path.Combine(outputDir, "thumbnails", "thumbnail-analysis-report.json")));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void ThumbnailHook_IsLocalizedAndMaxFiveWords(string language)
    {
        var service = new ThumbnailHookService();
        var hook = service.GenerateHook(BuildRequest(".", "missing.jpg", language, isShort: false), 5);

        Assert.InRange(hook.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 2, 5);
        if (language == "hi")
            Assert.Contains("आज", hook);
        else
            Assert.Contains("Tonight", hook);
    }

    [Fact]
    public async Task ThumbnailCandidateSelector_RejectsBlackFrame()
    {
        var outputDir = CreateTempDirectory();
        var black = Path.Combine(outputDir, "black.jpg");
        using (var image = new Image<Rgba32>(1280, 720, Color.Black))
        {
            await image.SaveAsJpegAsync(black);
        }
        var selector = new ThumbnailCandidateSelector(new ThumbnailScoringService(), Options.Create(new ThumbnailOptions()));

        var selection = await selector.SelectAsync(BuildRequest(outputDir, black, "en", isShort: false), CancellationToken.None);

        Assert.Contains(selection.CandidateScores, x => x.IsRejected);
        Assert.True(selection.FallbackUsed);
    }

    [Fact]
    public async Task CinematicThumbnail_FallsBackToExtractedFrame_WhenCompositionFails()
    {
        var outputDir = CreateTempDirectory();
        var source = Path.Combine(outputDir, "source.jpg");
        await WriteAstronomyImageAsync(source, 1280, 720);
        var service = CreateService(new ThumbnailOptions(), compositionService: new ThrowingCompositionService());

        var plan = await service.GenerateAsync(BuildRequest(outputDir, source, "en", isShort: false), CancellationToken.None);

        Assert.Equal(source, plan.ThumbnailPath);
        var report = await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-analysis-report.json"));
        Assert.Contains("fallbackUsed", report);
        Assert.Contains("true", report);
    }


    [Fact]
    public async Task PublishingFlow_ResolvesCinematicShortThumbnailFromThumbnailsDirectory()
    {
        var outputDir = CreateTempDirectory();
        var shortsThumbs = Path.Combine(outputDir, "shorts", "thumbnails");
        Directory.CreateDirectory(shortsThumbs);
        var thumbnail = Path.Combine(shortsThumbs, "thumbnail-short.jpg");
        await WriteAstronomyImageAsync(thumbnail, 1080, 1920);

        var service = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ContentPublishService));
        var method = typeof(ContentPublishService).GetMethod("ResolveShortThumbnailPathAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var task = (Task<string>)method!.Invoke(service, [outputDir, CancellationToken.None])!;
        var resolved = await task;

        Assert.Equal(thumbnail, resolved);
    }

    private static CinematicThumbnailService CreateService(ThumbnailOptions options, IThumbnailCompositionService? compositionService = null)
        => new(
            new ThumbnailStrategyService(),
            new ThumbnailCandidateSelector(new ThumbnailScoringService(), Options.Create(options)),
            compositionService ?? new ThumbnailCompositionService(Options.Create(options)),
            new ThumbnailHookService(),
            CreateAiOptimizationService(),
            Options.Create(options),
            NullLogger<CinematicThumbnailService>.Instance);

    private static ThumbnailAiOptimizationService CreateAiOptimizationService()
    {
        var options = Options.Create(new ThumbnailAIOptimizationOptions());
        return new ThumbnailAiOptimizationService(new ThumbnailCtrScoringService(options), options);
    }

    private static ThumbnailGenerationRequest BuildRequest(string outputDir, string visualPath, string language, bool isShort)
        => new()
        {
            ContentType = ContentType.SpecialEventGuide,
            Context = new AstronomyContext
            {
                Date = new DateOnly(2026, 5, 15),
                LocationName = "Udaipur, India",
                Localization = new LocalizationContext(language, string.Empty, language, false),
                SceneObservationContexts =
                [
                    new SceneObservationContext
                    {
                        SceneId = "moon",
                        ObjectName = "Moon",
                        ObjectType = "Moon",
                        DirectionLabel = "West",
                        AltitudeDegrees = 45,
                        LocationName = "Udaipur, India"
                    }
                ]
            },
            Metadata = new OptimizedVideoMetadata { HookLine = language == "hi" ? "आज रात देखें" : "Look West Tonight" },
            AvailableVisuals = [visualPath],
            OutputDirectory = outputDir,
            IsShortForm = isShort,
            Scenes = [new RenderScene { SceneId = "moon", VisualPath = visualPath, DurationSeconds = 10 }]
        };

    private static async Task WriteAstronomyImageAsync(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(16, 28, 74));
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.White, new SixLabors.ImageSharp.Drawing.EllipsePolygon(width * 0.62f, height * 0.35f, width * 0.09f));
            ctx.Fill(Color.DeepSkyBlue.WithAlpha(0.55f), new RectangleF(0, height * 0.78f, width, height * 0.22f));
        });
        await image.SaveAsJpegAsync(path);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cinematic-thumb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ThrowingCompositionService : IThumbnailCompositionService
    {
        public Task<string> ComposeAsync(ThumbnailCompositionRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("composition failed");
    }
}
