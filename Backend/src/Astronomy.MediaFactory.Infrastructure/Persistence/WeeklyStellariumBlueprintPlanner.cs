using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static class WeeklyStellariumBlueprintPlanner
{
    public static WeeklyStellariumBlueprintPackage Build(WeeklyStoryboard storyboard, WeeklyAstronomyEventExtractionResult extraction, WeeklySkyForecastContext ctx, string root)
    {
        var primary = extraction.SelectedPrimaryEvent ?? extraction.ExtractedEvents.FirstOrDefault();
        var date = primary?.BestDateLocal ?? ctx.WeekStartDate;
        var time = primary?.BestTimeLocal ?? new TimeOnly(20, 0);
        var direction = primary?.Direction ?? "South-West";
        var grouped = extraction.ExtractedEvents.FirstOrDefault(e => e.EventType == WeeklyAstronomyEventType.Grouping);
        var groupedCodes = grouped?.Objects.Select(o => o.ObjectCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        var moonJupVen = new[] { "MOON", "JUPITER", "VENUS" }.Where(c => groupedCodes.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        var sceneBlueprints = new List<WeeklyStellariumSceneBlueprint>();

        foreach (var segment in storyboard.OrderedSegments)
        {
            var sceneType = segment.SegmentType switch
            {
                WeeklyStoryboardSegmentType.OpeningHook => "wide_sky_reveal",
                WeeklyStoryboardSegmentType.WeeklyOverview => "weekly_timeline_montage",
                WeeklyStoryboardSegmentType.MainAstronomyEvent or WeeklyStoryboardSegmentType.GroupingFocus => "multi_object_grouping",
                WeeklyStoryboardSegmentType.BestViewingNight => "best_window_horizon",
                WeeklyStoryboardSegmentType.ViewingDirectionGuide => "observation_guide",
                WeeklyStoryboardSegmentType.ClosingSequence => "calm_horizon_outro",
                _ => "weekly_context_sky"
            };

            var shotCount = segment.SegmentType == WeeklyStoryboardSegmentType.WeeklyOverview ? 3 : 1;
            var highlights = ResolveHighlights(segment, grouped, moonJupVen);
            var sceneCode = $"{segment.SegmentCode.ToLowerInvariant()}_{sceneType}";
            var imagePath = Path.Combine(root, "stellarium", "scenes", $"{sceneCode}.png");
            var sscPath = Path.Combine(root, "stellarium", "scripts", $"{sceneCode}.ssc");
            var shots = Enumerable.Range(1, shotCount)
                .Select(i => new WeeklyStellariumShot($"{sceneCode}_shot{i}", sceneType, date.AddDays(i - 1), time, Math.Max(4, segment.EstimatedDurationSeconds / shotCount), Path.Combine(root, "stellarium", "scenes", $"{sceneCode}_shot{i}.png")))
                .ToList();

            sceneBlueprints.Add(new WeeklyStellariumSceneBlueprint(
                segment.SegmentCode,
                sceneCode,
                sceneType,
                "Stellarium",
                date,
                time,
                ctx.Timezone,
                ctx.Latitude,
                ctx.Longitude,
                direction,
                sceneType == "wide_sky_reveal" ? 1.1 : 1.3,
                sceneType == "multi_object_grouping" ? 58 : 82,
                true,
                true,
                true,
                highlights,
                BuildOverlayText(segment, date, time, direction),
                segment.VisualPlan.RecommendedMotionStyle,
                "fade_from_black",
                segment.SegmentType == WeeklyStoryboardSegmentType.ClosingSequence ? "fade_to_black" : "cross_dissolve",
                imagePath,
                sscPath,
                new WeeklyStellariumCameraPlan("wide horizon / night sky", direction, segment.VisualPlan.RecommendedMotionStyle, 1.2, sceneType == "multi_object_grouping" ? 58 : 82, true, true, true),
                new WeeklyStellariumOverlayPlan(BuildOverlayText(segment, date, time, direction), true, segment.SegmentType == WeeklyStoryboardSegmentType.ViewingDirectionGuide, segment.SegmentType == WeeklyStoryboardSegmentType.ViewingDirectionGuide, segment.SegmentType == WeeklyStoryboardSegmentType.ViewingDirectionGuide),
                shots,
                BuildSscCommands(date, time, ctx, direction, sceneType == "multi_object_grouping" ? moonJupVen : highlights.Select(h => h.ObjectCode).ToList(), imagePath)));
        }

        var validation = Validate(sceneBlueprints, grouped is not null, moonJupVen, root);
        var package = new WeeklyStellariumBlueprintPackage(validation.Count == 0, ctx.RegionId, ctx.Latitude, ctx.Longitude, ctx.Timezone, sceneBlueprints, validation, []);

        var debugPath = Path.Combine(root, "debug");
        Directory.CreateDirectory(debugPath);
        File.WriteAllText(Path.Combine(debugPath, "weekly-stellarium-blueprints.json"), JsonSerializer.Serialize(new { storyboard, stellariumBlueprintPackage = package, sceneBlueprints, validation }, new JsonSerializerOptions { WriteIndented = true }));
        return package;
    }

    private static List<string> BuildSscCommands(DateOnly date, TimeOnly time, WeeklySkyForecastContext ctx, string direction, IReadOnlyList<string> objectCodes, string screenshot)
    =>
    [
        $"core.setDate('{date:yyyy-MM-dd}T{time:HH:mm:ss}', 'local')",
        $"core.setObserverLocation({ctx.Latitude:F6}, {ctx.Longitude:F6}, 0, '{ctx.LocationName}', '{ctx.Timezone}')",
        $"core.moveToAltAzi('{direction}', 35)",
        "core.setFov(58)",
        "landscapeMgr.setFlagAtmosphere(true); landscapeMgr.setFlagLandscape(true)",
        "core.setTracking(false)",
        "labelMgr.setFlagLabels(false)",
        $"core.output('Blueprint labels will be choreographed per shot for: {string.Join(",", objectCodes)}')",
        $"core.screenshot('{screenshot}', false, 'png')"
    ];

    private static List<WeeklyStellariumHighlightObject> ResolveHighlights(WeeklyStoryboardSegment segment, WeeklyAstronomyEvent? grouped, IReadOnlyList<string> moonJupVen)
    {
        var codes = segment.SegmentType is WeeklyStoryboardSegmentType.MainAstronomyEvent or WeeklyStoryboardSegmentType.GroupingFocus
            ? moonJupVen
            : segment.TargetObjects;

        if (codes.Count == 0 && grouped is not null)
            codes = grouped.Objects.Select(o => o.ObjectCode).ToList();

        return codes.Select((c, i) => new WeeklyStellariumHighlightObject(c, c, c, "soft_glow", i == 0 ? "#FFD166" : "#7FDBFF", i + 1, "likely_visible")).ToList();
    }

    private static List<string> BuildOverlayText(WeeklyStoryboardSegment segment, DateOnly date, TimeOnly time, string direction)
        => [$"{segment.Title}", $"Date: {date:dd MMM yyyy}", $"Time: {time:HH:mm}", $"Direction: {direction}"];

    private static List<string> Validate(IReadOnlyList<WeeklyStellariumSceneBlueprint> scenes, bool hasGrouping, IReadOnlyList<string> moonJupVen, string root)
    {
        var issues = new List<string>();
        var rootFull = Path.GetFullPath(root);
        if (hasGrouping && !scenes.Any(s => s.SceneType == "multi_object_grouping")) issues.Add("At least one blueprint must use MultiObjectSkyGrouping when grouping event exists.");
        if (hasGrouping && moonJupVen.Count >= 2)
        {
            var grouping = scenes.FirstOrDefault(s => s.SceneType == "multi_object_grouping");
            if (grouping is null || !new[] { "MOON", "JUPITER", "VENUS" }.Where(moonJupVen.Contains).All(c => grouping.HighlightObjects.Any(h => h.ObjectCode.Equals(c, StringComparison.OrdinalIgnoreCase))))
                issues.Add("Grouping blueprint must include Moon + Jupiter + Venus when available.");
        }
        if (scenes.Any(s => string.IsNullOrWhiteSpace(s.CameraDirection))) issues.Add("Every Stellarium blueprint must have cameraDirection.");
        if (scenes.Any(s => s.Shots.Any(shot => string.IsNullOrWhiteSpace(shot.ExpectedOutputImagePath)))) issues.Add("Every shot must have expected output path.");
        if (scenes.Any(s => !IsPathUnderRoot(s.ExpectedSscScriptPath, rootFull) || !IsPathUnderRoot(s.ExpectedOutputImagePath, rootFull))) issues.Add("Every scene path must be under workingDirectoryRoot.");
        if (scenes.Any(s => s.Shots.Any(shot => !IsPathUnderRoot(shot.ExpectedOutputImagePath, rootFull)))) issues.Add("Every shot image path must be under workingDirectoryRoot.");
        if (scenes.Any(s => s.DateLocal == default || s.TimeLocal == default)) issues.Add("Every blueprint must have date/time/location.");
        if (scenes.Any(s => string.IsNullOrWhiteSpace(s.TransitionIn) || string.IsNullOrWhiteSpace(s.TransitionOut))) issues.Add("Every scene must include transitionIn/transitionOut.");
        if (scenes.Where(s => s.SceneType == "multi_object_grouping").Any(s => s.HighlightObjects.Count == 0)) issues.Add("Grouping/conjunction scenes cannot have empty highlight objects.");
        return issues;
    }

    private static bool IsPathUnderRoot(string candidatePath, string rootPath)
    {
        var candidateFull = Path.GetFullPath(candidatePath);
        var rootWithSep = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidateFull.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) || string.Equals(candidateFull, rootPath, StringComparison.OrdinalIgnoreCase);
    }
}
