using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Optimization;

public sealed class RuleBasedOptimizationService : IOptimizationService
{
    private static readonly string[] AstronomyTopics = ["Moon", "Venus", "Mars", "Jupiter", "Saturn", "Orion Nebula", "Meteor Shower", "Eclipse", "Comet", "Galaxy", "Cluster"];
    private static readonly Regex HashtagRegex = new("#[a-zA-Z0-9_]+", RegexOptions.Compiled);
    private readonly IPipelineRepository _repository;
    private readonly IAnalyticsIntelligenceService _analytics;
    private readonly OptimizationOptions _options;
    private readonly MaintenanceOptions _maintenance;

    public RuleBasedOptimizationService(IPipelineRepository repository, IAnalyticsIntelligenceService analytics, IOptions<OptimizationOptions> options, IOptions<MaintenanceOptions> maintenance)
    {
        _repository = repository;
        _analytics = analytics;
        _options = options.Value;
        _maintenance = maintenance.Value;
    }

    public async Task<OptimizationPlan> BuildPlanAsync(string locationName, string platform, CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        var appliedRules = new List<string>();
        var query = new PlatformAnalyticsQuery(30, platform, locationName, null, Math.Max(_options.MinimumDataPoints * 5, 100));
        var rawItems = (await _repository.GetPlatformContentAnalyticsAsync(query, cancellationToken)).Where(x => x.IsAnalyticsAvailable).ToArray();
        var request = new AnalyticsIntelligenceRequest(30, platform, null, locationName, 20);
        var dashboard = await _analytics.BuildDashboardAsync(request, cancellationToken);
        var insights = await _analytics.GetInsightsAsync(request, cancellationToken);

        var plan = new OptimizationPlan { LocationName = locationName, Platform = platform };
        if (!_options.Enabled || _options.Mode == OptimizationMode.Disabled)
        {
            plan.ConfidenceScore = 0;
            plan.Reasons = ["Optimization is disabled."];
            await WritePlanAsync(plan, cancellationToken);
            return plan;
        }

        if (rawItems.Length < _options.MinimumDataPoints)
        {
            plan.ConfidenceScore = Math.Round(rawItems.Length / (double)Math.Max(1, _options.MinimumDataPoints) * 0.39, 2);
            plan.Reasons = [$"Only {rawItems.Length} analytics data point(s) are available; at least {_options.MinimumDataPoints} are required for confident optimization."];
            await WritePlanAsync(plan, cancellationToken);
            return plan;
        }

        if (_options.AllowPublishTimeOptimization && TryHasTwentyPercentLift(rawItems.Where(x => x.PublishedUtc.HasValue).GroupBy(x => x.PublishedUtc!.Value.Hour).Select(g => (Key: g.Key, Avg: g.Average(EngagementRate), Count: g.Count())), out var bestHour, out var hourLift))
        {
            plan.RecommendedPublishTimeLocal = $"{bestHour:00}:00";
            reasons.Add($"Publish hour {bestHour:00}:00 has {hourLift:0}% higher engagement than the next best hour.");
            appliedRules.Add("PublishTimeRule");
        }

        if (_options.AllowDurationOptimization && dashboard.ReelIntelligence.DurationBuckets.Count > 0)
        {
            var buckets = dashboard.ReelIntelligence.DurationBuckets.Where(x => x.ContentCount > 0).Select(x => (x.Range, Score: x.AveragePerformanceScore, x.ContentCount)).ToArray();
            if (TryHasTwentyPercentLift(buckets.Select(x => (x.Range, x.Score, x.ContentCount)), out var bestRange, out var durationLift))
            {
                plan.PreferredShortDurationRange = bestRange;
                reasons.Add($"{bestRange} short-form content performs {durationLift:0}% better than the next duration bucket.");
                appliedRules.Add("DurationRule");
            }
        }

        if (_options.AllowObjectRankingOptimization)
        {
            var objectScores = AstronomyTopics.Select(topic => new { Topic = topic, Items = rawItems.Where(x => ContainsTopic(x, topic)).ToArray() })
                .Where(x => x.Items.Length > 0)
                .Select(x => (x.Topic, Score: x.Items.Average(PerformanceScore), Count: x.Items.Length))
                .OrderByDescending(x => x.Score).ToArray();
            if (TryHasTwentyPercentLift(objectScores.Select(x => (x.Topic, x.Score, x.Count)), out var bestTopic, out var objectLift))
            {
                plan.PreferredContentObjects = [bestTopic];
                plan.AvoidedContentObjects = objectScores.Reverse().Take(2).Where(x => !x.Topic.Equals(bestTopic, StringComparison.OrdinalIgnoreCase)).Select(x => x.Topic).ToArray();
                reasons.Add($"{bestTopic} content performs {objectLift:0}% better than the next astronomy object/topic.");
                appliedRules.Add("ObjectRankingRule");
            }
        }

        if (_options.AllowThumbnailOptimization && dashboard.ThumbnailIntelligence.Variants.Count > 0)
        {
            var variants = dashboard.ThumbnailIntelligence.Variants.Where(x => x.ContentCount > 0 && (x.AverageCtr.HasValue || x.AveragePerformanceScore > 0)).Select(x => (x.Variant, Score: x.AverageCtr ?? x.AveragePerformanceScore, x.ContentCount)).ToArray();
            if (TryHasTwentyPercentLift(variants, out var variant, out var thumbnailLift))
            {
                plan.RecommendedThumbnailStyle = variant;
                reasons.Add($"Thumbnail style {variant} has {thumbnailLift:0}% better CTR/performance than the next variant.");
                appliedRules.Add("ThumbnailRule");
            }
        }

        var topContent = (await _analytics.GetTopContentAsync(request, cancellationToken)).Take(10).ToArray();
        var topTitles = topContent.Select(x => x.Title).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray();
        var questionCount = topTitles.Count(x => x.Contains('?'));
        if (_options.AllowTitleOptimization && questionCount > 0 && questionCount >= Math.Ceiling(topTitles.Length * 0.4d))
        {
            plan.RecommendedHookStyle = "Question-led hook";
            reasons.Add("Top content frequently uses a question-led opening hook.");
            appliedRules.Add("HookRule");
        }
        else if (_options.AllowTitleOptimization && topTitles.Any(x => x.StartsWith("Tonight", StringComparison.OrdinalIgnoreCase)))
        {
            plan.RecommendedHookStyle = "Tonight-led timely hook";
            reasons.Add("Top content frequently uses timely Tonight-led hooks.");
            appliedRules.Add("HookRule");
        }

        var hashtags = rawItems.OrderByDescending(PerformanceScore).Take(20).SelectMany(x => ExtractHashtags(x.Hashtags)).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(8).Select(g => g.Key).ToArray();
        if (hashtags.Length > 0)
        {
            plan.RecommendedHashtags = hashtags;
            reasons.Add("Recommended hashtags are drawn from top-performing content metadata.");
            appliedRules.Add("HashtagRule");
        }

        plan.ConfidenceScore = CalculateConfidence(rawItems.Length, appliedRules.Count, insights.Count);
        plan.Reasons = reasons.Count == 0 ? ["No safe optimization rule exceeded the 20% lift threshold."] : reasons;
        plan.AppliedRules = appliedRules;
        await WritePlanAsync(plan, cancellationToken);
        return plan;
    }

