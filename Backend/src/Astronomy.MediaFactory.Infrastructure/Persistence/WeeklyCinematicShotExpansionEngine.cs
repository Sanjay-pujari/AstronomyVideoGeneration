using Astronomy.MediaFactory.Core;
using System.Text.Json;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklyCinematicShotExpansionEngine : IWeeklyCinematicShotExpansionEngine
{
    public WeeklyCinematicShotPackage Expand(WeeklyStoryboard storyboard, WeeklyStellariumBlueprintPackage stellariumBlueprintPackage, WeeklyAstronomyEventExtractionResult eventExtractionResult, string region, string workingDirectoryRoot, string pipelineRunId)
    {
        var warnings = new List<string> { "narration sync placeholder only" };
        var fov = new List<WeeklyDynamicFovCalculation>();
        var sequences = new List<WeeklyCinematicSceneSequence>();

        foreach (var segment in storyboard.OrderedSegments)
        {
            var bp = stellariumBlueprintPackage.SceneBlueprints.FirstOrDefault(s => s.SegmentCode == segment.SegmentCode);
            if (bp is null) continue;
            var seqShots = BuildShots(segment, bp, eventExtractionResult, workingDirectoryRoot, fov, warnings);
            var seqDuration = seqShots.Sum(s => s.DurationSeconds);
            sequences.Add(new WeeklyCinematicSceneSequence(segment.SegmentCode, bp.SceneCode, bp.SceneType, bp.SceneCode, seqDuration, seqShots, segment.Purpose,
                new WeeklyShotTransitionPlan("cut", "cut", "sequence-start"),
                new WeeklyShotTransitionPlan("cross_dissolve", "fade_to_black", "sequence-end")));
        }

        var validation = Validate(sequences, fov);
        var pkg = new WeeklyCinematicShotPackage(validation.Count == 0, storyboard.EmotionalArc, pipelineRunId, sequences.Count, sequences.Sum(s => s.Shots.Count), sequences.Sum(s => s.DurationSeconds), sequences, fov, validation, warnings);
        Directory.CreateDirectory(Path.Combine(workingDirectoryRoot, "debug"));
        File.WriteAllText(Path.Combine(workingDirectoryRoot, "debug", "weekly-cinematic-shot-timeline.json"), JsonSerializer.Serialize(pkg, new JsonSerializerOptions { WriteIndented = true }));
        return pkg;
    }

    private static List<WeeklyCinematicShot> BuildShots(WeeklyStoryboardSegment segment, WeeklyStellariumSceneBlueprint bp, WeeklyAstronomyEventExtractionResult events, string root, List<WeeklyDynamicFovCalculation> fov, List<string> warnings)
    {
        var shots = new List<WeeklyCinematicShot>();
        if (segment.SegmentType == WeeklyStoryboardSegmentType.OpeningHook)
            shots.AddRange([
                BuildShot(bp, segment, root, "01", "fade_from_black", "reveal darkness and sky ambience", 3, "atmospheric_fade", bp.HighlightObjects.Select(x=>x.ObjectCode).ToList(), null, 82, 82, 82, "W"),
                BuildShot(bp, segment, root, "02", "wide_sky_establishing", "establish horizon and visible sky", 4, "slow_pan", bp.HighlightObjects.Select(x=>x.ObjectCode).ToList(), null, 82, 82, 78, "W"),
                BuildShot(bp, segment, root, "03", "primary_objects_reveal", "reveal Moon/Jupiter/Venus labels", 5, "slow_zoom_in", bp.HighlightObjects.Select(x=>x.ObjectCode).ToList(), "MOON", 58, 70, 58, "W")
            ]);
        else if (segment.SegmentType == WeeklyStoryboardSegmentType.WeeklyOverview)
        {
            var dates = events.ExtractedEvents.Select(e => e.BestDateLocal ?? bp.DateLocal).Distinct().Take(4).ToList();
            if (dates.Count < 2) dates = [bp.DateLocal, bp.DateLocal.AddDays(1), bp.DateLocal.AddDays(3)];
            foreach (var (d, i) in dates.Take(4).Select((d, i) => (d, i)))
                shots.Add(BuildShot(bp with { DateLocal = d }, segment, root, $"{i+1:00}", "timeline_date_focus", $"show important date {d:MMM dd}", 4, "cross_dissolve", bp.HighlightObjects.Select(x=>x.ObjectCode).ToList(), null, 58, 60, 55, bp.CameraDirection));
        }
        else if (segment.SegmentType is WeeklyStoryboardSegmentType.MainAstronomyEvent or WeeklyStoryboardSegmentType.GroupingFocus)
        {
            var targets = bp.HighlightObjects.Select(x => x.ObjectCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!targets.Any()) targets = ["MOON", "JUPITER", "VENUS"];
            var sourceSep = events.SelectedPrimaryEvent?.AngularSeparationDegrees;
            var gfov = CalcGroupingFov(bp.SceneCode, targets, sourceSep, fov, warnings);
            shots.Add(BuildShot(bp, segment, root, "01", "wide_grouping_reveal", "show all objects in one frame", 6, "slow_zoom_in", targets, null, gfov, gfov + 8, gfov, bp.CameraDirection));
            shots.Add(BuildShot(bp, segment, root, "02", "moon_focus", "select/track Moon", 5, "focus_pull", ["MOON"], "MOON", 28, 35, 28, bp.CameraDirection));
            shots.Add(BuildShot(bp, segment, root, "03", "jupiter_focus", "select/track Jupiter", 5, "object_tracking", ["JUPITER"], "JUPITER", 26, 32, 26, bp.CameraDirection));
            shots.Add(BuildShot(bp, segment, root, "04", "venus_focus", "select/track Venus", 5, "bright_flare_reveal", ["VENUS"], "VENUS", 24, 30, 24, bp.CameraDirection));
            shots.Add(BuildShot(bp, segment, root, "05", "final_grouping_composition", "return to all objects in one frame", 6, "slow_zoom_out", targets, null, gfov, gfov - 4, gfov, bp.CameraDirection));
        }
        else if (segment.SegmentType == WeeklyStoryboardSegmentType.BestViewingNight)
            shots.AddRange([BuildShot(bp, segment, root, "01", "best_night_title_reveal", "show date/time label", 4, "title_reveal", [], null, 60, 62, 58, "W"), BuildShot(bp, segment, root, "02", "western_horizon_scene", "show direction and horizon", 4, "slow_pan", [], null, 72, 75, 70, "W"), BuildShot(bp, segment, root, "03", "object_visibility_sequence", "highlight visible objects one by one", 5, "object_tracking", bp.HighlightObjects.Select(x=>x.ObjectCode).ToList(), bp.HighlightObjects.FirstOrDefault()?.ObjectCode, 45, 52, 45, "W"), BuildShot(bp, segment, root, "04", "final_best_window_summary", "practical viewing overlay", 4, "cross_dissolve", [], null, 60, 60, 60, "W")]);
        else if (segment.SegmentType == WeeklyStoryboardSegmentType.ViewingDirectionGuide)
            shots.AddRange([BuildShot(bp, segment, root, "01", "direction_establishing", "show W/E/S direction", 4, "direction_overlay", [], null, 78, 80, 75, "W"), BuildShot(bp, segment, root, "02", "altitude_path_trace", "show altitude hints and labels", 5, "path_trace", bp.HighlightObjects.Select(x=>x.ObjectCode).ToList(), null, 55, 58, 52, "W"), BuildShot(bp, segment, root, "03", "practical_observer_view", "look west after sunset guidance", 5, "instructional_hold", [], null, 70, 70, 70, "W")]);
        else if (segment.SegmentType == WeeklyStoryboardSegmentType.ClosingSequence)
            shots.AddRange([BuildShot(bp, segment, root, "01", "calm_horizon", "slow zoom out", 5, "slow_zoom_out", [], null, 70, 65, 75, "W"), BuildShot(bp, segment, root, "02", "stars_fade", "reduce labels", 4, "label_fade", [], null, 75, 75, 75, "W"), BuildShot(bp, segment, root, "03", "final_cta", "fade to black", 4, "fade_to_black", [], null, 82, 80, 82, "W")]);

        if (shots.Count < 2) shots.Add(BuildShot(bp, segment, root, "99", "holding_shot", "minimum cinematic continuity", 2, "static", bp.HighlightObjects.Select(x=>x.ObjectCode).ToList(), null, bp.FieldOfViewDegrees, bp.FieldOfViewDegrees, bp.FieldOfViewDegrees, bp.CameraDirection));
        return shots;
    }

    private static double CalcGroupingFov(string sceneCode, IReadOnlyList<string> targets, double? sep, List<WeeklyDynamicFovCalculation> list, List<string> warnings)
    {
        double fov; string reason;
        if (sep.HasValue)
        {
            if (sep <= 8) { fov = 22; reason = "separation <= 8°"; }
            else if (sep <= 20) { fov = 38; reason = "separation <= 20°"; }
            else if (sep <= 45) { fov = 58; reason = "separation <= 45°"; }
            else { fov = 80; reason = "separation > 45°"; }
        }
        else { fov = 52; reason = "fallback MultiObjectSkyGrouping default"; warnings.Add("no angular separation provided, fallback FOV used"); }
        list.Add(new WeeklyDynamicFovCalculation(sceneCode, targets, sep, fov, reason));
        return fov;
    }

    private static WeeklyCinematicShot BuildShot(WeeklyStellariumSceneBlueprint bp, WeeklyStoryboardSegment segment, string root, string suffix, string shotType, string purpose, int duration, string motion, IReadOnlyList<string> targets, string? primary, double fov, double startFov, double endFov, string direction)
    {
        var shotCode = $"{bp.SceneCode}_{suffix}";
        var img = Path.Combine(root, "stellarium", "scenes", $"{shotCode}.png");
        var vid = Path.Combine(root, "stellarium", "clips", $"{shotCode}.mp4");
        var ssc = Path.Combine(root, "stellarium", "scripts", $"{shotCode}.ssc");
        var commands = BuildSsc(bp, img, duration, direction, fov, primary, startFov, shotType);
        return new WeeklyCinematicShot(shotCode, shotType, purpose, targets, primary, bp.DateLocal, bp.TimeLocal, duration, direction, fov, startFov, endFov,
            new WeeklyCameraMovementPlan(motion, motion, direction, startFov, endFov, primary is not null),
            new WeeklyShotTransitionPlan("cross_dissolve", "cross_dissolve", "shot entry"),
            new WeeklyShotTransitionPlan("cross_dissolve", segment.SegmentType == WeeklyStoryboardSegmentType.ClosingSequence ? "fade_to_black" : "cross_dissolve", "shot exit"),
            motion, img, vid, ssc, commands,
            new WeeklyShotNarrationSync($"{segment.SegmentCode}_{suffix}", 0, duration, purpose, primary, targets.Take(3).ToList()));
    }

    private static List<string> BuildSsc(WeeklyStellariumSceneBlueprint bp, string img, int duration, string direction, double fov, string? primary, double endFov, string shotType)
    {
        var list = new List<string>
        {
            $"core.setDate('{bp.DateLocal:yyyy-MM-dd}T{bp.TimeLocal:HH\\:mm\\:ss}', 'local')",
            $"core.setObserverLocation({bp.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {bp.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 0, '{bp.RecommendedVisualSource}', '{bp.Timezone}')",
            $"core.moveToAltAzi('{direction}', 35)",
            $"core.setFov({fov.ToString(System.Globalization.CultureInfo.InvariantCulture)})"
        };
        if (!string.IsNullOrWhiteSpace(primary))
        {
            list.Add($"core.selectObjectByName('{primary}')");
            list.Add("core.setTracking(true)");
        }
        if (shotType.Contains("grouping", StringComparison.OrdinalIgnoreCase))
            list.Add("core.output('Plan: apply separate highlight labels for each object; no multi-select string usage.')");
        list.Add($"StelMovementMgr.zoomTo({endFov.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 3)");
        list.Add($"core.wait({Math.Max(1, duration - 1)})");
        list.Add($"core.screenshot('{img.Replace("\\", "/")}', false, 'png')");
        return list;
    }

    private static List<string> Validate(IReadOnlyList<WeeklyCinematicSceneSequence> seq, IReadOnlyList<WeeklyDynamicFovCalculation> fov)
    {
        var issues = new List<string>();
        if (!seq.Any()) issues.Add("no scene sequences");
        var shots = seq.SelectMany(x => x.Shots).ToList();
        if (!shots.Any()) issues.Add("totalShots == 0");
        if (seq.Any(s => s.Shots.Count < 2)) issues.Add("any sequence has fewer than 2 shots");
        if (seq.Where(s => s.SceneType.Contains("wide_sky", StringComparison.OrdinalIgnoreCase)).Any(s => s.Shots.Count < 3)) issues.Add("opening sequence has fewer than 3 shots");
        if (seq.Where(s => s.SceneType.Contains("group", StringComparison.OrdinalIgnoreCase)).Any(s => s.Shots.Count < 5)) issues.Add("grouping scene has fewer than 5 shots");
        if (shots.Any(s => s.DurationSeconds <= 0)) issues.Add("any shot missing duration");
        if (shots.Any(s => string.IsNullOrWhiteSpace(s.ExpectedOutputImagePath) || string.IsNullOrWhiteSpace(s.ExpectedOutputVideoPath) || string.IsNullOrWhiteSpace(s.ExpectedSscScriptPath))) issues.Add("any shot missing output path");
        if (shots.Any(s => string.IsNullOrWhiteSpace(s.TransitionIn.TransitionIn) || string.IsNullOrWhiteSpace(s.TransitionOut.TransitionOut))) issues.Add("any shot missing transition");
        if (shots.Where(s=>s.ShotType.Contains("group", StringComparison.OrdinalIgnoreCase) || s.ShotType.Contains("focus", StringComparison.OrdinalIgnoreCase)).Any(s => !s.TargetObjects.Any())) issues.Add("any grouping shot missing target objects");
        if (seq.Any(s => s.SceneType.Contains("group", StringComparison.OrdinalIgnoreCase)) && !fov.Any()) issues.Add("dynamic FOV missing for grouping scene");
        return issues;
    }
}
