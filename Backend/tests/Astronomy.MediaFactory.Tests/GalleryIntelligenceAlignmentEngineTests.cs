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
        Assert.Equal("4.5D", contract.Versions["galleryIntelligenceAlignment"]);
        Assert.Equal(["Wonder", "Curiosity", "Understanding", "Observation", "Memory"], contract.NarrativeFlow.EmotionalProgression);
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
        Assert.True(File.Exists(Path.Combine(folder, "GalleryInformationDensityReview.json")));
        Assert.True(File.Exists(Path.Combine(folder, "GalleryNarrativeFlowReview.json")));
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
    public void EditorialSequence_Defines_Five_EditorialPages_With_Required_Default_Progression()
    {
        var story = TestStory();
        var composition = compositionEngine.Compose(story);
        var strategy = strategyEngine.Create(story, composition);

        var contract = engine.Create(story, composition, strategy);

        Assert.Equal(5, contract.EditorialSequence.PageDefinitions.Count);
        Assert.Equal([1, 2, 3, 4, 5], contract.EditorialSequence.PageDefinitions.Select(page => page.PageNumber).ToArray());
        Assert.Equal(["Hook", "Discovery", "Explanation", "Observation", "Takeaway"], contract.EditorialSequence.PageDefinitions.Select(page => page.PageRole).ToArray());
        Assert.All(contract.EditorialSequence.PageDefinitions, page =>
        {
            Assert.False(string.IsNullOrWhiteSpace(page.PageGoal));
            Assert.False(string.IsNullOrWhiteSpace(page.ViewerQuestion));
            Assert.False(string.IsNullOrWhiteSpace(page.KeyLearning));
            Assert.False(string.IsNullOrWhiteSpace(page.RecommendedVisualFocus));
            Assert.False(string.IsNullOrWhiteSpace(page.RecommendedInformationDensity));
            Assert.False(string.IsNullOrWhiteSpace(page.RecommendedComposition));
            Assert.InRange(page.Confidence, 0, 1);
        });
    }

    [Fact]
    public void InformationDensityDirector_Defines_Natural_Density_Progression()
    {
        var story = TestStory();
        var composition = compositionEngine.Compose(story);
        var strategy = strategyEngine.Create(story, composition);

        var sequence = engine.Create(story, composition, strategy).EditorialSequence;

        Assert.Equal(["Hook", "Discovery", "Explanation", "Observation", "Takeaway"], sequence.InformationDensity.Select(page => page.PageRole).ToArray());
        Assert.Equal(["Very Low", "Low", "Medium", "Medium", "Low"], sequence.InformationDensity.Select(page => page.PageDensity).ToArray());
        Assert.Equal("Very High", sequence.InformationDensity[0].VisualWeight);
        Assert.Equal("Balanced visual + information.", sequence.InformationDensity[2].EducationalGuidance);
        Assert.Equal("Practical guidance.", sequence.InformationDensity[3].EducationalGuidance);
        Assert.Equal("Memorable ending.", sequence.InformationDensity[4].EducationalGuidance);
    }

    [Fact]
    public async Task InformationDensityDiagnostics_Include_Required_Review_Sections()
    {
        var story = TestStory();
        var composition = compositionEngine.Compose(story);
        var strategy = strategyEngine.Create(story, composition);
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

        await engine.WriteDiagnosticsAsync(story, composition, strategy, folder);

        var json = await File.ReadAllTextAsync(Path.Combine(folder, "GalleryInformationDensityReview.json"));
        Assert.Contains("\"pageDensity\"", json);
        Assert.Contains("\"visualWeight\"", json);
        Assert.Contains("\"textWeight\"", json);
        Assert.Contains("\"learningComplexity\"", json);
        Assert.Contains("\"viewerLoad\"", json);
        Assert.Contains("\"recommendations\"", json);
        Assert.Contains("\"generatesPrompts\": false", json);
        Assert.Contains("\"changesProductionRouting\": false", json);
    }

    [Fact]
    public void EditorialSequence_PlanetPairing_Uses_Astronomy_Teaching_Sequence()
    {
        var story = TestStory();
        var composition = compositionEngine.Compose(story);
        var strategy = strategyEngine.Create(story, composition);

        var sequence = engine.Create(story, composition, strategy).EditorialSequence;

        Assert.Contains("Two bright planets appear unusually close.", sequence.PageDefinitions[0].KeyLearning);
        Assert.Contains("Jupiter and Venus", sequence.PageDefinitions[1].KeyLearning);
        Assert.Contains("Conjunctions occur", sequence.PageDefinitions[2].KeyLearning);
        Assert.Contains("Observe", sequence.PageDefinitions[3].KeyLearning);
        Assert.Contains("viewpoint", sequence.PageDefinitions[4].KeyLearning);
    }

    [Fact]
    public void EditorialSequence_Serializes_PageDefinitions_And_Diagnostic_Sections()
    {
        var story = TestStory();
        var composition = compositionEngine.Compose(story);
        var strategy = strategyEngine.Create(story, composition);
        var jsonOptions = Contracts.VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true);

        var json = System.Text.Json.JsonSerializer.Serialize(engine.Create(story, composition, strategy).EditorialSequence, jsonOptions);

        Assert.Contains("\"pageDefinitions\"", json);
        Assert.Contains("\"pageNumber\"", json);
        Assert.Contains("\"pageRole\"", json);
        Assert.Contains("\"learningObjectives\"", json);
        Assert.Contains("\"viewerJourney\"", json);
        Assert.Contains("\"storyProgression\"", json);
        Assert.Contains("\"editorialRecommendations\"", json);
        Assert.Contains("\"informationDensity\"", json);
        Assert.Contains("\"narrativeFlow\"", json);
    }

    [Fact]
    public void NarrativeFlowDirector_Defines_Required_Editorial_Journey()
    {
        var story = TestStory();
        var composition = compositionEngine.Compose(story);
        var strategy = strategyEngine.Create(story, composition);

        var flow = engine.Create(story, composition, strategy).NarrativeFlow;

        Assert.Equal(["Hook", "Wonder", "Discovery", "Curiosity", "Explanation", "Understanding", "Observation", "Practical application", "Takeaway", "Memory"], flow.Stages.Select(stage => stage.Stage).ToArray());
        Assert.Equal(["Wonder", "Curiosity", "Understanding", "Observation", "Memory"], flow.EmotionalProgression);
        Assert.False(flow.GeneratesPrompts);
        Assert.False(flow.ChangesProductionGallery);
    }

    [Fact]
    public async Task NarrativeFlowDiagnostics_Include_Required_Review_Sections()
    {
        var story = TestStory();
        var composition = compositionEngine.Compose(story);
        var strategy = strategyEngine.Create(story, composition);
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

        await engine.WriteDiagnosticsAsync(story, composition, strategy, folder);

        var json = await File.ReadAllTextAsync(Path.Combine(folder, "GalleryNarrativeFlowReview.json"));
        Assert.Contains("\"emotionalJourney\"", json);
        Assert.Contains("\"learningJourney\"", json);
        Assert.Contains("\"viewerProgression\"", json);
        Assert.Contains("\"storyContinuity\"", json);
        Assert.Contains("\"educationalFlow\"", json);
        Assert.Contains("\"recommendations\"", json);
        Assert.Contains("\"generatesPrompts\": false", json);
        Assert.Contains("\"changesProductionGallery\": false", json);
    }

    [Fact]
    public void GalleryArtifactManifest_Registers_New_Diagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

        var manifest = OutputArtifactRegistry.CreateGalleryArtifactManifest(root, new OutputArtifactsOptions { Mode = OutputArtifactMode.Debug });

        Assert.Contains(OutputArtifactName.GalleryIntelligenceContract.ToString(), manifest.ExpectedArtifacts);
        Assert.Contains(OutputArtifactName.GalleryEditorialSequence.ToString(), manifest.ExpectedArtifacts);
        Assert.Contains(OutputArtifactName.GalleryReview.ToString(), manifest.ExpectedArtifacts);
        Assert.Contains(OutputArtifactName.GalleryInformationDensityReview.ToString(), manifest.ExpectedArtifacts);
        Assert.Contains(OutputArtifactName.GalleryNarrativeFlowReview.ToString(), manifest.ExpectedArtifacts);
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
