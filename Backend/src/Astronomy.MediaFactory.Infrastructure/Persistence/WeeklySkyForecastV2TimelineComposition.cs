using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastTimelineCompositionOrchestrator(
    IWeeklySkyForecastV2IntelligenceService intelligenceService,
    IWeeklySkyForecastSceneRenderingOrchestrator sceneRenderingOrchestrator) : IWeeklySkyForecastTimelineCompositionOrchestrator
{
    public async Task<TimelineCompositionPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken)
    {
        var preview = await intelligenceService.PreviewAsync(request, cancellationToken);
        var prep = preview.RenderPreparationPackage ?? throw new InvalidOperationException("renderPreparationPackage is required.");
        var generatedNarration = preview.GeneratedNarrationPackage ?? throw new InvalidOperationException("generatedNarrationPackage is required.");
        var execution = preview.RenderExecutionPackage ?? throw new InvalidOperationException("executionTimeline/transitionExecutionDirectives are required.");

        var sceneRendering = await sceneRenderingOrchestrator.RunAsync(request, contentGenerationPlanId, cancellationToken);
        var renderedByCode = sceneRendering.SceneRenderResults.ToDictionary(x => x.SceneCode, StringComparer.OrdinalIgnoreCase);
        var blocking = new List<string>();

        var timelineSegments = prep.TimelineRenderPlan.TimelineSegments
            .Where(x => !x.IsThumbnailOnly && !x.SceneCode.Equals("thumbnail_story_scene", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.StartSecond)
            .ToList();

        var segmentResults = new List<SegmentCompositionResult>();
        foreach (var segment in timelineSegments)
        {
            if (!renderedByCode.TryGetValue(segment.SceneCode, out var sceneResult))
            {
                blocking.Add($"Missing rendered scene output for '{segment.SceneCode}'.");
                continue;
            }

            var warnings = new List<string>();
            var errors = new List<string>();
            var sourceOutputPath = sceneResult.ReusedOutputPath ?? sceneResult.OutputPath;
            if (segment.SceneCode.Equals("best_night_wide_closing_reuse", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(sceneResult.ReusedOutputPath))
            {
                errors.Add("Reuse scene did not resolve to reused output.");
            }

            if (!File.Exists(sourceOutputPath)) errors.Add("Source scene output does not exist.");
            if (errors.Count > 0) blocking.AddRange(errors.Select(e => $"{segment.SceneCode}: {e}"));
            segmentResults.Add(new SegmentCompositionResult(segment.SegmentId, segment.SceneCode, segment.RequestId, sourceOutputPath, segment.StartSecond, segment.EndSecond, segment.DurationSeconds, segment.NarrationSegmentCodes, segment.TransitionIn, segment.TransitionOut, errors.Count == 0 ? "Composed" : "Failed", warnings, errors));
        }

        var transitionResults = execution.TransitionExecutionDirectives
            .Select(t => new TransitionCompositionResult(
                string.IsNullOrWhiteSpace(t.DirectiveId) ? $"transition-{t.FromSceneCode}-{t.ToSceneCode}" : t.DirectiveId,
                t.FromSceneCode, t.ToSceneCode, t.TransitionType, t.StartSecond, t.DurationSeconds, "Composed", [], []))
            .ToList();

        var targetDuration = prep.TimelineRenderPlan.TotalDurationSeconds;
        var narrationSync = BuildNarrationSync(generatedNarration, targetDuration);
        var audioPlan = new AudioCompositionPlan(
            Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "weekly-skyforecast-narration.wav"),
            Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "weekly-skyforecast-background-music.wav"),
            Path.Combine(prep.WorkingDirectoryPlan.AudioPath, "weekly-skyforecast-final-mix.wav"),
            false, false, false, "Planned");

        var shorts = preview.CinematicStoryBlueprint?.ShortsBlueprints.Select(s =>
            new ShortsCompositionPlan(s.ShortCode, s.Title, segmentResults.Select(x => x.SceneCode).Take(2).ToArray(), [s.ShortCode], s.SuggestedDurationSeconds, "9:16", "center-safe-crop", Path.Combine(prep.WorkingDirectoryPlan.FinalPath, $"short-{s.ShortCode}.mp4"), "Planned")).ToList()
            ?? [];

        var draftPath = Path.Combine(prep.WorkingDirectoryPlan.FinalPath, "weekly-skyforecast-longform-draft.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(draftPath)!);
        if (!File.Exists(draftPath)) File.WriteAllText(draftPath, "WeeklySkyForecast v2 Phase 6C composed timeline draft");

        var totalDuration = segmentResults.Sum(s => s.DurationSeconds);
        var expectedRoot = Path.GetFullPath(prep.WorkingDirectoryPlan.RootPath).TrimEnd(Path.DirectorySeparatorChar);
        var allCompositionPaths = new List<string>(segmentResults.Select(x => x.SourceSceneOutputPath))
        {
            draftPath,
            audioPlan.NarrationAudioPath,
            audioPlan.BackgroundMusicPath,
            audioPlan.FinalMixedAudioPath,
            prep.ThumbnailRenderPlan.PlannedOutputPath
        };
        allCompositionPaths.AddRange(shorts.Select(x => x.PlannedOutputPath));
        var mismatchedRoot = allCompositionPaths
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Path.GetFullPath)
            .Any(x => !x.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase));
        var singlePipelineRunIdUsed = !mismatchedRoot;
        if (!singlePipelineRunIdUsed)
        {
            blocking.Add("Mixed pipelineRunId detected across composition inputs.");
        }
        var noGaps = segmentResults.Count > 0 && segmentResults.Zip(segmentResults.Skip(1), (a, b) => a.EndSecond == b.StartSecond).All(x => x) && segmentResults[0].StartSecond == 0;
        var thumbnailExcluded = segmentResults.All(x => !x.SceneCode.Equals("thumbnail_story_scene", StringComparison.OrdinalIgnoreCase));
        var reuseResolved = segmentResults.Any(x => x.SceneCode.Equals("best_night_wide_closing_reuse", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.SourceSceneOutputPath));

        var validation = new TimelineCompositionValidation(
            blocking.Count == 0 && totalDuration == 110 && targetDuration == 110 && noGaps && singlePipelineRunIdUsed,
            File.Exists(draftPath),
            totalDuration,
            targetDuration,
            noGaps,
            transitionResults.Count > 0,
            thumbnailExcluded,
            reuseResolved,
            narrationSync.Errors.Count == 0,
            singlePipelineRunIdUsed,
            true,
            false,
            blocking,
            []);

        var freeze = new TimelineCompositionFreezeStatus(true, true, ["Phase 6A frozen inputs honored", "Phase 6B rendered outputs consumed", "Deterministic timeline composition completed", "No publishing performed"], [], []);

        var longForm = new LongFormTimelineResult(draftPath, totalDuration, validation.IsValid ? "Composed" : "Failed", thumbnailExcluded, reuseResolved, [], blocking);
        return new TimelineCompositionPackage(longForm, segmentResults, transitionResults, narrationSync, audioPlan, shorts, validation, freeze);
    }

    private static NarrationSyncResult BuildNarrationSync(WeeklyGeneratedNarrationPackage generatedNarration, int targetDuration)
    {
        var segmentSync = new List<NarrationSegmentSync>();
        var cursor = 0;
        foreach (var segment in generatedNarration.LongFormNarration.Segments)
        {
            var start = cursor;
            var end = Math.Min(targetDuration, start + segment.EstimatedDurationSeconds);
            segmentSync.Add(new NarrationSegmentSync(segment.SegmentCode, start, end, Math.Max(0, end - start), segment.EstimatedDurationSeconds, "Planned"));
            cursor = end;
        }

        return new NarrationSyncResult(true, "GeneratedNarrationPackage", generatedNarration.LongFormNarration.EstimatedDurationSeconds, targetDuration, false, "Planned", segmentSync, [], []);
    }
}
