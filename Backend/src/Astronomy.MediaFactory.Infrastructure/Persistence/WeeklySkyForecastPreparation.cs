using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    ILogger<WeeklySkyForecastContextBuilder> logger) : IWeeklySkyForecastContextBuilderV2
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, WeeklySkyForecastContext> ContextCache = new(StringComparer.OrdinalIgnoreCase);
    internal const string CategoryDebugNormalizedObjectCountKey = "WeeklySkyForecast.NormalizedObjectCount";
    internal const string CategoryDebugCorrectedHighlightCountKey = "WeeklySkyForecast.CorrectedHighlightCount";
    internal const string CategoryDebugExcludedObjectCountKey = "WeeklySkyForecast.ExcludedObjectCount";

    public async Task<WeeklySkyForecastContext> BuildAsync(WeeklySkyForecastProductionRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("WeeklySkyForecast raw request payload: {RequestPayload}", JsonSerializer.Serialize(request));
        logger.LogInformation("WeeklySkyForecast parsed WeekStartDate={WeekStartDate}, WeekEndDate={WeekEndDate}", request.WeekStartDate, request.WeekEndDate);
        if (request.WeekStartDate == DateOnly.MinValue || request.WeekEndDate == DateOnly.MinValue)
            throw new ArgumentException("WeekStartDate and WeekEndDate are required and cannot be DateOnly.MinValue.");
        if (request.WeekEndDate < request.WeekStartDate)
            throw new ArgumentException("WeekEndDate must be greater than or equal to WeekStartDate.");

        var weekStart = request.WeekStartDate;
        var weekEnd = request.WeekEndDate;
        var cacheKey = $"{RegionIdNormalizer.NormalizeRegionId(request.RegionId)}|{request.Language}|{weekStart:yyyy-MM-dd}|{weekEnd:yyyy-MM-dd}";
        if (ContextCache.TryGetValue(cacheKey, out var cachedContext))
        {
            logger.LogInformation("Reusing cached WeeklySkyForecast context for key {CacheKey}", cacheKey);
            return cachedContext;
        }

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
        var resolvedLocationName = string.IsNullOrWhiteSpace(resolution.LocationName) ? request.RegionName : resolution.LocationName;
        logger.LogInformation("Skyfield weekly request payload: regionId={RegionId}, location={LocationName}, latitude={Latitude}, longitude={Longitude}, timezone={Timezone}, startDate={StartDate}, endDate={EndDate}", resolution.CanonicalRegionId, resolvedLocationName, resolution.Latitude, resolution.Longitude, resolution.Timezone, weekStart, weekEnd);
        logger.LogInformation("Resolved region for WeeklySkyForecast: {RegionId}", resolution.CanonicalRegionId);
        logger.LogInformation("WeeklySkyForecast start date: {StartDate}", weekStart);
        logger.LogInformation("WeeklySkyForecast end date: {EndDate}", weekEnd);

        var skyfieldRequest = new Astronomy.MediaFactory.AstroData.Clients.WeeklySkyForecastSkyfieldRequest { RegionId = resolution.CanonicalRegionId, LocationName = resolvedLocationName, Latitude = resolution.Latitude, Longitude = resolution.Longitude, Timezone = resolution.Timezone, WeekStartDate = weekStart.ToString("yyyy-MM-dd"), Days = 7, Language = request.Language };
        var successfulDays = new List<DailySkyForecastItem>();
        var failedDays = new List<object>();
        var debugWarnings = new List<string>();
        WeeklySkyForecastSkyfieldResponse? response = null;
        try
        {
            response = await sidecarClient.GetWeeklySkyForecastAsync(skyfieldRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Skyfield weekly API call threw; falling back to day-by-day requests.");
            debugWarnings.Add("Skyfield weekly API threw exception; used day-by-day fallback.");
        }

        if (response is not null && response.Success)
        {
            successfulDays.AddRange(response.Days);
        }
        else
        {
            debugWarnings.Add("Weekly Skyfield API failed; retrying day-by-day.");
            for (var offset = 0; offset < 7; offset++)
            {
                var day = weekStart.AddDays(offset);
                try
                {
                    var dailyForecast = await sidecarClient.GetDailySkyAsync(new SkyfieldDailySkyRequest
                    {
                        Date = day.ToString("yyyy-MM-dd"),
                        LocationName = resolvedLocationName,
                        Latitude = resolution.Latitude,
                        Longitude = resolution.Longitude,
                        Timezone = resolution.Timezone
                    }, cancellationToken);
                    if (dailyForecast is null)
                    {
                        failedDays.Add(new { date = day.ToString("yyyy-MM-dd"), error = "Daily Skyfield response was null." });
                        logger.LogWarning("Skyfield daily fallback failed for {Date}: null response.", day);
                        continue;
                    }

                    var targetDate = DateOnly.TryParse(dailyForecast.Date, out var parsedDate) ? parsedDate : day;
                    var startUtc = DateTime.UtcNow;
                    var endUtc = startUtc.AddHours(8);
                    var fallbackVisibleObjects = dailyForecast.Events
                        .Where(e => !string.IsNullOrWhiteSpace(e.ObjectName))
                        .GroupBy(e => e.ObjectName.Trim(), StringComparer.OrdinalIgnoreCase)
                        .Select(g => new VisibleObjectForecastItem
                        {
                            ObjectCode = WeeklySkyForecastObjectCodeResolver.NormalizeObjectCode(g.Key),
                            ObjectName = g.Key,
                            ObjectType = g.First().Category,
                            Visible = true,
                            ViewingDirection = g.First().Direction,
                            Reason = g.First().Details,
                            VisibilityScore = 0.5,
                            PhotographyScore = 0.5
                        })
                        .ToList();

                    successfulDays.Add(new DailySkyForecastItem
                    {
                        Date = targetDate.ToString("yyyy-MM-dd"),
                        SunsetUtc = startUtc,
                        SunriseUtc = endUtc,
                        MoonPhase = "",
                        MoonIlluminationPercent = 0,
                        MoonRiseUtc = null,
                        MoonSetUtc = null,
                        VisibleObjects = fallbackVisibleObjects,
                        Events = [],
                        BestViewingStartUtc = startUtc,
                        BestViewingEndUtc = endUtc,
                        OverallViewingScore = fallbackVisibleObjects.Count == 0 ? 0 : fallbackVisibleObjects.Average(x => x.VisibilityScore),
                        ViewingSummary = string.Join(" ", dailyForecast.VisualIdeas.Select(v => v.Description).Where(v => !string.IsNullOrWhiteSpace(v))).Trim()
                    });
                }
                catch (Exception ex)
                {
                    failedDays.Add(new { date = day.ToString("yyyy-MM-dd"), error = ex.Message });
                    logger.LogWarning(ex, "Skyfield daily fallback failed for {Date}.", day);
                }
            }

            response = new WeeklySkyForecastSkyfieldResponse
            {
                Success = successfulDays.Count > 0,
                RegionId = resolution.CanonicalRegionId,
                LocationName = resolvedLocationName,
                Timezone = resolution.Timezone,
                WeekStartDate = weekStart.ToString("yyyy-MM-dd"),
                WeekEndDate = weekEnd.ToString("yyyy-MM-dd"),
                Days = successfulDays.OrderBy(x => x.Date).ToList(),
                WeeklyHighlights = [],
                RecommendedNights = [],
                Warnings = [..debugWarnings],
                ErrorMessage = successfulDays.Count == 0 ? "Unable to compute weekly forecast for all requested days." : null
            };
        }

        logger.LogInformation("Skyfield daily fallback summary: successfulDays={SuccessfulDays}, failedDays={FailedDays}", successfulDays.Count, failedDays.Count);
        if (successfulDays.Count == 0)
        {
            throw new InvalidOperationException("Skyfield weekly forecast failed: Unable to compute weekly forecast for all requested days.");
        }

        if (failedDays.Count > 0)
        {
            response.Warnings.Add("Partial Skyfield weekly forecast used.");
        }
        response.Warnings = response.Warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        WeeklySkyForecastPreparationDiagnostics.SetJson("WeeklySkyForecast.SkyfieldWeeklyResponse", JsonSerializer.Serialize(response));
        WeeklySkyForecastPreparationDiagnostics.SetJson("WeeklySkyForecast.SkyfieldWeeklyErrors", JsonSerializer.Serialize(failedDays));

        var normalizedObjectCount = 0;
        var correctedHighlightCount = 0;
        var excludedObjectCount = 0;

        var daily = response.Days.Select(d => new DailySkyForecastContextItem(DateOnly.Parse(d.Date), d.SunsetUtc, d.SunriseUtc, d.MoonPhase, d.MoonIlluminationPercent, d.MoonRiseUtc, d.MoonSetUtc,
            d.VisibleObjects.Select(v =>
            {
                var normalizedCode = WeeklySkyForecastObjectCodeResolver.NormalizeObjectCode(v.ObjectCode);
                if (!string.Equals(normalizedCode, v.ObjectCode, StringComparison.Ordinal))
                    normalizedObjectCount++;
                return new WeeklySkyForecastVisibleObjectItem(normalizedCode, v.ObjectName, v.ObjectType, v.Visible, v.RiseUtc, v.SetUtc, v.TransitUtc, v.MaxAltitudeDegrees, v.BestViewingAzimuthDegrees, v.BestViewingTimeUtc, v.VisibilityScore, v.PhotographyScore, v.ViewingDirection, v.Reason);
            }).ToList(),
            d.Events.Select(e => new WeeklySkyForecastEventItem(e.EventType, e.Title, e.Description, e.EventTimeUtc, e.ImportanceScore, e.ViralityScore, e.PrimaryObjectCode, e.ViewingDirection, e.ViewingTip)).ToList(),
            d.BestViewingStartUtc, d.BestViewingEndUtc, d.OverallViewingScore, d.ViewingSummary)).ToList();
        var highlights = response.WeeklyHighlights.Select(x =>
        {
            var normalizedCode = WeeklySkyForecastObjectCodeResolver.NormalizeObjectCode(x.ObjectCode);
            if (!string.Equals(normalizedCode, x.ObjectCode, StringComparison.Ordinal))
                normalizedObjectCount++;
            var targetDate = DateOnly.Parse(x.Date);
            var bestTimeUtc = x.BestTimeUtc;
            if (bestTimeUtc.HasValue && DateOnly.FromDateTime(bestTimeUtc.Value) != targetDate)
            {
                bestTimeUtc = daily.Where(d => d.Date == targetDate)
                    .SelectMany(d => d.VisibleObjects)
                    .Where(o => o.Visible && o.BestViewingTimeUtc.HasValue && (string.IsNullOrWhiteSpace(normalizedCode) || o.ObjectCode.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(o => o.VisibilityScore)
                    .Select(o => o.BestViewingTimeUtc)
                    .FirstOrDefault() ?? bestTimeUtc;
                correctedHighlightCount++;
            }
            return new WeeklySkyForecastHighlightItem(x.Order, x.HighlightType, x.Title, x.Description, targetDate, bestTimeUtc, normalizedCode, x.Score, x.SuggestedSceneType);
        }).ToList();
        var recommended = response.RecommendedNights.Select(x => new Astronomy.MediaFactory.Core.RecommendedObservationNight(DateOnly.Parse(x.Date), x.Score, x.Reason, x.BestObjects.Select(WeeklySkyForecastObjectCodeResolver.NormalizeObjectCode).ToList(), x.BestStartUtc, x.BestEndUtc)).ToList();
        var bestPlanet = !string.IsNullOrWhiteSpace(response.BestPlanetOfWeek?.ObjectCode)
            ? WeeklySkyForecastObjectCodeResolver.NormalizeObjectCode(response.BestPlanetOfWeek.ObjectCode)
            : daily.SelectMany(d => d.VisibleObjects).Where(o => o.Visible && o.ObjectType.Equals("Planet", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.VisibilityScore).Select(x => x.ObjectCode).FirstOrDefault();
        var bestMoonNight = response.BestMoonNight is not null
            ? DateOnly.Parse(response.BestMoonNight.Date)
            : highlights.FirstOrDefault(x => x.HighlightType.Equals("best_moon_night", StringComparison.OrdinalIgnoreCase))?.Date;
        if (bestMoonNight is not null)
        {
            highlights = highlights
                .Where(x => !x.HighlightType.Equals("best_moon_night", StringComparison.OrdinalIgnoreCase))
                .Append(new WeeklySkyForecastHighlightItem(2, "best_moon_night", "Best moon night", "Strong moon presentation for visual observation.", bestMoonNight.Value, response.BestMoonNight?.BestStartUtc, "MOON", response.BestMoonNight?.Score ?? 0, "moon_closeup"))
                .OrderBy(x => x.Order)
                .ToList();
        }
        var bestPhotoNight = response.BestPhotographyNight is not null
            ? DateOnly.Parse(response.BestPhotographyNight.Date)
            : daily.OrderByDescending(d => d.VisibleObjects.MaxBy(o => o.PhotographyScore)?.PhotographyScore ?? 0).Select(d => (DateOnly?)d.Date).FirstOrDefault();

        foreach (var n in recommended)
            excludedObjectCount += n.BestObjects.Count(o => o is "MOON" or "SUN");
        WeeklySkyForecastPreparationDiagnostics.Set(CategoryDebugNormalizedObjectCountKey, normalizedObjectCount);
        WeeklySkyForecastPreparationDiagnostics.Set(CategoryDebugCorrectedHighlightCountKey, correctedHighlightCount);
        WeeklySkyForecastPreparationDiagnostics.Set(CategoryDebugExcludedObjectCountKey, excludedObjectCount);
        var builtContext = new WeeklySkyForecastContext(resolution.CanonicalRegionId, response.LocationName, resolution.Latitude, resolution.Longitude, resolution.Timezone, DateOnly.Parse(response.WeekStartDate), DateOnly.Parse(response.WeekEndDate), request.Language, daily, highlights.OrderBy(h => h.Order).Select((h, i) => h with { Order = i + 1 }).ToList(), recommended, bestPlanet, bestMoonNight, bestPhotoNight, response.Warnings);
        ContextCache[cacheKey] = builtContext;
        return builtContext;
    }

    public Task<WeeklySkyForecastContext> BuildAsync(WeeklySkyForecastV2OrchestrationContext context, CancellationToken cancellationToken)
    {
        if (context.WeeklyForecast is not null)
        {
            logger.LogInformation("Reusing cached WeeklySkyForecast context for pipelineRunId {pipelineRunId}", context.PipelineRunId);
            return Task.FromResult(context.WeeklyForecast);
        }
        var weekStartDate = context.Request.WeekStartDate ?? DateOnly.FromDateTime(context.Request.ScheduledUtc.UtcDateTime);
        var preservedRequest = new WeeklySkyForecastProductionRequest(
            context.Request.ContentCategoryCode,
            context.Request.Language,
            context.Request.RegionId,
            context.Request.RegionName,
            context.Request.ScheduledUtc,
            weekStartDate,
            weekStartDate.AddDays(6),
            GenerateNarration: context.Request.GenerateNarration,
            GenerateAudio: context.Request.GenerateAudio,
            GenerateSscScripts: context.Request.GenerateSscScripts,
            CaptureStellariumScenes: context.Request.CaptureStellariumScenes,
            GenerateSegmentVideos: context.Request.GenerateSegmentVideos,
            GenerateFinalVideos: context.Request.GenerateFinalVideos,
            DryRun: context.Request.DryRun,
            OverwriteExisting: context.Request.OverwriteExisting,
            PublishToYouTube: context.Request.PublishToYouTube,
            PublishToFacebook: context.Request.PublishToFacebook,
            PublishToInstagram: context.Request.PublishToInstagram,
            Diagnostics: context.Request.Diagnostics);
        logger.LogInformation("WeeklySkyForecast request snapshot before orchestration context build: {RequestPayload}", JsonSerializer.Serialize(preservedRequest));

        return BuildAsync(preservedRequest, cancellationToken);
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
            new("MoonPhaseForecast", "Long", 2, "Moon Phase Forecast", "Explain moon trend", context.BestMoonNight, ["MOON"], "Moon", "BestMoonNight", 45, 0.85),
            new("BestPlanets", "Long", 3, "Best Planets", "Rank top planets", bestNight?.Date, ((bestNight?.BestObjects ?? []).Where(o => !string.IsNullOrWhiteSpace(o)).ToList()).Where(o => !string.IsNullOrWhiteSpace(o) && o is not "MOON" and not "SUN").ToList(), "Planet", "BestPlanetOfWeek", 50, 0.9),
            new("RecommendedNights", "Long", 4, "Recommended Nights", "Highlight best nights", bestNight?.Date, (bestNight?.BestObjects ?? []).Where(o => !string.IsNullOrWhiteSpace(o)).ToList(), "Night", "BestObservationNightWide", 40, 0.92),
            new("WeeklyHighlights", "Long", 5, "Weekly Highlights", "Cover ranked events", context.WeekStartDate, context.WeeklyHighlights.Select(x => x.ObjectCode).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct().ToList(), "Event", "BestObservationNightWide", 45, 0.88),
            new("AstroPhotographyTip", "Long", 6, "Astro Photography Tip", "Give practical tip", context.BestPhotographyNight, (bestNight?.BestObjects ?? []).Where(o => !string.IsNullOrWhiteSpace(o)).ToList(), "Tip", "BestObservationNightWide", 30, 0.7),
            new("WeeklyOutro", "Long", 7, "Weekly Outro", "Close and CTA", context.WeekEndDate, [], "Outro", "WeeklyIntroWideSky", 25, 0.6)
        };
        var shortSegments = new List<WeeklySkyForecastSegmentPlanItem>
        {
            new("BiggestWeeklyHighlight", "Short", 1, "Biggest Weekly Highlight", "Fast hook", bestNight?.Date, (bestNight?.BestObjects ?? []).Where(o => !string.IsNullOrWhiteSpace(o)).ToList(), "Highlight", "BestObservationNightWide", 20, 0.95),
            new("BestViewingNight", "Short", 2, "Best Viewing Night", "Tell best night", bestNight?.Date, (bestNight?.BestObjects ?? []).Where(o => !string.IsNullOrWhiteSpace(o)).ToList(), "Night", "BestObservationNightWide", 18, 0.92),
            new("BestPlanetOfWeek", "Short", 3, "Best Planet Of Week", "Focus top planet", bestNight?.Date, [context.BestPlanetOfWeek ?? "JUPITER"], "Planet", "BestPlanetOfWeek", 18, 0.9),
            new("QuickOutro", "Short", 4, "Quick Outro", "CTA", context.WeekEndDate, [], "Outro", "WeeklyIntroWideSky", 10, 0.5)
        };
        return Task.FromResult(new WeeklySkyForecastSegmentPlan(longSegments, shortSegments));
    }
}

public sealed class LegacyWeeklyVisualAssetGenerator(ILogger<LegacyWeeklyVisualAssetGenerator> logger) : IWeeklySkyForecastSscScenePlanner
{
    private const int MaxInitialWeeklyScenes = 5;

    public Task<WeeklySkyForecastSscScenePlan> BuildAsync(WeeklySkyForecastContext context, WeeklySkyForecastSegmentPlan segmentPlan, CancellationToken cancellationToken)
    {
        var visible = context.DailyForecasts.SelectMany(x => x.VisibleObjects).Where(x => x.Visible).ToList();
        var bestObject = visible.OrderByDescending(x => x.VisibilityScore).FirstOrDefault()?.ObjectCode;
        var thumb = context.BestPlanetOfWeek ?? (visible.Any(x => x.ObjectCode == "MOON") ? "MOON" : bestObject);
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
        var bestPlanetDate = context.WeeklyHighlights
            .Where(h => h.HighlightType.Contains("planet", StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Date)
            .FirstOrDefault();
        var planetDate = (bestPlanetDate == default ? (DateOnly?)null : bestPlanetDate) ?? context.DailyForecasts
            .FirstOrDefault(x => x.VisibleObjects.Any(o => o.Visible && o.ObjectCode.Equals(context.BestPlanetOfWeek ?? string.Empty, StringComparison.OrdinalIgnoreCase)))?.Date
            ?? targetDate;
        var bestPlanetCapture = context.WeeklyHighlights
            .FirstOrDefault(h => h.Date == planetDate && h.ObjectCode.Equals(context.BestPlanetOfWeek ?? string.Empty, StringComparison.OrdinalIgnoreCase))?.BestTimeUtc
            ?? ResolveCapture(planetDate, context.BestPlanetOfWeek, false);
        var recommendedDate = targetDate;
        var scenes = new List<WeeklySkyForecastSscScenePlanItem>
        {
            new("WeeklyIntroWideSky", "WideSky", null, ResolveCapture(context.WeekStartDate, null, true), context.WeekStartDate, 90, "long", false, "WeeklyIntro"),
            new("BestMoonNight", "Moon", "MOON", moonNight?.BestStartUtc ?? ResolveCapture(moonDate, "MOON", false), moonDate, 45, "both", false, "MoonPhaseForecast"),
            new("BestPlanetOfWeek", "Planet", context.BestPlanetOfWeek, bestPlanetCapture, planetDate, 35, "both", false, "BestPlanets"),
            new("BestObservationNightWide", "Night", null, ResolveCapture(recommendedDate, null, true), recommendedDate, 90, "both", false, "RecommendedNights"),
            new("ThumbnailCandidate", "Thumbnail", thumb, ResolveCapture(recommendedDate, thumb, false), recommendedDate, 30, "thumbnail", true, "BiggestWeeklyHighlight")
        };
        if (scenes.Count > MaxInitialWeeklyScenes)
            throw new InvalidOperationException($"WeeklySkyForecast initial production must generate at most {MaxInitialWeeklyScenes} scenes.");

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
        var pipelineRunFolderName = pipelineRunId.ToString("N");
        var root = Path.Combine(renderingOptions.Value.WorkingDirectory, categoryName, date.ToString("yyyy-MM-dd"), normalizedRegionId.ToLowerInvariant(), pipelineRunFolderName);
        return new(root, Path.Combine(root, "narration"), Path.Combine(root, "shorts"), Path.Combine(root, "thumbnails"), Path.Combine(root, "stellarium", "scenes"), Path.Combine(root, "stellarium", "scripts"), Path.Combine(root, "manifests"), Path.Combine(root, "metadata"));
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

public sealed class WeeklySkyForecastPreparationOrchestrator(
    IContentPlanningService planning,
    IWeeklySkyForecastContextBuilder contextBuilder,
    IWeeklySkyForecastSegmentPlanner segmentPlanner,
    IWeeklySkyForecastSscScenePlanner scenePlanner,
    ICategoryOutputPathResolver pathResolver,
    IWeeklySkyForecastMetadataBuilder metadataBuilder,
    ISpeechSynthesisService speechSynthesisService,
    IStellariumScriptGenerator scriptGenerator,
    IStellariumImageCaptureExecutor captureExecutor,
    IWeeklySkyForecastSegmentVideoRenderer segmentVideoRenderer,
    IProcessRunner processRunner,
    IOptions<StellariumOptions> stellariumOptions) : IWeeklySkyForecastPreparationOrchestrator
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
        stopwatch.Restart();
        var flagsUsed = new WeeklySkyForecastExecutionFlags(
            request.GenerateNarration || request.GenerateAudio,
            request.GenerateAudio,
            request.GenerateSscScripts || request.CaptureStellariumScenes,
            request.CaptureStellariumScenes,
            request.GenerateSegmentVideos || request.GenerateFinalVideos,
            request.GenerateFinalVideos,
            request.DryRun,
            request.OverwriteExisting);
        var executionState = new WeeklySkyForecastExecutionState();
        WeeklyNarrationManifest? narrationManifest = null;
        string? narrationManifestPath = null;
        var audioSegments = new List<WeeklySkyForecastAudioSegmentResult>();
        var sscScripts = new List<WeeklySkyForecastSscScriptResult>();
        var visualAssets = new List<WeeklySkyForecastVisualAssetResult>();
        var captureResults = new List<WeeklySkyForecastCaptureResult>();
        string? longVideoPath = null;
        string? shortVideoPath = null;
        string? finalVideoManifestPath = null;
        WeeklySkyForecastFinalVideoResult? finalVideoResults = null;
        WeeklySkyForecastFinalVideoValidation? finalVideoValidation = null;

        if (flagsUsed.GenerateNarration)
        {
            var narrationPlan = BuildNarrationPlan(segmentPlan);
            stopwatch.Restart();
            (narrationManifest, narrationManifestPath) = await GenerateNarrationArtifactsAsync(context, segmentPlan, narrationPlan, outputPaths, flagsUsed.OverwriteExisting, cancellationToken);
            executionState.GeneratedAssets["narrationManifest"] = narrationManifestPath;
            executionState.Diagnostics["narrationGenerationMs"] = stopwatch.ElapsedMilliseconds;
            MarkCompleted(executionState, "GenerateNarration");
        }
        else MarkSkipped(executionState, "GenerateNarration");
        steps.Add(Step("GenerateNarration", executionState.Diagnostics.GetValueOrDefault("narrationGenerationMs", 1)));
        if (flagsUsed.GenerateAudio)
        {
            stopwatch.Restart();
            audioSegments = await GenerateAudioArtifactsAsync(narrationManifestPath, outputPaths, flagsUsed.OverwriteExisting, cancellationToken);
            executionState.GeneratedAssets["audioManifest"] = Path.Combine(outputPaths.ManifestsDirectory, "AudioManifest.json");
            executionState.Diagnostics["audioGenerationMs"] = stopwatch.ElapsedMilliseconds;
            MarkCompleted(executionState, "GenerateAudio");
        }
        else MarkSkipped(executionState, "GenerateAudio");
        steps.Add(Step("GenerateAudio", executionState.Diagnostics.GetValueOrDefault("audioGenerationMs", 1)));
        stopwatch.Restart();
        if (flagsUsed.GenerateSscScripts)
        {
            var canonicalSscScriptsDirectory = Path.Combine(stellariumOptions.Value.ScriptsDirectory, "content-plans", plan.Id.ToString());
            var canonicalStellariumCapturesDirectory = Path.Combine(stellariumOptions.Value.CaptureDirectory, "content-plans", plan.Id.ToString(), "stellarium-scenes");
            Directory.CreateDirectory(canonicalSscScriptsDirectory);
            Directory.CreateDirectory(canonicalStellariumCapturesDirectory);
            var capturePlan = new StellariumSceneCapturePlan(plan.Id, "WeeklySkyForecast", context.RegionId, context.LocationName, context.Latitude, context.Longitude, context.Timezone, context.WeekStartDate, [], []);
            foreach (var scene in scenes.Scenes.OrderBy(x => x.SceneCode))
            {
                var normalizedTarget = string.IsNullOrWhiteSpace(scene.TargetObjectCode) ? null : WeeklySkyForecastObjectCodeResolver.NormalizeObjectCode(scene.TargetObjectCode);
                capturePlan.Scenes.Add(new StellariumSceneCaptureItem(scene.SceneCode, scene.SceneType, scene.SceneCode, normalizedTarget, normalizedTarget, scene.CaptureTimeUtc, "Focus", scene.FieldOfViewDegrees, true, true, true, false, false, scene.OutputRole, capturePlan.Scenes.Count + 1, new Dictionary<string, string> { ["linkedSegmentCode"] = scene.LinkedSegmentCode }));
            }
            foreach (var scene in capturePlan.Scenes)
            {
                var generated = await scriptGenerator.GenerateAsync(capturePlan, scene, cancellationToken);
                var destinationScriptPath = Path.Combine(canonicalSscScriptsDirectory, $"{scene.SceneCode}.ssc");
                File.Copy(generated.ScriptPath, destinationScriptPath, true);
                var expectedImagePath = Path.Combine(canonicalStellariumCapturesDirectory, $"{scene.SceneCode}_{scene.OutputImageRole}.png");
                sscScripts.Add(new WeeklySkyForecastSscScriptResult(scene.SceneCode, destinationScriptPath, expectedImagePath, generated.Success, generated.ErrorMessage));
                visualAssets.Add(new WeeklySkyForecastVisualAssetResult(scene.SceneCode, expectedImagePath, scene.OutputImageRole, scene.Metadata.TryGetValue("linkedSegmentCode", out var linked) ? linked : string.Empty, scene.TargetObjectCode));
            }
            executionState.Diagnostics["sscGenerationMs"] = stopwatch.ElapsedMilliseconds;
            MarkCompleted(executionState, "GenerateSscScripts");
            if (flagsUsed.CaptureStellariumScenes)
            {
                stopwatch.Restart();
                if (flagsUsed.DryRun)
                {
                    captureResults.AddRange(visualAssets.Select(x => new WeeklySkyForecastCaptureResult(x.SceneCode, x.ExpectedImagePath, "Skipped/DryRun", false, null)));
                }
                else
                {
                    var captureResponse = await captureExecutor.CaptureAsync(capturePlan, new StellariumCaptureExecutionRequest(plan.Id, false, flagsUsed.OverwriteExisting, request.Diagnostics), cancellationToken);
                    foreach (var asset in visualAssets)
                    {
                        var exists = File.Exists(asset.ExpectedImagePath) && new FileInfo(asset.ExpectedImagePath).Length > 0;
                        captureResults.Add(new WeeklySkyForecastCaptureResult(asset.SceneCode, asset.ExpectedImagePath, exists ? "Captured" : "Missing", exists, exists ? null : "PNG image file missing after capture."));
                    }
                }
                executionState.Diagnostics["captureExecutionMs"] = stopwatch.ElapsedMilliseconds;
                MarkCompleted(executionState, "CaptureStellariumScenes");
            }
            else MarkSkipped(executionState, "CaptureStellariumScenes");
        }
        else
        {
            MarkSkipped(executionState, "GenerateSscScripts");
            MarkSkipped(executionState, "CaptureStellariumScenes");
        }
        steps.Add(Step("GenerateSscScripts", executionState.Diagnostics.GetValueOrDefault("sscGenerationMs", 1)));
        steps.Add(Step("CaptureStellariumScenes", executionState.Diagnostics.GetValueOrDefault("captureExecutionMs", 1)));

        WeeklySkyForecastSegmentVideoRenderResponse? segmentRender = null;
        if (flagsUsed.GenerateSegmentVideos && !flagsUsed.DryRun)
        {
            segmentRender = await segmentVideoRenderer.RenderAsync(plan.Id, new WeeklySkyForecastSegmentVideoRenderRequest(flagsUsed.OverwriteExisting, request.Diagnostics), cancellationToken);
            steps.Add(Step("GenerateSegmentVideos", 1));
        }
        else
        {
            steps.Add(Step("GenerateSegmentVideos", 1, "Skipped"));
        }

        if (flagsUsed.GenerateFinalVideos && !flagsUsed.DryRun)
        {
            var manifestPath = Path.Combine(outputPaths.ManifestsDirectory, "SegmentVideoManifest.json");
            var segmentManifest = JsonSerializer.Deserialize<List<WeeklySkyForecastSegmentVideoRenderItem>>(await File.ReadAllTextAsync(manifestPath, cancellationToken)) ?? [];
            var renderByCode = segmentManifest.Where(x => string.Equals(x.Status, "Rendered", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "Skipped", StringComparison.OrdinalIgnoreCase)).ToDictionary(x => x.SegmentCode, StringComparer.OrdinalIgnoreCase);
            var longSegmentPaths = segmentPlan.LongSegments.OrderBy(x => x.SortOrder).Select(x => renderByCode.TryGetValue(x.SegmentCode, out var it) ? it.VideoPath : string.Empty).ToList();
            var shortSegmentPaths = segmentPlan.ShortSegments.OrderBy(x => x.SortOrder).Select(x => renderByCode.TryGetValue(x.SegmentCode, out var it) ? it.VideoPath : string.Empty).ToList();
            var validationErrors = new List<string>();
            ValidateSegments(longSegmentPaths, "long", validationErrors);
            ValidateSegments(shortSegmentPaths, "short", validationErrors);
            if (validationErrors.Count == 0)
            {
                Directory.CreateDirectory(Path.Combine(outputPaths.RootDirectory, "final"));
                Directory.CreateDirectory(outputPaths.ShortsDirectory);
                longVideoPath = Path.Combine(outputPaths.RootDirectory, "final", "weekly-skyforecast-long.mp4");
                shortVideoPath = Path.Combine(outputPaths.ShortsDirectory, "weekly-skyforecast-short.mp4");
                var longCmd = await ComposeConcatVideoAsync(longSegmentPaths, longVideoPath, outputPaths.ManifestsDirectory, "weekly-long", cancellationToken);
                steps.Add(Step("ComposeLongVideo", 1));
                var shortCmd = await ComposeConcatVideoAsync(shortSegmentPaths, shortVideoPath, outputPaths.ManifestsDirectory, "weekly-short", cancellationToken);
                steps.Add(Step("ComposeShortVideo", 1));
                var duration = await ProbeDurationSecondsAsync(longVideoPath, cancellationToken);
                var finalVideoWarnings = new List<string>();
                finalVideoValidation = new WeeklySkyForecastFinalVideoValidation(validationErrors.Count == 0 && duration > 0, validationErrors, finalVideoWarnings, File.Exists(longVideoPath), File.Exists(shortVideoPath), duration);
                finalVideoResults = new WeeklySkyForecastFinalVideoResult(longVideoPath, shortVideoPath, longSegmentPaths.Concat(shortSegmentPaths).ToList(), duration, "1920x1080 / 1080x1920", finalVideoValidation.IsValid ? "Completed" : "CompletedWithErrors", $"{longCmd} | {shortCmd}", finalVideoWarnings, validationErrors);
                finalVideoManifestPath = Path.Combine(outputPaths.ManifestsDirectory, "FinalVideoManifest.json");
                await File.WriteAllTextAsync(finalVideoManifestPath, JsonSerializer.Serialize(finalVideoResults, JsonOptions), cancellationToken);
            }
            else
            {
                steps.Add(Step("ComposeLongVideo", 1, "Failed"));
                steps.Add(Step("ComposeShortVideo", 1, "Failed"));
                finalVideoValidation = new WeeklySkyForecastFinalVideoValidation(false, validationErrors, [], false, false, 0);
            }
            steps.Add(Step("ValidateFinalVideos", 1, finalVideoValidation?.IsValid == true ? "Completed" : "Failed"));
        }
        await PersistExecutionStateAsync(outputPaths, executionState, cancellationToken);
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
        if (narrationManifest is not null && narrationManifest.Segments.Any(x => string.IsNullOrWhiteSpace(x.NarrationText))) errors.Add("narration text cannot be empty.");
        if (flagsUsed.GenerateAudio && narrationManifest is not null && narrationManifest.GeneratedAudioCount != narrationManifest.NarrationSegmentCount) errors.Add("audio generation incomplete.");
        if (narrationManifest is not null && narrationManifest.FailedNarrationCount > 0) errors.Add("narration generation failures detected.");
        if (metadata.TitleCandidates.Count == 0) errors.Add("metadata skeleton missing.");
        if (string.IsNullOrWhiteSpace(outputPaths.RootDirectory)) errors.Add("output paths missing.");
        if (validationWarnings.Count > 0) errors.Add("scene timing mismatch detected.");
        var preparationValidation = new WeeklySkyForecastPreparationValidation(errors.Count == 0, errors, validationWarnings, segmentPlan.LongSegments.Count, segmentPlan.ShortSegments.Count, scenes.Scenes.Count, context.DailyForecasts.Count > 0, metadata.TitleCandidates.Count > 0, !string.IsNullOrWhiteSpace(outputPaths.RootDirectory));
        if (context.WeeklyHighlights.GroupBy(h => h.Order).Any(g => g.Count() > 1)) errors.Add("duplicate weekly highlight order.");
        if (context.DailyForecasts.SelectMany(d => d.VisibleObjects).Any(o => !string.IsNullOrWhiteSpace(o.ObjectCode) && o.ObjectCode != o.ObjectCode.ToUpperInvariant())) errors.Add("lowercase object code.");
        if (segmentPlan.LongSegments.Concat(segmentPlan.ShortSegments).Any(s => s.SegmentCode == "BestPlanets" && s.TargetObjectCodes.Any(code => code is "MOON" or "SUN"))) errors.Add("segment contains invalid object type.");
        if (context.WeeklyHighlights.Any(h => h.BestTimeUtc.HasValue && DateOnly.FromDateTime(h.BestTimeUtc.Value) != h.Date)) errors.Add("highlight date/time mismatch.");
        var debugSummary = new WeeklyForecastDebugSummary(context.RegionId, request.RegionId, "/forecast/weekly-sky", context.DailyForecasts.Count, context.DailyForecasts.Sum(x => x.VisibleObjects.Count), context.RecommendedNights.Count, context.WeeklyHighlights.Count, WeeklySkyForecastPreparationDiagnostics.Get(WeeklySkyForecastContextBuilder.CategoryDebugNormalizedObjectCountKey), WeeklySkyForecastPreparationDiagnostics.Get(WeeklySkyForecastContextBuilder.CategoryDebugCorrectedHighlightCountKey), WeeklySkyForecastPreparationDiagnostics.Get(WeeklySkyForecastContextBuilder.CategoryDebugExcludedObjectCountKey), context.BestPlanetOfWeek, context.BestMoonNight, context.BestPhotographyNight);
        return new(plan.Id, "WeeklySkyForecast", context.WeekStartDate, context.WeekEndDate, context, segmentPlan.LongSegments, segmentPlan.ShortSegments, scenes.Scenes, outputPaths, metadata, preparationValidation, debugSummary, warnings, steps, false, false, narrationManifestPath, audioSegments, sscScripts, visualAssets, captureResults, longVideoPath, shortVideoPath, finalVideoManifestPath, finalVideoResults, finalVideoValidation, request.DryRun ? "DryRun" : "Execute", flagsUsed);
    }

    private async Task<string> ComposeConcatVideoAsync(IReadOnlyList<string> segmentPaths, string outputPath, string manifestsDirectory, string filePrefix, CancellationToken cancellationToken)
    {
        var concatPath = Path.Combine(manifestsDirectory, $"{filePrefix}-concat.txt");
        await File.WriteAllLinesAsync(concatPath, segmentPaths.Select(x => $"file '{x.Replace("'", "'\\''")}'"), cancellationToken);
        var args = $"-y -f concat -safe 0 -i \"{concatPath}\" -c copy \"{outputPath}\"";
        var result = await processRunner.ExecuteAsync("ffmpeg", args, cancellationToken, TimeSpan.FromSeconds(300));
        if (result.ExitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length <= 0) throw new InvalidOperationException($"Failed to compose final video: {outputPath}");
        return args;
    }

    private async Task<double> ProbeDurationSecondsAsync(string videoPath, CancellationToken cancellationToken)
    {
        var result = await processRunner.ExecuteAsync("ffprobe", $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"", cancellationToken, TimeSpan.FromSeconds(30));
        return result.ExitCode == 0 && double.TryParse(result.StandardOutput.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static void ValidateSegments(IEnumerable<string> paths, string bucket, List<string> errors)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length <= 0) errors.Add($"Missing or empty {bucket} segment: {path}");
        }
    }

    private WeeklySkyForecastNarrationPlan BuildNarrationPlan(WeeklySkyForecastSegmentPlan segmentPlan)
    {
        static WeeklySkyForecastNarrationPlanItem ToPlan(WeeklySkyForecastSegmentPlanItem segment, bool isShort)
            => new(
                segment.SegmentCode,
                isShort ? "Fast Hook" : "Conversational Astronomy",
                isShort ? "Energetic" : "Warm and Confident",
                Math.Max(18, (int)Math.Round(segment.EstimatedDurationSeconds * (isShort ? 2.4 : 2.8))),
                segment.EstimatedDurationSeconds,
                segment.PriorityScore);

        return new(
            segmentPlan.LongSegments.Select(x => ToPlan(x, false)).ToList(),
            segmentPlan.ShortSegments.Select(x => ToPlan(x, true)).ToList());
    }

    private async Task<(WeeklyNarrationManifest Manifest, string ManifestPath)> GenerateNarrationArtifactsAsync(WeeklySkyForecastContext context, WeeklySkyForecastSegmentPlan segmentPlan, WeeklySkyForecastNarrationPlan narrationPlan, CategoryOutputPaths outputPaths, bool overwriteExisting, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputPaths.NarrationDirectory);
        Directory.CreateDirectory(outputPaths.ManifestsDirectory);
        var allSegments = segmentPlan.LongSegments.Concat(segmentPlan.ShortSegments).ToList();
        var narrationSegments = new List<WeeklyNarrationAudioSegment>(allSegments.Count);
        foreach (var segment in allSegments)
        {
            var narrationText = BuildNarrationText(segment, context);
            var fileName = $"{segment.SortOrder:00}-{segment.SegmentCode}.mp3";
            narrationSegments.Add(new WeeklyNarrationAudioSegment(
                segment.SegmentCode,
                segment.SegmentType,
                narrationText,
                segment.EstimatedDurationSeconds,
                context.Language,
                segment.SegmentType.Equals("Short", StringComparison.OrdinalIgnoreCase) ? "EnergeticShort" : "WeeklyNarrator",
                fileName));
        }

        var manifest = new WeeklyNarrationManifest(
            narrationSegments,
            DateTime.UtcNow,
            context.Language,
            narrationSegments.Sum(x => x.EstimatedDurationSeconds),
            narrationSegments.Count,
            narrationSegments.Sum(x => x.EstimatedDurationSeconds),
            0,
            0);
        var manifestPath = Path.Combine(outputPaths.ManifestsDirectory, "NarrationManifest.json");
        if (overwriteExisting || !File.Exists(manifestPath)) await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        var narrationPlanPath = Path.Combine(outputPaths.ManifestsDirectory, "NarrationPlan.json");
        if (overwriteExisting || !File.Exists(narrationPlanPath)) await File.WriteAllTextAsync(narrationPlanPath, System.Text.Json.JsonSerializer.Serialize(narrationPlan, JsonOptions), cancellationToken);
        return (manifest, manifestPath);
    }

    private async Task<List<WeeklySkyForecastAudioSegmentResult>> GenerateAudioArtifactsAsync(string? narrationManifestPath, CategoryOutputPaths outputPaths, bool overwriteExisting, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(narrationManifestPath) || !File.Exists(narrationManifestPath)) return [];
        var manifest = System.Text.Json.JsonSerializer.Deserialize<WeeklyNarrationManifest>(await File.ReadAllTextAsync(narrationManifestPath, cancellationToken)) ?? throw new InvalidOperationException("Narration manifest not readable.");
        var audioSegments = new List<WeeklySkyForecastAudioSegmentResult>(manifest.Segments.Count);
        Directory.CreateDirectory(outputPaths.NarrationDirectory);
        foreach (var segment in manifest.Segments)
        {
            var targetPath = Path.Combine(outputPaths.NarrationDirectory, segment.OutputFileName);
            if (!overwriteExisting && File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
            {
                audioSegments.Add(new WeeklySkyForecastAudioSegmentResult(segment.SegmentCode, targetPath, segment.EstimatedDurationSeconds, true, null));
                continue;
            }
            var segmentDirectory = Path.Combine(outputPaths.NarrationDirectory, segment.SegmentType.ToLowerInvariant(), segment.SegmentCode);
            Directory.CreateDirectory(segmentDirectory);
            try
            {
                var audioPath = await speechSynthesisService.SynthesizeAsync(segment.NarrationText, segmentDirectory, cancellationToken);
                if (!audioPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase)) File.Copy(audioPath, targetPath, overwrite: true);
                audioSegments.Add(new WeeklySkyForecastAudioSegmentResult(segment.SegmentCode, targetPath, segment.EstimatedDurationSeconds, true, null));
            }
            catch { audioSegments.Add(new WeeklySkyForecastAudioSegmentResult(segment.SegmentCode, targetPath, segment.EstimatedDurationSeconds, false, "Audio synthesis failed.")); }
        }
        var audioManifestPath = Path.Combine(outputPaths.ManifestsDirectory, "AudioManifest.json");
        await File.WriteAllTextAsync(audioManifestPath, System.Text.Json.JsonSerializer.Serialize(audioSegments, JsonOptions), cancellationToken);
        return audioSegments;
    }

    private static Task PersistExecutionStateAsync(CategoryOutputPaths outputPaths, WeeklySkyForecastExecutionState state, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(outputPaths.ManifestsDirectory, "ExecutionState.json"), System.Text.Json.JsonSerializer.Serialize(state, JsonOptions), cancellationToken);

    private static void MarkCompleted(WeeklySkyForecastExecutionState state, string phase) => state.CompletedPhases.Add(phase);
    private static void MarkSkipped(WeeklySkyForecastExecutionState state, string phase) => state.RetryablePhases.Add(phase);

    private static string BuildNarrationText(WeeklySkyForecastSegmentPlanItem segment, WeeklySkyForecastContext context)
    {
        var dates = $"{context.WeekStartDate:MMM d} to {context.WeekEndDate:MMM d}";
        var topObjects = string.Join(", ", context.DailyForecasts.SelectMany(x => x.VisibleObjects).Where(x => x.Visible).OrderByDescending(x => x.VisibilityScore).Select(x => x.ObjectName).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
        var bestNight = context.RecommendedNights.FirstOrDefault()?.Date.ToString("MMM d") ?? context.WeekStartDate.ToString("MMM d");
        return segment.SegmentCode switch
        {
            "WeeklyIntro" => $"Welcome to your weekly sky forecast for {context.LocationName}. From {dates}, we’re tracking the best nights and the easiest targets like {topObjects}.",
            "MoonPhaseForecast" => $"Moon watch this week: plan around {context.BestMoonNight?.ToString("MMM d") ?? bestNight} for the strongest phase views, and use evening twilight for smooth contrast.",
            "BestPlanets" => $"Top planet picks this week feature {topObjects}. Prioritize steady skies and observe when each target climbs higher after dusk.",
            "RecommendedNights" => $"Your best observing window lands around {bestNight}, with stronger visibility scores and cleaner object separation in the night sky.",
            "WeeklyHighlights" => $"Weekly highlights combine the top events and viewing windows from {dates}. Keep your setup simple and focus on the highest-ranked moments first.",
            "AstroPhotographyTip" => $"Astro photo tip: on your best night, lock a stable tripod, use a timer, and start with wide frames before zooming into bright targets like {topObjects}.",
            "WeeklyOutro" => $"That’s your weekly sky plan for {context.LocationName}. Save this forecast, step outside on the top nights, and clear skies this week.",
            _ => $"Quick sky update for {context.LocationName}: biggest opportunity is around {bestNight}. Catch {topObjects} early and share your view tonight."
        };
    }

    private static CategoryProductionStepResult Step(string name, long durationMs)
    {
        var started = DateTime.UtcNow.AddMilliseconds(-Math.Max(1, durationMs));
        var ended = DateTime.UtcNow;
        return new(name, "Completed", started, ended, Math.Max(1, durationMs), null, null, []);
    }
    private static CategoryProductionStepResult Step(string name, long durationMs, string status)
    {
        var started = DateTime.UtcNow.AddMilliseconds(-Math.Max(1, durationMs));
        var ended = DateTime.UtcNow;
        return new(name, status, started, ended, Math.Max(1, durationMs), null, null, []);
    }

    private static TimeZoneInfo ResolveTimeZoneOrUtc(string timezone)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { return TimeZoneInfo.Utc; }
    }
}

