using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastV2CinematicEditorialRefiner : IWeeklySkyForecastV2CinematicEditorialRefiner
{
    private static readonly string[] ForbiddenPhrases = ["conjunction", "exact alignment", "rare alignment", "very close together", "almost touching"];

    public Task<WeeklyCinematicStoryBlueprint> RefineAsync(WeeklyEditorialStoryPackage editorialPackage, WeeklySkyForecastV2IntelligenceResponse intelligence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var groupedEvents = intelligence.EventIntelligence
            .Where(e => e.Source == "same_window_grouping_only_no_angular_separation")
            .GroupBy(e => string.Join('-', e.ObjectCodes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Max(e => e.StoryScore))
            .FirstOrDefault();

        var heroSource = groupedEvents?.OrderByDescending(e => e.StoryScore).ThenByDescending(e => e.VisualScore).FirstOrDefault()
            ?? intelligence.EventIntelligence.OrderByDescending(e => e.StoryScore).First();
        var supportingDates = groupedEvents?.Select(e => e.PrimaryDate).Distinct().Order().ToList() ?? [heroSource.PrimaryDate];
        var heroObjects = heroSource.ObjectCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var heroNames = heroSource.VisibleObjectNames.Any() ? heroSource.VisibleObjectNames : heroObjects;
        var heroObjectText = string.Join(", ", heroNames);

        var headline = $"{heroObjectText} light up this week's evening sky";
        var openingHook = $"If you look west after sunset this week, {heroObjectText} share the same evening sky.";

        var heroStory = new WeeklyHeroStory(
            $"{heroObjectText} this week",
            "Visible across several evenings with one strongest observation night.",
            heroSource.PrimaryDate,
            supportingDates,
            heroSource.BestTimeUtc,
            heroObjects,
            heroNames,
            "A single repeating sky story that peaks once and stays useful all week.",
            "You only need one strong evening plan, with backup nights available.",
            "Wide sunset composition with a clear object hierarchy.",
            heroSource.RecommendedVisualStrategy,
            heroSource.EventId);

        var support = intelligence.EventIntelligence
            .Where(e => e.EventId != heroSource.EventId && e.Source != "same_window_grouping_only_no_angular_separation")
            .Take(3)
            .Select((e, i) => new WeeklySupportingStory($"support_{i + 1}", e.Title, e.Description, e.PrimaryDate, e.ObjectCodes, "Adds practical planning value without duplicating the hero grouping.", e.RecommendedVisualStrategy, e.EventId))
            .ToList();

        var bestNight = intelligence.SkyfieldSummary.BestPhotographyNight ?? intelligence.SkyfieldSummary.BestMoonNight ?? heroSource.PrimaryDate;
        var beats = new List<WeeklyCinematicNarrativeBeat>
        {
            new(1,"hook","Look west after sunset this week","Open with curiosity and immediate action.","Inviting",heroStory.SourceEventId,heroSource.PrimaryDate,heroObjects,heroSource.RecommendedVisualStrategy,"opening",false,12),
            new(2,"hero_story",$"{heroObjectText} share the evening sky","Frame the week around one hero sky story.","Awe",heroStory.SourceEventId,heroSource.PrimaryDate,heroObjects,heroSource.RecommendedVisualStrategy,"hero",false,18),
            new(3,"why_it_matters","Why this week is worth your time","Convert visibility into clear viewer benefit.","Confident",heroStory.SourceEventId,heroSource.PrimaryDate,heroObjects,heroSource.RecommendedVisualStrategy,"explain",true,14),
            new(4,"best_night",$"Best overall night: {bestNight:MMMM d}","Recommend one night if viewer only goes out once.","Practical",heroStory.SourceEventId,bestNight,heroObjects,heroSource.RecommendedVisualStrategy,"wide",false,12),
            new(5,"highlight","Moon and planet highlight","Call out the strongest moon/planet visual moment.","Wonder",heroStory.SourceEventId,intelligence.SkyfieldSummary.BestMoonNight ?? heroSource.PrimaryDate,["MOON"],"CelestialAsset","asset",false,12),
            new(6,"tip","Simple viewing or photo tip","Give one easy setup tip for better results.","Helpful",heroStory.SourceEventId,bestNight,heroObjects,"Hybrid","tip",true,10),
            new(7,"cta","Step outside this week","Close with a clear and motivating CTA.","Uplifting",heroStory.SourceEventId,bestNight,heroObjects,heroSource.RecommendedVisualStrategy,"closing",true,8)
        };

        var moments = new List<WeeklyCinematicMomentBlueprint>
        {
            new("hero_grouping","Hero grouping visual",heroStory.Description,heroObjects,heroSource.PrimaryDate,heroSource.BestTimeUtc,"wide",heroSource.RecommendedVisualStrategy,"hero_grouping",false,$"grouping:{string.Join('-', heroObjects.OrderBy(x=>x))}"),
            new("best_night_wide","Best observation night wide sky","One complete sky composition for the best night.",heroObjects,bestNight,heroSource.BestTimeUtc,"wide",heroSource.RecommendedVisualStrategy,"best_night",true,$"wide:best-night:{bestNight:yyyy-MM-dd}"),
            new("jupiter_asset","Jupiter hero asset","Dedicated Jupiter visual asset.",["JUPITER"],bestNight,null,"asset","CelestialAsset","planet_asset",true,"asset:JUPITER"),
            new("moon_asset","Moon hero asset","Dedicated Moon visual asset.",["MOON"],intelligence.SkyfieldSummary.BestMoonNight ?? heroSource.PrimaryDate,null,"asset","CelestialAsset","moon_asset",true,"asset:MOON"),
            new("thumbnail_hero","Thumbnail composition","Primary thumbnail framing for this week.",heroObjects,heroSource.PrimaryDate,heroSource.BestTimeUtc,"thumbnail","Hybrid","thumbnail",true,"thumbnail:hero-grouping")
        };

        var shorts = new List<WeeklyShortBlueprint>
        {
            new("short_hero",$"{heroObjectText} this week","Look west after sunset — this week's hero sky story is already waiting.","Hero event",heroObjects,heroSource.PrimaryDate,heroSource.RecommendedVisualStrategy,30,95),
            new("short_best_night","Best night to watch the sky",$"If you only go outside once this week, pick {bestNight:MMMM d}.","Best night recommendation",heroObjects,bestNight,"Stellarium",25,90),
            new("short_moon","Best Moon night this week","The Moon reaches its strongest visual moment near the end of the week.","Moon highlight",["MOON"],intelligence.SkyfieldSummary.BestMoonNight ?? heroSource.PrimaryDate,"CelestialAsset",25,88)
        };

        var thumbnail = new WeeklyThumbnailBlueprint(["LOOK WEST THIS WEEK", "MOON + PLANETS"], heroObjects.Take(2).ToList(), heroObjects.Skip(2).Take(2).ToList(), "Urgent wonder", "Large glowing Moon on one side with bright planets set in twilight gradient.", "Deep blue to amber dusk horizon glow.", "LOOK WEST THIS WEEK", heroSource.RecommendedVisualStrategy);

        var warnings = intelligence.Warnings.ToList();
        if (ForbiddenPhrases.Any(x => headline.Contains(x, StringComparison.OrdinalIgnoreCase) || openingHook.Contains(x, StringComparison.OrdinalIgnoreCase)))
            warnings.Add("Safety wording check failed in generated headline or hook.");

        var blueprint = new WeeklyCinematicStoryBlueprint(Guid.NewGuid().ToString("N"), headline, editorialPackage.Subtitle, openingHook,
            "One human-readable weekly sky story with unique beats, moments, and shorts.", heroStory, support, beats,
            moments.GroupBy(m => m.VisualUniquenessKey, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList(),
            shorts, thumbnail, "Cinematic and conversational", "Wide-to-detail visual progression", warnings);

        return Task.FromResult(blueprint);
    }
}
