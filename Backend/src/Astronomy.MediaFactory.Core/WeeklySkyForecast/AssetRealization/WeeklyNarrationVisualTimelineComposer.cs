using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;

public sealed record WeeklyNarrationVisualTimelineShot(
    string ShotId,
    string SourceType,
    string ImagePath,
    int StartSecond,
    int EndSecond,
    int DurationSeconds,
    string MotionIntent,
    string TransitionIn,
    string TransitionOut,
    string NarrationBindingRole,
    bool ProductionReady);

public sealed record WeeklyNarrationVisualTimelineSegment(
    string SegmentId,
    string EpisodeType,
    string SegmentType,
    int StartSecond,
    int EndSecond,
    int DurationSeconds,
    string NarrationText,
    string NarrationStatus,
    IReadOnlyList<WeeklyNarrationVisualTimelineShot> AssignedVisualShots,
    bool ProductionReadyForTest,
    bool ProductionReadyForFinalVideo,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyNarrationVisualTimeline(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    string TimelineVersion,
    IReadOnlyList<string> InputArtifacts,
    IReadOnlyList<WeeklyNarrationVisualTimelineSegment> Segments);

public sealed record WeeklyTimelineValidationReport(
    bool LongformTimelineReadyForTest,
    bool ShortformTimelineReadyForTest,
    bool LongformTimelineReadyForFinalVideo,
    bool ShortformTimelineReadyForFinalVideo,
    int TotalTimelineDurationSeconds,
    int TotalShotCount,
    int SegmentsWithNarration,
    int SegmentsWithVisuals,
    IReadOnlyList<string> MissingNarrationSegments,
    IReadOnlyList<string> MissingVisualSegments,
    IReadOnlyList<string> FallbackVisualSegments,
    IReadOnlyList<string> FinalReadinessBlockers,
    string TimelineValidationStatus);

public sealed record WeeklyNarrationVisualTimelineResult(
    WeeklyNarrationVisualTimeline Timeline,
    WeeklyTimelineValidationReport ValidationReport,
    string WeeklyNarrationVisualTimelinePath,
    string WeeklyTimelineValidationReportPath,
    bool NarrationVisualTimelineReady);

public sealed record WeeklyNarrationVisualTimelineInput(
    string RootPath,
    string WeeklyProductionAssetManifestPath,
    string WeeklyAssetRealizationReportPath,
    string WeeklyVideoReadinessReportPath,
    string WeeklyEpisodePlanPath,
    string WeeklyLongformPlanPath,
    string WeeklyShortformPlanPath,
    string WeeklyStoryBeatsPath,
    WeeklyProductionAssetManifest ProductionAssetManifest,
    WeeklyAssetCoverageAuditReport AssetRealizationReport,
    WeeklyVideoReadinessReport VideoReadinessReport,
    WeeklyEpisodePlan LongformPlan,
    WeeklyEpisodePlan ShortformPlan,
    WeeklyGeneratedNarrationPackage? GeneratedNarrationPackage,
    IReadOnlyList<string> AllProductionImageAssets,
    IReadOnlyList<string> FrameScreenshots,
    IReadOnlyList<string> ExpandedFrameScreenshots,
    IReadOnlyList<string> AICinematicImagePaths);

public sealed class WeeklyNarrationVisualTimelineComposer(ILogger<WeeklyNarrationVisualTimelineComposer> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] MotionIntents =
    [
        "slow_push_in",
        "gentle_pan_left",
        "gentle_pan_right",
        "vertical_reveal",
        "cinematic_zoom",
        "still_hold",
        "parallax_ready"
    ];

    public async Task<WeeklyNarrationVisualTimelineResult> ComposeAndPersistAsync(WeeklyNarrationVisualTimelineInput input, CancellationToken cancellationToken)
    {
        logger.LogInformation("NARRATION_VISUAL_TIMELINE_START pipelineRunId={PipelineRunId} root={Root}", input.ProductionAssetManifest.PipelineRunId, input.RootPath);
        var episodeDirectory = Path.Combine(input.RootPath, "episode");
        Directory.CreateDirectory(episodeDirectory);

        var segments = new List<WeeklyNarrationVisualTimelineSegment>();
        var currentSecond = 0;
        var safeBundles = input.ProductionAssetManifest.SegmentBundles?.ToList() ?? [];
        var safeLongformSegments = input.LongformPlan.Segments ?? [];
        var safeShortformSegments = input.ShortformPlan.Segments ?? [];
        var bundlesBySegmentId = safeBundles
            .Where(x => !string.IsNullOrWhiteSpace(x.SegmentId))
            .GroupBy(x => x.SegmentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var planSegment in safeLongformSegments.Concat(safeShortformSegments))
        {
            var episodeType = safeLongformSegments.Any(x => x.SegmentId.Equals(planSegment.SegmentId, StringComparison.OrdinalIgnoreCase))
                ? input.LongformPlan.EpisodeType.ToString()
                : input.ShortformPlan.EpisodeType.ToString();
            if (!bundlesBySegmentId.TryGetValue(planSegment.SegmentId, out var bundle))
            {
                bundle = safeBundles.FirstOrDefault(x => x.SegmentType.Equals(planSegment.SegmentType, StringComparison.OrdinalIgnoreCase) && x.EpisodeType.Equals(episodeType, StringComparison.OrdinalIgnoreCase))
                    ?? new SegmentProductionAssetBundle(planSegment.SegmentId, episodeType, planSegment.SegmentType, planSegment.TargetDurationSeconds, "StoryBeatsMissing", input.WeeklyStoryBeatsPath, 0, [], ["dynamic-scene-visual-support"], false, "No post-asset bundle matched this dynamic segment.", ["No matching segment asset bundle was found after dynamic scene normalization."], false, false);
            }

            var narrationText = ResolveNarrationText(input.GeneratedNarrationPackage, planSegment, episodeType);
            var narrationStatus = string.IsNullOrWhiteSpace(narrationText) ? "Missing" : "FinalGeneratedNarrationBound";
            logger.LogInformation("TIMELINE_NARRATION_BOUND segmentId={SegmentId} segmentType={SegmentType} narrationStatus={NarrationStatus}", planSegment.SegmentId, planSegment.SegmentType, narrationStatus);

            var segmentStart = currentSecond;
            var segmentEnd = segmentStart + planSegment.TargetDurationSeconds;
            var shots = BuildShots(bundle, planSegment, segmentStart, segmentEnd, episodeType);
            var warnings = (bundle.Warnings ?? []).ToList();
            if (string.IsNullOrWhiteSpace(narrationText)) warnings.Add("Narration text missing for timeline segment.");
            if (shots.Count == 0) warnings.Add("No visual shot could be bound to timeline segment.");

            var timelineSegment = new WeeklyNarrationVisualTimelineSegment(
                planSegment.SegmentId,
                episodeType,
                planSegment.SegmentType,
                segmentStart,
                segmentEnd,
                planSegment.TargetDurationSeconds,
                narrationText,
                narrationStatus,
                shots,
                bundle.ProductionReadyForTest && !string.IsNullOrWhiteSpace(narrationText) && shots.Count > 0 && shots.All(x => x.ProductionReady),
                bundle.ProductionReadyForFinalVideo && !string.IsNullOrWhiteSpace(narrationText) && shots.Count > 0 && shots.All(x => x.ProductionReady),
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

            logger.LogInformation("TIMELINE_SEGMENT_CREATED segmentId={SegmentId} episodeType={EpisodeType} segmentType={SegmentType} startSecond={StartSecond} endSecond={EndSecond} shotCount={ShotCount}", timelineSegment.SegmentId, timelineSegment.EpisodeType, timelineSegment.SegmentType, timelineSegment.StartSecond, timelineSegment.EndSecond, timelineSegment.AssignedVisualShots.Count);
            segments.Add(timelineSegment);
            currentSecond = segmentEnd;
        }

        var timeline = new WeeklyNarrationVisualTimeline(
            input.ProductionAssetManifest.PipelineRunId,
            DateTime.UtcNow,
            "narration-visual-timeline-v1",
            [
                input.WeeklyProductionAssetManifestPath,
                input.WeeklyAssetRealizationReportPath,
                input.WeeklyVideoReadinessReportPath,
                input.WeeklyEpisodePlanPath,
                input.WeeklyLongformPlanPath,
                input.WeeklyShortformPlanPath,
                input.WeeklyStoryBeatsPath,
                "allProductionImageAssets",
                "frameScreenshots",
                "expandedFrameScreenshots",
                "aiCinematicImagePaths"
            ],
            segments);

        var validation = Validate(input, timeline);
        logger.LogInformation("TIMELINE_VALIDATION_COMPLETED pipelineRunId={PipelineRunId} status={Status} testLongform={LongformTest} testShortform={ShortformTest} finalLongform={LongformFinal} finalShortform={ShortformFinal} totalShots={TotalShots} totalDuration={TotalDuration}", input.ProductionAssetManifest.PipelineRunId, validation.TimelineValidationStatus, validation.LongformTimelineReadyForTest, validation.ShortformTimelineReadyForTest, validation.LongformTimelineReadyForFinalVideo, validation.ShortformTimelineReadyForFinalVideo, validation.TotalShotCount, validation.TotalTimelineDurationSeconds);

        var timelinePath = Path.Combine(episodeDirectory, "weekly-narration-visual-timeline.json");
        var validationPath = Path.Combine(episodeDirectory, "weekly-timeline-validation-report.json");
        await File.WriteAllTextAsync(timelinePath, JsonSerializer.Serialize(timeline, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        logger.LogInformation("NARRATION_VISUAL_TIMELINE_WRITTEN timelinePath={TimelinePath} validationPath={ValidationPath}", timelinePath, validationPath);
        logger.LogInformation("NARRATION_VISUAL_TIMELINE_COMPLETE pipelineRunId={PipelineRunId} ready={Ready}", input.ProductionAssetManifest.PipelineRunId, validation.LongformTimelineReadyForTest && validation.ShortformTimelineReadyForTest);

        return new WeeklyNarrationVisualTimelineResult(timeline, validation, timelinePath, validationPath, validation.LongformTimelineReadyForTest && validation.ShortformTimelineReadyForTest);
    }

    private List<WeeklyNarrationVisualTimelineShot> BuildShots(SegmentProductionAssetBundle bundle, WeeklyEpisodeSegment segment, int segmentStart, int segmentEnd, string episodeType)
    {
        var shots = new List<WeeklyNarrationVisualTimelineShot>();
        if (bundle.AssignedVisualAssets.Count == 0) return shots;

        var (minShotSeconds, maxShotSeconds) = GetShotTimingBounds(segment.SegmentType, episodeType);
        var segmentDuration = segmentEnd - segmentStart;
        var shotCount = Math.Max(1, (int)Math.Ceiling((double)segmentDuration / maxShotSeconds));
        while (shotCount > 1 && (double)segmentDuration / shotCount < minShotSeconds) shotCount--;

        var baseDuration = segmentDuration / shotCount;
        var remainder = segmentDuration % shotCount;
        var cursor = segmentStart;
        for (var i = 0; i < shotCount; i++)
        {
            var asset = bundle.AssignedVisualAssets[i % bundle.AssignedVisualAssets.Count];
            var duration = baseDuration + (i < remainder ? 1 : 0);
            var shotStart = cursor;
            var shotEnd = shotStart + duration;
            var motionIntent = ResolveMotionIntent(segment.SegmentType, i, asset.SourceType);
            var shot = new WeeklyNarrationVisualTimelineShot(
                $"{segment.SegmentId}-shot-{i + 1:00}",
                asset.SourceType.ToString(),
                asset.FilePath,
                shotStart,
                shotEnd,
                duration,
                motionIntent,
                i == 0 ? "clean_cut" : ResolveTransition(segment.SegmentType, i, true),
                i == shotCount - 1 ? "clean_cut" : ResolveTransition(segment.SegmentType, i, false),
                i == 0 ? "primary_narration_anchor" : "supporting_narration_visual",
                asset.Exists && asset.ProductionReady && File.Exists(asset.FilePath));
            logger.LogInformation("TIMELINE_VISUAL_SHOT_BOUND segmentId={SegmentId} shotId={ShotId} sourceType={SourceType} imagePath={ImagePath} startSecond={StartSecond} endSecond={EndSecond} motionIntent={MotionIntent}", segment.SegmentId, shot.ShotId, shot.SourceType, shot.ImagePath, shot.StartSecond, shot.EndSecond, shot.MotionIntent);
            shots.Add(shot);
            cursor = shotEnd;
        }

        return shots;
    }

    private WeeklyTimelineValidationReport Validate(WeeklyNarrationVisualTimelineInput input, WeeklyNarrationVisualTimeline timeline)
    {
        var safeLongformSegments = input.LongformPlan.Segments ?? [];
        var safeShortformSegments = input.ShortformPlan.Segments ?? [];
        var safeTimelineSegments = timeline.Segments?.ToList() ?? [];
        var expectedSegmentCount = safeLongformSegments.Count + safeShortformSegments.Count;
        var missingNarration = safeTimelineSegments.Where(x => string.IsNullOrWhiteSpace(x.NarrationText)).Select(x => x.SegmentId).ToList();
        var missingVisuals = safeTimelineSegments.Where(x => (x.AssignedVisualShots ?? []).Count == 0 || (x.AssignedVisualShots ?? []).Any(s => !File.Exists(s.ImagePath))).Select(x => x.SegmentId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var invalidDuration = safeTimelineSegments.Where(x => x.DurationSeconds <= 0 || x.EndSecond <= x.StartSecond || (x.AssignedVisualShots ?? []).Any(s => s.DurationSeconds <= 0 || s.EndSecond <= s.StartSecond)).Select(x => x.SegmentId).ToList();
        var hasGapOrOverlap = HasGapOrOverlap(safeTimelineSegments);
        var fallbackSegments = safeTimelineSegments.Where(x => (x.Warnings ?? []).Any(w => w.Contains("fallback", StringComparison.OrdinalIgnoreCase)) || (x.AssignedVisualShots ?? []).All(s => s.NarrationBindingRole.Contains("fallback", StringComparison.OrdinalIgnoreCase))).Select(x => x.SegmentId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var testTimelineReady = safeTimelineSegments.Count == expectedSegmentCount
            && expectedSegmentCount == 13
            && missingNarration.Count == 0
            && missingVisuals.Count == 0
            && invalidDuration.Count == 0
            && !hasGapOrOverlap;

        var blockers = new List<string>();
        if (safeTimelineSegments.Count != expectedSegmentCount || expectedSegmentCount != 13) blockers.Add($"Expected 13 timeline segments but found {safeTimelineSegments.Count} of {expectedSegmentCount} planned segments.");
        if (missingNarration.Count > 0) blockers.Add($"Missing narration: {string.Join(",", missingNarration)}.");
        if (missingVisuals.Count > 0) blockers.Add($"Missing visuals or image files: {string.Join(",", missingVisuals)}.");
        if (invalidDuration.Count > 0) blockers.Add($"Invalid timeline durations: {string.Join(",", invalidDuration)}.");
        if (hasGapOrOverlap) blockers.Add("Timeline has gaps or overlaps.");
        if (fallbackSegments.Count > 0) blockers.Add($"Fallback visual segments remain: {string.Join(",", fallbackSegments)}.");
        if ((input.VideoReadinessReport.MissingAssetCategories ?? []).Contains("MotionGraphics", StringComparer.OrdinalIgnoreCase)) blockers.Add("MotionGraphics requirements are not satisfied.");
        if ((input.VideoReadinessReport.MissingAssetCategories ?? []).Contains("EducationalOverlay", StringComparer.OrdinalIgnoreCase)) blockers.Add("EducationalOverlay requirements are not satisfied.");
        if ((input.VideoReadinessReport.MissingAssetCategories ?? []).Contains("NASA", StringComparer.OrdinalIgnoreCase) || (input.VideoReadinessReport.MissingAssetCategories ?? []).Contains("JWST", StringComparer.OrdinalIgnoreCase)) blockers.Add("NASA/JWST requirements are not satisfied.");
        if (input.GeneratedNarrationPackage is null) blockers.Add("Narration is not final production-grade generated narration.");

        var longformSegments = safeTimelineSegments.Where(x => x.EpisodeType == WeeklyEpisodeType.LongFormWeeklyForecast.ToString()).ToList();
        var shortformSegments = safeTimelineSegments.Where(x => x.EpisodeType == WeeklyEpisodeType.ShortFormWeeklyHighlight.ToString()).ToList();
        var longformTest = testTimelineReady && longformSegments.Count == safeLongformSegments.Count && longformSegments.All(x => x.ProductionReadyForTest);
        var shortformTest = testTimelineReady && shortformSegments.Count == safeShortformSegments.Count && shortformSegments.All(x => x.ProductionReadyForTest);
        var longformFinal = longformTest && input.VideoReadinessReport.LongformFinalReady && fallbackSegments.Count == 0 && !blockers.Any(x => x.Contains("requirements are not satisfied", StringComparison.OrdinalIgnoreCase) || x.Contains("Narration is not final", StringComparison.OrdinalIgnoreCase));
        var shortformFinal = shortformTest && input.VideoReadinessReport.ShortformFinalReady && fallbackSegments.Count == 0 && !blockers.Any(x => x.Contains("requirements are not satisfied", StringComparison.OrdinalIgnoreCase) || x.Contains("Narration is not final", StringComparison.OrdinalIgnoreCase));

        return new WeeklyTimelineValidationReport(
            longformTest,
            shortformTest,
            longformFinal,
            shortformFinal,
            safeTimelineSegments.Sum(x => x.DurationSeconds),
            safeTimelineSegments.Sum(x => (x.AssignedVisualShots ?? []).Count),
            safeTimelineSegments.Count(x => !string.IsNullOrWhiteSpace(x.NarrationText)),
            safeTimelineSegments.Count(x => (x.AssignedVisualShots ?? []).Count > 0),
            missingNarration,
            missingVisuals,
            fallbackSegments,
            blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            testTimelineReady ? "ReadyForTest" : "Blocked");
    }

    private static bool HasGapOrOverlap(IReadOnlyList<WeeklyNarrationVisualTimelineSegment> segments)
    {
        var ordered = (segments ?? []).OrderBy(x => x.StartSecond).ToList();
        if (ordered.Count == 0 || ordered[0].StartSecond != 0) return true;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].EndSecond != ordered[i].StartSecond + ordered[i].DurationSeconds) return true;
            if (i > 0 && ordered[i].StartSecond != ordered[i - 1].EndSecond) return true;
            var shots = (ordered[i].AssignedVisualShots ?? []).OrderBy(x => x.StartSecond).ToList();
            if (shots.Count == 0) continue;
            if (shots[0].StartSecond != ordered[i].StartSecond) return true;
            if (shots[^1].EndSecond != ordered[i].EndSecond) return true;
            for (var j = 1; j < shots.Count; j++)
            {
                if (shots[j].StartSecond != shots[j - 1].EndSecond) return true;
            }
        }
        return false;
    }

    private static string ResolveNarrationText(WeeklyGeneratedNarrationPackage? package, WeeklyEpisodeSegment segment, string episodeType)
    {
        if (package is not null && episodeType == WeeklyEpisodeType.LongFormWeeklyForecast.ToString())
        {
            var exact = (package.LongFormNarration.Segments ?? []).FirstOrDefault(s => s.SegmentCode.Equals(segment.SegmentType, StringComparison.OrdinalIgnoreCase))?.NarrationText
                ?? (package.LongFormNarration.Segments ?? []).FirstOrDefault(s => s.SegmentTitle.Contains(segment.Title, StringComparison.OrdinalIgnoreCase) || segment.Title.Contains(s.SegmentTitle, StringComparison.OrdinalIgnoreCase))?.NarrationText;
            if (!string.IsNullOrWhiteSpace(exact)) return exact;

            var mappedCode = segment.SegmentType switch
            {
                "WeeklySkyOverview" => "WhyThisWeekMatters",
                "HeroEvent" => "HeroSkyStory",
                "MoonHighlights" => "MoonPlanetHighlight",
                "PlanetHighlights" => "MoonPlanetHighlight",
                "BestObservationWindow" => "BestObservationNight",
                "AstrophotographyTip" => "ViewingPhotographyTip",
                "WeeklySummary" => "ClosingCTA",
                _ => segment.SegmentType
            };
            var mapped = (package.LongFormNarration.Segments ?? []).FirstOrDefault(s => s.SegmentCode.Equals(mappedCode, StringComparison.OrdinalIgnoreCase))?.NarrationText;
            if (!string.IsNullOrWhiteSpace(mapped)) return mapped;
        }

        if (package is not null)
        {
            var exact = (package.ShortNarrations ?? []).FirstOrDefault(s => s.ShortCode.Equals(segment.SegmentType, StringComparison.OrdinalIgnoreCase))?.NarrationText
                ?? (package.ShortNarrations ?? []).FirstOrDefault(s => s.Title.Contains(segment.Title, StringComparison.OrdinalIgnoreCase) || segment.Title.Contains(s.Title, StringComparison.OrdinalIgnoreCase))?.NarrationText;
            if (!string.IsNullOrWhiteSpace(exact)) return exact;

            var shortIndex = segment.SegmentType switch
            {
                "ShortHook" => 0,
                "StrongestEvent" => 0,
                "WhereToLook" => 1,
                "BestTime" => 1,
                "CallToAction" => 2,
                _ => 0
            };
            var safeShortNarrations = package.ShortNarrations ?? [];
            if (safeShortNarrations.Count > 0) return safeShortNarrations[Math.Min(shortIndex, safeShortNarrations.Count - 1)].NarrationText;
        }

        return $"{segment.Title}. {segment.Purpose}";
    }

    private static (int MinSeconds, int MaxSeconds) GetShotTimingBounds(string segmentType, string episodeType)
    {
        if (episodeType == WeeklyEpisodeType.ShortFormWeeklyHighlight.ToString()) return (2, 5);
        return segmentType switch
        {
            "OpeningHook" => (3, 5),
            "WeeklySkyOverview" => (5, 8),
            "HeroEvent" => (8, 12),
            "MoonHighlights" => (8, 12),
            "PlanetHighlights" => (8, 12),
            "BestObservationWindow" => (6, 8),
            "AstrophotographyTip" => (6, 10),
            "WeeklySummary" => (4, 6),
            _ => (5, 8)
        };
    }

    private static string ResolveMotionIntent(string segmentType, int shotIndex, RealizedVisualAssetSourceType sourceType)
    {
        if (sourceType == RealizedVisualAssetSourceType.MotionGraphics || sourceType == RealizedVisualAssetSourceType.EducationalOverlay) return "still_hold";
        if (segmentType is "HeroEvent" or "StrongestEvent") return shotIndex % 2 == 0 ? "cinematic_zoom" : "slow_push_in";
        if (segmentType is "OpeningHook" or "ShortHook") return MotionIntents[(shotIndex + 4) % MotionIntents.Length];
        if (segmentType is "MoonHighlights" or "PlanetHighlights") return shotIndex % 2 == 0 ? "gentle_pan_left" : "gentle_pan_right";
        if (segmentType is "BestObservationWindow" or "WhereToLook") return "vertical_reveal";
        if (segmentType is "AstrophotographyTip") return "parallax_ready";
        return MotionIntents[shotIndex % MotionIntents.Length];
    }

    private static string ResolveTransition(string segmentType, int shotIndex, bool isIn) => segmentType switch
    {
        "OpeningHook" or "ShortHook" => "fast_cut",
        "WeeklySummary" or "CallToAction" => isIn ? "soft_fade_in" : "soft_fade_out",
        "BestObservationWindow" or "WhereToLook" or "BestTime" => "directional_wipe",
        _ => shotIndex % 2 == 0 ? "crossfade" : "clean_cut"
    };
}
