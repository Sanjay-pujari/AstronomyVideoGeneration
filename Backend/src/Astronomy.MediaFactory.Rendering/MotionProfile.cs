namespace Astronomy.MediaFactory.Rendering;

public enum MotionProfileKind
{
    Hook = 0,
    CauseExplanation = 1,
    SkyGuide = 2,
    ViewingTip = 3,
    Closing = 4
}

public enum MotionEasingKind
{
    EaseOutCubic = 0,
    EaseInOutSine = 1
}

public sealed record MotionProfile(
    MotionProfileKind Kind,
    string SelectedMotion,
    MotionEasingKind Easing,
    double StartScale,
    double EndScale,
    double PanXStart,
    double PanXEnd,
    double PanYStart,
    double PanYEnd,
    string Description)
{
    public string PanDirection => Math.Abs(PanXEnd - PanXStart) >= Math.Abs(PanYEnd - PanYStart)
        ? PanXEnd.CompareTo(PanXStart) switch { > 0 => "right", < 0 => "left", _ => "none" }
        : PanYEnd.CompareTo(PanYStart) switch { > 0 => "down", < 0 => "up", _ => "none" };

    public double PanStrength => Math.Max(Math.Abs(PanXEnd - PanXStart), Math.Abs(PanYEnd - PanYStart));
}
