using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests;

public sealed class GalleryIntelligenceAlignmentEngineTests
{
    private readonly StoryCompositionEngine compositionEngine = new();
    private readonly ProductEditorialStrategyEngine strategyEngine = new();
    private readonly GalleryIntelligenceAlignmentEngine engine = new();

    [Fact]
    public void Create_Generates_GalleryIntelligenceContract_From_V4_Stack()
    {
        var story = TestStory();
        var composition = compositionEngine.Compose(story);
        var strategy = strategyEngine.Create(story, composition);

        var contract = engine.Create(story, composition, strategy);

        Assert.Equal(story.StoryId, contract.VisualStoryId);
        Assert.Equal(composition.GalleryComposition.Decision.CompositionId, contract.StoryCompositionId);
        Assert.Equal(strategy.GalleryEditorialStrategy.Strategy.StrategyId, contract.GalleryEditorialStrategyId);
        Assert.Equal(story.PrimaryStory, contract.PrimaryStory);
        Assert.Equal("Teach visually.", contract.EditorialSequence.Objective);
        Assert.Equal(5, contract.EditorialSequence.Steps.Count);
        Assert.Equal("4.5A", contract.Versions["galleryIntelligenceAlignment"]);
    }

    [Fact]
    public async Task Diagnostics_Writes_Gallery_Contract_Sequence_And_Review()
    {
        var story = TestStory();
        var composition = compositionEngine.Compose(story);
        var strategy = strategyEngine.Create(story, composition);
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

        var review = await engine.WriteDiagnosticsAsync(story, composition, strategy, folder);

        Assert.True(review.ConsumesVisualStory);
        Assert.True(review.ConsumesStoryComposition);
        Assert.True(review.ConsumesProductEditorialStrategy);
        Assert.False(review.GeneratesPrompts);
        Assert.False(review.ChangesProductionRouting);
        Assert.True(File.Exists(Path.Combine(folder, "GalleryIntelligenceContract.json")));
        Assert.True(File.Exists(Path.Combine(folder, "GalleryEditorialSequence.json")));
        Assert.True(File.Exists(Path.Combine(folder, "GalleryReview.json")));
    }

    [Fact]
    public void EditorialSequence_Uses_Required_Progression_Without_Prompt_Generation()
    {
        var story = TestStory();
        var composition = compositionEngine.Compose(story);
        var strategy = strategyEngine.Create(story, composition);

        var contract = engine.Create(story, composition, strategy);

        Assert.Equal(["Hook", "Discovery", "Explanation", "Observation", "Takeaway"], contract.EditorialSequence.Steps.Select(step => step.Role).ToArray());
        Assert.All(contract.EditorialSequence.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step.SourceEmphasis)));
    }

    [Fact]
    public void GalleryArtifactManifest_Registers_New_Diagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

        var manifest = OutputArtifactRegistry.CreateGalleryArtifactManifest(root, new OutputArtifactsOptions { Mode = OutputArtifactMode.Debug });

        Assert.Contains(OutputArtifactName.GalleryIntelligenceContract.ToString(), manifest.ExpectedArtifacts);
        Assert.Contains(OutputArtifactName.GalleryEditorialSequence.ToString(), manifest.ExpectedArtifacts);
        Assert.Contains(OutputArtifactName.GalleryReview.ToString(), manifest.ExpectedArtifacts);
        Assert.Equal(OutputArtifactRegistry.GetPath(root, OutputArtifactName.GalleryIntelligenceContract), manifest.Artifacts[OutputArtifactName.GalleryIntelligenceContract.ToString()]);
    }

    [Fact]
    public void Alignment_Does_Not_Change_Production_Gallery_Contract()
    {
        var ctor = typeof(AstroPulseGalleryResult).GetConstructors().Single();
        var parameterNames = ctor.GetParameters().Select(parameter => parameter.Name).ToArray();

        Assert.Equal(["OutputDirectory", "ImagePaths", "ReviewPath", "ManifestPath", "DiagnosticsPath", "ValidationPath"], parameterNames);
    }

    private static VisualStory TestStory() => new()
    {
        StoryId = "PlanetPairing-gallery-alignment-test",
        StoryTitle = "Venus and Jupiter close approach",
        ViewerQuestion = "Why do these planets look close?",
        PrimaryStory = "Two bright planets appear unusually close together.",
        ViewerTakeaway = "This is an apparent conjunction.",
        EmotionalHook = "Wonder.",
        StoryArc = ["Discovery", "Understanding", "Observation", "Wonder", "Action"],
        PrimaryVisualSubject = "Relationship",
        SecondaryVisualSubjects = ["Venus", "Jupiter"],
        VisualRelationship = "The apparent conjunction relationship is the subject; do not prioritize the largest planet.",
        RecommendedComposition = "Balanced pairing",
        RecommendedViewerFocus = "Relationship first",
        DocumentaryTone = "Documentary",
        EnvironmentRecommendation = "Observed sky realism.",
        LightingRecommendation = "Natural twilight documentary lighting.",
        RecommendedNegativeSpace = "Shared negative space around both planets.",
        RecommendedOverlayZones = ["lower third"],
        StoryConfidence = .91,
        CreativeKnowledgeVersion = CreativeKnowledgeLibrary.Version,
        EditorialReasoningVersion = "4.3A"
    };
}
