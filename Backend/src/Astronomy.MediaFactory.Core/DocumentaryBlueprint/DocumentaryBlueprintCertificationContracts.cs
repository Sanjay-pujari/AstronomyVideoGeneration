namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryBlueprintCertificationStatus { Certified, CertifiedWithWarnings, Rejected }

public sealed record DocumentaryBlueprintCertificationRequest(
    string ExecutionId, string PlanId, string EventId, string Language, string Profile,
    DocumentaryBlueprintArtifact Master, DocumentaryBlueprintArtifact Long, DocumentaryBlueprintArtifact Short,
    BlueprintBuildDiagnostics Phase4Diagnostics, IReadOnlyList<string> RequestedVariants,
    DocumentaryBlueprintAggregate? PublishedAggregate = null);

public sealed record DocumentaryBlueprintSceneOutcome(string SceneId, string Variant, int Sequence,
    DocumentaryNarrativeStage NarrativeStage, DocumentarySceneRole SceneRole, bool Certified,
    IReadOnlyList<string> KnowledgeReferenceIds);

public sealed record DocumentaryBlueprintCertification(
    string CertificationId, string ExecutionId, string PlanId, string EventId, string Language, string Profile,
    string SourcePhase4Checksum, string SourceMasterBlueprintChecksum, string SourceLongBlueprintChecksum,
    string SourceShortBlueprintChecksum, string CertificationVersion, string CertifierType,
    DocumentaryBlueprintCertificationStatus CertificationStatus, bool Passed,
    IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> NonBlockingWarnings,
    IReadOnlyList<string> CertifiedVariants, IReadOnlyList<string> RejectedVariants,
    IReadOnlyList<DocumentaryBlueprintSceneOutcome> SceneLevelOutcomes,
    IReadOnlyList<string> CoverageOutcomes, IReadOnlyList<string> KnowledgeReferenceOutcomes,
    IReadOnlyList<string> EditorialOutcomes, DateTimeOffset GeneratedUtc, string SemanticChecksum);

public sealed record DocumentaryBlueprintEditorialContract(
    string ContractId, string ExecutionId, string EventId, string Language, string Profile,
    string SourceCertificationId, string SourceCertificationChecksum, string SourcePhase4Checksum,
    IReadOnlyList<string> AllowedVariants, IReadOnlyList<string> CertifiedSceneIds,
    IReadOnlyList<string> SceneOrder, IReadOnlyDictionary<string, string> NarrativeStages,
    IReadOnlyDictionary<string, string> SceneRoles, IReadOnlyList<string> MandatoryViewerQuestions,
    IReadOnlyList<string> LearningObjectives, IReadOnlyList<string> KnowledgeReferenceConstraints,
    IReadOnlyDictionary<string, string> DeferredItems, IReadOnlyList<string> ApprovedEditorialWarnings,
    IReadOnlyList<string> BlockingConstraints, IReadOnlyList<string> DownstreamRequirements,
    bool NarrationEligible, bool StoryFrameEligible, DateTimeOffset GeneratedUtc, string Checksum);

public sealed record DocumentaryBlueprintCertificationDiagnostics(
    string ExecutionId, string CertifierType, string CertifierVersion, string IntegrationServiceType,
    string IntegrationServiceVersion, IReadOnlyList<string> InputArtifactPaths,
    IReadOnlyDictionary<string, string> InputArtifactChecksums, IReadOnlyDictionary<string, int> InputSceneCounts,
    int InputCoverageCount, int CertifiedSceneCount, int RejectedSceneCount, int BlockingIssueCount,
    int WarningCount, IReadOnlyList<string> CertifiedVariants, IReadOnlyList<string> RejectedVariants,
    IReadOnlyList<string> ValidationStagesExecuted, long BuildDurationMilliseconds, string SourcePhase4Checksum);

public sealed record DocumentaryBlueprintCertificationIntegrationResult(
    DocumentaryBlueprintCertification Certification, DocumentaryBlueprintEditorialContract EditorialContract,
    DocumentaryBlueprintCertificationDiagnostics Diagnostics,
    BlueprintValidationReport Validation, BlueprintSceneIntentProjection SceneIntents,
    BlueprintCoverageReport Coverage, BlueprintTransitionReport Transitions,
    BlueprintPauseTestReport PauseTest);

public interface IDocumentaryBlueprintCertificationIntegrationService
{
    Task<DocumentaryBlueprintCertificationIntegrationResult> CertifyAsync(DocumentaryBlueprintCertificationRequest request, CancellationToken cancellationToken);
}
