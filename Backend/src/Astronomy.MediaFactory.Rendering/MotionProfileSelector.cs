namespace Astronomy.MediaFactory.Rendering;

public sealed class MotionProfileSelector
{
    private const double Center = 0d;
    private const double GentlePan = 0.018d;
    private const double SkyGuidePanStart = -0.03d;
    private const double SkyGuidePanEnd = 0.03d;

    public MotionProfile Select(RenderPlanScene scene, int sceneIndex, int sceneCount)
    {
        var role = $"{scene.SceneType} {scene.Segment} {scene.Caption} {scene.ObjectName}";

        // Motion Layer V2 first family: planetary conjunction videos use only smooth,
        // deterministic documentary motion.  Every profile starts from a stable centered
        // composition and uses EaseInOutSine so adjacent scene transitions do not reveal
        // a sudden camera snap, shake, bounce, rotation, or randomized movement.
        if (IsPlanetaryConjunction(role))
        {
            if (sceneIndex == 0 || ContainsAny(role, "hook", "intro", "opening"))
            {
                return new MotionProfile(MotionProfileKind.Hook, MotionProfileType.SlowZoomIn, MotionEasingKind.EaseInOutSine, 1.000d, 1.045d, Center, Center, Center, Center, "Motion Layer V2 conjunction opening slow zoom in");
            }

            if (sceneIndex == sceneCount - 1 || ContainsAny(role, "closing", "outro", "cta", "calltoaction"))
            {
                return new MotionProfile(MotionProfileKind.Closing, MotionProfileType.SlowZoomOut, MotionEasingKind.EaseInOutSine, 1.045d, 1.000d, Center, Center, Center, Center, "Motion Layer V2 conjunction closing slow zoom out");
            }

            if (ContainsAny(role, "skyguide", "sky guide", "finder", "scan", "constellation", "stellarium", "where to look"))
            {
                return new MotionProfile(MotionProfileKind.SkyGuide, MotionProfileType.PanRight, MotionEasingKind.EaseInOutSine, 1.040d, 1.080d, SkyGuidePanStart, SkyGuidePanEnd, Center, Center, "Motion Layer V2 conjunction accurate sky guide right pan");
            }

            if (ContainsAny(role, "viewingtip", "viewing tip", "tip", "observe", "observation", "safety", "safe"))
            {
                return new MotionProfile(MotionProfileKind.ViewingTip, MotionProfileType.PanLeft, MotionEasingKind.EaseInOutSine, 1.035d, 1.035d, GentlePan, -GentlePan, Center, Center, "Motion Layer V2 conjunction slow left pan");
            }

            return new MotionProfile(MotionProfileKind.CauseExplanation, MotionProfileType.PushToObject, MotionEasingKind.EaseInOutSine, 1.000d, 1.050d, Center, Center, Center, Center, "Motion Layer V2 conjunction smooth push to object");
        }

        return new MotionProfile(MotionProfileKind.CauseExplanation, MotionProfileType.None, MotionEasingKind.EaseInOutSine, 1.000d, 1.000d, Center, Center, Center, Center, "Motion Layer V2 fallback stable hold");
    }

    public MotionProfile ClosingOutro() => new(MotionProfileKind.Closing, MotionProfileType.SlowZoomOut, MotionEasingKind.EaseInOutSine, 1.045d, 1.000d, Center, Center, Center, Center, "Motion Layer V2 music-only slow zoom out outro");

    private static bool IsPlanetaryConjunction(string value)
        => ContainsAny(value, "planetary conjunction", "conjunction", "jupiter venus", "jupiter + venus", "jupiter and venus", "venus jupiter", "venus and jupiter");

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
