using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class TopicSelectionService : ITopicSelectionService
{
    private readonly IAstronomyContextProvider _contextProvider;
    private readonly IPipelineRepository _repository;
    private readonly TopicSelectionOptions _options;

    public TopicSelectionService(
        IAstronomyContextProvider contextProvider,
        IPipelineRepository repository,
        IOptions<TopicSelectionOptions> options)
    {
        _contextProvider = contextProvider;
        _repository = repository;
        _options = options.Value;
    }

    public async Task<TopicSelectionPlan> BuildPlanAsync(TopicSelectionRequest request, CancellationToken cancellationToken)
    {
        var requestedType = request.ContentType ?? ContentType.DailySkyGuide;
        var context = await _contextProvider.BuildContextAsync(request.Date, requestedType, request.LocationName, request.TimeZone, cancellationToken);
        var analytics = await _repository.GetAnalyticsByContentTypeAsync(requestedType, DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, 50, cancellationToken);

        var repetitionWindowDays = Math.Max(1, _options.RepetitionWindowDays);
        var repetitionWindowStart = DateTimeOffset.UtcNow.AddDays(-repetitionWindowDays);
        var recentVideos = await _repository.GetRecentPublishedVideosAsync(repetitionWindowStart, cancellationToken);
        var recentScripts = await _repository.GetRecentGeneratedScriptsAsync(repetitionWindowStart, cancellationToken);

        var repetitionIndex = RepetitionIndex.Build(recentVideos, recentScripts);
        var trendSnapshot = TrendSnapshot.Build(requestedType, analytics);
        var opportunities = BuildOpportunities(request, context, requestedType, trendSnapshot, repetitionIndex);

        if (opportunities.Count == 0)
        {
            opportunities.Add(BuildFallbackOpportunity(request, requestedType));
        }

        var ranked = opportunities
            .OrderByDescending(x => x.PriorityScore)
            .ThenBy(x => x.TitleCandidate)
            .Take(Math.Max(1, request.MaxCandidates))
            .ToArray();

        var primary = ranked.FirstOrDefault(x => x.IsLongFormCandidate) ?? ranked.First();
        var shorts = ranked.Where(x => x.IsShortCandidate).Take(3).ToArray();
        var alternates = ranked.Where(x => x.Id != primary.Id).Take(3).ToArray();

        return new TopicSelectionPlan
        {
            PrimaryLongForm = primary,
            ShortsCandidates = shorts,
            AlternateCandidates = alternates,
            RankedOpportunities = ranked,
            SchedulingHints = BuildSchedulingHints(requestedType, request.Date, primary)
        };
    }

    private List<ContentOpportunity> BuildOpportunities(
        TopicSelectionRequest request,
        AstronomyContext context,
        ContentType requestedType,
        TrendSnapshot trendSnapshot,
        RepetitionIndex repetitionIndex)
    {
        var opportunities = new List<ContentOpportunity>();

        foreach (var ev in context.Events)
        {
            var eventType = NormalizeEventType(ev.Category, ev.Details, ev.ObjectName);
            var scores = ComposeScores(
                request.Date,
                context.Date,
                requestedType,
                ev,
                eventType,
                trendSnapshot,
                repetitionIndex);

            opportunities.Add(new ContentOpportunity
            {
                TitleCandidate = BuildTitleCandidate(eventType, ev.ObjectName, request.Date),
                ContentType = requestedType,
                EventType = eventType,
                ObjectName = string.IsNullOrWhiteSpace(ev.ObjectName) ? "Night Sky" : ev.ObjectName,
                Date = request.Date,
                PriorityScore = scores.Priority,
                ObservabilityScore = scores.Observability,
                TimelinessScore = scores.Timeliness,
                SignificanceScore = scores.Significance,
                EducationalValueScore = scores.Educational,
                GrowthPotentialScore = scores.Growth,
                DiversityScore = scores.Diversity,
                Rationale = BuildRationale(eventType, scores),
                IsShortCandidate = scores.Timeliness >= 0.5 || eventType is "conjunction" or "moon phase" or "meteor shower",
                IsLongFormCandidate = scores.Educational >= 0.5 || eventType is "eclipse" or "space news" or "astrophotography"
            });
        }

        return opportunities;
    }

    private TopicScoreBreakdown ComposeScores(
        DateOnly requestedDate,
        DateOnly contextDate,
        ContentType contentType,
        AstronomyEventModel astronomyEvent,
        string eventType,
        TrendSnapshot trendSnapshot,
        RepetitionIndex repetitionIndex)
    {
        var observability = Clamp01(astronomyEvent.Score);
        var timeliness = ComputeTimeliness(requestedDate, contextDate);
        var educational = ComputeEducationalValue(eventType, astronomyEvent.ObjectName);
        var significance = ComputeSignificance(eventType, astronomyEvent.Details);
        var growth = ComputeGrowthPotential(contentType, trendSnapshot, eventType, astronomyEvent.ObjectName);
        var diversity = repetitionIndex.GetDiversityScore(astronomyEvent.ObjectName, eventType);

        var priority =
            (_options.TimelinessWeight * timeliness) +
            (_options.ObservabilityWeight * observability) +
            (_options.SignificanceWeight * significance) +
            (_options.EducationalValueWeight * educational) +
            (_options.GrowthPotentialWeight * growth) +
            (_options.DiversityWeight * diversity);

        return new TopicScoreBreakdown(
            Math.Round(Clamp01(priority), 4),
            Math.Round(observability, 4),
            Math.Round(timeliness, 4),
            Math.Round(significance, 4),
            Math.Round(educational, 4),
            Math.Round(growth, 4),
            Math.Round(diversity, 4));
    }

    private static ContentOpportunity BuildFallbackOpportunity(TopicSelectionRequest request, ContentType requestedType)
        => new()
        {
            TitleCandidate = $"Best Thing To Watch Tonight - {request.Date:MMM dd}",
            ContentType = requestedType,
            EventType = "fallback",
            ObjectName = "Night Sky Highlights",
            Date = request.Date,
            PriorityScore = 0.55,
            ObservabilityScore = 0.45,
            TimelinessScore = 0.7,
            SignificanceScore = 0.5,
            EducationalValueScore = 0.5,
            GrowthPotentialScore = 0.4,
            DiversityScore = 0.8,
            Rationale = "Fallback opportunity: sparse event data, using broad nightly observing recommendation.",
            IsLongFormCandidate = true,
            IsShortCandidate = true
        };

    private static TopicSelectionSchedulingHints BuildSchedulingHints(ContentType contentType, DateOnly requestedDate, ContentOpportunity? primary)
    {
        var preferredHourUtc = contentType switch
        {
            ContentType.DailySkyGuide => 12,
            ContentType.TelescopeTargets => 13,
            ContentType.SpaceNews => 14,
            ContentType.AstrophotographyTips => 15,
            _ => 12
        };

        var cron = contentType switch
        {
            ContentType.DailySkyGuide => "0 0 18 * * ?",
            ContentType.TelescopeTargets => "0 0 19 * * ?",
            ContentType.SpaceNews => "0 0 20 * * ?",
            ContentType.AstrophotographyTips => "0 0 21 * * ?",
            _ => "0 0 18 * * ?"
        };

        return new TopicSelectionSchedulingHints
        {
            ContentType = contentType,
            PreferredCronExpression = cron,
            SuggestedQueueTimeUtc = new DateTimeOffset(requestedDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(preferredHourUtc))), TimeSpan.Zero),
            Notes = primary is null
                ? "No primary topic selected; schedule with default cadence."
                : $"Primary topic '{primary.ObjectName}' ({primary.EventType}) is trend-weighted and repetition-aware; schedule near local evening window."
        };
    }

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

    private static double ComputeTimeliness(DateOnly requestedDate, DateOnly eventDate)
    {
        var dayDelta = Math.Abs(eventDate.DayNumber - requestedDate.DayNumber);
        return dayDelta switch
        {
            0 => 1.0,
            <= 1 => 0.85,
            <= 3 => 0.65,
            <= 7 => 0.45,
            _ => 0.25
        };
    }

    private static double ComputeEducationalValue(string eventType, string objectName)
        => eventType switch
        {
            "eclipse" => 0.95,
            "meteor shower" => 0.8,
            "moon phase" => 0.85,
            "conjunction" => 0.75,
            "planetary visibility" => 0.7,
            "astrophotography" => 0.7,
            "space news" => 0.65,
            _ when objectName.Contains("nebula", StringComparison.OrdinalIgnoreCase) => 0.8,
            _ => 0.55
        };

    private static double ComputeSignificance(string eventType, string details)
    {
        var rarityBoost = details.Contains("peak", StringComparison.OrdinalIgnoreCase) || details.Contains("rare", StringComparison.OrdinalIgnoreCase) ? 0.1 : 0;
        var baseScore = eventType switch
        {
            "eclipse" => 0.95,
            "meteor shower" => 0.85,
            "conjunction" => 0.8,
            "comet/asteroid" => 0.82,
            "space news" => 0.78,
            _ => 0.65
        };

        return Clamp01(baseScore + rarityBoost);
    }

    private static double ComputeGrowthPotential(ContentType contentType, TrendSnapshot trendSnapshot, string eventType, string objectName)
    {
        if (!trendSnapshot.HasHistory)
            return eventType is "meteor shower" or "conjunction" ? 0.7 : 0.5;

        var keywordBoost = trendSnapshot.GetKeywordMomentum(objectName, eventType);
        var shortBoost = contentType == ContentType.DailySkyGuide && (eventType == "conjunction" || eventType == "moon phase") ? 0.1 : 0;

        return Clamp01((trendSnapshot.NormalizedViews * 0.45) + (trendSnapshot.NormalizedRetention * 0.45) + keywordBoost + shortBoost);
    }

    private static string NormalizeEventType(string category, string details, string objectName)
    {
        var value = $"{category} {details} {objectName}".ToLowerInvariant();
        if (value.Contains("eclipse")) return "eclipse";
        if (value.Contains("meteor")) return "meteor shower";
        if (value.Contains("conjunction") || value.Contains("pairing")) return "conjunction";
        if (value.Contains("planet")) return "planetary visibility";
        if (value.Contains("moon") || value.Contains("crescent") || value.Contains("gibbous") || value.Contains("full moon")) return "moon phase";
        if (value.Contains("comet") || value.Contains("asteroid") || value.Contains("neo")) return "comet/asteroid";
        if (value.Contains("news") || value.Contains("discovery") || value.Contains("nasa")) return "space news";
        if (value.Contains("photo") || value.Contains("camera") || value.Contains("milky way")) return "astrophotography";
        return "general sky event";
    }

    private static string BuildTitleCandidate(string eventType, string objectName, DateOnly date)
        => eventType switch
        {
            "meteor shower" => $"Meteor Shower Peak Tonight: How To Watch {objectName}",
            "conjunction" => $"Shorts Hook: {objectName} Pairing Tonight ({date:MMM dd})",
            "planetary visibility" => $"Top Telescope Target Tonight: {objectName}",
            "moon phase" => $"Best Thing To Watch Tonight: {objectName}",
            "eclipse" => $"Eclipse Watch Guide: {objectName}",
            "comet/asteroid" => $"Comet/Asteroid Opportunity Tonight: {objectName}",
            "space news" => $"Space Update Explained: {objectName}",
            "astrophotography" => $"Astrophotography Target Tonight: {objectName}",
            _ => $"Night Sky Highlight: {objectName}"
        };

    private static string BuildRationale(string eventType, TopicScoreBreakdown scores)
        => $"{eventType} selected: timeliness={scores.Timeliness:F2}, observability={scores.Observability:F2}, significance={scores.Significance:F2}, educational={scores.Educational:F2}, growth={scores.Growth:F2}, diversity={scores.Diversity:F2}.";

    private sealed record TopicScoreBreakdown(
        double Priority,
        double Observability,
        double Timeliness,
        double Significance,
        double Educational,
        double Growth,
        double Diversity);

    private sealed class TrendSnapshot
    {
        private readonly IReadOnlyCollection<VideoAnalytics> _analytics;

        private TrendSnapshot(IReadOnlyCollection<VideoAnalytics> analytics, double normalizedViews, double normalizedRetention)
        {
            _analytics = analytics;
            NormalizedViews = normalizedViews;
            NormalizedRetention = normalizedRetention;
        }

        public bool HasHistory => _analytics.Count > 0;
        public double NormalizedViews { get; }
        public double NormalizedRetention { get; }

        public static TrendSnapshot Build(ContentType contentType, IReadOnlyCollection<VideoAnalytics> analytics)
        {
            if (analytics.Count == 0)
                return new TrendSnapshot([], 0.3, 0.35);

            var scopedAnalytics = analytics.Where(a => a.ContentType == contentType).ToArray();
            if (scopedAnalytics.Length == 0)
                scopedAnalytics = analytics.ToArray();

            var avgViews = scopedAnalytics.Average(x => x.Views);
            var avgRetention = scopedAnalytics
                .Where(x => x.AverageViewDurationSeconds.HasValue && x.DurationSeconds > 0)
                .Select(x => x.AverageViewDurationSeconds!.Value / x.DurationSeconds)
                .DefaultIfEmpty(0.35)
                .Average();

            var normalizedViews = avgViews <= 0 ? 0.3 : Math.Min(1, avgViews / 10_000d);
            return new TrendSnapshot(scopedAnalytics, normalizedViews, Clamp01(avgRetention));
        }

        public double GetKeywordMomentum(string objectName, string eventType)
        {
            var keywordHits = _analytics.Count(x =>
                (x.Title?.Contains(objectName, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Title?.Contains(eventType, StringComparison.OrdinalIgnoreCase) ?? false));

            return Math.Min(0.25, keywordHits * 0.05);
        }
    }

    private sealed class RepetitionIndex
    {
        private readonly IReadOnlyCollection<string> _titleSources;

        private RepetitionIndex(IReadOnlyCollection<string> titleSources) => _titleSources = titleSources;

        public static RepetitionIndex Build(IReadOnlyCollection<PublishedVideo> videos, IReadOnlyCollection<GeneratedScript> scripts)
        {
            var sources = videos.Select(v => v.Title)
                .Concat(scripts.Select(s => s.Title))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            return new RepetitionIndex(sources);
        }

        public double GetDiversityScore(string objectName, string eventType)
        {
            var normalizedObject = objectName.Trim();
            var normalizedEventType = eventType.Trim();
            var totalRepeats = _titleSources.Count(title =>
                title.Contains(normalizedObject, StringComparison.OrdinalIgnoreCase)
                || title.Contains(normalizedEventType, StringComparison.OrdinalIgnoreCase));

            return totalRepeats switch
            {
                0 => 1.0,
                1 => 0.7,
                2 => 0.45,
                _ => 0.2
            };
        }
    }
}
