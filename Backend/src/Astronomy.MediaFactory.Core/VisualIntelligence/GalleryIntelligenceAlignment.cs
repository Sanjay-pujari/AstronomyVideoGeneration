using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record GalleryIntelligenceContract : EditorialProductContract
{
    [JsonIgnore]
    public string GalleryId => ProductId;
    [JsonIgnore]
    public string GalleryEditorialStrategyId => ProductEditorialStrategyId;
    [JsonIgnore]
    public IReadOnlyList<string> StoryProgression => EditorialSequence.StoryProgression;
    public required GalleryEditorialSequence EditorialSequence { get; init; }
    [JsonIgnore]
    public GalleryNarrativeFlow? NarrativeFlow => EditorialSequence.NarrativeFlow;
    public required IReadOnlyList<string> LearningObjectives { get; init; }
    public required IReadOnlyList<EditorialPage> PageDefinitions { get; init; }
    [JsonIgnore]
    public string RecommendedPlatform => PlatformRecommendations.TryGetValue("gallery", out var platform) ? platform : string.Empty;
    [JsonIgnore]
    public double Confidence => CreativeConfidence;
}

public sealed record GalleryEditorialSequence
{
    public required string SequenceId { get; init; }
    public string Objective { get; init; } = "Teach visually.";
    public IReadOnlyList<EditorialPage> PageDefinitions { get; init; } = [];
    public IReadOnlyList<string> LearningObjectives { get; init; } = [];
    public IReadOnlyList<string> ViewerJourney { get; init; } = [];
    public IReadOnlyList<string> StoryProgression { get; init; } = [];
    public IReadOnlyDictionary<string, string> EditorialRecommendations { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<GalleryPageInformationDensity> InformationDensity { get; init; } = [];
    public GalleryNarrativeFlow? NarrativeFlow { get; init; }

    // Backwards-compatible alias for V4.5A diagnostics. Rendering does not consume this contract.
    public IReadOnlyList<GalleryEditorialSequenceStep> Steps => PageDefinitions
        .Select(page => new GalleryEditorialSequenceStep
        {
            Order = page.PageNumber,
            Role = page.PageRole,
            EditorialPurpose = page.PageGoal,
            SourceEmphasis = page.KeyLearning
        })
        .ToArray();
}

public sealed record EditorialPage
{
    public required int PageNumber { get; init; }
    public required string PageRole { get; init; }
    public required string PageGoal { get; init; }
    public required string ViewerQuestion { get; init; }
    public required string KeyLearning { get; init; }
    public required string RecommendedVisualFocus { get; init; }
    public required string RecommendedInformationDensity { get; init; }
    public required string RecommendedComposition { get; init; }
    public IReadOnlyList<string> EditorialNotes { get; init; } = [];
    public required double Confidence { get; init; }
}

public sealed record GalleryPageInformationDensity
{
    public required int PageNumber { get; init; }
    public required string PageRole { get; init; }
    public required string PageDensity { get; init; }
    public required string VisualWeight { get; init; }
    public required string TextWeight { get; init; }
    public required string LearningComplexity { get; init; }
    public required string ViewerLoad { get; init; }
    public required string EducationalGuidance { get; init; }
    public IReadOnlyList<string> Recommendations { get; init; } = [];
}

public sealed record GalleryNarrativeFlow
{
    public required string FlowId { get; init; }
    public required string GalleryEditorialSequenceId { get; init; }
    public required IReadOnlyList<NarrativeFlowStage> Stages { get; init; }
    public required IReadOnlyList<string> EmotionalProgression { get; init; }
    public required IReadOnlyList<string> LearningProgression { get; init; }
    public required IReadOnlyList<string> CognitiveProgression { get; init; }
    public required IReadOnlyList<string> EditorialPacing { get; init; }
    public required bool GeneratesPrompts { get; init; }
    public required bool ChangesProductionGallery { get; init; }
}

public sealed record NarrativeFlowStage
{
    public required int Order { get; init; }
    public required string Stage { get; init; }
    public required string EmotionalState { get; init; }
    public required string LearningState { get; init; }
    public required string CognitiveState { get; init; }
    public required string EditorialPacing { get; init; }
    public required string ContinuityBridge { get; init; }
}

public sealed record GalleryNarrativeFlowReview
{
    public required string GalleryId { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<string> EmotionalJourney { get; init; }
    public required IReadOnlyList<string> LearningJourney { get; init; }
    public required IReadOnlyList<string> ViewerProgression { get; init; }
    public required IReadOnlyList<string> StoryContinuity { get; init; }
    public required IReadOnlyList<string> EducationalFlow { get; init; }
    public required IReadOnlyList<string> Recommendations { get; init; }
    public required bool GeneratesPrompts { get; init; }
    public required bool ChangesProductionGallery { get; init; }
}

public sealed class NarrativeFlowDirector
{
    private static readonly string[] RequiredStages = ["Hook", "Wonder", "Discovery", "Curiosity", "Explanation", "Understanding", "Observation", "Practical application", "Takeaway", "Memory"];
    private static readonly string[] RequiredEmotions = ["Wonder", "Curiosity", "Understanding", "Observation", "Memory"];

