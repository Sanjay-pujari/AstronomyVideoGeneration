using Astronomy.MediaFactory.Contracts;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record VisualStory
{
    public required string StoryId { get; init; }
    public string StoryVersion { get; init; } = VisualStoryModel.Version;
    public required string StoryTitle { get; init; }
    public required string ViewerQuestion { get; init; }
    public required string PrimaryStory { get; init; }
    public required string ViewerTakeaway { get; init; }
    public required string EmotionalHook { get; init; }
    public IReadOnlyList<string> StoryArc { get; init; } = [];
    public IReadOnlyList<string> EditorialPriority { get; init; } = [];
    public required string PrimaryVisualSubject { get; init; }
    public IReadOnlyList<string> SecondaryVisualSubjects { get; init; } = [];
    public required string VisualRelationship { get; init; }
    public required string RecommendedComposition { get; init; }
    public required string RecommendedViewerFocus { get; init; }
    public required string DocumentaryTone { get; init; }
    public required string EnvironmentRecommendation { get; init; }
    public required string LightingRecommendation { get; init; }
    public required string RecommendedNegativeSpace { get; init; }
    public IReadOnlyList<string> RecommendedOverlayZones { get; init; } = [];
    public IReadOnlyDictionary<string, VisualStoryPlatformVariation> RecommendedPlatformVariations { get; init; } = new Dictionary<string, VisualStoryPlatformVariation>();
    public required double StoryConfidence { get; init; }
    public required string CreativeKnowledgeVersion { get; init; }
    public required string EditorialReasoningVersion { get; init; }
    public IReadOnlyDictionary<string, object?> ExtensionFields { get; init; } = new Dictionary<string, object?>();
}

public sealed record VisualStoryPlatformVariation
{
    public required string Name { get; init; }
    public required string Recommendation { get; init; }
    public IReadOnlyList<string> Emphasis { get; init; } = [];
}

public interface IVisualStoryModel
{
    VisualStory Create(VisualIntelligenceOrchestrationContext context, EditorialDecision editorialDecision, CreativeKnowledge knowledge, IList<DiagnosticMessage>? diagnostics = null);
}

public sealed class VisualStoryModel : IVisualStoryModel
{
    public const string Version = "4.3A";

    public VisualStory Create(VisualIntelligenceOrchestrationContext context, EditorialDecision editorialDecision, CreativeKnowledge knowledge, IList<DiagnosticMessage>? diagnostics = null)
    {
        var isPlanetPairing = knowledge.Family == CreativeKnowledgeFamily.PlanetPairing || IsPlanetPairing(context);
        var primaryObjects = Normalize(context.PrimaryObjects);
        var supportingObjects = Normalize(context.SupportingObjects);
        var allObjects = Normalize(primaryObjects.Concat(supportingObjects));

        var story = isPlanetPairing
            ? CreatePlanetPairing(context, editorialDecision, knowledge, allObjects)
            : CreateGeneric(context, editorialDecision, knowledge, allObjects);

        diagnostics?.Add(new DiagnosticMessage
        {
            Severity = DiagnosticSeverity.Info,
            Code = "visual_story_model.created",
            Message = $"Visual Story Model created: {story.StoryId}.",
            Source = nameof(VisualStoryModel)
        });

        return story;
    }

