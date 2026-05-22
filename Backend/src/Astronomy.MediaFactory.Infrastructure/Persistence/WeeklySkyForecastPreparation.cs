using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastProductionPipelineStrategy : ICategoryProductionPipelineStrategy
{
    public string ContentCategoryCode => "WeeklySkyForecast";
    public Task<CategoryProductionPreviewResponse> RunAsync(CategoryProductionPreviewRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new CategoryProductionPreviewResponse(null, ContentCategoryCode, false, false, false, false, false, false, null, null, null, null, null, null, null, null, null, null, [], ["WeeklySkyForecast production preview execution is intentionally disabled in this phase."], "Not implemented in planning foundation phase.", null));
}

public sealed class WeeklySkyForecastContextBuilder(
    IOptions<SchedulerOptions> schedulerOptions,
    IRegionResolutionService regionResolutionService,
    ISkyfieldSidecarClient sidecarClient,
    ILogger<WeeklySkyForecastContextBuilder> logger) : IWeeklySkyForecastContextBuilder
{
    public async Task<WeeklySkyForecastContext> BuildAsync(WeeklySkyForecastProductionRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Resolving WeeklySkyForecast region using production region resolver.");
        logger.LogInformation("Requested regionId: {RequestedRegionId}", request.RegionId);

        var resolution = await regionResolutionService.TryResolveAsync(request.RegionId, request.RegionName, cancellationToken);
        var availableRegionIds = schedulerOptions.Value.Regions.Items
            .Where(r => !string.IsNullOrWhiteSpace(r.RegionId))
            .Select(r => RegionIdNormalizer.NormalizeRegionId(r.RegionId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (resolution is null)
        {
            throw new WeeklySkyForecastRegionResolutionException(
                requestedRegionId: RegionIdNormalizer.NormalizeRegionId(request.RegionId),
                availableRegionIds: availableRegionIds,
                message: $"Region '{RegionIdNormalizer.NormalizeRegionId(request.RegionId)}' is not configured in region settings. Resolver: IRegionResolutionService.");
        }

        logger.LogInformation(
            "Resolved region: {RegionId} ({DisplayName}) lat={Latitude}, lon={Longitude}, tz={Timezone}",
            resolution.CanonicalRegionId,
            resolution.LocationName,
            resolution.Latitude,
            resolution.Longitude,
            resolution.Timezone);

        var weekStart = DateOnly.FromDateTime(request.ScheduledUtc.UtcDateTime);
        var resolvedLocationName = string.IsNullOrWhiteSpace(resolution.LocationName) ? request.RegionName : resolution.LocationName;
        var skyfieldRequest = new Astronomy.MediaFactory.AstroData.Clients.WeeklySkyForecastSkyfieldRequest { RegionId = resolution.CanonicalRegionId, LocationName = resolvedLocationName, Latitude = resolution.Latitude, Longitude = resolution.Longitude, Timezone = resolution.Timezone, WeekStartDate = weekStart.ToString("yyyy-MM-dd"), Days = 7, Language = request.Language };
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
        var bestPlanet = !string.IsNullOrWhiteSpace(response.BestPlanetOfWeek)
            ? response.BestPlanetOfWeek
            : daily.SelectMany(d => d.VisibleObjects).Where(o => o.Visible && o.ObjectType.Equals("Planet", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.VisibilityScore).Select(x => x.ObjectCode).FirstOrDefault();
        var bestMoonNight = response.BestMoonNight is not null
            ? DateOnly.Parse(response.BestMoonNight.Date)
            : highlights.FirstOrDefault(x => x.HighlightType.Equals("best_moon_night", StringComparison.OrdinalIgnoreCase))?.Date;
        if (bestMoonNight is not null)
        {
            highlights = highlights
                .Where(x => !x.HighlightType.Equals("best_moon_night", StringComparison.OrdinalIgnoreCase))
                .Append(new WeeklySkyForecastHighlightItem(2, "best_moon_night", "Best moon night", "Strong moon presentation for visual observation.", bestMoonNight.Value, response.BestMoonNight?.BestStartUtc, "Moon", response.BestMoonNight?.Score ?? 0, "moon_closeup"))
                .OrderBy(x => x.Order)
                .ToList();
        }
        var bestPhotoNight = daily.OrderByDescending(d => d.VisibleObjects.MaxBy(o => o.PhotographyScore)?.PhotographyScore ?? 0).Select(d => (DateOnly?)d.Date).FirstOrDefault();

        return new(resolution.CanonicalRegionId, response.LocationName, resolution.Latitude, resolution.Longitude, resolution.Timezone, DateOnly.Parse(response.WeekStartDate), DateOnly.Parse(response.WeekEndDate), request.Language, daily, highlights, recommended, bestPlanet, bestMoonNight, bestPhotoNight, response.Warnings);
    }
}

public sealed class WeeklySkyForecastRegionResolutionException(string requestedRegionId, IReadOnlyList<string> availableRegionIds, string message) : KeyNotFoundException(message)
{
    public string RequestedRegionId { get; } = requestedRegionId;
    public IReadOnlyList<string> AvailableRegionIds { get; } = availableRegionIds;
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

public sealed class WeeklySkyForecastSscScenePlanner(ILogger<WeeklySkyForecastSscScenePlanner> logger) : IWeeklySkyForecastSscScenePlanner
{
    public Task<WeeklySkyForecastSscScenePlan> BuildAsync(WeeklySkyForecastContext context, WeeklySkyForecastSegmentPlan segmentPlan, CancellationToken cancellationToken)
    {
        var visible = context.DailyForecasts.SelectMany(x => x.VisibleObjects).Where(x => x.Visible).ToList();
        var bestObject = visible.OrderByDescending(x => x.VisibilityScore).FirstOrDefault()?.ObjectCode;
        var thumb = context.BestPlanetOfWeek ?? (visible.Any(x => x.ObjectCode == "Moon") ? "Moon" : bestObject);
        var targetDate = context.RecommendedNights.FirstOrDefault()?.Date ?? context.WeekStartDate;
        var targetTimeZone = ResolveTimeZoneOrUtc(context.Timezone);
        var bestNightByDate = context.RecommendedNights.ToDictionary(x => x.Date, x => x);

        DateTime ResolveCapture(DateOnly sceneTargetDate, string? targetObjectCode, bool isSummaryOrWide)
        {
            if (isSummaryOrWide && bestNightByDate.TryGetValue(sceneTargetDate, out var recommendedNight))
            {
                return recommendedNight.BestStartUtc;
            }

            var matchedObject = context.DailyForecasts
                .Where(x => x.Date == sceneTargetDate)
                .SelectMany(x => x.VisibleObjects)
                .FirstOrDefault(x => x.Visible && !string.IsNullOrWhiteSpace(x.BestViewingTimeUtc?.ToString()) && x.ObjectCode.Equals(targetObjectCode ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (matchedObject?.BestViewingTimeUtc is not null)
            {
                return matchedObject.BestViewingTimeUtc.Value;
            }

            if (bestNightByDate.TryGetValue(sceneTargetDate, out var fallbackNight))
            {
                return fallbackNight.BestStartUtc;
            }

            return context.DailyForecasts.FirstOrDefault(x => x.Date == sceneTargetDate)?.BestViewingStartUtc ?? DateTime.UtcNow;
        }
        var moonDate = context.BestMoonNight ?? targetDate;
        var moonNight = bestNightByDate.GetValueOrDefault(moonDate);
        var planetDate = context.DailyForecasts
            .FirstOrDefault(x => x.VisibleObjects.Any(o => o.Visible && o.ObjectCode.Equals(context.BestPlanetOfWeek ?? string.Empty, StringComparison.OrdinalIgnoreCase)))?.Date
            ?? targetDate;
        var summaryDate = targetDate;
        var recommendedDate = targetDate;
        var scenes = new List<WeeklySkyForecastSscScenePlanItem>
        {
            new("WeeklyIntroWideSky", "WideSky", null, ResolveCapture(context.WeekStartDate, null, true), context.WeekStartDate, 90, "long", false, "WeeklyIntro"),
            new("BestMoonNight", "Moon", "Moon", moonNight?.BestStartUtc ?? ResolveCapture(moonDate, "Moon", false), moonDate, 45, "both", false, "MoonPhaseForecast"),
            new("BestPlanetOfWeek", "Planet", context.BestPlanetOfWeek, ResolveCapture(planetDate, context.BestPlanetOfWeek, false), planetDate, 35, "both", false, "BestPlanets"),
            new("RecommendedObservationNight", "Night", bestObject, ResolveCapture(recommendedDate, bestObject, false), recommendedDate, 60, "both", false, "RecommendedNights"),
            new("WeeklyHighlight", "Highlight", context.WeeklyHighlights.FirstOrDefault()?.ObjectCode, ResolveCapture(recommendedDate, context.WeeklyHighlights.FirstOrDefault()?.ObjectCode, false), recommendedDate, 50, "both", false, "WeeklyHighlights"),
            new("WeeklySummaryMap", "Summary", null, ResolveCapture(summaryDate, null, true), summaryDate, 95, "long", false, "WeeklyOutro"),
            new("ThumbnailCandidate", "Thumbnail", thumb, ResolveCapture(recommendedDate, thumb, false), recommendedDate, 30, "thumbnail", true, "BiggestWeeklyHighlight")
        };

        foreach (var scene in scenes)
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(scene.CaptureTimeUtc.ToUniversalTime(), targetTimeZone));
            if (localDate != scene.TargetDate)
            {
                logger.LogWarning("WeeklySkyForecast scene targetDate mismatch: {SceneCode} targetDate={TargetDate} captureTimeUtc={CaptureTimeUtc} localDate={LocalDate}", scene.SceneCode, scene.TargetDate, scene.CaptureTimeUtc, localDate);
            }
        }
        return Task.FromResult(new WeeklySkyForecastSscScenePlan(scenes));
    }

    private static TimeZoneInfo ResolveTimeZoneOrUtc(string timezone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }
}

public sealed class CategoryOutputPathResolver(IOptions<RenderingOptions> renderingOptions) : ICategoryOutputPathResolver
{
    public CategoryOutputPaths Resolve(string categoryName, DateOnly date, string regionId, Guid pipelineRunId)
    {
        var normalizedRegionId = RegionIdNormalizer.NormalizeRegionId(regionId);
        var root = Path.Combine(renderingOptions.Value.WorkingDirectory, categoryName, date.ToString("yyyy-MM-dd"), normalizedRegionId.ToLowerInvariant(), pipelineRunId.ToString());
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
        var stopwatch = Stopwatch.StartNew();
        var plan = await planning.GenerateDailyPlanAsync("WeeklySkyForecast", request.Language, request.RegionId, request.ScheduledUtc, null, cancellationToken);
        steps.Add(Step("BuildContentGenerationPlan", stopwatch.ElapsedMilliseconds));
        stopwatch.Restart();
        var context = await contextBuilder.BuildAsync(request, cancellationToken);
        steps.Add(Step("BuildWeeklyAstronomyContext", stopwatch.ElapsedMilliseconds));
        stopwatch.Restart();
        var segmentPlan = await segmentPlanner.BuildAsync(context, cancellationToken);
        steps.Add(Step("GenerateSegmentPlans", stopwatch.ElapsedMilliseconds));
        stopwatch.Restart();
        var scenes = await scenePlanner.BuildAsync(context, segmentPlan, cancellationToken);
        steps.Add(Step("GenerateSscScenePlan", stopwatch.ElapsedMilliseconds));
        stopwatch.Restart();
        var outputPaths = pathResolver.Resolve("WeeklySkyForecast", context.WeekStartDate, context.RegionId, plan.Id);
        steps.Add(Step("BuildOutputPaths", stopwatch.ElapsedMilliseconds));
        stopwatch.Restart();
        var metadata = await metadataBuilder.BuildAsync(context, segmentPlan, cancellationToken);
        steps.Add(Step("BuildMetadataSkeleton", stopwatch.ElapsedMilliseconds));
        var warnings = context.Warnings.Concat(["Publishing disabled by policy.", "Analytics disabled by policy."]).Distinct().ToList();
        var validationWarnings = new List<string>();
        foreach (var scene in scenes.Scenes)
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(scene.CaptureTimeUtc.ToUniversalTime(), ResolveTimeZoneOrUtc(context.Timezone)));
            if (localDate != scene.TargetDate)
                validationWarnings.Add($"Scene timing mismatch for {scene.SceneCode}: targetDate={scene.TargetDate:yyyy-MM-dd} localCaptureDate={localDate:yyyy-MM-dd}.");
        }
        var errors = new List<string>();
        if (segmentPlan.LongSegments.Count < 6) errors.Add("longSegments must be >= 6.");
        if (segmentPlan.ShortSegments.Count < 3) errors.Add("shortSegments must be >= 3.");
        if (scenes.Scenes.Count < 5) errors.Add("sscScenes must be >= 5.");
        if (context.DailyForecasts.Count == 0) errors.Add("weekly context missing.");
        if (metadata.TitleCandidates.Count == 0) errors.Add("metadata skeleton missing.");
        if (string.IsNullOrWhiteSpace(outputPaths.RootDirectory)) errors.Add("output paths missing.");
        if (validationWarnings.Count > 0) errors.Add("scene timing mismatch detected.");
        var preparationValidation = new WeeklySkyForecastPreparationValidation(errors.Count == 0, errors, validationWarnings, segmentPlan.LongSegments.Count, segmentPlan.ShortSegments.Count, scenes.Scenes.Count, context.DailyForecasts.Count > 0, metadata.TitleCandidates.Count > 0, !string.IsNullOrWhiteSpace(outputPaths.RootDirectory));
        var debugSummary = new WeeklyForecastDebugSummary(context.RegionId, request.RegionId, "/forecast/weekly-sky", context.DailyForecasts.Count, context.DailyForecasts.Sum(x => x.VisibleObjects.Count), context.RecommendedNights.Count, context.WeeklyHighlights.Count, context.BestPlanetOfWeek, context.BestMoonNight, context.BestPhotographyNight);
        return new(plan.Id, "WeeklySkyForecast", context.WeekStartDate, context.WeekEndDate, context, segmentPlan.LongSegments, segmentPlan.ShortSegments, scenes.Scenes, outputPaths, metadata, preparationValidation, debugSummary, warnings, steps, false, false);
    }

    private static CategoryProductionStepResult Step(string name, long durationMs)
    {
        var started = DateTime.UtcNow.AddMilliseconds(-Math.Max(1, durationMs));
        var ended = DateTime.UtcNow;
        return new(name, "Completed", started, ended, Math.Max(1, durationMs), null, null, []);
    }

    private static TimeZoneInfo ResolveTimeZoneOrUtc(string timezone)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { return TimeZoneInfo.Utc; }
    }
}
