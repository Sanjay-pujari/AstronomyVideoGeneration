using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklyAstronomyEventExtractor : IWeeklyAstronomyEventExtractor
{
    public WeeklyAstronomyEventExtractionResult Extract(WeeklySkyForecastContext context, string region, DateOnly weekStartDate, DateOnly weekEndDate, string language, string? workingDirectoryRoot)
    {
        var warnings = new List<string>();
        var missing = new List<string>();
        var events = new List<WeeklyAstronomyEvent>();
        var visible = context.DailyForecasts.SelectMany(d => d.VisibleObjects.Where(v => v.Visible)).ToList();
        if (!visible.Any()) missing.Add("No visible objects in weekly forecast.");

        var heroCodes = new HashSet<string>(["MOON","VENUS","JUPITER","SATURN","MARS"], StringComparer.OrdinalIgnoreCase);
        foreach (var obj in visible.Where(v => heroCodes.Contains(v.ObjectCode)).GroupBy(v => v.ObjectCode, StringComparer.OrdinalIgnoreCase))
        {
            var best = obj.OrderByDescending(o => o.VisibilityScore).ThenByDescending(o => o.MaxAltitudeDegrees ?? 0).First();
            events.Add(BuildEvent(logger: null, WeeklyAstronomyEventType.HeroObject, $"{best.ObjectName} this week", $"{best.ObjectName} is a strong standalone viewing target this week.", [best], best.ObjectCode, best.BestViewingTimeUtc, best.ViewingDirection, null, "StellariumScene", "HeroObjectCloseup", "Object-focused narration angle"));
        }

        foreach (var day in context.DailyForecasts)
        {
            var dayVisible = day.VisibleObjects.Where(v => v.Visible).ToList();
            var grouping = dayVisible.Where(v => v.ObjectCode is "MOON" or "JUPITER" or "VENUS" or "SATURN" or "MARS").OrderByDescending(v => v.VisibilityScore).Take(3).ToList();
            if (grouping.Count >= 2)
            {
                events.Add(BuildEvent(null, grouping.Count >= 3 ? WeeklyAstronomyEventType.Grouping : WeeklyAstronomyEventType.Conjunction,
                    string.Join(" + ", grouping.Select(g => g.ObjectName)),
                    "Multiple bright objects are visible in the same evening window.",
                    grouping, grouping.First().ObjectCode, grouping.First().BestViewingTimeUtc, grouping.First().ViewingDirection, 12, "StellariumScene", grouping.Count >=3 ? "MultiObjectSkyGrouping" : "ConjunctionScene", "Comparative visual narration"));
            }
        }

        var bestNight = context.RecommendedNights.OrderByDescending(x => x.Score).FirstOrDefault();
        if (bestNight is not null)
        {
            var bestDate = bestNight.Date;
            var dayObjects = context.DailyForecasts.FirstOrDefault(d => d.Date == bestDate)?.VisibleObjects.Where(v => v.Visible).Take(5).ToList() ?? [];
            events.Add(BuildEvent(null, WeeklyAstronomyEventType.BestViewingWindow, "Best viewing window", "Best local viewing window for the week with practical observing convenience.", dayObjects, dayObjects.FirstOrDefault()?.ObjectCode, bestNight.BestStartUtc, dayObjects.FirstOrDefault()?.ViewingDirection, null, "StellariumScene", "BestWindowScene", "Actionable viewing recommendation"));
            events.Add(BuildEvent(null, WeeklyAstronomyEventType.DirectionalObservation, $"Look {dayObjects.FirstOrDefault()?.ViewingDirection ?? "west"} after sunset", "Use horizon direction guidance and altitude to find the best objects.", dayObjects, dayObjects.FirstOrDefault()?.ObjectCode, bestNight.BestStartUtc, dayObjects.FirstOrDefault()?.ViewingDirection, null, "StellariumScene", "DirectionalGuideScene", "Step-by-step finder guidance"));
        }

        var deduped = events.GroupBy(e => $"{e.EventType}|{e.BestDateLocal}|{string.Join(',', e.Objects.Select(o=>o.ObjectCode).OrderBy(x=>x))}")
            .Select(g => g.OrderByDescending(x => x.ImportanceScore).First()).ToList();
        var primary = deduped.OrderByDescending(e => e.ImportanceScore + e.VisibilityScore).FirstOrDefault();
        var counts = deduped.GroupBy(e => e.EventType.ToString()).ToDictionary(g => g.Key, g => g.Count());
        var valid = deduped.Count > 0 && deduped.Any(e => e.EventType == WeeklyAstronomyEventType.BestViewingWindow) && primary is not null && deduped.All(e => !string.IsNullOrWhiteSpace(e.RecommendedVisualSource));
        var message = valid ? "Weekly astronomy events extracted." : "No astronomy events could be extracted from Skyfield weekly forecast.";

        var result = new WeeklyAstronomyEventExtractionResult(valid, message, deduped, $"{region}: {weekStartDate:yyyy-MM-dd} to {weekEndDate:yyyy-MM-dd}; language={language}", counts, primary, warnings, missing);
        if (!string.IsNullOrWhiteSpace(workingDirectoryRoot))
        {
            var debugDir = Path.Combine(workingDirectoryRoot!, "debug");
            Directory.CreateDirectory(debugDir);
            var path = Path.Combine(debugDir, "weekly-astronomy-events.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { extractedEvents = result.ExtractedEvents, sourceForecastSummary = result.SourceForecastSummary, eventCountsByType = result.EventCountsByType, selectedPrimaryEvent = result.SelectedPrimaryEvent, warnings = result.Warnings, missingData = result.MissingData }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return result;
    }

    private static WeeklyAstronomyEvent BuildEvent(Microsoft.Extensions.Logging.ILogger? logger, WeeklyAstronomyEventType type, string title, string summary, IReadOnlyList<WeeklySkyForecastVisibleObjectItem> objs, string? primary, DateTime? bestTimeUtc, string? direction, double? separation, string visualSource, string sceneType, string narrationAngle)
    {
        var first = objs.FirstOrDefault();
        var objects = objs.Select(o =>
        {
            logger?.LogInformation("SKYFIELD_COORDINATE_EXTRACTION object={ObjectName} timestamp={Timestamp} altitude={Altitude} azimuth={Azimuth} rawAlt={RawAltitude} rawAz={RawAzimuth} method={Method}",
                o.ObjectName, o.BestViewingTimeUtc, o.MaxAltitudeDegrees, o.BestViewingAzimuthDegrees, o.MaxAltitudeDegrees, o.BestViewingAzimuthDegrees, "weekly-forecast-max-sample");
            if (o.MaxAltitudeDegrees.HasValue && !o.BestViewingAzimuthDegrees.HasValue)
            {
                logger?.LogCritical("SKYFIELD_COORDINATE_EXTRACTION altitude exists but azimuth missing for object={ObjectName} timestamp={Timestamp}", o.ObjectName, o.BestViewingTimeUtc);
            }
            return new WeeklyAstronomyEventObject(o.ObjectCode, o.ObjectName, o.MaxAltitudeDegrees, o.BestViewingAzimuthDegrees, null, o.VisibilityScore);
        }).ToList();
        return new WeeklyAstronomyEvent(Guid.NewGuid().ToString("N"), type, title, summary, objects, primary, objects.Count, bestTimeUtc.HasValue ? DateOnly.FromDateTime(bestTimeUtc.Value) : null, bestTimeUtc.HasValue ? TimeOnly.FromDateTime(bestTimeUtc.Value.ToLocalTime()) : null, direction, first?.MaxAltitudeDegrees, null, separation, null, objs.DefaultIfEmpty().Average(o => o?.VisibilityScore ?? 0), Math.Min(100, 60 + objects.Count * 10), type is WeeklyAstronomyEventType.Grouping or WeeklyAstronomyEventType.Conjunction ? 70 : 45, visualSource, sceneType, narrationAngle, []);
    }
}
