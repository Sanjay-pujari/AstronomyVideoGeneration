using System.Text.Json;

namespace Astronomy.MediaFactory.AIOptimization;

public interface IHookOptimizationService
{
    IReadOnlyCollection<HookScoreResult> Score(HookOptimizationRequest request);
}

public interface ITrendSignalProvider
{
    IReadOnlyCollection<TrendSignalResult> GetSignals(DateOnly date);
}

public interface IPublishingOptimizationService
{
    PublishingOptimizationResult BuildRecommendation(Guid pipelineRunId, string language, string eventType);
}

public sealed class HookOptimizationService : IHookOptimizationService
{
    public IReadOnlyCollection<HookScoreResult> Score(HookOptimizationRequest request)
    {
        return request.GeneratedHooks.Select(hook =>
        {
            var curiosity = Math.Min(1, 0.45 + (hook.Contains('?') ? 0.25 : 0.05));
            var emotional = Math.Min(1, 0.3 + (hook.Contains('!') ? 0.2 : 0.1));
            var clarity = Math.Max(0.1, 1 - Math.Abs(hook.Length - 60) / 100d);
            var click = Math.Clamp((curiosity * 0.4) + (emotional * 0.2) + (clarity * 0.4), 0, 1);
            var finalScore = Math.Round(click * 100, 2);
            return new HookScoreResult(hook, curiosity, emotional, clarity, click, finalScore, "Balanced curiosity/clarity scoring.");
        }).OrderByDescending(x => x.FinalScore).ToArray();
    }
}

public sealed class StaticTrendSignalProvider : ITrendSignalProvider
{
    public IReadOnlyCollection<TrendSignalResult> GetSignals(DateOnly date) =>
    [
        new(date, "meteor shower", 0.76, "internal-static", DateTimeOffset.UtcNow),
        new(date, "eclipse", 0.88, "internal-static", DateTimeOffset.UtcNow),
        new(date, "planetary alignment", 0.68, "internal-static", DateTimeOffset.UtcNow)
    ];
}

public sealed class PublishingOptimizationService : IPublishingOptimizationService
{
    public PublishingOptimizationResult BuildRecommendation(Guid pipelineRunId, string language, string eventType) =>
        new(
            pipelineRunId,
            DateTimeOffset.UtcNow.Date.AddHours(18),
            ["#astronomy", "#spacescience", "#stargazing"],
            ["astronomy", eventType, language],
            "general-space-enthusiasts",
            ["YouTube", "Instagram", "Facebook"],
            DateTimeOffset.UtcNow);
}

public static class HookOptimizationReportWriter
{
    public static async Task WriteAsync(string outputPath, HookOptimizationRequest request, IReadOnlyCollection<HookScoreResult> scores, CancellationToken ct)
    {
        var payload = new
        {
            generatedHooks = request.GeneratedHooks,
            scores,
            selectedRecommendedHook = scores.OrderByDescending(s => s.FinalScore).FirstOrDefault()?.Hook,
            language = request.Language,
            targetAudience = request.TargetAudience,
            eventType = request.EventType
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json, ct);
    }
}
