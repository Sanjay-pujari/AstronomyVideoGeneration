namespace Astronomy.MediaFactory.Core.Certification;

public sealed record FamilyCertificationContext
{
    public required string OutputRoot { get; init; }
    public required string ValidationRoot { get; init; }
    public required string PlanId { get; init; }
    public required string EventTitle { get; init; }
    public required string EventType { get; init; }
    public required string Language { get; init; }
    public required string RegionId { get; init; }
    public required int RequestedStartPhase { get; init; }
    public required int RequestedEndPhase { get; init; }
}

public sealed record PhaseArtifactDefinition
{
    public required string ArtifactId { get; init; }
    public required int PhaseNumber { get; init; }
    public required string RelativePath { get; init; }
    public bool Required { get; init; }
    public bool ValidateJson { get; init; }
    public bool RequireNonEmpty { get; init; }
    public string? Description { get; init; }
}

public interface IPhaseArtifactRegistry { IReadOnlyList<PhaseArtifactDefinition> GetDefinitions(int phaseNumber, FamilyCertificationContext context); }

public sealed record RequiredSemanticFactDefinition { public required string FactId { get; init; } public bool Required { get; init; } public double? MinimumConfidence { get; init; } public string? Description { get; init; } }
public sealed record ForbiddenConceptDefinition { public required string ConceptId { get; init; } public required IReadOnlyList<string> Terms { get; init; } public bool Blocking { get; init; } = true; public string? Description { get; init; } }
public sealed record StoryStructureRequirement { public required string RequirementId { get; init; } public required string StoryRole { get; init; } public bool Required { get; init; } = true; }
public sealed record BeatCoverageRequirement { public required string FactId { get; init; } public required IReadOnlyList<string> AllowedBeatRoles { get; init; } public bool Required { get; init; } = true; }

public interface IFamilyCertificationProfile
{
    string FamilyId { get; }
    IReadOnlySet<string> SupportedEventTypeAliases { get; }
    string? CanonicalSemanticValueId { get; }
    IReadOnlyList<RequiredSemanticFactDefinition> GetRequiredFacts(FamilyCertificationContext context);
    IReadOnlyList<ForbiddenConceptDefinition> GetForbiddenConcepts(FamilyCertificationContext context);
    IReadOnlyList<StoryStructureRequirement> GetStoryRequirements(FamilyCertificationContext context);
    IReadOnlyList<BeatCoverageRequirement> GetBeatCoverageRequirements(FamilyCertificationContext context);
    IReadOnlyList<PhaseArtifactDefinition> GetAdditionalArtifacts(FamilyCertificationContext context);
}

public interface IFamilyCertificationProfileRegistry
{
    IFamilyCertificationProfile Resolve(string eventType);
    bool TryResolve(string eventType, out IFamilyCertificationProfile? profile);
}

public interface IPhaseCertifier { int PhaseNumber { get; } Task<PhaseCertificationResult> CertifyAsync(FamilyCertificationContext context, CancellationToken cancellationToken); }

public sealed record SemanticCertificationEvidence
{
    public bool CanonicalIdentityPresent { get; init; }
    public bool CanonicalFamilyValuePresent { get; init; }
    public string? FamilyId { get; init; }
    public string? CanonicalSemanticValueId { get; init; }
    public IReadOnlyList<SemanticFactCertificationResult> Facts { get; init; } = [];
    public IReadOnlyList<CertificationIssue> Issues { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

public interface ISemanticCertificationEvidenceReader { Task<SemanticCertificationEvidence> ReadAsync(FamilyCertificationContext context, CancellationToken cancellationToken); }
public interface ICertificationCoordinator { Task<FamilyCertificationSummary> CertifyAsync(FamilyCertificationContext context, CancellationToken cancellationToken); }
public interface ICertificationReportWriter { Task WritePhaseResultAsync(FamilyCertificationContext context, PhaseCertificationResult result, CancellationToken cancellationToken); Task WriteSummaryAsync(FamilyCertificationContext context, FamilyCertificationSummary summary, CancellationToken cancellationToken); }
