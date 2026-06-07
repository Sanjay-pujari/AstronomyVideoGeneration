using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class QuestionDrivenImagePromptGenerator : IQuestionDrivenImagePromptGenerator
{
    public string GeneratePrompt(QuestionDrivenImagePromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sceneMood = request.QuestionType.ToLowerInvariant() switch
        {
            "what" => "hero opening twilight over Udaipur, cinematic western sky, elegant astronomy documentary mood",
            "where" => "clear western horizon at dusk over Rajasthan, open negative space for a compass and labels",
            "when" => "sunset fading into early evening blue, calm timeline-friendly sky gradient, warm horizon glow",
            "how" => "practical stargazing guide atmosphere, unobstructed western sky and simple landscape silhouette",
            "why" => "poetic close-pairing astronomy mood, deepening blue sky, subtle sense of wonder",
            "action" => "beautiful closing evening sky over Udaipur, peaceful horizon, warm inviting atmosphere",
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
