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
        var recentVideos = await _repository.GetRecentPublishedVideosAsync(DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _options.RepetitionWindowDays)), cancellationToken);
        var recentScripts = await _repository.GetRecentGeneratedScriptsAsync(DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _options.RepetitionWindowDays)), cancellationToken);

        var opportunities = new List<ContentOpportunity>();

        foreach (var ev in context.Events)
        {
            var eventType = NormalizeEventType(ev.Category, ev.Details, ev.ObjectName);
            var observability = Clamp01(ev.Score);
            var timeliness = ComputeTimeliness(request.Date, context.Date);
            var educational = ComputeEducationalValue(eventType, ev.ObjectName);
            var growth = ComputeGrowthPotential(requestedType, analytics, eventType, ev.ObjectName);
            var significance = ComputeSignificance(eventType, ev.Details);
            var diversity = ComputeDiversityPenalty(ev.ObjectName, eventType, recentVideos, recentScripts);

            var priority =
                _options.TimelinessWeight * timeliness +
                _options.ObservabilityWeight * observability +
                _options.SignificanceWeight * significance +
                _options.EducationalValueWeight * educational +
                _options.GrowthPotentialWeight * growth +
                _options.DiversityWeight * diversity;

            opportunities.Add(new ContentOpportunity
            {
                TitleCandidate = BuildTitleCandidate(eventType, ev.ObjectName, request.Date),
                ContentType = requestedType,
                EventType = eventType,
                ObjectName = string.IsNullOrWhiteSpace(ev.ObjectName) ? "Night Sky" : ev.ObjectName,
                Date = request.Date,
                PriorityScore = Math.Round(priority, 4),
                ObservabilityScore = Math.Round(observability, 4),
                TimelinessScore = Math.Round(timeliness, 4),
                EducationalValueScore = Math.Round(educational, 4),
                GrowthPotentialScore = Math.Round(growth, 4),
                Rationale = BuildRationale(eventType, timeliness, observability, educational, growth, diversity),
                IsShortCandidate = timeliness >= 0.5 || eventType is "conjunction" or "moon phase" or "meteor shower",
                IsLongFormCandidate = educational >= 0.5 || eventType is "eclipse" or "space news" or "astrophotography"
            });
        }

        if (opportunities.Count == 0)
        {
            opportunities.Add(new ContentOpportunity
            {
                TitleCandidate = $"Best Thing To Watch Tonight - {request.Date:MMM dd}",
                ContentType = requestedType,
                EventType = "fallback",
                ObjectName = "Night Sky Highlights",
                Date = request.Date,
                PriorityScore = 0.55,
                ObservabilityScore = 0.45,
                TimelinessScore = 0.7,
                EducationalValueScore = 0.5,
                GrowthPotentialScore = 0.4,
                Rationale = "Fallback opportunity: sparse event data, using broad nightly observing recommendation.",
                IsLongFormCandidate = true,
                IsShortCandidate = true
            });
        }

        var ranked = opportunities.OrderByDescending(x => x.PriorityScore).ThenBy(x => x.TitleCandidate).Take(Math.Max(1, request.MaxCandidates)).ToArray();
        var primary = ranked.FirstOrDefault(x => x.IsLongFormCandidate) ?? ranked.First();
        var shorts = ranked.Where(x => x.IsShortCandidate).Take(3).ToArray();
        var alternates = ranked.Where(x => x.Id != primary.Id).Take(3).ToArray();

        return new TopicSelectionPlan
        {
            PrimaryLongForm = primary,
            ShortsCandidates = shorts,
            AlternateCandidates = alternates,
            RankedOpportunities = ranked
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

    private static double ComputeGrowthPotential(ContentType contentType, IReadOnlyCollection<VideoAnalytics> analytics, string eventType, string objectName)
    {
        if (analytics.Count == 0)
            return eventType is "meteor shower" or "conjunction" ? 0.7 : 0.5;

        var avgViews = analytics.Average(x => x.Views);
        var avgRetention = analytics.Where(x => x.AverageViewDurationSeconds.HasValue && x.DurationSeconds > 0)
            .Select(x => x.AverageViewDurationSeconds!.Value / x.DurationSeconds)
            .DefaultIfEmpty(0.35)
            .Average();

        var keywordBoost = analytics.Count(x =>
            (x.Title?.Contains(objectName, StringComparison.OrdinalIgnoreCase) ?? false)
            || (x.Title?.Contains(eventType, StringComparison.OrdinalIgnoreCase) ?? false));

        var normalizedViews = avgViews <= 0 ? 0.3 : Math.Min(1, avgViews / 10_000d);
        var normalizedRetention = Clamp01(avgRetention);
        var normalizedKeyword = Math.Min(0.25, keywordBoost * 0.05);
        var shortBoost = contentType == ContentType.DailySkyGuide && (eventType == "conjunction" || eventType == "moon phase") ? 0.1 : 0;

        return Clamp01((normalizedViews * 0.45) + (normalizedRetention * 0.45) + normalizedKeyword + shortBoost);
    }

    private static double ComputeDiversityPenalty(string objectName, string eventType, IReadOnlyCollection<PublishedVideo> recentVideos, IReadOnlyCollection<GeneratedScript> recentScripts)
    {
        var repeatsInTitles = recentVideos.Count(x =>
            x.Title.Contains(objectName, StringComparison.OrdinalIgnoreCase)
            || x.Title.Contains(eventType, StringComparison.OrdinalIgnoreCase));

        var repeatsInScripts = recentScripts.Count(x =>
            x.Title.Contains(objectName, StringComparison.OrdinalIgnoreCase)
            || x.Title.Contains(eventType, StringComparison.OrdinalIgnoreCase));

        var totalRepeats = repeatsInTitles + repeatsInScripts;
        return totalRepeats switch
        {
            0 => 1.0,
            1 => 0.7,
            2 => 0.45,
            _ => 0.2
        };
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

    private static string BuildRationale(string eventType, double timeliness, double observability, double educational, double growth, double diversity)
        => $"{eventType} selected: timeliness={timeliness:F2}, observability={observability:F2}, educational={educational:F2}, growth={growth:F2}, diversity={diversity:F2}.";
}
