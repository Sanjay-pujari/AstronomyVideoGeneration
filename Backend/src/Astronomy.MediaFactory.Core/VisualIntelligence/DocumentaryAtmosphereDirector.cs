using Astronomy.MediaFactory.Contracts;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public interface IDocumentaryAtmosphereDirector
{
    DocumentaryAtmosphereReview Review(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge, EditorialCompositionDecision editorial);
}

public sealed record DocumentaryAtmosphereReview
{
    public string ReviewVersion { get; init; } = "4.4C.2";
    public string Mode { get; init; } = "creative-refinement";
    public bool GeneratesPrompts { get; init; }
    public string TwilightAuthenticity { get; init; } = string.Empty;
    public string SkyRealism { get; init; } = string.Empty;
    public string EnvironmentQuality { get; init; } = string.Empty;
    public double DocumentaryScore { get; init; }
    public double ScientificAtmosphereScore { get; init; }
    public IReadOnlyList<string> TwilightRecommendations { get; init; } = [];
    public IReadOnlyList<string> EnvironmentRecommendations { get; init; } = [];
    public IReadOnlyList<string> SkyRecommendations { get; init; } = [];
    public IReadOnlyList<string> LightingRecommendations { get; init; } = [];
    public IReadOnlyList<string> AvoidPatterns { get; init; } = [];
    public IReadOnlyList<string> CreativeRecommendations { get; init; } = [];
    public DocumentaryAtmosphereBenchmarkMetadata BenchmarkPreparation { get; init; } = new();
}

public sealed record DocumentaryAtmosphereBenchmarkMetadata
{
    public string BenchmarkFamily { get; init; } = "hero-documentary-atmosphere";
    public string Version { get; init; } = "4.4C.2";
    public IReadOnlyList<string> ScoreDimensions { get; init; } = ["twilightAuthenticity", "skyRealism", "environmentQuality", "documentaryScore", "scientificAtmosphereScore"];
    public IReadOnlyList<string> CandidateTags { get; init; } = [];
    public bool RunnerImplemented { get; init; }
}

public sealed class DocumentaryAtmosphereDirector : IDocumentaryAtmosphereDirector
{
    private static readonly string[] SharedAvoidPatterns =
    [
        "fantasy orange explosions", "neon skies", "unrealistic color transitions", "fantasy nebulae",
        "excessive stars", "unrealistic Milky Way", "artificial glow", "random HDR", "fantasy rim lighting",
        "dramatic artificial glow", "unnecessary foreground clutter"
    ];

    public DocumentaryAtmosphereReview Review(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge, EditorialCompositionDecision editorial)
    {
        var twilight = SelectTwilight(context, knowledge);
        var environment = SelectEnvironment(context, knowledge);
        var sky = SelectSky(context);
        var lighting = SelectLighting(context);
        var documentaryScore = ScoreDocumentary(context, editorial, environment);
        var scientificScore = ScoreScientific(context, twilight, sky);
        var recommendations = new List<string>
        {
            "Make the scene feel observable from Earth: a viewer should believe they could step outside and see the sky under similar conditions.",
            twilight[0], sky[0], environment[0], lighting[0],
            "Use horizon storytelling only when it clarifies where and how to observe; otherwise preserve clean negative sky."
        };

        return new DocumentaryAtmosphereReview
        {
            TwilightAuthenticity = string.Join(" ", twilight),
            SkyRealism = string.Join(" ", sky),
            EnvironmentQuality = string.Join(" ", environment),
            DocumentaryScore = documentaryScore,
            ScientificAtmosphereScore = scientificScore,
            TwilightRecommendations = twilight,
            EnvironmentRecommendations = environment,
            SkyRecommendations = sky,
            LightingRecommendations = lighting,
            AvoidPatterns = SharedAvoidPatterns,
            CreativeRecommendations = recommendations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            BenchmarkPreparation = new DocumentaryAtmosphereBenchmarkMetadata
            {
                CandidateTags = BuildBenchmarkTags(context, knowledge, twilight, environment, sky)
            }
        };
    }

