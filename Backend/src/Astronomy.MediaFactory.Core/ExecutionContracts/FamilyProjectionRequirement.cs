using System.Collections.Immutable;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilyProjectionRequirement
{
    public FamilyProjectionRequirement(string RequirementId, string SourceCapabilityId, string TargetFactType, string Description = "", FamilyRequirementLevel Level = FamilyRequirementLevel.Required, FamilyRequirementScope Scope = FamilyRequirementScope.Execution, FamilyRequirementStatus Status = FamilyRequirementStatus.Active, string? ProjectionRuleId = null, string? ConditionKey = null, ImmutableDictionary<string, string>? Metadata = null)
    { this.RequirementId = ExecutionContractGuard.RequireNonEmpty(RequirementId, nameof(RequirementId)); this.SourceCapabilityId = ExecutionContractGuard.RequireNonEmpty(SourceCapabilityId, nameof(SourceCapabilityId)); this.TargetFactType = ExecutionContractGuard.RequireNonEmpty(TargetFactType, nameof(TargetFactType)); this.Description = ExecutionContractGuard.NormalizeText(Description); this.Level = Level; this.Scope = Scope; this.Status = Status; this.ProjectionRuleId = ExecutionContractGuard.NormalizeOptional(ProjectionRuleId); this.ConditionKey = ExecutionContractGuard.NormalizeOptional(ConditionKey); this.Metadata = ExecutionContractGuard.NormalizeMetadata(Metadata); }
    public string RequirementId { get; init; } public string SourceCapabilityId { get; init; } public string TargetFactType { get; init; } public string Description { get; init; } public FamilyRequirementLevel Level { get; init; } public FamilyRequirementScope Scope { get; init; } public FamilyRequirementStatus Status { get; init; } public string? ProjectionRuleId { get; init; } public string? ConditionKey { get; init; } public ImmutableDictionary<string, string> Metadata { get; init; }
}
