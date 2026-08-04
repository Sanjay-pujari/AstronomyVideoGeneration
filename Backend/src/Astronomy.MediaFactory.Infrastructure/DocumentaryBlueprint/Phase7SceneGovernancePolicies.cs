using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>
/// Compatibility rule for the frozen Phase 6 contract, which exposes only an authored ordered list.
/// Phase 6 documented the first item as the scene's governing reference and the remaining items as
/// supporting requirements; consequently all are required and only the first is primary.
/// </summary>
public sealed class Phase7SceneReferenceCompatibilityPolicy : IPhase7SceneReferenceCompatibilityPolicy
{
    public const string Reason = "P7PACKET_REFERENCE_COMPAT_PHASE6_ORDERED_PRIMARY";
    public Phase7SceneReferenceProjectionResult Project(StoryFrameAuthorityFrame frame)
    {
        if (frame.KnowledgeReferenceIds.Count == 0 || frame.KnowledgeReferenceIds.Any(string.IsNullOrWhiteSpace))
            return new(false, [], "P7PACKET_REFERENCE_REQUIREMENTS_UNRESOLVED", [],
                ["The frozen Phase 6 reference collection cannot be classified."], true, false);
        var requirements = frame.KnowledgeReferenceIds.Select((id, index) =>
            new Phase7SceneReferenceRequirement(id, frame.Variant, index == 0, true,
                "Phase7SceneReferenceCompatibilityPolicy", $"frames/{frame.FrameId}/knowledgeReferenceIds/{index}"))
            .ToArray();
        return new(true, requirements, Reason,
            ["Reference roles were projected by the governed frozen-Phase-6 compatibility rule."], [], true, false);
    }
}

/// <summary>The frozen source-scene contract explicitly defines NarrativeStage as its profile slot.</summary>
public sealed class Phase7SceneSectionAuthorityResolver : IPhase7SceneSectionAuthorityResolver
{
    public Phase7SceneSectionAuthorityResolution Resolve(StoryFrameAuthorityFrame frame, StoryFrameSceneIndex source)
    {
        if (source.SceneId != frame.SceneId || source.Variant != frame.Variant ||
            string.IsNullOrWhiteSpace(source.NarrativeStage) || string.IsNullOrWhiteSpace(source.SceneRole))
            return new(false, "", source.NarrativeStage, source.SceneRole, "", "P7PACKET_SECTION_AUTHORITY_MISSING");
        return new(true, source.NarrativeStage, source.NarrativeStage, source.SceneRole,
            "StoryFrameSceneIndex.NarrativeStage (frozen profile-slot compatibility)",
            "P7PACKET_SECTION_COMPAT_SOURCE_SCENE_NARRATIVE_STAGE");
    }
}
