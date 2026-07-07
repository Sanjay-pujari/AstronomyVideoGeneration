using Astronomy.MediaFactory.Contracts;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public interface IHumanContextDirector
{
    HumanContextReview Review(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge, EditorialCompositionDecision editorial, DocumentaryAtmosphereReview atmosphere);
}

public sealed record HumanContextReview
{
    public string ReviewVersion { get; init; } = "4.4C.3";
    public string Mode { get; init; } = "creative-refinement";
    public bool GeneratesPrompts { get; init; }
    public string ContextContribution { get; init; } = string.Empty;
    public string ObservationRealism { get; init; } = string.Empty;
    public string ScaleCommunication { get; init; } = string.Empty;
    public string StorySupport { get; init; } = string.Empty;
    public double DocumentaryScore { get; init; }
    public IReadOnlyList<string> RecommendedContextCues { get; init; } = [];
    public IReadOnlyList<string> AvoidPatterns { get; init; } = [];
    public IReadOnlyList<string> CreativeRecommendations { get; init; } = [];
    public HumanContextBenchmarkMetadata BenchmarkPreparation { get; init; } = new();
}

public sealed record HumanContextBenchmarkMetadata
{
    public string BenchmarkFamily { get; init; } = "hero-human-context";
    public string Version { get; init; } = "4.4C.3";
    public IReadOnlyList<string> ScoreDimensions { get; init; } = ["contextContribution", "observationRealism", "scaleCommunication", "storySupport", "documentaryScore"];
    public IReadOnlyList<string> CandidateTags { get; init; } = [];
    public bool RunnerImplemented { get; init; }
}

public sealed class HumanContextDirector : IHumanContextDirector
{
    private static readonly string[] AllowedCues = ["observatory silhouette", "mountain ridge", "temple skyline", "tree line", "lake reflection", "single observer silhouette"];
    private static readonly string[] AvoidPatterns = ["crowded cities", "tourism focus", "busy architecture", "decorative foreground clutter", "fantasy foregrounds", "oversized landmarks", "cinematic clutter"];

    public HumanContextReview Review(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge, EditorialCompositionDecision editorial, DocumentaryAtmosphereReview atmosphere)
    {
        var cues = SelectContextCues(context, knowledge, editorial);
        var shouldUseContext = cues.Count > 0;
        var contextContribution = shouldUseContext
            ? $"Use subtle human context only as environmental storytelling: {string.Join(", ", cues)} may make the sky feel observable without becoming the subject."
            : "Omit foreground human context unless it clearly improves the documentary story; preserve the celestial event as the subject.";
        var observationRealism = "Favor believable observation locations, subtle environmental framing, realistic foreground silhouettes, and clean horizons; avoid fantasy foregrounds, oversized landmarks, and cinematic clutter.";
        var scaleCommunication = "Context should communicate scale and the act of observing, never compete with celestial objects or reduce sky clarity.";
        var storySupport = "Ask whether a documentary photographer would include this foreground; if the answer is no, recommend omitting it.";
        var score = ScoreDocumentary(context, editorial, atmosphere, shouldUseContext);
        var recommendations = new List<string>
        {
            "Human context is optional: recommend it only when it helps the viewer feel, 'I could go outside tonight and observe this.'",
            contextContribution,
            scaleCommunication,
            observationRealism,
            storySupport
        };

        return new HumanContextReview
        {
            ContextContribution = contextContribution,
            ObservationRealism = observationRealism,
            ScaleCommunication = scaleCommunication,
            StorySupport = storySupport,
            DocumentaryScore = score,
            RecommendedContextCues = cues,
            AvoidPatterns = AvoidPatterns,
            CreativeRecommendations = recommendations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            BenchmarkPreparation = new HumanContextBenchmarkMetadata { CandidateTags = BuildBenchmarkTags(context, knowledge, cues, shouldUseContext) }
        };
    }

    private static IReadOnlyList<string> SelectContextCues(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge, EditorialCompositionDecision editorial)
    {
        var text = $"{context.EventName} {context.EventType} {context.Location} {context.Region} {editorial.DocumentaryComposition}".ToLowerInvariant();
        var cues = new List<string>();
        if (text.Contains("observatory")) cues.Add("observatory silhouette");
        if (text.Contains("temple")) cues.Add("temple skyline");
        if (text.Contains("lake")) cues.Add("lake reflection");
        if (text.Contains("mountain") || text.Contains("ridge")) cues.Add("mountain ridge");
        if (knowledge.Family is CreativeKnowledgeFamily.PlanetPairing or CreativeKnowledgeFamily.PlanetGrouping) cues.Add("tree line");
        if (context.EventFamily is ContractEventFamily.MeteorShower or ContractEventFamily.LunarEvent) cues.Add("single observer silhouette");
        return cues.Where(c => AllowedCues.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
    }

    private static double ScoreDocumentary(VisualIntelligenceOrchestrationContext context, EditorialCompositionDecision editorial, DocumentaryAtmosphereReview atmosphere, bool shouldUseContext)
    {
        var score = .84 + Math.Min(.08, editorial.DocumentaryScore * .08) + Math.Min(.04, atmosphere.DocumentaryScore * .04);
        if (shouldUseContext) score += .02;
        if (!string.IsNullOrWhiteSpace(context.Location) || !string.IsNullOrWhiteSpace(context.Region)) score += .01;
        return Math.Round(Math.Min(.98, score), 2);
    }

    private static IReadOnlyList<string> BuildBenchmarkTags(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge, IReadOnlyList<string> cues, bool shouldUseContext)
        => new[] { "human-context", shouldUseContext ? "context-recommended" : "context-omitted", knowledge.Family.ToString(), context.EventFamily.ToString() }
            .Concat(cues).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