    public GalleryNarrativeFlow CreateFlow(string galleryId, GalleryEditorialSequence sequence)
    {
        var stages = RequiredStages.Select((stage, index) => BuildStage(index + 1, stage, sequence)).ToArray();
        return new GalleryNarrativeFlow
        {
            FlowId = $"narrative_flow_{galleryId}".ToLowerInvariant(),
            GalleryEditorialSequenceId = sequence.SequenceId,
            Stages = stages,
            EmotionalProgression = RequiredEmotions,
            LearningProgression = stages.Select(stage => stage.LearningState).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            CognitiveProgression = stages.Select(stage => stage.CognitiveState).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            EditorialPacing = stages.Select(stage => stage.EditorialPacing).ToArray(),
            GeneratesPrompts = false,
            ChangesProductionGallery = false
        };
    }

    public GalleryNarrativeFlowReview Review(string galleryId, GalleryNarrativeFlow flow)
    {
        return new GalleryNarrativeFlowReview
        {
            GalleryId = galleryId,
            Version = GalleryIntelligenceAlignmentEngine.Version,
            EmotionalJourney = flow.EmotionalProgression,
            LearningJourney = flow.LearningProgression,
            ViewerProgression = flow.Stages.Select(stage => $"{stage.Stage}: {stage.EmotionalState} / {stage.CognitiveState}").ToArray(),
            StoryContinuity = flow.Stages.Select(stage => stage.ContinuityBridge).ToArray(),
            EducationalFlow = flow.Stages.Select(stage => $"{stage.Stage}: {stage.LearningState}").ToArray(),
            Recommendations = [
                "Preserve the Wonder → Curiosity → Understanding → Observation → Memory arc across future Gallery rendering.",
                "Use narrative flow as editorial metadata only; do not generate or replace prompts.",
                "Keep practical observation after explanation so action follows understanding."
            ],
            GeneratesPrompts = false,
            ChangesProductionGallery = false
        };
    }

    private static NarrativeFlowStage BuildStage(int order, string stage, GalleryEditorialSequence sequence) => new()
    {
        Order = order,
        Stage = stage,
        EmotionalState = stage switch { "Hook" or "Wonder" => "Wonder", "Discovery" or "Curiosity" => "Curiosity", "Explanation" or "Understanding" => "Understanding", "Observation" or "Practical application" => "Observation", _ => "Memory" },
        LearningState = stage switch { "Hook" => "Recognize why the event matters visually.", "Wonder" => "Feel invited to continue.", "Discovery" => "Identify what is being seen.", "Curiosity" => "Ask how the visual relationship works.", "Explanation" => "Learn the core astronomy idea.", "Understanding" => "Connect the explanation to the viewer question.", "Observation" => "Translate learning into skywatching guidance.", "Practical application" => "Know when, where, and how to look.", "Takeaway" => "Retain the main meaning.", _ => "Remember the event as a coherent story." },
        CognitiveState = stage switch { "Hook" or "Wonder" => "Attention", "Discovery" => "Orientation", "Curiosity" => "Question formation", "Explanation" => "Causal reasoning", "Understanding" => "Comprehension", "Observation" => "Planning", "Practical application" => "Action readiness", _ => "Recall" },
        EditorialPacing = stage switch { "Hook" or "Wonder" => "Fast and visual", "Discovery" or "Curiosity" => "Light and inviting", "Explanation" or "Understanding" => "Measured teaching", "Observation" or "Practical application" => "Practical and clear", _ => "Calm and memorable" },
        ContinuityBridge = BuildBridge(stage, sequence)
    };

