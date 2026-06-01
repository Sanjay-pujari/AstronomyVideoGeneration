using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EventScoring;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.NarrationEngine;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition;

public interface IWeeklyTimelineCompositionEngine
{
    Task<WeeklyTimelineCompositionResult> ComposeAndPersistAsync(WeeklyTimelineCompositionInput input, CancellationToken cancellationToken);
}

public sealed record WeeklyTimelineCompositionInput(
    Guid PipelineRunId,
    string WorkingDirectoryRoot,
    string LongformNarrationPath,
    string ShortformNarrationPath,
    string NarrationAssetMapPath,
    string NarrationTimelineMapPath,
    string EditorialReviewReportPath,
    string WeeklyEventPriorityReportPath,
    string HeroEventSelectionPath,
    string WeeklyProductionAssetManifestPath,
    string WeeklyAssetQualityReportPath,
    string WeeklyVideoReadinessReportPath,
    WeeklyNarrationPackage LongformNarration,
    WeeklyNarrationPackage ShortformNarration,
    IReadOnlyList<NarrationAssetMapEntry> NarrationAssetMap,
    IReadOnlyList<NarrationTimelineMapEntry> NarrationTimelineMap,
    WeeklyNarrationEditorialReviewReport EditorialReviewReport,
    WeeklyEventPriorityReport EventPriorityReport,
    WeeklyHeroEventSelection HeroEventSelection,
    WeeklyProductionAssetManifest ProductionAssetManifest,
    IReadOnlyList<string> AllProductionImageAssets);

public sealed record WeeklyTimelineCompositionResult(
    FinalRenderTimeline Timeline,
    IReadOnlyList<FinalRenderShotListEntry> ShotList,
    TimelineTransitionPlan TransitionPlan,
    IReadOnlyList<SegmentTimelineReportEntry> SegmentTimelineReport,
    RetentionMarkerTimeline RetentionMarkerTimeline,
    TimelineValidationReport ValidationReport,
    string FinalRenderTimelinePath,
    string FinalRenderShotListPath,
    string TimelineTransitionPlanPath,
    string SegmentTimelineReportPath,
    string RetentionMarkerTimelinePath,
    string FinalTimelineValidationReportPath,
    bool TimelineCompositionReady,
    bool LongformFinalTimelineReady,
    bool ShortformFinalTimelineReady,
    double LongformActualDurationSeconds,
    double ShortformActualDurationSeconds,
    int LongformFinalShotCount,
    int ShortformFinalShotCount,
    int TotalFinalShotCount);

public sealed record FinalRenderTimeline(Guid PipelineRunId, DateTime GeneratedAtUtc, FinalRenderEpisodeTimeline Longform, FinalRenderEpisodeTimeline Shortform);
public sealed record FinalRenderEpisodeTimeline(int TargetDurationSeconds, double ActualDurationSeconds, IReadOnlyList<FinalRenderSegment> Segments);
public sealed record FinalRenderSegment(string SegmentId, string SegmentType, string EpisodeType, double StartSecond, double EndSecond, double DurationSeconds, string NarrationText, double NarrationStart, double NarrationEnd, IReadOnlyList<FinalRenderShot> Shots);
public sealed record FinalRenderShot(
    int ShotNumber,
    string AssetId,
    string AssetType,
    string AssetPath,
    double StartSecond,
    double EndSecond,
    double DurationSeconds,
    string TransitionIn,
    string TransitionOut,
    string MotionEffect,
    string Purpose,
    bool IsOverlay = false,
    int? OverlayStartSecond = null,
    int? OverlayEndSecond = null);

public sealed record FinalRenderShotListEntry(
    string EpisodeType,
    string SegmentId,
    string SegmentType,
    int ShotNumber,
    int GlobalShotNumber,
    string AssetId,
    string AssetType,
    string AssetPath,
    double StartSecond,
    double EndSecond,
    double DurationSeconds,
    string TransitionIn,
    string TransitionOut,
    string MotionEffect,
    string NarrationText,
    double NarrationStart,
    double NarrationEnd);

