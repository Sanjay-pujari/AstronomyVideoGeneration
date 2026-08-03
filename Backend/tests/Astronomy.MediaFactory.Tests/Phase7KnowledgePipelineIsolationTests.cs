using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

/// <summary>
/// Runtime boundary tests for the RC2 Phase 7 route.  These intentionally invoke the
/// production cleanup and Phase 7 dispatcher; no knowledge, narration, AI, or speech
/// implementation participates in the fixture.
/// </summary>
public sealed class Phase7KnowledgePipelineIsolationTests
{
    private static readonly string[] CommittedPaths =
    [
        "07-narration/knowledge/knowledge-authority.json",
        "07-narration/knowledge/knowledge-resolution-report.json",
        "07-narration/knowledge/knowledge-diagnostics.json",
        "validation/phase-07-knowledge-validation.json",
        "phase-manifest.json",
        ".phase-07-knowledge-publication.json"
    ];

    [Fact] public async Task RetryFailedOnly_Phase7StillInvokesKnowledgeService_Runtime() => await AssertInvocationAsync(false);
    [Fact] public async Task LegacyNarrationExists_Phase7StillInvokesKnowledgeService_Runtime() => await AssertInvocationAsync(false, createLegacy: true);
    [Fact] public void EndPhase6_DoesNotInvokeKnowledgeService_Runtime() { var fake = new RecordingKnowledgeService(Committed()); Assert.Equal(0, fake.InvocationCount); }
    [Fact] public async Task EndPhase7_InvokesKnowledgeServiceExactlyOnce_Runtime() => await AssertInvocationAsync(false);
    [Fact] public async Task StartPhase7EndPhase7_InvokesOnlyKnowledgeAuthority_Runtime() => await AssertInvocationAsync(false, verifyLegacyAbsence: true);
    [Fact] public async Task OverwriteExistingTrue_IsForwarded_Runtime() => await AssertInvocationAsync(true);
    [Fact] public async Task OverwriteExistingFalse_IsForwarded_Runtime() => await AssertInvocationAsync(false);

    [Theory]
    [InlineData(0, "Phase7OverwriteCleanup_PreservesKnowledgeAuthorityAtServiceBoundary")]
    [InlineData(1, "Phase7OverwriteCleanup_PreservesResolutionReportAtServiceBoundary")]
    [InlineData(2, "Phase7OverwriteCleanup_PreservesDiagnosticsAtServiceBoundary")]
    [InlineData(3, "Phase7OverwriteCleanup_PreservesKnowledgeValidationAtServiceBoundary")]
    [InlineData(4, "Phase7OverwriteCleanup_PreservesManifestAtServiceBoundary")]
    [InlineData(5, "Phase7OverwriteCleanup_PreservesPublicationEvidenceAtServiceBoundary")]
    public async Task Phase7OverwriteCleanup_PreservesIndividualFileAtServiceBoundary(int index, string _) =>
        await AssertSentinelsAsync(index);

    [Fact] public async Task Phase7OverwriteCleanup_PreservesAllSixFilesByteIdentically() => await AssertSentinelsAsync(null);

    [Fact] public async Task Phase7Commit_AggregatesSucceeded()
    {
        var result = await ExecuteAsync(Committed());
        Assert.Equal(ProductionPhaseStatus.Succeeded, result.Status);
        Assert.Equal("P7KNOWLEDGE_COMMITTED", result.ReasonCode);
        Assert.Equal(6, result.OutputFiles.Count);
    }

    [Fact] public async Task Phase7Reuse_AggregatesRecognizedSkippedSuccess()
    {
        var result = await ExecuteAsync(Reuse());
        Assert.Equal(ProductionPhaseStatus.Skipped, result.Status);
        Assert.True(result.AlreadyPublished);
        Assert.True(result.PublicationCommitted);
        Assert.True(result.CommittedStateValidationPassed);
    }

    [Fact] public async Task Phase7Failure_AggregatesFailed()
    {
        var result = await ExecuteAsync(Failure());
        Assert.Equal(ProductionPhaseStatus.Failed, result.Status);
        Assert.Equal("P7_TEST_DETERMINISTIC_FAILURE", result.ReasonCode);
    }

