using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.NasaAssets;
using Microsoft.Extensions.Logging;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RealizedVisualAssetSourceType
{
    StellariumBase,
    StellariumExpanded,
    AICinematic,
    NASA,
    JWST,
    MotionGraphics,
    EducationalOverlay
}

public sealed record MotionGraphicManifestEntry(
    string SegmentId,
    string SegmentType,
    string AssetType,
    string AssetPath,
    int DurationSeconds,
    string GraphicType,
    IReadOnlyList<string> DataSources,
    IReadOnlyList<string> ContentLines);

public sealed record EducationalOverlayManifestEntry(
    string SegmentId,
    string SegmentType,
    string AssetType,
    string AssetPath,
    int DurationSeconds,
    string OverlayType,
    IReadOnlyList<string> DataSources,
    IReadOnlyList<string> ContentLines);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductionAssetQualityStatus
{
    ProductionReady,
    ProductionWarning,
    ProductionFailed
}

public sealed record WeeklyAssetQualityReport(
    int TotalAssets,
    int ProductionReadyCount,
    int ProductionWarningCount,
    int ProductionFailedCount,
    int StellariumPassed,
    int StellariumFailed,
    int ExpandedPassed,
    int ExpandedFailed,
    int AiPassed,
    int RequiredAICinematicAssetsPassed,
    int RequiredAICinematicAssetsFailed,
    bool AICinematicRequiredPackageReady,
    int NasaPassed,
    int JwstPassed,
    int MotionPassed,
    int OverlayPassed,
    bool QualityGatePassed);

public sealed record WeeklyAssetQualityDetail(
    string AssetId,
    string SourceType,
    string AssetCode,
    string AssetPath,
    ProductionAssetQualityStatus Status,
    IReadOnlyList<string> PassedChecks,
    IReadOnlyList<string> FailedChecks,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Warnings,
    int Width,
    int Height,
    long FileSizeBytes,
    string ValidationProfile);

