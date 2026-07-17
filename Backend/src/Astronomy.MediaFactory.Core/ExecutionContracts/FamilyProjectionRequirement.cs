namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilyProjectionRequirement(
    string CanonicalCapabilityId,
    string LegacyFactType,
    FamilyRequirementLevel Level,
    string? Description = null,
    string? Condition = null);
