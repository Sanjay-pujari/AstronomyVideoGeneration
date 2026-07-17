namespace Astronomy.MediaFactory.Core.ExecutionContracts;

/// <summary>Declares whether a family requirement must be satisfied.</summary>
public enum FamilyRequirementLevel { Required, Optional, Conditional }

/// <summary>Classifies an artifact requirement by execution importance.</summary>
public enum FamilyArtifactClassification { Required, Optional, Diagnostic }

/// <summary>Declares the execution boundary where a validation rule applies.</summary>
public enum FamilyValidationBoundary { PreExecution, SemanticResolution, Projection, ArtifactGeneration, PostExecution }

/// <summary>Declares the lifecycle status of a contract requirement.</summary>
public enum FamilyRequirementStatus { Active, Deprecated, Experimental }

/// <summary>Reports the result of resolving a family execution contract.</summary>
public enum FamilyContractResolutionStatus { Resolved, NotFound, InvalidRequest }

/// <summary>Declares the neutral scope described by a family requirement.</summary>
public enum FamilyRequirementScope { Execution, Format, Beat, Artifact }

/// <summary>Declares the expected number of artifacts matching a requirement.</summary>
public enum FamilyArtifactCardinality { ExactlyOne, OneOrMore, ZeroOrOne, ZeroOrMore }

/// <summary>Declares the severity of a validation requirement.</summary>
public enum FamilyValidationSeverity { Information, Warning, Blocking }

/// <summary>Declares how a family contract identity was matched.</summary>
public enum FamilyContractMatchKind { CanonicalFamilyId, Alias, None }
