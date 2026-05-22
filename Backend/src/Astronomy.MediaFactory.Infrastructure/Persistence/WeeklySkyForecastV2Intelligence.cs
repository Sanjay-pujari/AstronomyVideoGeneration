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
    IWeeklySkyForecastV2EditorialIntelligenceBuilder editorialBuilder,
    IWeeklySkyForecastV2CinematicEditorialRefiner cinematicRefiner,
    IWeeklySkyForecastV2NarrativeAbstractionBuilder narrativeAbstractionBuilder,
    IWeeklySkyForecastV2NarrationPlanner narrationPlanner,
    IWeeklySkyForecastV2NarrationTextGenerator narrationTextGenerator) : IWeeklySkyForecastV2IntelligenceService
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
            null,
            null,
            null,
            null,
            events.Select(e => e.RecommendedVisualStrategy).Distinct().ToList(),
            ctx.Warnings,
            [new CategoryProductionStepResult("weekly_skyfield_context", "completed", DateTime.UtcNow, DateTime.UtcNow, 0, "Context built", null, []), new CategoryProductionStepResult("event_intelligence", "completed", DateTime.UtcNow, DateTime.UtcNow, 0, "Event intelligence generated", null, [])]);
        var editorial = await editorialBuilder.BuildAsync(baseResponse, cancellationToken);
        var cinematic = await cinematicRefiner.RefineAsync(editorial, baseResponse with { EditorialStoryPackage = editorial }, cancellationToken);
        var narrative = await narrativeAbstractionBuilder.BuildAsync(cinematic, editorial, baseResponse with { EditorialStoryPackage = editorial, CinematicStoryBlueprint = cinematic }, cancellationToken);
        var narrationPlan = await narrationPlanner.BuildAsync(narrative, cinematic, baseResponse.SkyfieldSummary, baseResponse.Region, baseResponse.WeekStartDate, request.Language, cancellationToken);
        var generatedNarration = await narrationTextGenerator.GenerateAsync(narrationPlan, narrative, cancellationToken);
        return baseResponse with { EditorialStoryPackage = editorial, CinematicStoryBlueprint = cinematic, NarrativeAbstractionPackage = narrative, NarrationPlan = narrationPlan, GeneratedNarrationPackage = generatedNarration };
    }
}

public sealed class WeeklySkyForecastV2NarrationPlanner : IWeeklySkyForecastV2NarrationPlanner
{
    private static readonly string[] SegmentCodes = ["OpeningHook", "HeroSkyStory", "WhyThisWeekMatters", "BestObservationNight", "MoonPlanetHighlight", "ViewingPhotographyTip", "ClosingCTA"];
    private static readonly Dictionary<string, (int min, int max)> DurationGuidelines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OpeningHook"] = (10, 15), ["HeroSkyStory"] = (18, 25), ["WhyThisWeekMatters"] = (12, 18), ["BestObservationNight"] = (12, 18), ["MoonPlanetHighlight"] = (12, 18), ["ViewingPhotographyTip"] = (10, 15), ["ClosingCTA"] = (8, 12)
    };

    public Task<WeeklyNarrationPlan> BuildAsync(WeeklyNarrativeAbstractionPackage narrativePackage, WeeklyCinematicStoryBlueprint cinematicBlueprint, WeeklySkyForecastV2SkyfieldSummary skyfieldSummary, string regionId, DateOnly weekStartDate, string language, CancellationToken cancellationToken)
    {
        var flow = narrativePackage.NarrativeFlow.OrderBy(x => x.BeatOrder).ToList();
        var segments = new List<WeeklyNarrationSegment>();
        for (var i = 0; i < Math.Min(SegmentCodes.Length, flow.Count); i++)
        {
            var beat = flow[i];
            var code = SegmentCodes[i];
            var (minDur, maxDur) = DurationGuidelines[code];
            var duration = Math.Clamp(beat.EstimatedNarrationSeconds, minDur, maxDur);
            segments.Add(new WeeklyNarrationSegment(code, i + 1, beat.BeatTitle, beat.NarrationPurpose, beat.EmotionalIntent, beat.BeatCode, beat.TargetObjects, beat.TargetDate, duration, beat.RecommendedVisualStrategy, beat.VisualIntent, BuildHints(code, beat, regionId)));
        }

        var total = segments.Sum(x => x.EstimatedDurationSeconds);
        if (total < 90 && segments.Count > 0) segments[1] = segments[1] with { EstimatedDurationSeconds = segments[1].EstimatedDurationSeconds + Math.Min(150 - total, 90 - total) };
        total = segments.Sum(x => x.EstimatedDurationSeconds);
        var shorts = narrativePackage.ShortsNarrativePlan
            .Take(3)
            .Select((s, i) => new WeeklyShortNarrationItem(i switch { 0 => "HeroGroupingShort", 1 => "BestNightShort", _ => "MoonPlanetHighlightShort" }, s.Title, s.NarrationHook, s.ObjectCodes, s.TargetDate, Math.Clamp(s.EstimatedDurationSeconds, 20, 35), s.ViewerPromise, s.RecommendedVisualStrategy, 100 - i * 10))
            .ToList();
        while (shorts.Count < 3)
        {
            shorts.Add(new WeeklyShortNarrationItem(shorts.Count switch { 0 => "HeroGroupingShort", 1 => "BestNightShort", _ => "MoonPlanetHighlightShort" }, $"Weekly Sky Short {shorts.Count + 1}", narrativePackage.OpeningNarrationHook, narrativePackage.HeroNarrative.ObjectCodes, narrativePackage.HeroNarrative.PeakDate, 25, "Quick weekly sky update", narrativePackage.HeroNarrative.RecommendedVisualStrategy, 60 - shorts.Count * 5));
        }

        var warnings = new List<string>();
        if (segments.Count is < 6 or > 7) warnings.Add("Long-form narration segment count should be between 6 and 7.");
        if (segments.Select(x => x.SegmentCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() != segments.Count) warnings.Add("Duplicate long-form segment codes detected.");
        if (segments.Sum(x => x.EstimatedDurationSeconds) is < 90 or > 150) warnings.Add("Long-form total duration is outside 90-150 seconds.");

        var longForm = new WeeklyLongFormNarrationPlan(segments.Sum(x => x.EstimatedDurationSeconds), segments.Count, segments);
        return Task.FromResult(new WeeklyNarrationPlan(language, cinematicBlueprint.NarrationTone, longForm, new WeeklyShortNarrationPlan(shorts), warnings));
    }

    private static IReadOnlyList<string> BuildHints(string segmentCode, WeeklyNarrationSegment beat, string regionId)
        => segmentCode switch
        {
            "OpeningHook" => [$"Create curiosity immediately for viewers in {regionId}.", "Use short cinematic spoken sentences.", "Orient the viewer to the western sky after sunset."],
            "HeroSkyStory" => ["Sound awe-driven and natural.", "Emphasize continuity across multiple evenings.", "Avoid technical astronomy jargon."],
            "BestObservationNight" => [$"Sound practical and confident; recommend {beat.TargetDate:MMMM d}.", "Mention sunset timing organically.", "Provide one clear action the viewer can take."],
            "MoonPlanetHighlight" => ["Use emotionally descriptive visual wording.", "Keep pacing slightly slower for cinematic effect.", "Prioritize image-rich but conversational language."],
            "ViewingPhotographyTip" => ["Keep practical, concise, and casual-viewer friendly.", "Use one simple actionable setup tip.", "Avoid overexplaining camera settings."],
            "ClosingCTA" => ["Use uplifting and emotionally warm tone.", "Encourage stepping outside this week.", "Close with human, optimistic voice."],
            _ => ["Use conversational cinematic language.", "Avoid fake conjunction claims or exact-alignment wording.", "Keep pacing suitable for voiceover with vivid lines."]
        };
}

