using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryBlueprintBuilderFixture
{
    public static DocumentarySceneBlueprintInput Scene(string id = "scene.orion.belt", int number = 1,
        IReadOnlyList<KnowledgeReference>? references = null, IReadOnlyList<VisualOpportunity>? visuals = null) => new(
        id, number, "The Three Stars Everyone Knows", DocumentaryNarrativeStage.Recognition,
        DocumentarySceneRole.RecognitionGuide, new ViewerQuestion("Why are Orion's Belt stars so famous?"),
        new SceneObjective("Orient the viewer to Orion's Belt.", "Recognize the three belt stars.", "Invite investigation of their apparent alignment.", "Create familiar wonder."),
        new EditorialOutcome("The Belt is recognizable and physically surprising.", "Establishes the documentary landmark.", true, true, true, false, false), EditorialPriority.Critical,
        references ?? [new("orion.recognition.belt", "Recognition", "Identify the Belt pattern.", true), new("orion.fact.belt-distance", "Science", "Contrast apparent and physical distance.", false)],
        visuals ?? [new("Show the three-star line in its constellation context.", "SkyMap", "orion.recognition.belt", null, true), new("Compare the Belt stars' apparent spacing.", "Diagram", "orion.fact.belt-distance", "asset.orion.belt-depth", false)],
        new SceneTransition("Move from recognition to three-dimensional distance.", "Are the stars actually close together?", "Widen from appearance to physical structure."), 75);

    public static DocumentarySceneBlueprintInput DistanceScene() => new(
        "scene.orion.distance", 2, "The Distance Hidden in a Familiar Line", DocumentaryNarrativeStage.Science,
        DocumentarySceneRole.ScientificExplanation, new ViewerQuestion("How far apart are Orion's Belt stars in space?"),
        new SceneObjective("Reveal the Belt's three-dimensional structure.", "Distinguish apparent alignment from physical proximity.", "Invite comparison of stellar distances.", "Turn familiarity into discovery."),
        new EditorialOutcome("The Belt is a line of sight rather than a compact group.", "Deepens recognition into physical understanding.", true, true, true, false, false), EditorialPriority.High,
        [new("orion.fact.belt-distance", "Science", "Establish the stars' distinct distances.", true), new("orion.fact.belt-scale", "Scale", "Compare the separation at stellar scale.", false)],
        [new("Place the Belt stars on a depth diagram.", "Diagram", "orion.fact.belt-distance", null, true), new("Move from the sky view to a side-on spatial view.", "AnimationPlan", "orion.fact.belt-scale", "asset.orion.distance", false)],
        new SceneTransition("Return from spatial depth to observing the pattern.", "How can viewers find the Belt tonight?", "Connect scientific scale back to the familiar sky."), 90);

    public static DocumentaryBlueprintBuildRequest Create(IReadOnlyList<DocumentarySceneBlueprintInput>? scenes = null) => new(
        "documentary.orion.long.v1", "knowledge.orion.v1", "orion", "Orion", BlueprintPublicationFormat.LongDocumentary, "en-US", "1",
        new DocumentaryBlueprintMetadata(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), "editorial-system", "editorial-model-v1", "knowledge-v1", "1.0", "correlation-orion-001"),
        scenes ?? [Scene(), DistanceScene()]);
}
