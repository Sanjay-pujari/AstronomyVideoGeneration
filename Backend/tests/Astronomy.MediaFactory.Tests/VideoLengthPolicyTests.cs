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
                Scene("sky-overview", "Overview", "Sky"),
                Scene("special-event-highlight", "SpecialEventHighlight", "Meteor shower"),
                Scene("object-1", "Planet", "Jupiter"),
                Scene("object-2", "Planet", "Saturn"),
                Scene("object-3", "Planet", "Mars"),
                Scene("object-4", "Planet", "Venus"),
                Scene("object-5", "Planet", "Mercury"),
                Scene("object-6", "DeepSky", "Orion Nebula"),
                Scene("closing", "Closing", "Sky")
            ]
        };

        var result = PipelineOrchestrator.ApplyVideoLengthPolicy(context, new VideoLengthPolicyOptions
        {
            MaxFullVideoDurationSeconds = 420,
            MaxFullVideoSegments = 8,
            MaxObjectsInFullVideo = 5
        }, estimatedDurationSeconds: 540);

        Assert.Equal(9, result.OriginalSegmentCount);
        Assert.True(result.FinalSegmentCount <= 8);
        Assert.DoesNotContain(context.SceneObservationContexts, scene => scene.SceneId == "object-6");
        Assert.Contains(context.SceneObservationContexts, scene => scene.SceneId == "sky-overview");
        Assert.Contains(context.SceneObservationContexts, scene => scene.SceneId == "special-event-highlight");
        Assert.Contains(context.SceneObservationContexts, scene => scene.SceneId == "closing");
    }

    private static SceneObservationContext Scene(string sceneId, string sceneType, string objectName) => new()
    {
        SceneId = sceneId,
        SceneTitle = sceneId,
        SceneType = sceneType,
        ObjectName = objectName,
        ObjectType = sceneType
    };
}
