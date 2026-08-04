namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class NarrationPlanningPublicationContract
{
    public const string ValidationVersion = "rc2-phase7-narration-planning-validation.v1";
    public const string DiagnosticsVersion = "rc2-phase7-narration-planning-diagnostics.v1";
    public const string PublicationVersion = "rc2-phase7-narration-planning-publication.v1";
}
public static class NarrationPlanningPublicationReasonCodes
{
    public const string Committed="NARRATION_PLANNING_COMMITTED", ReuseValid="NARRATION_PLANNING_REUSE_VALID",
        InputInvalid="NARRATION_PLANNING_INPUT_INVALID", BuildInvalid="NARRATION_PLANNING_BUILD_INVALID",
        ValidationInvalid="NARRATION_PLANNING_VALIDATION_INVALID", TransactionFailed="NARRATION_PLANNING_TRANSACTION_FAILED",
        PhysicalReadbackInvalid="NARRATION_PLANNING_PHYSICAL_READBACK_INVALID", CommittedStateInvalid="NARRATION_PLANNING_COMMITTED_STATE_INVALID",
        LineageStale="NARRATION_PLANNING_LINEAGE_STALE", LockUnavailable="NARRATION_PLANNING_LOCK_UNAVAILABLE";
}
public static class NarrationPlanningArtifactPaths
{
    public const string Authority="07-narration/planning/narration-planning-authority.json";
    public const string Diagnostics="07-narration/planning/narration-planning-diagnostics.json";
    public const string Report="07-narration/planning/narration-planning-report.json";
    public const string Validation="validation/phase-07-narration-planning-validation.json";
    public const string Manifest="phase-manifest.json";
    public const string PublicationEvidence=".phase-07-narration-planning-publication.json";
    public static readonly IReadOnlyList<string> Governed=[Authority,Diagnostics,Report,Validation,Manifest,PublicationEvidence];
}
public sealed record NarrationPlanningArtifact(string RelativePath,string PhysicalSha256,long SizeBytes,string SemanticChecksum);
public sealed record NarrationPlanningContentArtifactInventory(IReadOnlyList<NarrationPlanningArtifact> Artifacts);
public sealed record NarrationPlanningValidatedArtifactInventory(IReadOnlyList<NarrationPlanningArtifact> Artifacts);
public sealed record NarrationPlanningCommittedArtifactInventory(IReadOnlyList<NarrationPlanningArtifact> Artifacts);
public sealed record NarrationPlanningDiagnosticsArtifact(string ContractVersion,string AuthorityId,string AuthorityChecksum,
    string ExecutionId,string PlanId,string EventId,string Language,string ProfileId,string ProfileVersion,
    NarrationPlanningDiagnostics Diagnostics,string DeterministicChecksum);
public sealed record NarrationPlanningPublicationReport(string ContractVersion,string ExecutionId,string PlanId,string EventId,
    string Language,string ProfileId,string ProfileVersion,string AuthorityId,string AuthorityChecksum,int LongPlanningSceneCount,
    int ShortPlanningSceneCount,int TotalPlanningSceneCount,int PrimaryReferenceCount,int SupportingReferenceCount,int RequiredClaimCount,
    int OptionalClaimCount,int DeferredClaimCount,int TransitionCount,int BlockingIssueCount,int FailedGateCount,int WarningCount,
    string ValidationReasonCode,string PublicationMode,bool Reused,string DeterministicChecksum);
public sealed record NarrationPlanningPhysicalResult(bool IsValid,string ReasonCode,IReadOnlyList<NarrationPlanningArtifact> Artifacts,
    IReadOnlyList<string> Errors);
public sealed record NarrationPlanningPhysicalValidation(string ContractVersion,string AuthorityId,string AuthorityChecksum,
    string ExecutionId,string PlanId,string EventId,string Language,string ProfileId,string ProfileVersion,string ValidationMode,
    IReadOnlyList<NarrationPlanningValidationGate> GateResults,IReadOnlyList<string> Errors,IReadOnlyList<string> Warnings,
    IReadOnlyList<NarrationPlanningArtifact> ArtifactInventory,IReadOnlyDictionary<string,string> LineageEvidence,
    bool PhysicalReadbackPassed,bool CommittedStatePassed,string DeterministicChecksum)
{
    public bool CandidateReadbackPassed { get; init; }
    public bool CommittedReadbackPassed { get; init; }
}
public sealed record NarrationPlanningManifestEntry(int PhaseNo,string SubPhase,string Name,string ContractVersion,string ExecutionId,string PlanId,string EventId,string AuthorityId,
    string AuthorityChecksum,string DiagnosticsChecksum,string ReportChecksum,string ValidationChecksum,string Phase6AuthorityId,
    string Phase6AuthorityChecksum,string Phase7KnowledgeAuthorityId,string Phase7KnowledgeAuthorityChecksum,string PacketCollectionChecksum,
    string ProfileId,string ProfileVersion,string Language,string PublicationStatus,DateTimeOffset CommittedAtUtc,
    IReadOnlyList<NarrationPlanningArtifact> ArtifactInventory,string DeterministicChecksum);
public sealed record NarrationPlanningPublicationEvidence(string ContractVersion,string State,string ExecutionId,string PlanId,string EventId,
    string Language,string ProfileId,string ProfileVersion,string AuthorityId,string AuthorityChecksum,string ValidationChecksum,
    string ManifestEntryChecksum,string ArtifactInventoryChecksum,string Phase6AuthorityId,string Phase6AuthorityChecksum,
    string Phase7KnowledgeAuthorityId,string Phase7KnowledgeAuthorityChecksum,string PacketCollectionChecksum,bool CommittedPhysical,
    DateTimeOffset PublishedAtUtc,string DeterministicChecksum);
