using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core;

public sealed class PromptFeedbackService : IPromptFeedbackService
{
    private static readonly IReadOnlyDictionary<ContentType, string[]> ToneByContentType = new Dictionary<ContentType, string[]>
    {
        [ContentType.DailySkyGuide] = ["Emphasize what is visible tonight.", "Keep beginner-friendly observation instructions concrete."],
        [ContentType.TelescopeTargets] = ["Prioritize target acquisition cues and eyepiece practicality.", "Keep recommendations realistic for beginner telescopes."],
        [ContentType.SpaceNews] = ["Lead with importance and timeliness.", "Explain why the update matters now."],
        [ContentType.AstrophotographyTips] = ["Favor practical settings, setup, and expected result framing.", "Use actionable step-by-step language."]
    };

    private readonly IAnalyticsFeedbackProvider _analyticsFeedbackProvider;
    private readonly IPipelineRepository _pipelineRepository;
    private readonly ILogger<PromptFeedbackService> _logger;
    private readonly IContentExperimentService? _contentExperimentService;

    public PromptFeedbackService(
        IAnalyticsFeedbackProvider analyticsFeedbackProvider,
        IPipelineRepository pipelineRepository,
        ILogger<PromptFeedbackService> logger,
        IContentExperimentService? contentExperimentService = null)
    {
        _analyticsFeedbackProvider = analyticsFeedbackProvider;
        _pipelineRepository = pipelineRepository;
        _logger = logger;
        _contentExperimentService = contentExperimentService;
    }

