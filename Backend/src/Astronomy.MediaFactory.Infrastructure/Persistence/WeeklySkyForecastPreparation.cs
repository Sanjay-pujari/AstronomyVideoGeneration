using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastProductionPipelineStrategy : ICategoryProductionPipelineStrategy
{
    public string ContentCategoryCode => "WeeklySkyForecast";
    public Task<CategoryProductionPreviewResponse> RunAsync(CategoryProductionPreviewRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new CategoryProductionPreviewResponse(null, ContentCategoryCode, false, false, false, false, false, false, null, null, null, null, null, null, null, null, null, null, [], ["WeeklySkyForecast production preview execution is intentionally disabled in this phase."], "Not implemented in planning foundation phase.", null));
}

public sealed class WeeklySkyForecastContextBuilder(IAstronomyVisibilityService visibilityService) : IWeeklySkyForecastContextBuilder
{
    public async Task<WeeklySkyForecastContext> BuildAsync(WeeklySkyForecastSkyfieldRequest request, CancellationToken cancellationToken)
    {
        var daily = new List<AstronomyVisibilityResult>();
        var warnings = new List<string>();
        for (var i = 0; i < request.Days; i++)
        {
            var date = request.WeekStartDate.AddDays(i);
            var result = await visibilityService.CalculateVisibilityAsync(new AstronomyVisibilityRequest(request.RegionId, request.LocationName, request.Latitude, request.Longitude, request.Timezone, date, request.PreferredObjectCodes?.FirstOrDefault(), request.Language), cancellationToken);
            daily.Add(result);
            warnings.AddRange(result.Warnings);
        }

        var recommended = daily.OrderByDescending(x => x.VisibleObjects.Sum(v => v.VisibilityScore)).Take(3)
            .Select(x => new RecommendedObservationNight(x.TargetDate, x.VisibleObjects.Sum(v => v.VisibilityScore), "High aggregate visibility.", x.VisibleObjects.OrderByDescending(v => v.VisibilityScore).Take(3).Select(v => v.ObjectCode).ToList(), x.BestViewingStartUtc, x.BestViewingEndUtc)).ToList();
        var highlights = recommended.Select((x, i) => $"#{i + 1} {x.Date:yyyy-MM-dd} score {x.Score:F1}").ToList();
        return new(request, request.WeekStartDate.AddDays(request.Days - 1), daily, highlights, recommended, warnings.Distinct().ToList());
    }
}

public sealed class WeeklySkyForecastSegmentPlanner : IWeeklySkyForecastSegmentPlanner
{
    public (IReadOnlyList<WeeklySkyForecastSegmentPlanItem> LongSegments, IReadOnlyList<WeeklySkyForecastSegmentPlanItem> ShortSegments) Build(WeeklySkyForecastContext context)
    {
        var bestNight = context.RecommendedObservationNights.FirstOrDefault();
        List<WeeklySkyForecastSegmentPlanItem> l = [
            new("WeeklyIntro","LongForm","Week overview",[],context.Request.WeekStartDate,"WeeklyIntroWideSky","intro",90),
            new("MoonPhaseForecast","LongForm","Moon phase forecast",[],context.Request.WeekStartDate,"MoonPhaseHighlight","moon",80),
            new("BestPlanets","LongForm","Planet visibility",bestNight?.BestObjects ?? [],bestNight?.Date,"BestPlanetNight","planet",88),
            new("BestObservationNights","LongForm","Top nights",bestNight?.BestObjects ?? [],bestNight?.Date,"BestObservationNight","night",92),
            new("MajorEvents","LongForm","Major events",[],context.Request.WeekStartDate,"NotableEventScene","event",75),
            new("AstroPhotographyTip","LongForm","Photography tip",bestNight?.BestObjects ?? [],bestNight?.Date,"WeeklySummaryMap","tip",65),
            new("WeeklySummary","LongForm","Summary",[],context.WeekEndDate,"WeeklySummaryMap","summary",85)
        ];
        List<WeeklySkyForecastSegmentPlanItem> s = [
            new("BiggestHighlight","ShortForm","Top highlight",bestNight?.BestObjects ?? [],bestNight?.Date,"NotableEventScene","highlight",95),
            new("BestViewingNight","ShortForm","Best night",bestNight?.BestObjects ?? [],bestNight?.Date,"BestObservationNight","night",92),
            new("TopObject","ShortForm","Top object",bestNight?.BestObjects.Take(1).ToList() ?? [],bestNight?.Date,"BestPlanetNight","object",90),
            new("CtaOutro","ShortForm","CTA",[],context.WeekEndDate,"WeeklySummaryMap","outro",70)
        ];
        return (l, s);
    }
}

public sealed class WeeklySkyForecastSscScenePlanner : IWeeklySkyForecastSscScenePlanner
{
    public IReadOnlyList<WeeklySkyForecastSscScenePlanItem> Build(WeeklySkyForecastContext context, IReadOnlyList<WeeklySkyForecastSegmentPlanItem> longSegments, IReadOnlyList<WeeklySkyForecastSegmentPlanItem> shortSegments)
    {
        var d = context.Request.WeekStartDate;
        return [
            new("WeeklyIntroWideSky","Overview",null,DateTime.UtcNow,d,"90deg","long",false),
            new("MoonPhaseHighlight","Moon",null,DateTime.UtcNow,d.AddDays(1),"45deg","long",false),
            new("BestPlanetNight","Planet",context.RecommendedObservationNights.FirstOrDefault()?.BestObjects.FirstOrDefault(),DateTime.UtcNow,d.AddDays(2),"35deg","both",false),
            new("BestObservationNight","Night",null,DateTime.UtcNow,d.AddDays(3),"70deg","both",true),
            new("NotableEventScene","Event",null,DateTime.UtcNow,d.AddDays(4),"50deg","both",false),
            new("WeeklySummaryMap","Summary",null,DateTime.UtcNow,d.AddDays(6),"95deg","long",false)
        ];
    }
}

