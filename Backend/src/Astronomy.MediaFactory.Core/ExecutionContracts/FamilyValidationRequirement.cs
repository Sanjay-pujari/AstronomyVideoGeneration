namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilyValidationRequirement(
    string RuleId,
    FamilyValidationBoundary Boundary,
    FamilyRequirementLevel Level,
    string? Description = null,
    string? Condition = null);
