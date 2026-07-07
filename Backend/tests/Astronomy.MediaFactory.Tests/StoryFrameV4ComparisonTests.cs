using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Astronomy.MediaFactory.Tests;

public sealed class StoryFrameV4ComparisonTests
{
    private readonly NarrativeCompositionEngine narrativeEngine = new();

    [Fact]
    public async Task Flag_false_generates_no_comparison_images()
    {
        var folder = TempFolder();
        try
        {
            var planner = new LongStoryFramePlanner(Options.Create(new VisualIntelligenceOptions { UseStoryFrameV4Comparison = false }), new FakeGenerator());
            var report = await planner.GenerateV4ComparisonAsync(narrativeEngine.ComposeLong(Story()), folder);

            Assert.Null(report);
            Assert.False(Directory.Exists(Path.Combine(folder, "long-story-frames")));
            Assert.False(File.Exists(Path.Combine(folder, "long-story-frames", "frame01-hook.png")));
            Assert.False(File.Exists(Path.Combine(folder, "long-story-frames", "comparison", "LongStoryFrameComparison.json")));
        }
        finally { Cleanup(folder); }
    }

    [Fact]
    public async Task Flag_true_generates_long_and_short_comparison_images_and_reports_without_production_changes()
    {
        var folder = TempFolder();
        var sceneAssetsV3 = Path.Combine(folder, "scene-assets-v3");
        var productionScene = Path.Combine(sceneAssetsV3, "scene01.png");
        Directory.CreateDirectory(sceneAssetsV3);
        await File.WriteAllTextAsync(productionScene, "production-scene-asset");
        var before = await File.ReadAllTextAsync(productionScene);
        try
        {
            var options = Options.Create(new VisualIntelligenceOptions { UseStoryFrameV4Comparison = true });
            var longPlanner = new LongStoryFramePlanner(options, new FakeGenerator());
            var shortPlanner = new ShortStoryFramePlanner(options, new FakeGenerator());

            var longReport = await longPlanner.GenerateV4ComparisonAsync(narrativeEngine.ComposeLong(Story()), folder);
            var shortReport = await shortPlanner.GenerateV4ComparisonAsync(narrativeEngine.ComposeShort(Story()), folder);

            Assert.NotNull(longReport);
            Assert.NotNull(shortReport);
            Assert.Equal(9, longReport!.ExpectedFrameCount);
            Assert.Equal(9, longReport.GeneratedFrameCount);
            Assert.True(longReport.ProductionSceneAssetsUnchanged);
            Assert.Equal("16:9", longReport.AspectRatio);
            Assert.Equal("ManualReviewRequired", longReport.Recommendation);
            Assert.Equal(5, shortReport!.ExpectedFrameCount);
            Assert.Equal(5, shortReport.GeneratedFrameCount);
            Assert.True(shortReport.ProductionSceneAssetsUnchanged);
            Assert.Equal("9:16", shortReport.AspectRatio);
            Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "frame01-hook.png")));
            Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "frame09-call-to-action.png")));
            Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "comparison", "LongStoryFrameComparison.json")));
            Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "frame01-hook.png")));
            Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "frame05-call-to-action.png")));
            Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "comparison", "ShortStoryFrameComparison.json")));

            using (var longImage = await Image.LoadAsync(Path.Combine(folder, "long-story-frames", "frame01-hook.png")) )
                Assert.Equal(16d / 9d, longImage.Width / (double)longImage.Height, precision: 2);
            using (var shortImage = await Image.LoadAsync(Path.Combine(folder, "short-story-frames", "frame01-hook.png")) )
                Assert.Equal(9d / 16d, shortImage.Width / (double)shortImage.Height, precision: 2);
            Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "diagnostics", "LongStoryFrameVisualReview.json")));
            Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "diagnostics", "ShortStoryFrameVisualReview.json")));
            Assert.Equal(before, await File.ReadAllTextAsync(productionScene));
        }
        finally { Cleanup(folder); }
    }

    [Fact]
    public async Task Frame_generation_failures_are_non_blocking_and_reported()
    {
        var folder = TempFolder();
        try
        {
            var planner = new ShortStoryFramePlanner(Options.Create(new VisualIntelligenceOptions { UseStoryFrameV4Comparison = true }), new FakeGenerator(failFrame: 3));

            var report = await planner.GenerateV4ComparisonAsync(narrativeEngine.ComposeShort(Story()), folder);

            Assert.NotNull(report);
            Assert.Equal(5, report!.ExpectedFrameCount);
            Assert.Equal(4, report.GeneratedFrameCount);
            Assert.Contains("frame03-explanation.png", report.FailedFrames);
            Assert.Contains(report.Warnings, warning => warning.Contains("Frame 3 failed non-blocking"));
            Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "comparison", "ShortStoryFrameComparison.json")));
        }
        finally { Cleanup(folder); }
    }


    [Fact]
    public async Task Provider_unavailable_is_non_blocking_and_writes_diagnostics()
    {
        var folder = TempFolder();
        try
        {
            var planner = new LongStoryFramePlanner(Options.Create(new VisualIntelligenceOptions { UseStoryFrameV4Comparison = true }), new FakeGenerator(configured: false));

            var report = await planner.GenerateV4ComparisonAsync(narrativeEngine.ComposeLong(Story()), folder);

            Assert.NotNull(report);
            Assert.Equal(9, report!.ExpectedFrameCount);
            Assert.Equal(0, report.GeneratedFrameCount);
            Assert.Equal(9, report.FailedFrames.Count);
            Assert.Contains(report.Warnings, warning => warning.Contains("provider unavailable", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "diagnostics", "StoryFrameGeneratorDiagnostics.json")));
        }
        finally { Cleanup(folder); }
    }

    private sealed class FakeGenerator(int failFrame = 0, bool configured = true) : IAICinematicImageGenerator
    {
        public bool IsConfigured => configured;
        public string DeploymentName => "fake-azure-image";
        public async Task<AICinematicProviderResult> GenerateAsync(AICinematicAssetRequest request, CancellationToken cancellationToken)
        {
            if (request.AssetId.EndsWith($"-{failFrame:00}", StringComparison.Ordinal)) throw new InvalidOperationException("planned failure");
            Directory.CreateDirectory(Path.GetDirectoryName(request.PlannedImagePath)!);
            using var image = new Image<Rgba32>(request.TargetWidth, request.TargetHeight, new Rgba32(4, 8, 20));
            await image.SaveAsPngAsync(request.PlannedImagePath, cancellationToken);
            return new AICinematicProviderResult("Generated", request.PlannedImagePath, true, []);
        }
    }

    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "story-frame-v4-comparison-" + Guid.NewGuid().ToString("N"));
    private static void Cleanup(string folder) { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }

    private static VisualStory Story() => new()
    {
        StoryId = "story-frame-v4-comparison-test",
        StoryTitle = "Venus and Jupiter close approach",
        ViewerQuestion = "Why do these planets look close?",
        PrimaryStory = "Two bright planets appear unusually close together.",
        ViewerTakeaway = "This is an apparent conjunction.",
        EmotionalHook = "Wonder.",
        PrimaryVisualSubject = "Relationship",
        SecondaryVisualSubjects = ["Venus", "Jupiter"],
        VisualRelationship = "The apparent conjunction relationship is the subject; do not prioritize the largest planet.",
        RecommendedComposition = "Balanced pairing",
        RecommendedViewerFocus = "Relationship first",
        DocumentaryTone = "Documentary",
        EnvironmentRecommendation = "Observed sky realism.",
        LightingRecommendation = "Natural twilight documentary lighting.",
        RecommendedNegativeSpace = "Shared negative space around both planets.",
        RecommendedOverlayZones = ["lower third"],
        RecommendedPlatformVariations = new Dictionary<string, VisualStoryPlatformVariation> { ["landscape"] = new() { Name = "Landscape Story", Recommendation = "wide documentary composition" } },
        StoryConfidence = .91,
        CreativeKnowledgeVersion = CreativeKnowledgeLibrary.Version,
        EditorialReasoningVersion = "4.3A"
    };
}
