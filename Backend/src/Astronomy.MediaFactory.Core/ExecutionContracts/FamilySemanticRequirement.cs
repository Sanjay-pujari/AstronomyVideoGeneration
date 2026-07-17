namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilySemanticRequirement(
    string CapabilityId,
    FamilyRequirementLevel Level,
    string? SourcePolicyId = null,
    string? Description = null,
    string? Condition = null);
