using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyInfographicDesignSystem : IAstronomyInfographicDesignSystem
{
    private static readonly string[] CommonForbiddenPatterns =
    [
        "PowerPoint slide layout",
        "card/panel/large bounding rectangle composition",
        "helper layout box",
        "large text box",
        "fake circle planets",
        "decorative translucent circles",
        "Canva-style background circles",
        "template helper circles",
        "solid dark planet backing circles",
        "debug/internal/path/GUID text",
        "text-dominant image"
    ];

    public AstronomyInfographicDesignTemplate CreateTemplate(AstronomyInfographicDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var questionType = request.QuestionType.ToLowerInvariant();
        return questionType switch
        {
            "what" => new(
                "magazine-cover-cinematic-twilight",
                "WHAT — Astronomy Magazine Cover",
                "YouTube-quality astronomy thumbnail with golden western twilight, dramatic horizon glow, large Venus/Jupiter focal point, and premium title typography.",
                0.25,
                0.75,
                ["golden western twilight", "dramatic horizon glow", "large Venus/Jupiter focal point", "premium typography", "title Venus & Jupiter", "subtitle After sunset", "no grid or chart background"],
                CommonForbiddenPatterns,
                new("Venus & Jupiter", "After sunset", ["Venus", "Jupiter"], [], ["Venus", "Jupiter"], [], [], []),
                ["background:golden western twilight atmospheric gradient", "horizon:dramatic warm western horizon glow", "texture:twilight haze and subtle sky grain", "vignette:natural edge falloff", "celestial:large local transparent Venus and Jupiter focal point integrated with subtle glow", "typography:premium thumbnail title Venus & Jupiter subtitle After sunset"],
                [request.AccessibilityIntent, "Small labels identify Venus and Jupiter without covering the sky."]),
            "where" => new(
                "western-horizon-observation-chart",
                "WHERE — Observation Chart",
                "Clean educational observation chart answering where to look with a western horizon line, subtle sky grid, Leo/Regulus reference-star guide, West marker, altitude cue, and plotted Venus/Jupiter positions.",
                0.25,
                0.75,
                ["western horizon", "horizon line", "West marker", "Venus/Jupiter positions", "altitude hint", "subtle sky grid", "Leo/Regulus reference-star guide"],
                CommonForbiddenPatterns,
                new("Where to Look", "Western horizon", ["West", "Venus", "Jupiter", "Horizon", "Leo / Regulus reference stars"], ["western horizon altitude guide"], ["Venus", "Jupiter"], ["West"], [], []),
                ["background:clean western observation chart", "horizon:measured western horizon line", "celestial:local Venus/Jupiter plotted positions", "reference:subtle sky grid", "reference:Leo Regulus constellation-star guide", "direction:West marker", "annotation:floating altitude hint"],
                [request.AccessibilityIntent, "West marker and horizon line provide orientation for Udaipur viewers."]),
            "when" => new(
                "twilight-viewing-window-timeline",
                "WHEN — Timeline Infographic",
                "Timeline infographic with sunset, 7:23 PM IST, and an after-sunset viewing window embedded in a twilight-to-night gradient.",
                0.25,
                0.75,
                ["sunset marker", "7:23 PM IST marker", "viewing window", "twilight-to-night gradient"],
                CommonForbiddenPatterns,
                new("Best Time Tonight", "Viewing window", ["Sunset", "Viewing window"], [], [], [], ["7:23 PM IST"], []),
                ["background:twilight-to-night gradient", "horizon:sunset band", "time:sunset marker", "time:7:23 PM IST marker", "direction:after-sunset viewing window", "layout:horizontal timeline"],
                [request.AccessibilityIntent, "Timeline markers are large enough to read while the gradient remains dominant."]),
            "how" => new(
                "field-step-guide-arrow-path",
                "HOW — Step Guide",
                "Field step guide showing the observing sequence: find Venus, look nearby for Jupiter, and face west, with arrows from Venus to Jupiter.",
                0.25,
                0.75,
                ["Step 1 Find Venus", "Step 2 Look nearby for Jupiter", "Step 3 Face west", "arrows from Venus to Jupiter", "West marker"],
                CommonForbiddenPatterns,
                new("How to Find It", "Use Venus as your anchor", ["Venus", "Jupiter", "West"], ["arrow from Venus to Jupiter", "arrow toward western horizon"], ["Venus", "Jupiter"], ["West"], [], ["Find Venus", "Look nearby for Jupiter", "Face west"]),
                ["background:clean observer-friendly western sky", "horizon:west reference", "celestial:local Venus/Jupiter assets integrated with subtle glow", "reference:subtle reference-star hint", "direction:arrow from Venus to Jupiter", "steps:three floating labels", "layout:diagonal field guide"],
                [request.AccessibilityIntent, "Numbered steps and arrows encode the observing path."]),
            "why" => new(
                "close-pair-significance-bracket",
                "WHY — Significance Graphic",
                "Human-interest significance visual: two of the brightest worlds sharing the evening sky, emphasizing brightness, closeness, shared sky, and emotional meaning.",
                0.25,
                0.75,
                ["two of the brightest worlds sharing the evening sky", "brightness", "closeness", "shared sky", "emotional significance"],
                CommonForbiddenPatterns,
                new("Why It Matters", "Two of the brightest worlds sharing the evening sky", ["Venus", "Jupiter", "brightness", "closeness", "shared sky"], ["closeness bracket", "brightness comparison"], ["Venus", "Jupiter"], [], [], []),
                ["background:deep editorial shared-sky atmospheric texture", "celestial:two of the brightest worlds sharing evening sky integrated with subtle glow", "significance:brightness closeness shared sky emotional significance", "comparison:brightness feel", "direction:closeness bracket", "annotation:floating human-interest significance line"],
                [request.AccessibilityIntent, "Bracket shows the planets' apparent closeness."]),
            "action" => new(
                "minimal-closing-astronomy-poster",
                "ACTION — Astronomy Poster",
                "Beautiful closing astronomy poster with Venus/Jupiter together and only a minimal call to action.",
                0.25,
                0.75,
                ["beautiful emotional twilight", "warm horizon", "Venus/Jupiter naturally integrated", "minimal CTA Step Outside Tonight Look west", "poster quality"],
                CommonForbiddenPatterns,
                new("Step Outside Tonight", "Look west", ["Venus", "Jupiter"], [], ["Venus", "Jupiter"], ["West"], [], []),
                ["background:beautiful emotional twilight poster atmospheric gradient", "horizon:warm peaceful western horizon", "celestial:local Venus and Jupiter naturally integrated with subtle glow", "typography:minimal CTA Step Outside Tonight Look west", "layout:minimal astronomy poster"],
                [request.AccessibilityIntent, "Minimal CTA and west cue keep the planet pair dominant."]),
            _ => throw new ArgumentException($"Unsupported astronomy infographic question type '{request.QuestionType}'.", nameof(request))
        };
    }
}
