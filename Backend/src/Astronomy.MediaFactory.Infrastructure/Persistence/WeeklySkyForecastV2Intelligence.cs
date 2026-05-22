using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastV2EventIntelligenceBuilder : IWeeklySkyForecastV2EventIntelligenceBuilder
{
    public IReadOnlyList<WeeklySkyForecastV2EventIntelligenceItem> Build(WeeklySkyForecastContext context)
    {
        var events = new List<WeeklySkyForecastV2EventIntelligenceItem>();
        var bestNight = context.RecommendedNights.OrderByDescending(x => x.Score).FirstOrDefault();
        if (bestNight is not null)
            events.Add(BuildEvent("best_overall_night", "Best overall night this week", bestNight.Date, bestNight.BestStartUtc, bestNight.BestObjects, context, 92, "Stellarium", "weekly_best_night", "recommendedNights"));

        if (!string.IsNullOrWhiteSpace(context.BestPlanetOfWeek))
        {
            var p = context.BestPlanetOfWeek!;
            var hit = context.DailyForecasts.SelectMany(d => d.VisibleObjects).FirstOrDefault(o => o.Visible && o.ObjectCode.Equals(p, StringComparison.OrdinalIgnoreCase));
            events.Add(BuildEvent("best_planet", $"{hit?.ObjectName ?? p} leads this week", hit is null ? context.WeekStartDate : context.DailyForecasts.First(d=>d.VisibleObjects.Contains(hit)).Date, hit?.BestViewingTimeUtc, [p], context, 84, "CelestialAsset", "planet_hero", "bestPlanetOfWeek"));
        }

        if (context.BestMoonNight is not null)
            events.Add(BuildEvent("best_moon_night", "Best moon night", context.BestMoonNight.Value, context.RecommendedNights.FirstOrDefault(n=>n.Date==context.BestMoonNight.Value)?.BestStartUtc, ["MOON"], context, 82, "CelestialAsset", "moon_hero", "bestMoonNight"));

        if (context.BestPhotographyNight is not null)
            events.Add(BuildEvent("photography_window", "Top photography window", context.BestPhotographyNight.Value, context.RecommendedNights.FirstOrDefault(n=>n.Date==context.BestPhotographyNight.Value)?.BestStartUtc, context.RecommendedNights.FirstOrDefault(n=>n.Date==context.BestPhotographyNight.Value)?.BestObjects ?? [], context, 75, "Stellarium", "photography_tip", "bestPhotographyNight"));

        AddGroupingEvents(context, events);

        var deduped = events
            .GroupBy(e => $"{e.EventType}|{e.PrimaryDate}|{string.Join(',', e.ObjectCodes.Order())}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.StoryScore).First())
            .OrderByDescending(e => e.StoryScore)
            .Take(8)
            .ToList();
        return deduped;
    }

    private static void AddGroupingEvents(WeeklySkyForecastContext context, List<WeeklySkyForecastV2EventIntelligenceItem> events)
    {
        foreach (var day in context.DailyForecasts)
        {
            var visible = day.VisibleObjects.Where(v => v.Visible && v.BestViewingTimeUtc.HasValue).ToList();
            var moon = visible.FirstOrDefault(v => v.ObjectCode.Equals("MOON", StringComparison.OrdinalIgnoreCase));
            var bright = visible.Where(v => v.ObjectCode is "JUPITER" or "VENUS" or "SATURN").ToList();
            if (moon is not null && bright.Any())
            {
                var close = bright.Where(b => Math.Abs((b.BestViewingTimeUtc!.Value - moon.BestViewingTimeUtc!.Value).TotalMinutes) <= 90).Take(2).ToList();
                if (close.Any())
                {
                    var codes = close.Select(x => x.ObjectCode).Append("MOON").Distinct().ToList();
                    events.Add(BuildEvent(codes.Count >= 3 ? "planetary_grouping" : "moon_planet_pairing", "Same viewing window grouping", day.Date, moon.BestViewingTimeUtc, codes, context, 90, "Hybrid", "grouping_story", "same_window_grouping_only_no_angular_separation"));
                }
            }
        }
    }

    private static WeeklySkyForecastV2EventIntelligenceItem BuildEvent(string type, string title, DateOnly date, DateTime? bestTimeUtc, IReadOnlyList<string> objectCodes, WeeklySkyForecastContext context, double baseStory, string visualStrategy, string scenePurpose, string source)
    {
        var objs = context.DailyForecasts.Where(d => d.Date == date).SelectMany(d => d.VisibleObjects).Where(o => objectCodes.Contains(o.ObjectCode, StringComparer.OrdinalIgnoreCase)).ToList();
        var visibleNames = objs.Select(o => o.ObjectName).Distinct().ToList();
        var hasMoon = objectCodes.Any(o => o.Equals("MOON", StringComparison.OrdinalIgnoreCase));
        var brightBonus = objectCodes.Count(o => o is "JUPITER" or "VENUS" or "SATURN") * 4;
        var importance = Math.Min(100, 50 + objectCodes.Count * 8 + (hasMoon ? 8 : 0) + brightBonus);
        var visual = Math.Min(100, (type.Contains("group", StringComparison.OrdinalIgnoreCase) ? 85 : 65) + (hasMoon ? 8 : 0) + brightBonus);
        var story = Math.Min(100, baseStory + (type.Contains("group", StringComparison.OrdinalIgnoreCase) ? 6 : 0));
        var rarity = type.Contains("group", StringComparison.OrdinalIgnoreCase) ? 70 : 55;
        var description = type.Contains("group", StringComparison.OrdinalIgnoreCase)
            ? "Objects share the same viewing window grouping. This is not labeled as a conjunction without angular separation data."
            : "High-value weekly observation event.";
        return new WeeklySkyForecastV2EventIntelligenceItem(Guid.NewGuid().ToString("N"), type, title, description, date, bestTimeUtc, objectCodes, visibleNames, importance, visual, story, rarity, visualStrategy, scenePurpose, "Derived from weekly skyfield forecast.", source);
    }
}

public sealed class WeeklySkyForecastV2IntelligenceService(
    IWeeklySkyForecastContextBuilder contextBuilder,
    IWeeklySkyForecastV2EventIntelligenceBuilder eventBuilder,
    IWeeklySkyForecastV2EditorialIntelligenceBuilder editorialBuilder) : IWeeklySkyForecastV2IntelligenceService
{
    public async Task<WeeklySkyForecastV2IntelligenceResponse> PreviewAsync(WeeklySkyForecastV2IntelligenceRequest request, CancellationToken cancellationToken)
    {
        var ctx = await contextBuilder.BuildAsync(new WeeklySkyForecastProductionRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc, Diagnostics: request.Diagnostics), cancellationToken);
        if (ctx.DailyForecasts.Count != 7)
            throw new InvalidOperationException("Skyfield weekly response must include 7 days.");
        var events = eventBuilder.Build(ctx);
        if (!events.Any()) throw new InvalidOperationException("At least one event must be extracted.");
        var primaryObjects = events.SelectMany(e => e.ObjectCodes).Distinct().Take(6).ToList();
        var arc = new WeeklyStoryArc(
            "This week has a standout sky story",
            "Best windows, moon/planet moments, and visual priorities",
            "Weekly visibility momentum",
            "Start with the strongest shared viewing window and anchor the rest of the week around it.",
            ["Hook the week with a story headline", "Cover the main sky event", "Recommend the best night", "Highlight moon or planet hero", "Share a practical photography tip", "Close with a clear call-to-action"],
            "Plan one primary observation night and one backup.",
            primaryObjects,
            events.Select(e => e.PrimaryDate.ToString("yyyy-MM-dd")).Distinct().Take(6).ToList(),
            events.OrderByDescending(e => e.StoryScore).Take(3).Select(e => e.Title).ToList());
        var baseResponse = new WeeklySkyForecastV2IntelligenceResponse(null, "WeeklySkyForecast", true, ctx.WeekStartDate, ctx.WeekEndDate, ctx.RegionId,
            new WeeklySkyForecastV2SkyfieldSummary(ctx.DailyForecasts.Count, ctx.DailyForecasts.SelectMany(d => d.VisibleObjects).Count(v => v.Visible), ctx.WeeklyHighlights.Count, ctx.RecommendedNights.Count, ctx.BestPlanetOfWeek, ctx.BestMoonNight, ctx.BestPhotographyNight),
            events,
            arc,
            null!,
            events.Select(e => e.RecommendedVisualStrategy).Distinct().ToList(),
            ctx.Warnings,
            [new CategoryProductionStepResult("weekly_skyfield_context", "completed", DateTime.UtcNow, DateTime.UtcNow, 0, "Context built", null, []), new CategoryProductionStepResult("event_intelligence", "completed", DateTime.UtcNow, DateTime.UtcNow, 0, "Event intelligence generated", null, [])]);
        var editorial = await editorialBuilder.BuildAsync(baseResponse, cancellationToken);
        return baseResponse with { EditorialStoryPackage = editorial };
    }
}