public sealed class WeeklySkyForecastV2NarrationTextGenerator : IWeeklySkyForecastV2NarrationTextGenerator
{
    private static readonly string[] Forbidden = ["conjunction", "exact alignment", "nearly touching", "rare alignment", "close approach"];
    public Task<WeeklyGeneratedNarrationPackage> GenerateAsync(WeeklyNarrationPlan narrationPlan, WeeklyNarrativeAbstractionPackage abstractionPackage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var segments = narrationPlan.LongFormPlan.Segments.Select(BuildLongSegment).ToList();
        var full = string.Join("\n\n", segments.Select(x => x.NarrationText));
        var shorts = narrationPlan.ShortsPlan.Shorts.Select(BuildShort).ToList();
        var warnings = new List<string>(narrationPlan.NarrationWarnings);
        if (Forbidden.Any(f => full.Contains(f, StringComparison.OrdinalIgnoreCase))) warnings.Add("Forbidden conjunction-like wording detected.");
        return Task.FromResult(new WeeklyGeneratedNarrationPackage(narrationPlan.Language, abstractionPackage.EmotionalTone, new WeeklyGeneratedLongNarration(full, segments.Sum(x => x.EstimatedDurationSeconds), segments), shorts, warnings));
    }
    private static WeeklyGeneratedNarrationSegment BuildLongSegment(WeeklyNarrationSegment s)
    {
        var objects = WeeklySkyForecastV2TextHelpers.FormatCelestialList(s.TargetObjects);
        var text = s.SegmentCode switch
        {
            "OpeningHook" => $"Step outside after sunset, look west, and you'll immediately spot {objects} sharing the same evening sky.",
            "HeroSkyStory" => $"Over several evenings, {objects} return like a repeating scene, building one continuous sky story that's easy to follow.",
            "BestObservationNight" => $"If you choose just one evening, make it {s.TargetDate:MMMM d}. The post-sunset window is clean, practical, and worth planning around.",
            "MoonPlanetHighlight" => $"On this highlight night, the Moon adds glow and depth while {objects} hold the frame, giving the western sky a rich cinematic feel.",
            "ViewingPhotographyTip" => $"For an easy shot, use a stable phone tripod, keep a wide frame, and include a bit of horizon for scale.",
            "ClosingCTA" => $"Before the week ends, give yourself ten quiet minutes outside. This is the kind of sky that rewards attention.",
            _ => $"{objects} remain visible in the same viewing window after sunset."
        };
        return new WeeklyGeneratedNarrationSegment(s.SegmentCode, s.SegmentTitle, text, s.EstimatedDurationSeconds, s.TargetObjects, s.RecommendedVisualStrategy, s.VisualPurpose);
    }
    private static WeeklyGeneratedShortNarration BuildShort(WeeklyShortNarrationItem s)
    {
        var objects = WeeklySkyForecastV2TextHelpers.FormatCelestialList(s.TargetObjects);
        var text = $"{s.Hook} Tonight, {objects} are visible in one western-sky window after sunset. Save this and step outside.";
        return new WeeklyGeneratedShortNarration(s.ShortCode, s.Title, text, s.EstimatedDurationSeconds, s.RecommendedVisualStrategy);
    }
}
