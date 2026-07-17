namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public sealed record FamilyPhaseArtifactRequirement(
    string ArtifactName,
    FamilyArtifactClassification Classification,
    string? PhaseId = null,
    string? Description = null);
