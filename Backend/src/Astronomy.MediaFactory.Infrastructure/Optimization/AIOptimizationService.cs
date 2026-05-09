using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Optimization;

public sealed class AIOptimizationService : IAIOptimizationService
{
    private const string ApiVersion = "2024-10-21";
    private static readonly Regex HashtagRegex = new("#[a-zA-Z0-9_]+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IPipelineRepository _repository;
    private readonly IAnalyticsIntelligenceService _analytics;
    private readonly IOptimizationService _optimization;
    private readonly HttpClient _httpClient;
    private readonly AIOptimizationOptions _options;
    private readonly AzureOpenAiOptions _azureOptions;
    private readonly SchedulerOptions _schedulerOptions;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly ILogger<AIOptimizationService> _logger;

    public AIOptimizationService(
        IPipelineRepository repository,
        IAnalyticsIntelligenceService analytics,
        IOptimizationService optimization,
        HttpClient httpClient,
        IOptions<AIOptimizationOptions> options,
        IOptions<AzureOpenAiOptions> azureOptions,
        IOptions<SchedulerOptions> schedulerOptions,
        IOptions<MaintenanceOptions> maintenanceOptions,
        ILogger<AIOptimizationService> logger)
    {
        _repository = repository;
        _analytics = analytics;
        _optimization = optimization;
        _httpClient = httpClient;
        _options = options.Value;
        _azureOptions = azureOptions.Value;
        _schedulerOptions = schedulerOptions.Value;
        _maintenanceOptions = maintenanceOptions.Value;
        _logger = logger;
    }

    public async Task<AIOptimizationRecommendations> GetRecommendationsAsync(CancellationToken cancellationToken)
    {
        var path = OutputPath;
        if (!File.Exists(path))
            return await GenerateNowAsync(cancellationToken);

        try
        {
            await using var stream = File.OpenRead(path);
            var recommendations = await JsonSerializer.DeserializeAsync<AIOptimizationRecommendations>(stream, JsonOptions, cancellationToken);
            return recommendations ?? await GenerateNowAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Unable to read cached AI optimization recommendations. Regenerating.");
            return await GenerateNowAsync(cancellationToken);
        }
    }

    public async Task<AIOptimizationRecommendations> GenerateNowAsync(CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(cancellationToken);
        var recommendations = await GenerateRecommendationsAsync(context, cancellationToken);
        recommendations = EnforceReadOnlyRecommendationRules(recommendations, context);
        await WriteRecommendationsAsync(recommendations, cancellationToken);
        return recommendations;
    }

    private async Task<AIOptimizationContext> LoadContextAsync(CancellationToken cancellationToken)
    {
        var request = new AnalyticsIntelligenceRequest(Days: 30, Limit: 25);
        var dashboard = await _analytics.BuildDashboardAsync(request, cancellationToken);
        var insights = await _analytics.GetInsightsAsync(request, cancellationToken);
        var topContent = await _analytics.GetTopContentAsync(request, cancellationToken);
        var firstSchedule = _schedulerOptions.Schedules.FirstOrDefault();
        var location = firstSchedule?.LocationName ?? "";
        var platform = dashboard.OverallSummary.BestPerformingPlatform ?? "YouTube";
        var rulePlan = await _optimization.BuildPlanAsync(location, platform, cancellationToken);
        var recentRuns = await _repository.GetRecentAsync(10, cancellationToken);
        var rawAnalytics = await _repository.GetPlatformContentAnalyticsAsync(new PlatformAnalyticsQuery(30, Take: 500), cancellationToken);

        return new AIOptimizationContext(dashboard, insights, topContent, rulePlan, recentRuns, _schedulerOptions, rawAnalytics);
    }

    private async Task<AIOptimizationRecommendations> GenerateRecommendationsAsync(AIOptimizationContext context, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || _options.Mode == OptimizationMode.Disabled)
        {
            return LowConfidence(context, "AI optimization is disabled; no recommendations were generated.");
        }

        if (context.AnalyticsRows.Count < _options.MinimumAnalyticsRows)
        {
            return LowConfidence(context, $"Insufficient analytics rows: found {context.AnalyticsRows.Count}, minimum required {_options.MinimumAnalyticsRows}.");
        }

        if (_options.UseAzureOpenAI)
        {
            if (MissingAzureConfiguration())
            {
                return LowConfidence(context, "Azure OpenAI configuration is missing Endpoint, ChatDeployment, or ApiKey/managed identity; recommendation generation failed gracefully without mutating scheduler or pipeline settings.");
            }

            try
            {
                var aiResult = await GenerateWithAzureOpenAIAsync(context, cancellationToken);
                if (IsSchemaValid(aiResult))
                    return aiResult;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Azure OpenAI AI optimization recommendation generation failed. Falling back to deterministic read-only recommendations.");
                var fallback = BuildDeterministicRecommendations(context);
                return AddWarnings(fallback, "Azure OpenAI generation failed; returned deterministic read-only recommendations based only on local analytics and rule-based optimization output.");
            }
        }

        return BuildDeterministicRecommendations(context);
    }

