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

public sealed class ThumbnailCinematicAiPhase3Tests
{
    [Fact]
    public async Task CinematicAi_DetectsFocalObjectAndWritesIntegritySafeReport()
    {
        var outputDir = CreateTempDirectory();
        var source = Path.Combine(outputDir, "moon.jpg");
        await WriteAstronomyImageAsync(source, 1280, 720, "moon");
        var composition = CreateCompositionService();
        var request = BuildRequest(outputDir, source, isShort: false, objectType: "Moon");

        await composition.ComposeAsync(new ThumbnailCompositionRequest
        {
            GenerationRequest = request,
            SelectedCandidate = BuildScore(source, sceneId: "moon"),
            HookText = "Big Moon Tonight",
            OutputPath = Path.Combine(outputDir, "thumbnails", "thumbnail-long.jpg")
        }, CancellationToken.None);

        var report = await ReadReportAsync(outputDir);
        Assert.Equal("moon", report.RootElement.GetProperty("dominantObject").GetString());
        Assert.Equal("warmGlow", report.RootElement.GetProperty("moodProfile").GetString());
        Assert.True(report.RootElement.GetProperty("enhancementApplied").GetBoolean());
        Assert.True(report.RootElement.GetProperty("scaleBoost").GetDouble() <= 1.35);
        Assert.True(report.RootElement.GetProperty("astronomyIntegrityValidation").GetProperty("noSyntheticObjectsAdded").GetBoolean());
        Assert.True(new FileInfo(Path.Combine(outputDir, "thumbnails", "thumbnail-long.jpg")).Length <= 2 * 1024 * 1024);
    }

    [Fact]
    public async Task CinematicAi_PortraitSafeCropPreservesReadableHook()
    {
        var outputDir = CreateTempDirectory();
        var source = Path.Combine(outputDir, "jupiter.jpg");
        await WriteAstronomyImageAsync(source, 1080, 1920, "planet");
        var composition = CreateCompositionService();
        var request = BuildRequest(outputDir, source, isShort: true, objectType: "Jupiter");

        await composition.ComposeAsync(new ThumbnailCompositionRequest
        {
            GenerationRequest = request,
            SelectedCandidate = BuildScore(source, sceneId: "moon", textSafe: 0.82),
            HookText = "Look West Tonight Now",
            OutputPath = Path.Combine(outputDir, "thumbnails", "thumbnail-short.jpg")
        }, CancellationToken.None);

        var report = await ReadReportAsync(outputDir);
        Assert.True(report.RootElement.GetProperty("portraitSafe").GetBoolean());
        Assert.Contains("portrait-safe", report.RootElement.GetProperty("cropStrategy").GetString());
        Assert.True(report.RootElement.GetProperty("readabilityScore").GetDouble() >= 0.65);
        using var image = await Image.LoadAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-short.jpg"));
        Assert.Equal(1080, image.Width);
        Assert.Equal(1920, image.Height);
    }

    [Fact]
    public async Task CinematicThumbnail_FallsBackToPhaseOneFrame_WhenIntegrityValidationFails()
    {
        var outputDir = CreateTempDirectory();
        var source = Path.Combine(outputDir, "empty-sky.jpg");
        await WriteAstronomyImageAsync(source, 1280, 720, "empty");
        var selector = new StubCandidateSelector(BuildScore(source, objectDetected: false));
        var service = new CinematicThumbnailService(
            new ThumbnailStrategyService(),
            selector,
            CreateCompositionService(),
            new ThumbnailHookService(),
            CreateAiOptimizationService(),
            Options.Create(new ThumbnailOptions()),
            NullLogger<CinematicThumbnailService>.Instance);

        var plan = await service.GenerateAsync(BuildRequest(outputDir, source, isShort: false, objectType: "Moon"), CancellationToken.None);

        Assert.Equal(source, plan.ThumbnailPath);
        var report = await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-analysis-report.json"));
        Assert.Contains("fallbackUsed", report);
        Assert.Contains("true", report);
    }

    [Fact]
    public void MoodGrading_SelectsExpectedProfilesForMajorObjectTypes()
    {
        var service = new ThumbnailMoodGradingService(Options.Create(new ThumbnailCinematicAIOptions()));

        Assert.Equal("warmGlow", service.SelectMood(new ThumbnailMoodGradingRequest { DominantObjectType = "moon" }).MoodProfile);
        Assert.Equal("deepSpace", service.SelectMood(new ThumbnailMoodGradingRequest { DominantObjectType = "deep sky object" }).MoodProfile);
        Assert.Equal("cinematicBlue", service.SelectMood(new ThumbnailMoodGradingRequest { DominantObjectType = "conjunction" }).MoodProfile);
        Assert.Equal("dramatic", service.SelectMood(new ThumbnailMoodGradingRequest { DominantObjectType = "meteor streak" }).MoodProfile);
    }

