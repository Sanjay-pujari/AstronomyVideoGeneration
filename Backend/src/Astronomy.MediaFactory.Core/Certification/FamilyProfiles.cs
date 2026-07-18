namespace Astronomy.MediaFactory.Core.Certification;

public sealed class MeteorShowerCertificationProfile : IFamilyCertificationProfile
{
    public string FamilyId => "MeteorShower";
    public IReadOnlySet<string> SupportedEventTypeAliases { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Meteor Shower" };
    public string? CanonicalSemanticValueId => "MeteorActivity";
    public IReadOnlyList<RequiredSemanticFactDefinition> GetRequiredFacts(FamilyCertificationContext context) =>
    [
        Req("EventIdentity", "Meteor shower identity/name."), Req("EventWindow", "Event date or activity window."), Req("ObservationDirection", "Radiant or valid observation direction."), Req("MeteorActivity", "Canonical meteor activity, radiant, and peak window."), Req("DomainScientificKnowledge", "Scientific importance or explanatory significance.")
    ];
    public IReadOnlyList<ForbiddenConceptDefinition> GetForbiddenConcepts(FamilyCertificationContext context) =>
    [ new() { ConceptId = "planet-conjunction-leakage", Terms = ["Venus", "Jupiter", "conjunction", "planet conjunction", "planet pairing", "western sky after sunset", "look west", "युति", "ग्रह-युति", "बृहस्पति", "शुक्र"], Blocking = true } ];
    public IReadOnlyList<StoryStructureRequirement> GetStoryRequirements(FamilyCertificationContext context) => [Role("Hook"), Role("Orientation"), Role("Timing"), Role("Observation"), Role("Science"), Role("Closing")];
    public IReadOnlyList<BeatCoverageRequirement> GetBeatCoverageRequirements(FamilyCertificationContext context) =>
    [ Beat("EventIdentity", ["Hook", "Identity"]), Beat("EventWindow", ["Timing", "Observation"]), Beat("ObservationDirection", ["Orientation", "Observation"]), Beat("MeteorActivity", ["Orientation", "Timing", "Observation", "Science"]), Beat("DomainScientificKnowledge", ["Science"] ) ];
    public IReadOnlyList<PhaseArtifactDefinition> GetAdditionalArtifacts(FamilyCertificationContext context) => [Opt("meteor-shower-shadow-validation", "narration-v5/meteor-shower-shadow-validation.json")];
    private static RequiredSemanticFactDefinition Req(string id, string desc) => new() { FactId = id, Required = true, MinimumConfidence = 80, Description = desc };
    private static StoryStructureRequirement Role(string role) => new() { RequirementId = $"meteor.story.{role}", StoryRole = role, Required = true };
    private static BeatCoverageRequirement Beat(string fact, IReadOnlyList<string> roles) => new() { FactId = fact, AllowedBeatRoles = roles, Required = true };
    private static PhaseArtifactDefinition Opt(string id, string path) => new() { ArtifactId = id, PhaseNumber = 7, RelativePath = path, Required = false, ValidateJson = true, RequireNonEmpty = true };
}

public sealed class PlanetConjunctionCertificationProfile : IFamilyCertificationProfile
{
    public string FamilyId => "PlanetConjunction";
    public IReadOnlySet<string> SupportedEventTypeAliases { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PlanetPairing", "PlanetaryConjunction", "PLANET_CONJUNCTION", "PLANET_PAIRING" };
    public string? CanonicalSemanticValueId => "PlanetPairing";
    public IReadOnlyList<RequiredSemanticFactDefinition> GetRequiredFacts(FamilyCertificationContext context) =>
    [ Req("EventIdentity", "Conjunction event identity."), Req("AstronomicalObjects", "Primary participating objects."), Req("EventWindow", "Event date or observation window."), Req("DomainScientificKnowledge", "Line-of-sight/scientific explanation.") ];
    public IReadOnlyList<ForbiddenConceptDefinition> GetForbiddenConcepts(FamilyCertificationContext context) =>
    [ new() { ConceptId = "meteor-shower-leakage", Terms = ["meteor shower", "radiant", "meteors per hour", "ZHR", "shooting stars", "meteor parent body", "meteor peak activity", "उल्का वर्षा", "टूटते तारे"], Blocking = true } ];
    public IReadOnlyList<StoryStructureRequirement> GetStoryRequirements(FamilyCertificationContext context) => [Role("Hook"), Role("Orientation"), Role("Timing"), Role("Observation"), Role("Science"), Role("Closing")];
    public IReadOnlyList<BeatCoverageRequirement> GetBeatCoverageRequirements(FamilyCertificationContext context) =>
    [ Beat("EventIdentity", ["Hook"]), Beat("AstronomicalObjects", ["Hook", "Orientation", "Observation"]), Beat("EventWindow", ["Timing", "Observation"]), Beat("DomainScientificKnowledge", ["Science"] ) ];
    public IReadOnlyList<PhaseArtifactDefinition> GetAdditionalArtifacts(FamilyCertificationContext context) => [];
    private static RequiredSemanticFactDefinition Req(string id, string desc) => new() { FactId = id, Required = true, MinimumConfidence = 80, Description = desc };
    private static StoryStructureRequirement Role(string role) => new() { RequirementId = $"conjunction.story.{role}", StoryRole = role, Required = true };
    private static BeatCoverageRequirement Beat(string fact, IReadOnlyList<string> roles) => new() { FactId = fact, AllowedBeatRoles = roles, Required = true };
}
