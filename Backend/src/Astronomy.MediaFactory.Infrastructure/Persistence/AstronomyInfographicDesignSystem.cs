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
                "YouTube-quality astronomy thumbnail with a professional astronomy magazine cover feel, richer western twilight over Rajasthan, stronger golden-orange horizon glow, subtle haze, a soft vignette, a denser natural starfield, brighter Venus/Jupiter focal region, and premium title typography.",
                0.25,
                0.75,
                ["professional astronomy magazine cover", "western twilight over Rajasthan", "golden-orange horizon glow", "reduced-scale Venus/Jupiter sky targets", "premium typography", "title Venus & Jupiter", "subtitle After sunset", "no grid or chart background"],
                CommonForbiddenPatterns,
                new("Venus & Jupiter", "After sunset", ["Venus", "Jupiter"], [], ["Venus", "Jupiter"], [], [], []),
                ["background:professional astronomy magazine cover western twilight over Rajasthan with richer twilight colors, natural atmospheric glow, and smooth sky gradient", "horizon:stronger golden-orange western horizon glow with subtle atmospheric haze", "texture:documentary twilight haze, subtle sky grain, natural density variation starfield, magnitude variation, brightness variation", "composition:strong focal contrast clickable thumbnail composition with slightly brighter focal region around Venus/Jupiter", "vignette:soft natural edge falloff", "celestial:reduced-scale local transparent Venus and Jupiter sky targets integrated with atmospheric blending and subtle shared glow", "typography:premium thumbnail title Venus & Jupiter subtitle After sunset"],
                [request.AccessibilityIntent, "Small labels identify Venus and Jupiter without covering the sky."]),
            "where" => new(
                "western-horizon-observation-chart",
                "WHERE — Observation Chart",
                "Astronomy guide observation chart answering where to look with a subtle real western horizon, delicate sky grid, Leo/Regulus reference-star guide, West marker, altitude cue, and plotted Venus/Jupiter positions.",
                0.25,
                0.75,
                ["western horizon", "horizon line", "West marker", "Venus/Jupiter positions", "altitude hint", "subtle sky grid", "Leo/Regulus reference-star guide"],
                CommonForbiddenPatterns,
                new("Where to Look", "Western horizon", ["West", "Venus", "Jupiter", "Horizon", "Leo / Regulus reference stars"], ["western horizon altitude guide"], ["Venus", "Jupiter"], ["West"], [], []),
                ["background:observation-chart sky with astronomy guide aesthetic and subtle atmospheric realism", "horizon:subtle real western horizon", "celestial:local Venus/Jupiter plotted positions", "reference:subtle sky grid", "reference:Leo Regulus constellation-star guide", "direction:West marker", "annotation:floating altitude hint"],
                [request.AccessibilityIntent, "West marker and horizon line provide orientation for Udaipur viewers."]),
            "when" => new(
                "twilight-viewing-window-timeline",
                "WHEN — Timeline Infographic",
                "Timeline visual with sunset, 7:23 PM IST, and an after-sunset viewing window embedded in a real twilight transition with warm sunset colors.",
                0.25,
                0.75,
                ["sunset marker", "7:23 PM IST marker", "viewing window", "real twilight transition"],
                CommonForbiddenPatterns,
                new("Best Time Tonight", "Viewing window", ["Sunset", "Viewing window"], [], [], [], ["7:23 PM IST"], []),
                ["background:real twilight transition with warm sunset colors and natural atmospheric haze", "horizon:natural warm western horizon glow", "time:sunset marker", "time:7:23 PM IST marker", "direction:after-sunset viewing window", "layout:horizontal timeline"],
                [request.AccessibilityIntent, "Timeline markers are large enough to read while the real twilight sky remains dominant."]),
            "how" => new(
                "field-step-guide-arrow-path",
                "HOW — Step Guide",
                "Field step guide showing the observing sequence: find Venus, look nearby for Jupiter, and face west, with arrows from Venus to Jupiter.",
                0.25,
                0.75,
                ["Step 1 Find Venus", "Step 2 Look nearby for Jupiter", "Step 3 Face west", "arrows from Venus to Jupiter", "West marker"],
                CommonForbiddenPatterns,
                new("How to Find It", "Use Venus as your anchor", ["Venus", "Jupiter", "West"], ["arrow from Venus to Jupiter", "arrow toward western horizon"], ["Venus", "Jupiter"], ["West"], [], ["Find Venus", "Look nearby for Jupiter", "Face west"]),
                ["background:observer-friendly western sky with natural atmospheric depth", "horizon:subtle west reference", "celestial:local Venus/Jupiter assets integrated with subtle glow", "reference:subtle reference-star hint", "direction:arrow from Venus to Jupiter", "steps:three floating labels", "layout:diagonal field guide"],
                [request.AccessibilityIntent, "Numbered steps and arrows encode the observing path."]),
            "why" => new(
                "close-pair-significance-bracket",
                "WHY — Significance Graphic",
                "Human-interest significance visual: two of the brightest worlds sharing the evening sky, emphasizing brightness, closeness, a subtle shared glow region, their visual relationship, premium editorial sky, and emotional meaning.",
                0.25,
                0.75,
                ["two of the brightest worlds sharing the evening sky", "brightness", "closeness", "shared sky", "emotional significance"],
                CommonForbiddenPatterns,
                new("Why It Matters", "Two of the brightest worlds sharing the evening sky", ["Venus", "Jupiter", "brightness", "closeness", "shared sky"], ["closeness bracket", "brightness comparison"], ["Venus", "Jupiter"], [], [], []),
                ["background:deep astronomy sky premium editorial background with atmospheric starfield depth, smooth sky gradient, natural density variation, magnitude variation, brightness variation", "celestial:two of the brightest worlds sharing the evening sky as reduced-scale sky targets integrated with atmospheric blending and subtle shared glow region", "significance:shared sky brightness emotional significance for human interest and memorable astronomy storytelling", "relationship:visual relationship between planets with slight emphasis on closeness", "comparison:brightness feel", "direction:closeness bracket", "annotation:floating human-interest significance line"],
                [request.AccessibilityIntent, "Bracket shows the planets' apparent closeness."]),
            "action" => new(
                "minimal-closing-astronomy-poster",
                "ACTION — Astronomy Poster",
                "Shareable poster-quality astronomy artwork with warmer horizon glow, richer cinematic twilight, atmospheric depth, subtle haze, a stronger landscape silhouette, Venus/Jupiter together as sky targets, premium composition, and only a minimal call to action.",
                0.25,
                0.75,
                ["poster-quality cinematic twilight", "warm golden-orange horizon", "Venus/Jupiter naturally integrated", "premium shareable composition", "minimal CTA Step Outside Tonight Look west"],
                CommonForbiddenPatterns,
                new("Step Outside Tonight", "Look west", ["Venus", "Jupiter"], [], ["Venus", "Jupiter"], ["West"], [], []),
                ["background:most beautiful poster-quality cinematic twilight premium astronomy artwork with atmospheric depth and smooth sky gradient", "horizon:warmer peaceful stronger golden-orange western horizon with subtle haze", "composition:premium shareable poster composition", "landscape:stronger landscape silhouette", "celestial:local Venus and Jupiter reduced-scale sky targets naturally integrated with atmospheric blending and subtle glow", "starfield:natural density variation, magnitude variation, brightness variation", "twilight:cinematic warm western glow with richer twilight", "typography:minimal CTA Step Outside Tonight Look west", "layout:minimal astronomy poster"],
                [request.AccessibilityIntent, "Minimal CTA and west cue keep the planet pair dominant."]),
            _ => throw new ArgumentException($"Unsupported astronomy infographic question type '{request.QuestionType}'.", nameof(request))
        };
    }
}
