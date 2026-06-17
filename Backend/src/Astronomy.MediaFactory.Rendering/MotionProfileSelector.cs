namespace Astronomy.MediaFactory.Rendering;

public sealed class MotionProfileSelector
{
    public MotionProfile Select(RenderPlanScene scene, int sceneIndex, int sceneCount)
    {
        var role = $"{scene.SceneType} {scene.Segment} {scene.Caption} {scene.ObjectName}";
        if (sceneIndex == 0 || ContainsAny(role, "hook", "intro", "opening"))
        {
            var motion = ContainsAny(role, "solareclipse", "solar eclipse", "eclipse", "corona") ? "CoronaReveal" : "StrongZoomIn";
            return new MotionProfile(MotionProfileKind.Hook, motion, MotionEasingKind.EaseOutCubic, 1.00d, 1.15d, 0d, 0d, 0d, 0d, "cold-open cinematic reveal");
        }

        if (sceneIndex == sceneCount - 1 || ContainsAny(role, "closing", "outro", "cta", "calltoaction"))
        {
            return new MotionProfile(MotionProfileKind.Closing, "SlowZoomOut", MotionEasingKind.EaseInOutSine, 1.10d, 1.00d, 0d, 0d, 0d, 0d, "smooth closing zoom out");
        }

        if (ContainsAny(role, "skyguide", "sky guide", "finder", "scan", "constellation", "stellarium", "where to look"))
        {
            return new MotionProfile(MotionProfileKind.SkyGuide, "HorizontalScan", MotionEasingKind.EaseInOutSine, 1.04d, 1.04d, -0.06d, 0.06d, 0d, 0d, "visible horizontal sky scan");
        }

        if (ContainsAny(role, "viewingtip", "viewing tip", "tip", "observe", "observation", "safety", "safe"))
        {
            return new MotionProfile(MotionProfileKind.ViewingTip, "SlowDrift", MotionEasingKind.EaseInOutSine, 1.02d, 1.06d, -0.025d, 0.025d, 0.02d, -0.02d, "gentle viewing-tip drift");
        }

        return new MotionProfile(MotionProfileKind.CauseExplanation, "DiagonalDrift", MotionEasingKind.EaseInOutSine, 1.00d, 1.08d, -0.03d, 0.03d, 0.02d, -0.02d, "cause/explanation diagonal drift");
    }

    public MotionProfile ClosingOutro() => new(MotionProfileKind.Closing, "SlowZoomOut", MotionEasingKind.EaseInOutSine, 1.10d, 1.00d, 0d, 0d, 0d, 0d, "music-only slow zoom out outro");

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
