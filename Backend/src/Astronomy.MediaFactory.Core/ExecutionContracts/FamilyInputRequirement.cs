namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilyInputRequirement(
    string Name,
    FamilyRequirementLevel Level,
    string? Description = null,
    string? Condition = null);
