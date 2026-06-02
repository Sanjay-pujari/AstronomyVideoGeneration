using Astronomy.MediaFactory.Core.WeeklySkyForecast.AudioGeneration;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklyAudioDrivenTimelineReconciliationTests
{
    [Fact]
    public async Task ReconcileAsync_UsesActualAudioDurationsAsSegmentSourceOfTruth()
    {
        var pipelineRunId = Guid.NewGuid();
        var workingRoot = Path.Combine(Path.GetTempPath(), "weekly-audio-reconcile-tests", Guid.NewGuid().ToString("N"));
        var runRoot = Path.Combine(workingRoot, pipelineRunId.ToString("N"));
        Directory.CreateDirectory(Path.Combine(runRoot, "audio", "longform"));
        Directory.CreateDirectory(Path.Combine(runRoot, "audio", "shortform"));
        Directory.CreateDirectory(Path.Combine(runRoot, "episode"));
        Directory.CreateDirectory(Path.Combine(runRoot, "render"));
        await WriteInputsAsync(runRoot, pipelineRunId);

        var service = new WeeklyAudioDrivenTimelineReconciliationService(
            new StaticWeeklyPipelineRunDirectoryResolver(runRoot),
            NullLogger<WeeklyAudioDrivenTimelineReconciliationService>.Instance);

        var response = await service.ReconcileAsync(pipelineRunId, new WeeklyAudioDrivenTimelineReconciliationRequest(OverwriteExisting: true), CancellationToken.None);

        response.AudioDrivenTimelineReady.Should().BeTrue(response.Errors.FirstOrDefault());
        response.InputMode.Should().Be("NewRendererContract");
        response.NewLongformDurationSeconds.Should().BeApproximately(18.5, 0.001);
        response.NewShortformDurationSeconds.Should().BeApproximately(10.25, 0.001);
        File.Exists(response.AudioDrivenFinalRenderTimelinePath).Should().BeTrue();
        File.Exists(response.AudioDrivenResolvedRenderShotPlanPath).Should().BeTrue();
        File.Exists(response.AudioDrivenRenderContractPath).Should().BeTrue();
        File.Exists(response.AudioDrivenTimelineReconciliationReportPath).Should().BeTrue();
        File.Exists(response.AudioDrivenReconciliationInputResolutionReportPath).Should().BeTrue();

        var timeline = await ReadJsonAsync<FinalRenderTimeline>(response.AudioDrivenFinalRenderTimelinePath);
        timeline.Longform.Segments[0].DurationSeconds.Should().BeApproximately(10.5, 0.001);
        timeline.Longform.Segments[1].StartSecond.Should().BeApproximately(10.5, 0.001);
        timeline.Longform.ActualDurationSeconds.Should().BeApproximately(18.5, 0.001);
        timeline.Shortform.ActualDurationSeconds.Should().BeApproximately(10.25, 0.001);
    }

    [Fact]
    public async Task ReconcileAsync_UsesNewRendererContract_WhenLegacyInputsAreMissing()
    {
        var pipelineRunId = Guid.NewGuid();
        var workingRoot = Path.Combine(Path.GetTempPath(), "weekly-audio-reconcile-tests", Guid.NewGuid().ToString("N"));
        var runRoot = Path.Combine(workingRoot, pipelineRunId.ToString("N"));
        Directory.CreateDirectory(Path.Combine(runRoot, "audio", "longform"));
        Directory.CreateDirectory(Path.Combine(runRoot, "audio", "shortform"));
        Directory.CreateDirectory(Path.Combine(runRoot, "episode"));
        Directory.CreateDirectory(Path.Combine(runRoot, "render"));
        await WriteInputsAsync(runRoot, pipelineRunId, writeLegacyInputs: false);

        var service = new WeeklyAudioDrivenTimelineReconciliationService(
            new StaticWeeklyPipelineRunDirectoryResolver(runRoot),
            NullLogger<WeeklyAudioDrivenTimelineReconciliationService>.Instance);

        var response = await service.ReconcileAsync(pipelineRunId, new WeeklyAudioDrivenTimelineReconciliationRequest(OverwriteExisting: true), CancellationToken.None);

        response.AudioDrivenTimelineReady.Should().BeTrue(response.Errors.FirstOrDefault());
        response.InputMode.Should().Be("NewRendererContract");
        response.NewLongformDurationSeconds.Should().BeApproximately(18.5, 0.001);
        response.NewShortformDurationSeconds.Should().BeApproximately(10.25, 0.001);
        response.Warnings.Should().Contain("Legacy resolved-render-shot-plan.json not found; using new renderer contract.");
        response.Errors.Should().NotContain(error => error.Contains("resolved-render-shot-plan.json", StringComparison.OrdinalIgnoreCase));
        response.Errors.Should().NotContain(error => error.Contains("render-storyboard-report.json", StringComparison.OrdinalIgnoreCase));

        var resolution = await ReadJsonAsync<WeeklyAudioDrivenReconciliationInputResolutionReport>(response.AudioDrivenReconciliationInputResolutionReportPath);
        resolution.InputResolutionReady.Should().BeTrue();
        resolution.InputMode.Should().Be("NewRendererContract");
        resolution.NewContractFilesFound.Should().BeTrue();
        resolution.LegacyFilesFound.Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileAsync_TreatsDynamicGroupingChildrenAsPreservedHeroAndShortformSupport()
    {
        var pipelineRunId = Guid.NewGuid();
        var workingRoot = Path.Combine(Path.GetTempPath(), "weekly-audio-reconcile-tests", Guid.NewGuid().ToString("N"));
        var runRoot = Path.Combine(workingRoot, pipelineRunId.ToString("N"));
        Directory.CreateDirectory(Path.Combine(runRoot, "audio", "longform"));
        Directory.CreateDirectory(Path.Combine(runRoot, "audio", "shortform"));
        Directory.CreateDirectory(Path.Combine(runRoot, "episode"));
        Directory.CreateDirectory(Path.Combine(runRoot, "render"));
        await WriteInputsAsync(runRoot, pipelineRunId, dynamicSplitGrouping: true);

        var service = new WeeklyAudioDrivenTimelineReconciliationService(
            new StaticWeeklyPipelineRunDirectoryResolver(runRoot),
            NullLogger<WeeklyAudioDrivenTimelineReconciliationService>.Instance);

        var response = await service.ReconcileAsync(pipelineRunId, new WeeklyAudioDrivenTimelineReconciliationRequest(OverwriteExisting: true), CancellationToken.None);

        response.AudioDrivenTimelineReady.Should().BeTrue(response.Errors.FirstOrDefault());
        response.Errors.Should().BeEmpty();

        var validation = await ReadJsonAsync<WeeklyAudioDrivenTimelineValidationReport>(response.AudioDrivenTimelineValidationReportPath);
        validation.DynamicGroupingPreservationReady.Should().BeTrue();
        validation.HeroGroupingParentSceneCode.Should().Be("western_planet_grouping_scene");
        validation.HeroGroupingChildSceneCodes.Should().Contain(new[] { "western_planet_grouping_scene_saturn", "western_planet_grouping_scene_venus" });
        validation.HeroGroupingPreservedFrameCount.Should().Be(2);
        validation.HeroGroupingFrameCountExact.Should().Be(0);
        validation.HeroGroupingFrameCountIncludingChildren.Should().Be(2);
        validation.ShortformGroupingPreservedShotCount.Should().Be(2);
        validation.ShortformGroupingShotCountExact.Should().Be(0);
        validation.ShortformGroupingShotCountIncludingChildren.Should().Be(1);
        validation.GroupingChildSceneCodesDetected.Should().Contain(new[] { "western_planet_grouping_scene_saturn", "western_planet_grouping_scene_venus" });
        validation.ShortformCtaVisualPreserved.Should().BeTrue();
        validation.PreservationValidationErrors.Should().BeEmpty();
    }

    private static async Task WriteInputsAsync(string runRoot, Guid pipelineRunId, bool writeLegacyInputs = true, bool dynamicSplitGrouping = false)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var longformHeroShots = dynamicSplitGrouping
            ? new[]
            {
                Shot(1, "western_planet_grouping_scene_venus", "Stellarium", "dynamic/western_planet_grouping_scene_venus.png", 0, 5),
                Shot(2, "western_planet_grouping_scene_saturn", "Stellarium", "dynamic/western_planet_grouping_scene_saturn.png", 5, 10),
                Shot(3, "moon_hero_scene", "Stellarium", "dynamic/moon_hero_scene.png", 10, 15)
            }
            : new[]
            {
                Shot(1, "western_planet_grouping_scene_01", "Stellarium", "western_planet_grouping_scene/01_horizon_context.png", 0, 5),
                Shot(2, "western_planet_grouping_scene_02", "Stellarium", "western_planet_grouping_scene/02_balanced_story_frame.png", 5, 10),
                Shot(3, "western_planet_grouping_scene_03", "Stellarium", "western_planet_grouping_scene/03_alignment_wide.png", 10, 15)
            };
        var longformSummaryShots = new[] { Shot(1, "weekly-summary-card", "MotionGraphic", "motion-graphics/weekly-summary-card.png", 15, 20) };
        var shortShots = dynamicSplitGrouping
            ? new[]
            {
                Shot(1, "fast_cinematic_sky_hook", "AICinematic", "ai-cinematic/fast_cinematic_sky_hook.png", 0, 4),
                Shot(2, "western_planet_grouping_scene_venus", "Stellarium", "dynamic/western_planet_grouping_scene_venus.png", 4, 8),
                Shot(3, "moon_hero_scene", "Stellarium", "dynamic/moon_hero_scene.png", 8, 12),
                Shot(4, "closing_background", "AICinematic", "ai-cinematic/closing-background.png", 12, 15)
            }
            : new[]
            {
                Shot(1, "fast_cinematic_sky_hook", "AICinematic", "ai-cinematic/fast_cinematic_sky_hook.png", 0, 4),
                Shot(2, "western_planet_grouping_scene_01", "Stellarium", "western_planet_grouping_scene/01_horizon_context.png", 4, 8),
                Shot(3, "western_planet_grouping_scene_02", "Stellarium", "western_planet_grouping_scene/02_balanced_story_frame.png", 8, 12),
                Shot(4, "shortform_call_to_action_background", "AICinematic", "ai-cinematic/shortform_call_to_action_background.png", 12, 15)
            };

        var longformSegments = new[]
        {
            new FinalRenderSegment("hero", "HeroEvent", "longform", 0, 15, 15, "hero narration", 0, 15, longformHeroShots),
            new FinalRenderSegment("summary", "WeeklySummary", "longform", 15, 20, 5, "summary narration", 15, 20, longformSummaryShots)
        };
        var shortSegments = new[] { new FinalRenderSegment("short", dynamicSplitGrouping ? "CallToAction" : "ShortHook", "shortform", 0, 15, 15, "short narration", 0, 15, shortShots) };
        var timeline = new FinalRenderTimeline(pipelineRunId, DateTime.UtcNow, new FinalRenderEpisodeTimeline(20, 20, longformSegments), new FinalRenderEpisodeTimeline(15, 15, shortSegments));
        var shotPlan = new ResolvedRenderShotPlan(pipelineRunId, DateTime.UtcNow, [ToEpisodePlan("longform", timeline.Longform), ToEpisodePlan("shortform", timeline.Shortform)]);
        var shotList = ToShotList(timeline);
        var storyboard = new RenderStoryboardReport(pipelineRunId, DateTime.UtcNow, []);
        var contract = new WeeklyRenderContract(pipelineRunId, "WeeklySkyForecast", new DateOnly(2026, 6, 1), "us", "en", new WeeklyEpisodeRenderContract(true, 1920, 1080, 30, 20, "timeline", 4, "long.mp4"), new WeeklyEpisodeRenderContract(true, 1080, 1920, 30, 15, "timeline", 4, "short.mp4"));
        var renderInputManifest = new WeeklyRenderInputManifest(pipelineRunId, DateTime.UtcNow, [], true, true, [], []);
        var manifest = new WeeklyAudioSegmentManifest(pipelineRunId, DateTime.UtcNow,
            [new WeeklyAudioSegmentManifestEntry("hero", "HeroEvent", 15, 10.5, "hero.mp3", "voice", -4.5, "generated"), new WeeklyAudioSegmentManifestEntry("summary", "WeeklySummary", 5, 8.0, "summary.mp3", "voice", 3, "generated")],
            [new WeeklyAudioSegmentManifestEntry("short", "ShortHook", 15, 10.25, "short.mp3", "voice", -4.75, "generated")]);
        var timing = new WeeklyAudioTimingValidationReport(20, 18.5, -1.5, false, 15, 10.25, -4.75, false, [], [], []);

        await File.WriteAllTextAsync(Path.Combine(runRoot, "episode", "final-render-timeline.json"), JsonSerializer.Serialize(timeline, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "episode", "final-render-shot-list.json"), JsonSerializer.Serialize(shotList, options));
        if (writeLegacyInputs)
        {
            await File.WriteAllTextAsync(Path.Combine(runRoot, "render", "resolved-render-shot-plan.json"), JsonSerializer.Serialize(shotPlan, options));
            await File.WriteAllTextAsync(Path.Combine(runRoot, "render", "render-storyboard-report.json"), JsonSerializer.Serialize(storyboard, options));
        }
        await File.WriteAllTextAsync(Path.Combine(runRoot, "render", "weekly-render-contract.json"), JsonSerializer.Serialize(contract, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "render", "render-input-manifest.json"), JsonSerializer.Serialize(renderInputManifest, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "audio", "audio-segment-manifest.json"), JsonSerializer.Serialize(manifest, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "audio", "audio-timing-validation-report.json"), JsonSerializer.Serialize(timing, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "audio", "longform", "weekly-skyforecast-longform.mp3"), "existing audio");
        await File.WriteAllTextAsync(Path.Combine(runRoot, "audio", "shortform", "weekly-skyforecast-shortform.mp3"), "existing audio");
    }

    private static FinalRenderShot Shot(int shotNumber, string assetId, string assetType, string assetPath, double start, double end)
        => new(shotNumber, assetId, assetType, assetPath, start, end, end - start, "Cut", "Cut", "StaticHold", "test shot");

    private static ResolvedRenderEpisodeShotPlan ToEpisodePlan(string episodeType, FinalRenderEpisodeTimeline timeline)
        => new(episodeType, timeline.ActualDurationSeconds, timeline.Segments.Select(segment => new ResolvedRenderSegmentShotPlan(episodeType, segment.SegmentId, segment.SegmentType, segment.StartSecond, segment.EndSecond, segment.DurationSeconds, segment.Shots.Select(shot => new ResolvedRenderShotPlanEntry(shot.ShotNumber, shot.AssetId, shot.AssetType, shot.AssetPath, shot.StartSecond, shot.EndSecond, shot.DurationSeconds, shot.TransitionIn, shot.TransitionOut, shot.MotionEffect, shot.Purpose, "test", false, false)).ToList())).ToList());

    private static IReadOnlyList<FinalRenderShotListEntry> ToShotList(FinalRenderTimeline timeline)
    {
        var global = 1;
        return timeline.Longform.Segments.Concat(timeline.Shortform.Segments)
            .SelectMany(segment => segment.Shots.Select(shot => new FinalRenderShotListEntry(segment.EpisodeType, segment.SegmentId, segment.SegmentType, shot.ShotNumber, global++, shot.AssetId, shot.AssetType, shot.AssetPath, shot.StartSecond, shot.EndSecond, shot.DurationSeconds, shot.TransitionIn, shot.TransitionOut, shot.MotionEffect, segment.NarrationText, segment.NarrationStart, segment.NarrationEnd)))
            .ToList();
    }

    private static async Task<T> ReadJsonAsync<T>(string path)
        => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private sealed class StaticWeeklyPipelineRunDirectoryResolver(string root) : IWeeklyPipelineRunDirectoryResolver
    {
        public Task<string> ResolveRunDirectoryAsync(Guid pipelineRunId) => Task.FromResult(root);
    }
}
