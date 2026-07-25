using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using DocumentaryBlueprintModel = Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryBlueprintValidationFixture
{
    public static DocumentaryBlueprintModel Create(params DocumentarySceneBlueprint[]? scenes) => new(
        "documentary.orion.validation.v1", "knowledge.orion.v1", "orion", "Orion",
        BlueprintPublicationFormat.LongDocumentary, "en-US", "1",
        new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), "editor", "model-v1", "knowledge-v1", "1.0", "orion-validation"),
        scenes is { Length: > 0 } ? scenes : [Scene(1, DocumentarySceneRole.OpeningHook), Scene(2, DocumentarySceneRole.ScientificExplanation), Scene(3, DocumentarySceneRole.ReflectiveClosing)]);

    public static DocumentaryBlueprintModel Empty() => new("documentary.orion.validation.v1", "knowledge.orion.v1", "orion", "Orion", BlueprintPublicationFormat.LongDocumentary, "en-US", "1",
        new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), "editor", "model-v1", "knowledge-v1", "1.0", "orion-validation"), []);

    public static DocumentarySceneBlueprint Scene(int number, DocumentarySceneRole role, string? id = null,
        string? question = null, IReadOnlyList<KnowledgeReference>? knowledge = null,
        IReadOnlyList<VisualOpportunity>? visuals = null, int duration = 60,
        EditorialPriority priority = EditorialPriority.High, EditorialOutcome? outcome = null) => new(
        id ?? $"scene.orion.{number}", number, $"Orion Scene {number}", DocumentaryNarrativeStage.Science, role,
        new(question ?? $"What does Orion scene {number} reveal?"),
        new("Explore Orion.", "Learn astronomy.", "Sustain curiosity.", "Create wonder."),
        outcome ?? new("Understand Orion.", "Advance the documentary.", true, true, true,
            role == DocumentarySceneRole.PracticalObservation, role == DocumentarySceneRole.ReflectiveClosing),
        priority, knowledge ?? [new($"orion.knowledge.{number}", "Science", "Support scene.", true)],
        visuals ?? [new("Show Orion.", "SkyMap", $"orion.knowledge.{number}", null, true)],
        new("Continue.", "What comes next?", "Advance."), duration);
}
