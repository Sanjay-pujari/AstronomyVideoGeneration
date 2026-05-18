using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class VideoLengthPolicyTests
{
    [Fact]
    public void ApplyVideoLengthPolicy_TrimsOptionalObjects_WhenEstimatedDurationExceedsLimit()
    {
        var context = new AstronomyContext
        {
            SceneObservationContexts =
            [
                Scene("sky-overview", "Overview", "Sky", duration: 18),
                Scene("special-event-highlight", "SpecialEventHighlight", "Meteor shower", duration: 30, isMajorSpecialEvent: true, score: 100),
                Scene("object-1", "Planet", "Jupiter", duration: 36, score: 95),
                Scene("object-2", "Planet", "Saturn", duration: 36, score: 90),
                Scene("object-3", "Planet", "Mars", duration: 36, score: 86),
                Scene("object-4", "Planet", "Venus", duration: 36, score: 94),
                Scene("object-5", "Planet", "Mercury", duration: 36, score: 70),
                Scene("object-6", "DeepSky", "Orion Nebula", duration: 30, score: 55),
                Scene("closing", "Closing", "Sky", duration: 12)
            ]
        };

        var result = PipelineOrchestrator.ApplyVideoLengthPolicy(context, Policy(), estimatedDurationSeconds: 540);

        Assert.Equal(9, result.OriginalSegmentCount);
        Assert.True(result.FinalSegmentCount <= 8);
        Assert.DoesNotContain(context.SceneObservationContexts, scene => scene.SceneId == "object-6");
        Assert.Contains(context.SceneObservationContexts, scene => scene.SceneId == "sky-overview");
        Assert.Contains(context.SceneObservationContexts, scene => scene.SceneId == "special-event-highlight");
        Assert.Contains(context.SceneObservationContexts, scene => scene.SceneId == "closing");
    }

    [Fact]
    public void ApplyVideoLengthPolicy_LongVideoCanKeepFiveObjectsAndSevenScenes()
    {
        var context = new AstronomyContext
        {
            SceneObservationContexts =
            [
                Scene("sky-overview", "Overview", "Sky", duration: 18),
                Scene("object-1", "Planet", "Jupiter", duration: 36, score: 95),
                Scene("object-2", "Planet", "Venus", duration: 36, score: 94),
                Scene("object-3", "Planet", "Saturn", duration: 36, score: 90),
                Scene("object-4", "Planet", "Mars", duration: 36, score: 86),
                Scene("object-5", "Moon", "Moon", duration: 36, score: 89),
                Scene("closing", "Closing", "Sky", duration: 12)
            ]
        };

        var result = PipelineOrchestrator.ApplyVideoLengthPolicy(context, Policy(), estimatedDurationSeconds: 210);

        Assert.Equal(7, result.FinalSegmentCount);
        Assert.Equal(5, context.SceneObservationContexts.Count(scene => scene.ObjectName != "Sky"));
        Assert.Empty(result.TrimmedSceneIds);
    }

    [Fact]
    public void ApplyVideoLengthPolicy_NarrationSceneOrderRemainsFinalSceneOrder()
    {
        var context = new AstronomyContext
        {
            SceneObservationContexts =
            [
                Scene("sky-overview", "Overview", "Sky", duration: 18),
                Scene("object-1", "Planet", "Jupiter", duration: 36, score: 95),
                Scene("object-2", "Planet", "Venus", duration: 36, score: 94),
                Scene("object-3", "Planet", "Saturn", duration: 36, score: 90),
                Scene("object-4", "Planet", "Mars", duration: 36, score: 86),
                Scene("special-event-highlight", "SpecialEventHighlight", "Meteor shower", duration: 30, isMajorSpecialEvent: true, score: 100),
                Scene("closing", "Closing", "Sky", duration: 12)
            ]
        };

        PipelineOrchestrator.ApplyVideoLengthPolicy(context, Policy(), estimatedDurationSeconds: 204);

        Assert.Equal(new[] { "Sky", "Jupiter", "Venus", "Saturn", "Mars", "Meteor shower", "Sky" }, context.SceneObservationContexts.Select(scene => scene.ObjectName));
        Assert.Equal(Enumerable.Range(1, 7), context.SceneObservationContexts.Select(scene => scene.SceneIndex));
    }

    [Fact]
    public void ApplyVideoLengthPolicy_DurationRemainsInThreeToFourMinuteTargetRange()
    {
        var context = new AstronomyContext
        {
            SceneObservationContexts =
            [
                Scene("sky-overview", "Overview", "Sky", duration: 18),
                Scene("object-1", "Planet", "Jupiter", duration: 36, score: 95),
                Scene("object-2", "Planet", "Venus", duration: 36, score: 94),
                Scene("object-3", "Planet", "Saturn", duration: 36, score: 90),
                Scene("object-4", "Planet", "Mars", duration: 36, score: 86),
                Scene("object-5", "Moon", "Moon", duration: 36, score: 89),
                Scene("closing", "Closing", "Sky", duration: 12)
            ]
        };

        var result = PipelineOrchestrator.ApplyVideoLengthPolicy(context, Policy(), estimatedDurationSeconds: 210);

        Assert.InRange(result.EstimatedDurationSeconds, 180, 240);
    }

    [Fact]
    public void ApplyVideoLengthPolicy_TrimsLowestPriorityOptionalObjectFirst()
    {
        var context = new AstronomyContext
        {
            SceneObservationContexts =
            [
                Scene("sky-overview", "Overview", "Sky", duration: 18),
                Scene("object-1", "Planet", "Jupiter", duration: 80, score: 95),
                Scene("object-2", "Planet", "Venus", duration: 80, score: 94),
                Scene("object-3", "Planet", "Saturn", duration: 80, score: 90),
                Scene("object-4", "Planet", "Mars", duration: 80, score: 86),
                Scene("object-5", "DeepSky", "Dim Galaxy", duration: 80, score: 20),
                Scene("closing", "Closing", "Sky", duration: 12)
            ]
        };

        var result = PipelineOrchestrator.ApplyVideoLengthPolicy(context, Policy(maxDuration: 300), estimatedDurationSeconds: 412);

        Assert.Contains("object-5", result.TrimmedSceneIds);
        Assert.DoesNotContain(context.SceneObservationContexts, scene => scene.ObjectName == "Dim Galaxy");
        Assert.Contains(context.SceneObservationContexts, scene => scene.ObjectName == "Jupiter");
    }

    [Fact]
    public void ApplyVideoLengthPolicy_OpeningAndClosingAlwaysPreserved()
    {
        var context = new AstronomyContext
        {
            SceneObservationContexts =
            [
                Scene("sky-overview", "Overview", "Sky", duration: 18),
                Scene("object-1", "Planet", "Jupiter", duration: 200, score: 95),
                Scene("object-2", "Planet", "Mercury", duration: 200, score: 40),
                Scene("closing", "Closing", "Sky", duration: 12)
            ]
        };

        PipelineOrchestrator.ApplyVideoLengthPolicy(context, Policy(maxDuration: 180), estimatedDurationSeconds: 430);

        Assert.Equal("sky-overview", context.SceneObservationContexts.First().SceneId);
        Assert.Equal("closing", context.SceneObservationContexts.Last().SceneId);
    }

    [Fact]
    public void ApplySafeRenderGuard_TrimsOptionalSceneButKeepsShortsLogicIndependent()
    {
        var manifest = new RenderManifest
        {
            Scenes =
            [
                RenderScene("sky-overview", "Overview", "Sky", 18),
                RenderScene("object-1", "Planet", "Jupiter", 220),
                RenderScene("object-2", "DeepSky", "Dim Galaxy", 70),
                RenderScene("closing", "Closing", "Sky", 12)
            ]
        };

        var policy = Policy(maxDuration: 420);
        policy.SafeRenderCostThreshold = 500;

        var result = PipelineOrchestrator.ApplySafeRenderGuard(manifest, policy);

        Assert.Contains("object-2", result.TrimmingActions);
        Assert.Equal(new[] { "sky-overview", "object-1", "closing" }, manifest.Scenes.Select(scene => scene.SceneId));
    }

    private static VideoLengthPolicyOptions Policy(int maxDuration = 420) => new()
    {
        MinPrimaryObjects = 3,
        TargetPrimaryObjects = 5,
        MaxPrimaryObjects = 5,
        IncludeOpeningOverview = true,
        IncludeClosingOverview = true,
        TargetFullVideoSegments = 7,
        MaxFullVideoSegments = 8,
        MinFullVideoDurationSeconds = 180,
        TargetFullVideoDurationSeconds = 240,
        MaxFullVideoDurationSeconds = maxDuration
    };

    private static SceneObservationContext Scene(string sceneId, string sceneType, string objectName, int duration = 36, double score = 0, bool isMajorSpecialEvent = false) => new()
    {
        SceneId = sceneId,
        SceneTitle = sceneId,
        SceneType = sceneType,
        ObjectName = objectName,
        ObjectType = sceneType,
        EstimatedDurationSeconds = duration,
        FinalScore = score,
        IsOptionalForLongForm = objectName != "Sky" && !isMajorSpecialEvent,
        IsMajorSpecialEvent = isMajorSpecialEvent
    };

    private static RenderScene RenderScene(string sceneId, string sceneType, string objectName, int duration) => new()
    {
        SceneId = sceneId,
        SceneType = sceneType,
        ObjectName = objectName,
        ObjectType = sceneType,
        DurationSeconds = duration
    };
}
