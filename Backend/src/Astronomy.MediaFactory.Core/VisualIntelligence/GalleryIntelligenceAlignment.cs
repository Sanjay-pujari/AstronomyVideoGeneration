using System.Text.Json;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record GalleryIntelligenceContract
{
    public required string GalleryId { get; init; }
    public required string VisualStoryId { get; init; }
    public required string StoryCompositionId { get; init; }
    public required string GalleryEditorialStrategyId { get; init; }
    public required string ViewerQuestion { get; init; }
    public required string PrimaryStory { get; init; }
    public required IReadOnlyList<string> StoryProgression { get; init; }
    public required GalleryEditorialSequence EditorialSequence { get; init; }
    public required IReadOnlyList<string> LearningObjectives { get; init; }
    public required string DocumentaryTone { get; init; }
    public required string RecommendedComposition { get; init; }
    public required string RecommendedPlatform { get; init; }
    public required double Confidence { get; init; }
    public required IReadOnlyDictionary<string, string> Versions { get; init; }
}

public sealed record GalleryEditorialSequence
{
    public required string SequenceId { get; init; }
    public string Objective { get; init; } = "Teach visually.";
    public IReadOnlyList<GalleryEditorialSequenceStep> Steps { get; init; } = [];
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
    public const string Version = "4.5A";

    public GalleryIntelligenceContract Create(VisualStory story, StoryCompositionResult composition, ProductEditorialStrategyResult strategy)
    {
        var galleryComposition = composition.GalleryComposition.Decision;
        var galleryStrategy = strategy.GalleryEditorialStrategy.Strategy;
        var sequence = BuildSequence(story, galleryStrategy);

        return new GalleryIntelligenceContract
        {
            GalleryId = $"gallery_{story.StoryId}".ToLowerInvariant(),
            VisualStoryId = story.StoryId,
            StoryCompositionId = galleryComposition.CompositionId,
            GalleryEditorialStrategyId = galleryStrategy.StrategyId,
            ViewerQuestion = story.ViewerQuestion,
            PrimaryStory = story.PrimaryStory,
            StoryProgression = story.StoryArc.Count > 0 ? story.StoryArc : ["Discovery", "Understanding", "Observation", "Takeaway"],
            EditorialSequence = sequence,
            LearningObjectives = BuildLearningObjectives(story, galleryStrategy),
            DocumentaryTone = story.DocumentaryTone,
            RecommendedComposition = galleryComposition.RecommendedHierarchy,
            RecommendedPlatform = galleryComposition.Platform.ToString(),
            Confidence = Math.Clamp(new[] { story.StoryConfidence, galleryComposition.Confidence, galleryStrategy.Confidence }.Average(), 0, 1),
            Versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
        return review;
    }

    private static GalleryEditorialSequence BuildSequence(VisualStory story, EditorialStrategy strategy) => new()
    {
        SequenceId = $"gallery_sequence_{story.StoryId}".ToLowerInvariant(),
        Steps =
        [
            new GalleryEditorialSequenceStep { Order = 1, Role = "Hook", EditorialPurpose = "Open with the viewer question and visual reason to continue learning.", SourceEmphasis = story.ViewerQuestion },
            new GalleryEditorialSequenceStep { Order = 2, Role = "Discovery", EditorialPurpose = "Name what the viewer is seeing without inventing a new story.", SourceEmphasis = story.PrimaryStory },
            new GalleryEditorialSequenceStep { Order = 3, Role = "Explanation", EditorialPurpose = "Explain the visual relationship from the shared Visual Story Model.", SourceEmphasis = strategy.RecommendedScienceEmphasis },
            new GalleryEditorialSequenceStep { Order = 4, Role = "Observation", EditorialPurpose = "Translate the story into observation guidance.", SourceEmphasis = strategy.RecommendedObservationEmphasis },
            new GalleryEditorialSequenceStep { Order = 5, Role = "Takeaway", EditorialPurpose = "End with the learning result the viewer should remember.", SourceEmphasis = story.ViewerTakeaway }
        ]
    };

    private static IReadOnlyList<string> BuildLearningObjectives(VisualStory story, EditorialStrategy strategy) =>
    [
        $"Answer: {story.ViewerQuestion}",
        $"Understand: {story.ViewerTakeaway}",
        $"Observe: {strategy.RecommendedObservationEmphasis}"
    ];
}
