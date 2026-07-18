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

    public CertificationSemanticFactCatalog()
    {
        facts = new[]
        {
            Fact("EventIdentity", "Event identity", "CanonicalEventIdentity", 80),
            Fact("EventWindow", "Event window", "ObservationWindow", 80),
            Fact("ObservationDirection", "Observation direction", "ObservationDirection", 80),
            Fact("MeteorActivity", "Meteor shower activity", "MeteorActivity", 80),
            Fact("DomainScientificKnowledge", "Domain scientific knowledge", "DomainScientificKnowledge", 80),
            Fact("AstronomicalObjects", "Astronomical objects", "AstronomicalObjectList", 80),
            Fact("AngularSeparation", "Angular separation", "AngularSeparation", 80, false),
            Fact("SecondaryAstronomicalObjects", "Secondary astronomical objects", "AstronomicalObjectList", 80, false)
        }.ToDictionary(f => f.FactId, StringComparer.OrdinalIgnoreCase);

        var sharedRoles = new[] { "Hook", "Orientation", "Timing", "Observation", "Science", "Closing" };
        families = new[]
        {
            Family("MeteorShower", ["Meteor Shower"], "MeteorActivity", ["EventIdentity", "EventWindow", "ObservationDirection", "MeteorActivity", "DomainScientificKnowledge"], [],
                [new() { ConceptId = "planet-conjunction-leakage", Terms = ["Venus", "Jupiter", "conjunction", "planet conjunction", "planet pairing", "western sky after sunset", "look west", "युति", "ग्रह-युति", "बृहस्पति", "शुक्र"], Blocking = true }], sharedRoles,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["EventIdentity"] = ["Hook", "Identity"], ["EventWindow"] = ["Timing", "Observation"], ["ObservationDirection"] = ["Orientation", "Observation"], ["MeteorActivity"] = ["Orientation", "Timing", "Observation", "Science"], ["DomainScientificKnowledge"] = ["Science"] },
                [new() { ArtifactId = "meteor-shower-shadow-validation", PhaseNumber = 7, RelativePath = "narration-v5/meteor-shower-shadow-validation.json", Required = false, ValidateJson = true, RequireNonEmpty = true }]),
            Family("PlanetConjunction", ["PlanetPairing", "PlanetaryConjunction", "PLANET_CONJUNCTION", "PLANET_PAIRING"], "PlanetPairing", ["EventIdentity", "AstronomicalObjects", "EventWindow", "DomainScientificKnowledge"], [],
                [new() { ConceptId = "meteor-shower-leakage", Terms = ["meteor shower", "radiant", "meteors per hour", "ZHR", "shooting stars", "meteor parent body", "meteor peak activity", "उल्का वर्षा", "टूटते तारे"], Blocking = true }], sharedRoles,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["EventIdentity"] = ["Hook"], ["AstronomicalObjects"] = ["Hook", "Orientation", "Observation"], ["EventWindow"] = ["Timing", "Observation"], ["DomainScientificKnowledge"] = ["Science"] }, [])
        }.SelectMany(f => new[] { f }.Concat(f.Aliases.Select(a => f with { FamilyId = a }))).ToDictionary(f => f.FamilyId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CertificationSemanticFactDefinition> Facts => facts.Values.DistinctBy(f => f.FactId, StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<CertificationFamilySemanticProfileMetadata> Families => families.Values.DistinctBy(f => f.CanonicalSemanticValueId ?? f.FamilyId, StringComparer.OrdinalIgnoreCase).ToArray();
    public CertificationSemanticFactDefinition ResolveFactId(string factId) => facts.TryGetValue(factId, out var f) ? f : throw new KeyNotFoundException($"Unknown certification semantic fact '{factId}'.");
    public string? ResolveCanonicalValue(string familyOrAlias) => ResolveFamily(familyOrAlias).CanonicalSemanticValueId;
    public double? ResolveConfidence(string factId) => ResolveFactId(factId).MinimumConfidence;
    public IReadOnlyDictionary<string, string> ResolveMetadata(string factId) { var f = ResolveFactId(factId); return new Dictionary<string, string> { ["metadataType"] = f.MetadataType, ["displayName"] = f.DisplayName }; }
    public string ResolveDisplayName(string factId) => ResolveFactId(factId).DisplayName;
    public bool ResolveRequiredStatus(string familyOrAlias, string factId) => ResolveFamily(familyOrAlias).RequiredFactIds.Contains(factId, StringComparer.OrdinalIgnoreCase);
    public CertificationFamilySemanticProfileMetadata ResolveFamily(string familyOrAlias) => families.TryGetValue(familyOrAlias, out var f) ? f : throw new KeyNotFoundException($"Unknown certification family '{familyOrAlias}'.");
    private static CertificationSemanticFactDefinition Fact(string id, string name, string type, double confidence, bool required = true) => new(id, name, type, confidence, required);
    private static CertificationFamilySemanticProfileMetadata Family(string id, IReadOnlySet<string> aliases, string canonical, IReadOnlyList<string> required, IReadOnlyList<string> optional, IReadOnlyList<ForbiddenConceptDefinition> forbidden, IReadOnlyList<string> roles, IReadOnlyDictionary<string, IReadOnlyList<string>> beat, IReadOnlyList<PhaseArtifactDefinition> artifacts) => new(id, aliases, canonical, required, optional, forbidden, roles, beat, artifacts);
}