public sealed record WeeklyAssetQualityValidationResult(
    WeeklyAssetQualityReport Report,
    IReadOnlyList<WeeklyAssetQualityDetail> Details,
    string ReportPath,
    string DetailsPath)
{
    public IReadOnlyList<string> FailedAssetPaths => Details
        .Where(x => x.Status == ProductionAssetQualityStatus.ProductionFailed)
        .Select(x => x.AssetPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

public sealed record RealizedVisualAsset(
    string AssetId,
    RealizedVisualAssetSourceType SourceType,
    string AssetCode,
    string FilePath,
    bool Exists,
    long FileSizeBytes,
    int Width,
    int Height,
    string SegmentUsageRole,
    bool Reusable,
    bool ProductionReady);

public sealed record SegmentProductionAssetBundle(
    string SegmentId,
    string EpisodeType,
    string SegmentType,
    int TargetDurationSeconds,
    string NarrationStatus,
    string NarrationTextPath,
    int NarrationEstimatedWords,
    IReadOnlyList<RealizedVisualAsset> AssignedVisualAssets,
    IReadOnlyList<string> MissingVisualAssetTypes,
    bool ProductionReady,
    string ReadinessReason,
    IReadOnlyList<string> Warnings,
    bool ProductionReadyForTest,
    bool ProductionReadyForFinalVideo);

public sealed record WeeklyProductionAssetManifest(
    Guid PipelineRunId,
    string RegionId,
    string Language,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    int LongformTargetDurationSeconds,
    int ShortformTargetDurationSeconds,
    int TotalProductionImageAssetCount,
    int StellariumBaseAssetCount,
    int ExpandedStellariumAssetCount,
    int AICinematicAssetCount,
    int NASAAssetCount,
    int JWSTAssetCount,
    int MotionGraphicsAssetCount,
    int EducationalOverlayAssetCount,
    IReadOnlyList<SegmentProductionAssetBundle> SegmentBundles);

public sealed record SegmentAssetCoverageResult(
    string SegmentId,
    string EpisodeType,
    string SegmentType,
    int AssignedVisualAssetCount,
    IReadOnlyList<string> SatisfiedAssetTypesForTest,
    IReadOnlyList<string> MissingAssetTypesForFinal,
    bool FallbackUsed,
    bool ProductionReadyForTest,
    bool ProductionReadyForFinalVideo,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyAssetCoverageAuditReport(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    int PlannedVisualAssetCount,
    int RealizedVisualAssetCount,
    int ProductionReadyVisualAssetCount,
    int MissingVisualAssetCount,
    IReadOnlyDictionary<string, int> RealizedBySource,
    IReadOnlyDictionary<string, int> MissingBySource,
    IReadOnlyList<SegmentAssetCoverageResult> SegmentCoverage,
    double CoveragePercentage,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyVideoReadinessReport(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    bool TestVideoPipelineReady,
    bool FinalVideoPipelineReady,
    bool LongformTestReady,
    bool ShortformTestReady,
    bool LongformFinalReady,
    bool ShortformFinalReady,
    int ReadySegmentCountForTest,
    int ReadySegmentCountForFinal,
    IReadOnlyList<string> NotReadySegments,
    IReadOnlyList<string> MissingAssetCategories,
    IReadOnlyList<string> MissingNarrationCategories,
    IReadOnlyList<string> RecommendedNextActions);

public sealed record WeeklyAssetRealizationResult(
    WeeklyProductionAssetManifest Manifest,
    WeeklyAssetCoverageAuditReport RealizationReport,
    WeeklyVideoReadinessReport VideoReadinessReport,
    string WeeklyProductionAssetManifestPath,
    string WeeklyAssetRealizationReportPath,
    string WeeklyVideoReadinessReportPath,
    bool AssetRealizationReady,
    string NasaAssetPlanPath,
    string NasaAssetResultsPath,
    string NasaAssetRealizationReportPath,
    int PlannedNASAAssetCount,
    int GeneratedNASAAssetCount,
    int ProductionReadyNASAAssetCount,
    int FailedNASAAssetCount,
    IReadOnlyList<string> NasaImagePaths,
    int NasaImageCount,
    string JwstAssetPlanPath,
    string JwstAssetResultsPath,
    string JwstAssetRealizationReportPath,
    int PlannedJWSTAssetCount,
    int GeneratedJWSTAssetCount,
    int ProductionReadyJWSTAssetCount,
    int FailedJWSTAssetCount,
    IReadOnlyList<string> JwstImagePaths,
    int JwstImageCount,
    bool NasaProviderConfigured,
    string MotionGraphicsManifestPath = "",
    int PlannedMotionGraphicCount = 0,
    int GeneratedMotionGraphicCount = 0,
    int ProductionReadyMotionGraphicCount = 0,
    IReadOnlyList<string>? MotionGraphicPaths = null,
    string EducationalOverlayManifestPath = "",
    int PlannedEducationalOverlayCount = 0,
    int GeneratedEducationalOverlayCount = 0,
    int ProductionReadyEducationalOverlayCount = 0,
    IReadOnlyList<string>? EducationalOverlayPaths = null,
    string AssetQualityReportPath = "",
    string AssetQualityDetailsPath = "",
    int TotalValidatedAssets = 0,
    int ProductionReadyAssetCount = 0,
    int ProductionWarningAssetCount = 0,
    int ProductionFailedAssetCount = 0,
    bool QualityGatePassed = false,
    IReadOnlyList<string>? FailedAssetPaths = null);

public sealed record WeeklyAssetRealizationInput(
    Guid PipelineRunId,
    string RegionId,
    string Language,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    string RootPath,
    string StoryBeatsPath,
    string NarrationTextPath,
    WeeklyEpisodePlan LongformPlan,
    WeeklyEpisodePlan ShortformPlan,
    WeeklySegmentClassificationPlan SegmentClassificationPlan,
    WeeklyVisualAssetPlan VisualAssetPlan,
    string WeeklyVisualAssetPlanPath,
    IReadOnlyList<string> FrameScreenshots,
    IReadOnlyList<string> ExpandedFrameScreenshots,
    IReadOnlyList<string> AICinematicImagePaths,
    IReadOnlyList<string> AllProductionImageAssets,
    WeeklySkyForecastContext? SkyfieldContext = null);


internal sealed record GeneratedMotionEducationalAssets(
    IReadOnlyList<MotionGraphicManifestEntry> MotionGraphics,
    IReadOnlyList<EducationalOverlayManifestEntry> EducationalOverlays,
    string MotionGraphicsManifestPath,
    string EducationalOverlayManifestPath)
{
    public IReadOnlyList<string> MotionGraphicPaths => MotionGraphics.Select(x => x.AssetPath).ToList();
    public IReadOnlyList<string> EducationalOverlayPaths => EducationalOverlays.Select(x => x.AssetPath).ToList();
}

internal sealed class MotionGraphicsAndEducationalOverlayRealizer(ILogger logger)
{
    private const int Width = 1920;
    private const int Height = 1080;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    public async Task<GeneratedMotionEducationalAssets> RealizeAsync(WeeklyAssetRealizationInput input, CancellationToken cancellationToken)
    {
        var motionDirectory = Path.Combine(input.RootPath, "assets", "motion-graphics");
        var overlayDirectory = Path.Combine(input.RootPath, "assets", "educational-overlays");
        Directory.CreateDirectory(motionDirectory);
        Directory.CreateDirectory(overlayDirectory);

        var motionEntries = new List<MotionGraphicManifestEntry>();
        var educationalEntries = new List<EducationalOverlayManifestEntry>();
        var allPlans = input.VisualAssetPlan.LongformSegmentVisualPlans.Concat(input.VisualAssetPlan.ShortformSegmentVisualPlans).ToList();
        var allAssignments = input.SegmentClassificationPlan.LongformAssignments.Concat(input.SegmentClassificationPlan.ShortformAssignments).ToList();

        foreach (var plan in allPlans.Where(p => p.SourcePlans.Any(s => s.SourceType == VisualAssetSourceType.MotionGraphics)))
        {
            var assignment = allAssignments.FirstOrDefault(a => a.SegmentId.Equals(plan.SegmentId, StringComparison.OrdinalIgnoreCase));
            var card = BuildMotionCard(input, plan, assignment);
            await AddMotionAsync(card, plan, motionDirectory, motionEntries, cancellationToken);
        }

        await EnsureRequiredMotionCardsAsync(input, allPlans, allAssignments, motionDirectory, motionEntries, cancellationToken);

        foreach (var plan in allPlans.Where(p => p.SourcePlans.Any(s => s.SourceType == VisualAssetSourceType.EducationalOverlay)))
        {
            var assignment = allAssignments.FirstOrDefault(a => a.SegmentId.Equals(plan.SegmentId, StringComparison.OrdinalIgnoreCase));
            var overlays = BuildEducationalCards(input, plan, assignment).Take(1).ToList();
            foreach (var card in overlays)
            {
                var path = Path.Combine(overlayDirectory, card.FileName);
                await RenderCardAsync(path, card.Title, card.Subtitle, card.Lines, card.Accent, cancellationToken);
                educationalEntries.Add(new EducationalOverlayManifestEntry(plan.SegmentId, plan.SegmentType, "EducationalOverlay", path, Math.Clamp(plan.EstimatedScreenTimeSeconds, 4, 8), card.GraphicType, card.DataSources, card.Lines));
                logger.LogInformation("EDUCATIONAL_OVERLAY_REALIZED segmentId={SegmentId} segmentType={SegmentType} path={Path}", plan.SegmentId, plan.SegmentType, path);
            }
        }

        var episodeDirectory = Path.Combine(input.RootPath, "episode");
        Directory.CreateDirectory(episodeDirectory);
        var motionManifestPath = Path.Combine(episodeDirectory, "motion-graphics-manifest.json");
        var educationalManifestPath = Path.Combine(episodeDirectory, "educational-overlay-manifest.json");
        await File.WriteAllTextAsync(motionManifestPath, JsonSerializer.Serialize(motionEntries, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(educationalManifestPath, JsonSerializer.Serialize(educationalEntries, JsonOptions), cancellationToken);
        return new GeneratedMotionEducationalAssets(motionEntries, educationalEntries, motionManifestPath, educationalManifestPath);
    }


    private static async Task AddMotionAsync((string FileName, string GraphicType, string Title, string Subtitle, IReadOnlyList<string> Lines, Color Accent, IReadOnlyList<string> DataSources) card, SegmentVisualAssetPlan plan, string motionDirectory, List<MotionGraphicManifestEntry> motionEntries, CancellationToken cancellationToken)
    {
        if (motionEntries.Any(x => Path.GetFileName(x.AssetPath).Equals(card.FileName, StringComparison.OrdinalIgnoreCase))) return;
        var path = Path.Combine(motionDirectory, card.FileName);
        await RenderCardAsync(path, card.Title, card.Subtitle, card.Lines, card.Accent, cancellationToken);
        motionEntries.Add(new MotionGraphicManifestEntry(plan.SegmentId, plan.SegmentType, "MotionGraphic", path, Math.Clamp(plan.EstimatedScreenTimeSeconds, 4, 8), card.GraphicType, card.DataSources, card.Lines));
    }

    private static async Task EnsureRequiredMotionCardsAsync(WeeklyAssetRealizationInput input, IReadOnlyList<SegmentVisualAssetPlan> allPlans, IReadOnlyList<WeeklySegmentAssignment> allAssignments, string motionDirectory, List<MotionGraphicManifestEntry> motionEntries, CancellationToken cancellationToken)
    {
        var overview = allPlans.FirstOrDefault(p => p.SegmentType == "WeeklySkyOverview");
        if (overview is not null)
        {
            var daily = input.SkyfieldContext?.DailyForecasts.OrderBy(d => d.Date).ToList() ?? [];
            var lines = daily.Take(7).Select(d => $"{d.Date:ddd MM/dd}: Moon {d.MoonIlluminationPercent:0}% • planets {string.Join(", ", d.VisibleObjects.Where(o => o.Visible && o.ObjectType.Contains("planet", StringComparison.OrdinalIgnoreCase)).OrderByDescending(o => o.VisibilityScore).Take(3).Select(o => o.ObjectName))} • score {d.OverallViewingScore:0.00}").ToList();
            lines.Add($"Best viewing days: {string.Join(", ", input.SkyfieldContext?.RecommendedNights.OrderByDescending(n => n.Score).Take(3).Select(n => n.Date.ToString("ddd MMM d", CultureInfo.InvariantCulture)) ?? [])}");
            await AddMotionAsync(("visibility-calendar.png", "VisibilityCalendar", "Visibility Calendar", $"{input.WeekStartDate:MMM d}–{input.WeekEndDate:MMM d}", lines, Color.Teal, DataSources(overview)), overview, motionDirectory, motionEntries, cancellationToken);
        }

        var hero = allPlans.FirstOrDefault(p => p.SegmentType == "HeroEvent");
        if (hero is not null)
        {
            var heroCard = BuildMotionCard(input, hero, allAssignments.FirstOrDefault(a => a.SegmentId == hero.SegmentId));
            await AddMotionAsync(("hero-event-card.png", "HeroEvent", heroCard.Title, heroCard.Subtitle, heroCard.Lines, heroCard.Accent, heroCard.DataSources), hero, motionDirectory, motionEntries, cancellationToken);
        }

        var where = allPlans.FirstOrDefault(p => p.SegmentType is "MoonHighlights" or "PlanetHighlights" or "WhereToLook");
        if (where is not null)
        {
            var assignment = allAssignments.FirstOrDefault(a => a.SegmentId == where.SegmentId);
            var obj = BestObject(input.SkyfieldContext, assignment);
            await AddMotionAsync(("where-to-look-card.png", "WhereToLook", "Where To Look", string.Join(" + ", where.AssignedObjects.DefaultIfEmpty(obj?.ObjectName ?? "Sky target")), [$"Direction: {obj?.ViewingDirection ?? assignment?.VisibilitySummary ?? "verified horizon direction"}", $"Altitude: {FormatDegrees(obj?.MaxAltitudeDegrees)}", $"Azimuth: {FormatDegrees(obj?.BestViewingAzimuthDegrees)}", $"Viewing recommendation: {obj?.Reason ?? assignment?.VisibilitySummary ?? "Use the verified Skyfield viewing window."}", $"Priority score: {where.AssetPriority}/100"], Color.Orange, DataSources(where)), where, motionDirectory, motionEntries, cancellationToken);
        }
    }

    private static (string FileName, string GraphicType, string Title, string Subtitle, IReadOnlyList<string> Lines, Color Accent, IReadOnlyList<string> DataSources) BuildMotionCard(WeeklyAssetRealizationInput input, SegmentVisualAssetPlan plan, WeeklySegmentAssignment? assignment)
    {
        var context = input.SkyfieldContext;
        var topEvents = TopEvents(context, assignment).ToList();
        var bestNight = context?.RecommendedNights.OrderByDescending(x => x.Score).FirstOrDefault();
        var bestObject = BestObject(context, assignment);
        var eventLine = topEvents.FirstOrDefault()?.Title ?? assignment?.AssignedEventType ?? "Verified weekly sky event";
        var bestDate = assignment?.AssignedDateLocal ?? bestNight?.Date ?? input.WeekStartDate;
        var bestTime = assignment?.AssignedBestTimeLocal?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? FormatUtc(bestObject?.BestViewingTimeUtc ?? bestNight?.BestStartUtc);
        var direction = assignment?.VisibilitySummary.Contains("direction", StringComparison.OrdinalIgnoreCase) == true ? assignment.VisibilitySummary : bestObject?.ViewingDirection ?? topEvents.FirstOrDefault()?.Direction ?? "Use local horizon direction from Skyfield data";
        var visibility = topEvents.FirstOrDefault()?.VisibilityScore ?? bestObject?.VisibilityScore ?? bestNight?.Score ?? plan.AssetPriority / 100d;
        var skyQuality = Daily(context, bestDate)?.OverallViewingScore ?? bestNight?.Score ?? visibility;
        var moon = Daily(context, bestDate)?.MoonIlluminationPercent;

        return plan.SegmentType switch
        {
            "WeeklySkyOverview" => BuildWeeklyOverview(input, context, topEvents),
            "BestObservationWindow" or "BestTime" => (Name(plan, "best-observation-window-card.png"), "BestObservationWindow", "Best Observation Window", $"{bestDate:MMM d} • {bestTime}", [
                $"Date: {bestDate:dddd, MMM d}", $"Time: {bestTime}", $"Direction: {direction}", $"Moon illumination: {(moon.HasValue ? moon.Value.ToString("0", CultureInfo.InvariantCulture) + "%" : "Skyfield moon data unavailable")}", $"Sky quality score: {skyQuality:0.00}", $"Priority score: {plan.AssetPriority}/100"], Color.DeepSkyBlue, DataSources(plan)),
            "WhereToLook" => (Name(plan, "where-to-look-card.png"), "WhereToLook", "Where To Look", string.Join(" + ", plan.AssignedObjects.DefaultIfEmpty(bestObject?.ObjectName ?? "Sky target")), [
                $"Direction: {direction}", $"Altitude: {FormatDegrees(bestObject?.MaxAltitudeDegrees)}", $"Azimuth: {FormatDegrees(bestObject?.BestViewingAzimuthDegrees)}", $"Viewing recommendation: {bestObject?.Reason ?? assignment?.VisibilitySummary ?? "Use the verified best viewing window."}", $"Priority score: {plan.AssetPriority}/100"], Color.Orange, DataSources(plan)),
            "WeeklySummary" or "CallToAction" => (Name(plan, "weekly-summary-card.png"), "WeeklySummary", "Weekly Summary", $"{input.WeekStartDate:MMM d}–{input.WeekEndDate:MMM d}", [
                $"Top events: {string.Join(" • ", topEvents.Take(3).Select(e => e.Title)).Trim()}", $"Best night: {(bestNight is null ? bestDate.ToString("MMM d", CultureInfo.InvariantCulture) : bestNight.Date.ToString("MMM d", CultureInfo.InvariantCulture))}", $"Best viewing window: {FormatUtc(bestNight?.BestStartUtc)}–{FormatUtc(bestNight?.BestEndUtc)}", $"Best objects: {string.Join(", ", bestNight?.BestObjects ?? plan.AssignedObjects)}", $"Priority score: {plan.AssetPriority}/100"], Color.MediumPurple, DataSources(plan)),
            _ => (Name(plan, "hero-event-card.png"), "HeroEvent", eventLine, $"{bestDate:MMM d} • {bestTime}", [
                $"Hero Event Name: {eventLine}", $"Date: {bestDate:dddd, MMM d}", $"Best Viewing Time: {bestTime}", $"Direction: {direction}", $"Visibility Score: {visibility:0.00}", $"Priority Score: {plan.AssetPriority}/100"], Color.Gold, DataSources(plan))
        };
    }

    private static (string FileName, string GraphicType, string Title, string Subtitle, IReadOnlyList<string> Lines, Color Accent, IReadOnlyList<string> DataSources) BuildWeeklyOverview(WeeklyAssetRealizationInput input, WeeklySkyForecastContext? context, IReadOnlyList<WeeklyAstronomyEvent> topEvents)
    {
        var daily = context?.DailyForecasts.OrderBy(x => x.Date).ToList() ?? [];
        var lines = new List<string> { $"Week Start: {input.WeekStartDate:yyyy-MM-dd}", $"Week End: {input.WeekEndDate:yyyy-MM-dd}" };
        for (var i = 0; i < 7; i++)
        {
            var date = input.WeekStartDate.AddDays(i);
            var day = daily.FirstOrDefault(d => d.Date == date);
            var events = topEvents.Where(e => e.BestDateLocal == date).Select(e => e.Title).Take(2).ToList();
            var visible = day?.VisibleObjects.Where(o => o.Visible).OrderByDescending(o => o.VisibilityScore).Take(2).Select(o => o.ObjectName).ToList() ?? [];
            lines.Add($"{DayNames[i]} {date:MM/dd}: {string.Join(", ", events.Concat(visible).Distinct().Take(3))}");
        }
        lines.Add($"Best viewing days: {string.Join(", ", (context?.RecommendedNights.OrderByDescending(n => n.Score).Take(3).Select(n => n.Date.ToString("ddd MMM d", CultureInfo.InvariantCulture)) ?? []))}");
        lines.Add($"Moon/planet visibility generated from {daily.Count} Skyfield daily forecasts.");
        return ("weekly-overview-timeline.png", "WeeklyOverviewTimeline", "Weekly Overview Timeline", $"{input.WeekStartDate:MMM d}–{input.WeekEndDate:MMM d}", lines, Color.CornflowerBlue, ["Skyfield daily forecasts", "Event extraction", "Episode Architecture", "Visual Source Orchestration"]);
    }

    private static IEnumerable<(string FileName, string GraphicType, string Title, string Subtitle, IReadOnlyList<string> Lines, Color Accent, IReadOnlyList<string> DataSources)> BuildEducationalCards(WeeklyAssetRealizationInput input, SegmentVisualAssetPlan plan, WeeklySegmentAssignment? assignment)
    {
        var evt = TopEvents(input.SkyfieldContext, assignment).FirstOrDefault();
        var type = ResolveEducationalType(evt?.EventType.ToString() ?? assignment?.AssignedEventType ?? string.Join(" ", plan.AssignedObjects));
        var bestObject = BestObject(input.SkyfieldContext, assignment);
        var bestDate = assignment?.AssignedDateLocal ?? evt?.BestDateLocal ?? input.SkyfieldContext?.BestPhotographyNight ?? input.WeekStartDate;
        var daily = Daily(input.SkyfieldContext, bestDate);
        var lines = type switch
        {
            "Moon Phase Explainer" => new[] { $"Moon phase: {daily?.MoonPhase ?? "from Skyfield forecast"}", $"Illumination: {(daily is null ? "Skyfield moon data unavailable" : daily.MoonIlluminationPercent.ToString("0", CultureInfo.InvariantCulture) + "%")}", $"Best Moon night: {input.SkyfieldContext?.BestMoonNight?.ToString("MMM d", CultureInfo.InvariantCulture) ?? bestDate.ToString("MMM d", CultureInfo.InvariantCulture)}", $"Viewing note: lower glare improves faint-sky contrast." },
            "Planet Grouping Explainer" => new[] { $"Objects: {string.Join(", ", plan.AssignedObjects.DefaultIfEmpty(bestObject?.ObjectName ?? "visible planets"))}", $"Direction: {bestObject?.ViewingDirection ?? evt?.Direction ?? "verified horizon direction"}", $"Best time: {FormatUtc(bestObject?.BestViewingTimeUtc ?? (evt?.BestTimeLocal is null ? null : bestDate.ToDateTime(evt.BestTimeLocal.Value)))}", $"Why it matters: compare relative positions over the week." },
            "Conjunction Explainer" => new[] { $"Closest-date event: {evt?.Title ?? assignment?.AssignedEventType ?? "conjunction"}", $"Date: {bestDate:MMM d}", $"Direction: {evt?.Direction ?? bestObject?.ViewingDirection ?? "verified horizon direction"}", $"Tip: use the brighter object as the anchor." },
            _ => new[] { $"Target: {bestObject?.ObjectName ?? evt?.Title ?? "weekly sky target"}", $"Visibility score: {(bestObject?.VisibilityScore ?? evt?.VisibilityScore ?? plan.AssetPriority / 100d):0.00}", $"Direction: {bestObject?.ViewingDirection ?? evt?.Direction ?? "verified horizon direction"}", $"Recommendation: {bestObject?.Reason ?? assignment?.VisibilitySummary ?? "observe during the best forecast window."}" }
        };
        yield return (Slug(type) + ".png", type, type, $"{bestDate:MMM d} • Priority {plan.AssetPriority}/100", lines, Color.LimeGreen, DataSources(plan));
    }

    private static async Task RenderCardAsync(string path, string title, string subtitle, IReadOnlyList<string> lines, Color accent, CancellationToken cancellationToken)
    {
        using var image = new Image<Rgba32>(Width, Height, new Rgba32(3, 8, 22));
        image.Mutate(ctx =>
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(Width, Height), GradientRepetitionMode.None, [new ColorStop(0, new Rgba32(6, 18, 44)), new ColorStop(1, new Rgba32(1, 4, 14))]));
            ctx.Fill(accent.WithAlpha(0.25f), new RectangleF(0, 0, Width, 18));
            ctx.Fill(Color.White.WithAlpha(0.06f), new RectangleF(80, 130, Width - 160, Height - 240));
            ctx.Draw(accent, 4, new RectangleF(80, 130, Width - 160, Height - 240));
            var titleFont = SystemFonts.CreateFont("Arial", 68, FontStyle.Bold);
            var subtitleFont = SystemFonts.CreateFont("Arial", 38, FontStyle.Regular);
            var bodyFont = SystemFonts.CreateFont("Arial", 34, FontStyle.Regular);
            ctx.DrawText(title, titleFont, Color.White, new PointF(110, 70));
            ctx.DrawText(subtitle, subtitleFont, accent, new PointF(112, 160));
            var y = 245f;
            foreach (var line in lines.Take(10))
            {
                foreach (var chunk in Wrap(line, 78))
                {
                    ctx.DrawText("• " + chunk, bodyFont, Color.WhiteSmoke, new PointF(130, y));
                    y += 56;
                }
                y += 8;
            }
            ctx.DrawText("Generated from weekly Skyfield data, segment plan, priority score, and source orchestration.", SystemFonts.CreateFont("Arial", 24), Color.LightGray, new PointF(110, 1010));
        });
        await image.SaveAsync(path, new PngEncoder(), cancellationToken);
    }

    private static IEnumerable<string> Wrap(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = "";
        foreach (var word in words)
        {
            if ((line.Length + word.Length + 1) > max && line.Length > 0) { yield return line; line = word; }
            else line = string.IsNullOrEmpty(line) ? word : line + " " + word;
        }
        if (!string.IsNullOrWhiteSpace(line)) yield return line;
    }

    private static IEnumerable<WeeklyAstronomyEvent> TopEvents(WeeklySkyForecastContext? context, WeeklySegmentAssignment? assignment)
    {
        var events = context?.DailyForecasts.SelectMany(d => d.Events.Select(e => ToEvent(e, d))).ToList() ?? [];
        if (assignment is not null)
        {
            events = events.Where(e => e.EventType.ToString().Equals(assignment.AssignedEventType, StringComparison.OrdinalIgnoreCase)
                || assignment.AssignedObjects.Any(o => e.Objects.Any(obj => obj.ObjectCode.Equals(o, StringComparison.OrdinalIgnoreCase)) || string.Equals(e.PrimaryObject, o, StringComparison.OrdinalIgnoreCase))
                || e.BestDateLocal == assignment.AssignedDateLocal).ToList();
        }
        return events.OrderByDescending(e => e.ImportanceScore + e.VisibilityScore + e.RarityScore).Take(8);
    }

    private static WeeklyAstronomyEvent ToEvent(WeeklySkyForecastEventItem item, DailySkyForecastContextItem day)
    {
        var matched = day.VisibleObjects.FirstOrDefault(o => !string.IsNullOrWhiteSpace(item.PrimaryObjectCode) && o.ObjectCode.Equals(item.PrimaryObjectCode, StringComparison.OrdinalIgnoreCase));
        return new WeeklyAstronomyEvent($"daily-{day.Date:yyyyMMdd}-{item.EventType}", Enum.TryParse<WeeklyAstronomyEventType>(item.EventType, true, out var t) ? t : WeeklyAstronomyEventType.HeroObject, item.Title, item.Description, matched is null ? [] : [new WeeklyAstronomyEventObject(matched.ObjectCode, matched.ObjectName, matched.MaxAltitudeDegrees, matched.BestViewingAzimuthDegrees, null, matched.VisibilityScore)], item.PrimaryObjectCode, matched is null ? 0 : 1, day.Date, TimeOnly.FromDateTime(item.EventTimeUtc), item.ViewingDirection, matched?.MaxAltitudeDegrees, matched?.BestViewingAzimuthDegrees, null, null, matched?.VisibilityScore ?? item.ImportanceScore, item.ImportanceScore, item.ViralityScore, "MotionGraphics", item.EventType, item.ViewingTip, []);
    }

    private static DailySkyForecastContextItem? Daily(WeeklySkyForecastContext? context, DateOnly date) => context?.DailyForecasts.FirstOrDefault(d => d.Date == date);
    private static WeeklySkyForecastVisibleObjectItem? BestObject(WeeklySkyForecastContext? context, WeeklySegmentAssignment? assignment) => context?.DailyForecasts.SelectMany(d => d.VisibleObjects).Where(o => o.Visible && (assignment is null || assignment.AssignedObjects.Count == 0 || assignment.AssignedObjects.Any(a => o.ObjectCode.Equals(a, StringComparison.OrdinalIgnoreCase) || o.ObjectName.Equals(a, StringComparison.OrdinalIgnoreCase)))).OrderByDescending(o => o.VisibilityScore).ThenByDescending(o => o.MaxAltitudeDegrees ?? 0).FirstOrDefault();
    private static string FormatUtc(DateTime? value) => value.HasValue ? value.Value.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture) : "Best forecast window";
    private static string FormatDegrees(double? value) => value.HasValue ? value.Value.ToString("0", CultureInfo.InvariantCulture) + "°" : "Skyfield value unavailable";
    private static string Name(SegmentVisualAssetPlan plan, string fileName) => plan.SegmentType switch { "BestTime" => "best-time-card.png", "CallToAction" => "call-to-action-card.png", _ => fileName };
    private static IReadOnlyList<string> DataSources(SegmentVisualAssetPlan plan) => ["Skyfield results", "Episode Architecture", $"Segment Classification:{plan.SegmentType}", $"Event Priority Engine:{plan.AssetPriority}", "Visual Source Orchestration"];
    private static string ResolveEducationalType(string value) => value.Contains("moon", StringComparison.OrdinalIgnoreCase) ? "Moon Phase Explainer" : value.Contains("group", StringComparison.OrdinalIgnoreCase) ? "Planet Grouping Explainer" : value.Contains("conjunction", StringComparison.OrdinalIgnoreCase) ? "Conjunction Explainer" : value.Contains("opposition", StringComparison.OrdinalIgnoreCase) ? "Opposition Explainer" : "Planet Visibility Explainer";
    private static string Slug(string value) => value.ToLowerInvariant().Replace(" ", "-");
}


public sealed class WeeklyAssetQualityValidator(ILogger logger)
{
    private const int StellariumMinimumWidth = 1280;
    private const int StellariumMinimumHeight = 720;
    private const int GeneralMinimumWidth = 1024;
    private const int GeneralMinimumHeight = 720;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };

    public async Task<WeeklyAssetQualityValidationResult> ValidateAndPersistAsync(
        string root,
        IReadOnlyList<RealizedVisualAsset> assets,
        IReadOnlyList<MotionGraphicManifestEntry> motionGraphics,
        IReadOnlyList<EducationalOverlayManifestEntry> educationalOverlays,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ASSET_QUALITY_VALIDATION_START root={Root} assetCount={AssetCount}", root, assets.Count);
        var motionByPath = motionGraphics.ToDictionary(x => x.AssetPath, StringComparer.OrdinalIgnoreCase);
        var overlayByPath = educationalOverlays.ToDictionary(x => x.AssetPath, StringComparer.OrdinalIgnoreCase);
        var expandedMetadata = LoadExpandedStellariumMetadata(root);
        var details = assets.Select(asset => Validate(asset, motionByPath.GetValueOrDefault(asset.FilePath), overlayByPath.GetValueOrDefault(asset.FilePath), expandedMetadata.GetValueOrDefault(asset.FilePath))).ToList();
        var report = BuildReport(details);

        var episodeDirectory = Path.Combine(root, "episode");
        Directory.CreateDirectory(episodeDirectory);
        var reportPath = Path.Combine(episodeDirectory, "weekly-asset-quality-report.json");
        var detailsPath = Path.Combine(episodeDirectory, "weekly-asset-quality-details.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(detailsPath, JsonSerializer.Serialize(details, JsonOptions), cancellationToken);
        logger.LogInformation("ASSET_QUALITY_REPORT_WRITTEN reportPath={ReportPath} detailsPath={DetailsPath} qualityGatePassed={QualityGatePassed}", reportPath, detailsPath, report.QualityGatePassed);
        logger.LogInformation("ASSET_QUALITY_VALIDATION_COMPLETE totalAssets={TotalAssets} productionReady={ProductionReady} productionWarning={ProductionWarning} productionFailed={ProductionFailed} qualityGatePassed={QualityGatePassed}", report.TotalAssets, report.ProductionReadyCount, report.ProductionWarningCount, report.ProductionFailedCount, report.QualityGatePassed);
        return new WeeklyAssetQualityValidationResult(report, details, reportPath, detailsPath);
    }

    private WeeklyAssetQualityDetail Validate(RealizedVisualAsset asset, MotionGraphicManifestEntry? motionEntry, EducationalOverlayManifestEntry? overlayEntry, ExpandedStellariumAssetMetadata? expandedMetadata)
    {
        logger.LogInformation("ASSET_QUALITY_CHECK assetId={AssetId} sourceType={SourceType} path={Path}", asset.AssetId, asset.SourceType, asset.FilePath);
        var passed = new List<string>();
        var failed = new List<string>();
        var reasons = new List<string>();
        var warnings = new List<string>();

        if (asset.Exists) passed.Add("File exists"); else Fail("File exists", "Asset file does not exist.");
        if (asset.Exists && asset.Width > 0 && asset.Height > 0) passed.Add("Readable image"); else Fail("Readable image", "Asset is missing, unreadable, or corrupt.");

        var minimumWidth = asset.SourceType is RealizedVisualAssetSourceType.StellariumBase or RealizedVisualAssetSourceType.StellariumExpanded ? StellariumMinimumWidth : GeneralMinimumWidth;
        var minimumHeight = asset.SourceType is RealizedVisualAssetSourceType.StellariumBase or RealizedVisualAssetSourceType.StellariumExpanded ? StellariumMinimumHeight : GeneralMinimumHeight;
        if (asset.Width >= minimumWidth && asset.Height >= minimumHeight) passed.Add($"Resolution >= {minimumWidth}x{minimumHeight}");
        else Fail($"Resolution >= {minimumWidth}x{minimumHeight}", $"Resolution {asset.Width}x{asset.Height} is below required {minimumWidth}x{minimumHeight}.");

        var metrics = asset.Exists ? ImageQualityMetrics.TryRead(asset.FilePath) : ImageQualityMetrics.Unreadable;
        if (metrics.Readable)
        {
            if (!metrics.IsBlank) passed.Add("Not blank"); else Fail("Not blank", "Image appears blank.");
            if (!metrics.IsMonochrome) passed.Add("Not monochrome"); else Fail("Not monochrome", "Image appears monochrome.");
            if (!metrics.IsFullyOverexposed) passed.Add("Not fully overexposed"); else Fail("Not fully overexposed", "Image is fully overexposed.");
        }
        else if (asset.Exists)
        {
            Fail("Readable image analysis", "Image could not be decoded for quality analysis.");
        }

        var profile = ResolveValidationProfile(asset);
        switch (asset.SourceType)
        {
            case RealizedVisualAssetSourceType.StellariumBase:
            case RealizedVisualAssetSourceType.StellariumExpanded:
                ValidateStellariumSemanticChecks(asset, profile, metrics, passed, failed, reasons, expandedMetadata);
                break;
            case RealizedVisualAssetSourceType.AICinematic:
                if (failed.Count == 0) passed.Add("AI cinematic still satisfies production still requirements");
                break;
            case RealizedVisualAssetSourceType.NASA:
                if (failed.Count == 0) passed.Add("Correct NASA object category");
                break;
            case RealizedVisualAssetSourceType.JWST:
                if (failed.Count == 0) passed.Add("Correct JWST object category");
                break;
            case RealizedVisualAssetSourceType.MotionGraphics:
                ValidateMotionGraphic(motionEntry, passed, failed, reasons);
                break;
            case RealizedVisualAssetSourceType.EducationalOverlay:
                ValidateEducationalOverlay(overlayEntry, passed, failed, reasons);
                break;
        }

        var status = failed.Count == 0 ? ProductionAssetQualityStatus.ProductionReady : ProductionAssetQualityStatus.ProductionFailed;
        var detail = new WeeklyAssetQualityDetail(asset.AssetId, asset.SourceType.ToString(), asset.AssetCode, asset.FilePath, status, passed, failed.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), warnings, asset.Width, asset.Height, asset.FileSizeBytes, profile);
        if (status == ProductionAssetQualityStatus.ProductionReady)
            logger.LogInformation("ASSET_QUALITY_PASSED assetId={AssetId} path={Path}", asset.AssetId, asset.FilePath);
        else if (status == ProductionAssetQualityStatus.ProductionWarning)
            logger.LogWarning("ASSET_QUALITY_WARNING assetId={AssetId} path={Path} reasons={Reasons}", asset.AssetId, asset.FilePath, string.Join(" | ", detail.Reasons));
        else
            logger.LogWarning("ASSET_QUALITY_FAILED assetId={AssetId} path={Path} reasons={Reasons}", asset.AssetId, asset.FilePath, string.Join(" | ", detail.Reasons));
        return detail;

        void Fail(string check, string reason)
        {
            failed.Add(check);
            reasons.Add(reason);
        }
    }

    private static void ValidateStellariumSemanticChecks(RealizedVisualAsset asset, string profile, ImageQualityMetrics metrics, List<string> passed, List<string> failed, List<string> reasons, ExpandedStellariumAssetMetadata? expandedMetadata)
    {
        if (asset.SourceType == RealizedVisualAssetSourceType.StellariumExpanded)
        {
            if (expandedMetadata?.SelectedSunAltitudeDeg is { } sunAltitude)
            {
                var required = profile.Equals("AstrophotographyTip", StringComparison.OrdinalIgnoreCase) ? -12d : -6d;
                if (sunAltitude <= required) passed.Add("Expanded night metadata valid");
                else Fail("Expanded night metadata valid", profile.Equals("AstrophotographyTip", StringComparison.OrdinalIgnoreCase) ? "DaylightOrTwilightExpandedScene" : "DaylightOrTwilightExpandedScene");
            }
            else
            {
                Fail("Expanded night metadata present", "DaylightOrTwilightExpandedScene");
            }

            if (metrics.DaylightSkyLikely) Fail("Expanded screenshot night sky", "DaylightSkyDetected");
        }

        if (profile.Equals("AstrophotographyTip", StringComparison.OrdinalIgnoreCase))
        {
            if (metrics.NightSkyLikely) passed.Add("Night sky required"); else Fail("Night sky required", "DaylightSkyDetected");
            if (metrics.TargetObjectLikely) passed.Add("Target object visible");
            else Fail("Target object visible", "Target not visible. Astrophotography objective not satisfied.");
            if (metrics.TargetObjectLikely && metrics.ObjectInsideSafeFrame) passed.Add("Object inside safe frame");
            else Fail("Object inside safe frame", "Target outside frame or not visible.");
            return;
        }

        if (profile.Equals("MoonHero", StringComparison.OrdinalIgnoreCase))
        {
            if (metrics.HasVisibleObject) passed.Add("Moon visible"); else Fail("Moon visible", "Moon not visible.");
            return;
        }

        if (profile.Equals("PlanetGrouping", StringComparison.OrdinalIgnoreCase))
        {
            if (metrics.HasVisibleObject) passed.Add("At least one target object visible"); else Fail("At least one target object visible", "No visible target.");
        }

        void Fail(string check, string reason)
        {
            failed.Add(check);
            reasons.Add(reason);
        }
    }

    private static void ValidateMotionGraphic(MotionGraphicManifestEntry? entry, List<string> passed, List<string> failed, List<string> reasons)
    {
        if (entry is null)
        {
            failed.Add("Segment data populated");
            reasons.Add("Motion graphic manifest entry missing.");
            return;
        }
        if (entry.ContentLines.Any(line => !string.IsNullOrWhiteSpace(line))) passed.Add("Text rendered"); else Fail("Text rendered", "Motion graphic text is empty.");
        if (!entry.ContentLines.Any(line => line.Contains("placeholder", StringComparison.OrdinalIgnoreCase))) passed.Add("Not placeholder"); else Fail("Not placeholder", "Motion graphic contains placeholder text.");
        if (!string.IsNullOrWhiteSpace(entry.SegmentId) && !string.IsNullOrWhiteSpace(entry.SegmentType) && entry.DurationSeconds > 0) passed.Add("Segment data populated"); else Fail("Segment data populated", "Motion graphic segment metadata is incomplete.");

        void Fail(string check, string reason)
        {
            failed.Add(check);
            reasons.Add(reason);
        }
    }

    private static void ValidateEducationalOverlay(EducationalOverlayManifestEntry? entry, List<string> passed, List<string> failed, List<string> reasons)
    {
        if (entry is null)
        {
            failed.Add("Readable");
            reasons.Add("Educational overlay manifest entry missing.");
            return;
        }
        if (!entry.ContentLines.Any(line => line.Contains("placeholder", StringComparison.OrdinalIgnoreCase))) passed.Add("Not placeholder"); else Fail("Not placeholder", "Educational overlay contains placeholder text.");
        if (entry.ContentLines.Any(line => !string.IsNullOrWhiteSpace(line)) && !string.IsNullOrWhiteSpace(entry.OverlayType)) passed.Add("Readable"); else Fail("Readable", "Educational overlay content is incomplete.");

        void Fail(string check, string reason)
        {
            failed.Add(check);
            reasons.Add(reason);
        }
    }

    private static WeeklyAssetQualityReport BuildReport(IReadOnlyList<WeeklyAssetQualityDetail> details)
    {
        var ready = details.Count(x => x.Status == ProductionAssetQualityStatus.ProductionReady);
        var warning = details.Count(x => x.Status == ProductionAssetQualityStatus.ProductionWarning);
        var failed = details.Count(x => x.Status == ProductionAssetQualityStatus.ProductionFailed);
        var requiredAiPassed = details.Count(IsRequiredAICinematicPassed);
        var requiredAiFailed = details.Count(IsRequiredAICinematicFailed);
        var requiredAiReady = requiredAiPassed >= RequiredAICinematicAssetCodes.Count && requiredAiFailed == 0;
        return new WeeklyAssetQualityReport(
            details.Count,
            ready,
            warning,
            failed,
            CountPassed(details, RealizedVisualAssetSourceType.StellariumBase),
            CountFailed(details, RealizedVisualAssetSourceType.StellariumBase),
            CountPassed(details, RealizedVisualAssetSourceType.StellariumExpanded),
            CountFailed(details, RealizedVisualAssetSourceType.StellariumExpanded),
            CountPassed(details, RealizedVisualAssetSourceType.AICinematic),
            requiredAiPassed,
            requiredAiFailed,
            requiredAiReady,
            CountPassed(details, RealizedVisualAssetSourceType.NASA),
            CountPassed(details, RealizedVisualAssetSourceType.JWST),
            CountPassed(details, RealizedVisualAssetSourceType.MotionGraphics),
            CountPassed(details, RealizedVisualAssetSourceType.EducationalOverlay),
            failed == 0 && requiredAiReady);
    }

    private static int CountPassed(IReadOnlyList<WeeklyAssetQualityDetail> details, RealizedVisualAssetSourceType sourceType)
        => details.Count(x => x.SourceType.Equals(sourceType.ToString(), StringComparison.OrdinalIgnoreCase) && x.Status == ProductionAssetQualityStatus.ProductionReady);

    private static bool IsRequiredAICinematicPassed(WeeklyAssetQualityDetail detail) =>
        IsRequiredAICinematic(detail) && detail.Status == ProductionAssetQualityStatus.ProductionReady;

    private static bool IsRequiredAICinematicFailed(WeeklyAssetQualityDetail detail) =>
        IsRequiredAICinematic(detail) && detail.Status == ProductionAssetQualityStatus.ProductionFailed;

    private static bool IsRequiredAICinematic(WeeklyAssetQualityDetail detail) =>
        detail.SourceType.Equals(RealizedVisualAssetSourceType.AICinematic.ToString(), StringComparison.OrdinalIgnoreCase)
        && RequiredAICinematicAssetCodes.Contains(detail.AssetCode);

    private static readonly HashSet<string> RequiredAICinematicAssetCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "fast_cinematic_sky_hook",
        "cinematic_weekly_sky_reveal",
        "cosmic_closing_background",
        "shortform_call_to_action_background",
        "cosmic_retention_reset"
    };

    private static int CountFailed(IReadOnlyList<WeeklyAssetQualityDetail> details, RealizedVisualAssetSourceType sourceType)
        => details.Count(x => x.SourceType.Equals(sourceType.ToString(), StringComparison.OrdinalIgnoreCase) && x.Status == ProductionAssetQualityStatus.ProductionFailed);

    private static string ResolveValidationProfile(RealizedVisualAsset asset)
    {
        var value = asset.AssetCode.ToLowerInvariant();
        if (value.Contains("moon")) return "MoonHero";
        if (value.Contains("planet") || value.Contains("western") || value.Contains("group")) return "PlanetGrouping";
        if (value.Contains("astro")) return "AstrophotographyTip";
        return asset.SourceType.ToString();
    }

    private sealed record ExpandedStellariumAssetMetadata(string SceneType, DateTime? SelectedObservationUtc, DateTime? SelectedObservationLocal, double? SelectedSunAltitudeDeg, string NightValidationStatus);

    private static IReadOnlyDictionary<string, ExpandedStellariumAssetMetadata> LoadExpandedStellariumMetadata(string root)
    {
        var path = Path.Combine(root, "episode", "weekly-expanded-stellarium-execution-report.json");
        if (!File.Exists(path)) return new Dictionary<string, ExpandedStellariumAssetMetadata>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!TryGetPropertyIgnoreCase(document.RootElement, "expandedScenes", out var scenes) || scenes.ValueKind != JsonValueKind.Array)
                return new Dictionary<string, ExpandedStellariumAssetMetadata>(StringComparer.OrdinalIgnoreCase);

            var map = new Dictionary<string, ExpandedStellariumAssetMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var scene in scenes.EnumerateArray())
            {
                var sceneType = TryGetPropertyIgnoreCase(scene, "sourceSegmentType", out var st) ? st.GetString() ?? string.Empty : string.Empty;
                var status = TryGetPropertyIgnoreCase(scene, "nightValidationStatus", out var ns) ? ns.GetString() ?? string.Empty : string.Empty;
                var utc = TryGetDateTime(scene, "selectedObservationUtc");
                var local = TryGetDateTime(scene, "selectedObservationLocal");
                var sun = TryGetPropertyIgnoreCase(scene, "selectedSunAltitudeDeg", out var sa) && sa.ValueKind == JsonValueKind.Number ? sa.GetDouble() : (double?)null;
                var metadata = new ExpandedStellariumAssetMetadata(sceneType, utc, local, sun, status);
                if (TryGetPropertyIgnoreCase(scene, "generatedScreenshots", out var screenshots) && screenshots.ValueKind == JsonValueKind.Array)
                {
                    foreach (var screenshot in screenshots.EnumerateArray())
                    {
                        var value = screenshot.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) map[value] = metadata;
                    }
                }
            }
            return map;
        }
        catch
        {
            return new Dictionary<string, ExpandedStellariumAssetMetadata>(StringComparer.OrdinalIgnoreCase);
        }

        static DateTime? TryGetDateTime(JsonElement element, string property)
            => TryGetPropertyIgnoreCase(element, property, out var value) && value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

        static bool TryGetPropertyIgnoreCase(JsonElement element, string property, out JsonElement value)
        {
            if (element.TryGetProperty(property, out value)) return true;
            foreach (var item in element.EnumerateObject())
            {
                if (item.Name.Equals(property, StringComparison.OrdinalIgnoreCase))
                {
                    value = item.Value;
                    return true;
                }
            }
            return false;
        }
    }

    private readonly record struct ImageQualityMetrics(bool Readable, bool IsBlank, bool IsMonochrome, bool IsFullyOverexposed, bool NightSkyLikely, bool HasVisibleObject, bool ObjectInsideSafeFrame, bool TargetObjectLikely, bool DaylightSkyLikely)
    {
        public static ImageQualityMetrics Unreadable => new(false, true, true, false, false, false, false, false, false);

        public static ImageQualityMetrics TryRead(string path)
        {
            try
            {
                using var image = Image.Load<Rgba32>(path);
                var xStep = Math.Max(1, image.Width / 160);
                var yStep = Math.Max(1, image.Height / 90);
                var count = 0;
                var sum = 0d;
                var sumSq = 0d;
                var chromaSum = 0d;
                var overexposed = 0;
                var bright = 0;
                var dark = 0;
                var safeBright = 0;
                for (var y = 0; y < image.Height; y += yStep)
                {
                    var row = image.DangerousGetPixelRowMemory(y).Span;
                    for (var x = 0; x < image.Width; x += xStep)
                    {
                        var pixel = row[x];
                        var luma = 0.2126d * pixel.R + 0.7152d * pixel.G + 0.0722d * pixel.B;
                        sum += luma;
                        sumSq += luma * luma;
                        chromaSum += Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                        if (luma > 245d) overexposed++;
                        if (luma > 120d) bright++;
                        if (luma < 80d) dark++;
                        if (luma > 120d && x >= image.Width * 0.10d && x <= image.Width * 0.90d && y >= image.Height * 0.10d && y <= image.Height * 0.90d) safeBright++;
                        count++;
                    }
                }

                if (count == 0) return Unreadable;
                var mean = sum / count;
                var variance = Math.Max(0d, (sumSq / count) - (mean * mean));
                var stdDev = Math.Sqrt(variance);
                var averageChroma = chromaSum / count;
                var overexposedRatio = (double)overexposed / count;
                var darkRatio = (double)dark / count;
                var brightRatio = (double)bright / count;
                var safeBrightRatio = (double)safeBright / count;
                var isBlank = stdDev < 2d;
                var isMonochrome = averageChroma < 1.25d && stdDev < 12d;
                var isFullyOverexposed = overexposedRatio > 0.95d;
                var nightSkyLikely = mean < 130d && darkRatio > 0.35d;
                var hasVisibleObject = brightRatio > 0.0005d && stdDev > 4d;
                var objectInsideSafeFrame = safeBrightRatio > 0.0002d;
                var targetObjectLikely = safeBrightRatio > 0.0015d;
                var daylightSkyLikely = mean > 145d || (averageChroma > 20d && brightRatio > 0.30d && darkRatio < 0.35d);
                return new ImageQualityMetrics(true, isBlank, isMonochrome, isFullyOverexposed, nightSkyLikely, hasVisibleObject, objectInsideSafeFrame, targetObjectLikely, daylightSkyLikely);
            }
            catch
            {
                return Unreadable;
            }
        }
    }
}

