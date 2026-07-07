using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Microsoft.Extensions.Options;

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
            Assert.False(File.Exists(Path.Combine(folder, "long-story-frames", "comparison", "frame01-hook-v4.png")));
            Assert.False(File.Exists(Path.Combine(folder, "long-story-frames", "comparison", "LongStoryFrameComparison.json")));
        }
        finally { Cleanup(folder); }
    }

    [Fact]
    public async Task Flag_true_generates_long_and_short_comparison_images_and_reports_without_production_changes()
    {
        var folder = TempFolder();
        var productionScene = Path.Combine(folder, "scene01.png");
        Directory.CreateDirectory(folder);
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
            Assert.Equal(9, longReport!.FrameCount);
            Assert.Equal(9, longReport.GeneratedV4FrameCount);
            Assert.True(longReport.ProductionUnchanged);
            Assert.Equal("ManualReviewRequired", longReport.Recommendation);
            Assert.Equal(5, shortReport!.FrameCount);
            Assert.Equal(5, shortReport.GeneratedV4FrameCount);
            Assert.True(shortReport.ProductionUnchanged);
            Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "comparison", "frame01-hook-v4.png")));
            Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "comparison", "frame09-call-to-action-v4.png")));
            Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "comparison", "LongStoryFrameComparison.json")));
            Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "comparison", "frame01-hook-v4.png")));
            Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "comparison", "frame05-call-to-action-v4.png")));
            Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "comparison", "ShortStoryFrameComparison.json")));
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
            Assert.Equal(5, report!.FrameCount);
            Assert.Equal(4, report.GeneratedV4FrameCount);
            Assert.Contains(report.Warnings, warning => warning.Contains("Frame 3 failed non-blocking"));
            Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "comparison", "ShortStoryFrameComparison.json")));
        }
        finally { Cleanup(folder); }
    }

    private sealed class FakeGenerator(int failFrame = 0) : IAICinematicImageGenerator
    {
        public bool IsConfigured => true;
        public string DeploymentName => "fake-azure-image";
        public async Task<AICinematicProviderResult> GenerateAsync(AICinematicAssetRequest request, CancellationToken cancellationToken)
        {
            if (request.AssetId.EndsWith($"-{failFrame:00}", StringComparison.Ordinal)) throw new InvalidOperationException("planned failure");
            Directory.CreateDirectory(Path.GetDirectoryName(request.PlannedImagePath)!);
            await File.WriteAllTextAsync(request.PlannedImagePath, "v4 comparison image", cancellationToken);
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
