using System.Text.Json;
namespace Astronomy.MediaFactory.AIOptimization;

public sealed class HookOptimizationService : IHookOptimizationService
{
    public Task<IReadOnlyCollection<HookScoreResult>> ScoreAsync(HookOptimizationRequest request, CancellationToken cancellationToken)
    {
        var scored = request.GeneratedHooks.Take(Math.Clamp(request.GeneratedHooks.Count,3,5)).Select(h => {
            var curiosity = Math.Min(1d, (h.Contains("?", StringComparison.Ordinal) ? 0.35 : 0.2) + h.Length / 200d);
            var emotional = Math.Min(1d, (h.Contains("amazing", StringComparison.OrdinalIgnoreCase) ? 0.35 : 0.2) + (request.EventType.Contains("meteor", StringComparison.OrdinalIgnoreCase) ? 0.25 : 0.1));
            var clarity = 1d - Math.Min(0.5d, Math.Abs(60 - h.Length) / 120d);
            var click = Math.Clamp((curiosity * 0.35) + (emotional * 0.3) + (clarity * 0.35), 0d, 1d);
            return new HookScoreResult(h, curiosity, emotional, clarity, click, click, "Scored using deterministic phase-1 heuristic for curiosity/emotion/clarity.");
        }).OrderByDescending(x => x.FinalScore).ToArray();
        return Task.FromResult<IReadOnlyCollection<HookScoreResult>>(scored);
    }

    public async Task<HookOptimizationReport> BuildReportAsync(HookOptimizationRequest request, string outputDirectory, CancellationToken cancellationToken)
    {
        var scores = await ScoreAsync(request, cancellationToken);
        var report = new HookOptimizationReport(request.PipelineRunId, request.GeneratedHooks.ToArray(), scores, scores.OrderByDescending(s => s.FinalScore).FirstOrDefault(), request.Language, request.TargetAudience, request.EventType);
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "ai-hook-optimization-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}), cancellationToken);
        return report;
    }
}

public sealed class StaticTrendSignalProvider : ITrendSignalProvider
{
    public Task<IReadOnlyCollection<TrendSignalResult>> GetSignalsAsync(DateOnly date, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<TrendSignalResult>>([
            new TrendSignalResult(Guid.NewGuid(), date, "meteor-shower", 0.71, "static", DateTimeOffset.UtcNow),
            new TrendSignalResult(Guid.NewGuid(), date, "lunar-eclipse", 0.78, "static", DateTimeOffset.UtcNow)
        ]);
}
