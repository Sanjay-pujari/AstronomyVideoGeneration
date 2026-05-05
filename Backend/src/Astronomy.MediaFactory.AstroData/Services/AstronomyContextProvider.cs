using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.AstroData.Services;

public sealed class AstronomyContextProvider : IAstronomyContextProvider
{
    private static ILogger<AstronomyContextProvider>? _loggerStatic;
    private readonly NasaApodClient _apodClient;
    private readonly NasaNeoWsClient _neoWsClient;
    private readonly ISkyfieldSidecarClient _skyfieldSidecarClient;
    private readonly ILogger<AstronomyContextProvider> _logger;
    private readonly IObservationWindowService _observationWindowService;
    private readonly SkyfieldSidecarOptions _sidecarOptions;
    private readonly ObservationOptions _observationOptions;

    public AstronomyContextProvider(NasaApodClient apodClient, NasaNeoWsClient neoWsClient, ISkyfieldSidecarClient skyfieldSidecarClient, IObservationWindowService observationWindowService, ILogger<AstronomyContextProvider> logger, IOptions<SkyfieldSidecarOptions> sidecarOptions, IOptions<ObservationOptions> observationOptions)
    {
        _apodClient = apodClient;
        _neoWsClient = neoWsClient;
        _skyfieldSidecarClient = skyfieldSidecarClient;
        _logger = logger;
        _observationWindowService = observationWindowService;
        _loggerStatic = logger;
        _sidecarOptions = sidecarOptions.Value;
        _observationOptions = observationOptions.Value;
    }

