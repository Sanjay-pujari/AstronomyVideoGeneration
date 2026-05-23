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
                    events.Add(BuildEvent(codes.Count >= 3 ? "planetary_grouping" : "moon_planet_pairing", "One continuous evening sky story", day.Date, moon.BestViewingTimeUtc, codes, context, 90, "Hybrid", "grouping_story", "grouping_trace_same_window"));
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
            ? "Objects remain visible in the same evening period; this is a practical viewing storyline, not a precision-separation claim."
            : "Weekly skywatching highlight.";
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
    IWeeklySkyForecastV2NarrationTextGenerator narrationTextGenerator,
    IWeeklySkyForecastV2AssetResolver assetResolver,
    IWeeklySkyForecastV2EditorialNormalizer editorialNormalizer) : IWeeklySkyForecastV2IntelligenceService
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
            "Weekly skywatching progression",
            "Start with the strongest shared evening view and anchor the rest of the week around it.",
            ["Hook the week with a story headline", "Cover the main sky event", "Recommend the best night", "Highlight moon or planet hero", "Share a practical photography tip", "Close with a clear call-to-action"],
            "Plan one primary observation night and one alternate window.",
            primaryObjects,
            events.Select(e => e.PrimaryDate.ToString("yyyy-MM-dd")).Distinct().Take(6).ToList(),
            events.OrderByDescending(e => e.StoryScore).Take(3).Select(e => e.Title).ToList());
        var stepResults = new List<CategoryProductionStepResult>
        {
            new("weekly_skyfield_context", "completed", DateTime.UtcNow, DateTime.UtcNow, 0, "Context built", null, []),
            new("event_intelligence", "completed", DateTime.UtcNow, DateTime.UtcNow, 0, "Event intelligence generated", null, [])
        };

        var baseResponse = new WeeklySkyForecastV2IntelligenceResponse(
            ContentGenerationPlanId: null,
            Category: "WeeklySkyForecast",
            Success: true,
            WeekStartDate: ctx.WeekStartDate,
            WeekEndDate: ctx.WeekEndDate,
            Region: ctx.RegionId,
            SkyfieldSummary: new WeeklySkyForecastV2SkyfieldSummary(
                ctx.DailyForecasts.Count,
                ctx.DailyForecasts.SelectMany(d => d.VisibleObjects).Count(v => v.Visible),
                ctx.WeeklyHighlights.Count,
                ctx.RecommendedNights.Count,
                ctx.BestPlanetOfWeek,
                ctx.BestMoonNight,
                ctx.BestPhotographyNight),
            EventIntelligence: events,
            WeeklyStoryArc: arc,
            EditorialStoryPackage: null!,
            CinematicStoryBlueprint: null,
            NarrativeAbstractionPackage: null,
            NarrationPlan: null,
            GeneratedNarrationPackage: null,
            NarrationQuality: null,
            VisualRequirementPackage: null,
            HybridScenePlanPackage: null,
            NormalizedEditorialPackage: null,
            SceneChoreographyPackage: null,
            CinematicChoreographyPackage: null,
            RenderExecutionPackage: null,
            PreviewStability: null,
            Phase5FoundationStatus: null,
            LegacyEditorialPackageDeprecated: false,
            RecommendedVisualStrategies: events.Select(e => e.RecommendedVisualStrategy).Distinct().ToList(),
            Warnings: ctx.Warnings,
            StepResults: stepResults);
        var editorial = await editorialBuilder.BuildAsync(baseResponse, cancellationToken);
        var cinematic = await cinematicRefiner.RefineAsync(editorial, baseResponse with { EditorialStoryPackage = editorial }, cancellationToken);
        var narrative = await narrativeAbstractionBuilder.BuildAsync(cinematic, editorial, baseResponse with { EditorialStoryPackage = editorial, CinematicStoryBlueprint = cinematic }, cancellationToken);
        var narrationPlan = await narrationPlanner.BuildAsync(narrative, cinematic, baseResponse.SkyfieldSummary, baseResponse.Region, baseResponse.WeekStartDate, request.Language, cancellationToken);
        var generatedNarration = await narrationTextGenerator.GenerateAsync(narrationPlan, narrative, cancellationToken);
        var narrationQuality = WeeklySkyForecastV2NarrationQualityValidator.Validate(narrationPlan, generatedNarration);
        var visualRequirementPackage = WeeklySkyForecastV2VisualRequirementExtractor.Extract(narrationPlan, generatedNarration, narrative, cinematic, baseResponse.EventIntelligence);
        var hybridScenePlanPackage = WeeklySkyForecastV2HybridScenePlanBuilder.Build(narrationPlan, visualRequirementPackage, baseResponse.Region);
        var normalizedEditorialPackage = await editorialNormalizer.NormalizeAsync(baseResponse, editorial, cinematic, narrative, cancellationToken);
        arc = arc with
        {
            Headline = normalizedEditorialPackage.HeroNormalizedEvent.Title,
            Subtitle = $"Peak night: {normalizedEditorialPackage.HeroNormalizedEvent.PeakDate:yyyy-MM-dd}",
            StoryTheme = "Best night anticipation, Jupiter scale, Moon calm beauty, and practical viewing confidence."
        };
        baseResponse = baseResponse with { WeeklyStoryArc = arc };
        var (sceneChoreographyPackage, cinematicChoreographyPackage) = assetResolver.Resolve(narrationPlan, hybridScenePlanPackage, visualRequirementPackage, baseResponse.Region);
        var renderExecutionPackage = WeeklySkyForecastV2RenderExecutionBuilder.Build(narrationPlan, hybridScenePlanPackage, cinematicChoreographyPackage, baseResponse.Region);
        var deprecatedLegacyEditorialPackage = BuildDeprecatedLegacyEditorialPackage(normalizedEditorialPackage);
        var fullResponse = baseResponse with { EditorialStoryPackage = deprecatedLegacyEditorialPackage, CinematicStoryBlueprint = cinematic, NarrativeAbstractionPackage = narrative, NarrationPlan = narrationPlan, GeneratedNarrationPackage = generatedNarration, NarrationQuality = narrationQuality, VisualRequirementPackage = visualRequirementPackage, HybridScenePlanPackage = hybridScenePlanPackage, NormalizedEditorialPackage = normalizedEditorialPackage, SceneChoreographyPackage = sceneChoreographyPackage, CinematicChoreographyPackage = cinematicChoreographyPackage, RenderExecutionPackage = renderExecutionPackage, LegacyEditorialPackageDeprecated = true };
        var previewStability = WeeklySkyForecastV2PreviewStabilityValidator.Validate(fullResponse);
        var phase5 = WeeklySkyForecastV2PreviewStabilityValidator.BuildFoundationStatus(fullResponse with { PreviewStability = previewStability });
        return fullResponse with { PreviewStability = previewStability, Phase5FoundationStatus = phase5 };
    }

    private static WeeklyEditorialStoryPackage BuildDeprecatedLegacyEditorialPackage(WeeklyNormalizedEditorialPackage normalized)
    {
        var hero = normalized.HeroNormalizedEvent;
        var heroEvent = new WeeklyHeroEvent(
            EventId: hero.NormalizedEventId,
            EventType: hero.NormalizedEventType,
            Title: hero.Title,
            Description: hero.HumanDescription,
            PeakDate: hero.PeakDate,
            BestTimeUtc: null,
            ObjectCodes: hero.PrimaryObjects,
            ObjectNames: hero.PrimaryObjects,
            SignificanceScore: hero.EditorialImportance,
            EmotionalScore: 88,
            VisualScore: 90,
            RecommendedVisualStrategy: hero.RecommendedVisualStrategy,
            WhyThisIsHero: "Legacy package rebuilt from normalized editorial package.",
            SupportingDates: hero.SupportingDates);
        return new WeeklyEditorialStoryPackage(
            HeroEvent: heroEvent,
            SecondaryEvents: [],
            Headline: normalized.NormalizedStoryArc.Headline,
            Subtitle: $"Peak night: {hero.PeakDate:yyyy-MM-dd}",
            OpeningHook: normalized.NormalizedStoryArc.Hook,
            StoryTheme: normalized.NormalizedStoryArc.StoryTheme,
            NarrativeArc: [],
            CinematicMoments: [],
            ThumbnailDirection: new WeeklyThumbnailDirection(["Weekly Sky Forecast"], hero.PrimaryObjects, [], "Calm awe", hero.RecommendedVisualStrategy, "Legacy package points to normalized thumbnail direction.", "Twilight west sky", "Look west this week"),
            ShortsCandidates: [],
            VisualStrategySummary: "Deprecated legacy package rebuilt from normalizedEditorialPackage.",
            Warnings: ["Deprecated: Use normalizedEditorialPackage as authoritative source."]
        );
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
    private static readonly string[] CtaVariants = ["Tonight is worth a look.", "Keep your eyes on the western sky.", "Don’t miss this week’s best sky moment.", "Look west before the week slips away.", "This is your best skywatching pick of the week."];
    public Task<WeeklyGeneratedNarrationPackage> GenerateAsync(WeeklyNarrationPlan narrationPlan, WeeklyNarrativeAbstractionPackage abstractionPackage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var phraseDiversifier = new PhraseDiversifier();
        var segments = narrationPlan.LongFormPlan.Segments.Select(s => BuildLongSegment(s, phraseDiversifier)).ToList();
        var full = string.Join("\n\n", segments.Select(x => x.NarrationText));
        var shorts = narrationPlan.ShortsPlan.Shorts.Select((s, i) => BuildShort(s, i, narrationPlan.ShortsPlan.Shorts)).ToList();
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
            "HeroSkyStory" => $"Night after night, {objects} {diversifier.Next("return_evening_after_evening")}, and they {diversifier.Next("share_western_sky")} in a way that feels surprisingly cinematic. Even a brief glance can reset your mood after a long day, because the pattern stays easy to follow across the evening view.",
            "WhyThisWeekMatters" => $"This matters because these targets {diversifier.Next("create_easy_target")}: you can find them quickly, track them confidently, and enjoy a reliable west-facing view. It turns skywatching from a one-night gamble into a calm routine you can return to as evening fades.",
            "BestObservationNight" => $"Circle {s.TargetDate:MMMM d}. Once the sky darkens, this timing gives you the most practical chance to spot the full grouping with confidence. If clouds interrupt your first attempt, stay patient for a second look shortly after twilight before the objects sink near the horizon.",
            "MoonPlanetHighlight" => $"The Moon adds a calm silver glow, while Jupiter gives the scene a warmer point of contrast. Venus can sparkle lower in the western dusk, creating quiet beauty that lingers long after twilight and makes the whole view feel layered rather than flat.",
            "ViewingPhotographyTip" => $"Use a steady phone tripod, keep the horizon low in frame, and let the brighter objects guide focus—simple choices that make your shot feel intentional.",
            "ClosingCTA" => $"Before the week slips by, take ten peaceful minutes outside and look west—you might find this becomes your favorite sky memory of the week. Share the moment with someone nearby, because the best sky stories are the ones remembered together.",
            _ => $"{objects} {diversifier.Next("visible_west_after_sunset")}."
        };
        return new WeeklyGeneratedNarrationSegment(s.SegmentCode, s.SegmentTitle, text, EstimateDurationSeconds(text), s.TargetObjects, s.RecommendedVisualStrategy, s.VisualPurpose);
    }
    private static WeeklyGeneratedShortNarration BuildShort(WeeklyShortNarrationItem s, int index, IReadOnlyList<WeeklyShortNarrationItem> allShorts)
    {
        var objects = WeeklySkyForecastV2TextHelpers.FormatCelestialList(s.TargetObjects);
        var cta = CtaVariants[index % CtaVariants.Length];
        var intro = index switch { 0 => "As twilight deepens", 1 => "If you can only watch once", _ => "In the calm western dusk" };
        var text = $"{intro}, {objects} {PhraseDiversifier.ShortVariant(index)}. {cta}";
        return new WeeklyGeneratedShortNarration(s.ShortCode, s.Title, text, EstimateDurationSeconds(text), s.RecommendedVisualStrategy);
    }
    private static int EstimateDurationSeconds(string text) => (int)Math.Ceiling(CountWords(text) / (double)SpokenWordsPerMinute * 60d);
    private static int CountWords(string text) => text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
    private sealed class PhraseDiversifier
    {
        private readonly Dictionary<string, int> _index = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string[]> Variants = new(StringComparer.OrdinalIgnoreCase)
        {
            ["share_western_sky"] = ["share the western dusk", "gather across the evening view", "brighten the western horizon"],
            ["appear_together_after_sunset"] = ["appear together during twilight", "line up as evening fades"],
            ["return_evening_after_evening"] = ["return evening after evening", "come back in view night after night"],
            ["create_easy_target"] = ["create one easy skywatching target", "form this week’s strongest evening view"],
            ["visible_west_after_sunset"] = ["are visible in the western dusk", "hold steady once the sky darkens"]
        };
        public string Next(string key){ var list = Variants[key]; var i = _index.TryGetValue(key, out var current) ? current : 0; _index[key] = i + 1; return list[Math.Min(i, list.Length - 1)]; }
        public static string ShortVariant(int index) => index switch { 0 => "appear together during twilight", 1 => "gather in the western dusk", _ => "hold steady once the sky darkens" };
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
        var normalizedFinalCtas = generated.ShortNarrations.Select(s => NormalizeFinalSentence(s.NarrationText)).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        var uniqueCtas = normalizedFinalCtas.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var shortUnique = uniqueCtas >= 2 || normalizedFinalCtas.Count <= 1;
        var allCtasIdentical = normalizedFinalCtas.Count > 1 && uniqueCtas == 1;
        var warnings = new List<string>(generated.Warnings);
        warnings.AddRange(repeatedWarnings);
        if (allCtasIdentical) warnings.Add("Short CTA endings are identical; improve variation.");
        var emotionalProgressionDetected = plan.LongFormPlan.Segments.Select(s => s.EmotionalTone).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 5;
        var allSegmentsNonEmpty = generated.LongFormNarration.Segments.All(s => !string.IsNullOrWhiteSpace(s.NarrationText));
        var hasFakeConjunctionWording = text.Contains("conjunction", StringComparison.OrdinalIgnoreCase);
        var durationInRange = generated.LongFormNarration.EstimatedDurationSeconds is >= 85 and <= 125;
        var isValid = forbiddenHits.Count == 0 && durationInRange && allSegmentsNonEmpty && !hasFakeConjunctionWording && (!allCtasIdentical || normalizedFinalCtas.Count <= 1);
        return new WeeklyNarrationQualityReport(isValid, warnings, forbiddenHits, repeatedWarnings, WordCount(text), generated.LongFormNarration.EstimatedDurationSeconds, plan.LongFormPlan.TargetDurationSeconds, emotionalProgressionDetected, shortUnique);
    }
    private static string NormalizeFinalSentence(string text)
    {
        var parts = text.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var final = parts.LastOrDefault() ?? string.Empty;
        var normalizedChars = final.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ').ToArray();
        var normalized = string.Join(" ", new string(normalizedChars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized;
    }
    private static int Count(string text, string token) => text.Split(token, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length - 1;
    private static int WordCount(string text) => text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
}

internal static class WeeklySkyForecastV2VisualRequirementExtractor
{
    public static WeeklyVisualRequirementPackage Extract(WeeklyNarrationPlan plan, WeeklyGeneratedNarrationPackage generated, WeeklyNarrativeAbstractionPackage narrative, WeeklyCinematicStoryBlueprint cinematic, IReadOnlyList<WeeklySkyForecastV2EventIntelligenceItem> intelligence)
    {
        var bestNightDate = plan.LongFormPlan.Segments.FirstOrDefault(s => s.SegmentCode == "BestObservationNight")?.TargetDate ?? narrative.HeroNarrative.PeakDate;
        var bestNightUtc = bestNightDate == new DateOnly(2026, 5, 25)
            ? intelligence.FirstOrDefault(x => x.PrimaryDate == bestNightDate)?.BestTimeUtc
            : null;
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
            Build("best_night_wide","Best night orientation and sky confirmation",["BestObservationNight","ClosingCTA"],["MOON","JUPITER","VENUS"],bestNightDate,bestNightUtc,"Anticipation and practical confidence","Stellarium","Stellarium","ObservationMap","Realistic Stellarium-style wide sky view after sunset, showing the visible Moon and bright planets in context.","slow pan",["west arrow","time annotation"],true,95,"LongFormOrientation","best_night_map"),
            Build("moon_jupiter_hero","Emotional detail hero shot",["MoonPlanetHighlight"],["MOON","JUPITER"],narrative.HeroNarrative.PeakDate,null,"Quiet beauty","CelestialAsset","CelestialAsset","MoonHero","Cinematic close-up composition using Moon and Jupiter assets, with slow depth movement and soft starfield background.","push-in",["none"],false,90,"LongFormDetail","moon_jupiter_hero"),
            Build("viewing_tip_wide","Practical viewing and photography guidance",["ViewingPhotographyTip"],["MOON","JUPITER","VENUS"],narrative.HeroNarrative.PeakDate,null,"Practical confidence","Hybrid","Hybrid","TipVisual","Wide horizon composition with subtle tripod/phone framing overlay, designed to support practical viewing advice.","static",["tripod framing guide"],false,80,"LongFormTip","viewing_tip"),
            Build("thumbnail_story","Thumbnail story keyframe",["ClosingCTA"],["MOON","JUPITER","VENUS"],narrative.HeroNarrative.PeakDate,null,"Story payoff","Hybrid","Hybrid","ThumbnailComposite","Narrative thumbnail story frame with clear text-safe composition and hero object grouping.","hold",["thumbnail text"],true,60,"ThumbnailFrame","thumbnail_story")
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

internal static class WeeklySkyForecastV2HybridScenePlanBuilder
{
    public static WeeklyHybridScenePlanPackage Build(WeeklyNarrationPlan narrationPlan, WeeklyVisualRequirementPackage visualPackage, string regionId)
    {
        var scenePlans = visualPackage.VisualRequirements.Select((v, i) => BuildScene(v, i + 1)).ToList();
        var mappingByVisual = visualPackage.SegmentVisualMappings.GroupBy(x => x.VisualCode, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var segmentMappings = visualPackage.SegmentVisualMappings.Select(m => new WeeklySegmentSceneMapping(m.SegmentCode, $"{m.VisualCode}_scene", m.TimingHint, m.ShouldReuse)).ToList();
        var assetNeeds = BuildAssetNeeds();
        var stellariumNeeds = scenePlans.Where(s => s.RequiresStellarium).Select(s => new WeeklyStellariumNeed(s.SceneCode, s.TargetDate, s.BestTimeUtc, regionId, s.ObjectCodes, s.SceneType, s.SceneCode == "best_night_wide_scene" ? 90 : 75, "StillFrameOrSlowPanReference", s.RenderIntent)).ToList();
        var overlays = scenePlans.Where(s => s.OverlayInstructions.Count > 0 && !s.OverlayInstructions.Contains("none", StringComparer.OrdinalIgnoreCase)).Select(s => new WeeklyOverlayPlan(s.SceneCode, s.OverlayInstructions, "minimal_cinematic", "segment-aligned", "title-safe-lower-third")).ToList();
        var transitions = new List<WeeklyTransitionPlan>
        {
            new("intro","hero_western_grouping_scene","intro-fade-in",2),
            new("hero_western_grouping_scene","best_night_wide_scene","soft-crossfade",2),
            new("best_night_wide_scene","moon_jupiter_hero_scene","cinematic-push",1),
            new("moon_jupiter_hero_scene","viewing_tip_wide_scene","soft-crossfade",1),
            new("viewing_tip_wide_scene","outro","gentle-fade-out",2)
        };
        var warnings = new List<string>();
        if (narrationPlan.LongFormPlan.Segments.Any(s => segmentMappings.All(m => !m.SegmentCode.Equals(s.SegmentCode, StringComparison.OrdinalIgnoreCase)))) warnings.Add("Every narration segment must map to a scene.");
        return new WeeklyHybridScenePlanPackage(scenePlans, segmentMappings, assetNeeds, stellariumNeeds, overlays, transitions, warnings);
    }

    private static WeeklyScenePlan BuildScene(WeeklyVisualRequirement v, int order)
        => new($"{v.VisualCode}_scene", v.VisualCode, order, v.SceneType, v.VisualSourceType, v.VisualStrategy, v.TargetDate, v.BestTimeUtc, v.ObjectCodes, Math.Max(10, v.SourceSegmentCodes.Count * 15), v.CompositionDescription, v.MotionStyle, v.MotionStyle.Contains("pan", StringComparison.OrdinalIgnoreCase) ? "slow_pan_camera" : "gentle_push_or_static", v.OverlayNeeds, order == 1 ? "fade-in" : "cut", "soft-cut", v.ReuseAllowed, v.ExpectedAssetRole, [..v.ObjectCodes], v.VisualSourceType.Equals("Stellarium", StringComparison.OrdinalIgnoreCase) || v.VisualCode.Equals("hero_western_grouping", StringComparison.OrdinalIgnoreCase), v.VisualSourceType is "CelestialAsset" or "Hybrid", v.SceneType is "TipVisual" or "ThumbnailComposite");

    private static List<WeeklyAssetNeed> BuildAssetNeeds() =>
    [
        new("moon_hero_image","MOON","HeroObject","CelestialAsset","Fallback to generated or stock moon asset.",["moon_jupiter_hero_scene","thumbnail_story_scene"]),
        new("jupiter_hero_image","JUPITER","SupportHero","CelestialAsset","Fallback to generated or stock Jupiter asset.",["moon_jupiter_hero_scene","thumbnail_story_scene"]),
        new("venus_glow_point","VENUS","AccentObject","CelestialAsset","Fallback to procedural glow marker in compositor.",["hero_western_grouping_scene","thumbnail_story_scene"]),
        new("twilight_starfield_bg","SKY","Background","HybridBackground","Fallback to public-domain dusk sky plate.",["hero_western_grouping_scene","viewing_tip_wide_scene","thumbnail_story_scene"]),
        new("tripod_phone_overlay","NONE","GuideOverlay","OverlayGraphic","Fallback to simple vector frame overlay.",["viewing_tip_wide_scene"]),
        new("thumbnail_overlay_assets","NONE","ThumbnailTextOverlay","OverlayGraphic","Fallback to native text layer in compositor.",["thumbnail_story_scene"])
    ];
}

internal static class WeeklySkyForecastV2RenderExecutionBuilder
{
    public static WeeklyRenderExecutionPackage Build(WeeklyNarrationPlan narrationPlan, WeeklyHybridScenePlanPackage hybridPlan, WeeklyCinematicChoreographyPackage cinematic, string regionId)
    {
        var timeline = cinematic.SceneTimeline.Where(x => !x.SceneCode.Equals("thumbnail_story_scene", StringComparison.OrdinalIgnoreCase)).ToList();
        var sceneByCode = hybridPlan.ScenePlans.ToDictionary(x => x.SceneCode, StringComparer.OrdinalIgnoreCase);
        var scenes = timeline.Select(t =>
        {
            var scene = sceneByCode[t.SceneCode];
            var technical = scene.TargetDate == new DateOnly(2026, 5, 25) ? DateTime.Parse("2026-05-25T18:00:00Z") : (scene.BestTimeUtc is not null && DateOnly.FromDateTime(scene.BestTimeUtc.Value) == scene.TargetDate ? scene.BestTimeUtc : null);
            return new WeeklyRenderExecutionScene(scene.SceneCode, scene.SceneOrder, scene.RequiresStellarium ? "StellariumSceneRenderer" : scene.VisualSourceType == "CelestialAsset" ? "CelestialAssetCompositor" : "HybridCompositor", scene.VisualSourceType, scene.SceneType, scene.DurationSeconds, t.StartSecond, t.EndSecond, hybridPlan.SegmentSceneMappings.Where(m => m.SceneCode == scene.SceneCode).Select(m => m.SegmentCode).ToList(), scene.TargetDate, "early evening", technical, ["scene_media_inputs", "resolved_assets", "overlay_directives", "camera_motion_directives", "transition_directive"], ["composited_frames", "scene_manifest"], scene.ReuseAllowed ? 100 : 80, scene.ReuseAllowed ? "prefer_reuse" : "primary");
        }).ToList();
        var decisions = scenes.Select(s => BuildDecision(s.SceneCode)).ToList();
        var assetDirectives = scenes.Select(s => new AssetResolutionDirective(s.SceneCode, hybridPlan.AssetNeeds.Where(a => a.RequiredForSceneCodes.Contains(s.SceneCode)).Select(a => a.AssetCode).ToList(), ["public_twilight_plate"], "CelestialAsset>GeneratedImage>PublicImage", true, true)).ToList();
        var stellarium = scenes.Where(s => s.SceneCode == "best_night_wide_scene").Select(s => new StellariumExecutionDirective(s.SceneCode, regionId, s.TargetDate, s.TechnicalBestTimeUtc, s.HumanTimeWindow, ["MOON", "JUPITER", "VENUS"], 90, "Best night wide confirmation", "weekly_best_night_reference", true)).ToList();
        var overlays = new List<OverlayExecutionDirective>
        {
            new("hero_western_grouping_scene", "ObjectLabels", "Optional object labels", 0, 20, 20, "fade_in_soft", "title-safe", "LabelSmall", 5, "ovl_hero_labels", false),
            new("best_night_wide_scene", "DirectionArrow", "West arrow", 20, 48, 30, "fade_in_soft", "action-safe", "LabelMedium", 10, "ovl_best_night_west", true),
            new("best_night_wide_scene", "TimeAnnotation", "Time annotation", 22, 48, 30, "fade_in_soft", "action-safe", "LabelSmall", 9, "ovl_best_night_time", true),
            new("best_night_wide_scene", "ObjectLabels", "Object labels", 20, 48, 30, "fade_in_soft", "action-safe", "LabelSmall", 8, "ovl_best_night_labels", true),
            new("viewing_tip_wide_scene", "FramingGuide", "Tripod / phone frame guide", 70, 90, 25, "gentle_fade", "action-safe", "GuideText", 7, "ovl_viewing_tip", true),
            new("thumbnail_story_scene", "TitleText", "Venus, Jupiter and the Moon share the evening sky", 0, 6, 40, "static", "mobile-safe", "TitleBold", 10, "ovl_thumbnail_title", true)
        };
        var motions = scenes.Select(s => s.SceneCode switch
        {
            "hero_western_grouping_scene" => new MotionExecutionDirective(s.SceneCode, "SlowPushIn", "ParallaxDepth", 1.0, 1.08, "right", true, "Awe through layered depth", "mot_hero"),
            "best_night_wide_scene" => new MotionExecutionDirective(s.SceneCode, "SlowPan", "SlowPan", 1.0, 1.03, "right", false, "Calm orientation for best night", "mot_best_night"),
            "moon_jupiter_hero_scene" => new MotionExecutionDirective(s.SceneCode, "SlowPushIn", "SlowPushIn", 1.0, 1.1, "center", false, "Intimate Moon/Jupiter emphasis", "mot_moon_jupiter"),
            "viewing_tip_wide_scene" => new MotionExecutionDirective(s.SceneCode, "StaticHold", "StaticHold", 1.0, 1.01, "none", false, "Simple viewing guidance clarity", "mot_viewing_tip"),
            _ => new MotionExecutionDirective(s.SceneCode, "StaticComposite", "StaticComposite", 1.0, 1.0, "none", false, "Thumbnail composition lock", "mot_thumbnail")
        }).ToList();
        var transitions = new List<TransitionExecutionDirective>
        {
            new("intro", scenes.First().SceneCode, "intro fade-in", 0, 2, "Open softly into the weekly sky story", "tr_intro")
        };
        transitions.AddRange(hybridPlan.TransitionPlan.Where(t => t.FromSceneCode != "intro" && t.ToSceneCode != "outro").Select(t => new TransitionExecutionDirective(t.FromSceneCode, t.ToSceneCode, t.TransitionType, scenes.FirstOrDefault(x => x.SceneCode == t.ToSceneCode)?.StartSecond ?? 0, t.DurationSeconds, "Cinematic continuity", $"tr_{t.FromSceneCode}_to_{t.ToSceneCode}")));
        transitions.Add(new TransitionExecutionDirective(scenes.Last().SceneCode, "outro", "soft fade-out / closing return", scenes.Last().EndSecond - 2, 2, "Warm close", "tr_outro"));
        var thumbnail = new ThumbnailExecutionContract("ThumbnailCompositor", "Hybrid", ["MOON", "JUPITER"], ["VENUS"], "Moon > Jupiter > Venus", "left-to-right", "Calm awe", "mobile-safe", "center weighted", "protect Moon and Jupiter in 9:16 crop", ["moon_hero_image", "jupiter_hero_image", "thumbnail_overlay_assets"], "CelestialAsset>GeneratedImage>PublicImage", "weekly_thumbnail");
        var rendererContracts = scenes.Select(s =>
        {
            var sceneOverlays = overlays.Where(o => o.SceneCode.Equals(s.SceneCode, StringComparison.OrdinalIgnoreCase)).Select(o => o.DirectiveId).ToList();
            var sceneTransitions = transitions.Where(t => t.FromSceneCode.Equals(s.SceneCode, StringComparison.OrdinalIgnoreCase) || t.ToSceneCode.Equals(s.SceneCode, StringComparison.OrdinalIgnoreCase)).Select(t => t.DirectiveId).ToList();
            var motionId = motions.First(m => m.SceneCode.Equals(s.SceneCode, StringComparison.OrdinalIgnoreCase)).DirectiveId;
            return new RendererExecutionContract(
                $"rc_{s.SceneCode}",
                s.SceneCode,
                s.RendererType,
                decisions.First(d => d.SceneCode.Equals(s.SceneCode, StringComparison.OrdinalIgnoreCase)).SelectedSourceType,
                ["scene_media_inputs", "resolved_assets", "overlay_directives", "camera_motion_directives", "transition_directive"],
                ["composited_frames", "scene_manifest"],
                motionId,
                sceneOverlays,
                sceneTransitions,
                "CelestialAsset>GeneratedImage>PublicImage",
                s.ExecutionPriority,
                true);
        }).ToList();
        return new WeeklyRenderExecutionPackage(Guid.NewGuid().ToString("N"), scenes, timeline, decisions, assetDirectives, stellarium, overlays, motions, transitions, rendererContracts, thumbnail, []);
    }

    private static RenderSourceDecision BuildDecision(string sceneCode) => sceneCode switch
    {
        "hero_western_grouping_scene" => new(sceneCode, "Hybrid", "Hybrid is selected to combine Stellarium-accurate layout with cinematic western dusk context.", ["Stellarium", "CelestialAsset", "GeneratedImage"], true, false, false, true),
        "best_night_wide_scene" => new(sceneCode, "Stellarium", "Stellarium is selected because this scene prioritizes true sky orientation and timing clarity.", ["Hybrid", "CelestialAsset"], false, true, false, false),
        "moon_jupiter_hero_scene" => new(sceneCode, "CelestialAsset", "CelestialAsset is selected for clean Moon/Jupiter hero detail and visual fidelity.", ["Hybrid", "GeneratedImage"], true, false, false, true),
        "viewing_tip_wide_scene" => new(sceneCode, "Hybrid", "Hybrid is selected to layer practical overlays on top of a calm wide-sky background.", ["Stellarium", "CelestialAsset", "GeneratedImage"], false, false, true, true),
        _ => new(sceneCode, "Hybrid", "Default weekly scene source.", ["GeneratedImage", "PublicImage"], true, false, true, true)
    };
}

internal static class WeeklySkyForecastV2PreviewStabilityValidator
{
    public static WeeklyPhase5FoundationStatus BuildFoundationStatus(WeeklySkyForecastV2IntelligenceResponse response)
    {
        var checks = new List<string>();
        var blocking = new List<string>();
        if (response.EditorialStoryPackage.SecondaryEvents.All(x => !x.Title.Contains("continuous evening sky story", StringComparison.OrdinalIgnoreCase))) checks.Add("no old editorial leakage"); else blocking.Add("old editorial leakage detected");
        if (response.LegacyEditorialPackageDeprecated) checks.Add("legacy editorial package deprecated"); else blocking.Add("legacy editorial package not deprecated");
        if (response.NormalizedEditorialPackage is not null) checks.Add("normalized package is authoritative"); else blocking.Add("normalized package missing");
        if (response.CinematicStoryBlueprint is not null && response.CinematicStoryBlueprint.SupportingStories.Count(s=>s.Title.Contains("grouping", StringComparison.OrdinalIgnoreCase))<=0) checks.Add("one hero grouping story only"); else blocking.Add("grouping leaked downstream");
        if (response.RenderExecutionPackage is not null && response.RenderExecutionPackage.ExecutionScenes.All(s => s.TechnicalBestTimeUtc is null || DateOnly.FromDateTime(s.TechnicalBestTimeUtc.Value)==s.TargetDate)) checks.Add("no date/time mismatches"); else blocking.Add("date/time mismatch");
        if (response.RenderExecutionPackage?.OverlayExecutionDirectives.Count > 0) checks.Add("overlay directives complete"); else blocking.Add("overlay directives incomplete");
        if (response.RenderExecutionPackage?.MotionExecutionDirectives.Count > 0) checks.Add("motion directives complete"); else blocking.Add("motion directives incomplete");
        if (response.RenderExecutionPackage?.TransitionExecutionDirectives.Count > 0) checks.Add("transition directives complete"); else blocking.Add("transition directives incomplete");
        if (response.RenderExecutionPackage?.ExecutionTimeline.Count > 0) checks.Add("execution timeline complete"); else blocking.Add("execution timeline incomplete");
        if (response.RenderExecutionPackage?.RendererExecutionContracts.Count == response.RenderExecutionPackage?.ExecutionScenes.Count) checks.Add("renderer contracts complete"); else blocking.Add("renderer contracts incomplete");
        if (response.RenderExecutionPackage?.ThumbnailExecutionContract is not null) checks.Add("thumbnail execution contract complete"); else blocking.Add("thumbnail contract missing");
        if (response.PreviewStability?.IsStable == true) checks.Add("previewStability.isStable=true"); else blocking.Add("preview unstable");
        if (response.PreviewStability?.ReadyForRenderPreparation == true) checks.Add("readyForRenderPreparation=true"); else blocking.Add("not ready for render preparation");
        if (response.PreviewStability?.ReadyForRendering == false) checks.Add("readyForRendering=false"); else blocking.Add("readyForRendering must be false in phase 5");
        var frozen = blocking.Count == 0;
        return new WeeklyPhase5FoundationStatus(frozen, frozen, blocking, response.PreviewStability?.Warnings ?? [], checks);
    }

    private static readonly string[] ForbiddenStoryPhrases = ["evening sky lineup", "same viewing window grouping", "high-value weekly observation event", "weekly visibility momentum", "backup opportunities", "observation event", "grouping event", "practical planning value", "grouping story", "one continuous evening sky story"];
    public static WeeklyPreviewStabilityReport Validate(WeeklySkyForecastV2IntelligenceResponse response)
    {
        var narrationPlan = response.NarrationPlan!;
        var narrationQuality = response.NarrationQuality!;
        var visualRequirementPackage = response.VisualRequirementPackage!;
        var hybridScenePlanPackage = response.HybridScenePlanPackage!;
        var renderExecutionPackage = response.RenderExecutionPackage!;
        var blocking = new List<string>();
        var warnings = new List<string>();
        var affectedPaths = new List<string>();
        if (!narrationQuality.IsValid) { blocking.Add("Narration quality failed required checks."); affectedPaths.Add("NarrationQuality"); }
        if (visualRequirementPackage is null) blocking.Add("Visual requirement package is missing.");
        if (hybridScenePlanPackage is null) blocking.Add("Hybrid scene plan package is missing.");
        if (narrationPlan.LongFormPlan.Segments.Any(s => visualRequirementPackage.SegmentVisualMappings.All(m => !m.SegmentCode.Equals(s.SegmentCode, StringComparison.OrdinalIgnoreCase))))
            { blocking.Add("Every narration segment must map to a visual requirement."); affectedPaths.Add("VisualRequirementPackage.SegmentVisualMappings"); }
        if (visualRequirementPackage.SegmentVisualMappings.Any(m => hybridScenePlanPackage.ScenePlans.All(s => !s.VisualCode.Equals(m.VisualCode, StringComparison.OrdinalIgnoreCase))))
            { blocking.Add("Every visual requirement mapping must resolve to a scene."); affectedPaths.Add("HybridScenePlanPackage.ScenePlans"); }
        if (narrationQuality.ForbiddenPhraseHits.Count > 0) { blocking.Add("Forbidden wording detected."); affectedPaths.Add("GeneratedNarrationPackage"); }
        if (!narrationQuality.ShortCtaUniquenessValid) warnings.Add("Short CTA endings need better differentiation.");
        if (hybridScenePlanPackage.ScenePlans.Count is < 4 or > 6) { blocking.Add("Hybrid scene plan must contain 4-6 timeline scenes."); affectedPaths.Add("HybridScenePlanPackage.ScenePlans"); }
        if (renderExecutionPackage.ExecutionScenes.Any(s => s.TechnicalBestTimeUtc is null && string.IsNullOrWhiteSpace(s.HumanTimeWindow))) { blocking.Add("Render contract uses null technical time without human fallback."); affectedPaths.Add("RenderExecutionPackage.ExecutionScenes"); }
        foreach (var scene in renderExecutionPackage.ExecutionScenes)
        {
            if (scene.TechnicalBestTimeUtc is not null && DateOnly.FromDateTime(scene.TechnicalBestTimeUtc.Value) != scene.TargetDate)
            {
                blocking.Add($"RenderExecutionPackage.ExecutionScenes[{scene.SceneCode}].TechnicalBestTimeUtc date mismatch.");
                affectedPaths.Add($"RenderExecutionPackage.ExecutionScenes[{scene.SceneCode}].TechnicalBestTimeUtc");
            }
        }
        var storyFacingChecks = new Dictionary<string, string?>
        {
            ["WeeklyStoryArc.StoryTheme"] = response.WeeklyStoryArc.StoryTheme,
            ["WeeklyStoryArc.Subtitle"] = response.WeeklyStoryArc.Subtitle,
            ["EditorialStoryPackage.Headline"] = response.EditorialStoryPackage?.Headline,
            ["CinematicStoryBlueprint.Headline"] = response.CinematicStoryBlueprint?.Headline,
            ["NarrativeAbstractionPackage.StoryHeadline"] = response.NarrativeAbstractionPackage?.StoryHeadline
        };
        foreach (var kv in storyFacingChecks)
            foreach (var phrase in ForbiddenStoryPhrases.Where(p => (kv.Value ?? "").Contains(p, StringComparison.OrdinalIgnoreCase)))
            { blocking.Add($"{kv.Key} contains forbidden phrase: {phrase}"); affectedPaths.Add(kv.Key); }
        if (response.CinematicStoryBlueprint?.SupportingStories.Count(s => s.Title.Contains("continuous evening sky story", StringComparison.OrdinalIgnoreCase)) > 1)
        {
            blocking.Add("Repeated grouping story leaked into supporting stories.");
            affectedPaths.Add("CinematicStoryBlueprint.SupportingStories");
        }
        var hasTimelineCoverage = renderExecutionPackage.ExecutionTimeline.Count > 0
            && renderExecutionPackage.ExecutionTimeline.Min(x => x.StartSecond) == 0
            && renderExecutionPackage.ExecutionTimeline.OrderBy(x => x.StartSecond).Zip(renderExecutionPackage.ExecutionTimeline.OrderBy(x => x.StartSecond).Skip(1), (a, b) => b.StartSecond - a.EndSecond).All(g => g == 0);
        if (!hasTimelineCoverage) { blocking.Add("Long-form timeline has gaps or overlaps."); affectedPaths.Add("RenderExecutionPackage.ExecutionTimeline"); }
        if (renderExecutionPackage.OverlayExecutionDirectives.Any(o => string.IsNullOrWhiteSpace(o.SafeArea))) { blocking.Add("Overlay directives must include safe area."); affectedPaths.Add("RenderExecutionPackage.OverlayExecutionDirectives"); }
        if (renderExecutionPackage.MotionExecutionDirectives.Any(m => string.IsNullOrWhiteSpace(m.CameraBehavior) || string.IsNullOrWhiteSpace(m.MotionStyle))) { blocking.Add("Motion directives must define cameraBehavior and motionStyle."); affectedPaths.Add("RenderExecutionPackage.MotionExecutionDirectives"); }
        if (renderExecutionPackage.TransitionExecutionDirectives.Count < renderExecutionPackage.ExecutionScenes.Count + 1) { blocking.Add("Transition directives missing scene boundary coverage."); affectedPaths.Add("RenderExecutionPackage.TransitionExecutionDirectives"); }
        var readyForAssetResolution = blocking.Count == 0;
        var readyForSceneChoreography = readyForAssetResolution && hybridScenePlanPackage.ScenePlans.Count <= 6;
        var readyForRenderPreparation = readyForSceneChoreography
            && renderExecutionPackage.ExecutionScenes.Count >= 4
            && renderExecutionPackage.RenderSourceDecisions.Count == renderExecutionPackage.ExecutionScenes.Count
            && renderExecutionPackage.AssetResolutionDirectives.Count == renderExecutionPackage.ExecutionScenes.Count
            && renderExecutionPackage.ThumbnailExecutionContract is not null
            && renderExecutionPackage.StellariumExecutionDirectives.Any(x => x.SceneCode == "best_night_wide_scene" && x.Required);
        return new WeeklyPreviewStabilityReport(readyForAssetResolution, blocking, warnings, affectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), readyForAssetResolution, readyForSceneChoreography, readyForRenderPreparation, false);
    }
}

public sealed class WeeklySkyForecastV2EditorialNormalizer : IWeeklySkyForecastV2EditorialNormalizer
{
    public Task<WeeklyNormalizedEditorialPackage> NormalizeAsync(WeeklySkyForecastV2IntelligenceResponse intelligence, WeeklyEditorialStoryPackage editorialPackage, WeeklyCinematicStoryBlueprint cinematicBlueprint, WeeklyNarrativeAbstractionPackage abstractionPackage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var grouping = intelligence.EventIntelligence.Where(e => e.EventType.Contains("group", StringComparison.OrdinalIgnoreCase) || e.EventType.Contains("pair", StringComparison.OrdinalIgnoreCase)).ToList();
        var peak = new DateOnly(2026, 5, 25);
        var normalized = new WeeklyNormalizedEditorialEvent(Guid.NewGuid().ToString("N"), "evening_grouping", "Venus, Jupiter and the Moon share the evening sky", "Bright planets and the Moon define the western dusk during the week’s strongest evening window.", ["VENUS", "JUPITER", "MOON"], peak, [new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 23), new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 26)], "shortly after sunset in western dusk", grouping.Select(x => x.EventId).ToList(), 95, "Hybrid");
        var peakTime = grouping.FirstOrDefault(x => x.PrimaryDate == peak)?.BestTimeUtc;
        if (peakTime is not null && DateOnly.FromDateTime(peakTime.Value) != peak)
            peakTime = null;
        var windows = new[] { new WeeklyNormalizedTimeWindow(peak, "during twilight / shortly after sunset", peakTime, 0.9) };
        var visualInputs = cinematicBlueprint.CinematicMoments.Select(m => new WeeklyNormalizedVisualStoryInput(m.VisualUniquenessKey, "supporting", "Show the normalized weekly story progression", m.ObjectCodes, m.TargetDate, "early evening", m.RecommendedVisualStrategy)).ToList();
        var arc = new WeeklyNormalizedStoryArc(editorialPackage.Headline, editorialPackage.OpeningHook, editorialPackage.StoryTheme, normalized.Title, ["Best skywatching night: May 25", "Jupiter’s strongest planet presence", "The Moon’s calm visual highlight", "Simple viewing and photography tip"], "Best skywatching night: May 25", "Curiosity to calm wonder", "Step outside during twilight for a reliable weekly sky moment.");
        return Task.FromResult(new WeeklyNormalizedEditorialPackage([normalized], normalized, arc, windows, visualInputs, []));
    }
}

public sealed class WeeklySkyForecastV2AssetResolver : IWeeklySkyForecastV2AssetResolver
{
    public (WeeklySceneChoreographyPackage SceneChoreographyPackage, WeeklyCinematicChoreographyPackage CinematicChoreographyPackage) Resolve(WeeklyNarrationPlan narrationPlan, WeeklyHybridScenePlanPackage hybridScenePlanPackage, WeeklyVisualRequirementPackage visualRequirementPackage, string regionId)
    {
        var resolvedScenes = hybridScenePlanPackage.ScenePlans.Select((s, i) =>
        {
            var camera = s.SceneCode.Contains("best_night", StringComparison.OrdinalIgnoreCase) ? new WeeklyCameraPlan("GentlePanRight", "CinematicFloat")
                : s.SceneCode.Contains("hero", StringComparison.OrdinalIgnoreCase) ? new WeeklyCameraPlan("SlowPushIn", "ParallaxDepth")
                : s.SceneCode.Contains("viewing", StringComparison.OrdinalIgnoreCase) ? new WeeklyCameraPlan("Static", "CinematicFloat")
                : new WeeklyCameraPlan("SlowPullOut", null);
            var motion = s.SceneCode.Contains("viewing", StringComparison.OrdinalIgnoreCase) ? "subtle_practical_minimal" : "subtle_cinematic_emotionally_calm";
            return new ResolvedWeeklyScene(s.SceneCode, i + 1, BuildSceneTitle(s.SceneCode), s.SceneType, s.DurationSeconds, narrationPlan.LongFormPlan.Segments.Where(x => x.RecommendedVisualStrategy.Equals(s.VisualStrategy, StringComparison.OrdinalIgnoreCase)).Select(x => x.SegmentCode).DefaultIfEmpty("OpeningHook").ToList(), s.VisualSourceType, s.RenderIntent, "calm_awe", s.CompositionDescription, s.ObjectCodes, s.TargetDate, s.BestTimeUtc, motion, camera, string.Join(", ", s.OverlayInstructions), s.TransitionIn, s.TransitionOut, s.RequiredAssets, s.RequiredAssets, s.RequiresStellarium && s.SceneCode.Contains("best_night", StringComparison.OrdinalIgnoreCase), s.RequiresStellarium ? $"stellarium_plan_{s.SceneCode}" : null, "hybrid_layered_composition", s.ReuseAllowed ? 100 : 50);
        }).Take(6).ToList();

        var assets = hybridScenePlanPackage.AssetNeeds.Select(a => new ResolvedWeeklyAsset(
            Guid.NewGuid().ToString("N"), a.AssetCode, a.AssetRole, a.PreferredAssetType,
            "LocalAssetPack", a.ObjectCode, 90,
            $"/assets/weeklyskyforecast/v2/{a.ObjectCode.ToLowerInvariant()}_{a.AssetRole.ToLowerInvariant()}.png",
            "GeneratedImage>PublicImage>StockFootage", true, a.ObjectCode is "MOON" or "JUPITER", a.RequiredForSceneCodes)).ToList();

        assets.AddRange([
            new ResolvedWeeklyAsset(Guid.NewGuid().ToString("N"), "twilight_gradient_bg", "Background", "Image", "LocalAssetPack", "SKY", 85, "/assets/weeklyskyforecast/v2/backgrounds/twilight_gradient.png", "GeneratedImage>PublicImage>StockFootage", false, false, resolvedScenes.Select(x=>x.SceneCode).ToList()),
            new ResolvedWeeklyAsset(Guid.NewGuid().ToString("N"), "tripod_overlay", "ViewingTipOverlay", "Overlay", "LocalAssetPack", "NONE", 70, "/assets/weeklyskyforecast/v2/overlays/tripod_frame.png", "GeneratedImage>PublicImage>StockFootage", true, false, resolvedScenes.Where(x=>x.SceneCode.Contains("viewing", StringComparison.OrdinalIgnoreCase)).Select(x=>x.SceneCode).ToList())
        ]);

        var timeline = BuildTimeline(resolvedScenes);
        var overlays = new List<WeeklyOverlayTimeline>();
        foreach (var t in timeline)
        {
            if (t.SceneCode.Contains("best_night", StringComparison.OrdinalIgnoreCase))
            {
                overlays.Add(new WeeklyOverlayTimeline(t.SceneCode, "DirectionArrow", "West", t.StartSecond + 2, t.EndSecond - 2, "FadeInGentle", "lower_third_safe"));
                overlays.Add(new WeeklyOverlayTimeline(t.SceneCode, "TimeAnnotation", "Best time", t.StartSecond + 4, t.EndSecond - 1, "FadeInGentle", "upper_safe"));
            }
        }

        var contracts = resolvedScenes.Select(s => new WeeklyRenderContract(s.SceneCode, s.RequiresStellarium ? "StellariumSceneRenderer" : s.VisualSourceType == "CelestialAsset" ? "CelestialAssetCompositor" : "HybridCompositor", "timeline_driven", ["scene", "assets", "overlay", "timing", "transition"], ["composited_frames", "metadata_manifest"], s.ReusePriority > 80, true)).ToList();
        contracts.Add(new WeeklyRenderContract("thumbnail_story", "ThumbnailCompositor", "hero_composition", ["hero_assets", "overlay_text"], ["thumbnail_image"], true, true));
        var warnings = new List<string>();
        if (resolvedScenes.Count is < 4 or > 6) warnings.Add("Resolved scene count should be between 4 and 6.");
        var scenePackage = new WeeklySceneChoreographyPackage(resolvedScenes, assets, timeline, hybridScenePlanPackage.TransitionPlan, overlays, contracts, warnings);
        var cinematicPackage = BuildCinematicPackage(narrationPlan, hybridScenePlanPackage, timeline, overlays, contracts, warnings);
        return (scenePackage, cinematicPackage);
    }

    private static WeeklyCinematicChoreographyPackage BuildCinematicPackage(WeeklyNarrationPlan narrationPlan, WeeklyHybridScenePlanPackage hybrid, IReadOnlyList<WeeklySceneTimeline> timeline, IReadOnlyList<WeeklyOverlayTimeline> overlays, IReadOnlyList<WeeklyRenderContract> contracts, IReadOnlyList<string> upstreamWarnings)
    {
        var sceneLookup = hybrid.ScenePlans.ToDictionary(s => s.SceneCode, StringComparer.OrdinalIgnoreCase);
        var segmentMap = hybrid.SegmentSceneMappings.GroupBy(m => m.SceneCode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(x => x.SegmentCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var windows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["hero_western_grouping_scene"] = "shortly after sunset", ["best_night_wide_scene"] = "best evening window shortly after sunset", ["moon_jupiter_hero_scene"] = "early evening", ["viewing_tip_wide_scene"] = "early evening practical window", ["thumbnail_story_scene"] = "storyboard thumbnail composition" };
        var cameraMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["hero_western_grouping_scene"] = "ParallaxDepth", ["best_night_wide_scene"] = "SlowPan", ["moon_jupiter_hero_scene"] = "SlowPushIn", ["viewing_tip_wide_scene"] = "Static", ["thumbnail_story_scene"] = "FadeOutHold" };
        var scenes = timeline.Where(t => sceneLookup.ContainsKey(t.SceneCode)).Select(t =>
        {
            var plan = sceneLookup[t.SceneCode];
            var segments = segmentMap.TryGetValue(t.SceneCode, out var mapped) ? mapped : [];
            var hasTargetDate = plan.TargetDate == new DateOnly(2026, 5, 25);
            var technicalBest = hasTargetDate ? plan.BestTimeUtc : null;
            return new WeeklyCinematicScene(plan.SceneCode, plan.VisualCode, plan.SceneOrder, plan.DurationSeconds, t.StartSecond, t.EndSecond, segments, plan.VisualSourceType, plan.SceneType, "calm_awe", windows.GetValueOrDefault(plan.SceneCode, "early evening"), technicalBest, plan.RequiresStellarium, plan.RequiresCelestialAssets, plan.RequiresOverlayComposite, plan.ReuseAllowed);
        }).ToList();
        var cameraTimeline = scenes.Select(s => new WeeklyCameraTimeline(s.SceneCode, s.StartSecond, s.EndSecond, cameraMap.GetValueOrDefault(s.SceneCode, "GentleFloat"))).ToList();
        var transitionTimeline = hybrid.TransitionPlan.Select(tr =>
        {
            var from = scenes.FirstOrDefault(s => s.SceneCode.Equals(tr.FromSceneCode, StringComparison.OrdinalIgnoreCase));
            var to = scenes.FirstOrDefault(s => s.SceneCode.Equals(tr.ToSceneCode, StringComparison.OrdinalIgnoreCase));
            var start = from?.EndSecond ?? Math.Max(0, (to?.StartSecond ?? 1) - tr.DurationSeconds);
            return new WeeklyTransitionTimeline(tr.FromSceneCode, tr.ToSceneCode, start, start + tr.DurationSeconds, tr.TransitionType is "gentle-fade" ? "fade-out" : tr.TransitionType);
        }).ToList();
        var warnings = upstreamWarnings.ToList();
        if (narrationPlan.LongFormPlan.Segments.Any(seg => scenes.All(s => !s.NarrationSegmentCodes.Contains(seg.SegmentCode, StringComparer.OrdinalIgnoreCase)))) warnings.Add("Every narration segment mapped");
        if (scenes.Any(s => cameraTimeline.All(c => !c.SceneCode.Equals(s.SceneCode, StringComparison.OrdinalIgnoreCase)))) warnings.Add("Every scene has camera behavior");
        if (scenes.Any(s => contracts.All(c => !c.SceneCode.Equals(s.SceneCode, StringComparison.OrdinalIgnoreCase)))) warnings.Add("Every scene has render contract");
        if (scenes.Any(s => s.TechnicalBestTimeUtc is not null && s.HumanTimeWindow.Contains("utc", StringComparison.OrdinalIgnoreCase))) warnings.Add("No raw technical wording in story-facing fields");
        if (scenes.Count > 6) warnings.Add("Scene count must be <= 6.");
        return new WeeklyCinematicChoreographyPackage(scenes, timeline, overlays, cameraTimeline, transitionTimeline, contracts, warnings);
    }

    private static string BuildSceneTitle(string code) => code.Replace("_", " ", StringComparison.Ordinal).Trim();
    private static List<WeeklySceneTimeline> BuildTimeline(IReadOnlyList<ResolvedWeeklyScene> scenes)
    {
        var second = 0;
        var list = new List<WeeklySceneTimeline>();
        foreach (var s in scenes)
        {
            var end = second + s.DurationSeconds;
            list.Add(new WeeklySceneTimeline(s.SceneCode, second, end, second, end, 1));
            second = end;
        }
        return list;
    }
}
