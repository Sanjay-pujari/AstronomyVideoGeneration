namespace Astronomy.MediaFactory.Rendering;

public sealed class MotionProfileSelector
{
    public MotionProfile Select(RenderPlanScene scene, int sceneIndex, int sceneCount)
    {
        var role = $"{scene.SceneType} {scene.Segment} {scene.Caption}";
        if (sceneIndex == 0 || ContainsAny(role, "hook", "intro", "opening"))
        {
            return new MotionProfile(MotionProfileKind.Hook, MotionEasingKind.EaseOutCubic, 1.00d, 1.10d, 0d, 0d, 0d, 0d, "slow zoom in");
        }

        if (sceneIndex == sceneCount - 1 || ContainsAny(role, "closing", "outro", "cta", "calltoaction"))
        {
            return new MotionProfile(MotionProfileKind.Closing, MotionEasingKind.EaseInOutSine, 1.08d, 1.00d, 0d, 0d, 0d, 0d, "slow zoom out");
        }

        if (ContainsAny(role, "skyguide", "sky guide", "finder", "scan", "constellation", "stellarium"))
        {
            return new MotionProfile(MotionProfileKind.SkyGuide, MotionEasingKind.EaseInOutSine, 1.04d, 1.04d, -0.035d, 0.035d, 0d, 0d, "horizontal scan");
        }

        if (ContainsAny(role, "viewingtip", "viewing tip", "tip", "observe", "observation"))
        {
            return new MotionProfile(MotionProfileKind.ViewingTip, MotionEasingKind.EaseInOutSine, 1.03d, 1.03d, -0.015d, 0.015d, 0.01d, -0.01d, "very slow drift");
        }

        return new MotionProfile(MotionProfileKind.CauseExplanation, MotionEasingKind.EaseInOutSine, 1.04d, 1.04d, -0.025d, 0.025d, 0.02d, -0.02d, "slow diagonal drift");
    }

    public MotionProfile ClosingOutro() => new(MotionProfileKind.Closing, MotionEasingKind.EaseInOutSine, 1.08d, 1.00d, 0d, 0d, 0d, 0d, "music-only slow zoom out outro");

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
