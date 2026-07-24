namespace Astronomy.MediaFactory.Core.Certification;

public sealed record CertificationSemanticFactDefinition(string FactId, string DisplayName, string MetadataType, double MinimumConfidence, bool RequiredByDefault);
public sealed record CertificationFamilySemanticProfileMetadata(string FamilyId, IReadOnlySet<string> Aliases, string? CanonicalSemanticValueId, IReadOnlyList<string> RequiredFactIds, IReadOnlyList<string> OptionalFactIds, IReadOnlyList<ForbiddenConceptDefinition> ForbiddenConcepts, IReadOnlyList<string> StoryRoles, IReadOnlyDictionary<string, IReadOnlyList<string>> BeatCoverage, IReadOnlyList<PhaseArtifactDefinition> AdditionalArtifacts);

public interface ISemanticFactCatalog
{
    CertificationSemanticFactDefinition ResolveFactId(string factId);
    string? ResolveCanonicalValue(string familyOrAlias);
    double? ResolveConfidence(string factId);
    IReadOnlyDictionary<string, string> ResolveMetadata(string factId);
    string ResolveDisplayName(string factId);
    bool ResolveRequiredStatus(string familyOrAlias, string factId);
    CertificationFamilySemanticProfileMetadata ResolveFamily(string familyOrAlias);
    IReadOnlyList<CertificationSemanticFactDefinition> Facts { get; }
    IReadOnlyList<CertificationFamilySemanticProfileMetadata> Families { get; }
}

public sealed class CertificationSemanticFactCatalog : ISemanticFactCatalog
{
    private readonly IReadOnlyDictionary<string, CertificationSemanticFactDefinition> facts;
    private readonly IReadOnlyDictionary<string, CertificationFamilySemanticProfileMetadata> families;

    public static IReadOnlyList<CertificationFamilySemanticProfileMetadata> BuiltInFamilyProfiles { get; } = CreateBuiltInFamilyProfiles();

    public CertificationSemanticFactCatalog()
    {
        facts = CreateFacts().ToDictionary(f => f.FactId, StringComparer.OrdinalIgnoreCase);
        families = BuildFamilyLookup(BuiltInFamilyProfiles);
    }


    public CertificationSemanticFactCatalog(IEnumerable<CertificationFamilySemanticProfileMetadata> familyProfiles)
        : this()
    {
        families = BuildFamilyLookup(familyProfiles);
    }


    private static IReadOnlyList<CertificationSemanticFactDefinition> CreateFacts() =>
    [
        Fact("EventIdentity", "Event identity", "CanonicalEventIdentity", 80),
        Fact("EventWindow", "Event window", "ObservationWindow", 80),
        Fact("ObservationDirection", "Observation direction", "ObservationDirection", 80),
        Fact("MeteorActivity", "Meteor shower activity", "MeteorActivity", 80),
        Fact("DomainScientificKnowledge", "Domain scientific knowledge", "DomainScientificKnowledge", 80),
        Fact("AstronomicalObjects", "Astronomical objects", "AstronomicalObjectList", 80),
        Fact("AngularSeparation", "Angular separation", "AngularSeparation", 80, false),
        Fact("SecondaryAstronomicalObjects", "Secondary astronomical objects", "AstronomicalObjectList", 80, false),
        Fact("ObjectKnowledge", "Object knowledge", "ConstellationKnowledge", 80),
        Fact("CulturalNameContext", "Cultural name context", "CulturalAstronomy", 80, false)
    ];

