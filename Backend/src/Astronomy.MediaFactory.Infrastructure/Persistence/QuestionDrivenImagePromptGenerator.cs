using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionDrivenImagePromptGenerator : IQuestionDrivenImagePromptGenerator
{
    public string GeneratePrompt(QuestionDrivenImagePromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sceneMood = request.QuestionType.ToLowerInvariant() switch
        {
            "what" => "golden twilight western sky over Udaipur with orange horizon glow, premium astronomy magazine cover mood, no grid or chart background",
            "where" => "clean educational western-horizon observation chart sky over Rajasthan, subtle sky grid, lightweight Leo and Regulus reference-star constellation guide, not cinematic",
            "when" => "sunset-to-night twilight gradient, warm western horizon glow, open negative space for a hero 7:23 PM IST viewing timeline",
            "how" => "clean observer-friendly western sky, low horizon reference, minimal distractions, subtle reference-star hint only, room for arrow path and three floating observing steps",
            "why" => "deep editorial sky with subtle astronomy texture, visual space for a significance infographic comparing brightness and closeness",
            "action" => "most beautiful emotional twilight astronomy poster over Udaipur, peaceful warm western horizon, minimal call-to-action mood",
            _ => "clean astronomy documentary twilight background over a western horizon"
        };

        var planetInstruction = request.LocalPlanetAssetsAvailable
            ? "No text, no planets, no labels, no arrows, no diagrams, no title cards; Venus and Jupiter will be added with local transparent assets and programmatic overlays."
            : "No text, no planets, no labels, no arrows, no diagrams, no title cards; keep the background to sky, horizon, atmosphere, and landscape only.";

        return string.Join(' ', new[]
        {
            "Professional 16:9 astronomy production background only.",
            sceneMood + ".",
            "Include sky, horizon, atmosphere, and landscape only.",
            "No text, no planets, no labels, no arrows, no diagrams, no title cards, no card containers, no panel outlines, no helper layout boxes, no watermarks, no UI, no filenames, no debug markings.",
            planetInstruction,
            "Leave clean space for floating annotations, subtle leader lines, and soft glow callouts. High-quality, polished, non-generic, scene-specific mood."
        });
    }
}
