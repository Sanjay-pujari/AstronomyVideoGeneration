using Astronomy.MediaFactory.AIOptimization;

namespace Astronomy.MediaFactory.Tests;

public sealed class AIOptimizationFoundationTests
{
    [Fact]
    public void HookVariants_AreScored()
    {
        var service = new HookOptimizationService();
        var request = new HookOptimizationRequest(new[]
        {
            "What if tonight's meteor shower rewrites your view of the sky?",
            "Meteor shower viewing guide for beginners",
            "This cosmic storm will blow your mind!"
        }, "en", new[] { "meteor", "moon" }, "meteor-shower", "beginner");

        var results = service.Score(request);
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.FinalScore > 0));
    }

    [Fact]
    public async Task OptimizationReport_IsGenerated()
    {
        var service = new HookOptimizationService();
        var request = new HookOptimizationRequest(new[] { "Educational hook", "Curiosity hook?" }, "en", new[] { "mars" }, "planetary", "general");
        var scores = service.Score(request);
        var file = Path.Combine(Path.GetTempPath(), $"ai-hook-optimization-report-{Guid.NewGuid():N}.json");

        await HookOptimizationReportWriter.WriteAsync(file, request, scores, CancellationToken.None);

        Assert.True(File.Exists(file));
        var text = await File.ReadAllTextAsync(file);
        Assert.Contains("selectedRecommendedHook", text);
    }
}