    private static string BuildBridge(string stage, GalleryEditorialSequence sequence) => stage switch
    {
        "Hook" => $"Enter through {sequence.PageDefinitions.FirstOrDefault()?.PageRole ?? "the opening page"} before explanation.",
        "Wonder" => "Convert visual attention into a reason to keep reading.",
        "Discovery" => "Name the subject after the viewer is emotionally engaged.",
        "Curiosity" => "Raise the next natural question before teaching the cause.",
        "Explanation" => "Answer curiosity with one focused astronomy idea.",
        "Understanding" => "Let the viewer restate the idea in practical terms.",
        "Observation" => "Move from meaning to how the viewer can observe it.",
        "Practical application" => "Make viewing guidance actionable without adding a new lesson.",
        "Takeaway" => "Compress the sequence into a single memorable conclusion.",
        _ => "Leave the viewer with a durable memory of the event."
    };
}

public sealed record GalleryInformationDensityReview
{
    public required string GalleryId { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<GalleryPageInformationDensity> PageDensity { get; init; }
    public IReadOnlyDictionary<string, string> VisualWeight { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> TextWeight { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> LearningComplexity { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> ViewerLoad { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Recommendations { get; init; } = [];
    public required bool GeneratesPrompts { get; init; }
    public required bool ChangesProductionRouting { get; init; }
}

public sealed class InformationDensityDirector
{
    public GalleryInformationDensityReview Review(string galleryId, IReadOnlyList<EditorialPage> pages)
    {
        var densities = pages
            .OrderBy(page => page.PageNumber)
            .Select(ReviewPage)
            .ToArray();

        return new GalleryInformationDensityReview
        {
            GalleryId = galleryId,
            Version = GalleryIntelligenceAlignmentEngine.Version,
            PageDensity = densities,
            VisualWeight = densities.ToDictionary(page => page.PageRole, page => page.VisualWeight, StringComparer.OrdinalIgnoreCase),
            TextWeight = densities.ToDictionary(page => page.PageRole, page => page.TextWeight, StringComparer.OrdinalIgnoreCase),
            LearningComplexity = densities.ToDictionary(page => page.PageRole, page => page.LearningComplexity, StringComparer.OrdinalIgnoreCase),
            ViewerLoad = densities.ToDictionary(page => page.PageRole, page => page.ViewerLoad, StringComparer.OrdinalIgnoreCase),
            Recommendations =
            [
                "Begin highly visual so the viewer enters the story before reading.",
                "Increase educational density through explanation and observation pages.",
                "Reduce load for the takeaway so the ending is memorable."
            ],
            GeneratesPrompts = false,
            ChangesProductionRouting = false
        };
    }

    private static GalleryPageInformationDensity ReviewPage(EditorialPage page)
    {
        var role = page.PageRole;
        var density = DefaultDensity(role);
        return new GalleryPageInformationDensity
        {
            PageNumber = page.PageNumber,
            PageRole = role,
            PageDensity = density,
            VisualWeight = VisualWeightFor(role),
            TextWeight = TextWeightFor(role),
            LearningComplexity = LearningComplexityFor(role),
            ViewerLoad = ViewerLoadFor(role),
            EducationalGuidance = GuidanceFor(role),
            Recommendations = RecommendationsFor(role)
        };
    }

    private static string DefaultDensity(string role) => role switch
    {
        "Hook" => "Very Low",
        "Discovery" => "Low",
        "Explanation" => "Medium",
        "Observation" => "Medium",
        "Takeaway" => "Low",
        _ => "Low"
    };

    private static string VisualWeightFor(string role) => role switch
    {
        "Hook" => "Very High",
        "Discovery" => "High",
        "Explanation" => "Balanced",
        "Observation" => "Balanced",
        "Takeaway" => "High",
        _ => "High"
    };

    private static string TextWeightFor(string role) => role switch
    {
        "Hook" => "Very Low",
        "Discovery" => "Low",
        "Explanation" => "Medium",
        "Observation" => "Medium",
        "Takeaway" => "Low",
        _ => "Low"
    };

    private static string LearningComplexityFor(string role) => role switch
    {
        "Explanation" => "Medium",
        "Observation" => "Medium",
        "Hook" => "Very Low",
        _ => "Low"
    };

    private static string ViewerLoadFor(string role) => role switch
    {
        "Explanation" => "Moderate",
        "Observation" => "Moderate",
        "Hook" => "Minimal",
        _ => "Light"
    };

    private static string GuidanceFor(string role) => role switch
    {
        "Hook" => "Visual first.",
        "Explanation" => "Balanced visual + information.",
        "Observation" => "Practical guidance.",
        "Takeaway" => "Memorable ending.",
        _ => "Light educational bridge."
    };

    private static IReadOnlyList<string> RecommendationsFor(string role) => role switch
    {
        "Hook" => ["Keep text sparse.", "Let the visual create curiosity."],
        "Explanation" => ["Teach one concept.", "Balance visual evidence with concise information."],
        "Observation" => ["Prioritize actionable viewing guidance.", "Avoid adding a second science lesson."],
        "Takeaway" => ["Return to a clean visual.", "Make the final idea easy to remember."],
        _ => ["Keep the page easy to scan."]
    };
}

public sealed record GalleryEditorialSequenceStep
{
    public required string Role { get; init; }
    public required int Order { get; init; }
    public required string EditorialPurpose { get; init; }
    public required string SourceEmphasis { get; init; }
}

public sealed record GalleryReview
{
    public required string GalleryId { get; init; }
    public required string AlignmentSummary { get; init; }
    public required bool ConsumesVisualStory { get; init; }
    public required bool ConsumesStoryComposition { get; init; }
    public required bool ConsumesProductEditorialStrategy { get; init; }
    public required bool GeneratesPrompts { get; init; }
    public required bool ChangesProductionRouting { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

public interface IGalleryIntelligenceAlignmentEngine
{
    GalleryIntelligenceContract Create(VisualStory story, StoryCompositionResult composition, ProductEditorialStrategyResult strategy);
    Task<GalleryReview> WriteDiagnosticsAsync(VisualStory story, StoryCompositionResult composition, ProductEditorialStrategyResult strategy, string outputFolder, CancellationToken cancellationToken = default);
}

public sealed class GalleryIntelligenceAlignmentEngine : IGalleryIntelligenceAlignmentEngine
{
    public const string Version = "4.5D";
    private readonly InformationDensityDirector informationDensityDirector = new();
    private readonly NarrativeFlowDirector narrativeFlowDirector = new();

    public GalleryIntelligenceContract Create(VisualStory story, StoryCompositionResult composition, ProductEditorialStrategyResult strategy)
    {
        var galleryComposition = composition.GalleryComposition.Decision;
        var galleryStrategy = strategy.GalleryEditorialStrategy.Strategy;
        var sequence = BuildSequence(story, galleryStrategy);
        var densityReview = informationDensityDirector.Review($"gallery_{story.StoryId}".ToLowerInvariant(), sequence.PageDefinitions);
        var narrativeFlow = narrativeFlowDirector.CreateFlow($"gallery_{story.StoryId}".ToLowerInvariant(), sequence);
        sequence = sequence with { InformationDensity = densityReview.PageDensity, NarrativeFlow = narrativeFlow };

        return new GalleryIntelligenceContract
        {
            ProductId = $"gallery_{story.StoryId}".ToLowerInvariant(),
            ProductType = "Gallery",
            StoryId = story.StoryId,
            StoryVersion = story.StoryVersion,
            EditorialDecisionId = story.StoryId,
            VisualStoryId = story.StoryId,
            StoryCompositionId = galleryComposition.CompositionId,
            ProductEditorialStrategyId = galleryStrategy.StrategyId,
            ViewerQuestion = story.ViewerQuestion,
            PrimaryStory = story.PrimaryStory,
            ViewerTakeaway = story.ViewerTakeaway,
            EditorialGoal = galleryStrategy.EditorialGoal,
            ViewerEmotion = galleryStrategy.ViewerEmotion,
            EditorialSequence = sequence,
            LearningObjectives = sequence.LearningObjectives,
            PageDefinitions = sequence.PageDefinitions,
            DocumentaryTone = story.DocumentaryTone,
            RecommendedComposition = galleryComposition.RecommendedHierarchy,
            RecommendedTypography = "Use existing Gallery typography system; architecture-only contract.",
            RecommendedInformationDensity = "Progressive",
            RecommendedVisualBalance = story.RecommendedNegativeSpace,
            RecommendedPlatformRecommendations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["gallery"] = galleryComposition.Platform.ToString() },
            CreativeConfidence = Math.Clamp(new[] { story.StoryConfidence, galleryComposition.Confidence, galleryStrategy.Confidence }.Average(), 0, 1),
            CreativeVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["galleryIntelligenceAlignment"] = Version,
                ["visualStory"] = story.StoryVersion,
                ["storyComposition"] = galleryComposition.CompositionVersion,
                ["productEditorialStrategy"] = galleryStrategy.Version
            }
        };
    }

    public async Task<GalleryReview> WriteDiagnosticsAsync(VisualStory story, StoryCompositionResult composition, ProductEditorialStrategyResult strategy, string outputFolder, CancellationToken cancellationToken = default)
    {
        var contract = Create(story, composition, strategy);
        var review = new GalleryReview
        {
            GalleryId = contract.GalleryId,
            AlignmentSummary = "Gallery consumes Visual Story, Story Composition, and Product Editorial Strategy without owning story generation.",
            ConsumesVisualStory = true,
            ConsumesStoryComposition = true,
            ConsumesProductEditorialStrategy = true,
            GeneratesPrompts = false,
            ChangesProductionRouting = false,
            Diagnostics = ["No image generation changes.", "No prompt changes.", "No production Gallery routing changes."]
        };

        Directory.CreateDirectory(outputFolder);
        var json = Astronomy.MediaFactory.Contracts.VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "GalleryIntelligenceContract.json"), JsonSerializer.Serialize(contract, json), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "GalleryEditorialSequence.json"), JsonSerializer.Serialize(contract.EditorialSequence, json), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "GalleryReview.json"), JsonSerializer.Serialize(review, json), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "GalleryInformationDensityReview.json"), JsonSerializer.Serialize(informationDensityDirector.Review(contract.GalleryId, contract.EditorialSequence.PageDefinitions), json), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "GalleryNarrativeFlowReview.json"), JsonSerializer.Serialize(narrativeFlowDirector.Review(contract.GalleryId, contract.NarrativeFlow), json), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "EditorialProductReview.json"), JsonSerializer.Serialize(EditorialProductContractDiagnostics.CreateReview(), json), cancellationToken);
        return review;
    }