    public async Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken)
    {
        var effectiveLocationName = string.IsNullOrWhiteSpace(locationName) ? _observationOptions.LocationName : locationName.Trim();
        var effectiveTimezone = string.IsNullOrWhiteSpace(timeZone) ? _observationOptions.Timezone : timeZone.Trim();
        var effectiveLatitude = _observationOptions.Latitude;
        var effectiveLongitude = _observationOptions.Longitude;

        var context = new AstronomyContext { Date = date, LocationName = effectiveLocationName, TimeZone = effectiveTimezone, Latitude = effectiveLatitude, Longitude = effectiveLongitude };
        await AddNasaContextAsync(context, date, cancellationToken);

        if (contentType == ContentType.DailySkyGuide && _sidecarOptions.Enabled)
        {
            var effectiveOptions = new ObservationOptions
            {
                LocationName = effectiveLocationName,
                Timezone = effectiveTimezone,
                Latitude = effectiveLatitude,
                Longitude = effectiveLongitude,
                MinimumObjectAltitudeDegrees = _observationOptions.MinimumObjectAltitudeDegrees,
                PreferredObjectAltitudeDegrees = _observationOptions.PreferredObjectAltitudeDegrees,
                VisibilitySearchStepMinutes = _observationOptions.VisibilitySearchStepMinutes,
                SkyOverviewMinutesAfterSunset = _observationOptions.SkyOverviewMinutesAfterSunset
            };
            var observationWindow = await _observationWindowService.BuildNightWindowAsync(effectiveOptions, date, cancellationToken);
            var nightPlanRequest = new SkyfieldNightPlanRequest
            {
                Date = date.ToString("yyyy-MM-dd"),
                LocationName = effectiveLocationName,
                Latitude = effectiveLatitude,
                Longitude = effectiveLongitude,
                Timezone = effectiveTimezone,
                NightWindowStartUtc = observationWindow.NightWindowStartUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                NightWindowEndUtc = observationWindow.NightWindowEndUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                MinimumAltitudeDegrees = _observationOptions.MinimumObjectAltitudeDegrees,
                StepMinutes = _observationOptions.VisibilitySearchStepMinutes,
                Candidates = NightSkyVisibilityPlanner.DefaultCandidates
                    .Select(c => new SkyfieldVisibilityCandidate { ObjectName = c.ObjectName, ObjectType = c.ObjectType })
                    .ToList()
            };

            context.VisualIdeas.Add(new VisualIdeaModel { Title = "observation-window", Description = Serialize(observationWindow) });
            context.VisualIdeas.Add(new VisualIdeaModel { Title = "effective-observation-settings", Description = Serialize(new { effectiveLocationName, effectiveTimezone, effectiveLatitude, effectiveLongitude }) });
            context.VisualIdeas.Add(new VisualIdeaModel { Title = "skyfield-night-plan-request", Description = Serialize(nightPlanRequest) });

            var nightPlanResponse = await _skyfieldSidecarClient.GetNightVisibilityPlanAsync(nightPlanRequest, cancellationToken);
            context.VisualIdeas.Add(new VisualIdeaModel { Title = "skyfield-night-plan-response", Description = Serialize(nightPlanResponse) });

            if (TryApplyNightPlanResponse(context, nightPlanResponse, effectiveTimezone, _observationOptions, observationWindow))
            {
                return context;
            }

            _logger.LogWarning("Skyfield night-plan returned no usable visible objects for {Date} at {LocationName}. Falling back to safe overview.", date, effectiveLocationName);
        }
        else if (contentType == ContentType.DailySkyGuide)
        {
            _logger.LogInformation("Skyfield sidecar is disabled. Using fallback astronomy context for {Date} at {LocationName}.", date, effectiveLocationName);
        }

        AddFallbackOverviewOnly(context);
        return context;
    }

    private async Task AddNasaContextAsync(AstronomyContext context, DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var apod = await _apodClient.GetAsync(date, cancellationToken);
            if (apod is not null)
            {
                context.NewsItems.Add(new NewsItemModel { Headline = apod.Title ?? "NASA APOD", Summary = apod.Explanation ?? "No summary available.", SourceName = "NASA APOD", PublishedDate = date, SourceUrl = apod.Hdurl ?? apod.Url });
                context.VisualIdeas.Add(new VisualIdeaModel { Title = apod.Title ?? "APOD visual", Description = "NASA APOD visual anchor for the video.", SourcePathOrUrl = apod.Hdurl ?? apod.Url });
            }

            _ = await _neoWsClient.GetFeedAsync(date, date.AddDays(2), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NASA context enrichment failed for {Date}. Continuing without NASA data.", date);
        }
    }

    private static bool TryApplyNightPlanResponse(AstronomyContext context, SkyfieldNightPlanResponse? response, string timezone, ObservationOptions observationOptions, ObservationWindow observationWindow)
    {
        var visible = response?.VisibleObjects?.Where(x => x.IsVisible).ToList() ?? [];
        if (visible.Count == 0)
        {
            return false;
        }

        context.Events.AddRange(visible.Select(v => new AstronomyEventModel
        {
            Category = string.IsNullOrWhiteSpace(v.ObjectType) ? "Object" : v.ObjectType,
            ObjectName = v.ObjectName,
            VisibilityWindow = v.BestLocalTime ?? "Night window",
            Direction = v.DirectionLabel ?? "N/A",
            ObservationTool = "Naked eye / binoculars",
            Details = v.VisibilityReason,
            Score = 0.9
        }));

        var selected = visible
            .Where(v => (v.AltitudeDegrees ?? 0) >= observationOptions.MinimumObjectAltitudeDegrees)
            .OrderByDescending(v => (v.AltitudeDegrees ?? 0) >= observationOptions.PreferredObjectAltitudeDegrees)
            .ThenByDescending(v => v.ObjectType.Equals("Moon", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(v => v.ObjectType.Equals("Planet", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(v => v.AltitudeDegrees ?? 0)
            .Take(3)
            .ToList();
        if (selected.Count == 0)
        {
            return false;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        var sunsetLocal = observationWindow.SunsetLocal.DateTime;
        var overviewLocal = sunsetLocal.AddMinutes(observationOptions.SkyOverviewMinutesAfterSunset);
        var scenes = new List<SceneObservationContext>
        {
            new() { SceneId = "sky-overview", SceneTitle = "Sky overview", SceneType = "Overview", ObjectName = "Sky", ObjectType = "Overview", LocalObservationTime = overviewLocal, UtcObservationTime = ToUtc(overviewLocal, tz), Timezone = timezone, IsVisible = true, VisibilityReason = "Night overview", RecommendedTool = "Naked eye", NarrationFocus = "Night sky orientation.", Latitude = context.Latitude, Longitude = context.Longitude, LocationName = context.LocationName }
        };

        var usedTimes = new HashSet<DateTime> { overviewLocal };
        var objectScenes = new List<SceneObservationContext>();
        foreach (var (v, i) in selected.Select((v, i) => (v, i)))
        {
            var minimumAltitude = ResolveMinimumAltitudeForObject(v, observationOptions.MinimumObjectAltitudeDegrees);
            var selection = SelectObservationForScene(v, minimumAltitude, sunsetLocal, tz, overviewLocal.AddMinutes(30 + (i * 30)));
            while (usedTimes.Contains(selection.Local))
            {
                selection = selection with
                {
                    Local = selection.Local.AddMinutes(20),
                    Utc = selection.Utc.AddMinutes(20),
                    Reason = $"{selection.Reason} + anti-cluster shift"
                };
            }

            usedTimes.Add(selection.Local);

            objectScenes.Add(new SceneObservationContext
            {
                SceneId = $"object-{i + 1}",
                SceneTitle = $"{v.ObjectName} focus",
                SceneType = "Object",
                ObjectName = v.ObjectName,
                ObjectType = string.IsNullOrWhiteSpace(v.ObjectType) ? "Object" : v.ObjectType,
                LocalObservationTime = selection.Local,
                UtcObservationTime = selection.Utc,
                Timezone = timezone,
                AltitudeDegrees = selection.Altitude,
                AzimuthDegrees = selection.Azimuth,
                DirectionLabel = selection.Direction,
                IsVisible = true,
                VisibilityReason = v.VisibilityReason,
                RecommendedTool = "Naked eye / binoculars",
                NarrationFocus = $"{v.VisibilityReason} Best around {selection.Local:h:mm tt}.",
                Latitude = context.Latitude,
                Longitude = context.Longitude,
                LocationName = context.LocationName
            });

            _loggerStatic?.LogInformation("Selected observation scene {ObjectName} at {SelectedLocalTime} altitude {Altitude:F1}° reason: {Reason}", v.ObjectName, selection.Local, selection.Altitude, selection.Reason);
        }

        objectScenes = objectScenes
            .OrderBy(s => s.LocalObservationTime)
            .ToList();

        for (var i = 0; i < objectScenes.Count; i++)
        {
            var orderedScene = objectScenes[i];
            scenes.Add(new SceneObservationContext
            {
                SceneId = $"object-{i + 1}",
                SceneTitle = orderedScene.SceneTitle,
                SceneType = orderedScene.SceneType,
                ObjectName = orderedScene.ObjectName,
                ObjectType = orderedScene.ObjectType,
                LocalObservationTime = orderedScene.LocalObservationTime,
                UtcObservationTime = orderedScene.UtcObservationTime,
                Timezone = orderedScene.Timezone,
                AltitudeDegrees = orderedScene.AltitudeDegrees,
                AzimuthDegrees = orderedScene.AzimuthDegrees,
                DirectionLabel = orderedScene.DirectionLabel,
                IsVisible = orderedScene.IsVisible,
                VisibilityReason = orderedScene.VisibilityReason,
                RecommendedTool = orderedScene.RecommendedTool,
                NarrationFocus = orderedScene.NarrationFocus,
                Latitude = orderedScene.Latitude,
                Longitude = orderedScene.Longitude,
                LocationName = orderedScene.LocationName
            });
        }

        var closingLocalTime = Clamp(scenes.Last().LocalObservationTime.AddMinutes(30), observationWindow.SunsetLocal.DateTime, observationWindow.SunriseLocal.DateTime);
        scenes.Add(new SceneObservationContext { SceneId = "closing", SceneTitle = "Closing wide sky", SceneType = "Tips", ObjectName = "Sky", ObjectType = "Overview", LocalObservationTime = closingLocalTime, UtcObservationTime = ToUtc(closingLocalTime, tz), Timezone = timezone, IsVisible = true, VisibilityReason = "Wrap-up", RecommendedTool = "Naked eye", NarrationFocus = "Safe viewing tips.", Latitude = context.Latitude, Longitude = context.Longitude, LocationName = context.LocationName });

        foreach (var (scene, index) in scenes.Select((scene, index) => (scene, index)))
        {
            _loggerStatic?.LogInformation("Scene timeline [{SceneIndex}] {ObjectName} at local {LocalTime}", index + 1, scene.ObjectName, scene.LocalObservationTime);
        }

        var overviewDecision = BuildOverviewDecision(selected, visible, context, observationOptions);
        var overviewNarration = BuildOverviewNarration(overviewDecision, context);
        var overviewScene = scenes[0];
        scenes[0] = new SceneObservationContext
        {
            SceneId = overviewScene.SceneId,
            SceneTitle = overviewScene.SceneTitle,
            SceneType = overviewScene.SceneType,
            ObjectName = overviewDecision.PrimaryObject ?? overviewScene.ObjectName,
            ObjectType = overviewScene.ObjectType,
            LocalObservationTime = overviewScene.LocalObservationTime,
            UtcObservationTime = overviewScene.UtcObservationTime,
            Timezone = overviewScene.Timezone,
            AltitudeDegrees = overviewScene.AltitudeDegrees,
            AzimuthDegrees = overviewScene.AzimuthDegrees,
            DirectionLabel = overviewScene.DirectionLabel,
            IsVisible = overviewScene.IsVisible,
            VisibilityReason = overviewScene.VisibilityReason,
            RecommendedTool = overviewScene.RecommendedTool,
            NarrationFocus = overviewNarration,
            Latitude = overviewScene.Latitude,
            Longitude = overviewScene.Longitude,
            LocationName = overviewScene.LocationName
        };

        context.SceneObservationContexts = scenes;

        context.VisualIdeas.Add(new VisualIdeaModel { Title = "selected-visible-objects", Description = Serialize(selected.Select(v => new { v.ObjectName, v.BestLocalTime, v.BestUtcTime, v.DirectionLabel, v.AltitudeDegrees, v.IsVisible, v.VisibilityReason })) });
        context.VisualIdeas.Add(new VisualIdeaModel { Title = "scene-observation-context", Description = Serialize(scenes) });
        context.VisualIdeas.Add(new VisualIdeaModel { Title = "narration-context", Description = Serialize(scenes.Select(s => new { s.SceneId, s.ObjectName, s.LocalObservationTime, s.UtcObservationTime, s.DirectionLabel, s.AltitudeDegrees, s.IsVisible, s.VisibilityReason })) });
        context.VisualIdeas.Add(new VisualIdeaModel { Title = "overview-strategy.json", Description = Serialize(overviewDecision) });
        return true;
    }


    private static SkyfieldVisibilitySample? SelectBestSample(IReadOnlyCollection<SkyfieldVisibilitySample>? samples, double minimumAltitudeDegrees)
    {
        if (samples is null || samples.Count == 0)
        {
            return null;
        }

        return samples
            .Where(s => s.AltitudeDegrees >= minimumAltitudeDegrees)
            .OrderByDescending(s => s.AltitudeDegrees)
            .ThenBy(s => ParseLocal(s.LocalTime) ?? DateTime.MaxValue)
            .FirstOrDefault();
    }

    private static DateTime? ComputeMidpointFromSamples(IReadOnlyCollection<SkyfieldVisibilitySample>? samples)
    {
        if (samples is null || samples.Count == 0)
        {
            return null;
        }

        var ordered = samples
            .Select(s => ParseLocal(s.LocalTime))
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .OrderBy(t => t)
            .ToList();

        if (ordered.Count == 0)
        {
            return null;
        }

        var start = ordered.First();
        var end = ordered.Last();
        return start + TimeSpan.FromTicks((end - start).Ticks / 2);
    }

    private static DateTimeOffset? ComputeMidpointUtcFromSamples(IReadOnlyCollection<SkyfieldVisibilitySample>? samples, TimeZoneInfo tz)
    {
        var midpoint = ComputeMidpointFromSamples(samples);
        return midpoint.HasValue ? ToUtc(midpoint.Value, tz) : null;
    }

    private static DateTime? ParseLocal(string? value)
        => DateTime.TryParse(value, out var parsed) ? DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified) : null;

    private static DateTimeOffset? ParseUtc(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo timezone)
        => new DateTimeOffset(local, timezone.GetUtcOffset(local)).ToUniversalTime();

    private static void AddFallbackOverviewOnly(AstronomyContext context)
    {
        context.Events.Add(new AstronomyEventModel { Category = "Overview", ObjectName = "Night sky", VisibilityWindow = "After sunset", Direction = "Varies", ObservationTool = "Naked eye", Details = "Check local sky conditions and focus on bright, easily identified targets.", Score = 0.5 });
    }

    private static string Serialize(object? value)
        => System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private sealed record SelectedObservation(DateTime Local, DateTimeOffset Utc, double Altitude, double Azimuth, string Direction, string Reason);

    private static double ResolveMinimumAltitudeForObject(SkyfieldObjectVisibility visibility, double globalMinimum)
    {
        if (visibility.ObjectType.Equals("Moon", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(globalMinimum, 25d);
        }

        if (visibility.ObjectType.Equals("Planet", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(globalMinimum, 20d);
        }

        return globalMinimum;
    }

    private static SelectedObservation SelectObservationForScene(SkyfieldObjectVisibility visibility, double minimumAltitude, DateTime sunsetLocal, TimeZoneInfo timezone, DateTime fallbackLocal)
    {
        var orderedSamples = (visibility.Samples ?? [])
            .Where(s => ParseLocal(s.LocalTime).HasValue)
            .OrderBy(s => ParseLocal(s.LocalTime))
            .ToList();

        var candidates = orderedSamples.Where(s => s.AltitudeDegrees >= minimumAltitude);
        var isDeepSky = !visibility.ObjectType.Equals("Moon", StringComparison.OrdinalIgnoreCase)
            && !visibility.ObjectType.Equals("Planet", StringComparison.OrdinalIgnoreCase)
            && !visibility.ObjectType.Equals("Overview", StringComparison.OrdinalIgnoreCase);

        if (isDeepSky)
        {
            candidates = candidates.Where(s => (ParseLocal(s.LocalTime) ?? sunsetLocal) >= sunsetLocal.AddHours(1.5));
        }

        var bestSample = candidates.OrderByDescending(s => s.AltitudeDegrees).FirstOrDefault();
        if (bestSample is not null)
        {
            var local = ParseLocal(bestSample.LocalTime) ?? fallbackLocal;
            var utc = ParseUtc(bestSample.UtcTime) ?? ToUtc(local, timezone);
            return new SelectedObservation(local, utc, bestSample.AltitudeDegrees, bestSample.AzimuthDegrees, bestSample.DirectionLabel ?? visibility.DirectionLabel ?? "N/A", "peak altitude");
        }

        if (orderedSamples.Count > 0)
        {
            var first = orderedSamples.First();
            var last = orderedSamples.Last();
            var firstLocal = ParseLocal(first.LocalTime) ?? fallbackLocal;
            var lastLocal = ParseLocal(last.LocalTime) ?? firstLocal;
            var midpointLocal = firstLocal + TimeSpan.FromTicks((lastLocal - firstLocal).Ticks / 2);
            var firstUtc = ParseUtc(first.UtcTime) ?? ToUtc(firstLocal, timezone);
            var lastUtc = ParseUtc(last.UtcTime) ?? ToUtc(lastLocal, timezone);
            var midpointUtc = firstUtc + TimeSpan.FromTicks((lastUtc - firstUtc).Ticks / 2);
            return new SelectedObservation(midpointLocal, midpointUtc, visibility.AltitudeDegrees ?? 0, visibility.AzimuthDegrees ?? 0, visibility.DirectionLabel ?? "N/A", "midpoint fallback");
        }

        var localFallback = ParseLocal(visibility.BestLocalTime) ?? fallbackLocal;
        var utcFallback = ParseUtc(visibility.BestUtcTime) ?? ToUtc(localFallback, timezone);
        return new SelectedObservation(localFallback, utcFallback, visibility.AltitudeDegrees ?? 0, visibility.AzimuthDegrees ?? 0, visibility.DirectionLabel ?? "N/A", "response fallback");
    }

    private static DateTime Clamp(DateTime value, DateTime min, DateTime max) => value < min ? min : value > max ? max : value;

    private static OverviewStrategyDiagnostics BuildOverviewDecision(
        IReadOnlyList<SkyfieldObjectVisibility> selected,
        IReadOnlyList<SkyfieldObjectVisibility> visible,
        AstronomyContext context,
        ObservationOptions options)
    {
        var mode = options.Overview.Mode;
        var hemisphere = context.Latitude >= 0 ? "Northern" : "Southern";
        var primary = SelectPrimaryAttractiveObject(selected, options.MinimumObjectAltitudeDegrees)
            ?? SelectPrimaryAttractiveObject(visible, options.MinimumObjectAltitudeDegrees)
            ?? "Sky";
        var polaris = visible.FirstOrDefault(v => v.ObjectName.Equals("Polaris", StringComparison.OrdinalIgnoreCase));
        var polarisUsable = options.Overview.EnablePolarisOrientation
            && hemisphere == "Northern"
            && polaris is not null
            && (polaris.AltitudeDegrees ?? 0) >= options.MinimumObjectAltitudeDegrees;
        var polarisUsed = mode == "PolarisOnly" ? polarisUsable : mode == "Hybrid" && polarisUsable;
        var reason = mode switch
        {
            "AttractiveOnly" => "Attractive hook selected for overview.",
            "PolarisOnly" when !polarisUsable => "Polaris unavailable; fallback to sky orientation.",
            "PolarisOnly" => "Polaris selected as orientation-first mode.",
            "Hybrid" when !polarisUsable => "Hybrid mode without usable Polaris; narration keeps orientation generic.",
            _ => "Hybrid mode uses attractive hook first with Polaris orientation support."
        };

        return new(mode, options.Overview.DefaultHookStrategy, primary, options.Overview.EnablePolarisOrientation, polarisUsed, reason, hemisphere);
    }

    private static string BuildOverviewNarration(OverviewStrategyDiagnostics overviewDecision, AstronomyContext context)
    {
        var mode = overviewDecision.Mode;
        var primaryObject = overviewDecision.PrimaryObject;
        var polarisUsed = overviewDecision.PolarisUsed;
        var hemisphere = overviewDecision.Hemisphere;

        var hook = $"Tonight's sky opens with {primaryObject} drawing your eye first.";
        if (mode == "AttractiveOnly")
            return hook;
        if (mode == "PolarisOnly")
            return polarisUsed
                ? "To orient yourself, find north using Polaris, the North Star, then scan tonight's sky."
                : "To orient yourself, face the darker southern sky and then scan tonight's best objects.";

        var orientation = polarisUsed
            ? "To orient yourself, find north using Polaris, the North Star."
            : hemisphere == "Southern"
                ? "To orient yourself, use a south-facing sky reference instead of Polaris."
                : "To orient yourself, find local north before moving through the targets.";
        return $"{hook} {orientation} Now let's move through tonight's best objects.";
    }

    private static string? SelectPrimaryAttractiveObject(IEnumerable<SkyfieldObjectVisibility> candidates, double minimumAltitude)
    {
        var list = candidates.Where(v => v.IsVisible && (v.AltitudeDegrees ?? 0) >= minimumAltitude).ToList();
        var moon = list.FirstOrDefault(v => v.ObjectName.Equals("Moon", StringComparison.OrdinalIgnoreCase));
        if (moon is not null)
            return moon.ObjectName;

        foreach (var name in new[] { "Jupiter", "Venus", "Saturn" })
        {
            var match = list.FirstOrDefault(v => v.ObjectName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.ObjectName;
        }

        return list.OrderByDescending(v => v.AltitudeDegrees ?? 0).FirstOrDefault()?.ObjectName;
    }

    private sealed record OverviewStrategyDiagnostics(
        string Mode,
        string HookStrategy,
        string PrimaryObject,
        bool PolarisOrientationEnabled,
        bool PolarisUsed,
        string Reason,
        string Hemisphere);
}
