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
                "Golden twilight astronomy magazine cover with an orange western horizon glow where Venus and Jupiter are the visual focus and text remains a small editorial accent.",
                0.25,
                0.75,
                ["golden twilight", "orange western horizon glow", "premium astronomy magazine cover", "Venus/Jupiter visual focus", "small floating title", "no grid or chart background"],
                CommonForbiddenPatterns,
                new("Venus & Jupiter Tonight", "After sunset", ["Venus", "Jupiter"], [], ["Venus", "Jupiter"], [], [], []),
                ["background:golden twilight western sky", "horizon:orange western horizon glow", "celestial:local transparent Venus and Jupiter assets integrated with subtle glow", "annotation:floating magazine title", "layout:asymmetric cover composition"],
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
                "Significance graphic emphasizing the close Venus/Jupiter pairing with a closeness bracket and brightness comparison feel.",
                0.25,
                0.75,
                ["Venus/Jupiter close pairing", "closeness bracket", "short significance line", "visual comparison/brightness feel"],
                CommonForbiddenPatterns,
                new("Why It Matters", "Close bright pairing", ["Venus", "Jupiter"], ["closeness bracket"], ["Venus", "Jupiter"], [], [], []),
                ["background:deep editorial sky with subtle astronomy texture", "celestial:close local Venus/Jupiter pairing", "comparison:brightness feel", "direction:closeness bracket", "annotation:floating significance line", "layout:center comparison graphic"],
                [request.AccessibilityIntent, "Bracket shows the planets' apparent closeness."]),
            "action" => new(
                "minimal-closing-astronomy-poster",
                "ACTION — Astronomy Poster",
                "Beautiful closing astronomy poster with Venus/Jupiter together and only a minimal call to action.",
                0.25,
                0.75,
                ["beautiful closing sky", "Venus/Jupiter together", "minimal CTA"],
                CommonForbiddenPatterns,
                new("Step Outside Tonight", "Look west", ["Venus", "Jupiter"], [], ["Venus", "Jupiter"], ["West"], [], []),
                ["background:beautiful emotional twilight poster sky", "horizon:warm peaceful western horizon", "celestial:local Venus and Jupiter together integrated with subtle glow", "annotation:minimal floating CTA", "layout:minimal astronomy poster"],
                [request.AccessibilityIntent, "Minimal CTA and west cue keep the planet pair dominant."]),
            _ => throw new ArgumentException($"Unsupported astronomy infographic question type '{request.QuestionType}'.", nameof(request))
        };
    }
}
