using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.NarrationEngine;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklyVisualIntentEngineTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task BuildAsync_CreatesProfessionalOverlayBasedVisualIntentPlan()
    {
        var pipelineRunId = Guid.NewGuid();
        var runRoot = Path.Combine(Path.GetTempPath(), "weekly-visual-intent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(runRoot, "episode"));
        Directory.CreateDirectory(Path.Combine(runRoot, "render"));
        Directory.CreateDirectory(Path.Combine(runRoot, "audio"));
        await WriteInputsAsync(runRoot, pipelineRunId);

        var service = new WeeklyVisualIntentEngine(new StaticWeeklyPipelineRunDirectoryResolver(runRoot), NullLogger<WeeklyVisualIntentEngine>.Instance);

        var response = await service.BuildAsync(pipelineRunId, CancellationToken.None);

        response.VisualIntentReady.Should().BeTrue(string.Join("; ", response.Errors.Concat(response.Warnings)));
        response.FullscreenMotionGraphicOveruseCount.Should().Be(0);
        response.FullscreenEducationalOverlayCount.Should().Be(0);
        response.ShortformHookStrongVisualPassed.Should().BeTrue();
        response.SaturnNarrationMatchedToSaturnVisual.Should().BeTrue();
        response.VenusNarrationMatchedToVenusVisual.Should().BeTrue();
        response.MoonNarrationMatchedToMoonVisual.Should().BeTrue();
        response.SameFamilyConsecutiveMax.Should().BeLessThanOrEqualTo(2);
        response.NarrationVisualMismatchCount.Should().Be(0);
        File.Exists(response.VisualIntentPlanPath).Should().BeTrue();
        File.Exists(response.VisualIntentShotPlanPath).Should().BeTrue();
        File.Exists(response.VisualIntentValidationReportPath).Should().BeTrue();

        var plan = await ReadJsonAsync<WeeklyVisualIntentPlan>(response.VisualIntentPlanPath);
        plan.Beats.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.NarrationSubject));
        plan.Beats.Single(x => x.SegmentId == "saturn").PrimaryVisual.MatchedObjects.Should().Contain("Saturn");
        plan.Beats.Single(x => x.SegmentId == "venus").PrimaryVisual.MatchedObjects.Should().Contain("Venus");
        plan.Beats.Single(x => x.SegmentId == "moon").PrimaryVisual.MatchedObjects.Should().Contain("Moon");

        var shotPlan = await ReadJsonAsync<WeeklyVisualIntentShotPlan>(response.VisualIntentShotPlanPath);
        shotPlan.Episodes.SelectMany(x => x.Segments).SelectMany(x => x.Overlays).Should().OnlyContain(x => x.IsOverlay);
        shotPlan.Episodes.SelectMany(x => x.Segments).SelectMany(x => x.Shots).Should().NotContain(x => x.VisualFamily is "MotionGraphics" or "EducationalOverlay");
    }

    [Fact]
    public async Task BuildAsync_NormalizesEpisodeContainerWhenTimelineSegmentEpisodeTypeIsStale()
    {
        var pipelineRunId = Guid.NewGuid();
        var runRoot = Path.Combine(Path.GetTempPath(), "weekly-visual-intent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(runRoot, "episode"));
        Directory.CreateDirectory(Path.Combine(runRoot, "render"));
        Directory.CreateDirectory(Path.Combine(runRoot, "audio"));
        await WriteInputsAsync(runRoot, pipelineRunId, useStaleSegmentEpisodeTypes: true);

        var service = new WeeklyVisualIntentEngine(new StaticWeeklyPipelineRunDirectoryResolver(runRoot), NullLogger<WeeklyVisualIntentEngine>.Instance);

        var response = await service.BuildAsync(pipelineRunId, CancellationToken.None);

        response.VisualIntentReady.Should().BeTrue(string.Join("; ", response.Errors.Concat(response.Warnings)));
        response.Warnings.Should().Contain(x => x.Contains("visual intent normalized it", StringComparison.OrdinalIgnoreCase));

        var plan = await ReadJsonAsync<WeeklyVisualIntentPlan>(response.VisualIntentPlanPath);
        plan.Beats.Should().Contain(x => x.EpisodeType == "longform" && x.SegmentId == "opening");
        plan.Beats.Should().Contain(x => x.EpisodeType == "shortform" && x.SegmentId == "short-hook");

        var shotPlan = await ReadJsonAsync<WeeklyVisualIntentShotPlan>(response.VisualIntentShotPlanPath);
        shotPlan.Episodes.Single(x => x.EpisodeType == "longform").Segments.Should().HaveCount(6);
        shotPlan.Episodes.Single(x => x.EpisodeType == "shortform").Segments.Should().HaveCount(2);
    }

    private static async Task WriteInputsAsync(string runRoot, Guid pipelineRunId, bool useStaleSegmentEpisodeTypes = false)
    {
        var assetRoot = Path.Combine(runRoot, "assets");
        Directory.CreateDirectory(assetRoot);
        string Asset(string name)
        {
            var path = Path.Combine(assetRoot, name);
            File.WriteAllText(path, "asset");
            return path;
        }

        var ai = Asset("ai-cinematic-hook.png");
        var saturn = Asset("nasa-saturn-rings-detail.png");
        var venus = Asset("stellarium-venus-west-horizon.png");
        var moon = Asset("stellarium-moon-hero.png");
        var moonReference = Asset("nasa-moon-surface-reference.png");
        var motion = Asset("motion-best-time-lower-third.png");
        var education = Asset("educational-camera-tip-overlay.png");
        var cta = Asset("ai-cinematic-cta.png");

        var longSegmentEpisodeType = useStaleSegmentEpisodeTypes ? "Long" : "longform";
        var shortSegmentEpisodeType = useStaleSegmentEpisodeTypes ? "Short" : "shortform";

        var longSegments = new[]
        {
            Segment("opening", "OpeningHook", longSegmentEpisodeType, 0, 5, "This week starts with a cinematic view of the night sky." , Shot(1, "ai_hook", "AICinematic", ai, 0, 5)),
            Segment("saturn", "ScientificContext", longSegmentEpisodeType, 5, 13, "Saturn and its rings show delicate planet detail.", Shot(1, "saturn_nasa", "NASA", saturn, 5, 13)),
            Segment("venus", "DirectionGuidance", longSegmentEpisodeType, 13, 20, "Look west after sunset for Venus near the horizon.", Shot(1, "venus_stellarium", "Stellarium", venus, 13, 20)),
            Segment("moon", "Observation", longSegmentEpisodeType, 20, 27, "The Moon is the easiest bright landmark tonight.", Shot(1, "moon_stellarium", "Stellarium", moon, 20, 27)),
            Segment("tip", "AstrophotographyTip", longSegmentEpisodeType, 27, 34, "Use a tripod and camera timer for a cleaner Moon photo.", Shot(1, "moon_stellarium", "Stellarium", moon, 27, 34)),
            Segment("summary", "WeeklySummary", longSegmentEpisodeType, 34, 39, "In summary, these are the strongest sky moments of the week.", Shot(1, "ai_hook", "AICinematic", ai, 34, 39))
        };
        var shortSegments = new[]
        {
            Segment("short-hook", "ShortHook", shortSegmentEpisodeType, 0, 4, "Saturn rings are the strongest sky visual this week.", Shot(1, "saturn_nasa", "NASA", saturn, 0, 4)),
            Segment("short-cta", "CallToAction", shortSegmentEpisodeType, 4, 8, "Follow for next week's sky forecast.", Shot(1, "ai_cta", "AICinematic", cta, 4, 8))
        };

        var timeline = new FinalRenderTimeline(pipelineRunId, DateTime.UtcNow, new FinalRenderEpisodeTimeline(39, 39, longSegments), new FinalRenderEpisodeTimeline(8, 8, shortSegments));
        var shotPlan = new ResolvedRenderShotPlan(pipelineRunId, DateTime.UtcNow, [ToEpisode("longform", timeline.Longform), ToEpisode("shortform", timeline.Shortform)]);
        var manifest = new WeeklyProductionAssetManifest(pipelineRunId, "us", "en", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 7), 39, 8, 7, 3, 0, 2, 1, 0, 1, 1,
        [
            Bundle("opening", "longform", "OpeningHook", Realized("ai_hook", RealizedVisualAssetSourceType.AICinematic, ai), Realized("motion_best_time", RealizedVisualAssetSourceType.MotionGraphics, motion)),
            Bundle("saturn", "longform", "ScientificContext", Realized("saturn_nasa", RealizedVisualAssetSourceType.NASA, saturn)),
            Bundle("venus", "longform", "DirectionGuidance", Realized("venus_stellarium", RealizedVisualAssetSourceType.StellariumBase, venus), Realized("motion_best_time", RealizedVisualAssetSourceType.MotionGraphics, motion)),
            Bundle("moon", "longform", "Observation", Realized("moon_stellarium", RealizedVisualAssetSourceType.StellariumBase, moon)),
            Bundle("tip", "longform", "AstrophotographyTip", Realized("moon_stellarium", RealizedVisualAssetSourceType.StellariumBase, moon), Realized("moon_reference", RealizedVisualAssetSourceType.NASA, moonReference), Realized("educational_camera", RealizedVisualAssetSourceType.EducationalOverlay, education)),
            Bundle("summary", "longform", "WeeklySummary", Realized("ai_hook", RealizedVisualAssetSourceType.AICinematic, ai), Realized("motion_best_time", RealizedVisualAssetSourceType.MotionGraphics, motion)),
            Bundle("short-hook", "shortform", "ShortHook", Realized("saturn_nasa", RealizedVisualAssetSourceType.NASA, saturn)),
            Bundle("short-cta", "shortform", "CallToAction", Realized("ai_cta", RealizedVisualAssetSourceType.AICinematic, cta), Realized("motion_best_time", RealizedVisualAssetSourceType.MotionGraphics, motion))
        ]);
        var longNarration = new WeeklyNarrationPackage(pipelineRunId, DateTime.UtcNow, "en", "test", 39, 39, longSegments.Select(x => new WeeklyNarrationSegment(x.SegmentId, x.SegmentType, x.NarrationText, (int)x.DurationSeconds, 1, 1, false)).ToList());
        var shortNarration = new WeeklyNarrationPackage(pipelineRunId, DateTime.UtcNow, "en", "test", 8, 8, shortSegments.Select(x => new WeeklyNarrationSegment(x.SegmentId, x.SegmentType, x.NarrationText, (int)x.DurationSeconds, 1, 1, false)).ToList());
        var assetMap = longSegments.Concat(shortSegments).Select(x => new NarrationAssetMapEntry(x.SegmentId, x.SegmentType, x.EpisodeType, x.NarrationText, x.Shots.Select(s => s.AssetId).ToList(), x.Shots.Select(s => s.AssetType).ToList())).ToList();
        var timelineMap = longSegments.Concat(shortSegments).Select(x => new NarrationTimelineMapEntry(x.SegmentId, x.SegmentType, x.EpisodeType, (int)x.NarrationStart, (int)x.NarrationEnd, x.Shots.Select(s => new NarrationTimelineAssetSequenceEntry(s.AssetId, s.AssetType, s.AssetPath, (int)s.StartSecond, (int)s.EndSecond, s.Purpose)).ToList())).ToList();

        await WriteJson(Path.Combine(runRoot, "render", "audio-driven-final-render-timeline.json"), timeline);
        await WriteJson(Path.Combine(runRoot, "render", "audio-driven-resolved-render-shot-plan.json"), shotPlan);
        await WriteJson(Path.Combine(runRoot, "episode", "weekly-production-asset-manifest.json"), manifest);
        await WriteJson(Path.Combine(runRoot, "episode", "longform-narration.json"), longNarration);
        await WriteJson(Path.Combine(runRoot, "episode", "shortform-narration.json"), shortNarration);
        await WriteJson(Path.Combine(runRoot, "episode", "narration-asset-map.json"), assetMap);
        await WriteJson(Path.Combine(runRoot, "episode", "narration-timeline-map.json"), new { pipelineRunId, generatedAtUtc = DateTime.UtcNow, segments = timelineMap });
    }

    private static FinalRenderSegment Segment(string id, string type, string episodeType, double start, double end, string narration, FinalRenderShot shot)
        => new(id, type, episodeType, start, end, end - start, narration, start, end, [shot]);

    private static FinalRenderShot Shot(int number, string assetId, string assetType, string assetPath, double start, double end)
        => new(number, assetId, assetType, assetPath, start, end, end - start, "cut", "cut", "slow_push", "test");

    private static ResolvedRenderEpisodeShotPlan ToEpisode(string episodeType, FinalRenderEpisodeTimeline timeline)
        => new(episodeType, timeline.ActualDurationSeconds, timeline.Segments.Select(x => new ResolvedRenderSegmentShotPlan(episodeType, x.SegmentId, x.SegmentType, x.StartSecond, x.EndSecond, x.DurationSeconds, x.Shots.Select(s => new ResolvedRenderShotPlanEntry(s.ShotNumber, s.AssetId, s.AssetType, s.AssetPath, s.StartSecond, s.EndSecond, s.DurationSeconds, s.TransitionIn, s.TransitionOut, s.MotionEffect, s.Purpose, "test", false, false)).ToList())).ToList());

    private static SegmentProductionAssetBundle Bundle(string segmentId, string episodeType, string segmentType, params RealizedVisualAsset[] assets)
        => new(segmentId, episodeType, segmentType, 5, "FinalGeneratedNarrationBound", "narration.txt", 10, assets, [], true, "ready", [], true, true);

    private static RealizedVisualAsset Realized(string id, RealizedVisualAssetSourceType sourceType, string path)
        => new(id, sourceType, id, path, true, new FileInfo(path).Length, 1920, 1080, "primary", true, true);

    private static Task WriteJson<T>(string path, T value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Options));

    private static async Task<T> ReadJsonAsync<T>(string path)
        => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Options)!;

    private sealed class StaticWeeklyPipelineRunDirectoryResolver(string root) : IWeeklyPipelineRunDirectoryResolver
    {
        public Task<string> ResolveRunDirectoryAsync(Guid pipelineRunId) => Task.FromResult(root);
    }
}
