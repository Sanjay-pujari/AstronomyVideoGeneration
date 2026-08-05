using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>
/// Governed P7.1B compatibility mapping for the frozen Phase 6 contract, which exposes an authored
/// ordered reference collection but no explicit serialized primary/required flags.
/// </summary>
public sealed class Phase7SceneReferenceCompatibilityPolicy : IPhase7SceneReferenceCompatibilityPolicy
{
    public const string Reason = "P7PACKET_REFERENCE_COMPAT_PHASE6_ORDERED_PRIMARY";

    public Phase7ReferenceCompatibilityScope Resolve(string authorityNamespace, string canonicalJsonPointer,
        string sectionKey, Phase7NarrationVariant variant)
    {
        var section = sectionKey ?? "";
        if (!string.Equals(authorityNamespace, "production-event-intelligence", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(canonicalJsonPointer))
            return Unsupported(authorityNamespace, canonicalJsonPointer, section, "P7PACKET_REQUIRED_REFERENCE_UNRESOLVED");

        var domains = DomainsFor(section);
        if (canonicalJsonPointer == "/primaryObjects")
            return Scope(authorityNamespace, canonicalJsonPointer, section, domains, PrimaryObjectPrefixes(section),
                "P7PACKET_REFERENCE_COMPAT_PHASE6_PRIMARY_OBJECTS_SCOPE");
        if (canonicalJsonPointer == "/scientificContext")
            return Scope(authorityNamespace, canonicalJsonPointer, section, domains, ScientificContextPrefixes(section),
                "P7PACKET_REFERENCE_COMPAT_PHASE6_SCIENTIFIC_CONTEXT_SCOPE");
        return Unsupported(authorityNamespace, canonicalJsonPointer, section, "P7PACKET_REQUIRED_REFERENCE_UNRESOLVED");
    }

    private static Phase7ReferenceCompatibilityScope Scope(string ns, string pointer, string section,
        IReadOnlyList<string> domains, IReadOnlyList<string> prefixes, string reason) =>
        new(true, ns, pointer, section, domains, prefixes, [Phase7KnowledgeOrigin.Event],
            "Phase7SceneReferenceCompatibilityPolicy", "phase7.scene-reference-compatibility.v1", reason);

    private static Phase7ReferenceCompatibilityScope Unsupported(string ns, string pointer, string section, string reason) =>
        new(false, ns, pointer, section, [], [], [], "Phase7SceneReferenceCompatibilityPolicy",
            "phase7.scene-reference-compatibility.v1", reason);

    private static IReadOnlyList<string> DomainsFor(string sectionKey) => sectionKey switch
    {
        "Wonder" => ["Identity", "Recognition", "InterestingFacts", "ScientificSignificance"],
        "Recognition" => ["Identity", "Recognition", "RecognitionGeometry", "PhysicalCharacteristics"],
        "Discovery" => ["KeyObjects", "ScientificStructure", "ScientificSignificance", "DeepSkyObjects"],
        "Science" => ["ScientificStructure", "PhysicalCharacteristics", "Formation", "Evolution", "ScientificSignificance", "StarFormation", "Distance"],
        "History" => ["History"],
        "Culture" => ["CultureAndMythology", "RegionalTraditions", "AstrologyClarification"],
        "ModernAstronomy" => ["ScientificSignificance", "ScientificStructure", "Astrophotography", "ImagingAppearance"],
        "Clarification" => ["ScientificSignificance", "ScientificStructure", "AstrologyClarification"],
        "Observation" => ["Observation", "Visibility", "Timing", "LocationDependence", "Equipment", "Recognition"],
        "Astrophotography" => ["Astrophotography", "ImagingAppearance", "Observation", "Equipment"],
        "Inspiration" => ["InterestingFacts", "ScientificSignificance", "CultureAndMythology"],
        _ => []
    };

    private static IReadOnlyList<string> PrimaryObjectPrefixes(string sectionKey) => sectionKey switch
    {
        "Wonder" => ["/scientific/astronomicalImportance", "/scientific/approximatePosition"],
        "Discovery" => ["/scientific/majorStars", "/scientific/orionBeltStars", "/scientific/majorDeepSkyObjects"],
        "Observation" => ["/observation/nakedEyeRecognition", "/observation/orionBeltIdentification", "/observation/binocularGuidance", "/observation/telescopeGuidance"],
        "History" => ["/history/historicalCataloguing", "/history/ancientRecognition", "/history/navigationSeasonalImportance"],
        _ => ["/scientific/majorStars", "/scientific/orionBeltStars", "/scientific/majorDeepSkyObjects", "/scientific/astronomicalImportance", "/scientific/approximatePosition", "/observation/nakedEyeRecognition", "/observation/orionBeltIdentification", "/history/historicalCataloguing"]
    };

    private static IReadOnlyList<string> ScientificContextPrefixes(string sectionKey) => sectionKey switch
    {
        "Recognition" => ["/scientific/approximatePosition", "/scientific/relativeSizeNote", "/scientific/neighboringConstellations"],
        "Clarification" => ["/scientific/starFormationContext", "/scientific/distanceCautions", "/astrologyRelationships/westernZodiacNotes"],
        _ => ["/scientific/summary", "/scientific/astronomicalImportance", "/scientific/approximatePosition", "/scientific/starFormationContext", "/scientific/distanceCautions"]
    };

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

/// <summary>Governed P7.1B compatibility mapping for the frozen Phase 6 contract, which exposes
/// NarrativeStage but no distinct serialized Phase 7 SectionKey.</summary>
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
