using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionDrivenImagePromptGenerator : IQuestionDrivenImagePromptGenerator
{
    public string GeneratePrompt(QuestionDrivenImagePromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sceneMood = request.QuestionType.ToLowerInvariant() switch
        {
            "what" => "hero opening sky over Udaipur with a cinematic western twilight glow, elegant astronomy documentary mood",
            "where" => "western horizon location guide at dusk over Rajasthan, open negative space for a compass, West marker, and planet labels",
            "when" => "sunset-to-viewing-time timeline background, fading into early evening blue, warm horizon glow with room for a 7:23 PM IST marker",
            "how" => "practical stargazing how-to guide atmosphere, unobstructed western sky, simple landscape silhouette, room for arrows and three observing steps",
            "why" => "poetic close bright pairing and astronomy significance mood, deepening blue sky, subtle sense of wonder",
            "action" => "emotional closing sky over Udaipur, peaceful western horizon, warm inviting atmosphere for a minimal call to action",
            _ => "clean astronomy documentary twilight background over a western horizon"
        };

        var planetInstruction = request.LocalPlanetAssetsAvailable
            ? "Do not paint Venus, Jupiter, dots, stars, labels, arrows, captions, title cards, or any readable text; those will be added with local assets and programmatic overlays."
            : "Do not add labels, arrows, captions, title cards, UI, metadata, or any readable text. Keep any natural sky objects subtle and non-specific.";

        return string.Join(' ', new[]
        {
            "Professional 16:9 astronomy production background only.",
            sceneMood + ".",
            "Include sky, horizon, atmosphere, and landscape only.",
            "No text, no labels, no watermarks, no UI, no diagrams, no filenames, no debug markings.",
            planetInstruction,
            "Leave clean space for foreground educational overlays. High-quality, polished, non-generic, scene-specific mood."
        });
    }
}
