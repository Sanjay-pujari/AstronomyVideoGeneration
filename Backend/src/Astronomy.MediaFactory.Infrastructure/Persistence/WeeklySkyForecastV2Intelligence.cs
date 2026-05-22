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
        var narrationQuality = WeeklySkyForecastV2NarrationQualityValidator.Validate(narrationPlan, generatedNarration);
        var visualRequirementPackage = WeeklySkyForecastV2VisualRequirementExtractor.Extract(narrationPlan, generatedNarration, narrative, cinematic, baseResponse.EventIntelligence);
        return baseResponse with { EditorialStoryPackage = editorial, CinematicStoryBlueprint = cinematic, NarrativeAbstractionPackage = narrative, NarrationPlan = narrationPlan, GeneratedNarrationPackage = generatedNarration, NarrationQuality = narrationQuality, VisualRequirementPackage = visualRequirementPackage };
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

    private static IReadOnlyList<string> BuildHints(string segmentCode, NarrativeFlowBeat beat, string regionId)
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
    private static readonly string[] Forbidden = ["conjunction", "exact alignment", "nearly touching", "rare alignment", "close approach", "same viewing window", "observation event", "visibility momentum", "high-value weekly observation event"];
    private const int SpokenWordsPerMinute = 145;
    private static readonly string[] CtaVariants = ["Tonight is worth a look.", "Keep your eyes on the western sky.", "Don’t miss this week’s best sky moment.", "Watch the western horizon after sunset.", "This is your best skywatching pick of the week.", "Look west before the week slips away."];
    public Task<WeeklyGeneratedNarrationPackage> GenerateAsync(WeeklyNarrationPlan narrationPlan, WeeklyNarrativeAbstractionPackage abstractionPackage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var phraseDiversifier = new PhraseDiversifier();
        var segments = narrationPlan.LongFormPlan.Segments.Select(s => BuildLongSegment(s, phraseDiversifier)).ToList();
        var full = string.Join("\n\n", segments.Select(x => x.NarrationText));
        var shorts = narrationPlan.ShortsPlan.Shorts.Select((s, i) => BuildShort(s, i)).ToList();
        var warnings = new List<string>(narrationPlan.NarrationWarnings);
        if (Forbidden.Any(f => full.Contains(f, StringComparison.OrdinalIgnoreCase))) warnings.Add("Forbidden conjunction-like wording detected.");
        var estimatedLongDuration = EstimateDurationSeconds(full);
        if (narrationPlan.LongFormPlan.TargetDurationSeconds == 90 && estimatedLongDuration < 75) warnings.Add("Generated long narration is shorter than target duration.");
        return Task.FromResult(new WeeklyGeneratedNarrationPackage(narrationPlan.Language, abstractionPackage.EmotionalTone, new WeeklyGeneratedLongNarration(full, estimatedLongDuration, segments), shorts, warnings));
    }
    private static WeeklyGeneratedNarrationSegment BuildLongSegment(WeeklyNarrationSegment s, PhraseDiversifier diversifier)
    {
        var objects = WeeklySkyForecastV2TextHelpers.FormatCelestialList(s.TargetObjects);
        var text = s.SegmentCode switch
        {
            "OpeningHook" => $"What if one glance after sunset could reveal the week’s full sky story? {objects} {diversifier.Next("appear_together_after_sunset")}.",
            "HeroSkyStory" => $"Night after night, {objects} {diversifier.Next("return_evening_after_evening")}, and they {diversifier.Next("share_western_sky")} in a way that feels surprisingly cinematic.",
            "WhyThisWeekMatters" => $"This matters because these targets {diversifier.Next("create_easy_target")}: you can find them quickly, track them confidently, and enjoy a reliable west-facing view.",
            "BestObservationNight" => $"Circle {s.TargetDate:MMMM d}. The sky is cleaner after sunset, and this timing gives you the most practical chance to spot the full grouping with confidence.",
            "MoonPlanetHighlight" => $"The Moon brings a calm silver glow while {objects} add warm contrast, creating quiet beauty that lingers long after twilight.",
            "ViewingPhotographyTip" => $"Use a steady phone tripod, keep the horizon low in frame, and let the brighter objects guide focus—simple choices that make your shot feel intentional.",
            "ClosingCTA" => $"Before the week slips by, take ten peaceful minutes outside and look west—you might find this becomes your favorite sky memory of the week.",
            _ => $"{objects} {diversifier.Next("visible_west_after_sunset")}."
        };
        return new WeeklyGeneratedNarrationSegment(s.SegmentCode, s.SegmentTitle, text, EstimateDurationSeconds(text), s.TargetObjects, s.RecommendedVisualStrategy, s.VisualPurpose);
    }
    private static WeeklyGeneratedShortNarration BuildShort(WeeklyShortNarrationItem s, int index)
    {
        var objects = WeeklySkyForecastV2TextHelpers.FormatCelestialList(s.TargetObjects);
        var cta = CtaVariants[index % CtaVariants.Length];
        var text = $"{s.Hook} Tonight, {objects} {PhraseDiversifier.ShortVariant(index)}. {cta}";
        return new WeeklyGeneratedShortNarration(s.ShortCode, s.Title, text, EstimateDurationSeconds(text), s.RecommendedVisualStrategy);
    }
    private static int EstimateDurationSeconds(string text) => (int)Math.Ceiling(CountWords(text) / (double)SpokenWordsPerMinute * 60d);
    private static int CountWords(string text) => text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
    private sealed class PhraseDiversifier
    {
        private readonly Dictionary<string, int> _index = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string[]> Variants = new(StringComparer.OrdinalIgnoreCase)
        {
            ["share_western_sky"] = ["share the western sky", "gather in the twilight view", "brighten the western horizon"],
            ["appear_together_after_sunset"] = ["appear together after sunset", "line up in twilight soon after sunset"],
            ["return_evening_after_evening"] = ["return evening after evening", "come back in view night after night"],
            ["create_easy_target"] = ["create one easy skywatching target", "form this week’s strongest evening view"],
            ["visible_west_after_sunset"] = ["are visible in the west after sunset", "hold steady in the western twilight"]
        };
        public string Next(string key){ var list = Variants[key]; var i = _index.TryGetValue(key, out var current) ? current : 0; _index[key] = i + 1; return list[Math.Min(i, list.Length - 1)]; }
        public static string ShortVariant(int index) => index switch { 0 => "appear together after sunset", 1 => "share the western sky", _ => "are visible in the west after sunset" };
    }
}

