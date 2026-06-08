using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionDrivenImagePromptGenerator : IQuestionDrivenImagePromptGenerator
{
    public string GeneratePrompt(QuestionDrivenImagePromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sceneMood = request.QuestionType.ToLowerInvariant() switch
        {
            "what" => "professional astronomy magazine cover, richer western twilight over Rajasthan, natural atmospheric glow, stronger golden-orange horizon, subtle atmospheric haze, soft vignette feel, slightly denser natural starfield, documentary style, stronger horizon glow and focal contrast for a clickable astronomy thumbnail",
            "where" => "observation chart sky over Rajasthan with a subtle real western horizon, astronomy guide aesthetic, delicate altitude grid, lightweight Leo and Regulus reference-star constellation guide, documentary not graphic design",
            "when" => "real twilight transition from warm sunset colors into early night, natural western horizon glow, open negative space for a hero 7:23 PM IST viewing timeline",
            "how" => "observer-friendly western sky with natural atmospheric depth, low real horizon reference, minimal distractions, subtle reference-star hint only, room for arrow path and three floating observing steps",
            "why" => "deep astronomy sky, premium editorial background, natural starfield variation, emotional shared-evening-sky atmosphere, subtle shared glow region, visual relationship and closeness between the two brightest worlds sharing the evening sky",
            "action" => "most beautiful poster-quality twilight over Udaipur, richer cinematic warm western horizon, subtle haze, atmospheric depth, stronger landscape silhouette, premium astronomy artwork composition, shareable minimal call-to-action mood",
            _ => "clean astronomy documentary twilight background over a western horizon"
        };

        var planetInstruction = request.LocalPlanetAssetsAvailable
            ? "No text, no planets, no labels, no arrows, no diagrams, no title cards; Venus and Jupiter will be added with local transparent assets and programmatic overlays."
            : "No text, no planets, no labels, no arrows, no diagrams, no title cards; keep the background to sky, horizon, atmosphere, and landscape only.";

        return string.Join(' ', new[]
        {
            "Professional 16:9 astronomy photography art-direction background only.",
            sceneMood + ".",
            "Include sky, horizon, atmosphere, and landscape only.",
            "No text, no planets, no labels, no arrows, no diagrams, no title cards, no card containers, no panel outlines, no helper layout boxes, no watermarks, no UI, no filenames, no debug markings.",
            planetInstruction,
            "Leave clean space for floating annotations, subtle leader lines, and soft glow callouts. High-quality, polished, realistic astronomy photography style, non-generic, scene-specific mood."
        });
    }
}
