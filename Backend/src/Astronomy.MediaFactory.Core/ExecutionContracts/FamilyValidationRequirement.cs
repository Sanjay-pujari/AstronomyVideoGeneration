using System.Collections.Immutable;

namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilyValidationRequirement
{
    public FamilyValidationRequirement(string RequirementId, string RuleId, FamilyValidationBoundary Boundary, FamilyValidationSeverity Severity, string Description = "", FamilyRequirementScope Scope = FamilyRequirementScope.Execution, FamilyRequirementStatus Status = FamilyRequirementStatus.Active, string? ConditionKey = null, ImmutableDictionary<string, string>? Metadata = null)
    { this.RequirementId = ExecutionContractGuard.RequireNonEmpty(RequirementId, nameof(RequirementId)); this.RuleId = ExecutionContractGuard.RequireNonEmpty(RuleId, nameof(RuleId)); this.Boundary = Boundary; this.Severity = Severity; this.Description = ExecutionContractGuard.NormalizeText(Description); this.Scope = Scope; this.Status = Status; this.ConditionKey = ExecutionContractGuard.NormalizeOptional(ConditionKey); this.Metadata = ExecutionContractGuard.NormalizeMetadata(Metadata); }
    public string RequirementId { get; init; } public string RuleId { get; init; } public FamilyValidationBoundary Boundary { get; init; } public FamilyValidationSeverity Severity { get; init; } public string Description { get; init; } public FamilyRequirementScope Scope { get; init; } public FamilyRequirementStatus Status { get; init; } public string? ConditionKey { get; init; } public ImmutableDictionary<string, string> Metadata { get; init; }
}
