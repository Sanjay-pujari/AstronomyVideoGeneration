using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastV2NarrativeAbstractionBuilder : IWeeklySkyForecastV2NarrativeAbstractionBuilder
{
    private static readonly string[] HeadlineForbidden = ["same viewing window grouping", "grouping event", "visibility momentum", "backup opportunities", "observation event", "visibility priority"];
    private static readonly string[] ConjunctionForbidden = ["conjunction", "exact alignment", "rare alignment", "nearly touching", "extremely close"];

    public Task<WeeklyNarrativeAbstractionPackage> BuildAsync(WeeklyCinematicStoryBlueprint cinematicBlueprint, WeeklyEditorialStoryPackage editorialPackage, WeeklySkyForecastV2IntelligenceResponse intelligence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var heroObjects = cinematicBlueprint.HeroStory.ObjectCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var heroNames = cinematicBlueprint.HeroStory.ObjectNames.Any() ? cinematicBlueprint.HeroStory.ObjectNames : heroObjects;
        var heroNamesText = WeeklySkyForecastV2TextHelpers.FormatCelestialList(heroNames);
        var heroNarrativeText = $"{heroNamesText} share the western evening sky throughout the week, with one standout night that makes the whole story feel cinematic.";

        var support = cinematicBlueprint.SupportingStories
            .GroupBy(s => s.StoryCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(3)
            .Select((s, i) => new NarrativeSupportConcept($"support_{i + 1}", HumanizeTitle(s.Title), s.Purpose, s.TargetDate, s.ObjectCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), "Adds a practical angle without breaking the main weekly story.", s.RecommendedVisualStrategy, [s.SourceEventId]))
            .ToList();

        var bestNight = intelligence.SkyfieldSummary.BestPhotographyNight ?? intelligence.SkyfieldSummary.BestMoonNight ?? cinematicBlueprint.HeroStory.PeakDate;
        var moonNight = intelligence.SkyfieldSummary.BestMoonNight ?? cinematicBlueprint.HeroStory.PeakDate;

        var headline = $"{heroNamesText} light up this week's evening sky";
        var hook = "Step outside after sunset this week, and the western sky will immediately reward your attention.";

        var hero = new NarrativeHeroConcept(
            "hero_weekly_grouping_story",
            HumanizeTitle(cinematicBlueprint.HeroStory.Title),
            heroNarrativeText,
            "A single, easy-to-follow sky story with one best night and flexible backup evenings.",
            heroObjects,
            heroNames,
            cinematicBlueprint.HeroStory.PeakDate,
            cinematicBlueprint.HeroStory.SupportingDates.Distinct().Order().ToList(),
            cinematicBlueprint.HeroStory.RecommendedVisualStrategy,
            94,
            96,
            [cinematicBlueprint.HeroStory.SourceEventId]);

        var beats = new List<NarrativeFlowBeat>
        {
            new(1, "hook", "Look west after sunset", "Open with curiosity and immediate action.", "Inviting wonder", "Wide twilight orientation", heroObjects, cinematicBlueprint.HeroStory.PeakDate, 12, cinematicBlueprint.HeroStory.RecommendedVisualStrategy, false),
            new(2, "hero_sky_story", "The week belongs to one sky story", "Frame multiple dates as one continuous viewer experience.", "Awe", "Hero grouping frame", heroObjects, cinematicBlueprint.HeroStory.PeakDate, 18, cinematicBlueprint.HeroStory.RecommendedVisualStrategy, false),
            new(3, "why_this_week", "Why this week matters", "Convert raw events into emotional value and simplicity.", "Confident", "Narrative mid-shot", heroObjects, cinematicBlueprint.HeroStory.PeakDate, 14, cinematicBlueprint.HeroStory.RecommendedVisualStrategy, true),
            new(4, "best_observation_night", $"Best observation night: {bestNight:MMMM d}", "Give one clear night recommendation.", "Practical excitement", "Wide sky confirmation", heroObjects, bestNight, 12, "Stellarium", false),
            new(5, "emotional_highlight", "Moon and planet emotional highlight", "Deliver the strongest visual/emotional beat.", "Wonder", "Moon or planet hero close-up", ["MOON", "JUPITER"], moonNight, 12, "CelestialAsset", false),
            new(6, "viewing_reco", "Simple photo or viewing plan", "Provide one actionable recommendation.", "Helpful confidence", "Tripod horizon composition", heroObjects, bestNight, 11, "Hybrid", true),
            new(7, "closing_cta", "Step outside before this week ends", "Close with emotional CTA.", "Uplifting", "Return to wide cinematic sky", heroObjects, bestNight, 9, cinematicBlueprint.HeroStory.RecommendedVisualStrategy, true)
        };

        var visuals = new List<NarrativeVisualConcept>
        {
            new("hero_western_grouping", "Hero western grouping", "Primary story frame", heroObjects, $"hero:{string.Join('-', heroObjects.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}", cinematicBlueprint.HeroStory.RecommendedVisualStrategy, 100, false),
            new("best_night_wide", "Best observation night wide sky", "Confirms practical recommendation", heroObjects, $"best-night:{bestNight:yyyy-MM-dd}", "Stellarium", 92, true),
            new("jupiter_hero", "Jupiter cinematic hero", "Planet emotional detail", ["JUPITER"], "planet-hero:JUPITER", "CelestialAsset", 88, true),
            new("moon_hero", "Moon cinematic hero", "Moon emotional detail", ["MOON"], "moon-hero:MOON", "CelestialAsset", 89, true),
            new("thumbnail_story", "Thumbnail hero composition", "Marketing emotional anchor", heroObjects.Take(3).ToList(), "thumbnail:weekly-hero", "Hybrid", 95, true)
        }.GroupBy(v => v.VisualUniquenessKey, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

        var shorts = new List<NarrativeShortConcept>
        {
            new("short_story_grouping", $"{heroNamesText} this week", "Three bright objects share your evening sky this week.", "You get one simple weekly sky story that is easy to follow.", heroObjects, cinematicBlueprint.HeroStory.PeakDate, "Hero weekly grouping narrative", cinematicBlueprint.HeroStory.RecommendedVisualStrategy, 30),
            new("short_best_night", $"Best skywatching night: {bestNight:MMMM d}", "If you only watch once this week, make it this night.", "You get the highest-value observation window in one pick.", heroObjects, bestNight, "Best observation night recommendation", "Stellarium", 26),
            new("short_moon_or_planet", "Moon and Jupiter emotional highlight", "One moonlit evening this week feels cinematic on its own.", "You get a close-up emotional moment beyond the wide story.", ["MOON", "JUPITER"], moonNight, "Moon/planet emotional hero", "CelestialAsset", 24)
        };

        var thumbnail = new NarrativeThumbnailConcept(
            "Cinematic wonder with layered depth",
            heroObjects.Take(2).ToList(),
            heroObjects.Skip(2).Take(2).ToList(),
            "A glowing Moon above the western twilight with bright planets stepping down toward the horizon.",
            "Compose a large luminous Moon in the upper frame, Jupiter nearby for balance, and Venus lower for depth, so the viewer feels drawn into one continuous evening story.",
            ["LOOK WEST THIS WEEK", "MOON + BRIGHT PLANETS", "EVENING SKY STORY"],
            "LOOK WEST THIS WEEK",
            cinematicBlueprint.HeroStory.RecommendedVisualStrategy);

        var warnings = intelligence.Warnings.ToList();
        if (HeadlineForbidden.Any(p => headline.Contains(p, StringComparison.OrdinalIgnoreCase) || hook.Contains(p, StringComparison.OrdinalIgnoreCase)))
            warnings.Add("Headline or hook contains forbidden metadata phrasing.");
        if (ConjunctionForbidden.Any(p => headline.Contains(p, StringComparison.OrdinalIgnoreCase) || hook.Contains(p, StringComparison.OrdinalIgnoreCase)))
            warnings.Add("Headline or hook contains unsafe conjunction-style wording without angular separation data.");

        var package = new WeeklyNarrativeAbstractionPackage(
            Guid.NewGuid().ToString("N"),
            headline,
            cinematicBlueprint.Subtitle,
            hook,
            hero,
            support,
            beats,
            visuals,
            shorts,
            thumbnail,
            "Cinematic, emotional, conversational",
            "One cohesive weekly sky story with clear action and emotional payoff.",
            warnings);

        return Task.FromResult(package);
    }

    private static string HumanizeTitle(string value)
        => value.Replace("same viewing window grouping", "shared evening sky story", StringComparison.OrdinalIgnoreCase);
}
