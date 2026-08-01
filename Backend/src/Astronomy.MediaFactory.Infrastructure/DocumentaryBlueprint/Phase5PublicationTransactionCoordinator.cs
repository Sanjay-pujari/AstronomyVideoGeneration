using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase5PublicationTransactionCoordinator(
    IPhase5CommittedAuthorityEvaluator evaluator, IPhase5PublicationRecoveryService recovery)
    : IPhase5PublicationTransactionCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly (string File, string Role, Func<DocumentaryBlueprintCertificationIntegrationResult, object> Value, Func<DocumentaryBlueprintCertificationIntegrationResult, string> Semantic)[] Required =
    [
        ("blueprint-validation.json", "SupportingValidation", x => x.Validation, x => x.Validation.SemanticChecksum),
        ("blueprint-certification.json", "CanonicalAuthority", x => x.Certification, x => x.Certification.SemanticChecksum),
        ("editorial-contract.json", "DownstreamContract", x => x.EditorialContract, x => x.EditorialContract.Checksum),
        ("scene-intents.json", "SupportingProjection", x => x.SceneIntents, x => x.SceneIntents.SemanticChecksum),
        ("coverage-report.json", "SupportingValidation", x => x.Coverage, x => x.Coverage.SemanticChecksum),
        ("transition-report.json", "SupportingValidation", x => x.Transitions, x => x.Transitions.SemanticChecksum),
        ("pause-test-report.json", "SupportingValidation", x => x.PauseTest, x => x.PauseTest.SemanticChecksum)
    ];

    public async Task<Phase5PublicationTransactionResult> PublishAsync(Phase5PublicationTransactionRequest request,
        CancellationToken token = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var paths = Phase5PublicationTransactionPaths.Create(request.OutputRoot, id);
        var candidateErrors = DocumentaryBlueprintCertificationArtifactValidator.Validate(request.Candidate, request.CertificationRequest);
        if (candidateErrors.Count != 0) return Failed(id, "P5PUB_CANDIDATE_INVALID", candidateErrors);
        var recovered = await recovery.RecoverAsync(request.OutputRoot, request.CertificationRequest.ExecutionId,
            request.CertificationRequest.PlanId, request.CertificationRequest.EventId,
            request.CertificationRequest.Language, request.ExpectedPhase4, token);
        if (!recovered.Succeeded) return Failed(id, recovered.ReasonCode, recovered.Errors);

        var marker = new Phase5PublicationTransactionMarker(id, Phase5PublicationTransactionStatus.Preparing, paths,
            false, false, false, request.CertificationRequest.ExecutionId, request.CertificationRequest.PlanId,
            request.CertificationRequest.EventId, request.CertificationRequest.Language, request.ExpectedPhase4,
            DateTimeOffset.UtcNow);
        Phase5CommittedStateEvaluation? evaluation = null;
        try
        {
            Directory.CreateDirectory(paths.StagingRoot);
            await WriteMarker(marker, token);
            foreach (var item in Required) await WriteAtomic(Path.Combine(paths.StagingRoot, item.File), item.Value(request.Candidate), token);
            await WriteAtomic(Path.Combine(paths.StagingRoot, "certification-diagnostics.json"), request.Candidate.Diagnostics, token);
            var staged = await ReadStaged(paths.StagingRoot, token);
            var stagedErrors = DocumentaryBlueprintCertificationArtifactValidator.Validate(staged, request.CertificationRequest);
            if (stagedErrors.Count != 0 || Required.Any(x => new FileInfo(Path.Combine(paths.StagingRoot, x.File)).Length == 0))
                throw new InvalidOperationException("P5PUB_STAGED_INVALID: " + string.Join("; ", stagedErrors));
            marker = marker with { Status = Phase5PublicationTransactionStatus.StagedValidated, UpdatedUtc = DateTimeOffset.UtcNow };
            await WriteMarker(marker, token);

            marker = marker with { PreviousEditorialExisted = Directory.Exists(paths.EditorialRoot),
                PreviousManifestExisted = File.Exists(paths.ManifestPath), PreviousValidationExisted = File.Exists(paths.ValidationPath) };
            if (marker.PreviousManifestExisted) File.Copy(paths.ManifestPath, paths.ManifestBackupPath, true);
            if (marker.PreviousValidationExisted) File.Copy(paths.ValidationPath, paths.ValidationBackupPath, true);
            if (marker.PreviousEditorialExisted) Directory.Move(paths.EditorialRoot, paths.BackupRoot);
            marker = marker with { Status = Phase5PublicationTransactionStatus.PreviousStateBackedUp, UpdatedUtc = DateTimeOffset.UtcNow };
            await WriteMarker(marker, token);
            Directory.Move(paths.StagingRoot, paths.EditorialRoot);
            marker = marker with { Status = Phase5PublicationTransactionStatus.EditorialSwapped, UpdatedUtc = DateTimeOffset.UtcNow };
            await WriteMarker(marker, token);
            await WriteManifest(request, paths, token);
            var outputs = Required.Select(x => Path.Combine(paths.EditorialRoot, x.File)).Append(Path.Combine(paths.EditorialRoot, "certification-diagnostics.json")).ToArray();
            await WriteValidation(request, paths, outputs, token);
            marker = marker with { Status = Phase5PublicationTransactionStatus.MetadataPublished, UpdatedUtc = DateTimeOffset.UtcNow };
            await WriteMarker(marker, token);
            evaluation = await evaluator.EvaluateAsync(request.OutputRoot, request.CertificationRequest.ExecutionId,
                request.CertificationRequest.PlanId, request.CertificationRequest.EventId,
                request.CertificationRequest.Language, request.ExpectedPhase4, token);
            if (!evaluation.IsValid || evaluation.PublishedAuthority is null)
                // Phase 5 committed-state readback failed; rollback remains coordinator-owned.
                return await Rollback(request, marker, evaluation.ReasonCode, evaluation.Errors, evaluation, token);
            marker = marker with { Status = Phase5PublicationTransactionStatus.Committed, UpdatedUtc = DateTimeOffset.UtcNow };
            await WriteMarker(marker, token);
            CleanupSuccess(paths);
            return new(true, false, true, true, id, "P5PUB_COMMITTED", "Phase 5 publication committed.",
                outputs, [], [], evaluation.PublishedAuthority, evaluation, false, true, false, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException)
        {
            return await Rollback(request, marker, "P5PUB_PUBLICATION_FAILED", [ex.Message], evaluation, CancellationToken.None);
        }
    }

    private async Task<Phase5PublicationTransactionResult> Rollback(Phase5PublicationTransactionRequest request,
        Phase5PublicationTransactionMarker marker, string originalCode, IReadOnlyList<string> originalErrors,
        Phase5CommittedStateEvaluation? evaluation, CancellationToken token)
    {
        var p = marker.Paths; var errors = new List<string>();
        async Task Attempt(Action action) { try { action(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException) { errors.Add(ex.Message); } await Task.CompletedTask; }
        marker = marker with { Status = Phase5PublicationTransactionStatus.RollingBack, UpdatedUtc = DateTimeOffset.UtcNow };
        try { await WriteMarker(marker, token); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException) { errors.Add(ex.Message); }
        await Attempt(() => { if (Directory.Exists(p.EditorialRoot)) Directory.Move(p.EditorialRoot, p.FailedRoot); });
        await Attempt(() => { if (marker.PreviousEditorialExisted) { if (!Directory.Exists(p.BackupRoot)) throw new InvalidOperationException("Editorial backup is missing."); Directory.Move(p.BackupRoot, p.EditorialRoot); } });
        await Attempt(() => RestoreFile(p.ManifestBackupPath, p.ManifestPath, marker.PreviousManifestExisted));
        await Attempt(() => RestoreFile(p.ValidationBackupPath, p.ValidationPath, marker.PreviousValidationExisted));
        if (marker.PreviousEditorialExisted != Directory.Exists(p.EditorialRoot)) errors.Add("Restored editorial state does not match snapshot.");
        if (marker.PreviousManifestExisted != File.Exists(p.ManifestPath)) errors.Add("Restored manifest state does not match snapshot.");
        if (marker.PreviousValidationExisted != File.Exists(p.ValidationPath)) errors.Add("Restored validation state does not match snapshot.");
        var restored = marker.PreviousEditorialExisted && Directory.Exists(p.EditorialRoot);
        if (errors.Count == 0)
        {
            CleanupSuccess(p);
            if (Directory.Exists(p.FailedRoot)) Directory.Delete(p.FailedRoot, true);
            return new(false, false, false, false, p.TransactionId, originalCode, "Committed readback failed; previous state was restored.",
                [], [], originalErrors, null, evaluation, true, true, restored, null);
        }
        marker = marker with { Status = Phase5PublicationTransactionStatus.RollbackFailed, UpdatedUtc = DateTimeOffset.UtcNow };
        try { await WriteMarker(marker, token); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException) { errors.Add(ex.Message); }
        var remaining = AllPaths(p).Where(x => File.Exists(x) || Directory.Exists(x)).ToArray();
        var diagnostic = new Phase5PublicationFailureDiagnostics(p.TransactionId, "P5PUB_ROLLBACK_FAILED", originalCode,
            originalErrors, errors, true, false, marker.PreviousEditorialExisted, marker.PreviousManifestExisted,
            marker.PreviousValidationExisted, restored, remaining, DateTimeOffset.UtcNow);
        try { await WriteAtomic(p.FailureDiagnosticsPath, diagnostic, token); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException) { errors.Add(ex.Message); }
        return new(false, false, false, false, p.TransactionId, "P5PUB_ROLLBACK_FAILED", "Phase 5 publication and rollback failed.",
            [], [], originalErrors.Concat(errors).ToArray(), null, evaluation, true, false, restored, p.FailureDiagnosticsPath);
    }

    private static async Task WriteManifest(Phase5PublicationTransactionRequest request, Phase5PublicationTransactionPaths p, CancellationToken token)
    {
        var root = File.Exists(p.ManifestPath) ? JsonNode.Parse(await File.ReadAllTextAsync(p.ManifestPath, token))?.AsObject() ?? new() : new JsonObject();
        var entries = new JsonArray();
        foreach (var item in Required)
        {
            var path = Path.Combine(p.EditorialRoot, item.File); var bytes = await File.ReadAllBytesAsync(path, token);
            entries.Add(JsonSerializer.SerializeToNode(new { relativePath = $"05-editorial/{item.File}", role = item.Role,
                semanticChecksum = item.Semantic(request.Candidate), physicalSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                size = bytes.LongLength, sourcePhase4Checksum = request.ExpectedPhase4.AggregateChecksum }, JsonOptions));
        }
        root["phase5Artifacts"] = entries;
        await WriteAtomic(p.ManifestPath, root, token);
    }

    private static Task WriteValidation(Phase5PublicationTransactionRequest request, Phase5PublicationTransactionPaths p, string[] outputs, CancellationToken token) =>
        WriteAtomic(p.ValidationPath, new { phaseNo = 5, phaseName = request.PhaseName, status = "Succeeded", publicationCommitted = true,
            validationStatus = "Valid", executionId = request.CertificationRequest.ExecutionId, planId = request.CertificationRequest.PlanId,
            eventId = request.CertificationRequest.EventId, language = request.CertificationRequest.Language,
            sourcePhase4AggregateId = request.ExpectedPhase4.AggregateId, sourcePhase4Checksum = request.ExpectedPhase4.AggregateChecksum,
            sourceLongChecksum = request.ExpectedPhase4.LongChecksum, sourceShortChecksum = request.ExpectedPhase4.ShortChecksum,
            certificationId = request.Candidate.Certification.CertificationId, certificationChecksum = request.Candidate.Certification.SemanticChecksum,
            certificationStatus = request.Candidate.Certification.CertificationStatus.ToString(), certifiedVariants = request.Candidate.Certification.CertifiedVariants,
            rejectedVariants = request.Candidate.Certification.RejectedVariants, coverageValid = request.Candidate.Coverage.IsValid,
            transitionsValid = request.Candidate.Transitions.IsValid, pauseTestPassedSceneCount = request.Candidate.PauseTest.PassedSceneCount,
            pauseTestFailedSceneCount = request.Candidate.PauseTest.FailedSceneCount, transactionId = p.TransactionId, outputFiles = outputs,
            startedUtc = request.StartedUtc, completedUtc = DateTimeOffset.UtcNow }, token);

    private static async Task<DocumentaryBlueprintCertificationIntegrationResult> ReadStaged(string root, CancellationToken token) => new(
        await Read<DocumentaryBlueprintCertification>(root, "blueprint-certification.json", token), await Read<DocumentaryBlueprintEditorialContract>(root, "editorial-contract.json", token),
        await Read<DocumentaryBlueprintCertificationDiagnostics>(root, "certification-diagnostics.json", token), await Read<BlueprintValidationReport>(root, "blueprint-validation.json", token),
        await Read<BlueprintSceneIntentProjection>(root, "scene-intents.json", token), await Read<BlueprintCoverageReport>(root, "coverage-report.json", token),
        await Read<BlueprintTransitionReport>(root, "transition-report.json", token), await Read<BlueprintPauseTestReport>(root, "pause-test-report.json", token));
    private static async Task<T> Read<T>(string root, string file, CancellationToken token) => JsonSerializer.Deserialize<T>(await File.ReadAllBytesAsync(Path.Combine(root, file), token), JsonOptions) ?? throw new JsonException($"Empty staged artifact: {file}");
    private static Task WriteMarker(Phase5PublicationTransactionMarker marker, CancellationToken token) => WriteAtomic(marker.Paths.TransactionMarkerPath, marker, token);
    private static async Task WriteAtomic(string path, object value, CancellationToken token) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temp = path + $".{Guid.NewGuid():N}.tmp"; await File.WriteAllTextAsync(temp, value is JsonNode n ? n.ToJsonString(JsonOptions) : JsonSerializer.Serialize(value, JsonOptions), token); File.Move(temp, path, true); }
    private static void RestoreFile(string snapshot, string destination, bool existed) { if (existed) { if (!File.Exists(snapshot)) throw new InvalidOperationException($"Snapshot missing: {snapshot}"); File.Move(snapshot, destination, true); } else if (File.Exists(destination)) File.Delete(destination); }
    private static void CleanupSuccess(Phase5PublicationTransactionPaths p) { if (Directory.Exists(p.BackupRoot)) Directory.Delete(p.BackupRoot, true); if (Directory.Exists(p.StagingRoot)) Directory.Delete(p.StagingRoot, true); foreach (var f in new[] { p.ManifestBackupPath, p.ValidationBackupPath, p.TransactionMarkerPath }) if (File.Exists(f)) File.Delete(f); }
    private static IEnumerable<string> AllPaths(Phase5PublicationTransactionPaths p) => [p.EditorialRoot,p.StagingRoot,p.BackupRoot,p.FailedRoot,p.ManifestBackupPath,p.ValidationBackupPath,p.TransactionMarkerPath];
    private static Phase5PublicationTransactionResult Failed(string id, string code, IReadOnlyList<string> errors) => new(false,false,false,false,id,code,"Phase 5 publication did not start.",[],[],errors,null,null,false,true,false,null);
}
