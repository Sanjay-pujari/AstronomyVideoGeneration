using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public static class Phase4PublicationReasonCodes
{
    public const string ProjectionInvalid="P4PUB_PROJECTION_INVALID", IdentityMismatch="P4PUB_IDENTITY_MISMATCH",
        LanguageMismatch="P4PUB_LANGUAGE_MISMATCH", ProfileMismatch="P4PUB_PROFILE_MISMATCH",
        UpstreamChanged="P4PUB_UPSTREAM_CHANGED", LockFailed="P4PUB_LOCK_FAILED", RecoveryFailed="P4PUB_RECOVERY_FAILED",
        TemporaryDirectoryFailed="P4PUB_TEMP_DIRECTORY_FAILED", SerializationFailed="P4PUB_SERIALIZATION_FAILED",
        TemporaryValidationFailed="P4PUB_TEMP_VALIDATION_FAILED", ChecksumFailed="P4PUB_CHECKSUM_FAILED",
        ManifestPrepareFailed="P4PUB_MANIFEST_PREPARE_FAILED", BackupFailed="P4PUB_BACKUP_FAILED",
        AuthorityCommitFailed="P4PUB_AUTHORITY_COMMIT_FAILED", CommitFailed="P4PUB_COMMIT_FAILED",
        ManifestCommitFailed="P4PUB_MANIFEST_COMMIT_FAILED", ValidationCommitFailed="P4PUB_VALIDATION_COMMIT_FAILED",
        PostCommitValidationFailed="P4PUB_POST_COMMIT_VALIDATION_FAILED", RollbackFailed="P4PUB_ROLLBACK_FAILED",
        CompatibilityFailed="P4PUB_COMPATIBILITY_FAILED";
}

public sealed record Phase4ChecksumSnapshot(IReadOnlyDictionary<string,string> Files);
public sealed record Phase4PublicationPolicy(bool ReplaceExisting=true, bool RemoveStaleTransactions=true,
    TimeSpan? StaleTransactionAge=null);
public sealed record Phase4DocumentaryBlueprintPublicationRequest(string ExecutionRoot,string ExecutionId,string PlanId,
    string EventId,string Language,DocumentaryBlueprintProjectionResult ProjectionResult,
    Phase4ChecksumSnapshot ExpectedPhase1ChecksumSnapshot,Phase4ChecksumSnapshot ExpectedPhase2ChecksumSnapshot,
    Phase4ChecksumSnapshot ExpectedPhase3ChecksumSnapshot,JsonNode? ExistingManifest,bool CompatibilityProjectionRequired,
    Phase4PublicationPolicy PublicationPolicy);

public sealed record Phase4PublicationDiagnostic(string Code,string Message,string? Path=null);
public sealed record Phase4ArtifactEntry(string RelativePath,int PhaseNo,string PhaseName,string ArtifactType,
    string AuthorityClassification,string SchemaVersion,string SemanticChecksum,string PhysicalSha256,long SizeBytes,
    string SourceAuthorityChecksum,string? Variant,bool Required,bool CompatibilityOnly);

public sealed record Phase4DocumentaryBlueprintPublicationResult(bool Success,bool PublicationCommitted,string TransactionId,
    string ExecutionId,string PlanId,string EventId,string PhaseDirectory,string CanonicalAuthorityPath,string LongProjectionPath,
    string ShortProjectionPath,string KnowledgeSelectionPath,string LongSceneIndexPath,string ShortSceneIndexPath,
    string BuildReportPath,string ValidationPath,string ManifestPath,string? CompatibilityPath,string? AggregateId,
    string? AggregateChecksum,IReadOnlyList<Phase4ArtifactEntry> ArtifactEntries,IReadOnlyList<Phase4PublicationDiagnostic> Errors,
    IReadOnlyList<Phase4PublicationDiagnostic> Warnings,bool ManifestUpdated,bool ValidationCommitted,
    bool CompatibilityCommitted,bool RollbackPerformed,bool RecoveryPerformed,bool FrozenUpstreamUnchanged);

public sealed record Phase4KnowledgeSelectionEntry(string KnowledgeSelectionId,string Variant,string SceneOpportunityId,
    string SceneId,string PrimaryViewerQuestionId,string KnowledgeReferenceId,string SourceArtifact,string SourcePointer,
    string SemanticChecksum,string PurposeCode,string SelectionReasonCode,bool IsPrimary,QuestionEvidenceStatus EvidenceStatus);
public sealed record Phase4KnowledgeReuse(string KnowledgeReferenceId,int LongCount,int ShortCount,int TotalCount);
public sealed record DocumentaryBlueprintKnowledgeSelectionArtifact(string SchemaVersion,string ContractVersion,string ExecutionId,
    string PlanId,string EventId,string Language,string ProfileId,string ProfileVersion,string SourceAggregateId,
    string SourceAggregateChecksum,IReadOnlyList<Phase4KnowledgeSelectionEntry> LongSelections,
    IReadOnlyList<Phase4KnowledgeSelectionEntry> ShortSelections,IReadOnlyList<string> UniqueKnowledgeReferences,
    IReadOnlyList<Phase4KnowledgeReuse> KnowledgeReuseSummary,int EditorialOnlySceneCount,int MixedSceneCount,string DeterministicChecksum);
