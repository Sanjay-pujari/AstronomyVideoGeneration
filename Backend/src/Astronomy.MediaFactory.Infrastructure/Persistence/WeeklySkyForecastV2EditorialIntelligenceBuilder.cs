using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastV2EditorialIntelligenceBuilder : IWeeklySkyForecastV2EditorialIntelligenceBuilder
{
    public Task<WeeklyEditorialStoryPackage> BuildAsync(WeeklySkyForecastV2IntelligenceResponse intelligence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rankedEvents = intelligence.EventIntelligence
            .OrderByDescending(e => e.StoryScore)
            .ThenByDescending(e => e.VisualScore)
            .ToList();

        if (rankedEvents.Count == 0)
            throw new InvalidOperationException("At least one event is required to build editorial intelligence.");

        var heroSource = rankedEvents[0];
        var secondarySources = rankedEvents.Skip(1).Take(4).ToList();

        var hero = new WeeklyHeroEvent(
            heroSource.EventId,
            heroSource.EventType,
            heroSource.Title,
            heroSource.Description,
            heroSource.PrimaryDate,
            heroSource.BestTimeUtc,
            heroSource.ObjectCodes,
            heroSource.ObjectNames,
            heroSource.StoryScore,
            Math.Min(100d, heroSource.StoryScore + 4d),
            heroSource.VisualScore,
            heroSource.RecommendedVisualStrategy,
            $"Highest story signal ({heroSource.StoryScore:0.#}) and visual priority ({heroSource.VisualScore:0.#}) this week.",
            secondarySources.Select(s => s.PrimaryDate).Distinct().Order().ToList());

        var secondary = secondarySources.Select(s => new WeeklyHeroEvent(
            s.EventId,
            s.EventType,
            s.Title,
            s.Description,
            s.PrimaryDate,
            s.BestTimeUtc,
            s.ObjectCodes,
            s.ObjectNames,
            s.StoryScore,
            Math.Min(100d, s.StoryScore + 2d),
            s.VisualScore,
            s.RecommendedVisualStrategy,
            "Supports the weekly arc with a complementary observation opportunity."))
            .ToList();

        var beats = rankedEvents.Take(6)
            .Select((e, i) => new WeeklyNarrativeBeat(
                i + 1,
                i == 0 ? "hook" : i == rankedEvents.Take(6).Count() - 1 ? "cta" : "support",
                e.Title,
                i == 0 ? "Open on the strongest sky moment of the week." : "Build momentum through the week with practical, date-anchored guidance.",
                e.EventId,
                e.ObjectCodes,
                e.PrimaryDate,
                i == 0 ? "Awe" : "Confident",
                e.RecommendedVisualStrategy,
                e.RecommendedScenePurpose))
            .ToList();

        var cinematicMoments = rankedEvents.Take(5)
            .Select((e, i) => new WeeklyCinematicMoment(
                $"moment_{i + 1}",
                i == 0 ? "hero" : "support",
                e.Title,
                e.Description,
                e.ObjectCodes,
                e.PrimaryDate,
                e.BestTimeUtc,
                (int)Math.Round(e.VisualScore),
                e.RecommendedVisualStrategy,
                i > 0,
                e.RecommendedScenePurpose))
            .ToList();

        var topObjects = rankedEvents.SelectMany(e => e.ObjectCodes).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
        var topDates = rankedEvents.Select(e => e.PrimaryDate).Distinct().Take(4).ToList();

        var thumbnail = new WeeklyThumbnailDirection(
            [hero.Title, $"{intelligence.WeekStartDate:MMM d}-{intelligence.WeekEndDate:MMM d} Night Sky"],
            topObjects,
            secondary.SelectMany(s => s.ObjectCodes).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList(),
            "Wonder",
            hero.RecommendedVisualStrategy,
            "Place the hero object grouping in foreground with subtle horizon context.",
            "Deep twilight gradient with soft starfield contrast.",
            "Best Night This Week");

        var shorts = rankedEvents.Take(3).Select((e, i) => new WeeklyShortCandidate(
            $"short_{i + 1}",
            e.Title,
            e.Description,
            e.EventId,
            e.ObjectCodes,
            e.PrimaryDate,
            28,
            e.RecommendedVisualStrategy,
            e.StoryScore)).ToList();

        var story = new WeeklyEditorialStoryPackage(
            hero,
            secondary,
            $"{hero.Title}: Weekly Sky Forecast",
            "The strongest observing windows, organized for quick planning.",
            "This week features one clear hero moment with several strong backup opportunities.",
            "Momentum through the week anchored by visibility and timing.",
            beats,
            cinematicMoments,
            thumbnail,
            shorts,
            string.Join(", ", intelligence.RecommendedVisualStrategies),
            intelligence.Warnings);

        return Task.FromResult(story);
    }
}
