using System.Collections.Immutable;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilySemanticRequirement
{
    public FamilySemanticRequirement(string RequirementId, string CapabilityId, string Description = "", FamilyRequirementLevel Level = FamilyRequirementLevel.Required, FamilyRequirementScope Scope = FamilyRequirementScope.Execution, FamilyRequirementStatus Status = FamilyRequirementStatus.Active, string MinimumEvidenceStrength = "", ImmutableArray<string> AllowedSourceIds = default, string? ConditionKey = null, ImmutableDictionary<string, string>? Metadata = null)
    { this.RequirementId = ExecutionContractGuard.RequireNonEmpty(RequirementId, nameof(RequirementId)); this.CapabilityId = ExecutionContractGuard.RequireNonEmpty(CapabilityId, nameof(CapabilityId)); this.Description = ExecutionContractGuard.NormalizeText(Description); this.Level = Level; this.Scope = Scope; this.Status = Status; this.MinimumEvidenceStrength = ExecutionContractGuard.NormalizeText(MinimumEvidenceStrength); this.AllowedSourceIds = ExecutionContractGuard.NormalizeArray(AllowedSourceIds); this.ConditionKey = ExecutionContractGuard.NormalizeOptional(ConditionKey); this.Metadata = ExecutionContractGuard.NormalizeMetadata(Metadata); }
    public string RequirementId { get; init; } public string CapabilityId { get; init; } public string Description { get; init; } public FamilyRequirementLevel Level { get; init; } public FamilyRequirementScope Scope { get; init; } public FamilyRequirementStatus Status { get; init; } public string MinimumEvidenceStrength { get; init; } public ImmutableArray<string> AllowedSourceIds { get; init; } public string? ConditionKey { get; init; } public ImmutableDictionary<string, string> Metadata { get; init; }
}