internal static class WeeklySkyForecastV2NarrationQualityValidator
{
    public static WeeklyNarrationQualityReport Validate(WeeklyNarrationPlan plan, WeeklyGeneratedNarrationPackage generated)
    {
        var forbidden = new[] { "same viewing window", "observation event", "visibility momentum", "high-value weekly observation event" };
        var text = generated.LongFormNarration.FullNarration;
        var forbiddenHits = forbidden.Where(f => text.Contains(f, StringComparison.OrdinalIgnoreCase) || generated.ShortNarrations.Any(s => s.NarrationText.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
        var repeatedWarnings = new List<string>();
        foreach (var phrase in new[] { "western sky", "step outside", "after sunset" })
            if (Count(text, phrase) > 1) repeatedWarnings.Add($"Repeated phrase detected: {phrase}");
        var uniqueCtas = generated.ShortNarrations.Select(s => s.NarrationText.Split('.').LastOrDefault()?.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var shortUnique = uniqueCtas == generated.ShortNarrations.Count;
        var warnings = new List<string>(generated.Warnings);
        warnings.AddRange(repeatedWarnings);
        var emotionalProgressionDetected = plan.LongFormPlan.Segments.Select(s => s.EmotionalTone).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 5;
        return new WeeklyNarrationQualityReport(forbiddenHits.Count == 0 && shortUnique, warnings, forbiddenHits, repeatedWarnings, WordCount(text), generated.LongFormNarration.EstimatedDurationSeconds, plan.LongFormPlan.TargetDurationSeconds, emotionalProgressionDetected, shortUnique);
    }
    private static int Count(string text, string token) => text.Split(token, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length - 1;
    private static int WordCount(string text) => text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
}

internal static class WeeklySkyForecastV2VisualRequirementExtractor
{
    public static WeeklyVisualRequirementPackage Extract(WeeklyNarrationPlan plan, WeeklyGeneratedNarrationPackage generated, WeeklyNarrativeAbstractionPackage narrative, WeeklyCinematicStoryBlueprint cinematic, IReadOnlyList<WeeklySkyForecastV2EventIntelligenceItem> intelligence)
    {
        var mappings = new List<SegmentVisualMapping>
        {
            new("OpeningHook","hero_western_grouping","Primary","0-15s",false,"fade-in","cut"),
            new("HeroSkyStory","hero_western_grouping","Reuse","15-40s",true,"cut","cut"),
            new("WhyThisWeekMatters","hero_western_grouping","Reuse","40-55s",true,"cut","cut"),
            new("BestObservationNight","best_night_wide","Primary","55-75s",false,"cut","crossfade"),
            new("MoonPlanetHighlight","moon_jupiter_hero","Primary","75-100s",false,"crossfade","cut"),
            new("ViewingPhotographyTip","viewing_tip_wide","Primary","100-120s",false,"cut","fade"),
            new("ClosingCTA","best_night_wide","Reuse","120-140s",true,"fade","fade-out")
        };
        var reqs = new List<WeeklyVisualRequirement>
        {
            Build("hero_western_grouping","Core grouping visual for opening and story body",["OpeningHook","HeroSkyStory","WhyThisWeekMatters"],["MOON","JUPITER","VENUS"],narrative.HeroNarrative.PeakDate,null,"Curious wonder","Hybrid","Hybrid","GroupingComposite","Western twilight scene with the Moon glowing large, Jupiter nearby as a bright golden point, and Venus lower toward the horizon.","slow parallax",["object labels"],true,100,"LongFormHero","hero_grouping"),
            Build("best_night_wide","Best night orientation and sky confirmation",["BestObservationNight","ClosingCTA"],["MOON","JUPITER","VENUS"],plan.LongFormPlan.Segments.FirstOrDefault(s=>s.SegmentCode=="BestObservationNight")?.TargetDate ?? narrative.HeroNarrative.PeakDate,null,"Anticipation and practical confidence","Stellarium","Stellarium","ObservationMap","Realistic Stellarium-style wide sky view after sunset, showing the visible Moon and bright planets in context.","slow pan",["west arrow","time annotation"],true,95,"LongFormOrientation","best_night_map"),
            Build("moon_jupiter_hero","Emotional detail hero shot",["MoonPlanetHighlight"],["MOON","JUPITER"],narrative.HeroNarrative.PeakDate,null,"Quiet beauty","CelestialAsset","CelestialAsset","MoonHero","Cinematic close-up composition using Moon and Jupiter assets, with slow depth movement and soft starfield background.","push-in",["none"],false,90,"LongFormDetail","moon_jupiter_hero"),
            Build("viewing_tip_wide","Practical viewing and photography guidance",["ViewingPhotographyTip"],["MOON","JUPITER","VENUS"],narrative.HeroNarrative.PeakDate,null,"Practical confidence","Hybrid","Hybrid","TipVisual","Wide horizon composition with subtle tripod/phone framing overlay, designed to support practical viewing advice.","static",["tripod framing guide"],false,80,"LongFormTip","viewing_tip")
        };
        var thumb = new ThumbnailVisualRequirement("thumbnail_story", narrative.ThumbnailNarrativeDirection.PrimaryObjects, narrative.ThumbnailNarrativeDirection.SecondaryObjects, "High-contrast hybrid thumbnail with glowing Moon, Jupiter and Venus, clear western twilight background, and bold overlay text.", cinematic.ThumbnailBlueprint.OverlayTextSuggestion, "Hybrid", "Hybrid");
        var warnings = new List<string>();
        if (plan.LongFormPlan.Segments.Any(s => mappings.All(m => !m.SegmentCode.Equals(s.SegmentCode, StringComparison.OrdinalIgnoreCase)))) warnings.Add("At least one long-form segment has no visual mapping.");
        if (generated.ShortNarrations.Any(s => s.ShortCode is not ("HeroGroupingShort" or "BestNightShort" or "MoonPlanetHighlightShort"))) warnings.Add("Unexpected short code detected for visual mapping.");
        return new WeeklyVisualRequirementPackage(reqs, mappings, [new VisualReusePlan("hero_western_grouping", ["HeroSkyStory", "WhyThisWeekMatters"], "Keep narrative continuity"), new VisualReusePlan("best_night_wide", ["ClosingCTA"], "Return to practical orientation for close")], thumb, warnings);
    }
    private static WeeklyVisualRequirement Build(string code, string purpose, IReadOnlyList<string> source, IReadOnlyList<string> objects, DateOnly date, DateTime? bestTimeUtc, string tone, string strategy, string sourceType, string sceneType, string composition, string motion, IReadOnlyList<string> overlays, bool reuse, int priority, string role, string uniq)
        => new(Guid.NewGuid().ToString("N"), code, purpose, source, objects, date, bestTimeUtc, tone, strategy, sourceType, sceneType, composition, motion, overlays, reuse, priority, role, uniq);
}
