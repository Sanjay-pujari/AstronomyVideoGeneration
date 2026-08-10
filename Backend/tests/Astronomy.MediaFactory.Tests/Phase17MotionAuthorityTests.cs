using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase17MotionAuthorityTests
{
    [Fact]
    public void SemanticSelector_ReusesDeterministicMaturePolicy()
    {
        var selector = new MotionProfileSelector();

        var first = selector.SelectSemantic("planetary conjunction hook", 0, 4);
        var repeated = selector.SelectSemantic("planetary conjunction hook", 0, 4);

        Assert.Equal(first, repeated);
        Assert.Equal(MotionProfileType.SlowZoomIn, first.MotionType);
        Assert.Equal(MotionEasingKind.EaseInOutSine, first.Easing);
    }

    [Fact]
    public void StaticFallback_IsAClosedValidProductionMotion()
    {
        Assert.Contains(Phase17MotionType.Static, Enum.GetValues<Phase17MotionType>());
        Assert.DoesNotContain(Enum.GetNames<Phase17MotionType>(), value =>
            value is "Parallax" or "Orbit" or "Tilt");
    }
}
