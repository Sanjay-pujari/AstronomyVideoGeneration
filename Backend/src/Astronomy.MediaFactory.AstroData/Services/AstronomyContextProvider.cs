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
    private readonly SkyfieldSidecarOptions _sidecarOptions;
    private readonly ObservationOptions _observationOptions;

    public AstronomyContextProvider(NasaApodClient apodClient, NasaNeoWsClient neoWsClient, ISkyfieldSidecarClient skyfieldSidecarClient, ILogger<AstronomyContextProvider> logger, IOptions<SkyfieldSidecarOptions> sidecarOptions, IOptions<ObservationOptions> observationOptions)
    {
        _apodClient = apodClient;
        _neoWsClient = neoWsClient;
        _skyfieldSidecarClient = skyfieldSidecarClient;
        _logger = logger;
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
            var nightPlanRequest = new SkyfieldNightPlanRequest
            {
                Date = date.ToString("yyyy-MM-dd"),
                LocationName = effectiveLocationName,
                Latitude = effectiveLatitude,
                Longitude = effectiveLongitude,
                Timezone = effectiveTimezone,
                MinimumAltitudeDegrees = _observationOptions.MinimumObjectAltitudeDegrees,
                StepMinutes = _observationOptions.VisibilitySearchStepMinutes,
                Candidates = NightSkyVisibilityPlanner.DefaultCandidates
                    .Select(c => new SkyfieldVisibilityCandidate { ObjectName = c.ObjectName, ObjectType = c.ObjectType })
                    .ToList()
            };

            context.VisualIdeas.Add(new VisualIdeaModel { Title = "effective-observation-settings", Description = Serialize(new { effectiveLocationName, effectiveTimezone, effectiveLatitude, effectiveLongitude }) });
            context.VisualIdeas.Add(new VisualIdeaModel { Title = "skyfield-night-plan-request", Description = Serialize(nightPlanRequest) });

            var nightPlanResponse = await _skyfieldSidecarClient.GetNightVisibilityPlanAsync(nightPlanRequest, cancellationToken);
            context.VisualIdeas.Add(new VisualIdeaModel { Title = "skyfield-night-plan-response", Description = Serialize(nightPlanResponse) });

            if (TryApplyNightPlanResponse(context, nightPlanResponse, effectiveTimezone, _observationOptions))
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

    private static bool TryApplyNightPlanResponse(AstronomyContext context, SkyfieldNightPlanResponse? response, string timezone, ObservationOptions observationOptions)
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
        var overviewLocal = ParseLocal(response!.NightWindowStartLocal) ?? DateTime.SpecifyKind(DateTime.Today.AddHours(19), DateTimeKind.Unspecified);
        var scenes = new List<SceneObservationContext>
        {
            new() { SceneId = "sky-overview", SceneTitle = "Sky overview", SceneType = "Overview", ObjectName = "Sky", ObjectType = "Overview", LocalObservationTime = overviewLocal, UtcObservationTime = ToUtc(overviewLocal, tz), Timezone = timezone, IsVisible = true, VisibilityReason = "Night overview", RecommendedTool = "Naked eye", NarrationFocus = "Night sky orientation.", Latitude = context.Latitude, Longitude = context.Longitude, LocationName = context.LocationName }
        };

        foreach (var (v, i) in selected.Select((v, i) => (v, i)))
        {
            var bestSample = SelectBestSample(v.Samples, observationOptions.MinimumObjectAltitudeDegrees);
            var local = ParseLocal(bestSample?.LocalTime)
                ?? ComputeMidpointFromSamples(v.Samples)
                ?? ParseLocal(v.BestLocalTime)
                ?? overviewLocal.AddMinutes(30 + (i * 30));
            var utc = ParseUtc(bestSample?.UtcTime)
                ?? ComputeMidpointUtcFromSamples(v.Samples, tz)
                ?? (DateTimeOffset.TryParse(v.BestUtcTime, out var pUtc) ? pUtc.ToUniversalTime() : ToUtc(local, tz));
            var altitude = bestSample?.AltitudeDegrees ?? v.AltitudeDegrees ?? 0;
            var azimuth = bestSample?.AzimuthDegrees ?? v.AzimuthDegrees ?? 0;
            var direction = bestSample?.DirectionLabel ?? v.DirectionLabel ?? "N/A";

            scenes.Add(new SceneObservationContext
            {
                SceneId = $"object-{i + 1}",
                SceneTitle = $"{v.ObjectName} focus",
                SceneType = "Object",
                ObjectName = v.ObjectName,
                ObjectType = string.IsNullOrWhiteSpace(v.ObjectType) ? "Object" : v.ObjectType,
                LocalObservationTime = local,
                UtcObservationTime = utc,
                Timezone = timezone,
                AltitudeDegrees = altitude,
                AzimuthDegrees = azimuth,
                DirectionLabel = direction,
                IsVisible = true,
                VisibilityReason = v.VisibilityReason,
                RecommendedTool = "Naked eye / binoculars",
                NarrationFocus = v.VisibilityReason,
                Latitude = context.Latitude,
                Longitude = context.Longitude,
                LocationName = context.LocationName
            });

            _loggerStatic?.LogInformation("Selected observation time for {ObjectName}: {SelectedTime} (alt: {Altitude:F1}°)", v.ObjectName, local, altitude);
        }

        scenes.Add(new SceneObservationContext { SceneId = "closing", SceneTitle = "Closing wide sky", SceneType = "Tips", ObjectName = "Sky", ObjectType = "Overview", LocalObservationTime = scenes.Last().LocalObservationTime.AddMinutes(30), UtcObservationTime = scenes.Last().UtcObservationTime.AddMinutes(30), Timezone = timezone, IsVisible = true, VisibilityReason = "Wrap-up", RecommendedTool = "Naked eye", NarrationFocus = "Safe viewing tips.", Latitude = context.Latitude, Longitude = context.Longitude, LocationName = context.LocationName });
        context.SceneObservationContexts = scenes;

        context.VisualIdeas.Add(new VisualIdeaModel { Title = "selected-visible-objects", Description = Serialize(selected.Select(v => new { v.ObjectName, v.BestLocalTime, v.BestUtcTime, v.DirectionLabel, v.AltitudeDegrees, v.IsVisible, v.VisibilityReason })) });
        context.VisualIdeas.Add(new VisualIdeaModel { Title = "scene-observation-context", Description = Serialize(scenes) });
        context.VisualIdeas.Add(new VisualIdeaModel { Title = "narration-context", Description = Serialize(scenes.Select(s => new { s.SceneId, s.ObjectName, s.LocalObservationTime, s.UtcObservationTime, s.DirectionLabel, s.AltitudeDegrees, s.IsVisible, s.VisibilityReason })) });
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
}
