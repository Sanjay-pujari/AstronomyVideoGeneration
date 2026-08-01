using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed record Phase5PublicationTransactionRequest(string OutputRoot,
    DocumentaryBlueprintCertificationRequest CertificationRequest,
    DocumentaryBlueprintCertificationIntegrationResult Candidate,
    Phase5ExpectedPhase4Authority ExpectedPhase4, string PhaseName, DateTimeOffset StartedUtc);

public sealed record Phase5PublicationTransactionResult(bool Succeeded, bool AlreadyPublished,
    bool PublicationCommitted, bool CommittedStateValidationPassed, string TransactionId,
    string ReasonCode, string Reason, IReadOnlyList<string> OutputFiles,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors,
    PublishedBlueprintCertification? PublishedAuthority, Phase5CommittedStateEvaluation? CommittedEvaluation,
    bool RollbackPerformed, bool RollbackSucceeded, bool PreviousAuthorityRestored,
    string? FailureDiagnosticsPath);

public sealed record Phase5PublicationTransactionPaths(string TransactionId, string EditorialRoot,
    string StagingRoot, string BackupRoot, string FailedRoot, string ManifestPath,
    string ManifestBackupPath, string ValidationPath, string ValidationBackupPath,
    string TransactionMarkerPath, string FailureDiagnosticsPath)
{
    public static Phase5PublicationTransactionPaths Create(string root, string transactionId) => new(transactionId,
        Path.Combine(root, "05-editorial"), Path.Combine(root, $".05-editorial-staging-{transactionId}"),
        Path.Combine(root, $".05-editorial-backup-{transactionId}"), Path.Combine(root, $".05-editorial-failed-{transactionId}"),
        Path.Combine(root, "phase-manifest.json"), Path.Combine(root, $".phase-05-manifest-backup-{transactionId}.json"),
        Path.Combine(root, "validation", "phase-05-validation.json"), Path.Combine(root, $".phase-05-validation-backup-{transactionId}.json"),
        Path.Combine(root, $".phase-05-transaction-{transactionId}.json"),
        Path.Combine(root, "validation", $"phase-05-publication-failure-{transactionId}.json"));
}

public enum Phase5PublicationTransactionStatus { Preparing, StagedValidated, PreviousStateBackedUp,
    EditorialSwapped, MetadataPublished, Committed, RollingBack, RollbackFailed }

public sealed record Phase5PublicationTransactionMarker(string TransactionId,
    Phase5PublicationTransactionStatus Status, Phase5PublicationTransactionPaths Paths,
    bool PreviousEditorialExisted, bool PreviousManifestExisted, bool PreviousValidationExisted,
    string ExecutionId, string PlanId, string EventId, string Language,
    Phase5ExpectedPhase4Authority ExpectedPhase4, DateTimeOffset UpdatedUtc);

public sealed record Phase5PublicationFailureDiagnostics(string TransactionId, string ReasonCode,
    string OriginalFailureReasonCode, IReadOnlyList<string> OriginalErrors,
    IReadOnlyList<string> RollbackErrors, bool RollbackPerformed, bool RollbackSucceeded,
    bool PreviousEditorialExisted, bool PreviousManifestExisted, bool PreviousValidationExisted,
    bool PreviousAuthorityRestored, IReadOnlyList<string> RemainingTransactionPaths, DateTimeOffset FailedUtc);

public sealed record Phase5PublicationRecoveryResult(bool Succeeded, string ReasonCode,
    IReadOnlyList<string> RecoveredTransactionIds, IReadOnlyList<string> Errors);

public interface IPhase5PublicationTransactionCoordinator
{
    Task<Phase5PublicationTransactionResult> PublishAsync(Phase5PublicationTransactionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPhase5PublicationRecoveryService
{
    Task<Phase5PublicationRecoveryResult> RecoverAsync(string outputRoot, string executionId,
        string planId, string eventId, string language, Phase5ExpectedPhase4Authority expectedPhase4,
        CancellationToken cancellationToken = default);
}
