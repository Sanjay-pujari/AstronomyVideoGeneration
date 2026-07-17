namespace Astronomy.MediaFactory.Core.ExecutionContracts;

public enum FamilyRequirementLevel
{
    Required,
    Optional,
    Conditional
}

public enum FamilyArtifactClassification
{
    Required,
    Optional,
    Diagnostic
}

public enum FamilyValidationBoundary
{
    PreExecution,
    SemanticResolution,
    Projection,
    ArtifactGeneration,
    PostExecution
}
