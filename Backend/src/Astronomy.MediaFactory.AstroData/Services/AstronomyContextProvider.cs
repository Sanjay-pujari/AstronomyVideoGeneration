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
    private readonly ContentExpansionOptions _contentExpansionOptions;

    public AstronomyContextProvider(NasaApodClient apodClient, NasaNeoWsClient neoWsClient, ISkyfieldSidecarClient skyfieldSidecarClient, IObservationWindowService observationWindowService, ILogger<AstronomyContextProvider> logger, IOptions<SkyfieldSidecarOptions> sidecarOptions, IOptions<ObservationOptions> observationOptions, IOptions<ContentExpansionOptions>? contentExpansionOptions = null)
    {
        _apodClient = apodClient;
        _neoWsClient = neoWsClient;
        _skyfieldSidecarClient = skyfieldSidecarClient;
        _logger = logger;
        _observationWindowService = observationWindowService;
        _loggerStatic = logger;
        _sidecarOptions = sidecarOptions.Value;
        _observationOptions = observationOptions.Value;
        _contentExpansionOptions = contentExpansionOptions?.Value ?? new ContentExpansionOptions();
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

    private bool TryApplyNightPlanResponse(AstronomyContext context, SkyfieldNightPlanResponse? response, string timezone, ObservationOptions observationOptions, ObservationWindow observationWindow)
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

        var scoredObjects = visible
            .Where(v => (v.AltitudeDegrees ?? 0) >= observationOptions.MinimumObjectAltitudeDegrees)
            .Where(IsAllowedByContentExpansion)
            .Select(v => ScoreVisibleObject(v, observationOptions))
            .Where(x => x.VisibilityScore >= (_contentExpansionOptions.Enabled ? _contentExpansionOptions.MinimumVisibilityScore * 10d : 0d))
            .OrderByDescending(x => x.FinalScore)
            .ThenByDescending(x => ResolveObjectPriority(x.Visibility.ObjectType, x.Visibility.ObjectName))
            .DistinctBy(x => x.Visibility.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var maxPrimaryObjects = Math.Clamp(_contentExpansionOptions.Enabled ? _contentExpansionOptions.MaxObjectsPerGuide : 5, 3, 5);
        var selectedScores = SelectAdaptiveLongFormObjects(scoredObjects, maxPrimaryObjects).ToList();
        var selected = selectedScores.Select(x => x.Visibility).ToList();
        if (selected.Count == 0)
        {
            return false;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        var sunsetLocal = observationWindow.SunsetLocal.DateTime;
        var overviewLocal = sunsetLocal.AddMinutes(observationOptions.SkyOverviewMinutesAfterSunset);
        var scenes = new List<SceneObservationContext>();

        var usedTimes = new HashSet<DateTime> { overviewLocal };
        var objectScenes = new List<SceneObservationContext>();
        foreach (var (score, i) in selectedScores.Select((score, i) => (score, i)))
        {
            var v = score.Visibility;
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
                SceneIndex = i + 2,
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
                RecommendedTool = ResolveRecommendedTool(v),
                NarrationFocus = $"{v.VisibilityReason} Best around {selection.Local:h:mm tt}. {score.SelectionReason}",
                EstimatedDurationSeconds = ResolveSceneDurationSeconds(v.ObjectType, false),
                VisibilityScore = score.VisibilityScore,
                BrightnessScore = score.BrightnessScore,
                FamiliarityScore = score.FamiliarityScore,
                EngagementScore = score.EngagementScore,
                SpecialEventBonus = score.SpecialEventBonus,
                RarityBonus = score.RarityBonus,
                FinalScore = score.FinalScore,
                SelectionReason = score.SelectionReason,
                IsOptionalForLongForm = !score.IsMajorSpecialEvent,
                IsMajorSpecialEvent = score.IsMajorSpecialEvent,
                Latitude = context.Latitude,
                Longitude = context.Longitude,
                LocationName = context.LocationName
            });

            _loggerStatic?.LogInformation("Selected observation scene {ObjectName} at {SelectedLocalTime} altitude {Altitude:F1}° reason: {Reason}", v.ObjectName, selection.Local, selection.Altitude, selection.Reason);
        }

        for (var i = 0; i < objectScenes.Count; i++)
        {
            var orderedScene = objectScenes[i];
            scenes.Add(new SceneObservationContext
            {
                SceneId = $"object-{i + 1}",
                SceneTitle = orderedScene.SceneTitle,
                SceneType = orderedScene.SceneType,
                SceneIndex = i + 2,
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
                EstimatedDurationSeconds = orderedScene.EstimatedDurationSeconds,
                VisibilityScore = orderedScene.VisibilityScore,
                BrightnessScore = orderedScene.BrightnessScore,
                FamiliarityScore = orderedScene.FamiliarityScore,
                EngagementScore = orderedScene.EngagementScore,
                SpecialEventBonus = orderedScene.SpecialEventBonus,
                RarityBonus = orderedScene.RarityBonus,
                FinalScore = orderedScene.FinalScore,
                SelectionReason = orderedScene.SelectionReason,
                IsOptionalForLongForm = orderedScene.IsOptionalForLongForm,
                IsMajorSpecialEvent = orderedScene.IsMajorSpecialEvent,
                Latitude = orderedScene.Latitude,
                Longitude = orderedScene.Longitude,
                LocationName = orderedScene.LocationName
            });
        }

        var closingLocalTime = Clamp(scenes.Last().LocalObservationTime.AddMinutes(30), observationWindow.SunsetLocal.DateTime, observationWindow.SunriseLocal.DateTime);
        scenes.Add(new SceneObservationContext { SceneId = "closing", SceneTitle = "Closing wide sky", SceneType = "Closing", SceneIndex = scenes.Count + 1, ObjectName = "Sky", ObjectType = "Overview", LocalObservationTime = closingLocalTime, UtcObservationTime = ToUtc(closingLocalTime, tz), Timezone = timezone, IsVisible = true, VisibilityReason = "Wrap-up", RecommendedTool = "Naked eye", NarrationFocus = "Closing overview and safe viewing recap.", EstimatedDurationSeconds = 12, IsOptionalForLongForm = false, Latitude = context.Latitude, Longitude = context.Longitude, LocationName = context.LocationName });

        var overviewDecision = BuildOverviewDecision(selected, visible, context, observationOptions);
        var overviewNarration = BuildOverviewNarration(overviewDecision);
        var overviewScene = new SceneObservationContext
        {
            SceneId = "sky-overview",
            SceneTitle = "Sky overview",
            SceneType = "Overview",
            SceneIndex = 1,
            ObjectName = "Sky",
            ObjectType = "Overview",
            PrimaryObject = overviewDecision.PrimaryObject,
            IncludePolarisOrientation = overviewDecision.PolarisOrientationEnabled,
            LocalObservationTime = overviewLocal,
            UtcObservationTime = ToUtc(overviewLocal, tz),
            Timezone = timezone,
            IsVisible = true,
            VisibilityReason = "Night overview",
            RecommendedTool = "Naked eye",
            NarrationFocus = overviewNarration,
            EstimatedDurationSeconds = 18,
            IsOptionalForLongForm = false,
            Latitude = context.Latitude,
            Longitude = context.Longitude,
            LocationName = context.LocationName
        };
        scenes.Insert(0, overviewScene);
        if (scenes.Count == 0 || !string.Equals(scenes[0].SceneType, "Overview", StringComparison.OrdinalIgnoreCase) || scenes[0].SceneIndex != 1)
        {
            throw new InvalidOperationException("Overview scene missing from sequence");
        }

        foreach (var (scene, index) in scenes.Select((scene, index) => (scene, index)))
        {
            _loggerStatic?.LogInformation("Scene timeline [{SceneIndex}] {ObjectName} at local {LocalTime}", scene.SceneIndex > 0 ? scene.SceneIndex : index + 1, scene.ObjectName, scene.LocalObservationTime);
        }

        context.SceneObservationContexts = scenes;

        var skipped = visible.Where(v => selected.All(s => !s.ObjectName.Equals(v.ObjectName, StringComparison.OrdinalIgnoreCase))).ToList();
        context.VisualIdeas.Add(new VisualIdeaModel { Title = "selected-visible-objects", Description = Serialize(selectedScores.Select(s => BuildSelectionDiagnostic(s, selected: true))) });
        context.VisualIdeas.Add(new VisualIdeaModel { Title = "content-expansion-selection", Description = Serialize(new { selectedObjects = selectedScores.Select(s => BuildSelectionDiagnostic(s, selected: true)), skippedObjects = scoredObjects.Where(s => selectedScores.All(selectedScore => !selectedScore.Visibility.ObjectName.Equals(s.Visibility.ObjectName, StringComparison.OrdinalIgnoreCase))).Select(s => BuildSelectionDiagnostic(s, selected: false)), minObjects = _contentExpansionOptions.MinObjectsPerGuide, targetObjects = 5, maxObjects = maxPrimaryObjects, enabled = _contentExpansionOptions.Enabled }) });
        context.VisualIdeas.Add(new VisualIdeaModel { Title = "scene-observation-context", Description = Serialize(scenes) });
        context.VisualIdeas.Add(new VisualIdeaModel { Title = "narration-context", Description = Serialize(scenes.Select(s => new { s.SceneId, s.ObjectName, s.LocalObservationTime, s.UtcObservationTime, s.DirectionLabel, s.AltitudeDegrees, s.IsVisible, s.VisibilityReason })) });
        context.VisualIdeas.Add(new VisualIdeaModel
        {
            Title = "overview-strategy.json",
            Description = Serialize(new
            {
                mode = overviewDecision.Mode,
                primaryObject = overviewDecision.PrimaryObject,
                overviewSceneExists = true,
                sceneIndex = overviewScene.SceneIndex
            })
        });
        return true;
    }

    private sealed record ScoredVisibleObject(
        SkyfieldObjectVisibility Visibility,
        double VisibilityScore,
        double BrightnessScore,
        double FamiliarityScore,
        double EngagementScore,
        double SpecialEventBonus,
        double RarityBonus,
        double FinalScore,
        bool IsMajorSpecialEvent,
        string SelectionReason);

    private static IEnumerable<ScoredVisibleObject> SelectAdaptiveLongFormObjects(IReadOnlyList<ScoredVisibleObject> scoredObjects, int maxPrimaryObjects)
    {
        var selected = new List<ScoredVisibleObject>();
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var planetCount = 0;
        var moonCount = 0;
        var deepSkyCount = 0;
        var starOrConstellationCount = 0;

        foreach (var scored in scoredObjects)
        {
            if (!selectedNames.Add(scored.Visibility.ObjectName))
            {
                continue;
            }

            var type = scored.Visibility.ObjectType ?? string.Empty;
            var isMoon = type.Equals("Moon", StringComparison.OrdinalIgnoreCase) || scored.Visibility.ObjectName.Contains("Moon", StringComparison.OrdinalIgnoreCase);
            var isPlanet = type.Equals("Planet", StringComparison.OrdinalIgnoreCase);
            var isDeepSky = IsDeepSkyType(type);
            var isStarOrConstellation = type.Contains("Star", StringComparison.OrdinalIgnoreCase) || type.Contains("Constellation", StringComparison.OrdinalIgnoreCase);

            if ((isMoon && moonCount >= 1) ||
                (isPlanet && planetCount >= 5) ||
                (isDeepSky && deepSkyCount >= 1) ||
                (isStarOrConstellation && starOrConstellationCount >= 1))
            {
                selectedNames.Remove(scored.Visibility.ObjectName);
                continue;
            }

            selected.Add(scored);
            moonCount += isMoon ? 1 : 0;
            planetCount += isPlanet ? 1 : 0;
            deepSkyCount += isDeepSky ? 1 : 0;
            starOrConstellationCount += isStarOrConstellation ? 1 : 0;

            if (selected.Count >= maxPrimaryObjects)
            {
                break;
            }
        }

        return selected;
    }

    private static ScoredVisibleObject ScoreVisibleObject(SkyfieldObjectVisibility visibility, ObservationOptions observationOptions)
    {
        var visibilityScore = Math.Clamp(((visibility.AltitudeDegrees ?? 0d) / 90d) * 10d, 0d, 10d);
        if ((visibility.AltitudeDegrees ?? 0d) >= observationOptions.PreferredObjectAltitudeDegrees)
        {
            visibilityScore = Math.Min(10d, visibilityScore + 1.25d);
        }

        var brightnessScore = ResolveBrightnessScore(visibility.ObjectName, visibility.ObjectType);
        var familiarityScore = ResolveFamiliarityScore(visibility.ObjectName, visibility.ObjectType);
        var engagementScore = ResolveEngagementScore(visibility.ObjectName, visibility.ObjectType);
        var specialEventBonus = ResolveSpecialEventBonus(visibility.ObjectName, visibility.ObjectType);
        var rarityBonus = ResolveRarityBonus(visibility.ObjectName, visibility.ObjectType);
        var finalScore = visibilityScore + brightnessScore + familiarityScore + engagementScore + specialEventBonus + rarityBonus;
        var isMajorSpecialEvent = specialEventBonus >= 5d;
        var selectionReason = BuildSelectionReason(visibility, brightnessScore, engagementScore, specialEventBonus, rarityBonus);

        return new ScoredVisibleObject(visibility, visibilityScore, brightnessScore, familiarityScore, engagementScore, specialEventBonus, rarityBonus, finalScore, isMajorSpecialEvent, selectionReason);
    }

    private static object BuildSelectionDiagnostic(ScoredVisibleObject score, bool selected) => new
    {
        @object = score.Visibility.ObjectName,
        objectType = score.Visibility.ObjectType,
        visibilityScore = Math.Round(score.VisibilityScore, 2),
        brightnessScore = Math.Round(score.BrightnessScore, 2),
        familiarityScore = Math.Round(score.FamiliarityScore, 2),
        engagementScore = Math.Round(score.EngagementScore, 2),
        specialEventBonus = Math.Round(score.SpecialEventBonus, 2),
        rarityBonus = Math.Round(score.RarityBonus, 2),
        finalScore = Math.Round(score.FinalScore, 2),
        selected,
        selectionReason = score.SelectionReason
    };

    private static double ResolveBrightnessScore(string? objectName, string? objectType)
    {
        var name = objectName ?? string.Empty;
        var type = objectType ?? string.Empty;
        if (name.Contains("Moon", StringComparison.OrdinalIgnoreCase)) return 9.8d;
        if (name.Contains("Venus", StringComparison.OrdinalIgnoreCase)) return 9.7d;
        if (name.Contains("Jupiter", StringComparison.OrdinalIgnoreCase)) return 9.4d;
        if (name.Contains("Saturn", StringComparison.OrdinalIgnoreCase)) return 8.5d;
        if (name.Contains("Mars", StringComparison.OrdinalIgnoreCase)) return 8.1d;
        if (type.Contains("Star", StringComparison.OrdinalIgnoreCase)) return 7.2d;
        if (IsDeepSkyType(type)) return 5.4d;
        if (type.Contains("Planet", StringComparison.OrdinalIgnoreCase)) return 7.4d;
        return 5.8d;
    }

    private static double ResolveFamiliarityScore(string? objectName, string? objectType)
    {
        var name = objectName ?? string.Empty;
        if (name.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) || name.Contains("Venus", StringComparison.OrdinalIgnoreCase) || name.Contains("Saturn", StringComparison.OrdinalIgnoreCase) || name.Contains("Moon", StringComparison.OrdinalIgnoreCase) || name.Contains("Mars", StringComparison.OrdinalIgnoreCase)) return 9.5d;
        if (name.Contains("Orion", StringComparison.OrdinalIgnoreCase) || name.Contains("Pleiades", StringComparison.OrdinalIgnoreCase) || name.Contains("Sirius", StringComparison.OrdinalIgnoreCase)) return 8d;
        var type = objectType ?? string.Empty;
        if (type.Contains("Planet", StringComparison.OrdinalIgnoreCase)) return 7d;
        if (IsDeepSkyType(type)) return 5.5d;
        return 5d;
    }

    private static double ResolveEngagementScore(string? objectName, string? objectType)
    {
        var name = objectName ?? string.Empty;
        if (name.Contains("Jupiter", StringComparison.OrdinalIgnoreCase)) return 9.6d;
        if (name.Contains("Venus", StringComparison.OrdinalIgnoreCase)) return 9.4d;
        if (name.Contains("Saturn", StringComparison.OrdinalIgnoreCase)) return 9.2d;
        if (name.Contains("Moon", StringComparison.OrdinalIgnoreCase)) return 9.1d;
        if (name.Contains("Mars", StringComparison.OrdinalIgnoreCase)) return 8.6d;
        if (name.Contains("Meteor", StringComparison.OrdinalIgnoreCase) || name.Contains("Eclipse", StringComparison.OrdinalIgnoreCase) || name.Contains("Conjunction", StringComparison.OrdinalIgnoreCase)) return 10d;
        var type = objectType ?? string.Empty;
        if (IsDeepSkyType(type)) return 7.2d;
        if (type.Contains("Star", StringComparison.OrdinalIgnoreCase) || type.Contains("Constellation", StringComparison.OrdinalIgnoreCase)) return 6.5d;
        return 5d;
    }

    private static double ResolveSpecialEventBonus(string? objectName, string? objectType)
    {
        var text = $"{objectName} {objectType}";
        return text.Contains("Meteor", StringComparison.OrdinalIgnoreCase) || text.Contains("Eclipse", StringComparison.OrdinalIgnoreCase) || text.Contains("Conjunction", StringComparison.OrdinalIgnoreCase) || text.Contains("Occultation", StringComparison.OrdinalIgnoreCase) || text.Contains("Event", StringComparison.OrdinalIgnoreCase) ? 7.5d : 0d;
    }

    private static double ResolveRarityBonus(string? objectName, string? objectType)
    {
        var text = $"{objectName} {objectType}";
        if (text.Contains("Eclipse", StringComparison.OrdinalIgnoreCase)) return 4d;
        if (text.Contains("Meteor", StringComparison.OrdinalIgnoreCase) || text.Contains("Conjunction", StringComparison.OrdinalIgnoreCase)) return 3d;
        if (IsDeepSkyType(objectType ?? string.Empty)) return 1.2d;
        return 0d;
    }

    private static string BuildSelectionReason(SkyfieldObjectVisibility visibility, double brightnessScore, double engagementScore, double specialEventBonus, double rarityBonus)
    {
        if (specialEventBonus > 0d)
        {
            return "Major timely sky event with strong audience interest";
        }

        if (brightnessScore >= 9d && engagementScore >= 9d)
        {
            return "Bright and highly recognizable evening target";
        }

        if (rarityBonus > 0d)
        {
            return "Adds a rarer deep-sky layer after the major bright objects";
        }

        return "Visible target selected to keep the long-form sky tour diverse";
    }

    private static bool IsDeepSkyType(string objectType)
        => objectType.Contains("Deep", StringComparison.OrdinalIgnoreCase)
           || objectType.Contains("Cluster", StringComparison.OrdinalIgnoreCase)
           || objectType.Contains("Galaxy", StringComparison.OrdinalIgnoreCase)
           || objectType.Contains("Nebula", StringComparison.OrdinalIgnoreCase);

    private static int ResolveSceneDurationSeconds(string? objectType, bool isSpecialEvent)
    {
        if (isSpecialEvent) return 30;
        var type = objectType ?? string.Empty;
        if (IsDeepSkyType(type)) return 30;
        return 36;
    }

    private static string ResolveRecommendedTool(SkyfieldObjectVisibility visibility)
    {
        var type = visibility.ObjectType ?? string.Empty;
        if (type.Equals("Moon", StringComparison.OrdinalIgnoreCase) || type.Equals("Planet", StringComparison.OrdinalIgnoreCase))
        {
            return "Naked eye / binoculars";
        }

        if (IsDeepSkyType(type))
        {
            return "Binoculars / telescope";
        }

        return "Naked eye";
    }


    private bool IsAllowedByContentExpansion(SkyfieldObjectVisibility visibility)
    {
        if (!_contentExpansionOptions.Enabled)
        {
            return true;
        }

        var type = visibility.ObjectType ?? string.Empty;
        if (type.Equals("Moon", StringComparison.OrdinalIgnoreCase)) return _contentExpansionOptions.AllowMoonSegment;
        if (type.Contains("Constellation", StringComparison.OrdinalIgnoreCase)) return _contentExpansionOptions.AllowConstellations;
        if (type.Contains("Star", StringComparison.OrdinalIgnoreCase)) return _contentExpansionOptions.AllowBrightStars;
        if (type.Contains("Deep", StringComparison.OrdinalIgnoreCase) || type.Contains("Cluster", StringComparison.OrdinalIgnoreCase) || type.Contains("Galaxy", StringComparison.OrdinalIgnoreCase) || type.Contains("Nebula", StringComparison.OrdinalIgnoreCase)) return _contentExpansionOptions.AllowDeepSkyObjects;
        return true;
    }

    private double ScoreVisibility(SkyfieldObjectVisibility visibility, ObservationOptions observationOptions)
    {
        var altitude = Math.Max(0d, visibility.AltitudeDegrees ?? 0d);
        var altitudeScore = Math.Clamp(altitude / 90d, 0d, 1d);
        var typeScore = ResolveObjectPriority(visibility.ObjectType, visibility.ObjectName) / 100d;
        var preferredAltitudeBoost = altitude >= observationOptions.PreferredObjectAltitudeDegrees ? 0.15d : 0d;
        var beginnerBoost = visibility.ObjectType.Equals("Moon", StringComparison.OrdinalIgnoreCase) || visibility.ObjectType.Equals("Planet", StringComparison.OrdinalIgnoreCase) ? 0.1d : 0d;
        return Math.Clamp((altitudeScore * 0.45d) + (typeScore * 0.35d) + preferredAltitudeBoost + beginnerBoost, 0d, 1d);
    }

    private static int ResolveObjectPriority(string? objectType, string? objectName)
    {
        var type = objectType ?? string.Empty;
        var name = objectName ?? string.Empty;
        if (type.Contains("Event", StringComparison.OrdinalIgnoreCase) || name.Contains("event", StringComparison.OrdinalIgnoreCase)) return 100;
        if (type.Equals("Moon", StringComparison.OrdinalIgnoreCase)) return 90;
        if (type.Equals("Planet", StringComparison.OrdinalIgnoreCase)) return 80;
        if (type.Contains("Constellation", StringComparison.OrdinalIgnoreCase)) return 70;
        if (type.Contains("Star", StringComparison.OrdinalIgnoreCase)) return 60;
        if (type.Contains("Deep", StringComparison.OrdinalIgnoreCase) || type.Contains("Cluster", StringComparison.OrdinalIgnoreCase) || type.Contains("Galaxy", StringComparison.OrdinalIgnoreCase) || type.Contains("Nebula", StringComparison.OrdinalIgnoreCase)) return 50;
        return 40;
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

    private static string BuildOverviewNarration(OverviewStrategyDiagnostics overviewDecision)
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
