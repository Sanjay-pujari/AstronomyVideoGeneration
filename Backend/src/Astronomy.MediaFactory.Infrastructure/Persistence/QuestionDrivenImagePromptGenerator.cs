using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionDrivenImagePromptGenerator : IQuestionDrivenImagePromptGenerator
{
    public string GeneratePrompt(QuestionDrivenImagePromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isMeteorShower = ContainsMeteorContext(request.VisualIntent) || ContainsMeteorContext(request.ImagePromptIntent);
        var sceneMood = isMeteorShower ? request.QuestionType.ToLowerInvariant() switch
        {
            "what" => "meteor shower peak-night alert, cinematic dark night sky over Udaipur India, multiple meteor streaks, subtle Gemini radiant constellation hint, premium astronomy magazine cover mood",
            "where" => "dark open sky viewing guide over Udaipur, east-to-overhead sky orientation, meteor streaks, subtle Gemini radiant marker, low light pollution context",
            "when" => "midnight to pre-dawn meteor shower timing visual, dark blue-black sky, 00:00 to 05:00 IST night window mood, meteor activity increasing toward dawn",
            "how" => "observer-friendly meteor shower scene, person reclining under dark open sky, no telescope, city lights avoided, eyes adapting, meteor streaks overhead",
            "why" => "premium editorial meteor shower sky with abundant meteor streaks, strong annual meteor shower mood, low moon interference, subtle radiant from Gemini",
            "action" => "inspirational save-date meteor shower poster mood, Udaipur night landscape silhouette, meteor streaks overhead, Dec 13/14 reminder energy",
            _ => "clean dark night meteor shower sky over India with meteor streaks and subtle constellation context"
        } : request.QuestionType.ToLowerInvariant() switch
        {
            "what" => "professional astronomy magazine cover, richer western twilight over Rajasthan, natural atmospheric glow, stronger golden-orange horizon, subtle atmospheric haze, soft vignette feel, slightly denser natural starfield, documentary style, stronger horizon glow and focal contrast for a clickable astronomy thumbnail",
            "where" => "observation chart sky over Rajasthan with a subtle real western horizon, astronomy guide aesthetic, delicate altitude grid, lightweight Leo and Regulus reference-star constellation guide, documentary not graphic design",
            "when" => "real twilight transition from warm sunset colors into early night, natural western horizon glow, open negative space for a hero 7:23 PM IST viewing timeline",
            "how" => "observer-friendly western sky with natural atmospheric depth, low real horizon reference, minimal distractions, subtle reference-star hint only, room for arrow path and three floating observing steps",
            "why" => "deep astronomy sky, premium editorial background, natural starfield variation, emotional shared-evening-sky atmosphere, subtle shared glow region, visual relationship and closeness between the two brightest worlds sharing the evening sky",
            "action" => "most beautiful poster-quality twilight over Udaipur, richer cinematic warm western horizon, subtle haze, atmospheric depth, stronger landscape silhouette, premium astronomy artwork composition, shareable minimal call-to-action mood",
            _ => "clean astronomy documentary twilight background over a western horizon"
        };

        var planetInstruction = isMeteorShower
            ? "No Venus, no Jupiter, no unrelated planets, no conjunction visuals; include meteor streaks, dark night sky, and optional subtle Gemini radiant only."
            : request.LocalPlanetAssetsAvailable
                ? "No text, no planets, no labels, no arrows, no diagrams, no title cards; Venus and Jupiter will be added with local transparent assets and programmatic overlays."
                : "No text, no planets, no labels, no arrows, no diagrams, no title cards; keep the background to sky, horizon, atmosphere, and landscape only.";

        return string.Join(' ', new[]
        {
            "Professional 16:9 astronomy photography art-direction background only.",
            sceneMood + ".",
            isMeteorShower ? "Include dark night sky, meteor streaks, atmospheric depth, Udaipur/India viewing context, and subtle Gemini radiant or constellation hint." : "Include sky, horizon, atmosphere, and landscape only.",
            "No text, no planets, no labels, no arrows, no diagrams, no title cards, no card containers, no panel outlines, no helper layout boxes, no watermarks, no UI, no filenames, no debug markings.",
            planetInstruction,
            "Leave clean space for floating annotations, subtle leader lines, and soft glow callouts. High-quality, polished, realistic astronomy photography style, non-generic, scene-specific mood."
        });
    }

    private static bool ContainsMeteorContext(string? value)
        => !string.IsNullOrWhiteSpace(value) && (value.Contains("meteor", StringComparison.OrdinalIgnoreCase) || value.Contains("radiant", StringComparison.OrdinalIgnoreCase));
}