    public async Task<RunPipelineRequest> ApplyPlanAsync(RunPipelineRequest request, OptimizationPlan plan, CancellationToken cancellationToken)
    {
        var original = request;
        var result = request;
        if (!_options.Enabled || _options.Mode != OptimizationMode.ApplySafeRules || plan.ConfidenceScore < _options.ConfidenceThreshold)
        {
            await WriteAppliedAsync(original, result, plan, [], cancellationToken);
            return result;
        }

        var changed = new List<string>();
        if (_options.AllowObjectRankingOptimization && plan.PreferredContentObjects.Count > 0 && !result.UseTopicPlanner)
        {
            result = result with { UseTopicPlanner = true };
            changed.Add(nameof(RunPipelineRequest.UseTopicPlanner));
        }

        await WriteAppliedAsync(original, result, plan, changed, cancellationToken);
        return result;
    }

    private async Task WritePlanAsync(OptimizationPlan plan, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(OutputDirectory);
        await File.WriteAllTextAsync(Path.Combine(OutputDirectory, "optimization-plan.json"), JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
    }

    private async Task WriteAppliedAsync(RunPipelineRequest original, RunPipelineRequest result, OptimizationPlan plan, IReadOnlyCollection<string> changedFields, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(OutputDirectory);
        var payload = new OptimizationApplyResult { OriginalRequest = original, ResultRequest = result, Plan = plan, ChangedFields = changedFields, Mode = _options.Mode.ToString() };
        await File.WriteAllTextAsync(Path.Combine(OutputDirectory, "optimization-applied.json"), JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
    }

    private string OutputDirectory => string.IsNullOrWhiteSpace(_maintenance.WorkingDirectory) ? Directory.GetCurrentDirectory() : _maintenance.WorkingDirectory;
    private static JsonSerializerOptions JsonOptions => new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static bool TryHasTwentyPercentLift<T>(IEnumerable<(T Key, double Avg, int Count)> scores, out T bestKey, out double lift)
    {
        var ordered = scores.Where(x => x.Count > 0).OrderByDescending(x => x.Avg).ToArray();
        bestKey = ordered.FirstOrDefault().Key!;
        lift = 0;
        if (ordered.Length == 0 || ordered[0].Avg <= 0) return false;
        var baseline = ordered.Length > 1 ? ordered[1].Avg : ordered.Skip(1).DefaultIfEmpty().Average(x => x.Avg);
        if (baseline <= 0) return false;
        lift = ((ordered[0].Avg - baseline) / baseline) * 100;
        return lift > 20;
    }

    private static double CalculateConfidence(int samples, int rules, int insights)
        => Math.Round(Math.Clamp(0.35 + Math.Min(samples, 100) / 100d * 0.35 + Math.Min(rules, 6) / 6d * 0.2 + Math.Min(insights, 5) / 5d * 0.1, 0, 1), 2);
    private static double EngagementRate(PlatformContentAnalytics x) => x.EngagementRate ?? ((x.Views ?? 0) <= 0 ? 0 : (double)((x.Likes ?? 0) + (x.Comments ?? 0) + (x.Shares ?? 0)) / (x.Views ?? 1));
    private static double PerformanceScore(PlatformContentAnalytics x) => x.PerformanceScore ?? ((x.Views ?? 0) * EngagementRate(x));
    private static bool ContainsTopic(PlatformContentAnalytics item, string topic) => (item.Title ?? "").Contains(topic, StringComparison.OrdinalIgnoreCase) || (item.Hashtags ?? "").Contains(topic.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) || (item.Hashtags ?? "").Contains(topic, StringComparison.OrdinalIgnoreCase);
    private static IReadOnlyCollection<string> ExtractHashtags(string? text) => string.IsNullOrWhiteSpace(text) ? [] : HashtagRegex.Matches(text).Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