public sealed record PublishedNarrationPlanningAuthority(NarrationPlanningAuthority Authority,NarrationPlanningDiagnostics Diagnostics,
    NarrationPlanningPublicationReport Report,NarrationPlanningPhysicalValidation Validation,NarrationPlanningManifestEntry ManifestEntry,
    NarrationPlanningPublicationEvidence PublicationEvidence,IReadOnlyList<string> PhysicalArtifactPaths,
    IReadOnlyDictionary<string,string> PhysicalHashes,IReadOnlyList<string> CommittedStateDiagnostics);
public sealed record Phase7NarrationPlanningPublicationRequest(Phase7NarrationPlanningInputAuthorityRequest Input,
    bool OverwriteExisting=false,bool RetryFailedOnly=false);
public sealed record Phase7NarrationPlanningPublicationResult(bool Success,string ReasonCode,bool AlreadyPublished,bool Reused,
    bool PublicationCommitted,bool CommittedStateValidationPassed,string AuthorityId,string AuthorityChecksum,int LongPlanningSceneCount,
    int ShortPlanningSceneCount,int TotalPlanningSceneCount,IReadOnlyList<string> Warnings,IReadOnlyList<string> Errors,
    IReadOnlyList<string> ArtifactPaths,string ValidationPath,string PublicationEvidencePath);
public sealed record NarrationPlanningCommittedStateEvaluation(bool IsValid,PublishedNarrationPlanningAuthority? Authority,string ReasonCode,
    IReadOnlyList<string> Errors,IReadOnlyList<string> Warnings);
public sealed record NarrationPlanningCandidateReadbackResult(bool IsValid,string ReasonCode,
    IReadOnlyList<NarrationPlanningArtifact> Artifacts,IReadOnlyList<string> Errors,IReadOnlyList<string> Warnings);
public sealed record NarrationPlanningArtifactReconciliationResult(bool IsValid,string ReasonCode,
    IReadOnlyList<string> Errors,IReadOnlyList<string> Warnings);
public sealed record NarrationPlanningRecoveryResult(bool IsValid,bool Recovered,string ReasonCode,
    IReadOnlyList<string> Actions,IReadOnlyList<string> Errors,IReadOnlyList<string> Warnings);
public enum NarrationPlanningPublicationFaultPoint { AfterAuthorityStageWrite,AfterDiagnosticsStageWrite,AfterReportStageWrite,
    AfterValidationStageWrite,AfterManifestStageWrite,AfterEvidenceStageWrite,AfterCandidateReadback,AfterBackup,
    AfterPlanningSwap,AfterValidationSwap,AfterManifestSwap,AfterEvidenceSwap,BeforeCommittedReadback,
    AfterCommittedReadback,BeforeBackupDeletion }
public interface IPhase7NarrationPlanningFileSystem { bool FileExists(string path);bool DirectoryExists(string path);void CreateDirectory(string path);Task<byte[]> ReadAsync(string path,CancellationToken token=default);Task WriteAsync(string path,byte[] bytes,CancellationToken token=default);void MoveFile(string source,string destination,bool overwrite);void MoveDirectory(string source,string destination);void DeleteFile(string path);void DeleteDirectory(string path);IReadOnlyList<string> Directories(string root,string pattern); }
public interface IPhase7NarrationPlanningExecutionLock { Task<IAsyncDisposable?> TryAcquireAsync(string executionRoot,string planId,CancellationToken token=default); }
public interface IPhase7NarrationPlanningRecoveryService { Task RecoverAsync(string executionRoot,CancellationToken token=default); }
public interface IPhase7NarrationPlanningPhysicalReadback { Task<NarrationPlanningPhysicalResult> ReadCommittedAsync(string executionRoot,CancellationToken token=default); }
public interface IPhase7NarrationPlanningCandidateReadback { Task<NarrationPlanningCandidateReadbackResult> ReadAsync(string stagingRoot,CancellationToken token=default); }
public interface IPhase7NarrationPlanningArtifactReconciler { NarrationPlanningArtifactReconciliationResult Reconcile(
    NarrationPlanningAuthority authority,NarrationPlanningDiagnosticsArtifact diagnostics,NarrationPlanningPublicationReport report,
    NarrationPlanningPhysicalValidation validation,NarrationPlanningManifestEntry manifest,NarrationPlanningPublicationEvidence evidence,
    IReadOnlyList<NarrationPlanningArtifact> inventory,Phase7NarrationPlanningInputAuthorityRequest? currentInput=null); }
public interface IPhase7NarrationPlanningPublicationFaultInjector { void Inject(NarrationPlanningPublicationFaultPoint point); }
public interface IPhase7NarrationPlanningClock { DateTimeOffset UtcNow { get; } }
public interface IPhase7NarrationPlanningCommittedStateEvaluator { Task<NarrationPlanningCommittedStateEvaluation> EvaluateAsync(Phase7NarrationPlanningInputAuthorityRequest input,CancellationToken token=default); }
public interface IPhase7NarrationPlanningTransactionCoordinator { Task<Phase7NarrationPlanningPublicationResult> ExecuteAsync(Phase7NarrationPlanningPublicationRequest request,CancellationToken token=default); }
public interface IPhase7NarrationPlanningPublicationService { Task<Phase7NarrationPlanningPublicationResult> ExecuteAsync(Phase7NarrationPlanningPublicationRequest request,CancellationToken token=default); }
