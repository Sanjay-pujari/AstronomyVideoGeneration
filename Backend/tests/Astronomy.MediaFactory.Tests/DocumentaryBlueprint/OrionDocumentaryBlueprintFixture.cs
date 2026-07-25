using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryBlueprintFixture
{
    public static DocumentarySceneBlueprint Scene(string id = "scene.orion.belt", int number = 1) => new(
        id, number, "The Three Stars Everyone Knows", DocumentaryNarrativeStage.Recognition,
        DocumentarySceneRole.RecognitionGuide, new ViewerQuestion("Why are Orion's Belt stars so famous?"),
        new SceneObjective("Orient the viewer to Orion's Belt.", "Recognize the three belt stars.", "Invite investigation of their apparent alignment.", "Create familiar wonder."),
        new EditorialOutcome("The Belt is recognizable and physically surprising.", "Establishes the documentary landmark.", true, true, true, false, false), EditorialPriority.Critical,
        [new("orion.recognition.belt", "Recognition", "Identify the Belt pattern.", true), new("orion.fact.belt-distance", "Science", "Contrast apparent and physical distance.", false)],
        [new("Show the three-star line in its constellation context.", "SkyMap", "orion.recognition.belt", null, true)],
        new SceneTransition("Move from recognition to three-dimensional distance.", "Are the stars actually close together?", "Widen from appearance to physical structure."), 75);

    public static global::Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint Create(IReadOnlyList<DocumentarySceneBlueprint>? scenes = null) => new(
        "documentary.orion.long.v1", "knowledge.orion.v1", "orion", "Orion", BlueprintPublicationFormat.LongDocumentary, "en-US", "1",
        new DocumentaryBlueprintMetadata(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), "editorial-system", "editorial-model-v1", "knowledge-v1", "1.0", "correlation-orion-001"), scenes ?? [Scene()]);
}