public sealed class CategoryOutputPathResolver(IOptions<RenderingOptions> renderingOptions) : ICategoryOutputPathResolver
{
    public CategoryOutputPaths Resolve(string categoryName, DateOnly date, string regionId, Guid pipelineRunId)
    {
        var root = Path.Combine(renderingOptions.Value.WorkingDirectory, categoryName, date.ToString("yyyy-MM-dd"), regionId, pipelineRunId.ToString());
        return new(root, Path.Combine(root, "narration"), Path.Combine(root, "shorts"), Path.Combine(root, "thumbnails"), Path.Combine(root, "stellarium-scenes"), Path.Combine(root, "stellarium-scripts"), Path.Combine(root, "manifests"), Path.Combine(root, "metadata"));
    }
}

public sealed class WeeklySkyForecastMetadataBuilder : IWeeklySkyForecastMetadataBuilder
{
    public WeeklySkyForecastMetadataSkeleton Build(WeeklySkyForecastContext context, IReadOnlyList<WeeklySkyForecastSegmentPlanItem> longSegments, IReadOnlyList<WeeklySkyForecastSegmentPlanItem> shortSegments)
        => new([
            $"Weekly Sky Forecast: {context.Request.LocationName} ({context.Request.WeekStartDate:MMM dd} - {context.WeekEndDate:MMM dd})",
            $"{context.Request.LocationName} Night Sky Weekly Guide"
        ], ["Weekly Sky Highlights", "Best Nights This Week"], ["#WeeklySkyForecast", "#Astronomy", "#NightSky"], ["weekly sky forecast", context.Request.LocationName.ToLowerInvariant(), "moon phase"], string.Join("; ", context.WeeklyHighlights), context.DailyForecasts.SelectMany(x => x.VisibleObjects).Select(x => x.ObjectCode).Distinct().Take(10).ToList(), []);
}

public sealed class WeeklySkyForecastPreparationOrchestrator(MediaFactoryDbContext db, IContentPlanningService planning, IOptions<SchedulerOptions> schedulerOptions, IWeeklySkyForecastContextBuilder contextBuilder, IWeeklySkyForecastSegmentPlanner segmentPlanner, IWeeklySkyForecastSscScenePlanner scenePlanner, ICategoryOutputPathResolver pathResolver, IWeeklySkyForecastMetadataBuilder metadataBuilder) : IWeeklySkyForecastPreparationOrchestrator
{
    public async Task<WeeklySkyForecastPreparationResponse> RunAsync(WeeklySkyForecastPreparationRequest request, CancellationToken cancellationToken)
    {
        var steps = new List<CategoryProductionStepResult>();
        var warnings = new List<string>();
        var plan = await planning.GenerateDailyPlanAsync("WeeklySkyForecast", request.Language, request.RegionId, request.ScheduledUtc, null, cancellationToken);
        steps.Add(Step("BuildContentGenerationPlan"));
        var region = schedulerOptions.Value.Regions.Items.FirstOrDefault(x => x.RegionId == request.RegionId);
        var req = new WeeklySkyForecastSkyfieldRequest(request.RegionId, request.RegionName, region?.Latitude ?? 24.5854, region?.Longitude ?? 73.7125, region?.Timezone ?? "Asia/Kolkata", DateOnly.FromDateTime(request.ScheduledUtc.UtcDateTime), 7, request.Language, []);
        steps.Add(Step("BuildWeeklySkyfieldRequest"));
        var ctx = await contextBuilder.BuildAsync(req, cancellationToken); steps.Add(Step("BuildWeeklyAstronomyContext"));
        var (longSegments, shortSegments) = segmentPlanner.Build(ctx); steps.Add(Step("GenerateSegmentPlans"));
        var scenes = scenePlanner.Build(ctx, longSegments, shortSegments); steps.Add(Step("GenerateSscScenePlan"));
        var outputPaths = pathResolver.Resolve("WeeklySkyForecast", req.WeekStartDate, req.RegionId, plan.Id); steps.Add(Step("BuildOutputPaths"));
        var metadata = metadataBuilder.Build(ctx, longSegments, shortSegments); steps.Add(Step("BuildMetadataSkeleton"));
        warnings.AddRange(ctx.Warnings);
        warnings.Add("Publishing disabled by policy.");
        warnings.Add("Analytics disabled by policy.");
        return new(plan.Id, req.WeekStartDate, ctx.WeekEndDate, longSegments, shortSegments, scenes, outputPaths, metadata, ctx.WeeklyHighlights, ctx.RecommendedObservationNights, warnings.Distinct().ToList(), steps);
    }

    private static CategoryProductionStepResult Step(string name) => new(name, "Completed", DateTime.UtcNow, DateTime.UtcNow, 0, null, null, []);
}