    public async Task<PromptFeedbackContext> BuildContextAsync(PromptFeedbackRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _analyticsFeedbackProvider.GetSummaryAsync(10, cancellationToken);
            var signals = await _analyticsFeedbackProvider.GetSignalsAsync(10, cancellationToken);
            var overusedTopics = await ResolveOverusedTopicsAsync(cancellationToken);
            var experimentFeedback = _contentExperimentService is null
                ? new ExperimentFeedbackSnapshot()
                : await _contentExperimentService.GetFeedbackSnapshotAsync(cancellationToken);

            var winningTopics = summary.TopVideosByViews
                .Concat(summary.TopShortsByRetention)
                .Select(x => ExtractTopic(x.Title))
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToArray();

            var recommendedTitlePatterns = summary.BestPerformingTitles
                .Concat(experimentFeedback.WinningTitlePatterns)
                .Select(ExtractTitlePattern)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();

            var avoidTitlePatterns = overusedTopics
                .Select(topic => $"Avoid repeating '{topic}' framing in opening title clause")
                .Take(4)
                .ToArray();

            var recommendedHooks = signals.BestHooks
                .Concat(experimentFeedback.WinningHooks)
                .Select(x => ShortenPattern(x, 90))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(request.IsShortForm ? 5 : 3)
                .ToArray();

            var avoidObjects = overusedTopics
                .Where(static x => x.Length > 2)
                .Take(4)
                .ToArray();

            var metadataHints = BuildMetadataHints(request.ContentType, signals.TopKeywords, experimentFeedback);
            var thumbnailHints = BuildThumbnailHints(experimentFeedback);

            var context = new PromptFeedbackContext
            {
                ContentType = request.ContentType,
                RecommendedKeywords = signals.TopKeywords.Take(8).ToArray(),
                AvoidKeywords = overusedTopics.Take(4).ToArray(),
                RecommendedHookPatterns = recommendedHooks,
                AvoidHookPatterns = ["Do not reuse the most recent exact hook sentence."],
                RecommendedTitlePatterns = recommendedTitlePatterns,
                AvoidTitlePatterns = avoidTitlePatterns,
                RecommendedToneNotes = ResolveToneNotes(request.ContentType, request.IsShortForm),
                RecentWinningTopics = winningTopics,
                RecentOverusedTopics = overusedTopics.Take(6).ToArray(),
                AvoidObjectEmphasis = avoidObjects,
                ShortsHookSuggestions = BuildShortHookSuggestions(signals.BestHooks.Concat(experimentFeedback.WinningHooks).ToArray(), request.TopicSelectionPlan),
                MetadataOptimizationHints = metadataHints,
                ThumbnailStrategyHints = thumbnailHints,
                TopicSelectionRationale = BuildTopicRationale(request.TopicSelectionPlan),
                UsedFallbackDefaults = false
            };

            if (IsEmpty(context))
            {
                return BuildFallback(request.ContentType, request.TopicSelectionPlan);
            }

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prompt feedback construction failed for {ContentType}. Using deterministic fallback context.", request.ContentType);
            return BuildFallback(request.ContentType, request.TopicSelectionPlan);
        }
    }

    private async Task<string[]> ResolveOverusedTopicsAsync(CancellationToken cancellationToken)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-14);
        var scripts = await _pipelineRepository.GetRecentGeneratedScriptsAsync(from, cancellationToken);
        return scripts
            .Select(x => ExtractTopic(x.Title))
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(6)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ResolveToneNotes(ContentType type, bool isShortForm)
    {
        var notes = ToneByContentType.TryGetValue(type, out var mapped)
            ? mapped.ToList()
            : new List<string> { "Keep the narrative clear, accurate, and beginner-friendly." };

        if (isShortForm)
        {
            notes.Add("Use a fast, high-clarity hook for Shorts pacing.");
        }

        return notes;
    }

    private static string[] BuildShortHookSuggestions(IReadOnlyCollection<string> hooks, TopicSelectionPlan? plan)
    {
        var suggestions = hooks
            .Select(x => ShortenPattern(x, 70))
            .Take(3)
            .ToList();

        var objectName = plan?.PrimaryLongForm?.ObjectName;
        if (!string.IsNullOrWhiteSpace(objectName))
        {
            suggestions.Add($"Stop scrolling — {objectName} is worth looking up tonight.");
        }

        if (suggestions.Count == 0)
        {
            suggestions.Add("Stop scrolling — tonight's sky has one quick thing you shouldn't miss.");
        }

        return suggestions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
    }

    private static string[] BuildMetadataHints(ContentType type, IReadOnlyCollection<string> keywords, ExperimentFeedbackSnapshot experimentFeedback)
    {
        var hints = new List<string>
        {
            "Use only one primary promise in title and keep phrasing natural.",
            "Reinforce top keyword intent in first 140 description characters."
        };

        if (keywords.Any())
        {
            hints.Add($"Prefer these proven keywords where natural: {string.Join(", ", keywords.Take(4))}.");
        }

        if (experimentFeedback.WinningCallToActions.Any())
        {
            hints.Add($"Winning CTA phrasing to reuse selectively: {experimentFeedback.WinningCallToActions.First()}.");
        }

        if (experimentFeedback.WinningHooks.Any())
        {
            hints.Add($"Best-performing hook family: {experimentFeedback.WinningHooks.First()}.");
        }

        var titleInsight = experimentFeedback.Insights.FirstOrDefault(x => x.ExperimentType == ContentExperimentType.Title);
        if (titleInsight is not null)
        {
            hints.Add($"Latest title winner delivered CTR {titleInsight.Metrics.Ctr?.ToString("F2") ?? "n/a"} with pattern: {titleInsight.WinningPattern}.");
        }

        return hints
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] BuildThumbnailHints(ExperimentFeedbackSnapshot experimentFeedback)
    {
        var hints = experimentFeedback.WinningThumbnailPatterns
            .Select(x => $"Recent winning thumbnail pattern: {x}")
            .ToList();

        var thumbnailInsight = experimentFeedback.Insights.FirstOrDefault(x => x.ExperimentType == ContentExperimentType.Thumbnail);
        if (thumbnailInsight is not null)
        {
            hints.Add($"Most recent thumbnail winner reached {thumbnailInsight.Metrics.Views} views with pattern: {thumbnailInsight.WinningPattern}");
        }

        return hints
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
    }

    private static string BuildTopicRationale(TopicSelectionPlan? plan)
    {
        var primary = plan?.PrimaryLongForm;
        if (primary is null)
        {
            return "No topic-planner rationale available; use baseline event-priority ordering.";
        }

        return $"Selected because score={primary.PriorityScore:F2} with observability={primary.ObservabilityScore:F2}, timeliness={primary.TimelinessScore:F2}, significance={primary.SignificanceScore:F2}. {primary.Rationale} Schedule hint: {plan?.SchedulingHints.Notes}";
    }

    private static bool IsEmpty(PromptFeedbackContext context)
        => context.RecommendedKeywords.Count == 0
           && context.RecommendedHookPatterns.Count == 0
           && context.RecommendedTitlePatterns.Count == 0
           && context.RecentWinningTopics.Count == 0
           && context.ThumbnailStrategyHints.Count == 0;

    private static PromptFeedbackContext BuildFallback(ContentType type, TopicSelectionPlan? plan)
        => new()
        {
            ContentType = type,
            RecommendedKeywords = ["astronomy", "night sky"],
            RecommendedHookPatterns = ["Lead with one concrete sky outcome for viewer."],
            RecommendedTitlePatterns = ["<Object/Event> Tonight: What You Can See"],
            RecommendedToneNotes = ResolveToneNotes(type, isShortForm: false),
            MetadataOptimizationHints = ["Keep title clear and non-clickbait.", "Keep tags compact and relevant."],
            ThumbnailStrategyHints = ["Use TopBanner layout when a winning thumbnail pattern is unavailable."],
            ShortsHookSuggestions = ["Stop scrolling — here's tonight's fastest sky tip."],
            TopicSelectionRationale = BuildTopicRationale(plan),
            UsedFallbackDefaults = true
        };

    private static string ExtractTopic(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var tokens = Regex.Split(title, "[^A-Za-z0-9]+").Where(x => x.Length >= 4).Take(2).ToArray();
        return tokens.Length == 0 ? string.Empty : string.Join(' ', tokens);
    }

    private static string ExtractTitlePattern(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var normalized = Regex.Replace(title, "\\d+", "<N>");
        normalized = Regex.Replace(normalized, "\\s+", " ").Trim();
        return ShortenPattern(normalized, 72);
    }

    private static string ShortenPattern(string text, int max)
        => text.Length <= max ? text : text[..max].TrimEnd() + "...";
}