    private async Task<AIOptimizationRecommendations> GenerateWithAzureOpenAIAsync(AIOptimizationContext context, CancellationToken cancellationToken)
    {
        var endpoint = _azureOptions.Endpoint.TrimEnd('/');
        var uri = $"{endpoint}/openai/deployments/{_azureOptions.ChatDeployment}/chat/completions?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("api-key", _azureOptions.ApiKey);
        request.Content = JsonContent.Create(new
        {
            messages = new[]
            {
                new { role = "system", content = "You generate read-only content strategy recommendations. Do not invent analytics facts. Every recommendation must cite metric values present in the provided JSON. Return only JSON matching the requested schema." },
                new { role = "user", content = BuildPrompt(context) }
            },
            temperature = 0.2,
            response_format = new { type = "json_object" }
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(body);
        var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Azure OpenAI response did not include content.");

        var result = JsonSerializer.Deserialize<AIOptimizationRecommendations>(content, JsonOptions)
                     ?? throw new JsonException("Azure OpenAI recommendation JSON was empty.");
        return result;
    }

    private static string BuildPrompt(AIOptimizationContext context)
    {
        var payload = new
        {
            schema = new
            {
                recommendedHooks = "string[]",
                recommendedVideoIdeas = "string[]",
                recommendedThumbnailText = "string[]",
                recommendedPublishTimes = "string[]",
                recommendedObjectsToBoost = "string[]",
                recommendedObjectsToAvoid = "string[]",
                recommendedHashtagSets = "string[][]",
                riskWarnings = "string[]",
                confidenceScore = "number 0..1",
                reasoningSummary = "string with cited local metric values"
            },
            analyticsDashboardSummary = context.Dashboard,
            analyticsInsights = context.Insights,
            topContent = context.TopContent,
            ruleBasedOptimizationPlan = context.RulePlan,
            recentPipelineRuns = context.RecentRuns.Select(x => new { x.Id, x.RunDate, x.ContentType, x.LocationName, x.TimeZone, x.Status, x.StartedUtc, x.FinishedUtc }),
            currentSchedulerConfig = context.Scheduler,
            localAnalyticsMetricRows = context.AnalyticsRows.Select(x => new { x.Platform, x.PlatformContentType, x.PlatformMediaId, x.Title, x.Views, x.Likes, x.Comments, x.Shares, x.EngagementRate, x.Ctr, x.DurationSeconds, x.PublishedUtc, x.Hashtags, x.PerformanceScore })
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private AIOptimizationRecommendations BuildDeterministicRecommendations(AIOptimizationContext context)
    {
        var top = context.TopContent.OrderByDescending(x => x.PerformanceScore).Take(5).ToArray();
        var hooks = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.RulePlan.RecommendedHookStyle))
            hooks.Add($"{context.RulePlan.RecommendedHookStyle} (rule confidence {context.RulePlan.ConfidenceScore:0.00}; top sample count {context.AnalyticsRows.Count}).");
        hooks.AddRange(top.Where(x => !string.IsNullOrWhiteSpace(x.Title)).Take(2).Select(x => $"Model opening after top title '{x.Title}' with views={x.Views}, engagement={x.Engagement}, performanceScore={x.PerformanceScore:0.##}."));

        var videoIdeas = top.Where(x => !string.IsNullOrWhiteSpace(x.Title)).Select(x => $"Follow up on '{x.Title}' because local analytics show views={x.Views}, engagementRate={x.EngagementRate:0.####}, shares={x.Shares}.").Take(5).ToArray();
        var thumbnailText = context.Dashboard.ThumbnailIntelligence.Variants.OrderByDescending(x => x.AveragePerformanceScore).Take(3).Select(x => $"{x.Variant}: cite avgCtr={(x.AverageCtr?.ToString("0.####") ?? "n/a")}, totalViews={x.TotalViews}.").ToArray();
        var publishTimes = new List<string>();
        if (context.Dashboard.TimeIntelligence.BestPublishHour.HasValue)
            publishTimes.Add($"{context.Dashboard.TimeIntelligence.BestPublishHour.Value:00}:00 UTC (dashboard bestPublishHour={context.Dashboard.TimeIntelligence.BestPublishHour.Value}, totalContent={context.Dashboard.OverallSummary.TotalContentPublished}).");
        if (!string.IsNullOrWhiteSpace(context.RulePlan.RecommendedPublishTimeLocal))
            publishTimes.Add($"{context.RulePlan.RecommendedPublishTimeLocal} local (rule plan confidence={context.RulePlan.ConfidenceScore:0.00}).");

        var hashtagSets = BuildHashtagSets(context);
        var risks = BuildRiskWarnings(context).ToList();
        var confidence = Math.Round(Math.Clamp(Math.Min(context.RulePlan.ConfidenceScore, 0.95) * 0.7 + Math.Min(context.AnalyticsRows.Count, 100) / 100d * 0.3, 0.05, 0.9), 2);

        return new AIOptimizationRecommendations
        {
            RecommendedHooks = hooks.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray(),
            RecommendedVideoIdeas = videoIdeas,
            RecommendedThumbnailText = thumbnailText,
            RecommendedPublishTimes = publishTimes.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray(),
            RecommendedObjectsToBoost = context.RulePlan.PreferredContentObjects.Take(8).Select(x => $"{x} (rule plan confidence={context.RulePlan.ConfidenceScore:0.00}; analytics rows={context.AnalyticsRows.Count}).").ToArray(),
            RecommendedObjectsToAvoid = context.RulePlan.AvoidedContentObjects.Take(8).Select(x => $"{x} (rule-based avoidance from local analytics; analytics rows={context.AnalyticsRows.Count}).").ToArray(),
            RecommendedHashtagSets = hashtagSets,
            RiskWarnings = risks,
            ConfidenceScore = confidence,
            ReasoningSummary = $"Read-only recommendations use {context.AnalyticsRows.Count} local analytics rows, dashboard totalViews={context.Dashboard.OverallSummary.TotalViews}, totalEngagement={context.Dashboard.OverallSummary.TotalEngagement}, bestPlatform={context.Dashboard.OverallSummary.BestPerformingPlatform ?? "n/a"}, and rulePlan confidence={context.RulePlan.ConfidenceScore:0.00}. No scheduler or pipeline fields were changed."
        };
    }

    private static IReadOnlyCollection<IReadOnlyCollection<string>> BuildHashtagSets(AIOptimizationContext context)
    {
        var fromRule = context.RulePlan.RecommendedHashtags.Take(8).ToArray();
        var fromAnalytics = context.AnalyticsRows
            .OrderByDescending(x => x.PerformanceScore ?? 0)
            .Take(20)
            .SelectMany(x => string.IsNullOrWhiteSpace(x.Hashtags) ? Enumerable.Empty<string>() : HashtagRegex.Matches(x.Hashtags).Select(m => m.Value))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key)
            .Take(8)
            .Select(x => x.Key)
            .ToArray();
        return new[] { fromRule, fromAnalytics }.Where(x => x.Length > 0).Cast<IReadOnlyCollection<string>>().ToArray();
    }

    private IEnumerable<string> BuildRiskWarnings(AIOptimizationContext context)
    {
        if (_options.Mode != OptimizationMode.RecommendOnly)
            yield return $"AIOptimization mode is {_options.Mode}; this service still returns read-only recommendations and does not apply AI decisions.";
        if (context.AnalyticsRows.Count < _options.MinimumAnalyticsRows * 2)
            yield return $"Limited analytics volume: rows={context.AnalyticsRows.Count}, configured minimum={_options.MinimumAnalyticsRows}. Treat recommendations as directional.";
        if (context.Dashboard.OverallSummary.TotalContentPublished == 0)
            yield return "Dashboard totalContentPublished=0; confidence is constrained by missing content summary.";
    }

    private AIOptimizationRecommendations LowConfidence(AIOptimizationContext context, string warning)
        => new()
        {
            RiskWarnings = [warning],
            ConfidenceScore = 0.1,
            ReasoningSummary = $"Low confidence because {warning} Local source metrics: analyticsRows={context.AnalyticsRows.Count}, dashboardTotalViews={context.Dashboard.OverallSummary.TotalViews}, dashboardTotalEngagement={context.Dashboard.OverallSummary.TotalEngagement}, rulePlanConfidence={context.RulePlan.ConfidenceScore:0.00}. No scheduler or pipeline fields were changed."
        };

    private AIOptimizationRecommendations EnforceReadOnlyRecommendationRules(AIOptimizationRecommendations recommendations, AIOptimizationContext context)
    {
        if (context.AnalyticsRows.Count < _options.MinimumAnalyticsRows && recommendations.ConfidenceScore > 0.3)
            recommendations = Clone(recommendations, confidenceScore: 0.3);

        var sourceMetrics = $" Source metrics: analyticsRows={context.AnalyticsRows.Count}, dashboardTotalViews={context.Dashboard.OverallSummary.TotalViews}, dashboardTotalEngagement={context.Dashboard.OverallSummary.TotalEngagement}, rulePlanConfidence={context.RulePlan.ConfidenceScore:0.00}.";
        var reasoning = recommendations.ReasoningSummary;
        if (!reasoning.Contains("analyticsRows=", StringComparison.OrdinalIgnoreCase))
            reasoning += sourceMetrics;
        if (!reasoning.Contains("No scheduler or pipeline fields were changed", StringComparison.OrdinalIgnoreCase))
            reasoning += " No scheduler or pipeline fields were changed.";

        if (!string.Equals(reasoning, recommendations.ReasoningSummary, StringComparison.Ordinal))
            recommendations = Clone(recommendations, reasoningSummary: reasoning);

        return recommendations;
    }

    private static bool IsSchemaValid(AIOptimizationRecommendations recommendations)
        => recommendations.ConfidenceScore is >= 0 and <= 1 && recommendations.ReasoningSummary.Length > 0;

    private bool MissingAzureConfiguration()
        => string.IsNullOrWhiteSpace(_azureOptions.Endpoint)
           || string.IsNullOrWhiteSpace(_azureOptions.ChatDeployment)
           || _azureOptions.UseManagedIdentity
           || string.IsNullOrWhiteSpace(_azureOptions.ApiKey);

    private static AIOptimizationRecommendations AddWarnings(AIOptimizationRecommendations source, params string[] warnings)
        => Clone(source, riskWarnings: source.RiskWarnings.Concat(warnings).ToArray());

    private static AIOptimizationRecommendations Clone(
        AIOptimizationRecommendations source,
        IReadOnlyCollection<string>? riskWarnings = null,
        double? confidenceScore = null,
        string? reasoningSummary = null)
        => new()
        {
            RecommendedHooks = source.RecommendedHooks,
            RecommendedVideoIdeas = source.RecommendedVideoIdeas,
            RecommendedThumbnailText = source.RecommendedThumbnailText,
            RecommendedPublishTimes = source.RecommendedPublishTimes,
            RecommendedObjectsToBoost = source.RecommendedObjectsToBoost,
            RecommendedObjectsToAvoid = source.RecommendedObjectsToAvoid,
            RecommendedHashtagSets = source.RecommendedHashtagSets,
            RiskWarnings = riskWarnings ?? source.RiskWarnings,
            ConfidenceScore = confidenceScore ?? source.ConfidenceScore,
            ReasoningSummary = reasoningSummary ?? source.ReasoningSummary
        };

    private async Task WriteRecommendationsAsync(AIOptimizationRecommendations recommendations, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(OutputDirectory);
        await File.WriteAllTextAsync(OutputPath, JsonSerializer.Serialize(recommendations, JsonOptions), cancellationToken);
    }

    private string OutputDirectory => string.IsNullOrWhiteSpace(_maintenanceOptions.WorkingDirectory) ? Directory.GetCurrentDirectory() : _maintenanceOptions.WorkingDirectory;
    private string OutputPath => Path.Combine(OutputDirectory, string.IsNullOrWhiteSpace(_options.OutputFileName) ? "ai-optimization-recommendations.json" : _options.OutputFileName);

    private sealed record AIOptimizationContext(
        AnalyticsDashboardResponse Dashboard,
        IReadOnlyCollection<AnalyticsInsight> Insights,
        IReadOnlyCollection<AnalyticsTopContentItem> TopContent,
        OptimizationPlan RulePlan,
        IReadOnlyCollection<PipelineRun> RecentRuns,
        SchedulerOptions Scheduler,
        IReadOnlyCollection<PlatformContentAnalytics> AnalyticsRows);
}