    private static IReadOnlyList<string> SelectTwilight(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge)
    {
        if (context.EventFamily is ContractEventFamily.PlanetConjunction || knowledge.Family is CreativeKnowledgeFamily.PlanetPairing or CreativeKnowledgeFamily.PlanetGrouping)
            return ["Prefer civil or nautical twilight with a realistic evening gradient for visible bright planets.", "Use physically plausible atmospheric scattering from warm horizon to deep blue upper sky."];
        if (context.EventFamily is ContractEventFamily.MeteorShower)
            return ["Use late nautical twilight only if it supports the viewing window; otherwise settle into a believable dark sky.", "Avoid saturated sunset color competing with faint meteors."];
        return ["Prefer civil twilight, nautical twilight, or blue hour only when the event would be plausibly observable then.", "Keep color transitions gradual and physically plausible."];
    }

    private static IReadOnlyList<string> SelectEnvironment(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge)
    {
        var text = $"{context.EventName} {context.EventType} {context.Location} {context.Region}".ToLowerInvariant();
        if (text.Contains("temple")) return ["A temple skyline may be used as a restrained horizon cue when it strengthens local observation storytelling.", "Keep the skyline low, dark, and secondary to the sky."];
        if (text.Contains("lake")) return ["Lake reflections may add grounded atmosphere when calm water supports observation, but reflections must remain subtle and physically plausible.", "Avoid mirror-perfect fantasy reflections."];
        if (text.Contains("observatory")) return ["An observatory silhouette can strengthen scientific authenticity when it remains quiet and secondary.", "Avoid making the building the hero."];
        if (knowledge.Family is CreativeKnowledgeFamily.PlanetPairing or CreativeKnowledgeFamily.PlanetGrouping) return ["Use a low mountain silhouette, tree line, or clean horizon only if it helps the viewer orient toward the observable sky.", "Avoid cluttering the foreground with decorative landscape elements."];
        return ["Prefer minimal environmental context: a clean horizon, mountain silhouette, observatory, temple skyline, lake reflection, or tree line only when it strengthens observation.", "Leave unnecessary clutter out of the scene."];
    }

    private static IReadOnlyList<string> SelectSky(VisualIntelligenceOrchestrationContext context)
        => context.EventFamily == ContractEventFamily.MeteorShower
            ? ["Use a clean dark sky with believable sparse stars and restrained meteor activity.", "Avoid star overload, fantasy nebulae, artificial glow, or an unrealistic Milky Way."]
            : ["Use a clean sky with subtle stars only where visibility is realistic for twilight and the event.", "Prefer documentary astrophotography restraint over decorative deep-space artwork."];

    private static IReadOnlyList<string> SelectLighting(VisualIntelligenceOrchestrationContext context)
        => ["Use natural sunset, blue hour, soft atmospheric light, and realistic planetary or lunar illumination.", "Avoid random HDR, fantasy rim lighting, and dramatic artificial glow."];

    private static double ScoreDocumentary(VisualIntelligenceOrchestrationContext context, EditorialCompositionDecision editorial, IReadOnlyList<string> environment)
    {
        var score = .84 + Math.Min(.08, editorial.DocumentaryScore * .08);
        if (!string.IsNullOrWhiteSpace(context.Location) || !string.IsNullOrWhiteSpace(context.Region)) score += .03;
        if (environment.Any(e => e.Contains("only if", StringComparison.OrdinalIgnoreCase))) score += .02;
        return Math.Round(Math.Min(.98, score), 2);
    }

    private static double ScoreScientific(VisualIntelligenceOrchestrationContext context, IReadOnlyList<string> twilight, IReadOnlyList<string> sky)
    {
        var score = .86;
        if (twilight.Any(t => t.Contains("physically plausible", StringComparison.OrdinalIgnoreCase))) score += .04;
        if (sky.Any(s => s.Contains("visibility is realistic", StringComparison.OrdinalIgnoreCase) || s.Contains("believable", StringComparison.OrdinalIgnoreCase))) score += .04;
        if (context.PrimaryObjects.Count > 0) score += .02;
        return Math.Round(Math.Min(.99, score), 2);
    }

    private static IReadOnlyList<string> BuildBenchmarkTags(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge, IReadOnlyList<string> twilight, IReadOnlyList<string> environment, IReadOnlyList<string> sky)
        => new[] { "documentary-atmosphere", knowledge.Family.ToString(), context.EventFamily.ToString(), twilight[0], environment[0], sky[0] }
            .Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
