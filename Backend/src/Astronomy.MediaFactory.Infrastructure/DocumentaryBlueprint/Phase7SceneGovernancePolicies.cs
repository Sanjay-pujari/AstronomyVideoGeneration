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
        string sectionKey, Phase7NarrationVariant variant) =>
        Resolve(new Phase7ReferenceCompatibilityRequest(authorityNamespace, canonicalJsonPointer, sectionKey, variant,
            Phase7ReferenceRole.Required, true));

    public Phase7ReferenceCompatibilityScope Resolve(Phase7ReferenceCompatibilityRequest request)
    {
        var ns = request.AuthorityNamespace ?? "";
        var pointer = request.CanonicalJsonPointer ?? "";
        var section = request.SectionKey ?? "";
        if (!string.Equals(ns, "production-event-intelligence", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(pointer))
            return Unsupported(ns, pointer, section, "P7PACKET_REQUIRED_REFERENCE_UNRESOLVED");

        if (pointer == "/primaryObjects")
            return Scope(request, DomainsFor(section), PrimaryPreferred(section).Concat(PrimaryFallback(section)).ToArray(),
                PrimaryPreferred(section), "P7PACKET_REFERENCE_COMPAT_PHASE6_PRIMARY_OBJECTS_SCOPE");
        if (pointer == "/scientificContext")
            return Scope(request, DomainsFor(section), ScientificPreferred(section).Concat(ScientificFallback(section)).ToArray(),
                ScientificPreferred(section), "P7PACKET_REFERENCE_COMPAT_PHASE6_SCIENTIFIC_CONTEXT_SCOPE");
        return Unsupported(ns, pointer, section, "P7PACKET_REQUIRED_REFERENCE_UNRESOLVED");
    }

    private static Phase7ReferenceCompatibilityScope Scope(Phase7ReferenceCompatibilityRequest r,
        IReadOnlyList<string> domains, IReadOnlyList<string> allowed, IReadOnlyList<string> preferred, string reason) =>
        new(true, domains, Canon(allowed), Canon(preferred), [Phase7ClaimDisposition.Required, Phase7ClaimDisposition.Optional],
            r.IsRequired, "Phase7SceneReferenceCompatibilityPolicy", "phase7.scene-reference-compatibility.v2", reason)
        { AuthorityNamespace = r.AuthorityNamespace, CanonicalJsonPointer = r.CanonicalJsonPointer, SectionKey = r.SectionKey, AllowedOrigins = [Phase7KnowledgeOrigin.Event] };

    private static Phase7ReferenceCompatibilityScope Unsupported(string ns, string pointer, string section, string reason) =>
        new(false, [], [], [], [], false, "Phase7SceneReferenceCompatibilityPolicy", "phase7.scene-reference-compatibility.v2", reason)
        { AuthorityNamespace = ns, CanonicalJsonPointer = pointer, SectionKey = section, AllowedOrigins = [] };

    private static string[] Canon(IEnumerable<string> prefixes) => prefixes.Select(p => p.StartsWith("/", StringComparison.Ordinal) ? p : "/" + p.TrimEnd('.').Replace('.', '/')).Distinct(StringComparer.Ordinal).ToArray();
    private static IReadOnlyList<string> DomainsFor(string sectionKey) => sectionKey switch
    {
        "Wonder" => ["ScientificSignificance", "Identity", "Recognition", "ScientificStructure", "Observation"],
        "Discovery" => ["KeyObjects", "ScientificStructure", "Identity", "Recognition", "Observation"],
        "Observation" => ["Observation", "Recognition", "Identity", "ScientificStructure"],
        "History" => ["History", "CultureAndMythology", "Identity", "ScientificStructure"],
        "Recognition" => ["PhysicalCharacteristics", "Identity", "Recognition", "RecognitionGeometry", "ScientificStructure", "Observation"],
        "Clarification" => ["AstrologyClarification", "ScientificStructure", "PhysicalCharacteristics", "Identity"],
        "Science" => ["ScientificStructure", "PhysicalCharacteristics", "Formation", "Evolution", "StarFormation", "Distance"],
        "ModernAstronomy" => ["ScientificSignificance", "ScientificStructure", "History", "Astrophotography", "ImagingAppearance"],
        "Inspiration" => ["Identity", "Recognition", "Observation", "InterestingFacts", "ScientificStructure"],
        "Culture" => ["CultureAndMythology", "RegionalTraditions", "History", "Identity"],
        _ => ["Identity", "Recognition", "ScientificStructure", "Observation", "History", "CultureAndMythology"]
    };
    private static IReadOnlyList<string> PrimaryPreferred(string s) => s switch
    {
        "Wonder" => ["scientific.astronomicalImportance", "identity.", "objects.objectName", "scientific.summary", "observation.orionBeltIdentification"],
        "Discovery" => ["scientific.orionBeltStars", "scientific.majorStars", "objects.objectName", "identity."],
        "Observation" => ["observation.binocularGuidance", "observation.nakedEyeRecognition", "observation.orionBeltIdentification"],
        "History" => ["history.historicalCataloguing", "history.ancientRecognition", "cultureAndMythology.", "identity."],
        "Science" => ["scientific.summary", "scientific.majorStars", "scientific.orionBeltStars", "objects.objectName"],
        "ModernAstronomy" => ["scientific.summary", "history.modernInterpretation", "scientific.majorStars", "objects.objectName"],
        "Inspiration" => ["identity.", "objects.objectName", "observation.nakedEyeRecognition", "observation.orionBeltIdentification", "scientific.summary"],
        "Culture" => ["cultureAndMythology.", "history.ancientRecognition", "identity.", "objects.objectName"],
        _ => ["scientific.summary", "objects.objectName", "identity."]
    };
    private static IReadOnlyList<string> PrimaryFallback(string s) => s switch
    {
        "Wonder" => ["scientific.majorStars", "scientific.orionBeltStars", "observation.nakedEyeRecognition"],
        "Science" => ["observation.orionBeltIdentification", "observation.nakedEyeRecognition"],
        _ => []
    };
    private static IReadOnlyList<string> ScientificPreferred(string s) => s switch
    {
        "Recognition" => ["scientific.approximatePosition", "scientific.summary", "scientific.orionBeltStars", "observation.orionBeltIdentification", "observation.nakedEyeRecognition", "scientific.majorStars", "objects.objectName"],
        "Clarification" => ["astrologyRelationships.westernZodiacNotes", "scientific.summary", "scientific.majorStars", "objects.objectName"],
        _ => ["scientific.summary", "scientific.majorStars", "scientific.orionBeltStars", "objects.objectName"]
    };
    private static IReadOnlyList<string> ScientificFallback(string s) => s switch
    {
        "Recognition" => [],
        _ => ["observation.orionBeltIdentification", "observation.nakedEyeRecognition"]
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
