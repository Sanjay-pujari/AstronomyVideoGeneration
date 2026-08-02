using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

internal sealed class Phase5PublicationTestFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "phase5-publication-tests", Guid.NewGuid().ToString("N"));
    public DocumentaryBlueprintCertificationRequest CertificationRequest { get; }
    public DocumentaryBlueprintCertificationIntegrationResult Candidate { get; }
    public Phase5ExpectedPhase4Authority Expected { get; }
    public Phase5PublicationTransactionRequest Request { get; }
    public IPhase5PublicationFileSystem FileSystem { get; } = new Phase5PublicationFileSystem();

    public Phase5PublicationTestFixture()
    {
        Directory.CreateDirectory(Root);
        var source = Phase5CertificationFixture.Create();
        CertificationRequest = source.Request;
        Candidate = source.Result;
        // The adapter is the single projection boundary between the published Phase 4
        // aggregate and Phase 5.  In particular, do not build a second, equivalent-
        // looking Long/Short projection here: the certification service certifies the
        // adapter projections and the committed evaluator must be given those checksums.
        Expected = new(source.PublishedPhase4.AggregateId, source.PublishedPhase4.DeterministicChecksum,
            CertificationRequest.Long.Metadata.Checksum, CertificationRequest.Short.Metadata.Checksum);
        Request = new(Root, CertificationRequest, Candidate, Expected, "Editorial certification", DateTimeOffset.UtcNow);
        ValidateFixtureLineage();
    }

    public Phase5PublicationTransactionCoordinator Coordinator(IPhase5CommittedAuthorityEvaluator? evaluator = null,
        IPhase5PublicationRecoveryService? recovery = null, IPhase5PublicationFileSystem? fs = null)
    {
        evaluator ??= new Phase5CommittedAuthorityEvaluator();
        recovery ??= new Phase5PublicationRecoveryService(evaluator, fs ?? FileSystem);
        return new(evaluator, recovery, fs ?? FileSystem);
    }

    public async Task PublishValidAsync()
    {
        ValidateFixtureLineage();
        var result = await Coordinator().PublishAsync(Request);
        Assert.True(result.Succeeded, $"""
            ReasonCode: {result.ReasonCode}
            Reason: {result.Reason}
            Errors: {string.Join("; ", result.Errors)}
            Expected Phase 4 aggregate checksum: {Request.ExpectedPhase4.AggregateChecksum}
            Expected Long checksum: {Request.ExpectedPhase4.LongChecksum}
            Expected Short checksum: {Request.ExpectedPhase4.ShortChecksum}
            Candidate aggregate checksum: {Candidate.Certification.SourcePhase4Checksum}
            Candidate Long checksum: {Candidate.Certification.SourceLongBlueprintChecksum}
            Candidate Short checksum: {Candidate.Certification.SourceShortBlueprintChecksum}
            """);
    }

    public void ValidateFixtureLineage()
    {
        Assert.Equal(Request.ExpectedPhase4.AggregateChecksum, Candidate.Certification.SourcePhase4Checksum);
        Assert.Equal(Request.ExpectedPhase4.LongChecksum, Candidate.Certification.SourceLongBlueprintChecksum);
        Assert.Equal(Request.ExpectedPhase4.ShortChecksum, Candidate.Certification.SourceShortBlueprintChecksum);
        Assert.Equal(Request.ExpectedPhase4.AggregateChecksum, Candidate.EditorialContract.SourcePhase4Checksum);
        Assert.Equal(Request.ExpectedPhase4.AggregateChecksum, Candidate.Diagnostics.SourcePhase4Checksum);

        var projections = new[]
        {
            (Candidate.Validation.SourceAggregateChecksum, Candidate.Validation.SourceLongChecksum, Candidate.Validation.SourceShortChecksum),
            (Candidate.SceneIntents.SourceAggregateChecksum, Candidate.SceneIntents.SourceLongChecksum, Candidate.SceneIntents.SourceShortChecksum),
            (Candidate.Coverage.SourceAggregateChecksum, Candidate.Coverage.SourceLongChecksum, Candidate.Coverage.SourceShortChecksum),
            (Candidate.Transitions.SourceAggregateChecksum, Candidate.Transitions.SourceLongChecksum, Candidate.Transitions.SourceShortChecksum),
            (Candidate.PauseTest.SourceAggregateChecksum, Candidate.PauseTest.SourceLongChecksum, Candidate.PauseTest.SourceShortChecksum)
        };
        Assert.All(projections, lineage =>
        {
            Assert.Equal(Request.ExpectedPhase4.AggregateChecksum, lineage.SourceAggregateChecksum);
            Assert.Equal(Request.ExpectedPhase4.LongChecksum, lineage.SourceLongChecksum);
            Assert.Equal(Request.ExpectedPhase4.ShortChecksum, lineage.SourceShortChecksum);
        });
    }

    public Task<Phase5CommittedStateEvaluation> EvaluateAsync(Phase5ExpectedPhase4Authority? expected = null,
        string? execution = null, string? plan = null, string? eventId = null, string? language = null) =>
        new Phase5CommittedAuthorityEvaluator().EvaluateAsync(Root, execution ?? CertificationRequest.ExecutionId,
            plan ?? CertificationRequest.PlanId, eventId ?? CertificationRequest.EventId,
            language ?? CertificationRequest.Language, expected ?? Expected);

    public string Editorial(string file) => Path.Combine(Root, "05-editorial", file);
    public string Manifest => Path.Combine(Root, "phase-manifest.json");
    public string Validation => Path.Combine(Root, "validation", "phase-05-validation.json");
    public Phase5PublicationTransactionMarker WriteMarker(string id, Phase5PublicationTransactionStatus status,
        bool editorial = false, bool manifest = false, bool validation = false)
    {
        var paths = Phase5PublicationTransactionPaths.Create(Root, id);
        var marker = new Phase5PublicationTransactionMarker(id, status, paths, editorial, manifest, validation,
            CertificationRequest.ExecutionId, CertificationRequest.PlanId, CertificationRequest.EventId,
            CertificationRequest.Language, Expected, DateTimeOffset.UtcNow);
        File.WriteAllText(paths.TransactionMarkerPath, JsonSerializer.Serialize(marker, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return marker;
    }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}

internal enum Phase5FileSystemOperation
{
    CreateDirectory, ReadAllBytes, ReadAllText, WriteAllText, CopyFile, MoveFile,
    MoveDirectory, DeleteFile, DeleteDirectory, GetFiles, GetFileLength
}

internal sealed record Phase5FileSystemCall(Phase5FileSystemOperation Operation,
    string PrimaryPath, string? SecondaryPath, int Sequence);

internal sealed class FaultInjectingPhase5PublicationFileSystem : IPhase5PublicationFileSystem
{
    private readonly IPhase5PublicationFileSystem inner = new Phase5PublicationFileSystem();
    public Func<Phase5FileSystemCall, bool>? ShouldFail { get; init; }
    public List<Phase5FileSystemCall> Calls { get; } = [];
    private void Hit(Phase5FileSystemOperation operation, string primary, string? secondary = null)
    {
        var call = new Phase5FileSystemCall(operation, primary, secondary, Calls.Count + 1);
        Calls.Add(call);
        if (ShouldFail?.Invoke(call) == true)
            throw new IOException($"Injected {operation} failure for '{primary}'" + (secondary is null ? "." : $" -> '{secondary}'."));
    }
    public bool FileExists(string p) => inner.FileExists(p); public bool DirectoryExists(string p) => inner.DirectoryExists(p);
    public void CreateDirectory(string p) { Hit(Phase5FileSystemOperation.CreateDirectory,p); inner.CreateDirectory(p); }
    public async Task<byte[]> ReadAllBytesAsync(string p, CancellationToken t) { Hit(Phase5FileSystemOperation.ReadAllBytes,p); return await inner.ReadAllBytesAsync(p,t); }
    public async Task<string> ReadAllTextAsync(string p, CancellationToken t) { Hit(Phase5FileSystemOperation.ReadAllText,p); return await inner.ReadAllTextAsync(p,t); }
    public async Task WriteAllTextAsync(string p,string c,CancellationToken t) { Hit(Phase5FileSystemOperation.WriteAllText,p); await inner.WriteAllTextAsync(p,c,t); }
    public void CopyFile(string s,string d,bool o) { Hit(Phase5FileSystemOperation.CopyFile,s,d); inner.CopyFile(s,d,o); }
    public void MoveFile(string s,string d,bool o=false) { Hit(Phase5FileSystemOperation.MoveFile,s,d); inner.MoveFile(s,d,o); }
    public void MoveDirectory(string s,string d) { Hit(Phase5FileSystemOperation.MoveDirectory,s,d); inner.MoveDirectory(s,d); }
    public void DeleteFile(string p) { Hit(Phase5FileSystemOperation.DeleteFile,p); inner.DeleteFile(p); }
    public void DeleteDirectory(string p,bool r) { Hit(Phase5FileSystemOperation.DeleteDirectory,p); inner.DeleteDirectory(p,r); }
    public string[] GetFiles(string p,string q) { Hit(Phase5FileSystemOperation.GetFiles,p,q); return inner.GetFiles(p,q); }
    public long GetFileLength(string p) { Hit(Phase5FileSystemOperation.GetFileLength,p); return inner.GetFileLength(p); }
}

internal sealed class StubPhase5Evaluator(Func<Task<Phase5CommittedStateEvaluation>> evaluate) : IPhase5CommittedAuthorityEvaluator
{
    public int Calls { get; private set; }
    public Task<Phase5CommittedStateEvaluation> EvaluateAsync(string a,string b,string c,string d,string e,Phase5ExpectedPhase4Authority f,CancellationToken g=default) { Calls++; return evaluate(); }
    public static StubPhase5Evaluator Invalid(string code="P5REUSE_CHECKSUM_INVALID", string error="readback failed") =>
        new(() => Task.FromResult(new Phase5CommittedStateEvaluation(false,code,[error],[],null)));
}
internal sealed class StubPhase5Recovery(Phase5PublicationRecoveryResult result) : IPhase5PublicationRecoveryService
{
    public int Calls { get; private set; }
    public Task<Phase5PublicationRecoveryResult> RecoverAsync(string a,string b,string c,string d,string e,Phase5ExpectedPhase4Authority f,CancellationToken g=default) { Calls++; return Task.FromResult(result); }
}