public sealed class WeeklySkyForecastExecutionState
{
    public List<string> CompletedPhases { get; init; } = [];
    public List<string> FailedPhases { get; init; } = [];
    public Dictionary<string, string> GeneratedAssets { get; init; } = [];
    public Dictionary<string, long> Diagnostics { get; init; } = [];
    public List<string> RetryablePhases { get; init; } = [];
    public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;
}

internal static class WeeklySkyForecastObjectCodeResolver
{
    private static readonly IReadOnlyDictionary<string, string> AliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["JUP"] = "JUPITER",
        ["SAT"] = "SATURN",
        ["VEN"] = "VENUS",
        ["LUNA"] = "MOON"
    };
    public static string NormalizeObjectCode(string? code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        return AliasMap.TryGetValue(normalized, out var canonical) ? canonical : normalized;
    }
}

public static class WeeklySkyForecastPreparationDiagnostics
{
    private static readonly AsyncLocal<Dictionary<string, int>> Store = new();
    public static void Set(string key, int value)
    {
        Store.Value ??= [];
        Store.Value[key] = value;
    }
    public static int Get(string key) => Store.Value is not null && Store.Value.TryGetValue(key, out var value) ? value : 0;
    public static void SetJson(string key, string value)
    {
        StoreJson.Value ??= [];
        StoreJson.Value[key] = value;
    }
    public static string? GetJson(string key) => StoreJson.Value is not null && StoreJson.Value.TryGetValue(key, out var value) ? value : null;
    private static readonly AsyncLocal<Dictionary<string, string>> StoreJson = new();
}
