using System.Text.Json;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase5PublicationRecoveryService(IPhase5CommittedAuthorityEvaluator evaluator)
    : IPhase5PublicationRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<Phase5PublicationRecoveryResult> RecoverAsync(string root, string executionId,
        string planId, string eventId, string language, Core.DocumentaryBlueprint.Phase5ExpectedPhase4Authority expected,
        CancellationToken token = default)
    {
        var recovered = new List<string>();
        var errors = new List<string>();
        foreach (var markerPath in Directory.GetFiles(root, ".phase-05-transaction-*.json").Order(StringComparer.Ordinal))
        {
            Phase5PublicationTransactionMarker marker;
            try
            {
                marker = JsonSerializer.Deserialize<Phase5PublicationTransactionMarker>(await File.ReadAllBytesAsync(markerPath, token), JsonOptions)
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

    private static void Restore(Phase5PublicationTransactionMarker marker)
    {
        var p = marker.Paths;
        if (Directory.Exists(p.EditorialRoot)) Directory.Move(p.EditorialRoot, p.FailedRoot);
        if (marker.PreviousEditorialExisted)
        {
            if (!Directory.Exists(p.BackupRoot)) throw new InvalidOperationException("Previous editorial backup is missing.");
            Directory.Move(p.BackupRoot, p.EditorialRoot);
        }
        RestoreFile(p.ManifestBackupPath, p.ManifestPath, marker.PreviousManifestExisted);
        RestoreFile(p.ValidationBackupPath, p.ValidationPath, marker.PreviousValidationExisted);
        CleanupUncommitted(p);
        if (Directory.Exists(p.FailedRoot)) Directory.Delete(p.FailedRoot, true);
    }

    private static void RestoreFile(string snapshot, string destination, bool existed)
    {
        if (existed)
        {
            if (!File.Exists(snapshot)) throw new InvalidOperationException($"Snapshot is missing: {snapshot}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(snapshot, destination, true);
        }
        else if (File.Exists(destination)) File.Delete(destination);
    }

    private static void CleanupCommitted(Phase5PublicationTransactionPaths p)
    {
        if (Directory.Exists(p.BackupRoot)) Directory.Delete(p.BackupRoot, true);
        CleanupUncommitted(p);
    }

    private static void CleanupUncommitted(Phase5PublicationTransactionPaths p)
    {
        if (Directory.Exists(p.StagingRoot)) Directory.Delete(p.StagingRoot, true);
        if (File.Exists(p.ManifestBackupPath)) File.Delete(p.ManifestBackupPath);
        if (File.Exists(p.ValidationBackupPath)) File.Delete(p.ValidationBackupPath);
        if (File.Exists(p.TransactionMarkerPath)) File.Delete(p.TransactionMarkerPath);
    }
}