    private static IReadOnlyList<CertificationFamilySemanticProfileMetadata> CreateBuiltInFamilyProfiles()
    {
        IReadOnlyList<string> sharedRoles = ["Hook", "Orientation", "Timing", "Observation", "Science", "Closing"];
        return
        [
            Family("MeteorShower", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Meteor Shower" }, "MeteorActivity", ["EventIdentity", "EventWindow", "ObservationDirection", "MeteorActivity", "DomainScientificKnowledge"], [],
                [new ForbiddenConceptDefinition { ConceptId = "planet-conjunction-leakage", Terms = ["Venus", "Jupiter", "conjunction", "planet conjunction", "planet pairing", "western sky after sunset", "look west", "युति", "ग्रह-युति", "बृहस्पति", "शुक्र"], Blocking = true }], sharedRoles,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["EventIdentity"] = ["Hook", "Identity"], ["EventWindow"] = ["Timing", "Observation"], ["ObservationDirection"] = ["Orientation", "Observation"], ["MeteorActivity"] = ["Orientation", "Timing", "Observation", "Science"], ["DomainScientificKnowledge"] = ["Science"] },
                [new PhaseArtifactDefinition { ArtifactId = "meteor-shower-shadow-validation", PhaseNumber = 7, RelativePath = "narration-v5/meteor-shower-shadow-validation.json", Required = false, ValidateJson = true, RequireNonEmpty = true }]),
            Family("PlanetConjunction", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PlanetPairing", "PlanetaryConjunction", "PLANET_CONJUNCTION", "PLANET_PAIRING" }, "PlanetPairing", ["EventIdentity", "AstronomicalObjects", "EventWindow", "DomainScientificKnowledge"], [],
                [new ForbiddenConceptDefinition { ConceptId = "meteor-shower-leakage", Terms = ["meteor shower", "radiant", "meteors per hour", "ZHR", "shooting stars", "meteor parent body", "meteor peak activity", "उल्का वर्षा", "टूटते तारे"], Blocking = true }], sharedRoles,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["EventIdentity"] = ["Hook"], ["AstronomicalObjects"] = ["Hook", "Orientation", "Observation"], ["EventWindow"] = ["Timing", "Observation"], ["DomainScientificKnowledge"] = ["Science"] }, []),
            Family("CONSTELLATION", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Constellation" }, "Constellation", ["ObjectKnowledge"], ["EventIdentity", "ObservationDirection", "CulturalNameContext", "EditorialContext"],
                [new ForbiddenConceptDefinition { ConceptId = "transient-event-leakage", Terms = ["peak time", "maximum eclipse", "meteor radiant", "ZHR", "angular separation", "eclipse glasses"], Blocking = true }], ["Hook", "Identification", "Orientation", "Science", "Significance", "Closing"],
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["ObjectKnowledge"] = ["Identification", "Orientation", "Science", "Significance"] },
                [new PhaseArtifactDefinition { ArtifactId = "constellation-knowledge", PhaseNumber = 2, RelativePath = "plan-input/constellation-knowledge.json", Required = true, ValidateJson = true, RequireNonEmpty = true }])
        ];
    }

    private sealed record FamilyKeyRegistration(string OriginalKey, string NormalizedKey, Type ProviderType, string CanonicalFamilyId, bool IsAlias, CertificationFamilySemanticProfileMetadata Family);

    private static IReadOnlyDictionary<string, CertificationFamilySemanticProfileMetadata> BuildFamilyLookup(IEnumerable<CertificationFamilySemanticProfileMetadata> familyProfiles)
    {
        ArgumentNullException.ThrowIfNull(familyProfiles);
        var registrations = familyProfiles.SelectMany(CreateRegistrations).OrderBy(r => r.NormalizedKey, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.CanonicalFamilyId, StringComparer.OrdinalIgnoreCase).ToArray();
        var builder = new Dictionary<string, CertificationFamilySemanticProfileMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in registrations.GroupBy(r => r.NormalizedKey, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            foreach (var candidate in group.Skip(1))
            {
                if (!IsSameLogicalFamily(first, candidate))
                {
                    throw new InvalidOperationException($"Duplicate certification semantic-fact key '{candidate.OriginalKey}' is claimed by '{first.ProviderType.Name}' for family '{first.CanonicalFamilyId}' and '{candidate.ProviderType.Name}' for family '{candidate.CanonicalFamilyId}'.");
                }
            }
            builder[group.Key] = first.Family;
        }
        return builder;
    }

    private static IEnumerable<FamilyKeyRegistration> CreateRegistrations(CertificationFamilySemanticProfileMetadata family)
    {
        ArgumentNullException.ThrowIfNull(family);
        yield return Registration(family.FamilyId, family, false);
        foreach (var alias in family.Aliases.Order(StringComparer.OrdinalIgnoreCase)) yield return Registration(alias, family, true);
    }

    private static FamilyKeyRegistration Registration(string key, CertificationFamilySemanticProfileMetadata family, bool isAlias)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException($"Certification semantic-fact family '{family.FamilyId}' contains an empty family id or alias.", nameof(family));
        var normalized = key.Trim();
        return new FamilyKeyRegistration(key, normalized, family.GetType(), CanonicalFamilyId(family), isAlias, family);
    }

    private static bool IsSameLogicalFamily(FamilyKeyRegistration first, FamilyKeyRegistration second)
        => first.ProviderType == second.ProviderType && string.Equals(first.CanonicalFamilyId, second.CanonicalFamilyId, StringComparison.OrdinalIgnoreCase);

    private static string CanonicalFamilyId(CertificationFamilySemanticProfileMetadata family)
        => string.IsNullOrWhiteSpace(family.CanonicalSemanticValueId) ? family.FamilyId.Trim() : family.CanonicalSemanticValueId.Trim();

    public IReadOnlyList<CertificationSemanticFactDefinition> Facts => facts.Values.DistinctBy(f => f.FactId, StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<CertificationFamilySemanticProfileMetadata> Families => families.Values.DistinctBy(f => f.CanonicalSemanticValueId ?? f.FamilyId, StringComparer.OrdinalIgnoreCase).ToArray();
    public CertificationSemanticFactDefinition ResolveFactId(string factId) => facts.TryGetValue(factId.Trim(), out var f) ? f : throw new KeyNotFoundException($"Unknown certification semantic fact '{factId}'.");
    public string? ResolveCanonicalValue(string familyOrAlias) => ResolveFamily(familyOrAlias).CanonicalSemanticValueId;
    public double? ResolveConfidence(string factId) => ResolveFactId(factId).MinimumConfidence;
    public IReadOnlyDictionary<string, string> ResolveMetadata(string factId) { var f = ResolveFactId(factId); return new Dictionary<string, string> { ["metadataType"] = f.MetadataType, ["displayName"] = f.DisplayName }; }
    public string ResolveDisplayName(string factId) => ResolveFactId(factId).DisplayName;
    public bool ResolveRequiredStatus(string familyOrAlias, string factId) => ResolveFamily(familyOrAlias).RequiredFactIds.Contains(factId, StringComparer.OrdinalIgnoreCase);
    public CertificationFamilySemanticProfileMetadata ResolveFamily(string familyOrAlias) => families.TryGetValue(familyOrAlias.Trim(), out var f) ? f : throw new KeyNotFoundException($"Unknown certification family '{familyOrAlias}'.");
    private static CertificationSemanticFactDefinition Fact(string id, string name, string type, double confidence, bool required = true) => new(id, name, type, confidence, required);
    private static CertificationFamilySemanticProfileMetadata Family(string id, IReadOnlySet<string> aliases, string canonical, IReadOnlyList<string> required, IReadOnlyList<string> optional, IReadOnlyList<ForbiddenConceptDefinition> forbidden, IReadOnlyList<string> roles, IReadOnlyDictionary<string, IReadOnlyList<string>> beat, IReadOnlyList<PhaseArtifactDefinition> artifacts) => new(id, aliases, canonical, required, optional, forbidden, roles, beat, artifacts);
}
