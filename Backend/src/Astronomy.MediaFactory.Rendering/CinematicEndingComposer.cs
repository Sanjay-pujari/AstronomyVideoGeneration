namespace Astronomy.MediaFactory.Rendering;

public sealed class CinematicEndingComposer
{
    public const double DefaultOutroDurationSeconds = 3.5d;

    public bool ShouldAppendOutro(RenderPlan plan)
        => plan.Scenes.Count > 0;

    public RenderPlanScene ComposeOutro(RenderPlan plan)
    {
        var source = plan.Scenes[^1];
        return new RenderPlanScene
        {
            Order = source.Order + 1,
            Caption = "Cinematic music-only outro",
            VisualPath = source.VisualPath,
            DurationSeconds = (int)Math.Ceiling(DefaultOutroDurationSeconds),
            Segment = "outro",
            SceneId = "cinematic-ending",
            SceneType = "Closing",
            ObjectName = source.ObjectName,
            NarrationLanguage = source.NarrationLanguage,
            NarrationText = string.Empty
        };
    }
}
