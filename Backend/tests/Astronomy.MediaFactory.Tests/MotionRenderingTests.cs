using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests;

public sealed class MotionRenderingTests
{
    [Fact]
    public void SmoothMotionRenderer_UsesMotionLayerV2SineEasingInterpolation()
    {
        var profile = new MotionProfile(MotionProfileKind.Hook, MotionProfileType.SlowZoomIn, MotionEasingKind.EaseInOutSine, 1.00d, 1.10d, 0d, 0d, 0d, 0d, "slow zoom in");
        var filter = new SmoothMotionRenderer().BuildZoomPanFilter(8d, 30, 1920, 1080, profile);

        Assert.Contains("zoompan=z='1+(0.10000000000000009)*((1-cos(PI*(on/239.0)))/2)'", filter, StringComparison.Ordinal);
        Assert.Contains(":d=240:", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("pzoom", filter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MotionProfileSelector_AssignsMotionLayerV2ProfilesForPlanetaryConjunction()
    {
        var selector = new MotionProfileSelector();
        var hook = selector.Select(new RenderPlanScene { Caption = "Jupiter and Venus conjunction hook", Segment = "intro", SceneType = "Hook" }, 0, 5);
        var skyGuide = selector.Select(new RenderPlanScene { Caption = "Jupiter Venus where to look", SceneType = "SkyGuide" }, 2, 5);
        var closing = selector.Select(new RenderPlanScene { Caption = "Planetary conjunction closing", SceneType = "Closing" }, 4, 5);

        Assert.Equal(MotionProfileKind.Hook, hook.Kind);
        Assert.Equal(MotionProfileType.SlowZoomIn, hook.MotionType);
        Assert.Equal(MotionEasingKind.EaseInOutSine, hook.Easing);
        Assert.Equal(1.00d, hook.StartScale);
        Assert.Equal(1.045d, hook.EndScale);
        Assert.True(hook.ValidationPassed);
        Assert.Equal(MotionProfileKind.SkyGuide, skyGuide.Kind);
        Assert.Equal(MotionProfileType.PanRight, skyGuide.MotionType);
        Assert.Equal(MotionEasingKind.EaseInOutSine, skyGuide.Easing);
        Assert.Equal(1.04d, skyGuide.StartScale);
        Assert.Equal(1.08d, skyGuide.EndScale);
        Assert.Equal(-0.03d, skyGuide.PanXStart);
        Assert.Equal(0.03d, skyGuide.PanXEnd);
        Assert.Equal("right", skyGuide.PanDirection);
        Assert.Equal(MotionProfileKind.Closing, closing.Kind);
        Assert.Equal(MotionProfileType.SlowZoomOut, closing.MotionType);
        Assert.Equal(1.045d, closing.StartScale);
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
