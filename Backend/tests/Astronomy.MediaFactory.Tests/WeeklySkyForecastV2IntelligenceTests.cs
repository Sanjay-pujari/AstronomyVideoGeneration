using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklySkyForecastV2IntelligenceTests
{
    [Fact]
    public async Task V2_Intelligence_Generates_Cinematic_Blueprint()
    {
        var service = new WeeklySkyForecastV2IntelligenceService(new StubContextBuilder(), new WeeklySkyForecastV2EventIntelligenceBuilder(), new WeeklySkyForecastV2EditorialIntelligenceBuilder(), new WeeklySkyForecastV2CinematicEditorialRefiner(), new WeeklySkyForecastV2NarrativeAbstractionBuilder(), new WeeklySkyForecastV2NarrationPlanner(), new WeeklySkyForecastV2NarrationTextGenerator());
        var response = await service.PreviewAsync(new WeeklySkyForecastV2IntelligenceRequest("WeeklySkyForecast", "en", "IN-RJ-UDAIPUR", "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), Diagnostics: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.CinematicStoryBlueprint);
        Assert.NotNull(response.NarrativeAbstractionPackage);
        Assert.NotNull(response.NarrationPlan);
        Assert.NotNull(response.GeneratedNarrationPackage);
        Assert.NotNull(response.NarrationQuality);
        Assert.NotNull(response.VisualRequirementPackage);
        Assert.NotNull(response.HybridScenePlanPackage);
        Assert.NotNull(response.EditorialStoryPackage);
        Assert.DoesNotContain("Same viewing window grouping", response.CinematicStoryBlueprint!.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Moon", response.CinematicStoryBlueprint.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.CinematicStoryBlueprint.NarrativeBeats.Count >= 6);
        Assert.Equal(3, response.CinematicStoryBlueprint.ShortsBlueprints.Count);
        Assert.Equal(7, response.NarrativeAbstractionPackage!.NarrativeFlow.Count);
        Assert.Equal(3, response.NarrativeAbstractionPackage.ShortsNarrativePlan.Count);
        Assert.Equal(3, response.NarrationPlan!.ShortsPlan.Shorts.Count);
        Assert.Equal(3, response.GeneratedNarrationPackage!.ShortNarrations.Count);
        Assert.DoesNotContain("same viewing window", response.GeneratedNarrationPackage.LongFormNarration.FullNarration, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.NarrationQuality!.ShortCtaUniquenessValid);
        Assert.InRange(response.VisualRequirementPackage!.VisualRequirements.Count, 4, 6);
        Assert.Contains(response.VisualRequirementPackage.VisualRequirements, v => v.VisualSourceType == "Hybrid");
        Assert.Contains(response.VisualRequirementPackage.VisualRequirements, v => v.VisualSourceType == "Stellarium");
        Assert.Contains(response.VisualRequirementPackage.VisualRequirements, v => v.VisualSourceType == "CelestialAsset");
        Assert.Equal(response.NarrationPlan.LongFormPlan.Segments.Count, response.VisualRequirementPackage.SegmentVisualMappings.Count);
        Assert.InRange(response.HybridScenePlanPackage!.ScenePlans.Count, 4, 6);
        Assert.Contains(response.HybridScenePlanPackage.ScenePlans, s => s.VisualSourceType == "Hybrid");
        Assert.Contains(response.HybridScenePlanPackage.ScenePlans, s => s.VisualSourceType == "Stellarium");
        Assert.Contains(response.HybridScenePlanPackage.ScenePlans, s => s.VisualSourceType == "CelestialAsset");
        Assert.Contains(response.HybridScenePlanPackage.AssetNeeds, a => a.ObjectCode == "MOON");
        Assert.Contains(response.HybridScenePlanPackage.AssetNeeds, a => a.ObjectCode == "JUPITER");
        Assert.Contains(response.HybridScenePlanPackage.AssetNeeds, a => a.ObjectCode == "VENUS");
    }

    [Fact]
    public async Task V2_Cinematic_Refiner_Collapses_Repeated_Grouping_And_Keeps_Unique_Moments_And_Shorts()
    {
        var intelligence = BuildResponse(new WeeklySkyForecastV2EventIntelligenceBuilder().Build(BuildContext()));
        var editorial = await new WeeklySkyForecastV2EditorialIntelligenceBuilder().BuildAsync(intelligence, CancellationToken.None);
        var cinematic = await new WeeklySkyForecastV2CinematicEditorialRefiner().RefineAsync(editorial, intelligence with { EditorialStoryPackage = editorial }, CancellationToken.None);
        var narrative = await new WeeklySkyForecastV2NarrativeAbstractionBuilder().BuildAsync(cinematic, editorial, intelligence with { EditorialStoryPackage = editorial, CinematicStoryBlueprint = cinematic }, CancellationToken.None);

        Assert.True(cinematic.HeroStory.SupportingDates.Count > 1);
        Assert.Equal(cinematic.CinematicMoments.Count, cinematic.CinematicMoments.Select(x => x.VisualUniquenessKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(3, cinematic.ShortsBlueprints.Count);
        Assert.Equal(3, cinematic.ShortsBlueprints.Select(x => x.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(cinematic.OpeningHook, new[] { "conjunction", "exact alignment", "rare alignment", "almost touching" }, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(narrative.CinematicVisualPlan.Count, narrative.CinematicVisualPlan.Select(x => x.VisualUniquenessKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(3, narrative.ShortsNarrativePlan.Select(x => x.DistinctStoryAngle).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(narrative.StoryHeadline, new[] { "same viewing window grouping", "grouping event", "visibility momentum" }, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(narrative.OpeningNarrationHook, new[] { "conjunction", "exact alignment", "rare alignment", "nearly touching", "extremely close" }, StringComparer.OrdinalIgnoreCase);
    }

    private static WeeklySkyForecastV2IntelligenceResponse BuildResponse(IReadOnlyList<WeeklySkyForecastV2EventIntelligenceItem> events)
    {
        return new WeeklySkyForecastV2IntelligenceResponse(null, "WeeklySkyForecast", true, new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), "IN-RJ-UDAIPUR",
            new WeeklySkyForecastV2SkyfieldSummary(7, 21, 0, 1, "JUPITER", new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 25)),
            events,
            new WeeklyStoryArc("h", "s", "t", "o", ["a"], "c", ["MOON"], ["2026-05-24"], ["x"]),
            null!,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            ["Hybrid"],
            [],
            []);
    }

    [Fact]
    public async Task V2_NarrationPlan_Has_Expected_Segments_Durations_And_Strategies()
    {
        var intelligence = BuildResponse(new WeeklySkyForecastV2EventIntelligenceBuilder().Build(BuildContext()));
        var editorial = await new WeeklySkyForecastV2EditorialIntelligenceBuilder().BuildAsync(intelligence, CancellationToken.None);
        var cinematic = await new WeeklySkyForecastV2CinematicEditorialRefiner().RefineAsync(editorial, intelligence with { EditorialStoryPackage = editorial }, CancellationToken.None);
        var narrative = await new WeeklySkyForecastV2NarrativeAbstractionBuilder().BuildAsync(cinematic, editorial, intelligence with { EditorialStoryPackage = editorial, CinematicStoryBlueprint = cinematic }, CancellationToken.None);
        var narration = await new WeeklySkyForecastV2NarrationPlanner().BuildAsync(narrative, cinematic, intelligence.SkyfieldSummary, intelligence.Region, intelligence.WeekStartDate, "en", CancellationToken.None);

        Assert.NotNull(narration);
        Assert.InRange(narration.LongFormPlan.SegmentCount, 6, 7);
        Assert.Equal(new[] { "OpeningHook", "HeroSkyStory", "WhyThisWeekMatters", "BestObservationNight", "MoonPlanetHighlight", "ViewingPhotographyTip", "ClosingCTA" }, narration.LongFormPlan.Segments.Select(x => x.SegmentCode).ToArray());
        Assert.Equal(3, narration.ShortsPlan.Shorts.Count);
        Assert.All(narration.LongFormPlan.Segments, s => Assert.False(string.IsNullOrWhiteSpace(s.RecommendedVisualStrategy)));
        Assert.All(narration.LongFormPlan.Segments, s => Assert.DoesNotContain(s.NarrationPromptHints, h => h.Contains("conjunction", StringComparison.OrdinalIgnoreCase) && h.Contains("claim", StringComparison.OrdinalIgnoreCase)));
        Assert.InRange(narration.LongFormPlan.TargetDurationSeconds, 90, 150);
        Assert.All(narration.LongFormPlan.Segments, s => Assert.True(s.EstimatedDurationSeconds > 0));
        Assert.All(narration.LongFormPlan.Segments, s => Assert.False(string.IsNullOrWhiteSpace(s.SourceBeatCode)));
        Assert.Equal(narration.LongFormPlan.Segments.Count, narration.LongFormPlan.Segments.Select(s => string.Join("|", s.NarrationPromptHints)).Distinct().Count());
    }

    private static WeeklySkyForecastContext BuildContext()
    {
        var start = new DateOnly(2026, 5, 22);
        var days = Enumerable.Range(0, 7).Select(i =>
        {
            var d = start.AddDays(i);
            var t = DateTime.Parse($"{d:yyyy-MM-dd}T18:00:00Z");
            return new DailySkyForecastContextItem(d, t, t, "Waxing", 33, null, null,
                [new WeeklySkyForecastVisibleObjectItem("MOON", "Moon", "Moon", true, null, null, null, 55, t, 90, 80, "W", "Good"), new WeeklySkyForecastVisibleObjectItem("JUPITER", "Jupiter", "Planet", true, null, null, null, 60, t.AddMinutes(20), 92, 88, "W", "Great"), new WeeklySkyForecastVisibleObjectItem("VENUS", "Venus", "Planet", true, null, null, null, 40, t.AddMinutes(30), 85, 82, "W", "Great")],
                [], t, t.AddHours(2), 95 - i, "Excellent");
        }).ToList();

        return new WeeklySkyForecastContext("IN-RJ-UDAIPUR", "Udaipur", 24, 73, "Asia/Kolkata", start, start.AddDays(6), "en", days, [], [new RecommendedObservationNight(new DateOnly(2026, 5, 24), 95, "Best", ["MOON", "JUPITER", "VENUS"], DateTime.Parse("2026-05-24T18:00:00Z"), DateTime.Parse("2026-05-24T20:00:00Z"))], "JUPITER", new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 25), []);
    }

    private sealed class StubContextBuilder : IWeeklySkyForecastContextBuilder
    {
        public Task<WeeklySkyForecastContext> BuildAsync(WeeklySkyForecastProductionRequest request, CancellationToken cancellationToken) => Task.FromResult(BuildContext());
    }
}
