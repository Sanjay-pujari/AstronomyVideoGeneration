using System.Text.Json;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase5PublicationRecoveryService(IPhase5CommittedAuthorityEvaluator evaluator,
    IPhase5PublicationFileSystem fileSystem)
    : IPhase5PublicationRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<Phase5PublicationRecoveryResult> RecoverAsync(string root, string executionId,
        string planId, string eventId, string language, Core.DocumentaryBlueprint.Phase5ExpectedPhase4Authority expected,
        CancellationToken token = default)
    {
        var recovered = new List<string>();
        var errors = new List<string>();
        foreach (var markerPath in fileSystem.GetFiles(root, ".phase-05-transaction-*.json").Order(StringComparer.Ordinal))
        {
            Phase5PublicationTransactionMarker marker;
            try
            {
                marker = JsonSerializer.Deserialize<Phase5PublicationTransactionMarker>(await fileSystem.ReadAllBytesAsync(markerPath, token), JsonOptions)
                    ?? throw new JsonException("Transaction marker is empty.");
                var exact = Phase5PublicationTransactionPaths.Create(root, marker.TransactionId);
                if (marker.Paths != exact || marker.Paths.TransactionMarkerPath != markerPath)
                    return new(false, "P5PUB_RECOVERY_AMBIGUOUS_STATE", recovered,
                        [$"Transaction marker paths do not match transaction {marker.TransactionId}."]);
                if (marker.Status == Phase5PublicationTransactionStatus.RollbackFailed)
                    return new(false, "P5PUB_ROLLBACK_FAILED", recovered,
                        [$"Transaction {marker.TransactionId} has a prior rollback failure."]);

                if (marker.Status is Phase5PublicationTransactionStatus.Preparing or Phase5PublicationTransactionStatus.StagedValidated)
                    CleanupUncommitted(marker.Paths);
                else if (marker.Status is Phase5PublicationTransactionStatus.MetadataPublished or Phase5PublicationTransactionStatus.Committed)
                {
                    var committed = await evaluator.EvaluateAsync(root, executionId, planId, eventId, language, expected, token);
                    if (committed.IsValid) CleanupCommitted(marker.Paths);
                    else Restore(marker);
                }
                else Restore(marker);
                recovered.Add(marker.TransactionId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException)
            { errors.Add($"{markerPath}: {ex.Message}"); }
        }
        return errors.Count == 0
            ? new(true, "P5PUB_RECOVERY_COMPLETED", recovered, [])
            : new(false, "P5PUB_RECOVERY_FAILED", recovered, errors);
    }

    private void Restore(Phase5PublicationTransactionMarker marker)
    {
        var p = marker.Paths;
        if (fileSystem.DirectoryExists(p.EditorialRoot)) fileSystem.MoveDirectory(p.EditorialRoot, p.FailedRoot);
        if (marker.PreviousEditorialExisted)
        {
            if (!fileSystem.DirectoryExists(p.BackupRoot)) throw new InvalidOperationException("Previous editorial backup is missing.");
            fileSystem.MoveDirectory(p.BackupRoot, p.EditorialRoot);
        }
        RestoreFile(p.ManifestBackupPath, p.ManifestPath, marker.PreviousManifestExisted);
        RestoreFile(p.ValidationBackupPath, p.ValidationPath, marker.PreviousValidationExisted);
        CleanupUncommitted(p);
        if (fileSystem.DirectoryExists(p.FailedRoot)) fileSystem.DeleteDirectory(p.FailedRoot, true);
    }

    private void RestoreFile(string snapshot, string destination, bool existed)
    {
        if (existed)
        {
            if (!fileSystem.FileExists(snapshot)) throw new InvalidOperationException($"Snapshot is missing: {snapshot}");
            fileSystem.CreateDirectory(Path.GetDirectoryName(destination)!);
            fileSystem.MoveFile(snapshot, destination, true);
        }
        else if (fileSystem.FileExists(destination)) fileSystem.DeleteFile(destination);
    }

    private void CleanupCommitted(Phase5PublicationTransactionPaths p)
    {
        if (fileSystem.DirectoryExists(p.BackupRoot)) fileSystem.DeleteDirectory(p.BackupRoot, true);
        CleanupUncommitted(p);
    }

    private void CleanupUncommitted(Phase5PublicationTransactionPaths p)
    {
        if (fileSystem.DirectoryExists(p.StagingRoot)) fileSystem.DeleteDirectory(p.StagingRoot, true);
        if (fileSystem.FileExists(p.ManifestBackupPath)) fileSystem.DeleteFile(p.ManifestBackupPath);
        if (fileSystem.FileExists(p.ValidationBackupPath)) fileSystem.DeleteFile(p.ValidationBackupPath);
        if (fileSystem.FileExists(p.TransactionMarkerPath)) fileSystem.DeleteFile(p.TransactionMarkerPath);
    }
}