public sealed record Phase4SceneIndexEntry(string SceneId,int SceneNumber,string SourceOpportunityId,string ProfileSlotId,
    string NarrativeStage,string SceneRole,string PrimaryViewerQuestionId,IReadOnlyList<string> SupportingViewerQuestionIds,
    string LearningObjectiveId,QuestionEvidenceStatus QuestionEvidenceStatus,int TargetDurationSeconds,int MinimumDurationSeconds,
    int MaximumDurationSeconds,string TransitionIntent,IReadOnlyList<string> KnowledgeReferenceIds,
    IReadOnlyList<string> EditorialConstraintCodes,IReadOnlyList<string> MustNotClaim,string SceneChecksum,string SourceOpportunityChecksum);
public sealed record Phase4SceneIndex(string SchemaVersion,string ContractVersion,string Variant,string SourceAggregateId,
    string SourceAggregateChecksum,IReadOnlyList<Phase4SceneIndexEntry> Scenes,string DeterministicChecksum);
public sealed record Phase4BlueprintBuildReport(string SchemaVersion,string ContractVersion,string PublicationVersion,
    string ExecutionId,string PlanId,string EventId,string Language,string ProfileId,string ProfileVersion,string IntentId,
    string IntentChecksum,string AggregateId,string AggregateChecksum,string LongVariantId,string LongVariantChecksum,
    string ShortVariantId,string ShortVariantChecksum,int LongSceneCount,int ShortSceneCount,int LongDurationSeconds,
    int ShortDurationSeconds,bool QuestionReconciliationPassed,bool ObjectiveReconciliationPassed,
    bool KnowledgeReconciliationPassed,bool SafetyReconciliationPassed,bool DurationReconciliationPassed,
    bool TransitionReconciliationPassed,bool VariantIndependencePassed,bool ChecksumValidationPassed,
    IReadOnlyList<string> ArtifactInventory,bool CompatibilityProjectionGenerated,string PublicationStatus,string DeterministicChecksum);

public enum Phase4PublicationCheckpoint { TempDirectoryCreated,FirstArtifactWritten,AllArtifactsWritten,TemporaryValidationPassed,
    BackupPhaseMoved,BackupManifestCreated,BackupValidationCreated,PhaseDirectoryCommitted,ManifestCommitted,
    PostCommitValidated,SuccessValidationCommitted }
public interface IPhase4PublicationFaultInjector { void Checkpoint(Phase4PublicationCheckpoint checkpoint); }
public sealed class Phase4PublicationFaultInjector:IPhase4PublicationFaultInjector { public void Checkpoint(Phase4PublicationCheckpoint checkpoint) { } }
public sealed record Phase4TransactionRecord(string SchemaVersion,string ExecutionId,string TransactionId,string AggregateChecksum,
    DateTimeOffset CreatedUtc,string State,string DeterministicChecksum);
public sealed record Phase4ValidationRecord(string SchemaVersion,string ContractVersion,int PhaseNo,string PhaseName,string ExecutionId,
    string PlanId,string EventId,string Language,string ProfileId,string ProfileVersion,string TransactionId,DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,string Status,string ValidationStatus,bool PublicationCommitted,string AggregateId,string AggregateChecksum,
    string LongVariantChecksum,string ShortVariantChecksum,bool SemanticValidationPassed,bool ChecksumValidationPassed,
    bool ManifestValidationPassed,bool ProjectionValidationPassed,bool KnowledgeSelectionValidationPassed,bool SceneIndexValidationPassed,
    bool BuildReportValidationPassed,bool FrozenUpstreamValidationPassed,int LongSceneCount,int ShortSceneCount,int LongDurationSeconds,
    int ShortDurationSeconds,int ArtifactCount,int AuthoritativeArtifactCount,int DerivedArtifactCount,IReadOnlyList<Phase4PublicationDiagnostic> Errors,
    IReadOnlyList<Phase4PublicationDiagnostic> Warnings,string DeterministicChecksum);
public sealed record Phase4BackupMutationState(bool PhaseMoved,bool ManifestCopied,bool ValidationCopied,bool CompatibilityCopied);

public interface IPhase4DocumentaryBlueprintPublicationService { Task<Phase4DocumentaryBlueprintPublicationResult> PublishAsync(Phase4DocumentaryBlueprintPublicationRequest request,CancellationToken cancellationToken=default); }
public interface IPhase4PublicationTransactionCoordinator:IPhase4DocumentaryBlueprintPublicationService { }
public interface IPhase4ArtifactSerializer { byte[] Serialize<T>(T value); T Deserialize<T>(byte[] bytes); string SemanticChecksum<T>(T value,Func<T,T> clearChecksum); }
public interface IPhase4PublishedAuthorityValidator { Task<IReadOnlyList<Phase4PublicationDiagnostic>> ValidateAsync(string phaseDirectory,DocumentaryBlueprintAggregate expected,CancellationToken token); }
public interface IPhase4CommittedStateValidator { Task<IReadOnlyList<Phase4PublicationDiagnostic>> ValidateAsync(string executionRoot,DocumentaryBlueprintAggregate expected,CancellationToken token); }
public interface IPhase4ManifestUpdater { byte[] Merge(byte[]? existing,IReadOnlyList<Phase4ArtifactEntry> entries); }
public interface IPhase4RecoveryService { Task<bool> RecoverAsync(string executionRoot,CancellationToken token); }
public interface IPhase4ExecutionLock { ValueTask<IAsyncDisposable> AcquireAsync(string executionRoot,string executionId,CancellationToken token); }
public interface IPhase4FileSystem { Task WriteAsync(string path,byte[] bytes,CancellationToken token); byte[] Read(string path); string Sha256(byte[] bytes); }
