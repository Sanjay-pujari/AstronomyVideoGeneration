using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;

namespace Astronomy.MediaFactory.Tests;

public sealed class EditorialProductContractTests
{
    [Fact]
    public void Hero_And_Gallery_Inherit_EditorialProductContract()
    {
        Assert.True(typeof(HeroIntelligenceContract).IsSubclassOf(typeof(EditorialProductContract)));
        Assert.True(typeof(GalleryIntelligenceContract).IsSubclassOf(typeof(EditorialProductContract)));
    }

    [Fact]
    public void EditorialProductReview_Validates_Inheritance_And_Field_Sets()
    {
        var review = EditorialProductContractDiagnostics.CreateReview();

        Assert.Contains(nameof(EditorialProductContract.ProductId), review.SharedFields);
        Assert.Contains(nameof(HeroIntelligenceContract.HeroSpecificFields), review.HeroSpecificFields);
        Assert.Contains(nameof(GalleryIntelligenceContract.EditorialSequence), review.GallerySpecificFields);
        Assert.True(review.InheritanceValidation.HeroDerivesFromEditorialProductContract);
        Assert.True(review.InheritanceValidation.GalleryDerivesFromEditorialProductContract);
        Assert.True(review.InheritanceValidation.HeroContainsAllSharedFields);
        Assert.True(review.InheritanceValidation.GalleryContainsAllSharedFields);
        Assert.True(review.InheritanceValidation.ArchitectureOnly);
        Assert.False(review.InheritanceValidation.ChangesRendering);
        Assert.False(review.InheritanceValidation.ChangesPrompts);
        Assert.False(review.InheritanceValidation.ChangesAzure);
        Assert.False(review.InheritanceValidation.ChangesProductionRouting);
    }

    [Fact]
    public void Hero_EditorialProductContract_Serializes_Shared_And_Specific_Fields()
    {
        var contract = new HeroIntelligenceContract
        {
            ProductId = "hero-story-1",
            ProductType = "Hero",
            StoryId = "story-1",
            StoryVersion = "4.5D-test",
            HeroSpecificFields = new HeroSpecificFields
            {
                PlanId = "plan-1",
                EventType = "planet-conjunction",
                EventFamily = "PlanetConjunction",
                EmotionalHook = "Wonder.",
                CompositionGoal = "Show the apparent relationship.",
                VisualRelationship = "Neither planet dominates.",
                ConfidenceSummary = new HeroIntelligenceConfidenceSummary(.9, .9, .9, .9, null)
            },
            EditorialDecisionId = "decision-1",
            VisualStoryId = "story-1",
            StoryCompositionId = "composition-hero",
            ProductEditorialStrategyId = "strategy-hero",
            ViewerQuestion = "Why are they close?",
            PrimaryStory = "Two planets appear close.",
            ViewerTakeaway = "The closeness is apparent from Earth.",
            EditorialGoal = "Stop scrolling.",
            ViewerEmotion = "Wonder.",
            DocumentaryTone = "premium documentary",
            RecommendedComposition = "relationship-first composition",
            RecommendedTypography = "existing Hero typography",
            RecommendedInformationDensity = "Low",
            RecommendedVisualBalance = "shared negative space",
            RecommendedPlatformRecommendations = new Dictionary<string, string> { ["landscape"] = "shared negative space" },
            CreativeConfidence = .9,
            CreativeVersions = new Dictionary<string, string> { ["editorialProductContract"] = "4.5D" }
        };

        var json = JsonSerializer.Serialize(contract, VisualIntelligenceJson.CreateSerializerOptions());
        var roundTrip = JsonSerializer.Deserialize<HeroIntelligenceContract>(json, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.Equal("hero-story-1", roundTrip!.ProductId);
        Assert.Equal("composition-hero", roundTrip.CompositionId);
        Assert.Equal("Wonder.", roundTrip.EmotionalHook);
    }
}
