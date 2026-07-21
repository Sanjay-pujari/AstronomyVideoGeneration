namespace Astronomy.MediaFactory.Core.Certification;

public sealed class MeteorShowerCertificationProfile(ISemanticFactCatalog? catalog = null) : IFamilyCertificationProfile
{
    private readonly ISemanticFactCatalog catalog = catalog ?? new CertificationSemanticFactCatalog();
    private CertificationFamilySemanticProfileMetadata Metadata => catalog.ResolveFamily(FamilyId);
    public string FamilyId => "MeteorShower";
    public IReadOnlySet<string> SupportedEventTypeAliases => Metadata.Aliases;
    public string? CanonicalSemanticValueId => Metadata.CanonicalSemanticValueId;
    public IReadOnlyList<RequiredSemanticFactDefinition> GetRequiredFacts(FamilyCertificationContext context) => Metadata.RequiredFactIds.Select(id => new RequiredSemanticFactDefinition { FactId = id, Required = catalog.ResolveRequiredStatus(FamilyId, id), MinimumConfidence = catalog.ResolveConfidence(id), Description = catalog.ResolveDisplayName(id) }).ToArray();
    public IReadOnlyList<ForbiddenConceptDefinition> GetForbiddenConcepts(FamilyCertificationContext context) => Metadata.ForbiddenConcepts;
    public IReadOnlyList<StoryStructureRequirement> GetStoryRequirements(FamilyCertificationContext context) => Metadata.StoryRoles.Select(r => new StoryStructureRequirement { RequirementId = $"{FamilyId}.story.{r}", StoryRole = r, Required = true }).ToArray();
    public IReadOnlyList<BeatCoverageRequirement> GetBeatCoverageRequirements(FamilyCertificationContext context) => Metadata.BeatCoverage.Select(kv => new BeatCoverageRequirement { FactId = kv.Key, AllowedBeatRoles = kv.Value, Required = true }).ToArray();
    public IReadOnlyList<PhaseArtifactDefinition> GetAdditionalArtifacts(FamilyCertificationContext context) => Metadata.AdditionalArtifacts;
}

public sealed class PlanetConjunctionCertificationProfile(ISemanticFactCatalog? catalog = null) : IFamilyCertificationProfile
{
    private readonly ISemanticFactCatalog catalog = catalog ?? new CertificationSemanticFactCatalog();
    private CertificationFamilySemanticProfileMetadata Metadata => catalog.ResolveFamily(FamilyId);
    public string FamilyId => "PlanetConjunction";
    public IReadOnlySet<string> SupportedEventTypeAliases => Metadata.Aliases;
    public string? CanonicalSemanticValueId => Metadata.CanonicalSemanticValueId;
    public IReadOnlyList<RequiredSemanticFactDefinition> GetRequiredFacts(FamilyCertificationContext context) => Metadata.RequiredFactIds.Select(id => new RequiredSemanticFactDefinition { FactId = id, Required = catalog.ResolveRequiredStatus(FamilyId, id), MinimumConfidence = catalog.ResolveConfidence(id), Description = catalog.ResolveDisplayName(id) }).ToArray();
    public IReadOnlyList<ForbiddenConceptDefinition> GetForbiddenConcepts(FamilyCertificationContext context) => Metadata.ForbiddenConcepts;
    public IReadOnlyList<StoryStructureRequirement> GetStoryRequirements(FamilyCertificationContext context) => Metadata.StoryRoles.Select(r => new StoryStructureRequirement { RequirementId = $"{FamilyId}.story.{r}", StoryRole = r, Required = true }).ToArray();
    public IReadOnlyList<BeatCoverageRequirement> GetBeatCoverageRequirements(FamilyCertificationContext context) => Metadata.BeatCoverage.Select(kv => new BeatCoverageRequirement { FactId = kv.Key, AllowedBeatRoles = kv.Value, Required = true }).ToArray();
    public IReadOnlyList<PhaseArtifactDefinition> GetAdditionalArtifacts(FamilyCertificationContext context) => Metadata.AdditionalArtifacts;
}


public sealed class ConstellationCertificationProfile(ISemanticFactCatalog? catalog = null) : IFamilyCertificationProfile
{
    private readonly ISemanticFactCatalog catalog = catalog ?? new CertificationSemanticFactCatalog();
    private CertificationFamilySemanticProfileMetadata Metadata => catalog.ResolveFamily(FamilyId);
    public string FamilyId => "CONSTELLATION";
    public IReadOnlySet<string> SupportedEventTypeAliases => Metadata.Aliases;
    public string? CanonicalSemanticValueId => Metadata.CanonicalSemanticValueId;
    public IReadOnlyList<RequiredSemanticFactDefinition> GetRequiredFacts(FamilyCertificationContext context) => Metadata.RequiredFactIds.Select(id => new RequiredSemanticFactDefinition { FactId = id, Required = catalog.ResolveRequiredStatus(FamilyId, id), MinimumConfidence = catalog.ResolveConfidence(id), Description = catalog.ResolveDisplayName(id) }).ToArray();
    public IReadOnlyList<ForbiddenConceptDefinition> GetForbiddenConcepts(FamilyCertificationContext context) => Metadata.ForbiddenConcepts;
    public IReadOnlyList<StoryStructureRequirement> GetStoryRequirements(FamilyCertificationContext context) => Metadata.StoryRoles.Select(r => new StoryStructureRequirement { RequirementId = $"{FamilyId}.story.{r}", StoryRole = r, Required = true }).ToArray();
    public IReadOnlyList<BeatCoverageRequirement> GetBeatCoverageRequirements(FamilyCertificationContext context) => Metadata.BeatCoverage.Select(kv => new BeatCoverageRequirement { FactId = kv.Key, AllowedBeatRoles = kv.Value, Required = true }).ToArray();
    public IReadOnlyList<PhaseArtifactDefinition> GetAdditionalArtifacts(FamilyCertificationContext context) => Metadata.AdditionalArtifacts;
}
