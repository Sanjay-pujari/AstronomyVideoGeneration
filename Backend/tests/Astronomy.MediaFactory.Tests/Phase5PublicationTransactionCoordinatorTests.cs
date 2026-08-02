using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase5PublicationTransactionCoordinatorTests
{
    [Fact]
    public async Task PublishAsync_InvalidCandidate_DoesNotMutateWorkspace()
    {
        using var f = new Phase5PublicationTestFixture();
        var bad = f.Request with { Candidate = f.Candidate with { Certification = f.Candidate.Certification with { Passed = false } } };
        var r = await f.Coordinator().PublishAsync(bad);
        Failed(r, "P5PUB_CANDIDATE_INVALID", false);
        Assert.Empty(Directory.GetFileSystemEntries(f.Root));
    }

    [Fact]
    public async Task PublishAsync_InvalidCandidate_ReturnsCertificationBlockingIssues()
    {
        using var f = new Phase5PublicationTestFixture();
        var certification = f.Candidate.Certification with { BlockingIssues = ["The production certification blocker."] };
        var result = await PublishInvalid(f, f.Candidate with { Certification = certification });

        Assert.Contains("P5_CERTIFICATION_BLOCKING: The production certification blocker.", result.Errors);
    }

    [Fact]
    public async Task PublishAsync_InvalidCoverage_ReturnsVariantAndIssue()
    {
        using var f = new Phase5PublicationTestFixture();
        var original = f.Candidate.Coverage.Variants[0];
        var scene = f.Candidate.SceneIntents.Scenes.First(x => x.Variant == original.Variant);
        var issue = $"Missing {scene.ViewerQuestionId}, {scene.LearningObjectiveId}, and {scene.KnowledgeReferenceIds[0]} in {scene.SceneId}.";
        var invalid = original with { IsValid = false, Issues = [issue] };
        var coverage = f.Candidate.Coverage with { IsValid = false, Variants = [invalid, .. f.Candidate.Coverage.Variants.Skip(1)] };
        coverage = coverage with { SemanticChecksum = Phase5SemanticChecksum.Calculate(coverage with { SemanticChecksum = string.Empty }) };

        var result = await PublishInvalid(f, f.Candidate with { Coverage = coverage });

        Assert.Contains($"P5_COVERAGE_INVALID: variant={original.Variant};sceneId={scene.SceneId};viewerQuestionId={scene.ViewerQuestionId};learningObjectiveId={scene.LearningObjectiveId};knowledgeEntryId={scene.KnowledgeReferenceIds[0]};issue={issue}", result.Errors);
    }

    [Fact]
    public async Task PublishAsync_InvalidTransition_ReturnsVariantSceneAndIssue()
    {
        using var f = new Phase5PublicationTestFixture();
        var original = f.Candidate.Transitions.Variants[0];
        var scene = f.Candidate.SceneIntents.Scenes.First(x => x.Variant == original.Variant);
        var issue = $"Abrupt handoff at {scene.SceneId}.";
        var invalid = original with { IsValid = false, Issues = [issue] };
        var transitions = f.Candidate.Transitions with { IsValid = false, Variants = [invalid, .. f.Candidate.Transitions.Variants.Skip(1)] };
        transitions = transitions with { SemanticChecksum = Phase5SemanticChecksum.Calculate(transitions with { SemanticChecksum = string.Empty }) };

        var result = await PublishInvalid(f, f.Candidate with { Transitions = transitions });

        Assert.Contains($"P5_TRANSITION_INVALID: variant={original.Variant};sceneId={scene.SceneId};issue={issue}", result.Errors);
    }

    [Fact]
    public async Task PublishAsync_InvalidPauseTest_ReturnsVariantSceneAndIssue()
    {
        using var f = new Phase5PublicationTestFixture();
        var original = f.Candidate.PauseTest.Scenes[0];
        var invalid = original with { Passed = false, Issues = ["Scene purpose is unclear."] };
        var pauseTest = f.Candidate.PauseTest with { IsValid = false, FailedSceneCount = 1,
            PassedSceneCount = f.Candidate.PauseTest.Scenes.Count - 1, Scenes = [invalid, .. f.Candidate.PauseTest.Scenes.Skip(1)] };
        pauseTest = pauseTest with { SemanticChecksum = Phase5SemanticChecksum.Calculate(pauseTest with { SemanticChecksum = string.Empty }) };

        var result = await PublishInvalid(f, f.Candidate with { PauseTest = pauseTest });

        Assert.Contains($"P5_PAUSE_TEST_INVALID: variant={original.Variant};sceneId={original.SceneId};issue=Scene purpose is unclear.", result.Errors);
    }

    [Fact]
    public async Task PublishAsync_InvalidEditorialFinding_ReturnsVariantSceneAndMessage()
    {
        using var f = new Phase5PublicationTestFixture();
        var original = f.Candidate.Validation.Variants[0];
        var scene = f.Candidate.SceneIntents.Scenes.First(x => x.Variant == original.Variant);
        var finding = new DocumentaryBlueprintValidationFinding("TEST-P5", DocumentaryBlueprintValidationSeverity.Error,
            "Editorial continuity failed.", "test-blueprint", scene.SceneId);
        var invalid = original with { IsValid = false, EditorialFindings = [.. original.EditorialFindings, finding] };
        var validation = f.Candidate.Validation with { OverallValid = false,
            Variants = [invalid, .. f.Candidate.Validation.Variants.Skip(1)] };

        var result = await PublishInvalid(f, f.Candidate with { Validation = validation });

        Assert.Contains($"P5_EDITORIAL_INVALID: variant={original.Variant};sceneId={scene.SceneId};issue=Editorial continuity failed.", result.Errors);
    }

    [Fact]
    public async Task PublishAsync_InvalidCandidate_DeduplicatesDetailedErrors()
    {
        using var f = new Phase5PublicationTestFixture();
        var original = f.Candidate.Coverage.Variants[0];
        var invalid = original with { IsValid = false, Issues = ["Duplicate issue.", "Duplicate issue."] };
        var coverage = f.Candidate.Coverage with { IsValid = false, Variants = [invalid, .. f.Candidate.Coverage.Variants.Skip(1)] };
        var result = await PublishInvalid(f, f.Candidate with { Coverage = coverage });

        Assert.Single(result.Errors.Where(x => x == $"P5_COVERAGE_INVALID: variant={original.Variant};issue=Duplicate issue."));
    }

    [Fact]
    public async Task PublishAsync_InvalidCandidate_DoesNotPublishPhase5Authority()
    {
        using var f = new Phase5PublicationTestFixture();
        await PublishInvalid(f, InvalidCandidate(f));
        Assert.False(Directory.Exists(Path.Combine(f.Root, "05-editorial")));
        Assert.False(File.Exists(f.Manifest));
    }

    [Fact]
    public async Task PublishAsync_InvalidCandidate_DoesNotWriteStablePhase5Validation()
    {
        using var f = new Phase5PublicationTestFixture();
        await PublishInvalid(f, InvalidCandidate(f));
        Assert.False(File.Exists(f.Validation));
    }

    [Fact]
    public async Task PublishAsync_InvalidCandidate_DoesNotModifyExistingCommittedAuthority()
    {
        using var f = new Phase5PublicationTestFixture();
        SeedPreviousAuthority(f);
        var before = Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(x => Path.GetRelativePath(f.Root, x), File.ReadAllText, StringComparer.Ordinal);

        await PublishInvalid(f, InvalidCandidate(f));

        var after = Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(x => Path.GetRelativePath(f.Root, x), File.ReadAllText, StringComparer.Ordinal);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task PublishAsync_InvokesRecoveryBeforePublication()
    {
        using var f = new Phase5PublicationTestFixture();
        var recovery = new StubPhase5Recovery(new(true, "ok", [], []));
        var r = await f.Coordinator(recovery: recovery).PublishAsync(f.Request);
        Assert.Equal(1, recovery.Calls);
        Assert.True(r.Succeeded);
    }

    [Fact]
    public async Task PublishAsync_RecoveryFailure_StopsBeforePublication()
    {
        using var f = new Phase5PublicationTestFixture();
        var recovery = new StubPhase5Recovery(new(false, "P5REC_INJECTED", [], ["Injected recovery failure."]));
        var evaluator = StubPhase5Evaluator.Invalid();
        var before = SnapshotWorkspace(f.Root);

        var r = await f.Coordinator(evaluator, recovery).PublishAsync(f.Request);

        Assert.Equal(1, recovery.Calls);
        Assert.False(r.Succeeded);
        Assert.Equal("P5REC_INJECTED", r.ReasonCode);
        Assert.Equal(0, evaluator.Calls);
        Assert.Equal(before, SnapshotWorkspace(f.Root));
        AssertNoTransactionResidue(f.Root);
        Assert.False(Directory.Exists(Path.Combine(f.Root, "05-editorial")));
    }

    [Fact]
    public async Task PublishAsync_ValidCandidate_CommitsAllRequiredArtifacts()
    {
        using var f = new Phase5PublicationTestFixture();
        var r = await f.Coordinator().PublishAsync(f.Request);
        Committed(r);
        Assert.Equal(8, Directory.GetFiles(Path.Combine(f.Root, "05-editorial")).Length);
    }

    [Fact] public async Task PublishAsync_WritesCertificationDiagnostics() { using var f = await Published(); Assert.True(File.Exists(f.Editorial("certification-diagnostics.json"))); }
    [Fact] public async Task PublishAsync_PreservesExistingManifestProperties() { using var f = new Phase5PublicationTestFixture(); await File.WriteAllTextAsync(f.Manifest, "{\"custom\":42}"); await f.PublishValidAsync(); Assert.Equal(42, JsonNode.Parse(File.ReadAllText(f.Manifest))!["custom"]!.GetValue<int>()); }
    [Fact] public async Task PublishAsync_PreservesPhase1ToPhase4ManifestEntries() { using var f = new Phase5PublicationTestFixture(); await File.WriteAllTextAsync(f.Manifest, "{\"phase4Artifacts\":[{\"path\":\"04-blueprint/a.json\"}]}"); await f.PublishValidAsync(); Assert.NotNull(JsonNode.Parse(File.ReadAllText(f.Manifest))!["phase4Artifacts"]); }
    [Fact] public async Task PublishAsync_MergesOnlyPhase5Artifacts() { using var f = await Published(); var n = JsonNode.Parse(File.ReadAllText(f.Manifest))!; Assert.Equal(7, n["phase5Artifacts"]!.AsArray().Count); }
    [Fact] public async Task PublishAsync_WritesCommittedValidation() { using var f = await Published(); var n = JsonNode.Parse(File.ReadAllText(f.Validation))!; Assert.True(n["publicationCommitted"]!.GetValue<bool>()); Assert.Equal("Valid", n["validationStatus"]!.GetValue<string>()); }
    [Fact] public async Task PublishAsync_RequiresCommittedReadback() { using var f = new Phase5PublicationTestFixture(); var e = StubPhase5Evaluator.Invalid(); var r = await f.Coordinator(e, SuccessfulRecovery()).PublishAsync(f.Request); Assert.Equal(1, e.Calls); Assert.False(r.CommittedStateValidationPassed); }
    [Fact] public async Task PublishAsync_CommittedReadbackFailure_RollsBackNewPublication() => await ReadbackRestoresAbsence();
    [Fact] public async Task PublishAsync_ReadbackFailure_RestoresPreviousEditorialAuthority() => await ReadbackRestoresPrevious("editorial");
    [Fact] public async Task PublishAsync_ReadbackFailure_RestoresPreviousManifest() => await ReadbackRestoresPrevious("manifest");
    [Fact] public async Task PublishAsync_ReadbackFailure_RestoresPreviousValidation() => await ReadbackRestoresPrevious("validation");
    [Fact] public async Task PublishAsync_ReadbackFailure_WithNoPreviousState_RestoresAbsence() => await ReadbackRestoresAbsence();
    [Fact] public async Task PublishAsync_Success_CleansStagingBackupMarkerAndSnapshots() { using var f = await Published(); AssertNoTransactionResidue(f.Root); }

    [Fact]
    public async Task PublishAsync_StagedArtifactValidationFailure_DoesNotSwapAuthority()
    {
        using var f = new Phase5PublicationTestFixture();
        Directory.CreateDirectory(Path.Combine(f.Root, "05-editorial"));
        File.WriteAllText(f.Editorial("old"), "old");
        var fs = new FaultInjectingPhase5PublicationFileSystem
        {
            ShouldFail = call => call.Operation == Phase5FileSystemOperation.ReadAllBytes
                && Path.GetFileName(call.PrimaryPath) == "blueprint-certification.json"
                && IsTransactionDirectory(call.PrimaryPath, ".05-editorial-staging-")
        };

        var r = await f.Coordinator(fs: fs).PublishAsync(f.Request);

        Assert.False(r.Succeeded);
        Assert.False(r.PublicationCommitted);
        Assert.Equal("old", File.ReadAllText(f.Editorial("old")));
        Assert.False(Directory.EnumerateDirectories(f.Root, ".05-editorial-backup-*", SearchOption.TopDirectoryOnly).Any());
        AssertNoTransactionResidue(f.Root);
    }

    [Fact]
    public async Task PublishAsync_FailureBeforeSwap_CleansUncommittedState()
    {
        using var f = new Phase5PublicationTestFixture();
        var before = SnapshotWorkspace(f.Root);
        var failed = false;
        var fs = new FaultInjectingPhase5PublicationFileSystem
        {
            ShouldFail = call => !failed && (failed = call.Operation == Phase5FileSystemOperation.CreateDirectory
                && IsTransactionDirectory(call.PrimaryPath, ".05-editorial-staging-"))
        };
        var r = await f.Coordinator(fs: fs).PublishAsync(f.Request);
        Assert.False(r.Succeeded);
        Assert.False(r.PublicationCommitted);
        Assert.Equal(before, SnapshotWorkspace(f.Root));
        AssertNoTransactionResidue(f.Root);
    }

    [Fact] public async Task PublishAsync_FailureAfterEditorialSwap_PerformsRollback() => await FailureDuringMetadataPublication("phase-manifest.json");
    [Fact] public async Task PublishAsync_FailureDuringManifestPublication_PerformsRollback() => await FailureDuringMetadataPublication("phase-manifest.json");
    [Fact] public async Task PublishAsync_FailureDuringValidationPublication_PerformsRollback() => await FailureDuringMetadataPublication(Path.Combine("validation", "phase-05-validation.json"));
    [Fact] public async Task PublishAsync_RollbackMoveFailure_ReturnsRollbackFailed() => await RollbackFailure(RollbackFailurePoint.Editorial);
    [Fact] public async Task PublishAsync_RollbackManifestRestoreFailure_ReturnsRollbackFailed() => await RollbackFailure(RollbackFailurePoint.Manifest);
    [Fact] public async Task PublishAsync_RollbackValidationRestoreFailure_ReturnsRollbackFailed() => await RollbackFailure(RollbackFailurePoint.Validation);

    [Fact]
    public async Task PublishAsync_RollbackFailure_WritesPayloadFreeFailureDiagnostics()
    {
        var (f, r) = await RollbackFailureCore(RollbackFailurePoint.Editorial);
        using (f)
        {
            Assert.NotNull(r.FailureDiagnosticsPath);
            Assert.DoesNotContain("sceneLevelOutcomes", File.ReadAllText(r.FailureDiagnosticsPath!));
        }
    }

    [Fact]
    public async Task PublishAsync_RollbackFailure_PreservesOriginalAndRollbackErrors()
    {
        var (f, r) = await RollbackFailureCore(RollbackFailurePoint.Editorial);
        using (f)
        {
            Assert.Equal("P5PUB_ROLLBACK_FAILED", r.ReasonCode);
            Assert.Contains("Injected committed readback failure.", r.Errors);
            Assert.Contains(r.Errors, x => x.Contains("Injected MoveDirectory failure", StringComparison.Ordinal));
            Assert.True(r.RollbackPerformed);
            Assert.False(r.RollbackSucceeded);
            Assert.NotNull(r.FailureDiagnosticsPath);
            var diagnostic = JsonNode.Parse(File.ReadAllText(r.FailureDiagnosticsPath!))!;
            Assert.Contains("Injected committed readback failure.", diagnostic["originalErrors"]!.AsArray().Select(x => x!.GetValue<string>()));
            Assert.Contains(diagnostic["rollbackErrors"]!.AsArray().Select(x => x!.GetValue<string>()), x => x.Contains("Injected MoveDirectory failure", StringComparison.Ordinal));
        }
    }

    [Fact] public async Task PublishAsync_UsesExactTypedTransactionPaths() { using var f = await Published(); Assert.True(Directory.Exists(Path.Combine(f.Root, "05-editorial"))); Assert.False(Directory.Exists(Path.Combine(f.Root, "phase5"))); }
    [Fact] public async Task PublishAsync_DoesNotTouchUnrelatedTransactionLikeDirectories() { using var f = new Phase5PublicationTestFixture(); var unrelated = Path.Combine(f.Root, ".05-editorial-staging-unrelated"); Directory.CreateDirectory(unrelated); File.WriteAllText(Path.Combine(unrelated, "keep"), "x"); await f.PublishValidAsync(); Assert.True(File.Exists(Path.Combine(unrelated, "keep"))); }

    [Fact]
    public async Task PublishAsync_CancellationBeforeMutation_LeavesWorkspaceUnchanged()
    {
        using var f = new Phase5PublicationTestFixture();
        var before = SnapshotWorkspace(f.Root);
        using var c = new CancellationTokenSource();
        c.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Coordinator().PublishAsync(f.Request, c.Token));
        Assert.Equal(before, SnapshotWorkspace(f.Root));
        AssertNoTransactionResidue(f.Root);
    }

    [Fact]
    public async Task PublishAsync_CancellationAfterMutation_UsesNonInterruptibleRollback()
    {
        using var f = new Phase5PublicationTestFixture();
        var fs = FailAtomicReplacement(f.Manifest);
        var r = await f.Coordinator(fs: fs).PublishAsync(f.Request);
        Assert.True(r.RollbackPerformed);
    }

    private static async Task<Phase5PublicationTestFixture> Published() { var f = new Phase5PublicationTestFixture(); await f.PublishValidAsync(); return f; }
    private static DocumentaryBlueprintCertificationIntegrationResult InvalidCandidate(Phase5PublicationTestFixture f) =>
        f.Candidate with { Certification = f.Candidate.Certification with { Passed = false } };
    private static async Task<Phase5PublicationTransactionResult> PublishInvalid(Phase5PublicationTestFixture f,
        DocumentaryBlueprintCertificationIntegrationResult candidate)
    {
        var result = await f.Coordinator().PublishAsync(f.Request with { Candidate = candidate });
        Failed(result, "P5PUB_CANDIDATE_INVALID", false);
        return result;
    }
    private static StubPhase5Recovery SuccessfulRecovery() => new(new(true, "ok", [], []));
    private static void Committed(Phase5PublicationTransactionResult r) { Assert.True(r.Succeeded); Assert.True(r.PublicationCommitted); Assert.True(r.CommittedStateValidationPassed); Assert.Equal("P5PUB_COMMITTED", r.ReasonCode); Assert.False(r.RollbackPerformed); Assert.True(r.RollbackSucceeded); Assert.Null(r.FailureDiagnosticsPath); }
    private static void Failed(Phase5PublicationTransactionResult r, string code, bool rollback) { Assert.False(r.Succeeded); Assert.False(r.PublicationCommitted); Assert.False(r.CommittedStateValidationPassed); Assert.Equal(code, r.ReasonCode); Assert.Equal(rollback, r.RollbackPerformed); }

    private static async Task ReadbackRestoresAbsence()
    {
        using var f = new Phase5PublicationTestFixture();
        var r = await f.Coordinator(StubPhase5Evaluator.Invalid(), SuccessfulRecovery()).PublishAsync(f.Request);
        Failed(r, "P5REUSE_CHECKSUM_INVALID", true);
        Assert.True(r.RollbackSucceeded);
        Assert.False(r.PreviousAuthorityRestored);
        Assert.False(Directory.Exists(Path.Combine(f.Root, "05-editorial")));
        Assert.False(File.Exists(f.Manifest));
        Assert.False(File.Exists(f.Validation));
    }

    private static async Task ReadbackRestoresPrevious(string item)
    {
        using var f = new Phase5PublicationTestFixture();
        SeedPreviousAuthority(f);
        var r = await f.Coordinator(StubPhase5Evaluator.Invalid(), SuccessfulRecovery()).PublishAsync(f.Request);
        Assert.True(r.RollbackPerformed);
        Assert.True(r.RollbackSucceeded);
        Assert.True(r.PreviousAuthorityRestored);
        if (item == "editorial") Assert.Equal("old", File.ReadAllText(f.Editorial("old")));
        if (item == "manifest") Assert.Equal("{}", File.ReadAllText(f.Manifest));
        if (item == "validation") Assert.Equal("old validation", File.ReadAllText(f.Validation));
    }

    private static async Task FailureDuringMetadataPublication(string relativeDestination)
    {
        using var f = new Phase5PublicationTestFixture();
        var destination = Path.Combine(f.Root, relativeDestination);
        var fs = FailAtomicReplacement(destination);
        var r = await f.Coordinator(fs: fs).PublishAsync(f.Request);
        Assert.False(r.Succeeded);
        Assert.False(r.PublicationCommitted);
        Assert.True(r.RollbackPerformed);
        Assert.True(r.RollbackSucceeded);
        AssertNoTransactionResidue(f.Root);
    }

    private static FaultInjectingPhase5PublicationFileSystem FailAtomicReplacement(string destination)
    {
        var failed = false;
        return new()
        {
            ShouldFail = call => !failed && (failed = call.Operation == Phase5FileSystemOperation.MoveFile
                && Path.GetFullPath(call.SecondaryPath!) == Path.GetFullPath(destination))
        };
    }

    private static async Task RollbackFailure(RollbackFailurePoint point)
    {
        var (f, r) = await RollbackFailureCore(point);
        using (f)
        {
            Assert.Equal("P5PUB_ROLLBACK_FAILED", r.ReasonCode);
            Assert.True(r.RollbackPerformed);
            Assert.False(r.RollbackSucceeded);
            Assert.NotNull(r.FailureDiagnosticsPath);
        }
    }

    private static async Task<(Phase5PublicationTestFixture, Phase5PublicationTransactionResult)> RollbackFailureCore(RollbackFailurePoint point)
    {
        var f = new Phase5PublicationTestFixture();
        SeedPreviousAuthority(f);
        var failed = false;
        var fs = new FaultInjectingPhase5PublicationFileSystem
        {
            ShouldFail = call => !failed && (failed = IsExactRollbackCall(call, f.Root, point))
        };
        var evaluator = StubPhase5Evaluator.Invalid(error: "Injected committed readback failure.");
        var r = await f.Coordinator(evaluator, SuccessfulRecovery(), fs).PublishAsync(f.Request);
        return (f, r);
    }

    private static bool IsExactRollbackCall(Phase5FileSystemCall call, string root, RollbackFailurePoint point)
    {
        var editorial = Path.Combine(root, "05-editorial");
        return point switch
        {
            RollbackFailurePoint.Editorial => call.Operation == Phase5FileSystemOperation.MoveDirectory
                && IsTransactionPath(call.PrimaryPath, root, ".05-editorial-backup-")
                && Path.GetFullPath(call.SecondaryPath!) == Path.GetFullPath(editorial),
            RollbackFailurePoint.Manifest => call.Operation == Phase5FileSystemOperation.MoveFile
                && IsTransactionPath(call.PrimaryPath, root, ".phase-05-manifest-backup-")
                && Path.GetFullPath(call.SecondaryPath!) == Path.GetFullPath(Path.Combine(root, "phase-manifest.json")),
            RollbackFailurePoint.Validation => call.Operation == Phase5FileSystemOperation.MoveFile
                && IsTransactionPath(call.PrimaryPath, root, ".phase-05-validation-backup-")
                && Path.GetFullPath(call.SecondaryPath!) == Path.GetFullPath(Path.Combine(root, "validation", "phase-05-validation.json")),
            _ => false
        };
    }

    private static void SeedPreviousAuthority(Phase5PublicationTestFixture f)
    {
        Directory.CreateDirectory(Path.Combine(f.Root, "05-editorial"));
        File.WriteAllText(f.Editorial("old"), "old");
        File.WriteAllText(f.Manifest, "{}");
        Directory.CreateDirectory(Path.GetDirectoryName(f.Validation)!);
        File.WriteAllText(f.Validation, "old validation");
    }

    private static IReadOnlyList<string> SnapshotWorkspace(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private static void AssertNoTransactionResidue(string root)
    {
        var names = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).ToArray();
        Assert.DoesNotContain(names, name => name!.StartsWith(".05-editorial-staging-", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name!.StartsWith(".05-editorial-backup-", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name!.StartsWith(".phase-05-transaction-", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name!.StartsWith(".phase-05-manifest-backup-", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name!.StartsWith(".phase-05-validation-backup-", StringComparison.Ordinal));
    }

    private static bool IsTransactionDirectory(string path, string prefix) =>
        Path.GetFileName(Path.GetDirectoryName(path))?.StartsWith(prefix, StringComparison.Ordinal) == true
        || Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal);

    private static bool IsTransactionPath(string path, string root, string prefix) =>
        Path.GetDirectoryName(Path.GetFullPath(path)) == Path.GetFullPath(root)
        && Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal);

    private enum RollbackFailurePoint { Editorial, Manifest, Validation }
}
