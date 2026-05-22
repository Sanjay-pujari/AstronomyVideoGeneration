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
    internal const string CategoryDebugNormalizedObjectCountKey = "WeeklySkyForecast.NormalizedObjectCount";
    internal const string CategoryDebugCorrectedHighlightCountKey = "WeeklySkyForecast.CorrectedHighlightCount";
    internal const string CategoryDebugExcludedObjectCountKey = "WeeklySkyForecast.ExcludedObjectCount";

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

        var normalizedObjectCount = 0;
        var correctedHighlightCount = 0;
        var excludedObjectCount = 0;

        var daily = response.Days.Select(d => new DailySkyForecastContextItem(DateOnly.Parse(d.Date), d.SunsetUtc, d.SunriseUtc, d.MoonPhase, d.MoonIlluminationPercent, d.MoonRiseUtc, d.MoonSetUtc,
            d.VisibleObjects.Select(v =>
            {
                var normalizedCode = WeeklySkyForecastObjectCodeResolver.NormalizeObjectCode(v.ObjectCode);
                if (!string.Equals(normalizedCode, v.ObjectCode, StringComparison.Ordinal))
                    normalizedObjectCount++;
                return new WeeklySkyForecastVisibleObjectItem(normalizedCode, v.ObjectName, v.ObjectType, v.Visible, v.RiseUtc, v.SetUtc, v.TransitUtc, v.MaxAltitudeDegrees, v.BestViewingTimeUtc, v.VisibilityScore, v.PhotographyScore, v.ViewingDirection, v.Reason);
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
        return new(resolution.CanonicalRegionId, response.LocationName, resolution.Latitude, resolution.Longitude, resolution.Timezone, DateOnly.Parse(response.WeekStartDate), DateOnly.Parse(response.WeekEndDate), request.Language, daily, highlights.OrderBy(h => h.Order).Select((h, i) => h with { Order = i + 1 }).ToList(), recommended, bestPlanet, bestMoonNight, bestPhotoNight, response.Warnings);
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
            new("BestPlanets", "Long", 3, "Best Planets", "Rank top planets", bestNight?.Date, (bestNight?.BestObjects ?? []).Where(o => o is not "MOON" and not "SUN").ToList(), "Planet", "BestPlanetOfWeek", 50, 0.9),
            new("RecommendedNights", "Long", 4, "Recommended Nights", "Highlight best nights", bestNight?.Date, bestNight?.BestObjects ?? [], "Night", "RecommendedObservationNight", 40, 0.92),
            new("WeeklyHighlights", "Long", 5, "Weekly Highlights", "Cover ranked events", context.WeekStartDate, context.WeeklyHighlights.Select(x => x.ObjectCode).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct().ToList(), "Event", "WeeklyHighlight", 45, 0.88),
            new("AstroPhotographyTip", "Long", 6, "Astro Photography Tip", "Give practical tip", context.BestPhotographyNight, bestNight?.BestObjects ?? [], "Tip", "WeeklySummaryMap", 30, 0.7),
            new("WeeklyOutro", "Long", 7, "Weekly Outro", "Close and CTA", context.WeekEndDate, [], "Outro", "WeeklySummaryMap", 25, 0.6)
        };
        var shortSegments = new List<WeeklySkyForecastSegmentPlanItem>
        {
            new("BiggestWeeklyHighlight", "Short", 1, "Biggest Weekly Highlight", "Fast hook", bestNight?.Date, bestNight?.BestObjects ?? [], "Highlight", "WeeklyHighlight", 20, 0.95),
            new("BestViewingNight", "Short", 2, "Best Viewing Night", "Tell best night", bestNight?.Date, bestNight?.BestObjects ?? [], "Night", "RecommendedObservationNight", 18, 0.92),
            new("BestPlanetOfWeek", "Short", 3, "Best Planet Of Week", "Focus top planet", bestNight?.Date, [context.BestPlanetOfWeek ?? "JUPITER"], "Planet", "BestPlanetOfWeek", 18, 0.9),
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
        var planetDate = context.DailyForecasts
            .FirstOrDefault(x => x.VisibleObjects.Any(o => o.Visible && o.ObjectCode.Equals(context.BestPlanetOfWeek ?? string.Empty, StringComparison.OrdinalIgnoreCase)))?.Date
            ?? targetDate;
        var summaryDate = targetDate;
        var recommendedDate = targetDate;
        var scenes = new List<WeeklySkyForecastSscScenePlanItem>
        {
            new("WeeklyIntroWideSky", "WideSky", null, ResolveCapture(context.WeekStartDate, null, true), context.WeekStartDate, 90, "long", false, "WeeklyIntro"),
            new("BestMoonNight", "Moon", "MOON", moonNight?.BestStartUtc ?? ResolveCapture(moonDate, "MOON", false), moonDate, 45, "both", false, "MoonPhaseForecast"),
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

public sealed class WeeklySkyForecastPreparationOrchestrator(
    IContentPlanningService planning,
    IWeeklySkyForecastContextBuilder contextBuilder,
    IWeeklySkyForecastSegmentPlanner segmentPlanner,
    IWeeklySkyForecastSscScenePlanner scenePlanner,
    ICategoryOutputPathResolver pathResolver,
    IWeeklySkyForecastMetadataBuilder metadataBuilder,
    ISpeechSynthesisService speechSynthesisService,
    IStellariumScriptGenerator scriptGenerator,
    IStellariumImageCaptureExecutor captureExecutor) : IWeeklySkyForecastPreparationOrchestrator
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
        stopwatch.Restart();
        var flagsUsed = new WeeklySkyForecastExecutionFlags(
            request.GenerateNarration || request.GenerateAudio,
            request.GenerateAudio,
            request.GenerateSscScripts || request.CaptureStellariumScenes,
            request.CaptureStellariumScenes,
            request.DryRun,
            request.OverwriteExisting);
        WeeklyNarrationManifest? narrationManifest = null;
        string? narrationManifestPath = null;
        var audioSegments = new List<WeeklySkyForecastAudioSegmentResult>();
        var sscScripts = new List<WeeklySkyForecastSscScriptResult>();
        var visualAssets = new List<WeeklySkyForecastVisualAssetResult>();
        var captureResults = new List<WeeklySkyForecastCaptureResult>();

        if (flagsUsed.GenerateNarration)
        {
            var narrationPlan = BuildNarrationPlan(segmentPlan);
            (narrationManifest, narrationManifestPath, audioSegments) = await GenerateNarrationArtifactsAsync(context, segmentPlan, narrationPlan, outputPaths, flagsUsed.GenerateAudio, cancellationToken);
        }
        steps.Add(Step("GenerateNarration", stopwatch.ElapsedMilliseconds));
        steps.Add(Step("GenerateAudio", Math.Max(1, stopwatch.ElapsedMilliseconds)));
        stopwatch.Restart();
        if (flagsUsed.GenerateSscScripts)
        {
            Directory.CreateDirectory(outputPaths.StellariumScriptsDirectory);
            Directory.CreateDirectory(outputPaths.StellariumScenesDirectory);
            var capturePlan = new StellariumSceneCapturePlan(plan.Id, "WeeklySkyForecast", context.RegionId, context.LocationName, context.Latitude, context.Longitude, context.Timezone, context.WeekStartDate, [], []);
            foreach (var scene in scenes.Scenes.OrderBy(x => x.SceneCode))
            {
                var normalizedTarget = string.IsNullOrWhiteSpace(scene.TargetObjectCode) ? null : WeeklySkyForecastObjectCodeResolver.NormalizeObjectCode(scene.TargetObjectCode);
                capturePlan.Scenes.Add(new StellariumSceneCaptureItem(scene.SceneCode, scene.SceneType, scene.SceneCode, normalizedTarget, normalizedTarget, scene.CaptureTimeUtc, "Focus", scene.FieldOfViewDegrees, true, true, true, false, false, scene.OutputRole, capturePlan.Scenes.Count + 1, new Dictionary<string, string> { ["linkedSegmentCode"] = scene.LinkedSegmentCode }));
            }
            foreach (var scene in capturePlan.Scenes)
            {
                var generated = await scriptGenerator.GenerateAsync(capturePlan, scene, cancellationToken);
                var destinationScriptPath = Path.Combine(outputPaths.StellariumScriptsDirectory, $"{scene.SceneCode}.ssc");
                File.Copy(generated.ScriptPath, destinationScriptPath, true);
                var expectedImagePath = Path.Combine(outputPaths.StellariumScenesDirectory, $"{scene.SceneCode}_{scene.OutputImageRole}.png");
                sscScripts.Add(new WeeklySkyForecastSscScriptResult(scene.SceneCode, destinationScriptPath, expectedImagePath, generated.Success, generated.ErrorMessage));
                visualAssets.Add(new WeeklySkyForecastVisualAssetResult(scene.SceneCode, expectedImagePath, scene.OutputImageRole, scene.Metadata.TryGetValue("linkedSegmentCode", out var linked) ? linked : string.Empty, scene.TargetObjectCode));
            }
            if (flagsUsed.CaptureStellariumScenes)
            {
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
            }
        }
        steps.Add(Step("GenerateSscScripts", Math.Max(1, stopwatch.ElapsedMilliseconds)));
        steps.Add(Step("CaptureStellariumScenes", 1));
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
        return new(plan.Id, "WeeklySkyForecast", context.WeekStartDate, context.WeekEndDate, context, segmentPlan.LongSegments, segmentPlan.ShortSegments, scenes.Scenes, outputPaths, metadata, preparationValidation, debugSummary, warnings, steps, false, false, narrationManifestPath, audioSegments, sscScripts, visualAssets, captureResults, request.DryRun ? "DryRun" : "Execute", flagsUsed);
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

    private async Task<(WeeklyNarrationManifest Manifest, string ManifestPath, List<WeeklySkyForecastAudioSegmentResult> AudioSegments)> GenerateNarrationArtifactsAsync(WeeklySkyForecastContext context, WeeklySkyForecastSegmentPlan segmentPlan, WeeklySkyForecastNarrationPlan narrationPlan, CategoryOutputPaths outputPaths, bool generateAudio, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputPaths.NarrationDirectory);
        Directory.CreateDirectory(outputPaths.ManifestsDirectory);
        var allSegments = segmentPlan.LongSegments.Concat(segmentPlan.ShortSegments).ToList();
        var narrationSegments = new List<WeeklyNarrationSegment>(allSegments.Count);
        var audioSegments = new List<WeeklySkyForecastAudioSegmentResult>(allSegments.Count);
        var generatedAudioCount = 0;
        var failedNarrationCount = 0;

        foreach (var segment in allSegments)
        {
            var narrationText = BuildNarrationText(segment, context);
            var fileName = $"{segment.SortOrder:00}-{segment.SegmentCode}.mp3";
            var segmentDirectory = Path.Combine(outputPaths.NarrationDirectory, segment.SegmentType.ToLowerInvariant(), segment.SegmentCode);
            Directory.CreateDirectory(segmentDirectory);

            var targetPath = Path.Combine(outputPaths.NarrationDirectory, fileName);
            if (generateAudio) try
            {
                var audioPath = await speechSynthesisService.SynthesizeAsync(narrationText, segmentDirectory, cancellationToken);
                if (!audioPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(audioPath, targetPath, overwrite: true);
                }
                generatedAudioCount++;
                audioSegments.Add(new WeeklySkyForecastAudioSegmentResult(segment.SegmentCode, targetPath, segment.EstimatedDurationSeconds, true, null));
            }
            catch
            {
                failedNarrationCount++;
                audioSegments.Add(new WeeklySkyForecastAudioSegmentResult(segment.SegmentCode, targetPath, segment.EstimatedDurationSeconds, false, "Audio synthesis failed."));
            }

            narrationSegments.Add(new WeeklyNarrationSegment(
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
            generatedAudioCount,
            failedNarrationCount);
        var manifestPath = Path.Combine(outputPaths.ManifestsDirectory, "NarrationManifest.json");
        await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputPaths.ManifestsDirectory, "NarrationPlan.json"), System.Text.Json.JsonSerializer.Serialize(narrationPlan, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return (manifest, manifestPath, audioSegments);
    }

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

    private static TimeZoneInfo ResolveTimeZoneOrUtc(string timezone)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { return TimeZoneInfo.Utc; }
    }
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

internal static class WeeklySkyForecastPreparationDiagnostics
{
    private static readonly AsyncLocal<Dictionary<string, int>> Store = new();
    public static void Set(string key, int value)
    {
        Store.Value ??= [];
        Store.Value[key] = value;
    }
    public static int Get(string key) => Store.Value is not null && Store.Value.TryGetValue(key, out var value) ? value : 0;
}
