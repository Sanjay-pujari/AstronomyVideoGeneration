using System.Collections.Immutable;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilyExecutionContract
{
    public FamilyExecutionContract(string FamilyId, string ContractVersion, string DisplayName, string Description = "", ImmutableArray<string> Aliases = default, ImmutableArray<FamilyInputRequirement> InputRequirements = default, ImmutableArray<FamilySemanticRequirement> SemanticRequirements = default, ImmutableArray<FamilyProjectionRequirement> ProjectionRequirements = default, ImmutableArray<FamilyPhaseArtifactRequirement> ArtifactRequirements = default, ImmutableArray<FamilyValidationRequirement> ValidationRequirements = default, ImmutableDictionary<string, string>? Metadata = null, FamilyRequirementStatus Status = FamilyRequirementStatus.Active)
    {
        this.FamilyId = ExecutionContractGuard.RequireNonEmpty(FamilyId, nameof(FamilyId));
        this.ContractVersion = ExecutionContractGuard.RequireNonEmpty(ContractVersion, nameof(ContractVersion));
        this.DisplayName = ExecutionContractGuard.RequireNonEmpty(DisplayName, nameof(DisplayName));
        this.Description = ExecutionContractGuard.NormalizeText(Description);
        this.Aliases = ExecutionContractGuard.NormalizeAliases(this.FamilyId, Aliases);
        this.InputRequirements = ExecutionContractGuard.NormalizeArray(InputRequirements);
        this.SemanticRequirements = ExecutionContractGuard.NormalizeArray(SemanticRequirements);
        this.ProjectionRequirements = ExecutionContractGuard.NormalizeArray(ProjectionRequirements);
        this.ArtifactRequirements = ExecutionContractGuard.NormalizeArray(ArtifactRequirements);
        this.ValidationRequirements = ExecutionContractGuard.NormalizeArray(ValidationRequirements);
        this.Metadata = ExecutionContractGuard.NormalizeMetadata(Metadata);
        this.Status = Status;
        RejectDuplicateRequirementIds();
    }
    public string FamilyId { get; init; }
    public string ContractVersion { get; init; }
    public string DisplayName { get; init; }
    public string Description { get; init; }
    public ImmutableArray<string> Aliases { get; init; }
    public ImmutableArray<FamilyInputRequirement> InputRequirements { get; init; }
    public ImmutableArray<FamilySemanticRequirement> SemanticRequirements { get; init; }
    public ImmutableArray<FamilyProjectionRequirement> ProjectionRequirements { get; init; }
    public ImmutableArray<FamilyPhaseArtifactRequirement> ArtifactRequirements { get; init; }
    public ImmutableArray<FamilyValidationRequirement> ValidationRequirements { get; init; }
    public ImmutableDictionary<string, string> Metadata { get; init; }
    public FamilyRequirementStatus Status { get; init; }

    private void RejectDuplicateRequirementIds()
    {
        ExecutionContractGuard.RejectDuplicateRequirementIds(nameof(InputRequirements), InputRequirements.Select(r => r.RequirementId));
        ExecutionContractGuard.RejectDuplicateRequirementIds(nameof(SemanticRequirements), SemanticRequirements.Select(r => r.RequirementId));
        ExecutionContractGuard.RejectDuplicateRequirementIds(nameof(ProjectionRequirements), ProjectionRequirements.Select(r => r.RequirementId));
        ExecutionContractGuard.RejectDuplicateRequirementIds(nameof(ArtifactRequirements), ArtifactRequirements.Select(r => r.RequirementId));
        ExecutionContractGuard.RejectDuplicateRequirementIds(nameof(ValidationRequirements), ValidationRequirements.Select(r => r.RequirementId));
        ExecutionContractGuard.RejectDuplicateRequirementIds("all requirement categories", InputRequirements.Select(r => r.RequirementId).Concat(SemanticRequirements.Select(r => r.RequirementId)).Concat(ProjectionRequirements.Select(r => r.RequirementId)).Concat(ArtifactRequirements.Select(r => r.RequirementId)).Concat(ValidationRequirements.Select(r => r.RequirementId)));
    }
}
