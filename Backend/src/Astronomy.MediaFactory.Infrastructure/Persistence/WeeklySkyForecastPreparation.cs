using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastProductionPipelineStrategy : ICategoryProductionPipelineStrategy
{
    public string ContentCategoryCode => "WeeklySkyForecast";
    public Task<CategoryProductionPreviewResponse> RunAsync(CategoryProductionPreviewRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new CategoryProductionPreviewResponse(null, ContentCategoryCode, false, false, false, false, false, false, null, null, null, null, null, null, null, null, null, null, [], ["WeeklySkyForecast production preview execution is intentionally disabled in this phase."], "Not implemented in planning foundation phase.", null));
}

public sealed class WeeklySkyForecastContextBuilder(IOptions<SchedulerOptions> schedulerOptions, ISkyfieldSidecarClient sidecarClient) : IWeeklySkyForecastContextBuilder
{
    public async Task<WeeklySkyForecastContext> BuildAsync(WeeklySkyForecastProductionRequest request, CancellationToken cancellationToken)
    {
        var region = schedulerOptions.Value.Regions.Items.FirstOrDefault(x => x.RegionId == request.RegionId) ?? throw new InvalidOperationException($"Region '{request.RegionId}' not configured.");
        var weekStart = DateOnly.FromDateTime(request.ScheduledUtc.UtcDateTime);
        var skyfieldRequest = new Astronomy.MediaFactory.AstroData.Clients.WeeklySkyForecastSkyfieldRequest { RegionId = request.RegionId, LocationName = request.RegionName, Latitude = region.Latitude, Longitude = region.Longitude, Timezone = region.Timezone, WeekStartDate = weekStart.ToString("yyyy-MM-dd"), Days = 7, Language = request.Language };
        var response = await sidecarClient.GetWeeklySkyForecastAsync(skyfieldRequest, cancellationToken) ?? throw new InvalidOperationException("Skyfield weekly forecast sidecar returned no response.");
        if (!response.Success)
        {
            throw new InvalidOperationException($"Skyfield weekly forecast failed: {response.ErrorMessage ?? "unknown error"}");
        }

        var daily = response.Days.Select(d => new DailySkyForecastContextItem(DateOnly.Parse(d.Date), d.SunsetUtc, d.SunriseUtc, d.MoonPhase, d.MoonIlluminationPercent, d.MoonRiseUtc, d.MoonSetUtc,
            d.VisibleObjects.Select(v => new WeeklySkyForecastVisibleObjectItem(v.ObjectCode, v.ObjectName, v.ObjectType, v.Visible, v.RiseUtc, v.SetUtc, v.TransitUtc, v.MaxAltitudeDegrees, v.BestViewingTimeUtc, v.VisibilityScore, v.PhotographyScore, v.ViewingDirection, v.Reason)).ToList(),
            d.Events.Select(e => new WeeklySkyForecastEventItem(e.EventType, e.Title, e.Description, e.EventTimeUtc, e.ImportanceScore, e.ViralityScore, e.PrimaryObjectCode, e.ViewingDirection, e.ViewingTip)).ToList(),
            d.BestViewingStartUtc, d.BestViewingEndUtc, d.OverallViewingScore, d.ViewingSummary)).ToList();
        var highlights = response.WeeklyHighlights.Select(x => new WeeklySkyForecastHighlightItem(x.Order, x.HighlightType, x.Title, x.Description, DateOnly.Parse(x.Date), x.BestTimeUtc, x.ObjectCode, x.Score, x.SuggestedSceneType)).ToList();
        var recommended = response.RecommendedNights.Select(x => new Astronomy.MediaFactory.Core.RecommendedObservationNight(DateOnly.Parse(x.Date), x.Score, x.Reason, x.BestObjects, x.BestStartUtc, x.BestEndUtc)).ToList();
        var bestPlanet = daily.SelectMany(d => d.VisibleObjects).Where(o => o.Visible && o.ObjectType.Equals("Planet", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.VisibilityScore).Select(x => x.ObjectCode).FirstOrDefault();
        var bestMoonNight = daily.OrderByDescending(d => d.VisibleObjects.Where(o => o.ObjectCode.Equals("Moon", StringComparison.OrdinalIgnoreCase)).Select(o => o.VisibilityScore).DefaultIfEmpty(0).Max()).Select(d => (DateOnly?)d.Date).FirstOrDefault();
        var bestPhotoNight = daily.OrderByDescending(d => d.VisibleObjects.MaxBy(o => o.PhotographyScore)?.PhotographyScore ?? 0).Select(d => (DateOnly?)d.Date).FirstOrDefault();

        return new(request.RegionId, response.LocationName, region.Latitude, region.Longitude, response.Timezone, DateOnly.Parse(response.WeekStartDate), DateOnly.Parse(response.WeekEndDate), request.Language, daily, highlights, recommended, bestPlanet, bestMoonNight, bestPhotoNight, response.Warnings);
    }
}

public sealed class WeeklySkyForecastSegmentPlanner : IWeeklySkyForecastSegmentPlanner
{
    public Task<WeeklySkyForecastSegmentPlan> BuildAsync(WeeklySkyForecastContext context, CancellationToken cancellationToken)
    {
        var bestNight = context.RecommendedNights.FirstOrDefault();
        var longSegments = new List<WeeklySkyForecastSegmentPlanItem>
        {
            new("WeeklyIntro", "Long", 1, "Weekly Intro", "Set weekly expectation", context.WeekStartDate, [], "Context", "WeeklyIntroWideSky", 35, 0.8),
            new("MoonPhaseForecast", "Long", 2, "Moon Phase Forecast", "Explain moon trend", context.BestMoonNight, ["Moon"], "Moon", "BestMoonNight", 45, 0.85),
            new("BestPlanets", "Long", 3, "Best Planets", "Rank top planets", bestNight?.Date, bestNight?.BestObjects ?? [], "Planet", "BestPlanetOfWeek", 50, 0.9),
            new("RecommendedNights", "Long", 4, "Recommended Nights", "Highlight best nights", bestNight?.Date, bestNight?.BestObjects ?? [], "Night", "RecommendedObservationNight", 40, 0.92),
            new("WeeklyHighlights", "Long", 5, "Weekly Highlights", "Cover ranked events", context.WeekStartDate, context.WeeklyHighlights.Select(x => x.ObjectCode).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct().ToList(), "Event", "WeeklyHighlight", 45, 0.88),
            new("AstroPhotographyTip", "Long", 6, "Astro Photography Tip", "Give practical tip", context.BestPhotographyNight, bestNight?.BestObjects ?? [], "Tip", "WeeklySummaryMap", 30, 0.7),
            new("WeeklyOutro", "Long", 7, "Weekly Outro", "Close and CTA", context.WeekEndDate, [], "Outro", "WeeklySummaryMap", 25, 0.6)
        };
        var shortSegments = new List<WeeklySkyForecastSegmentPlanItem>
        {
            new("BiggestWeeklyHighlight", "Short", 1, "Biggest Weekly Highlight", "Fast hook", bestNight?.Date, bestNight?.BestObjects ?? [], "Highlight", "WeeklyHighlight", 20, 0.95),
            new("BestViewingNight", "Short", 2, "Best Viewing Night", "Tell best night", bestNight?.Date, bestNight?.BestObjects ?? [], "Night", "RecommendedObservationNight", 18, 0.92),
            new("BestPlanetOfWeek", "Short", 3, "Best Planet Of Week", "Focus top planet", bestNight?.Date, [context.BestPlanetOfWeek ?? "Moon"], "Planet", "BestPlanetOfWeek", 18, 0.9),
            new("QuickOutro", "Short", 4, "Quick Outro", "CTA", context.WeekEndDate, [], "Outro", "WeeklySummaryMap", 10, 0.5)
        };
        return Task.FromResult(new WeeklySkyForecastSegmentPlan(longSegments, shortSegments));
    }
}

public sealed class WeeklySkyForecastSscScenePlanner : IWeeklySkyForecastSscScenePlanner
{
    public Task<WeeklySkyForecastSscScenePlan> BuildAsync(WeeklySkyForecastContext context, WeeklySkyForecastSegmentPlan segmentPlan, CancellationToken cancellationToken)
    {
        var visible = context.DailyForecasts.SelectMany(x => x.VisibleObjects).Where(x => x.Visible).ToList();
        var bestObject = visible.OrderByDescending(x => x.VisibilityScore).FirstOrDefault()?.ObjectCode;
        var thumb = context.BestPlanetOfWeek ?? (visible.Any(x => x.ObjectCode == "Moon") ? "Moon" : bestObject);
        var targetDate = context.RecommendedNights.FirstOrDefault()?.Date ?? context.WeekStartDate;
        var capture = visible.FirstOrDefault(x => x.ObjectCode == thumb)?.BestViewingTimeUtc ?? DateTime.UtcNow;
        var scenes = new List<WeeklySkyForecastSscScenePlanItem>
        {
            new("WeeklyIntroWideSky", "WideSky", null, capture, context.WeekStartDate, 90, "long", false, "WeeklyIntro"),
            new("BestMoonNight", "Moon", "Moon", capture, context.BestMoonNight ?? targetDate, 45, "both", false, "MoonPhaseForecast"),
            new("BestPlanetOfWeek", "Planet", context.BestPlanetOfWeek, capture, targetDate, 35, "both", false, "BestPlanets"),
            new("RecommendedObservationNight", "Night", bestObject, capture, targetDate, 60, "both", false, "RecommendedNights"),
            new("WeeklyHighlight", "Highlight", context.WeeklyHighlights.FirstOrDefault()?.ObjectCode, capture, targetDate, 50, "both", false, "WeeklyHighlights"),
            new("WeeklySummaryMap", "Summary", null, capture, context.WeekEndDate, 95, "long", false, "WeeklyOutro"),
            new("ThumbnailCandidate", "Thumbnail", thumb, capture, targetDate, 30, "thumbnail", true, "BiggestWeeklyHighlight")
        };
        return Task.FromResult(new WeeklySkyForecastSscScenePlan(scenes));
    }
}

public sealed class CategoryOutputPathResolver(IOptions<RenderingOptions> renderingOptions) : ICategoryOutputPathResolver
{
    public CategoryOutputPaths Resolve(string categoryName, DateOnly date, string regionId, Guid pipelineRunId)
    {
        var root = Path.Combine(renderingOptions.Value.WorkingDirectory, categoryName, date.ToString("yyyy-MM-dd"), regionId.ToLowerInvariant(), pipelineRunId.ToString());
        return new(root, Path.Combine(root, "narration"), Path.Combine(root, "shorts"), Path.Combine(root, "thumbnails"), Path.Combine(root, "stellarium-scenes"), Path.Combine(root, "stellarium-scripts"), Path.Combine(root, "manifests"), Path.Combine(root, "metadata"));
    }
}

public sealed class WeeklySkyForecastMetadataBuilder : IWeeklySkyForecastMetadataBuilder
{
    public Task<WeeklySkyForecastMetadataSkeleton> BuildAsync(WeeklySkyForecastContext context, WeeklySkyForecastSegmentPlan segmentPlan, CancellationToken cancellationToken)
    {
        var keyObjects = context.DailyForecasts.SelectMany(x => x.VisibleObjects).Where(x => x.Visible).Select(x => x.ObjectCode).Distinct().Take(10).ToList();
        var keyDates = context.RecommendedNights.Select(x => x.Date.ToString("yyyy-MM-dd")).ToList();
        var weekRange = $"{context.WeekStartDate:yyyy-MM-dd} to {context.WeekEndDate:yyyy-MM-dd}";
        var skeleton = new WeeklySkyForecastMetadataSkeleton([$"Weekly Sky Forecast for {context.LocationName} ({weekRange})"], ["Best Nights This Week"], "This week in the night sky: highlights, best nights and top objects.", ["weekly sky forecast", context.LocationName.ToLowerInvariant()], ["#WeeklySkyForecast", "#NightSky"], keyObjects, keyDates, weekRange, context.LocationName, context.Language);
        return Task.FromResult(skeleton);
    }
}

public sealed class WeeklySkyForecastPreparationOrchestrator(IContentPlanningService planning, IWeeklySkyForecastContextBuilder contextBuilder, IWeeklySkyForecastSegmentPlanner segmentPlanner, IWeeklySkyForecastSscScenePlanner scenePlanner, ICategoryOutputPathResolver pathResolver, IWeeklySkyForecastMetadataBuilder metadataBuilder) : IWeeklySkyForecastPreparationOrchestrator
{
    public async Task<WeeklySkyForecastPreparationResponse> RunAsync(WeeklySkyForecastProductionRequest request, CancellationToken cancellationToken)
    {
        var steps = new List<CategoryProductionStepResult>();
        var plan = await planning.GenerateDailyPlanAsync("WeeklySkyForecast", request.Language, request.RegionId, request.ScheduledUtc, null, cancellationToken);
        steps.Add(Step("BuildContentGenerationPlan"));
        var context = await contextBuilder.BuildAsync(request, cancellationToken);
        steps.Add(Step("BuildWeeklyAstronomyContext"));
        var segmentPlan = await segmentPlanner.BuildAsync(context, cancellationToken);
        steps.Add(Step("GenerateSegmentPlans"));
        var scenes = await scenePlanner.BuildAsync(context, segmentPlan, cancellationToken);
        steps.Add(Step("GenerateSscScenePlan"));
        var outputPaths = pathResolver.Resolve("WeeklySkyForecast", context.WeekStartDate, context.RegionId, plan.Id);
        steps.Add(Step("BuildOutputPaths"));
        var metadata = await metadataBuilder.BuildAsync(context, segmentPlan, cancellationToken);
        steps.Add(Step("BuildMetadataSkeleton"));
        var warnings = context.Warnings.Concat(["Publishing disabled by policy.", "Analytics disabled by policy."]).Distinct().ToList();
        return new(plan.Id, "WeeklySkyForecast", context.WeekStartDate, context.WeekEndDate, context, segmentPlan.LongSegments, segmentPlan.ShortSegments, scenes.Scenes, outputPaths, metadata, warnings, steps, false, false);
    }

    private static CategoryProductionStepResult Step(string name) => new(name, "Completed", DateTime.UtcNow, DateTime.UtcNow, 0, null, null, []);
}
