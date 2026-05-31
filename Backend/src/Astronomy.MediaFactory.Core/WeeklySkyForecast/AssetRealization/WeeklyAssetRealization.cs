using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
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
    IReadOnlyList<string>? EducationalOverlayPaths = null);

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
            bundles = BuildSegmentBundles(enrichedInput, assets);
            manifest = BuildManifest(enrichedInput, assets, bundles);
            report = BuildCoverageReport(enrichedInput, assets, bundles);
            readiness = validator.BuildVideoReadinessReport(enrichedInput, manifest, report);
            paths = await persister.PersistAsync(enrichedInput.RootPath, manifest, report, readiness, cancellationToken);
        }

        logger.LogInformation("ASSET_REALIZATION_COMPLETE pipelineRunId={PipelineRunId} testReady={TestReady} finalReady={FinalReady} segmentCount={SegmentCount} nasaGenerated={NasaGenerated} nasaProductionReady={NasaProductionReady}", input.PipelineRunId, readiness.TestVideoPipelineReady, readiness.FinalVideoPipelineReady, bundles.Count, nasaAssets.Report.GeneratedNASAAssetCount, nasaAssets.Report.ProductionReadyNASAAssetCount);
        return new WeeklyAssetRealizationResult(manifest, report, readiness, paths.ManifestPath, paths.RealizationReportPath, paths.VideoReadinessReportPath, readiness.TestVideoPipelineReady, nasaAssets.PlanPath, nasaAssets.ResultsPath, nasaAssets.ReportPath, nasaAssets.Report.PlannedNASAAssetCount, nasaAssets.Report.GeneratedNASAAssetCount, nasaAssets.Report.ProductionReadyNASAAssetCount, nasaAssets.Report.FailedNASAAssetCount, nasaAssets.Report.NasaImagePaths, nasaAssets.Report.NasaImagePaths.Count, nasaAssets.JwstPlanPath, nasaAssets.JwstResultsPath, nasaAssets.JwstReportPath, nasaAssets.Report.PlannedJWSTAssetCount, nasaAssets.Report.GeneratedJWSTAssetCount, nasaAssets.Report.ProductionReadyJWSTAssetCount, nasaAssets.Report.FailedJWSTAssetCount, nasaAssets.Report.JwstImagePaths, nasaAssets.Report.JwstImagePaths.Count, nasaAssets.Report.NasaProviderConfigured, motionOverlayAssets.MotionGraphicsManifestPath, motionOverlayAssets.MotionGraphics.Count, motionOverlayAssets.MotionGraphics.Count, motionOverlayAssets.MotionGraphics.Count(x => File.Exists(x.AssetPath) && ImageDimensionReader.Read(x.AssetPath).Width > 0), motionOverlayAssets.MotionGraphicPaths, motionOverlayAssets.EducationalOverlayManifestPath, motionOverlayAssets.EducationalOverlays.Count, motionOverlayAssets.EducationalOverlays.Count, motionOverlayAssets.EducationalOverlays.Count(x => File.Exists(x.AssetPath) && ImageDimensionReader.Read(x.AssetPath).Width > 0), motionOverlayAssets.EducationalOverlayPaths);
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
        var stellarium = baseStellarium.Concat(expanded).ToList();
        var ai = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.AICinematic).ToList();
        var motion = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.MotionGraphics).ToList();
        var educational = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.EducationalOverlay).ToList();
        var finalReady = true;

        void Use(IEnumerable<RealizedVisualAsset> candidates, string role)
        {
            var asset = SelectBest(candidates, segmentType);
            if (asset is not null && assigned.All(x => !x.AssetId.Equals(asset.AssetId, StringComparison.OrdinalIgnoreCase)))
            {
                assigned.Add(asset with { SegmentUsageRole = role, Reusable = true });
            }
        }

        void Fallback(IEnumerable<RealizedVisualAsset> candidates, string ideal, string reason)
        {
            finalReady = false;
            missing.Add(ideal);
            Use(candidates, "fallback");
            warnings.Add(reason);
        }

        switch (segmentType)
        {
            case "OpeningHook":
                if (ai.Count > 0) Use(ai, "preferred_cinematic_hook");
                else if (stellarium.Count > 0) Fallback(stellarium, "AICinematic", "AICinematic opening hook missing; assigned Stellarium fallback for test readiness.");
                else missing.Add("AICinematicOrStellarium");
                break;
            case "WeeklySkyOverview":
                if (motion.Count > 0) Use(motion, "preferred_motion_overview");
                else Fallback(stellarium.Concat(ai), "MotionGraphics", "WeeklySkyOverview missing MotionGraphics; assigned widest Stellarium/AI fallback for test readiness.");
                break;
            case "HeroEvent":
            case "MoonHighlights":
            case "PlanetHighlights":
            case "StrongestEvent":
                Use(stellarium, "required_stellarium_visual");
                if (assigned.Count == 0) missing.Add("StellariumBaseOrStellariumExpanded");
                break;
            case "BestObservationWindow":
                if (motion.Count > 0) Use(motion, "preferred_motion_window");
                else Fallback(expanded.Concat(stellarium).Concat(ai), "MotionGraphics", "BestObservationWindow missing motion graphic; assigned expanded/Stellarium fallback for test readiness.");
                break;
            case "AstrophotographyTip":
                Use(expanded.Concat(educational).Concat(ai), "astrophotography_visual");
                if (assigned.Count == 0) missing.Add("StellariumExpandedOrEducationalOverlayOrAICinematic");
                if (educational.Count == 0)
                {
                    finalReady = false;
                    missing.Add("EducationalOverlay");
                    warnings.Add("EducationalOverlay is not realized; expanded/AI asset can satisfy test coverage only.");
                }
                break;
            case "WeeklySummary":
                if (ai.Count > 0) Use(ai, "closing_cinematic_or_montage");
                else Fallback(stellarium, "AICinematicOrMontage", "WeeklySummary missing recap montage/AI; assigned Stellarium fallback for test readiness.");
                if (motion.Count == 0)
                {
                    finalReady = false;
                    missing.Add("MotionGraphics");
                    warnings.Add("WeeklySummary recap motion graphics are not realized for final video readiness.");
                }
                break;
            case "ShortHook":
                if (ai.Count > 0) Use(ai, "short_hook_cinematic");
                else if (stellarium.Count > 0) Use(stellarium, "short_hook_stellarium");
                else missing.Add("AICinematicOrStellarium");
                break;
            case "WhereToLook":
                if (stellarium.Count > 0) Use(stellarium, "where_to_look_visual");
                else if (motion.Count > 0) Use(motion, "where_to_look_motion_graphic");
                else missing.Add("StellariumOrMotionGraphics");
                if (motion.Count == 0)
                {
                    finalReady = false;
                    missing.Add("MotionGraphics");
                    warnings.Add("WhereToLook direction motion graphic is not realized for final video readiness.");
                }
                break;
            case "BestTime":
                if (motion.Count > 0) Use(motion, "best_time_card");
                else Fallback(stellarium.Concat(ai), "MotionGraphics", "BestTime missing time-card motion graphic; assigned available visual fallback for test readiness.");
                break;
            case "CallToAction":
                if (ai.Count > 0) Use(ai, "cta_cinematic_background");
                else Fallback(stellarium, "AICinematicOrGenericClosingVisual", "CallToAction missing AI/generic closing visual; assigned Stellarium fallback for test readiness.");
                if (motion.Count == 0)
                {
                    finalReady = false;
                    missing.Add("MotionGraphics");
                }
                break;
            default:
                Use(assets, "generic_visual");
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
            .Concat(missingBySource.Where(x => x.Value > 0).Select(x => $"{x.Key} has {x.Value} planned assets not realized."))
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