    private static VisualStory CreatePlanetPairing(VisualIntelligenceOrchestrationContext context, EditorialDecision decision, CreativeKnowledge knowledge, IReadOnlyList<string> objects) => new()
    {
        StoryId = decision.StoryId,
        StoryTitle = FirstNonEmpty(context.EventName, "Bright planet pairing"),
        ViewerQuestion = decision.ViewerQuestion,
        PrimaryStory = "Two bright planets appear unusually close together.",
        ViewerTakeaway = "This is an apparent conjunction.",
        EmotionalHook = "Wonder.",
        StoryArc = ["Discovery", "Understanding", "Observation", "Wonder", "Action"],
        EditorialPriority = decision.EditorialPriority,
        PrimaryVisualSubject = "Relationship",
        SecondaryVisualSubjects = objects.Count > 0 ? objects : ["Individual planets"],
        VisualRelationship = "The apparent closeness between the planets is the subject; do not prioritize the largest planet.",
        RecommendedComposition = "Balanced pairing",
        RecommendedViewerFocus = "Relationship first, then the individual planets.",
        DocumentaryTone = decision.DocumentaryTone,
        EnvironmentRecommendation = "Observed sky realism with restrained horizon or twilight context only when it supports observability.",
        LightingRecommendation = "Natural low-noise twilight or night-sky documentary lighting with restrained glow.",
        RecommendedNegativeSpace = "Shared negative space around both planets to make the pairing readable.",
        RecommendedOverlayZones = ["lower third", "outer edges", "avoid the space between paired planets"],
        RecommendedPlatformVariations = PlatformVariations(),
        StoryConfidence = decision.Confidence,
        CreativeKnowledgeVersion = CreativeKnowledgeLibrary.Version,
        EditorialReasoningVersion = decision.ReasoningVersion,
        ExtensionFields = new Dictionary<string, object?> { ["eventFamily"] = context.EventFamily.ToString(), ["eventType"] = context.EventType, ["source"] = "EditorialReasoningEngine" }
    };

    private static VisualStory CreateGeneric(VisualIntelligenceOrchestrationContext context, EditorialDecision decision, CreativeKnowledge knowledge, IReadOnlyList<string> objects) => new()
    {
        StoryId = decision.StoryId,
        StoryTitle = FirstNonEmpty(context.EventName, context.EventType, "Astronomy story"),
        ViewerQuestion = decision.ViewerQuestion,
        PrimaryStory = decision.PrimaryStory,
        ViewerTakeaway = decision.ViewerTakeaway,
        EmotionalHook = decision.EmotionalHook,
        StoryArc = ["Discovery", "Understanding", "Observation", "Wonder", "Action"],
        EditorialPriority = decision.EditorialPriority,
        PrimaryVisualSubject = objects.FirstOrDefault() ?? "Observable sky event",
        SecondaryVisualSubjects = objects.Skip(1).DefaultIfEmpty("Supporting astronomy context").ToArray(),
        VisualRelationship = decision.RecommendedVisualRelationship,
        RecommendedComposition = decision.RecommendedComposition,
        RecommendedViewerFocus = decision.RecommendedViewerFocus,
        DocumentaryTone = decision.DocumentaryTone,
        EnvironmentRecommendation = "Use factual observation context only when it clarifies the event.",
        LightingRecommendation = "Premium astronomy documentary lighting with realistic sky contrast.",
        RecommendedNegativeSpace = "Maintain clean negative space for comprehension and future overlays.",
        RecommendedOverlayZones = ["lower third", "top corners", "edge-safe labels"],
        RecommendedPlatformVariations = PlatformVariations(),
        StoryConfidence = decision.Confidence,
        CreativeKnowledgeVersion = CreativeKnowledgeLibrary.Version,
        EditorialReasoningVersion = decision.ReasoningVersion,
        ExtensionFields = new Dictionary<string, object?> { ["eventFamily"] = context.EventFamily.ToString(), ["eventType"] = context.EventType, ["source"] = "EditorialReasoningEngine" }
    };

    private static IReadOnlyDictionary<string, VisualStoryPlatformVariation> PlatformVariations() => new Dictionary<string, VisualStoryPlatformVariation>
    {
        ["landscape"] = new() { Name = "Landscape Story", Recommendation = "wide documentary composition", Emphasis = ["horizontal context", "shared sky", "clean lower-third room"] },
        ["portrait"] = new() { Name = "Portrait Story", Recommendation = "large hero objects with vertical emphasis", Emphasis = ["vertical emphasis", "large readable subjects", "stacked safe zones"] },
        ["square"] = new() { Name = "Square Story", Recommendation = "balanced centered composition", Emphasis = ["centered balance", "symmetry", "compact documentary clarity"] }
    };

    private static bool IsPlanetPairing(VisualIntelligenceOrchestrationContext context) => $"{context.EventType} {context.EventName}".Contains("pair", StringComparison.OrdinalIgnoreCase) || $"{context.EventType} {context.EventName}".Contains("conjunction", StringComparison.OrdinalIgnoreCase);
    private static IReadOnlyList<string> Normalize(IEnumerable<string> values) => values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}
