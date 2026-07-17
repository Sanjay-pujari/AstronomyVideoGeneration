using System.Collections.Immutable;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilyInputRequirement
{
    public FamilyInputRequirement(string RequirementId, string InputKey, string Description = "", FamilyRequirementLevel Level = FamilyRequirementLevel.Required, FamilyRequirementScope Scope = FamilyRequirementScope.Execution, FamilyRequirementStatus Status = FamilyRequirementStatus.Active, string? ConditionKey = null, ImmutableArray<string> AcceptedSourceIds = default, ImmutableDictionary<string, string>? Metadata = null)
    { this.RequirementId = ExecutionContractGuard.RequireNonEmpty(RequirementId, nameof(RequirementId)); this.InputKey = ExecutionContractGuard.RequireNonEmpty(InputKey, nameof(InputKey)); this.Description = ExecutionContractGuard.NormalizeText(Description); this.Level = Level; this.Scope = Scope; this.Status = Status; this.ConditionKey = ExecutionContractGuard.NormalizeOptional(ConditionKey); this.AcceptedSourceIds = ExecutionContractGuard.NormalizeArray(AcceptedSourceIds); this.Metadata = ExecutionContractGuard.NormalizeMetadata(Metadata); }
    public string RequirementId { get; init; } public string InputKey { get; init; } public string Description { get; init; } public FamilyRequirementLevel Level { get; init; } public FamilyRequirementScope Scope { get; init; } public FamilyRequirementStatus Status { get; init; } public string? ConditionKey { get; init; } public ImmutableArray<string> AcceptedSourceIds { get; init; } public ImmutableDictionary<string, string> Metadata { get; init; }
}
