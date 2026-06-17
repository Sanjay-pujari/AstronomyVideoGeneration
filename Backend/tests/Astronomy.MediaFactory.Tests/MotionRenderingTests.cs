using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests;

public sealed class MotionRenderingTests
{
    [Fact]
    public void SmoothMotionRenderer_UsesFrameBasedEasingInterpolation()
    {
        var profile = new MotionProfile(MotionProfileKind.Hook, MotionEasingKind.EaseOutCubic, 1.00d, 1.10d, 0d, 0d, 0d, 0d, "slow zoom in");
        var filter = new SmoothMotionRenderer().BuildZoomPanFilter(8d, 30, 1920, 1080, profile);

        Assert.Contains("zoompan=z='1+(0.1)*(1-pow(1-(on/239.0),3))'", filter, StringComparison.Ordinal);
        Assert.Contains(":d=240:", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("pzoom", filter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MotionProfileSelector_AssignsRc1ProfilesBySceneRole()
    {
        var selector = new MotionProfileSelector();
        var hook = selector.Select(new RenderPlanScene { Caption = "Hook", Segment = "intro", SceneType = "Hook" }, 0, 5);
        var skyGuide = selector.Select(new RenderPlanScene { Caption = "Scan the sky", SceneType = "SkyGuide" }, 2, 5);
        var closing = selector.Select(new RenderPlanScene { Caption = "Closing", SceneType = "Closing" }, 4, 5);

        Assert.Equal(MotionProfileKind.Hook, hook.Kind);
        Assert.Equal(MotionEasingKind.EaseOutCubic, hook.Easing);
        Assert.Equal(1.00d, hook.StartScale);
        Assert.Equal(1.10d, hook.EndScale);
        Assert.Equal(MotionProfileKind.SkyGuide, skyGuide.Kind);
        Assert.Equal("right", skyGuide.PanDirection);
        Assert.Equal(MotionProfileKind.Closing, closing.Kind);
        Assert.Equal(1.08d, closing.StartScale);
        Assert.Equal(1.00d, closing.EndScale);
    }

    [Fact]
    public void CinematicEndingComposer_AppendsMusicOnlyClosingOutro()
    {
        var plan = new RenderPlan
        {
            Scenes =
            [
                new RenderPlanScene { Order = 1, VisualPath = "/tmp/closing.png", Segment = "scene", SceneType = "ViewingTip" }
            ]
        };

        var outro = new CinematicEndingComposer().ComposeOutro(plan);

        Assert.Equal("cinematic-ending", outro.SceneId);
        Assert.Equal("Closing", outro.SceneType);
        Assert.Equal("outro", outro.Segment);
        Assert.Equal(4, outro.DurationSeconds);
    }
}
