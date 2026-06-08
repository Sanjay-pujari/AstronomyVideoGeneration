using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionDrivenImagePromptGenerator : IQuestionDrivenImagePromptGenerator
{
    public string GeneratePrompt(QuestionDrivenImagePromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sceneMood = request.QuestionType.ToLowerInvariant() switch
        {
            "what" => "hero opening sky over Udaipur with a cinematic western twilight glow, elegant astronomy documentary poster mood, distinct desert-dune silhouette",
            "where" => "western horizon location guide at dusk over Rajasthan, observation-chart background with measured horizon, subtle sky grid, optional reference-star constellation placeholder, open negative space for a compass, West marker, and planet labels",
            "when" => "sunset-to-viewing-time timeline background, fading into early evening blue, warm horizon glow with room for a 7:23 PM IST marker",
            "how" => "practical stargazing how-to guide atmosphere, unobstructed western sky, polished spotting-frame layout, low horizon reference, room for arrows and three observing steps",
            "why" => "poetic close bright pairing and astronomy significance mood, deepening blue sky, visual space for brightness scale, comparison strip, and closeness indicator",
            "action" => "emotional cinematic closing astronomy poster over Udaipur, peaceful western horizon, warm inviting atmosphere for a minimal call to action",
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
            "No text, no planets, no labels, no arrows, no diagrams, no title cards, no watermarks, no UI, no filenames, no debug markings.",
            planetInstruction,
            "Leave clean space for foreground educational overlays. High-quality, polished, non-generic, scene-specific mood."
        });
    }
}
