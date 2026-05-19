using Astronomy.MediaFactory.AIOptimization;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AIOptimizationPhase1Tests
{
    [Fact]
    public async Task HookVariants_AreScored()
    {
        var service = new HookOptimizationService();
        var result = await service.ScoreAsync(new HookOptimizationRequest([
            "Educational hook about Saturn rings",
            "Dramatic sky event you must not miss?",
            "Curiosity-driven mystery in tonight's sky",
            "Scientific explanation of lunar eclipse",
            "Emotional cosmic wonder for everyone"], "en", ["Saturn"], "metadata", "general", "eclipse"), default);
        Assert.True(result.Count >= 3);
    }
}