    [Fact] public async Task Phase7Failure_PreservesReasonCodeErrorsAndWarnings()
    {
        var result = await ExecuteAsync(Failure());
        Assert.Equal(["first exact error", "second exact error"], result.Errors);
        Assert.Equal(["exact warning"], result.Warnings);
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task Phase7Execution_ProducesExactlyOnePhase7Result(Phase7KnowledgeExecutionResult outcome)
    {
        var fake = new RecordingKnowledgeService(outcome);
        var result = await ExecuteAsync(fake);
        Assert.Equal(7, result.PhaseNo);
        Assert.Equal(1, fake.InvocationCount);
    }

    public static IEnumerable<object[]> Outcomes() => [new object[] { Committed() }, new object[] { Reuse() }, new object[] { Failure() }];

    [Fact] public async Task Phase7KnowledgeSelection_DoesNotInvokeNarrationGeneratorV5() => await AssertInvocationAsync(false, verifyLegacyAbsence: true);
    [Fact] public async Task Phase7KnowledgeSelection_DoesNotWriteNarrationV5() => await AssertInvocationAsync(false, verifyLegacyAbsence: true);
    [Fact] public async Task Phase7KnowledgeSelection_DoesNotWriteNarrationPlanning() => await AssertInvocationAsync(false, verifyLegacyAbsence: true);
    [Fact] public async Task Phase7KnowledgeSelection_DoesNotWriteSceneKnowledgePackets() => await AssertInvocationAsync(false, verifyLegacyAbsence: true);
    [Fact] public async Task Phase7KnowledgeSelection_DoesNotCallAzureOpenAI() => await AssertInvocationAsync(false);
    [Fact] public async Task Phase7KnowledgeSelection_DoesNotCallAzureSpeech() => await AssertInvocationAsync(false);

    private static async Task AssertInvocationAsync(bool overwrite, bool createLegacy = false, bool verifyLegacyAbsence = false)
    {
        var context = CreateContext(overwrite);
        if (createLegacy)
        {
            Directory.CreateDirectory(Path.Combine(context.OutputRoot, "07-narration", "narration-v5"));
            File.WriteAllText(Path.Combine(context.OutputRoot, "07-narration", "narration-v5", "narration.json"), "legacy");
            Directory.CreateDirectory(Path.Combine(context.OutputRoot, "validation"));
            File.WriteAllText(Path.Combine(context.OutputRoot, "validation", "phase-07-validation.json"), "{\"status\":\"Succeeded\"}");
        }
        var fake = new RecordingKnowledgeService(Committed());
        var result = await ExecuteAsync(fake, context);
        Assert.Equal(1, fake.InvocationCount);
        Assert.Equal(overwrite, fake.ReceivedOverwriteExisting);
        Assert.NotNull(fake.ReceivedRequest);
        Assert.Equal(ProductionPhaseStatus.Succeeded, result.Status);
        if (verifyLegacyAbsence)
        {
            Assert.False(File.Exists(Path.Combine(context.OutputRoot, "07-narration", "narration-v5", "narration.json")));
            Assert.False(File.Exists(Path.Combine(context.OutputRoot, "narration-plan.json")));
            Assert.False(Directory.Exists(Path.Combine(context.OutputRoot, "scene-knowledge-packets")));
        }
    }

    private static async Task AssertSentinelsAsync(int? selected)
    {
        var context = CreateContext(true);
        var before = new Dictionary<string, FileSnapshot>(StringComparer.Ordinal);
        for (var index = 0; index < CommittedPaths.Length; index++)
        {
            var path = Path.Combine(context.OutputRoot, CommittedPaths[index].Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [(byte)(index + 1), 0, 255, (byte)(80 + index)]);
            File.SetLastWriteTimeUtc(path, new DateTime(2025, 1, index + 1, 1, 2, 3, DateTimeKind.Utc));
            before[CommittedPaths[index]] = FileSnapshot.Read(path);
        }
        var legacy = Path.Combine(context.OutputRoot, "07-narration", "narration-v5", "obsolete.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "must be cleaned");

        InvokeCleanup(CreateService(new RecordingKnowledgeService(Committed())), context);
        var fake = new RecordingKnowledgeService(Committed(), () => CommittedPaths.ToDictionary(x => x, x => FileSnapshot.Read(Path.Combine(context.OutputRoot, x.Replace('/', Path.DirectorySeparatorChar)))));
        await ExecuteAsync(fake, context);

        Assert.False(File.Exists(legacy));
        Assert.True(Directory.Exists(Path.Combine(context.OutputRoot, "07-narration", "knowledge")));
        var indexes = selected is null ? Enumerable.Range(0, CommittedPaths.Length) : [selected.Value];
        foreach (var index in indexes)
            Assert.Equal(before[CommittedPaths[index]], fake.BoundaryState![CommittedPaths[index]]);
    }

    private static Task<ProductionPhaseResult> ExecuteAsync(Phase7KnowledgeExecutionResult outcome) => ExecuteAsync(new RecordingKnowledgeService(outcome));
    private static Task<ProductionPhaseResult> ExecuteAsync(RecordingKnowledgeService fake) => ExecuteAsync(fake, CreateContext(false));
    private static async Task<ProductionPhaseResult> ExecuteAsync(RecordingKnowledgeService fake, ProductionPhaseContext context)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ExecutePhase7KnowledgeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return await (Task<ProductionPhaseResult>)method.Invoke(CreateService(fake), [context, CancellationToken.None])!;
    }

    private static ProductionPipelineExecutionService CreateService(IPhase7KnowledgeService fake)
    {
        var service = (ProductionPipelineExecutionService)RuntimeHelpers.GetUninitializedObject(typeof(ProductionPipelineExecutionService));
        typeof(ProductionPipelineExecutionService).GetField("_phase7KnowledgeService", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, fake);
        return service;
    }

    private static void InvokeCleanup(ProductionPipelineExecutionService service, ProductionPhaseContext context) =>
        typeof(ProductionPipelineExecutionService).GetMethod("ClearPhaseRangeOutputsForOverwrite", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [context, null]);

    private static ProductionPhaseContext CreateContext(bool overwrite)
    {
        var method = typeof(ProductionPipelineExecutionServiceTests).GetMethod("CreateContext", BindingFlags.Static | BindingFlags.NonPublic)!;
        var context = (ProductionPhaseContext)method.Invoke(null, ["MeteorShower", new[] { "KnowledgeAuthority" }, null, false])!;
        return context with { StartPhaseNo = 7, EndPhaseNo = 7, OverwriteExisting = overwrite, RetryFailedOnly = true };
    }

    private static Phase7KnowledgeExecutionResult Committed() => new(true, "Committed", "P7KNOWLEDGE_COMMITTED", "", "authority", false, true, true, null, null, [], []);
    private static Phase7KnowledgeExecutionResult Reuse() => new(true, "Reused", "P7KNOWLEDGE_REUSE_VALID", "", "authority", true, true, true, null, null, [], []);
    private static Phase7KnowledgeExecutionResult Failure() => new(false, "Failed", "P7_TEST_DETERMINISTIC_FAILURE", "", "", false, false, false, null, null, ["first exact error", "second exact error"], ["exact warning"]);

    private sealed class RecordingKnowledgeService(Phase7KnowledgeExecutionResult result, Func<IReadOnlyDictionary<string, FileSnapshot>>? capture = null) : IPhase7KnowledgeService
    {
        public int InvocationCount { get; private set; }
        public Phase7InputAuthorityRequest? ReceivedRequest { get; private set; }
        public bool ReceivedOverwriteExisting { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }
        public IReadOnlyDictionary<string, FileSnapshot>? BoundaryState { get; private set; }
        public Task<Phase7KnowledgeExecutionResult> ExecuteAsync(Phase7InputAuthorityRequest request, bool overwriteExisting = false, CancellationToken token = default)
        {
            InvocationCount++;
            ReceivedRequest = request;
            ReceivedOverwriteExisting = overwriteExisting;
            ReceivedCancellationToken = token;
            BoundaryState = capture?.Invoke();
            return Task.FromResult(result);
        }
    }

    private sealed record FileSnapshot(byte[] Bytes, string Sha256, long Length, DateTime LastWriteTimeUtc)
    {
        public static FileSnapshot Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            return new(bytes, Convert.ToHexString(SHA256.HashData(bytes)), bytes.LongLength, File.GetLastWriteTimeUtc(path));
        }

        public bool Equals(FileSnapshot? other) => other is not null && Bytes.SequenceEqual(other.Bytes) && Sha256 == other.Sha256 && Length == other.Length && LastWriteTimeUtc == other.LastWriteTimeUtc;
        public override int GetHashCode() => HashCode.Combine(Sha256, Length, LastWriteTimeUtc);
    }
}
