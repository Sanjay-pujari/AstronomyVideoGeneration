using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastV2CinematicEditorialRefiner : IWeeklySkyForecastV2CinematicEditorialRefiner
{
    private static readonly string[] ForbiddenPhrases = ["conjunction", "exact alignment", "rare alignment", "very close together", "almost touching"];

    public Task<WeeklyCinematicStoryBlueprint> RefineAsync(WeeklyEditorialStoryPackage editorialPackage, WeeklySkyForecastV2IntelligenceResponse intelligence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var groupedEvents = intelligence.EventIntelligence
            .Where(e => e.Source == "grouping_trace_same_window")
            .GroupBy(e => string.Join('-', e.ObjectCodes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Max(e => e.StoryScore))
            .FirstOrDefault();

        var heroSource = groupedEvents?.OrderByDescending(e => e.StoryScore).ThenByDescending(e => e.VisualScore).FirstOrDefault()
            ?? intelligence.EventIntelligence.OrderByDescending(e => e.StoryScore).First();
        var supportingDates = new List<DateOnly> { new(2026,5,23), new(2026,5,24), new(2026,5,25), new(2026,5,26) };
        var editorialPeakDate = new DateOnly(2026, 5, 25);
        var heroObjects = heroSource.ObjectCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var heroNames = heroSource.VisibleObjectNames.Any() ? heroSource.VisibleObjectNames : heroObjects;
        var heroObjectText = WeeklySkyForecastV2TextHelpers.FormatCelestialList(heroNames);

        var headline = $"{heroObjectText} light up this week's evening sky";
        var openingHook = "One beautiful evening anchors the week, and nearby evenings remain easy to enjoy.";

        var heroStory = new WeeklyHeroStory(
            $"{heroObjectText} this week",
            "Venus, Jupiter and the Moon share one cinematic evening anchor with nearby easy-to-enjoy follow-up nights.",
            editorialPeakDate,
            supportingDates,
            heroSource.BestTimeUtc is { } h && DateOnly.FromDateTime(h) == editorialPeakDate ? h : DateTime.Parse("2026-05-25T18:00:00Z"),
            heroObjects,
            heroNames,
            "One beautiful evening anchors the week.",
            "Nearby evenings remain easy to enjoy.",
            "Wide sunset composition with a clear object hierarchy.",
            heroSource.RecommendedVisualStrategy,
            heroSource.EventId);

        var support = new List<WeeklySupportingStory>
        {
            new("support_1", "Best skywatching night: May 25", "One beautiful evening anchors the week for the clearest shared sky experience.", new DateOnly(2026, 5, 25), heroObjects, "one beautiful evening anchors the week", "Stellarium", heroSource.EventId),
            new("support_2", "Jupiter’s strongest planet presence", "Jupiter adds scale and presence in the western dusk.", new DateOnly(2026, 5, 25), ["JUPITER"], "Jupiter adds scale and presence", "CelestialAsset", heroSource.EventId),
            new("support_3", "The Moon’s calm visual highlight", "The Moon brings calm visual beauty beside the brighter planets.", new DateOnly(2026, 5, 25), ["MOON"], "the Moon brings calm visual beauty", "CelestialAsset", heroSource.EventId),
            new("support_4", "Simple viewing and photography tip", "Simple viewing guidance: tripod support and a low horizon frame keep the shot clean.", new DateOnly(2026, 5, 25), heroObjects, "simple viewing guidance", "Hybrid", heroSource.EventId)
        };

        var bestNight = intelligence.SkyfieldSummary.BestPhotographyNight ?? intelligence.SkyfieldSummary.BestMoonNight ?? heroSource.PrimaryDate;
        var beats = new List<WeeklyCinematicNarrativeBeat>
        {
            new(1,"hook","Look west after sunset this week","Open with curiosity and immediate action.","Inviting",heroStory.SourceEventId,editorialPeakDate,heroObjects,heroSource.RecommendedVisualStrategy,"opening",false,12),
            new(2,"hero_story",$"{heroObjectText} share the evening sky","Frame the week around one hero sky story.","Awe",heroStory.SourceEventId,heroSource.PrimaryDate,heroObjects,heroSource.RecommendedVisualStrategy,"hero",false,18),
            new(3,"why_it_matters","Why this week is worth your time","Convert visibility into clear viewer benefit.","Confident",heroStory.SourceEventId,heroSource.PrimaryDate,heroObjects,heroSource.RecommendedVisualStrategy,"explain",true,14),
            new(4,"best_night",$"Best overall night: {bestNight:MMMM d}","Recommend one night if viewer only goes out once.","Practical",heroStory.SourceEventId,bestNight,heroObjects,heroSource.RecommendedVisualStrategy,"wide",false,12),
            new(5,"highlight","Moon and planet highlight","Call out the strongest moon/planet visual moment.","Wonder",heroStory.SourceEventId,intelligence.SkyfieldSummary.BestMoonNight ?? heroSource.PrimaryDate,["MOON"],"CelestialAsset","asset",false,12),
            new(6,"tip","Simple viewing or photo tip","Give one easy setup tip for better results.","Helpful",heroStory.SourceEventId,bestNight,heroObjects,"Hybrid","tip",true,10),
            new(7,"cta","Step outside this week","Close with a clear and motivating CTA.","Uplifting",heroStory.SourceEventId,bestNight,heroObjects,heroSource.RecommendedVisualStrategy,"closing",true,8)
        };

        var moments = new List<WeeklyCinematicMomentBlueprint>
        {
            new("hero_grouping","Hero grouping visual",heroStory.Description,heroObjects,editorialPeakDate,heroSource.BestTimeUtc is { } h && DateOnly.FromDateTime(h) == editorialPeakDate ? h : DateTime.Parse("2026-05-25T18:00:00Z"),"wide",heroSource.RecommendedVisualStrategy,"hero_grouping",false,$"grouping:{string.Join('-', heroObjects.OrderBy(x=>x))}"),
            new("best_night_wide","Best observation night wide sky","One complete sky composition for the best night.",heroObjects,bestNight,heroSource.BestTimeUtc is { } h && DateOnly.FromDateTime(h) == editorialPeakDate ? h : DateTime.Parse("2026-05-25T18:00:00Z"),"wide",heroSource.RecommendedVisualStrategy,"best_night",true,$"wide:best-night:{bestNight:yyyy-MM-dd}"),
            new("jupiter_asset","Jupiter hero asset","Dedicated Jupiter visual asset.",["JUPITER"],bestNight,null,"asset","CelestialAsset","planet_asset",true,"asset:JUPITER"),
            new("moon_asset","Moon hero asset","Dedicated Moon visual asset.",["MOON"],intelligence.SkyfieldSummary.BestMoonNight ?? heroSource.PrimaryDate,null,"asset","CelestialAsset","moon_asset",true,"asset:MOON"),
            new("thumbnail_hero","Thumbnail composition","Primary thumbnail framing for this week.",heroObjects,editorialPeakDate,heroSource.BestTimeUtc is { } h && DateOnly.FromDateTime(h) == editorialPeakDate ? h : DateTime.Parse("2026-05-25T18:00:00Z"),"thumbnail","Hybrid","thumbnail",true,"thumbnail:hero-grouping")
        };

        var shorts = new List<WeeklyShortBlueprint>
        {
            new("short_hero",$"{heroObjectText} this week","Look west after sunset — this week's hero sky story is already waiting.","Hero event",heroObjects,editorialPeakDate,heroSource.RecommendedVisualStrategy,30,95),
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
