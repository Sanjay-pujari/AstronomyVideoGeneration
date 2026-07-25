using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintEnumTests
{
 [Fact] public void Enum_inventories_are_exactly_approved() { Assert.Equal(["Wonder","Recognition","Discovery","Science","History","Culture","ModernAstronomy","Clarification","Observation","Astrophotography","Inspiration"],Enum.GetNames<DocumentaryNarrativeStage>()); Assert.Equal(["OpeningHook","Orientation","RecognitionGuide","CoreDiscovery","ScientificExplanation","HistoricalContext","CulturalContext","MythologyContext","MisconceptionCorrection","PracticalObservation","AstrophotographyGuide","ReflectiveClosing"],Enum.GetNames<DocumentarySceneRole>()); Assert.Equal(["Critical","High","Medium","Optional"],Enum.GetNames<EditorialPriority>()); Assert.Equal(["LongDocumentary","ShortDocumentary","ObservationGuide","Article","Podcast","SocialVideo"],Enum.GetNames<BlueprintPublicationFormat>()); }
}