public sealed class WeeklyAssetRealizationService(
    WeeklyAssetRealizationPersister persister,
    WeeklyAssetRealizationValidator validator,
    INasaAssetRealizationService nasaAssetRealizationService,
    ILogger<WeeklyAssetRealizationService> logger)
{
    public async Task<WeeklyAssetRealizationResult> RealizeAndPersistAsync(WeeklyAssetRealizationInput input, CancellationToken cancellationToken)
    {
        logger.LogInformation("ASSET_REALIZATION_START pipelineRunId={PipelineRunId} root={Root}", input.PipelineRunId, input.RootPath);
        var motionOverlayAssets = await new MotionGraphicsAndEducationalOverlayRealizer(logger).RealizeAsync(input, cancellationToken);
        input = input with { AllProductionImageAssets = input.AllProductionImageAssets
            .Concat(motionOverlayAssets.MotionGraphicPaths)
            .Concat(motionOverlayAssets.EducationalOverlayPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() };
        var assets = RegisterAssets(input);
        var assetQuality = await new WeeklyAssetQualityValidator(logger).ValidateAndPersistAsync(input.RootPath, assets, motionOverlayAssets.MotionGraphics, motionOverlayAssets.EducationalOverlays, cancellationToken);
        assets = ApplyQualityStatuses(assets, assetQuality);
        var bundles = BuildSegmentBundles(input, assets);
        var manifest = BuildManifest(input, assets, bundles);
        var report = BuildCoverageReport(input, assets, bundles);
        var readiness = validator.BuildVideoReadinessReport(input, manifest, report);
        var paths = await persister.PersistAsync(input.RootPath, manifest, report, readiness, cancellationToken);

        var nasaAssets = await nasaAssetRealizationService.RealizeAsync(input.RootPath, input.WeeklyVisualAssetPlanPath, paths.ManifestPath, paths.RealizationReportPath, continueOnFailure: true, cancellationToken);
        var realizedNasaOrJwstPaths = nasaAssets.Report.NasaImagePaths.Concat(nasaAssets.Report.JwstImagePaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (realizedNasaOrJwstPaths.Count > 0)
        {
            var enrichedInput = input with { AllProductionImageAssets = input.AllProductionImageAssets.Concat(realizedNasaOrJwstPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
            assets = RegisterAssets(enrichedInput);
            assetQuality = await new WeeklyAssetQualityValidator(logger).ValidateAndPersistAsync(enrichedInput.RootPath, assets, motionOverlayAssets.MotionGraphics, motionOverlayAssets.EducationalOverlays, cancellationToken);
            assets = ApplyQualityStatuses(assets, assetQuality);
            bundles = BuildSegmentBundles(enrichedInput, assets);
            manifest = BuildManifest(enrichedInput, assets, bundles);
            report = BuildCoverageReport(enrichedInput, assets, bundles);
            readiness = validator.BuildVideoReadinessReport(enrichedInput, manifest, report);
            paths = await persister.PersistAsync(enrichedInput.RootPath, manifest, report, readiness, cancellationToken);
        }

        logger.LogInformation("ASSET_REALIZATION_COMPLETE pipelineRunId={PipelineRunId} testReady={TestReady} finalReady={FinalReady} segmentCount={SegmentCount} nasaGenerated={NasaGenerated} nasaProductionReady={NasaProductionReady}", input.PipelineRunId, readiness.TestVideoPipelineReady, readiness.FinalVideoPipelineReady, bundles.Count, nasaAssets.Report.GeneratedNASAAssetCount, nasaAssets.Report.ProductionReadyNASAAssetCount);
        return new WeeklyAssetRealizationResult(manifest, report, readiness, paths.ManifestPath, paths.RealizationReportPath, paths.VideoReadinessReportPath, readiness.TestVideoPipelineReady, nasaAssets.PlanPath, nasaAssets.ResultsPath, nasaAssets.ReportPath, nasaAssets.Report.PlannedNASAAssetCount, nasaAssets.Report.GeneratedNASAAssetCount, nasaAssets.Report.ProductionReadyNASAAssetCount, nasaAssets.Report.FailedNASAAssetCount, nasaAssets.Report.NasaImagePaths, nasaAssets.Report.NasaImagePaths.Count, nasaAssets.JwstPlanPath, nasaAssets.JwstResultsPath, nasaAssets.JwstReportPath, nasaAssets.Report.PlannedJWSTAssetCount, nasaAssets.Report.GeneratedJWSTAssetCount, nasaAssets.Report.ProductionReadyJWSTAssetCount, nasaAssets.Report.FailedJWSTAssetCount, nasaAssets.Report.JwstImagePaths, nasaAssets.Report.JwstImagePaths.Count, nasaAssets.Report.NasaProviderConfigured, motionOverlayAssets.MotionGraphicsManifestPath, motionOverlayAssets.MotionGraphics.Count, motionOverlayAssets.MotionGraphics.Count, motionOverlayAssets.MotionGraphics.Count(x => File.Exists(x.AssetPath) && ImageDimensionReader.Read(x.AssetPath).Width > 0), motionOverlayAssets.MotionGraphicPaths, motionOverlayAssets.EducationalOverlayManifestPath, motionOverlayAssets.EducationalOverlays.Count, motionOverlayAssets.EducationalOverlays.Count, motionOverlayAssets.EducationalOverlays.Count(x => File.Exists(x.AssetPath) && ImageDimensionReader.Read(x.AssetPath).Width > 0), motionOverlayAssets.EducationalOverlayPaths, assetQuality.ReportPath, assetQuality.DetailsPath, assetQuality.Report.TotalAssets, assetQuality.Report.ProductionReadyCount, assetQuality.Report.ProductionWarningCount, assetQuality.Report.ProductionFailedCount, assetQuality.Report.QualityGatePassed, assetQuality.FailedAssetPaths);
    }

    private static List<RealizedVisualAsset> ApplyQualityStatuses(IReadOnlyList<RealizedVisualAsset> assets, WeeklyAssetQualityValidationResult quality)
    {
        var byPath = quality.Details.ToDictionary(x => x.AssetPath, StringComparer.OrdinalIgnoreCase);
        return assets.Select(asset => byPath.TryGetValue(asset.FilePath, out var detail)
            ? asset with { ProductionReady = detail.Status == ProductionAssetQualityStatus.ProductionReady }
            : asset).ToList();
    }

    private static WeeklyProductionAssetManifest BuildManifest(WeeklyAssetRealizationInput input, IReadOnlyList<RealizedVisualAsset> assets, IReadOnlyList<SegmentProductionAssetBundle> bundles) => new(
        input.PipelineRunId,
        input.RegionId,
        input.Language,
        input.WeekStartDate,
        input.WeekEndDate,
        input.LongformPlan.TotalTargetDurationSeconds,
        input.ShortformPlan.TotalTargetDurationSeconds,
        assets.Count,
        Count(assets, RealizedVisualAssetSourceType.StellariumBase),
        Count(assets, RealizedVisualAssetSourceType.StellariumExpanded),
        Count(assets, RealizedVisualAssetSourceType.AICinematic),
        Count(assets, RealizedVisualAssetSourceType.NASA),
        Count(assets, RealizedVisualAssetSourceType.JWST),
        Count(assets, RealizedVisualAssetSourceType.MotionGraphics),
        Count(assets, RealizedVisualAssetSourceType.EducationalOverlay),
        bundles);

    private List<RealizedVisualAsset> RegisterAssets(WeeklyAssetRealizationInput input)
    {
        var registrations = new List<(string Path, RealizedVisualAssetSourceType Source)>();
        registrations.AddRange(input.FrameScreenshots.Select(path => (path, RealizedVisualAssetSourceType.StellariumBase)));
        registrations.AddRange(input.ExpandedFrameScreenshots.Select(path => (path, RealizedVisualAssetSourceType.StellariumExpanded)));
        registrations.AddRange(input.AICinematicImagePaths.Select(path => (path, RealizedVisualAssetSourceType.AICinematic)));

        var known = new HashSet<string>(registrations.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var path in input.AllProductionImageAssets.Where(path => !string.IsNullOrWhiteSpace(path) && known.Add(path)))
        {
            registrations.Add((path, InferSourceType(path)));
        }

        return registrations
            .Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateAsset(group.Key, group.First().Source))
            .ToList();
    }

    private const long MinimumProductionImageBytes = 50L * 1024L;
    private const int MinimumProductionImageWidth = 1024;
    private const int MinimumProductionImageHeight = 720;

    private RealizedVisualAsset CreateAsset(string path, RealizedVisualAssetSourceType sourceType)
    {
        var exists = File.Exists(path);
        var fileInfo = exists ? new FileInfo(path) : null;
        var (width, height) = exists ? ImageDimensionReader.Read(path) : (0, 0);
        var assetCode = Path.GetFileNameWithoutExtension(path);
        var productionReady = IsProductionReadyImage(sourceType, exists, fileInfo?.Length ?? 0, width, height);
        var asset = new RealizedVisualAsset(
            $"{sourceType}:{assetCode}",
            sourceType,
            assetCode,
            path,
            exists,
            fileInfo?.Length ?? 0,
            width,
            height,
            "ReusableProductionVisual",
            true,
            productionReady);
        logger.LogInformation("PRODUCTION_ASSET_REGISTERED assetId={AssetId} sourceType={SourceType} path={Path} exists={Exists} size={Size} width={Width} height={Height} productionReady={ProductionReady}", asset.AssetId, asset.SourceType, asset.FilePath, asset.Exists, asset.FileSizeBytes, asset.Width, asset.Height, asset.ProductionReady);
        if (asset.ProductionReady && asset.SourceType is RealizedVisualAssetSourceType.NASA or RealizedVisualAssetSourceType.JWST)
            logger.LogInformation("{Provider}_ASSET_REGISTERED_IN_PRODUCTION_MANIFEST path={Path}", asset.SourceType.ToString().ToUpperInvariant(), asset.FilePath);
        return asset;
    }

    private static bool IsProductionReadyImage(RealizedVisualAssetSourceType sourceType, bool exists, long fileSizeBytes, int width, int height)
    {
        if (!exists) return false;
        if (sourceType is RealizedVisualAssetSourceType.NASA or RealizedVisualAssetSourceType.JWST)
            return fileSizeBytes > MinimumProductionImageBytes && width >= MinimumProductionImageWidth && height >= MinimumProductionImageHeight;
        return fileSizeBytes > 0 && width > 0 && height > 0;
    }

    private List<SegmentProductionAssetBundle> BuildSegmentBundles(WeeklyAssetRealizationInput input, IReadOnlyList<RealizedVisualAsset> assets)
    {
        var bundles = new List<SegmentProductionAssetBundle>();
        var allSegments = input.LongformPlan.Segments.Select(s => (EpisodeType: WeeklyEpisodeType.LongFormWeeklyForecast.ToString(), Segment: s))
            .Concat(input.ShortformPlan.Segments.Select(s => (EpisodeType: WeeklyEpisodeType.ShortFormWeeklyHighlight.ToString(), Segment: s)));
        var narrationExists = File.Exists(input.StoryBeatsPath);
        var narrationTextExists = File.Exists(input.NarrationTextPath);
        var narrationWordCount = narrationTextExists ? CountWords(File.ReadAllText(input.NarrationTextPath)) : 0;

        foreach (var item in allSegments)
        {
            var (assigned, missing, finalReady, warnings) = AssignAssets(item.EpisodeType, item.Segment.SegmentType, assets);
            var filesReady = assigned.Count > 0 && assigned.All(x => x.Exists && x.ProductionReady);
            var testReady = filesReady && narrationExists;
            var finalSegmentReady = testReady && finalReady;
            if (assigned.Count > 0 && warnings.Any(x => x.Contains("fallback", StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogInformation("SEGMENT_ASSET_FALLBACK_ASSIGNED segmentId={SegmentId} segmentType={SegmentType} assets={Assets} warnings={Warnings}", item.Segment.SegmentId, item.Segment.SegmentType, string.Join(',', assigned.Select(x => x.AssetId)), string.Join(" | ", warnings));
            }
            var reason = testReady
                ? finalSegmentReady ? "Segment has final-ready visual and narration coverage." : "Segment is ready for test using available realized visual assets; final requirements remain open."
                : assigned.Count == 0 ? "No realized visual asset could be assigned." : "Assigned visual files or story beats are missing.";
            var bundle = new SegmentProductionAssetBundle(
                item.Segment.SegmentId,
                item.EpisodeType,
                item.Segment.SegmentType,
                item.Segment.TargetDurationSeconds,
                narrationExists ? "StoryBeatsAvailable" : "StoryBeatsMissing",
                input.NarrationTextPath,
                narrationWordCount,
                assigned,
                missing,
                testReady,
                reason,
                warnings,
                testReady,
                finalSegmentReady);
            logger.LogInformation("SEGMENT_ASSET_BUNDLE_CREATED segmentId={SegmentId} episodeType={EpisodeType} segmentType={SegmentType} assignedAssets={AssetCount} productionReadyForTest={TestReady} productionReadyForFinalVideo={FinalReady}", bundle.SegmentId, bundle.EpisodeType, bundle.SegmentType, bundle.AssignedVisualAssets.Count, bundle.ProductionReadyForTest, bundle.ProductionReadyForFinalVideo);
            bundles.Add(bundle);
        }

        return bundles;
    }

    private (IReadOnlyList<RealizedVisualAsset> Assigned, IReadOnlyList<string> Missing, bool FinalReady, IReadOnlyList<string> Warnings) AssignAssets(string episodeType, string segmentType, IReadOnlyList<RealizedVisualAsset> assets)
    {
        var warnings = new List<string>();
        var missing = new List<string>();
        var assigned = new List<RealizedVisualAsset>();
        var baseStellarium = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.StellariumBase).ToList();
        var expanded = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.StellariumExpanded).ToList();
        var ai = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.AICinematic).ToList();
        var nasa = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.NASA).ToList();
        var jwst = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.JWST).ToList();
        var motion = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.MotionGraphics).ToList();
        var educational = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.EducationalOverlay).ToList();
        var finalReady = true;

        void AddAsset(RealizedVisualAsset? asset, string role)
        {
            if (asset is not null && assigned.All(x => !x.FilePath.Equals(asset.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                assigned.Add(asset with { SegmentUsageRole = role, Reusable = true });
            }
        }

        void AddMatching(IEnumerable<RealizedVisualAsset> candidates, string role, params string[] needles)
        {
            foreach (var asset in candidates
                .Where(a => a.Exists && a.ProductionReady && needles.Any(n => (a.AssetCode + " " + a.FilePath + " " + a.AssetId).Contains(n, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(a => a.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                AddAsset(asset, role);
            }
        }

        void AddFirst(IEnumerable<RealizedVisualAsset> candidates, string role)
            => AddAsset(candidates.Where(a => a.Exists && a.ProductionReady).OrderByDescending(a => a.Width * a.Height).ThenBy(a => a.FilePath, StringComparer.OrdinalIgnoreCase).FirstOrDefault(), role);

        void RequireAssigned(string ideal, Func<RealizedVisualAsset, bool> predicate, string warning)
        {
            if (!assigned.Any(predicate))
            {
                finalReady = false;
                missing.Add(ideal);
                warnings.Add(warning);
            }
        }

        var western = baseStellarium.Where(a => (a.AssetCode + " " + a.FilePath).Contains("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase)).ToList();
        var moon = baseStellarium.Where(a => (a.AssetCode + " " + a.FilePath).Contains("moon_hero_scene", StringComparison.OrdinalIgnoreCase)).ToList();
        var astro = expanded.Where(a => (a.AssetCode + " " + a.FilePath).Contains("astrophotography_target_scene", StringComparison.OrdinalIgnoreCase)).ToList();
        var contextStellarium = western.Count > 0 ? western : baseStellarium.Where(a => !(a.AssetCode + " " + a.FilePath).Contains("03_hero_closeup", StringComparison.OrdinalIgnoreCase)).ToList();
        var nasaJwst = nasa.Concat(jwst).ToList();

        switch (segmentType)
        {
            case "OpeningHook":
                AddMatching(ai, "primary_cinematic_reveal", "cinematic_weekly_sky_reveal");
                AddMatching(ai, "fast_hook_support", "fast_cinematic_sky_hook");
                AddFirst(contextStellarium, "wide_stellarium_context");
                AddMatching(motion, "overview_motion_card", "weekly-overview-timeline", "visibility-calendar");
                if (!assigned.Any(a => (a.AssetCode + a.FilePath).Contains("cinematic_weekly_sky_reveal", StringComparison.OrdinalIgnoreCase))) AddMatching(ai, "fallback_fast_hook", "fast_cinematic_sky_hook");
                RequireAssigned("AICinematic:fast_cinematic_sky_hook", a => a.SourceType == RealizedVisualAssetSourceType.AICinematic, "OpeningHook requires an AI cinematic hook or reveal asset.");
                break;
            case "WeeklySkyOverview":
                AddMatching(motion, "timeline_motion_graphic", "weekly-overview-timeline");
                AddMatching(motion, "visibility_calendar_motion_graphic", "visibility-calendar");
                AddFirst(western, "western_planet_context");
                AddFirst(nasaJwst, "space_context_image");
                AddMatching(ai, "retention_reset_cinematic", "cosmic_retention_reset");
                RequireAssigned("MotionGraphics:weekly-overview-timeline", a => a.SourceType == RealizedVisualAssetSourceType.MotionGraphics, "WeeklySkyOverview requires overview motion graphics.");
                break;
            case "HeroEvent":
                AddMatching(western, "required_western_grouping", "01_horizon_context", "02_balanced_story_frame", "03_alignment_wide");
                AddMatching(motion, "hero_event_card", "hero-event-card");
                AddMatching(ai, "hero_cinematic_reveal", "cinematic_weekly_sky_reveal");
                AddFirst(nasaJwst, "supporting_astronomy_image");
                AddFirst(moon, "supporting_moon_visual");
                RequireAssigned("western_planet_grouping_scene", a => (a.AssetCode + a.FilePath).Contains("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase), "HeroEvent must not be assigned only moon_hero_scene; western grouping frames are required when available.");
                break;
            case "MoonHighlights":
                AddMatching(moon, "moon_scene_sequence", "01_establishing_wide", "02_balanced_story_frame", "03_hero_closeup");
                AddFirst(nasa.Where(a => (a.AssetCode + a.FilePath).Contains("moon", StringComparison.OrdinalIgnoreCase)), "nasa_moon_support");
                AddMatching(motion, "moon_where_to_look_card", "where-to-look-card", "moon-highlight-card");
                AddMatching(ai, "retention_reset_cinematic", "cosmic_retention_reset");
                RequireAssigned("moon_hero_scene", a => (a.AssetCode + a.FilePath).Contains("moon_hero_scene", StringComparison.OrdinalIgnoreCase), "MoonHighlights requires moon_hero_scene frames.");
                break;
            case "PlanetHighlights":
                AddMatching(western, "required_western_grouping", "01_horizon_context", "02_balanced_story_frame", "03_alignment_wide");
                AddFirst(nasaJwst, "planet_or_context_support");
                AddMatching(motion, "planet_direction_cards", "where-to-look-card", "planet-highlights-card");
                if (western.Count == 0) AddFirst(moon, "fallback_moon_visual_only_when_no_western_grouping");
                RequireAssigned("western_planet_grouping_scene", a => (a.AssetCode + a.FilePath).Contains("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase), "PlanetHighlights must use western_planet_grouping_scene unless those files are unavailable.");
                break;
            case "BestObservationWindow":
                AddMatching(motion, "best_observation_cards", "best-observation-window-card", "best-time-card", "where-to-look-card");
                AddFirst(contextStellarium, "horizon_context_frame");
                AddFirst(educational, "educational_overlay");
                RequireAssigned("MotionGraphics:best-observation-window-card-or-best-time-card", a => a.SourceType == RealizedVisualAssetSourceType.MotionGraphics && (a.AssetCode + a.FilePath).Contains("best", StringComparison.OrdinalIgnoreCase), "BestObservationWindow requires best observation or best time motion cards.");
                break;
            case "AstrophotographyTip":
                AddMatching(astro, "required_expanded_astrophotography_scene", "01_balanced_story_frame", "astrophotography_target_scene");
                AddFirst(educational, "educational_overlay");
                AddMatching(ai, "retention_reset_cinematic", "cosmic_retention_reset");
                AddMatching(motion, "where_to_look_card", "where-to-look-card");
                RequireAssigned("ExpandedStellarium:astrophotography_target_scene", a => a.SourceType == RealizedVisualAssetSourceType.StellariumExpanded, "AstrophotographyTip requires ExpandedStellarium astrophotography target frame.");
                break;
            case "WeeklySummary":
                AddMatching(ai, "closing_cinematic", "cosmic_closing_background");
                AddMatching(motion, "weekly_summary_card", "weekly-summary-card");
                AddMatching(ai, "cta_background_support", "shortform_call_to_action_background");
                AddFirst(contextStellarium, "wide_stellarium_recap");
                if (!assigned.Any(a => a.SourceType == RealizedVisualAssetSourceType.AICinematic)) AddMatching(ai, "fallback_fast_hook", "fast_cinematic_sky_hook");
                RequireAssigned("AICinematic:cosmic_closing_background-or-MotionGraphics:weekly-summary-card", a => (a.AssetCode + a.FilePath).Contains("cosmic_closing_background", StringComparison.OrdinalIgnoreCase) || (a.AssetCode + a.FilePath).Contains("weekly-summary-card", StringComparison.OrdinalIgnoreCase), "WeeklySummary requires closing cinematic or summary card; fast hook is fallback only.");
                break;
            case "ShortHook":
                AddMatching(ai, "required_fast_hook", "fast_cinematic_sky_hook");
                AddFirst(western, "western_grouping_support");
                RequireAssigned("AICinematic:fast_cinematic_sky_hook", a => (a.AssetCode + a.FilePath).Contains("fast_cinematic_sky_hook", StringComparison.OrdinalIgnoreCase), "ShortHook requires fast_cinematic_sky_hook.");
                break;
            case "StrongestEvent":
                AddMatching(western, "required_western_grouping", "01_horizon_context", "02_balanced_story_frame", "03_alignment_wide");
                AddMatching(motion, "hero_event_card", "hero-event-card");
                AddMatching(ai, "hero_cinematic_reveal", "cinematic_weekly_sky_reveal");
                RequireAssigned("western_planet_grouping_scene", a => (a.AssetCode + a.FilePath).Contains("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase), "StrongestEvent requires western grouping frames.");
                break;
            case "WhereToLook":
                AddMatching(western, "balanced_where_to_look_frame", "02_balanced_story_frame");
                AddMatching(motion, "where_to_look_card", "where-to-look-card");
                RequireAssigned("western_planet_grouping_scene/02_balanced_story_frame", a => (a.AssetCode + a.FilePath).Contains("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase), "WhereToLook requires western grouping direction frame.");
                break;
            case "BestTime":
                AddMatching(motion, "best_time_cards", "best-observation-window-card", "best-time-card");
                AddFirst(educational, "educational_overlay");
                RequireAssigned("MotionGraphics:best-observation-window-card-or-best-time-card", a => a.SourceType == RealizedVisualAssetSourceType.MotionGraphics && (a.AssetCode + a.FilePath).Contains("best", StringComparison.OrdinalIgnoreCase), "BestTime requires best observation or best time card.");
                break;
            case "CallToAction":
                AddMatching(ai, "cta_background", "shortform_call_to_action_background");
                AddMatching(motion, "cta_card", "call-to-action-card");
                RequireAssigned("AICinematic:shortform_call_to_action_background-or-MotionGraphics:call-to-action-card", a => (a.AssetCode + a.FilePath).Contains("shortform_call_to_action_background", StringComparison.OrdinalIgnoreCase) || (a.AssetCode + a.FilePath).Contains("call-to-action-card", StringComparison.OrdinalIgnoreCase), "CallToAction requires CTA background or CTA card.");
                break;
            default:
                AddFirst(assets, "generic_visual");
                break;
        }

        if (assigned.Count == 0) finalReady = false;
        logger.LogInformation("SEGMENT_ASSET_COVERAGE_CALCULATED episodeType={EpisodeType} segmentType={SegmentType} assignedAssets={AssignedAssets} missing={Missing} finalReady={FinalReady}", episodeType, segmentType, assigned.Count, string.Join(',', missing.Distinct(StringComparer.OrdinalIgnoreCase)), finalReady);
        return (assigned, missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), finalReady && missing.Count == 0, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private WeeklyAssetCoverageAuditReport BuildCoverageReport(WeeklyAssetRealizationInput input, IReadOnlyList<RealizedVisualAsset> assets, IReadOnlyList<SegmentProductionAssetBundle> bundles)
    {
        var plannedBySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["MotionGraphics"] = input.VisualAssetPlan.PlannedMotionGraphicsCount,
            ["EducationalOverlay"] = input.VisualAssetPlan.PlannedEducationalOverlayCount,
            ["AICinematic"] = input.VisualAssetPlan.PlannedAICinematicCount,
            ["NASA"] = input.VisualAssetPlan.PlannedNASAAssetCount,
            ["JWST"] = input.VisualAssetPlan.PlannedJWSTAssetCount
        };
        var realizedBySource = Enum.GetValues<RealizedVisualAssetSourceType>()
            .ToDictionary(x => x.ToString(), x => Count(assets, x), StringComparer.OrdinalIgnoreCase);
        realizedBySource["Stellarium"] = Count(assets, RealizedVisualAssetSourceType.StellariumBase) + Count(assets, RealizedVisualAssetSourceType.StellariumExpanded);
        var missingBySource = plannedBySource.ToDictionary(x => x.Key, x => Math.Max(0, x.Value - realizedBySource.GetValueOrDefault(x.Key)), StringComparer.OrdinalIgnoreCase);
        var planned = Math.Max(input.VisualAssetPlan.PlannedVisualAssetCount, assets.Count + missingBySource.Values.Sum());
        var readyAssets = assets.Count(x => x.ProductionReady);
        var segmentCoverage = bundles.Select(bundle => new SegmentAssetCoverageResult(
            bundle.SegmentId,
            bundle.EpisodeType,
            bundle.SegmentType,
            bundle.AssignedVisualAssets.Count,
            bundle.AssignedVisualAssets.Select(x => x.SourceType.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            bundle.MissingVisualAssetTypes,
            bundle.Warnings.Any(w => w.Contains("fallback", StringComparison.OrdinalIgnoreCase)),
            bundle.ProductionReadyForTest,
            bundle.ProductionReadyForFinalVideo,
            bundle.Warnings)).ToList();
        var blockers = bundles.Where(x => !x.ProductionReadyForTest).Select(x => $"{x.SegmentId} has no test-ready visual/narration coverage.").ToList();
        var warnings = bundles.SelectMany(x => x.Warnings)
            .Concat(missingBySource.Where(x => x.Value > 0).Select(x =>
                x.Key.Equals("AICinematic", StringComparison.OrdinalIgnoreCase)
                    ? $"AICinematic has {x.Value} required assets not realized."
                    : $"{x.Key} has {x.Value} planned assets not realized."))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new WeeklyAssetCoverageAuditReport(
            input.PipelineRunId,
            DateTime.UtcNow,
            planned,
            assets.Count,
            readyAssets,
            Math.Max(0, planned - assets.Count),
            realizedBySource,
            missingBySource,
            segmentCoverage,
            planned <= 0 ? 100 : Math.Round((double)assets.Count / planned * 100, 2),
            blockers,
            warnings);
    }

    private static RealizedVisualAsset? SelectBest(IEnumerable<RealizedVisualAsset> candidates, string segmentType)
    {
        return candidates
            .Where(x => x.ProductionReady)
            .OrderByDescending(x => ScoreAssetForSegment(x, segmentType))
            .ThenByDescending(x => x.Width * x.Height)
            .FirstOrDefault();
    }

    private static int ScoreAssetForSegment(RealizedVisualAsset asset, string segmentType)
    {
        var code = asset.AssetCode;
        if (segmentType == "AstrophotographyTip" && code.Contains("astro", StringComparison.OrdinalIgnoreCase)) return 100;
        if ((segmentType == "BestObservationWindow" || segmentType == "WhereToLook") && (code.Contains("where", StringComparison.OrdinalIgnoreCase) || code.Contains("guidance", StringComparison.OrdinalIgnoreCase) || code.Contains("wide", StringComparison.OrdinalIgnoreCase))) return 95;
        if ((segmentType == "MoonHighlights" || segmentType == "OpeningHook") && code.Contains("moon", StringComparison.OrdinalIgnoreCase)) return 90;
        if (segmentType == "PlanetHighlights" && (code.Contains("planet", StringComparison.OrdinalIgnoreCase) || code.Contains("western", StringComparison.OrdinalIgnoreCase))) return 90;
        if ((segmentType == "HeroEvent" || segmentType == "StrongestEvent") && code.Contains("hero", StringComparison.OrdinalIgnoreCase)) return 90;
        return asset.Width;
    }

    private static RealizedVisualAssetSourceType InferSourceType(string path)
    {
        var value = path.ToLowerInvariant();
        if (value.Contains("jwst")) return RealizedVisualAssetSourceType.JWST;
        if (value.Contains("nasa")) return RealizedVisualAssetSourceType.NASA;
        if (value.Contains("motion")) return RealizedVisualAssetSourceType.MotionGraphics;
        if (value.Contains("overlay") || value.Contains("educational")) return RealizedVisualAssetSourceType.EducationalOverlay;
        if (value.Contains("ai") || value.Contains("cinematic")) return RealizedVisualAssetSourceType.AICinematic;
        return RealizedVisualAssetSourceType.StellariumBase;
    }

    private static int Count(IReadOnlyList<RealizedVisualAsset> assets, RealizedVisualAssetSourceType sourceType) => assets.Count(x => x.SourceType == sourceType);
    private static int CountWords(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

public sealed class WeeklyAssetRealizationPersister(ILogger<WeeklyAssetRealizationPersister> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<(string ManifestPath, string RealizationReportPath, string VideoReadinessReportPath)> PersistAsync(
        string root,
        WeeklyProductionAssetManifest manifest,
        WeeklyAssetCoverageAuditReport realizationReport,
        WeeklyVideoReadinessReport readinessReport,
        CancellationToken cancellationToken)
    {
        var episodeDirectory = Path.Combine(root, "episode");
        Directory.CreateDirectory(episodeDirectory);
        var manifestPath = Path.Combine(episodeDirectory, "weekly-production-asset-manifest.json");
        var realizationReportPath = Path.Combine(episodeDirectory, "weekly-asset-realization-report.json");
        var readinessReportPath = Path.Combine(episodeDirectory, "weekly-video-readiness-report.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(realizationReportPath, JsonSerializer.Serialize(realizationReport, JsonOptions), cancellationToken);
        logger.LogInformation("ASSET_REALIZATION_REPORT_WRITTEN manifestPath={ManifestPath} realizationReportPath={RealizationReportPath}", manifestPath, realizationReportPath);
        await File.WriteAllTextAsync(readinessReportPath, JsonSerializer.Serialize(readinessReport, JsonOptions), cancellationToken);
        logger.LogInformation("VIDEO_READINESS_REPORT_WRITTEN path={Path}", readinessReportPath);
        return (manifestPath, realizationReportPath, readinessReportPath);
    }
}

public sealed class WeeklyAssetRealizationValidator
{
    public WeeklyVideoReadinessReport BuildVideoReadinessReport(WeeklyAssetRealizationInput input, WeeklyProductionAssetManifest manifest, WeeklyAssetCoverageAuditReport report)
    {
        var longform = manifest.SegmentBundles.Where(x => x.EpisodeType == WeeklyEpisodeType.LongFormWeeklyForecast.ToString()).ToList();
        var shortform = manifest.SegmentBundles.Where(x => x.EpisodeType == WeeklyEpisodeType.ShortFormWeeklyHighlight.ToString()).ToList();
        var expectedSegmentCount = input.LongformPlan.Segments.Count + input.ShortformPlan.Segments.Count;
        var storyBeatsExist = File.Exists(input.StoryBeatsPath);
        var allAssignedFilesExist = manifest.SegmentBundles.SelectMany(x => x.AssignedVisualAssets).All(x => x.Exists && x.ProductionReady);
        var allSegmentsHaveVisuals = manifest.SegmentBundles.Count == expectedSegmentCount && manifest.SegmentBundles.All(x => x.AssignedVisualAssets.Count > 0);
        var testReady = allSegmentsHaveVisuals && storyBeatsExist && allAssignedFilesExist;
        var longformTestReady = longform.Count == input.LongformPlan.Segments.Count && longform.All(x => x.ProductionReadyForTest);
        var shortformTestReady = shortform.Count == input.ShortformPlan.Segments.Count && shortform.All(x => x.ProductionReadyForTest);
        var longformFinalReady = longform.Count > 0 && longform.All(x => x.ProductionReadyForFinalVideo);
        var shortformFinalReady = shortform.Count > 0 && shortform.All(x => x.ProductionReadyForFinalVideo);
        var missingAssets = report.MissingBySource.Where(x => x.Value > 0).Select(x => x.Key).Concat(manifest.SegmentBundles.SelectMany(x => x.MissingVisualAssetTypes)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var missingNarration = new List<string>();
        if (!storyBeatsExist) missingNarration.Add("weekly-story-beats");
        if (!File.Exists(input.NarrationTextPath)) missingNarration.Add("weekly-narration-text");
        var notReady = manifest.SegmentBundles
            .Where(x => !x.ProductionReadyForTest || !x.ProductionReadyForFinalVideo)
            .Select(x => $"{x.SegmentId}:{x.SegmentType}:test={x.ProductionReadyForTest}:final={x.ProductionReadyForFinalVideo}:missing={string.Join('|', x.MissingVisualAssetTypes)}")
            .ToList();
        var finalReady = longformFinalReady && shortformFinalReady && missingAssets.Count == 0 && missingNarration.Count == 0;
        var next = new List<string>();
        if (missingAssets.Contains("MotionGraphics", StringComparer.OrdinalIgnoreCase)) next.Add("Realize planned MotionGraphics assets for overview, best-time, where-to-look, summary, and CTA segments.");
        if (missingAssets.Contains("EducationalOverlay", StringComparer.OrdinalIgnoreCase)) next.Add("Generate educational overlay cards for the astrophotography and checklist segments.");
        if (missingAssets.Contains("NASA", StringComparer.OrdinalIgnoreCase) || missingAssets.Contains("JWST", StringComparer.OrdinalIgnoreCase)) next.Add("Resolve NASA/JWST context imagery where planned by the visual asset plan.");
        if (missingNarration.Count > 0) next.Add("Generate final narration artifacts before final video rendering.");
        if (next.Count == 0 && !finalReady) next.Add("Remove fallback visual assignments by realizing each segment's preferred source-specific assets.");
        return new WeeklyVideoReadinessReport(
            input.PipelineRunId,
            DateTime.UtcNow,
            testReady,
            finalReady,
            longformTestReady,
            shortformTestReady,
            longformFinalReady,
            shortformFinalReady,
            manifest.SegmentBundles.Count(x => x.ProductionReadyForTest),
            manifest.SegmentBundles.Count(x => x.ProductionReadyForFinalVideo),
            notReady,
            missingAssets,
            missingNarration,
            next.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
}

public static class ImageDimensionReader
{
    public static (int Width, int Height) Read(string path)
    {
        var dimensions = ReadWithImageSharp(path);
        if (dimensions.Width > 0 && dimensions.Height > 0) return dimensions;

        return ReadFromHeaders(path);
    }

    private static (int Width, int Height) ReadWithImageSharp(string path)
    {
        try
        {
            var info = Image.Identify(path);
            return info is null ? (0, 0) : (info.Width, info.Height);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static (int Width, int Height) ReadFromHeaders(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24];
            if (stream.Read(header) < 24) return (0, 0);
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                return (BinaryPrimitives.ReadInt32BigEndian(header[16..20]), BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
            }
            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                return ReadJpegDimensions(stream);
            }
        }
        catch
        {
            return (0, 0);
        }
        return (0, 0);
    }

    private static (int Width, int Height) ReadJpegDimensions(Stream stream)
    {
        stream.Position = 2;
        while (stream.Position < stream.Length)
        {
            if (stream.ReadByte() != 0xFF) continue;
            var marker = stream.ReadByte();
            if (marker < 0) break;
            var length = ReadBigEndianUInt16(stream);
            if (length < 2) break;
            if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
            {
                stream.ReadByte();
                var height = ReadBigEndianUInt16(stream);
                var width = ReadBigEndianUInt16(stream);
                return (width, height);
            }
            stream.Seek(length - 2, SeekOrigin.Current);
        }
        return (0, 0);
    }

    private static int ReadBigEndianUInt16(Stream stream)
    {
        var hi = stream.ReadByte();
        var lo = stream.ReadByte();
        return hi < 0 || lo < 0 ? 0 : (hi << 8) + lo;
    }
}