    private static GalleryEditorialSequence BuildSequence(VisualStory story, EditorialStrategy strategy)
    {
        var isPlanetPairing = ContainsAny(story.StoryId + story.StoryTitle + story.PrimaryStory, "PlanetPairing", "Venus", "Jupiter", "conjunction", "close approach");
        var confidence = Math.Clamp(story.StoryConfidence, 0, 1);
        var pages = isPlanetPairing
            ? BuildPlanetPairingPages(story, strategy, confidence)
            : BuildDefaultPages(story, strategy, confidence);

        return new GalleryEditorialSequence
        {
            SequenceId = $"gallery_sequence_{story.StoryId}".ToLowerInvariant(),
            Objective = "Teach visually.",
            PageDefinitions = pages,
            LearningObjectives = BuildLearningObjectives(story, strategy),
            ViewerJourney = pages.Select(page => $"Page {page.PageNumber}: viewer asks '{page.ViewerQuestion}' and learns '{page.KeyLearning}'").ToArray(),
            StoryProgression = pages.Select(page => page.PageRole).ToArray(),
            EditorialRecommendations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["architectureOnly"] = "Diagnostics-only contract; production Gallery rendering is unchanged.",
                ["informationDensity"] = "Progress from very-low-density hook to medium educational pages and back to low-density takeaway.",
                ["composition"] = story.RecommendedComposition,
                ["visualFocus"] = story.RecommendedViewerFocus
            }
        };
    }

    private static IReadOnlyList<EditorialPage> BuildPlanetPairingPages(VisualStory story, EditorialStrategy strategy, double confidence) =>
    [
        Page(1, "Hook", "Create curiosity with the unusual closeness of two bright planets.", "Why are two bright planets so close tonight?", "Two bright planets appear unusually close.", "The apparent gap between the two bright planets.", "Very Low", "Wide sky view with both planets sharing negative space.", ["Visual first.", "Do not explain everything on page 1.", "Use the closeness as the narrative hook."], confidence),
        Page(2, "Discovery", "Identify the objects so the viewer knows what they are seeing.", "Which planets are they?", "Identify Jupiter and Venus.", "Jupiter and Venus as distinct bright points.", "Low", "Balanced pairing with subtle identity emphasis for both planets.", ["Keep object identification simple.", "Avoid making either planet dominate the story."], confidence),
        Page(3, "Explanation", "Explain the astronomy behind the apparent pairing.", "Are Jupiter and Venus actually close in space?", "Conjunctions occur when planets line up from our viewpoint on Earth.", "Earth-view geometry and apparent alignment.", "Medium", "Relationship-first composition that supports a simple cause-and-effect explanation.", [strategy.RecommendedScienceEmphasis, "Explain apparent closeness without replacing prompts."], confidence),
        Page(4, "Observation", "Turn the learning into practical viewing guidance.", "Where and when should I look?", "Observe from the recommended local direction and time window.", "Horizon, direction, and viewing window.", "Medium", "Observation-guide layout with clear sky context and room for existing overlay systems.", ["Practical guidance.", strategy.RecommendedObservationEmphasis, "Diagnostics only; no production overlay changes."], confidence),
        Page(5, "Takeaway", "Close with a memorable reminder that reinforces the learning.", "What should I remember?", "Planet pairings are viewpoint events: the planets look close even while separated by vast distances.", "A memorable final view of the planetary pair.", "Low", "Clean closing frame with the pair and generous negative space.", ["Memorable ending.", story.ViewerTakeaway, "End with an interesting fact or reminder."], confidence)
    ];

    private static IReadOnlyList<EditorialPage> BuildDefaultPages(VisualStory story, EditorialStrategy strategy, double confidence) =>
    [
        Page(1, "Hook", "Open with the viewer question and visual reason to continue learning.", story.ViewerQuestion, story.PrimaryStory, story.PrimaryVisualSubject, "Very Low", story.RecommendedComposition, ["Visual first.", "Hook first; explanation later."], confidence),
        Page(2, "Discovery", "Name what the viewer is seeing without inventing a new story.", "What am I seeing?", story.PrimaryStory, story.RecommendedViewerFocus, "Low", story.RecommendedComposition, ["Use source story only."], confidence),
        Page(3, "Explanation", "Explain the visual relationship from the shared Visual Story Model.", "Why does it happen?", strategy.RecommendedScienceEmphasis, story.VisualRelationship, "Medium", story.RecommendedComposition, ["Balanced visual + information.", "Teach one idea on this page."], confidence),
        Page(4, "Observation", "Translate the story into observation guidance.", "How can I observe it?", strategy.RecommendedObservationEmphasis, story.EnvironmentRecommendation, "Medium", story.RecommendedComposition, ["Practical guidance.", "Favor practical observing clarity."], confidence),
        Page(5, "Takeaway", "End with the learning result the viewer should remember.", "What should I remember?", story.ViewerTakeaway, story.PrimaryVisualSubject, "Low", story.RecommendedComposition, ["Memorable ending.", "Close with a memorable learning outcome."], confidence)
    ];

    private static EditorialPage Page(int number, string role, string goal, string question, string learning, string focus, string density, string composition, IReadOnlyList<string> notes, double confidence) => new()
    {
        PageNumber = number,
        PageRole = role,
        PageGoal = goal,
        ViewerQuestion = question,
        KeyLearning = learning,
        RecommendedVisualFocus = focus,
        RecommendedInformationDensity = density,
        RecommendedComposition = composition,
        EditorialNotes = notes.Where(note => !string.IsNullOrWhiteSpace(note)).ToArray(),
        Confidence = confidence
    };

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> BuildLearningObjectives(VisualStory story, EditorialStrategy strategy) =>
    [
        $"Answer: {story.ViewerQuestion}",
        $"Understand: {story.ViewerTakeaway}",
        $"Observe: {strategy.RecommendedObservationEmphasis}"
    ];
}