public sealed record TimelineTransitionPlan(Guid PipelineRunId, DateTime GeneratedAtUtc, IReadOnlyList<TimelineTransitionPlanEntry> Transitions, IReadOnlyList<string> AllowedTransitionNames, IReadOnlyList<string> RulesApplied);
public sealed record TimelineTransitionPlanEntry(string EpisodeType, string SegmentId, string SegmentType, int ShotNumber, string FromAssetType, string ToAssetType, double AtSecond, string TransitionName, string Reason);
public sealed record SegmentTimelineReportEntry(string EpisodeType, string SegmentId, string SegmentType, double StartSecond, double EndSecond, double DurationSeconds, int ShotCount, IReadOnlyList<string> AssetTypes, int NarrationCharacterCount, bool HasNarration, bool HasAssets);
public sealed record RetentionMarkerTimeline(Guid PipelineRunId, DateTime GeneratedAtUtc, IReadOnlyList<RetentionMarkerTimelineEntry> Markers, string OverlapStrategy);
public sealed record RetentionMarkerTimelineEntry(double ResetSecond, string EpisodeType, int AssignedShotNumber, string AssetId, string AssetType, string AssetPath, string Reason, bool IsOverlay, double OverlayStartSecond, double OverlayEndSecond, string Strategy);
public sealed record TimelineValidationReport(
    bool TimelineCompositionReady,
    bool LongformTimelineReady,
    bool ShortformTimelineReady,
    double LongformActualDurationSeconds,
    double ShortformActualDurationSeconds,
    int LongformShotCount,
    int ShortformShotCount,
    int TotalShotCount,
    bool AssetValidationPassed,
    bool NarrationValidationPassed,
    bool DurationValidationPassed,
    bool GapValidationPassed,
    bool OverlapValidationPassed,
    bool VisualVarietyPassed,
    bool HeroEventRulePassed,
    bool AstrophotographyRulePassed,
    bool SummaryRulePassed,
    bool ShortformRulePassed,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed class WeeklyTimelineCompositionEngine(ILogger<WeeklyTimelineCompositionEngine> logger) : IWeeklyTimelineCompositionEngine
{
    private const int LongformTargetSeconds = 380;
    private const int ShortformTargetSeconds = 50;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    private static readonly string[] AllowedTransitions = ["Cut", "SoftCut", "Fade", "FadeIn", "FadeOut", "CrossFade", "Dissolve", "SlowDissolve", "CinematicFade"];
    private static readonly string[] AllowedMotionEffects = ["StaticHold", "SlowZoomIn", "SlowZoomOut", "SubtlePan", "SlowPushIn", "KenBurnsZoom", "KenBurnsDrift"];

    public async Task<WeeklyTimelineCompositionResult> ComposeAndPersistAsync(WeeklyTimelineCompositionInput input, CancellationToken cancellationToken)
    {
        logger.LogInformation("TIMELINE_COMPOSITION_START pipelineRunId={PipelineRunId} root={Root}", input.PipelineRunId, input.WorkingDirectoryRoot);
        var episodeDirectory = Path.Combine(input.WorkingDirectoryRoot, "episode");
        Directory.CreateDirectory(episodeDirectory);
        logger.LogInformation("TIMELINE_INPUTS_LOADED pipelineRunId={PipelineRunId}", input.PipelineRunId);

        var availableAssets = BuildAvailableAssets(input);
        var longform = ComposeEpisode(input.LongformNarration, input.NarrationTimelineMap, input.ProductionAssetManifest, availableAssets, "LongFormWeeklyForecast", LongformTargetSeconds, false);
        var shortform = ComposeEpisode(input.ShortformNarration, input.NarrationTimelineMap, input.ProductionAssetManifest, availableAssets, "ShortFormWeeklyForecast", ShortformTargetSeconds, true);
        var timeline = new FinalRenderTimeline(input.PipelineRunId, DateTime.UtcNow, longform, shortform);
        var shotList = BuildShotList(timeline);
        var transitionPlan = BuildTransitionPlan(input.PipelineRunId, timeline);
        var segmentReport = BuildSegmentReport(timeline);
        var retentionTimeline = BuildRetentionTimeline(input, timeline, availableAssets);
        var validation = Validate(timeline, shotList, input, availableAssets);

        var finalRenderTimelinePath = Path.Combine(episodeDirectory, "final-render-timeline.json");
        var finalRenderShotListPath = Path.Combine(episodeDirectory, "final-render-shot-list.json");
        var timelineTransitionPlanPath = Path.Combine(episodeDirectory, "timeline-transition-plan.json");
        var segmentTimelineReportPath = Path.Combine(episodeDirectory, "segment-timeline-report.json");
        var retentionMarkerTimelinePath = Path.Combine(episodeDirectory, "retention-marker-timeline.json");
        var finalTimelineValidationReportPath = Path.Combine(episodeDirectory, "final-timeline-validation-report.json");

        await File.WriteAllTextAsync(finalRenderTimelinePath, JsonSerializer.Serialize(timeline, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(finalRenderShotListPath, JsonSerializer.Serialize(shotList, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(timelineTransitionPlanPath, JsonSerializer.Serialize(transitionPlan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(segmentTimelineReportPath, JsonSerializer.Serialize(segmentReport, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(retentionMarkerTimelinePath, JsonSerializer.Serialize(retentionTimeline, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(finalTimelineValidationReportPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);

        if (validation.TimelineCompositionReady) logger.LogInformation("TIMELINE_VALIDATION_PASSED pipelineRunId={PipelineRunId}", input.PipelineRunId);
        else logger.LogWarning("TIMELINE_VALIDATION_FAILED pipelineRunId={PipelineRunId} errors={Errors}", input.PipelineRunId, validation.Errors);
        logger.LogInformation("TIMELINE_COMPOSITION_COMPLETE pipelineRunId={PipelineRunId}", input.PipelineRunId);

        return new WeeklyTimelineCompositionResult(timeline, shotList, transitionPlan, segmentReport, retentionTimeline, validation, finalRenderTimelinePath, finalRenderShotListPath, timelineTransitionPlanPath, segmentTimelineReportPath, retentionMarkerTimelinePath, finalTimelineValidationReportPath, validation.TimelineCompositionReady, validation.LongformTimelineReady, validation.ShortformTimelineReady, longform.ActualDurationSeconds, shortform.ActualDurationSeconds, validation.LongformShotCount, validation.ShortformShotCount, validation.TotalShotCount);
    }

    private FinalRenderEpisodeTimeline ComposeEpisode(WeeklyNarrationPackage narration, IReadOnlyList<NarrationTimelineMapEntry> timelineMap, WeeklyProductionAssetManifest manifest, IReadOnlyDictionary<string, AssetCandidate> availableAssets, string episodeType, int targetDuration, bool shortform)
    {
        var segments = new List<FinalRenderSegment>();
        var cursor = 0;
        foreach (var narrationSegment in narration.Segments)
        {
            var isLast = narrationSegment == narration.Segments[^1];
            var duration = isLast ? targetDuration - cursor : narrationSegment.EstimatedDurationSeconds;
            if (duration <= 0) continue;
            var start = cursor;
            var end = start + duration;
            var map = timelineMap.FirstOrDefault(x => x.EpisodeType.Contains(shortform ? "Short" : "Long", StringComparison.OrdinalIgnoreCase) && x.SegmentId.Equals(narrationSegment.SegmentId, StringComparison.OrdinalIgnoreCase));
            var assets = SelectAssets(narrationSegment.SegmentId, narrationSegment.SegmentType, episodeType, map, manifest, availableAssets, shortform);
            var shots = BuildShots(narrationSegment.SegmentType, episodeType, start, end, assets, shortform);
            logger.LogInformation(shortform ? "TIMELINE_SHORTFORM_SEGMENT_COMPOSED segmentId={SegmentId} duration={Duration}" : "TIMELINE_LONGFORM_SEGMENT_COMPOSED segmentId={SegmentId} duration={Duration}", narrationSegment.SegmentId, duration);
            segments.Add(new FinalRenderSegment(narrationSegment.SegmentId, narrationSegment.SegmentType, episodeType, start, end, duration, narrationSegment.NarrationText, start, end, shots));
            cursor = end;
        }
        return new FinalRenderEpisodeTimeline(targetDuration, segments.Sum(x => x.DurationSeconds), segments);
    }

    private static IReadOnlyList<AssetCandidate> SelectAssets(string segmentId, string segmentType, string episodeType, NarrationTimelineMapEntry? map, WeeklyProductionAssetManifest manifest, IReadOnlyDictionary<string, AssetCandidate> availableAssets, bool shortform)
    {
        var selected = new List<AssetCandidate>();
        void Add(AssetCandidate? asset)
        {
            if (asset is null || string.IsNullOrWhiteSpace(asset.AssetPath)) return;
            if (selected.All(x => !x.AssetId.Equals(asset.AssetId, StringComparison.OrdinalIgnoreCase))) selected.Add(asset);
        }

        foreach (var entry in (map?.AssetSequence ?? []).Where(x => !x.Purpose.Contains("retention", StringComparison.OrdinalIgnoreCase)))
        {
            Add(new AssetCandidate(entry.AssetId, NormalizeAssetType(entry.AssetType), entry.AssetPath));
        }
        foreach (var asset in manifest.SegmentBundles.Where(x => x.SegmentId.Equals(segmentId, StringComparison.OrdinalIgnoreCase) && (x.EpisodeType.Contains(episodeType, StringComparison.OrdinalIgnoreCase) || episodeType.Contains(x.EpisodeType, StringComparison.OrdinalIgnoreCase))).SelectMany(x => x.AssignedVisualAssets))
        {
            Add(new AssetCandidate(asset.AssetId, NormalizeAssetType(asset.SourceType.ToString()), asset.FilePath));
        }
        foreach (var required in RequiredTypes(segmentType, shortform)) Add(FindByType(required, availableAssets.Values, selected));
        foreach (var fallback in availableAssets.Values.Where(x => selected.All(s => !s.AssetId.Equals(x.AssetId, StringComparison.OrdinalIgnoreCase))))
        {
            Add(fallback);
            if (selected.Select(x => x.AssetType).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 3) break;
        }
        if (selected.Count == 0) Add(availableAssets.Values.FirstOrDefault());
        return selected.Count > 0 ? Arrange(selected).Take(shortform ? 5 : 6).ToList() : selected;
    }

    private static IReadOnlyList<FinalRenderShot> BuildShots(string segmentType, string episodeType, int start, int end, IReadOnlyList<AssetCandidate> assets, bool shortform)
    {
        var duration = Math.Max(1, end - start);
        var maxShotDuration = shortform && !segmentType.Equals("StrongestEvent", StringComparison.OrdinalIgnoreCase) ? 12 : 14;
        var minShotCount = Math.Max(1, (int)Math.Ceiling(duration / (double)maxShotDuration));
        var shotCount = Math.Max(minShotCount, Math.Min(Math.Max(assets.Count, 1), 3));
        if (assets.Count == 0) assets = [new AssetCandidate("missing-asset", "AICinematic", string.Empty)];
        var baseDuration = duration / shotCount;
        var remainder = duration % shotCount;
        var cursor = start;
        var shots = new List<FinalRenderShot>();
        for (var i = 0; i < shotCount; i++)
        {
            var asset = assets[i % assets.Count];
            var shotDuration = baseDuration + (i < remainder ? 1 : 0);
            var shotStart = cursor;
            var shotEnd = i == shotCount - 1 ? end : cursor + shotDuration;
            var transitionIn = i == 0 ? (start == 0 ? "FadeIn" : ResolveTransition(shots.LastOrDefault()?.AssetType, asset.AssetType, segmentType, shortform)) : ResolveTransition(shots[^1].AssetType, asset.AssetType, segmentType, shortform);
            var transitionOut = i == shotCount - 1 ? "FadeOut" : ResolveTransition(asset.AssetType, assets[(i + 1) % assets.Count].AssetType, segmentType, shortform);
            var motionEffect = ResolveMotionEffect(asset.AssetType, segmentType);
            shots.Add(new FinalRenderShot(i + 1, asset.AssetId, asset.AssetType, asset.AssetPath, shotStart, shotEnd, shotEnd - shotStart, transitionIn, transitionOut, motionEffect, i == 0 ? $"primary visual for {segmentType}" : "supporting visual variety"));
            cursor = shotEnd;
        }
        return shots;
    }

    private TimelineTransitionPlan BuildTransitionPlan(Guid pipelineRunId, FinalRenderTimeline timeline)
    {
        var entries = new List<TimelineTransitionPlanEntry>();
        foreach (var segment in timeline.Longform.Segments.Concat(timeline.Shortform.Segments))
        {
            foreach (var pair in segment.Shots.Zip(segment.Shots.Skip(1)))
            {
                var transition = ResolveTransition(pair.First.AssetType, pair.Second.AssetType, segment.SegmentType, segment.EpisodeType.Contains("Short", StringComparison.OrdinalIgnoreCase));
                logger.LogInformation("TIMELINE_TRANSITION_ASSIGNED episodeType={EpisodeType} segmentId={SegmentId} shotNumber={ShotNumber} transition={Transition}", segment.EpisodeType, segment.SegmentId, pair.Second.ShotNumber, transition);
                entries.Add(new TimelineTransitionPlanEntry(segment.EpisodeType, segment.SegmentId, segment.SegmentType, pair.Second.ShotNumber, pair.First.AssetType, pair.Second.AssetType, pair.Second.StartSecond, transition, ResolveTransitionReason(pair.First.AssetType, pair.Second.AssetType, segment.SegmentType, segment.EpisodeType)));
            }
            foreach (var shot in segment.Shots)
            {
                logger.LogInformation("TIMELINE_MOTION_EFFECT_ASSIGNED episodeType={EpisodeType} segmentId={SegmentId} shotNumber={ShotNumber} motionEffect={MotionEffect}", segment.EpisodeType, segment.SegmentId, shot.ShotNumber, shot.MotionEffect);
            }
        }
        return new TimelineTransitionPlan(pipelineRunId, DateTime.UtcNow, entries, AllowedTransitions, ["Asset-type transition matrix applied", "HeroEvent uses slower transitions", "Shortform uses faster transitions", "Repeated asset type uses Cut or SoftCut"]);
    }

    private RetentionMarkerTimeline BuildRetentionTimeline(WeeklyTimelineCompositionInput input, FinalRenderTimeline timeline, IReadOnlyDictionary<string, AssetCandidate> availableAssets)
    {
        var markers = new List<RetentionMarkerTimelineEntry>();
        foreach (var marker in input.EditorialReviewReport.EmotionalResetMarkers.OrderBy(x => x.ResetSecond))
        {
            var segment = timeline.Longform.Segments.FirstOrDefault(s => marker.ResetSecond >= s.StartSecond && marker.ResetSecond < s.EndSecond)
                ?? timeline.Shortform.Segments.FirstOrDefault(s => marker.ResetSecond >= s.StartSecond && marker.ResetSecond < s.EndSecond);
            if (segment is null) continue;
            var shot = segment.Shots.First(s => marker.ResetSecond >= s.StartSecond && marker.ResetSecond < s.EndSecond);
            var asset = availableAssets.Values.FirstOrDefault(x => x.AssetId.Equals(marker.AssetId, StringComparison.OrdinalIgnoreCase)) ?? new AssetCandidate(marker.AssetId, NormalizeAssetType(marker.AssetType), shot.AssetPath);
            var overlayStart = Math.Max(segment.StartSecond, marker.ResetSecond - 2);
            var overlayEnd = Math.Min(segment.EndSecond, marker.ResetSecond + 4);
            logger.LogInformation("TIMELINE_RETENTION_MARKER_ASSIGNED episodeType={EpisodeType} resetSecond={ResetSecond} assignedShot={ShotNumber}", segment.EpisodeType, marker.ResetSecond, shot.ShotNumber);
            markers.Add(new RetentionMarkerTimelineEntry(marker.ResetSecond, segment.EpisodeType, shot.ShotNumber, asset.AssetId, NormalizeAssetType(asset.AssetType), asset.AssetPath, marker.Reason, true, overlayStart, overlayEnd, "Overlay retention asset on existing primary shot to avoid invalid primary-shot overlap."));
        }
        return new RetentionMarkerTimeline(input.PipelineRunId, DateTime.UtcNow, markers, "Retention markers are overlays on containing primary shots; primary shots are not duplicated or overlapped.");
    }

    private TimelineValidationReport Validate(FinalRenderTimeline timeline, IReadOnlyList<FinalRenderShotListEntry> shotList, WeeklyTimelineCompositionInput input, IReadOnlyDictionary<string, AssetCandidate> availableAssets)
    {
        logger.LogInformation("TIMELINE_VALIDATION_START pipelineRunId={PipelineRunId}", input.PipelineRunId);
        var errors = new List<string>();
        var warnings = new List<string>();
        var validPaths = new HashSet<string>(input.AllProductionImageAssets.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        foreach (var asset in availableAssets.Values) validPaths.Add(asset.AssetPath);

        var assetValidationPassed = shotList.All(x => !string.IsNullOrWhiteSpace(x.AssetPath) && validPaths.Contains(x.AssetPath));
        if (!assetValidationPassed) errors.Add("One or more final timeline shot assetPath values were not found in the production asset manifest, allProductionImageAssets, or narration timeline map.");
        var narrationValidationPassed = timeline.Longform.Segments.Concat(timeline.Shortform.Segments).All(x => !string.IsNullOrWhiteSpace(x.NarrationText) && x.NarrationStart >= 0 && x.NarrationEnd > x.NarrationStart && x.DurationSeconds > 0);
        if (!narrationValidationPassed) errors.Add("One or more segments failed narration validation.");
        var durationValidationPassed = Math.Abs(timeline.Longform.ActualDurationSeconds - LongformTargetSeconds) <= 0.5 && Math.Abs(timeline.Shortform.ActualDurationSeconds - ShortformTargetSeconds) <= 0.5;
        if (!durationValidationPassed) errors.Add("Timeline durations do not match final targets.");
        var gapValidationPassed = HasNoGaps(timeline.Longform) && HasNoGaps(timeline.Shortform);
        if (!gapValidationPassed) errors.Add("Timeline contains a gap.");
        var overlapValidationPassed = HasNoOverlaps(timeline.Longform) && HasNoOverlaps(timeline.Shortform);
        if (!overlapValidationPassed) errors.Add("Timeline contains invalid overlapping primary shots.");
        var varietyPassed = ValidateVisualVariety(timeline, warnings);
        var heroEventPassed = ValidateHeroEvent(timeline, warnings);
        var astrophotographyPassed = timeline.Longform.Segments.Concat(timeline.Shortform.Segments).Where(x => x.SegmentType.Equals("AstrophotographyTip", StringComparison.OrdinalIgnoreCase)).All(x => x.Shots.Any(s => s.AssetType.Equals("ExpandedStellarium", StringComparison.OrdinalIgnoreCase)));
        if (!astrophotographyPassed) errors.Add("AstrophotographyTip segment is missing ExpandedStellarium.");
        var summaryPassed = timeline.Longform.Segments.Concat(timeline.Shortform.Segments).Where(x => x.SegmentType.Equals("WeeklySummary", StringComparison.OrdinalIgnoreCase)).All(x => x.Shots.Any(s => s.AssetType.Equals("AICinematic", StringComparison.OrdinalIgnoreCase)) && x.Shots.Any(s => s.AssetType is "MotionGraphic" or "Stellarium"));
        if (!summaryPassed) errors.Add("WeeklySummary segment does not include AICinematic plus MotionGraphic or Stellarium.");
        var shortformPassed = ValidateShortform(timeline.Shortform, errors);
        var ready = assetValidationPassed && narrationValidationPassed && durationValidationPassed && gapValidationPassed && overlapValidationPassed && varietyPassed && heroEventPassed && astrophotographyPassed && summaryPassed && shortformPassed;
        return new TimelineValidationReport(ready, ready && timeline.Longform.ActualDurationSeconds == LongformTargetSeconds, ready && timeline.Shortform.ActualDurationSeconds == ShortformTargetSeconds, timeline.Longform.ActualDurationSeconds, timeline.Shortform.ActualDurationSeconds, timeline.Longform.Segments.Sum(x => x.Shots.Count), timeline.Shortform.Segments.Sum(x => x.Shots.Count), shotList.Count, assetValidationPassed, narrationValidationPassed, durationValidationPassed, gapValidationPassed, overlapValidationPassed, varietyPassed, heroEventPassed, astrophotographyPassed, summaryPassed, shortformPassed, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IReadOnlyDictionary<string, AssetCandidate> BuildAvailableAssets(WeeklyTimelineCompositionInput input)
    {
        var assets = new Dictionary<string, AssetCandidate>(StringComparer.OrdinalIgnoreCase);
        void Add(string assetId, string assetType, string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetId) || string.IsNullOrWhiteSpace(assetPath)) return;
            assets.TryAdd(assetId, new AssetCandidate(assetId, NormalizeAssetType(assetType), assetPath));
        }
        foreach (var bundle in input.ProductionAssetManifest.SegmentBundles)
        foreach (var asset in bundle.AssignedVisualAssets)
            Add(asset.AssetId, asset.SourceType.ToString(), asset.FilePath);
        foreach (var map in input.NarrationTimelineMap)
        foreach (var asset in map.AssetSequence)
            Add(asset.AssetId, asset.AssetType, asset.AssetPath);
        return assets;
    }

    private static IReadOnlyList<FinalRenderShotListEntry> BuildShotList(FinalRenderTimeline timeline)
    {
        var global = 1;
        return timeline.Longform.Segments.Concat(timeline.Shortform.Segments)
            .SelectMany(segment => segment.Shots.Select(shot => new FinalRenderShotListEntry(segment.EpisodeType, segment.SegmentId, segment.SegmentType, shot.ShotNumber, global++, shot.AssetId, shot.AssetType, shot.AssetPath, shot.StartSecond, shot.EndSecond, shot.DurationSeconds, shot.TransitionIn, shot.TransitionOut, shot.MotionEffect, segment.NarrationText, segment.NarrationStart, segment.NarrationEnd)))
            .ToList();
    }

    private static IReadOnlyList<SegmentTimelineReportEntry> BuildSegmentReport(FinalRenderTimeline timeline)
        => timeline.Longform.Segments.Concat(timeline.Shortform.Segments)
            .Select(segment => new SegmentTimelineReportEntry(segment.EpisodeType, segment.SegmentId, segment.SegmentType, segment.StartSecond, segment.EndSecond, segment.DurationSeconds, segment.Shots.Count, segment.Shots.Select(x => x.AssetType).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), segment.NarrationText.Length, !string.IsNullOrWhiteSpace(segment.NarrationText), segment.Shots.Count > 0 && segment.Shots.All(x => !string.IsNullOrWhiteSpace(x.AssetPath))))
            .ToList();

    private static bool HasNoGaps(FinalRenderEpisodeTimeline timeline)
    {
        if (timeline.Segments.Count == 0 || timeline.Segments[0].StartSecond != 0 || timeline.Segments[^1].EndSecond != timeline.TargetDurationSeconds) return false;
        if (timeline.Segments.Zip(timeline.Segments.Skip(1), (a, b) => a.EndSecond == b.StartSecond).Any(x => !x)) return false;
        return timeline.Segments.All(s => s.Shots.Count > 0 && s.Shots[0].StartSecond == s.StartSecond && s.Shots[^1].EndSecond == s.EndSecond && s.Shots.Zip(s.Shots.Skip(1), (a, b) => a.EndSecond == b.StartSecond).All(x => x));
    }

    private static bool HasNoOverlaps(FinalRenderEpisodeTimeline timeline)
        => timeline.Segments.All(s => s.Shots.Zip(s.Shots.Skip(1), (a, b) => a.EndSecond <= b.StartSecond).All(x => x));

    private static bool ValidateVisualVariety(FinalRenderTimeline timeline, List<string> warnings)
    {
        var passed = true;
        foreach (var segment in timeline.Longform.Segments.Concat(timeline.Shortform.Segments))
        {
            var streak = 1;
            for (var i = 1; i < segment.Shots.Count; i++)
            {
                streak = string.Equals(segment.Shots[i].AssetType, segment.Shots[i - 1].AssetType, StringComparison.OrdinalIgnoreCase) ? streak + 1 : 1;
                if (streak > 2 && !RequiresRepeatedType(segment.SegmentType))
                {
                    passed = false;
                    warnings.Add($"More than two consecutive primary shots use {segment.Shots[i].AssetType} in {segment.EpisodeType}:{segment.SegmentId}.");
                }
            }
        }
        return passed;
    }

    private static bool ValidateHeroEvent(FinalRenderTimeline timeline, List<string> warnings)
    {
        var heroSegments = timeline.Longform.Segments.Concat(timeline.Shortform.Segments).Where(x => x.SegmentType is "HeroEvent" or "StrongestEvent").ToList();
        if (heroSegments.Count == 0) return true;
        var contentMax = timeline.Longform.Segments.Where(x => !x.SegmentType.Contains("Opening", StringComparison.OrdinalIgnoreCase) && !x.SegmentType.Contains("Summary", StringComparison.OrdinalIgnoreCase) && !x.SegmentType.Contains("CallToAction", StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty().Max(x => x?.DurationSeconds ?? 0);
        var passed = heroSegments.All(x => x.Shots.Select(s => s.AssetType).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
            && heroSegments.Any(x => x.DurationSeconds >= contentMax || x.SegmentType.Equals("StrongestEvent", StringComparison.OrdinalIgnoreCase));
        if (!passed) warnings.Add("HeroEvent/StrongestEvent should have highest content duration or explicit priority and at least two asset types.");
        return passed;
    }

    private static bool ValidateShortform(FinalRenderEpisodeTimeline shortform, List<string> errors)
    {
        var passed = shortform.ActualDurationSeconds == ShortformTargetSeconds
            && shortform.Segments.SelectMany(x => x.Shots).All(x => x.DurationSeconds <= 12 || shortform.Segments.Any(s => s.SegmentType.Equals("StrongestEvent", StringComparison.OrdinalIgnoreCase) && s.Shots.Contains(x)))
            && shortform.Segments.Where(x => x.SegmentType.Equals("CallToAction", StringComparison.OrdinalIgnoreCase)).All(x => x.Shots.Any(s => s.AssetType is "AICinematic" or "MotionGraphic"));
        if (!passed) errors.Add("Shortform rule failed.");
        return passed;
    }

    private static string ResolveTransition(string? fromType, string toType, string segmentType, bool shortform)
    {
        if (string.IsNullOrWhiteSpace(fromType)) return "FadeIn";
        if (string.Equals(fromType, toType, StringComparison.OrdinalIgnoreCase)) return shortform ? "Cut" : "SoftCut";
        if (shortform) return "CrossFade";
        if (segmentType is "HeroEvent" or "StrongestEvent") return (fromType, toType) switch
        {
            ("NASA", "JWST") => "SlowDissolve",
            ("ExpandedStellarium", "AICinematic") => "CinematicFade",
            _ => "SlowDissolve"
        };
        return (fromType, toType) switch
        {
            ("AICinematic", "Stellarium") => "Dissolve",
            ("Stellarium", "MotionGraphic") => "CrossFade",
            ("MotionGraphic", "Stellarium") => "Fade",
            ("Stellarium", "NASA") => "CrossFade",
            ("NASA", "JWST") => "SlowDissolve",
            ("JWST", "AICinematic") => "Dissolve",
            ("ExpandedStellarium", "AICinematic") => "CinematicFade",
            _ => "CrossFade"
        };
    }

    private static string ResolveMotionEffect(string assetType, string segmentType)
    {
        var effect = segmentType switch
        {
            "HeroEvent" or "StrongestEvent" => assetType.Equals("Stellarium", StringComparison.OrdinalIgnoreCase) ? "SubtlePan" : "SlowPushIn",
            "WeeklySummary" => "SlowZoomOut",
            _ => assetType switch
            {
                "AICinematic" => "SlowZoomIn",
                "Stellarium" => "SubtlePan",
                "ExpandedStellarium" => "SlowPushIn",
                "NASA" => "KenBurnsZoom",
                "JWST" => "KenBurnsDrift",
                "MotionGraphic" => "StaticHold",
                "EducationalOverlay" or "WhereToLook" => "StaticHold",
                _ => "StaticHold"
            }
        };
        return AllowedMotionEffects.Contains(effect) ? effect : "StaticHold";
    }

    private static string ResolveTransitionReason(string from, string to, string segmentType, string episodeType)
    {
        if (segmentType is "HeroEvent" or "StrongestEvent") return "HeroEvent segment uses slower transitions.";
        if (episodeType.Contains("Short", StringComparison.OrdinalIgnoreCase)) return "Shortform uses faster transitions.";
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return "Same asset type repeated uses Cut or SoftCut.";
        return $"Asset transition rule applied for {from} to {to}.";
    }

    private static IReadOnlyList<string> RequiredTypes(string segmentType, bool shortform) => segmentType switch
    {
        "HeroEvent" or "StrongestEvent" => ["Stellarium", "MotionGraphic", "AICinematic"],
        "AstrophotographyTip" => ["ExpandedStellarium", "AICinematic", "MotionGraphic"],
        "WeeklySummary" => ["AICinematic", "MotionGraphic", "Stellarium"],
        "CallToAction" => ["AICinematic", "MotionGraphic"],
        _ => shortform ? ["AICinematic", "MotionGraphic", "Stellarium"] : []
    };

    private static AssetCandidate? FindByType(string type, IEnumerable<AssetCandidate> assets, IEnumerable<AssetCandidate> selected)
        => assets.FirstOrDefault(x => x.AssetType.Equals(type, StringComparison.OrdinalIgnoreCase) && selected.All(s => !s.AssetId.Equals(x.AssetId, StringComparison.OrdinalIgnoreCase)));

    private static List<AssetCandidate> Arrange(IReadOnlyList<AssetCandidate> assets)
    {
        var remaining = assets.ToList();
        var arranged = new List<AssetCandidate>();
        while (remaining.Count > 0)
        {
            var previousType = arranged.Count == 0 ? null : arranged[^1].AssetType;
            var next = remaining.FirstOrDefault(x => !x.AssetType.Equals(previousType, StringComparison.OrdinalIgnoreCase)) ?? remaining[0];
            remaining.Remove(next);
            arranged.Add(next);
        }
        return arranged;
    }

    private static bool RequiresRepeatedType(string segmentType) => segmentType.Contains("Timelapse", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAssetType(string assetType)
    {
        if (assetType.Contains("Expanded", StringComparison.OrdinalIgnoreCase)) return "ExpandedStellarium";
        if (assetType.Contains("Stellarium", StringComparison.OrdinalIgnoreCase)) return "Stellarium";
        if (assetType.Contains("JWST", StringComparison.OrdinalIgnoreCase)) return "JWST";
        if (assetType.Contains("NASA", StringComparison.OrdinalIgnoreCase)) return "NASA";
        if (assetType.Contains("Overlay", StringComparison.OrdinalIgnoreCase) || assetType.Equals("WhereToLook", StringComparison.OrdinalIgnoreCase)) return "EducationalOverlay";
        if (assetType.Contains("Motion", StringComparison.OrdinalIgnoreCase)) return "MotionGraphic";
        if (assetType.Contains("AI", StringComparison.OrdinalIgnoreCase)) return "AICinematic";
        return assetType;
    }

    private sealed record AssetCandidate(string AssetId, string AssetType, string AssetPath);
}
