namespace Astronomy.MediaFactory.Core.Certification;

public enum CertificationStatus { NotEvaluated, Passed, PassedWithWarnings, Failed, NotApplicable }
public enum CertificationLevel { Structural, Semantic, Quality }
public enum CertificationIssueCategory { MissingArtifact, EmptyArtifact, InvalidArtifact, UnexpectedArtifact, IdentityMismatch, FamilyMismatch, LanguageMismatch, MissingCanonicalValue, MissingSemanticFact, ResolutionFailure, ProjectionFailure, RetentionFailure, BeatAssignmentFailure, NarrationEvidenceFailure, CrossFamilyLeakage, StoryStructureFailure, ContentQualityFailure, DataQualityFailure, ConfigurationFailure, ImplementationFailure, ArchitecturalFailure }

public sealed record CertificationIssue
{
    public required CertificationIssueCategory Category { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? ArtifactPath { get; init; }
    public string? SemanticFactId { get; init; }
    public string? Source { get; init; }
    public string? Recommendation { get; init; }
    public bool IsBlocking { get; init; }
}

public sealed record ArtifactCertificationResult
{
    public required string ArtifactId { get; init; }
    public required string ExpectedPath { get; init; }
    public bool Required { get; init; }
    public bool Exists { get; init; }
    public bool IsNonEmpty { get; init; }
    public bool IsValid { get; init; }
    public long? LengthBytes { get; init; }
    public string? ValidationMessage { get; init; }
}

public sealed record SemanticFactCertificationResult
{
    public required string FactId { get; init; }
    public bool Required { get; init; }
    public bool Resolved { get; init; }
    public bool Projected { get; init; }
    public bool Retained { get; init; }
    public bool BeatAssigned { get; init; }
    public bool NarrationEvidenceFound { get; init; }
    public string? SourceAdapterId { get; init; }
    public string? SourcePath { get; init; }
    public string? ResolutionMode { get; init; }
    public double? Confidence { get; init; }
    public IReadOnlyList<string> BeatIds { get; init; } = [];
    public IReadOnlyList<string> SceneIds { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

public sealed record PhaseCertificationResult
{
    public required int PhaseNumber { get; init; }
    public required string PhaseName { get; init; }
    public required CertificationStatus StructuralStatus { get; init; }
    public required CertificationStatus SemanticStatus { get; init; }
    public required CertificationStatus QualityStatus { get; init; }
    public IReadOnlyList<ArtifactCertificationResult> Artifacts { get; init; } = [];
    public IReadOnlyList<SemanticFactCertificationResult> SemanticFacts { get; init; } = [];
    public IReadOnlyList<CertificationIssue> Issues { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Recommendations { get; init; } = [];
    public DateTimeOffset GeneratedUtc { get; init; }
}

public sealed record FamilyCertificationSummary
{
    public required string PlanId { get; init; }
    public required string EventTitle { get; init; }
    public required string EventType { get; init; }
    public required string FamilyId { get; init; }
    public required string Language { get; init; }
    public required string RegionId { get; init; }
    public required CertificationStatus ExecutionStatus { get; init; }
    public required CertificationStatus SemanticStatus { get; init; }
    public required CertificationStatus QualityStatus { get; init; }
    public IReadOnlyList<PhaseCertificationResult> Phases { get; init; } = [];
    public IReadOnlyList<CertificationIssue> BlockingIssues { get; init; } = [];
    public bool ExecutionCertified { get; init; }
    public bool SemanticCertified { get; init; }
    public bool PublicationCertified { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; }
}
