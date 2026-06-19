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

public enum MotionProfileType
{
    None = 0,
    SlowZoomIn = 1,
    SlowZoomOut = 2,
    PanLeft = 3,
    PanRight = 4,
    PushToObject = 5
}

public sealed record MotionProfile(
    MotionProfileKind Kind,
    MotionProfileType MotionType,
    MotionEasingKind Easing,
    double StartScale,
    double EndScale,
    double PanXStart,
    double PanXEnd,
    double PanYStart,
    double PanYEnd,
    string Description)
{
    public string SelectedMotion => MotionType.ToString();
    public string StartPosition => FormatPosition(PanXStart, PanYStart);
    public string EndPosition => FormatPosition(PanXEnd, PanYEnd);

    public string PanDirection => Math.Abs(PanXEnd - PanXStart) >= Math.Abs(PanYEnd - PanYStart)
        ? PanXEnd.CompareTo(PanXStart) switch { > 0 => "right", < 0 => "left", _ => "none" }
        : PanYEnd.CompareTo(PanYStart) switch { > 0 => "down", < 0 => "up", _ => "none" };

    public double PanStrength => Math.Max(Math.Abs(PanXEnd - PanXStart), Math.Abs(PanYEnd - PanYStart));

    public bool ValidationPassed => Easing == MotionEasingKind.EaseInOutSine
        && StartScale > 0d
        && EndScale > 0d
        && !double.IsNaN(StartScale)
        && !double.IsNaN(EndScale)
        && MotionType is MotionProfileType.None or MotionProfileType.SlowZoomIn or MotionProfileType.SlowZoomOut or MotionProfileType.PanLeft or MotionProfileType.PanRight or MotionProfileType.PushToObject;

    private static string FormatPosition(double x, double y) => $"{x:0.####},{y:0.####}";
}
