using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record EditorialDecision
{
    public required string StoryId { get; init; }
    public required string ViewerQuestion { get; init; }
    public required string PrimaryStory { get; init; }
    public required string ViewerTakeaway { get; init; }
    public required string EmotionalHook { get; init; }
    public IReadOnlyList<string> EditorialPriority { get; init; } = [];
    public required string DocumentaryTone { get; init; }
    public required string ScientificPriority { get; init; }
    public required string RecommendedVisualRelationship { get; init; }
    public required string RecommendedComposition { get; init; }
    public required string RecommendedViewerFocus { get; init; }
    public required string RecommendedNarrativeArc { get; init; }
    public required double Confidence { get; init; }
    public required string ReasoningVersion { get; init; }
}

public interface IEditorialReasoningEngine
{
    EditorialDecision Decide(VisualIntelligenceOrchestrationContext context, CreativeKnowledge? knowledge = null, IList<DiagnosticMessage>? diagnostics = null);
}

public sealed class EditorialReasoningEngine : IEditorialReasoningEngine
{
    public const string Version = "4.2A";
    private readonly ICreativeKnowledgeLibrary knowledgeLibrary;

    public EditorialReasoningEngine()
        : this(new CreativeKnowledgeLibrary()) { }

    public EditorialReasoningEngine(ICreativeKnowledgeLibrary knowledgeLibrary)
    {
        this.knowledgeLibrary = knowledgeLibrary;
    }

    public EditorialDecision Decide(VisualIntelligenceOrchestrationContext context, CreativeKnowledge? knowledge = null, IList<DiagnosticMessage>? diagnostics = null)
    {
        knowledge ??= knowledgeLibrary.Resolve(context, diagnostics: diagnostics);
        var decision = knowledge.Family == CreativeKnowledgeFamily.PlanetPairing
            ? PlanetPairingDecision(context, knowledge)
            : GenericDecision(context, knowledge);

        diagnostics?.Add(new DiagnosticMessage
        {
            Severity = DiagnosticSeverity.Info,
            Code = "editorial_reasoning.decision_created",
            Message = $"Editorial reasoning decision created: {decision.StoryId}.",
            Source = nameof(EditorialReasoningEngine)
        });

        return decision;
    }

    private static EditorialDecision PlanetPairingDecision(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge) => new()
    {
        StoryId = BuildStoryId(context, knowledge),
        ViewerQuestion = FirstNonEmpty(knowledge.ViewerQuestion, "Why are these two planets close together tonight?"),
        PrimaryStory = "The unusual closeness of two bright planets.",
        ViewerTakeaway = "The planets only appear close from Earth's perspective.",
        EmotionalHook = "Witness one of the brightest conjunctions of the year.",
        EditorialPriority = ["relationship", "observability", "scientific perspective", "beauty", "scale"],
        DocumentaryTone = "premium astronomy documentary; calm, factual, and quietly wondrous",
        ScientificPriority = "Explain apparent alignment from Earth's line of sight without implying physical proximity.",
        RecommendedVisualRelationship = "Relationship > Scale",
        RecommendedComposition = "Balanced pairing",
        RecommendedViewerFocus = "Relationship first",
        RecommendedNarrativeArc = "Notice the bright pair, understand their apparent closeness, then know why the moment is worth observing.",
        Confidence = Confidence(context, knowledge, .94),
        ReasoningVersion = Version
    };

    private static EditorialDecision GenericDecision(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge) => new()
    {
        StoryId = BuildStoryId(context, knowledge),
        ViewerQuestion = FirstNonEmpty(knowledge.ViewerQuestion, "What should the viewer understand about this sky event?"),
        PrimaryStory = FirstNonEmpty(knowledge.StoryGoal, "A clear observable astronomy event deserves focused, factual explanation."),
        ViewerTakeaway = "The viewer should understand the observable event and why it matters now.",
        EmotionalHook = FirstNonEmpty(knowledge.EmotionalGoal, "Create trustworthy curiosity."),
        EditorialPriority = ["clarity", "observability", "scientific trust", "composition restraint"],
        DocumentaryTone = FirstNonEmpty(knowledge.DocumentaryGuidance, "premium astronomy documentary restraint"),
        ScientificPriority = "Preserve factual astronomy context and avoid invented observation details.",
        RecommendedVisualRelationship = "Clarity > spectacle",
        RecommendedComposition = FirstNonEmpty(knowledge.CompositionPreferences.FirstOrDefault(), "Single clear subject with restrained supporting context"),
        RecommendedViewerFocus = "Story first",
        RecommendedNarrativeArc = "Identify the event, explain the viewing significance, and leave the viewer with one clear takeaway.",
        Confidence = Confidence(context, knowledge, knowledge.Family == CreativeKnowledgeFamily.GenericAstronomy ? .68 : .84),
        ReasoningVersion = Version
    };

    private static string BuildStoryId(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge)
    {
        var source = FirstNonEmpty(context.EventType, context.EventName, context.EventFamily.ToString(), knowledge.Family.ToString());
        return $"{Version}:{knowledge.Family}:{source}";
    }

    private static double Confidence(VisualIntelligenceOrchestrationContext context, CreativeKnowledge knowledge, double baseConfidence)
    {
        var confidence = baseConfidence;
        if (!string.IsNullOrWhiteSpace(context.EventType) || !string.IsNullOrWhiteSpace(context.EventName)) confidence += .02;
        if (context.PrimaryObjects.Count > 0 || context.SupportingObjects.Count > 0) confidence += .02;
        if (knowledge.Family == CreativeKnowledgeFamily.GenericAstronomy) confidence -= .04;
        return Math.Clamp(Math.Round(confidence, 2), 0, 1);
    }

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}
