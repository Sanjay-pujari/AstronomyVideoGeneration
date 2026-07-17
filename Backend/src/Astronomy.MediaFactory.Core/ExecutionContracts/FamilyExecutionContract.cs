namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilyExecutionContract(
    string FamilyId,
    IReadOnlyList<FamilyInputRequirement> InputRequirements,
    IReadOnlyList<FamilySemanticRequirement> SemanticRequirements,
    IReadOnlyList<FamilyProjectionRequirement> ProjectionRequirements,
    IReadOnlyList<FamilyPhaseArtifactRequirement> PhaseArtifactRequirements,
    IReadOnlyList<FamilyValidationRequirement> ValidationRequirements);