    [Fact]
    public async Task ExistingPublishingResolver_IsUnaffectedByCinematicAiThumbnailFiles()
    {
        var outputDir = CreateTempDirectory();
        var shortsThumbs = Path.Combine(outputDir, "shorts", "thumbnails");
        Directory.CreateDirectory(shortsThumbs);
        var thumbnail = Path.Combine(shortsThumbs, "thumbnail-short.jpg");
        await WriteAstronomyImageAsync(thumbnail, 1080, 1920, "moon");
        await File.WriteAllTextAsync(Path.Combine(shortsThumbs, "thumbnail-cinematic-ai-report.json"), "{}");

        var service = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Astronomy.MediaFactory.Publishing.ContentPublishService));
        var method = typeof(Astronomy.MediaFactory.Publishing.ContentPublishService).GetMethod("ResolveShortThumbnailPathAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        var task = (Task<string>)method!.Invoke(service, [outputDir, CancellationToken.None])!;
        Assert.Equal(thumbnail, await task);
    }

    private static ThumbnailCompositionService CreateCompositionService()
    {
        var cinematicOptions = Options.Create(new ThumbnailCinematicAIOptions());
        var mood = new ThumbnailMoodGradingService(cinematicOptions);
        var ai = new CinematicThumbnailAiService(mood, cinematicOptions);
        return new ThumbnailCompositionService(
            Options.Create(new ThumbnailOptions()),
            cinematicOptions,
            ai,
            new ThumbnailVisualHierarchyService(),
            mood);
    }

    private static ThumbnailAiOptimizationService CreateAiOptimizationService()
    {
        var options = Options.Create(new ThumbnailAIOptimizationOptions());
        return new ThumbnailAiOptimizationService(new ThumbnailCtrScoringService(options), options);
    }

    private static ThumbnailGenerationRequest BuildRequest(string outputDir, string visualPath, bool isShort, string objectType)
        => new()
        {
            ContentType = ContentType.SpecialEventGuide,
            Context = new AstronomyContext
            {
                Date = new DateOnly(2026, 5, 15),
                LocationName = "Udaipur, India",
                Localization = LocalizationContext.English,
                SpecialEvent = objectType.Contains("conjunction", StringComparison.OrdinalIgnoreCase)
                    ? new SpecialEventContext { EventType = "conjunction", EventTitle = "Moon Jupiter conjunction" }
                    : null,
                SceneObservationContexts =
                [
                    new SceneObservationContext
                    {
                        SceneId = "moon",
                        ObjectName = objectType,
                        ObjectType = objectType,
                        AltitudeDegrees = 45,
                        AzimuthDegrees = 260,
                        DirectionLabel = "West"
                    }
                ]
            },
            Metadata = new OptimizedVideoMetadata { PrimaryTitle = $"{objectType} Tonight", HookLine = "Look West Tonight" },
            AvailableVisuals = [visualPath],
            OutputDirectory = outputDir,
            IsShortForm = isShort,
            Scenes = [new RenderScene { SceneId = "moon", VisualPath = visualPath, ObjectName = objectType, ObjectType = objectType, DurationSeconds = 10 }]
        };

    private static ThumbnailCandidateScore BuildScore(string path, string sceneId = "moon", bool objectDetected = true, double textSafe = 0.74)
        => new()
        {
            Path = path,
            SceneId = sceneId,
            TimestampSeconds = 2,
            Score = objectDetected ? 0.82 : 0.15,
            Brightness = 0.48,
            BlackPixelPercentage = 0.18,
            Contrast = 0.72,
            ObjectDetected = objectDetected,
            ObjectVisibility = objectDetected ? 0.22 : 0,
            CelestialFocalSize = objectDetected ? 0.18 : 0,
            ColorRichness = 0.42,
            TextSafeCompositionArea = textSafe,
            Sharpness = 0.66
        };

    private static async Task<JsonDocument> ReadReportAsync(string outputDir)
        => JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-cinematic-ai-report.json")));

    private static async Task WriteAstronomyImageAsync(string path, int width, int height, string mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Image<Rgba32>(width, height, new Rgba32(12, 20, 50));
        image.Mutate(ctx =>
        {
            if (mode != "empty")
                ctx.Fill(Color.White, new SixLabors.ImageSharp.Drawing.EllipsePolygon(width * 0.62f, height * 0.35f, width * 0.065f));
            if (mode == "planet")
                ctx.Fill(Color.Orange.WithAlpha(0.86f), new SixLabors.ImageSharp.Drawing.EllipsePolygon(width * 0.38f, height * 0.42f, width * 0.035f));
            ctx.Fill(Color.DeepSkyBlue.WithAlpha(0.32f), new RectangleF(0, height * 0.80f, width, height * 0.20f));
        });
        await image.SaveAsJpegAsync(path);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cinematic-ai-phase3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubCandidateSelector : IThumbnailCandidateSelector
    {
        private readonly ThumbnailCandidateScore _score;
        public StubCandidateSelector(ThumbnailCandidateScore score) => _score = score;
        public Task<ThumbnailCandidateSelection> SelectAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ThumbnailCandidateSelection { SelectedCandidate = _score, CandidateScores = [_score] });
    }
}
