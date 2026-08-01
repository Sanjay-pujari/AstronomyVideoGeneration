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
        Expected = new(source.PublishedPhase4.AggregateId, source.PublishedPhase4.DeterministicChecksum,
            source.PublishedPhase4.LongVariant.DeterministicChecksum, source.PublishedPhase4.ShortVariant.DeterministicChecksum);
        Request = new(Root, CertificationRequest, Candidate, Expected, "Editorial certification", DateTimeOffset.UtcNow);
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
        var result = await Coordinator().PublishAsync(Request);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
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

internal sealed class FaultInjectingPhase5PublicationFileSystem : IPhase5PublicationFileSystem
{
    private readonly IPhase5PublicationFileSystem inner = new Phase5PublicationFileSystem();
    public Func<string, bool>? Fail { get; init; }
    public List<string> Operations { get; } = [];
    private void Hit(string operation) { Operations.Add(operation); if (Fail?.Invoke(operation) == true) throw new IOException($"Injected {operation} failure"); }
    public bool FileExists(string p) => inner.FileExists(p); public bool DirectoryExists(string p) => inner.DirectoryExists(p);
    public void CreateDirectory(string p) { Hit($"CreateDirectory:{p}"); inner.CreateDirectory(p); }
    public async Task<byte[]> ReadAllBytesAsync(string p, CancellationToken t) { Hit($"ReadAllBytes:{p}"); return await inner.ReadAllBytesAsync(p,t); }
    public async Task<string> ReadAllTextAsync(string p, CancellationToken t) { Hit($"ReadAllText:{p}"); return await inner.ReadAllTextAsync(p,t); }
    public async Task WriteAllTextAsync(string p,string c,CancellationToken t) { Hit($"WriteAllText:{p}"); await inner.WriteAllTextAsync(p,c,t); }
    public void CopyFile(string s,string d,bool o) { Hit($"CopyFile:{d}"); inner.CopyFile(s,d,o); }
    public void MoveFile(string s,string d,bool o=false) { Hit($"MoveFile:{d}"); inner.MoveFile(s,d,o); }
    public void MoveDirectory(string s,string d) { Hit($"MoveDirectory:{d}"); inner.MoveDirectory(s,d); }
    public void DeleteFile(string p) { Hit($"DeleteFile:{p}"); inner.DeleteFile(p); }
    public void DeleteDirectory(string p,bool r) { Hit($"DeleteDirectory:{p}"); inner.DeleteDirectory(p,r); }
    public string[] GetFiles(string p,string q) { Hit($"GetFiles:{p}"); return inner.GetFiles(p,q); }
    public long GetFileLength(string p) { Hit($"GetFileLength:{p}"); return inner.GetFileLength(p); }
}

internal sealed class StubPhase5Evaluator(Func<Task<Phase5CommittedStateEvaluation>> evaluate) : IPhase5CommittedAuthorityEvaluator
{
    public int Calls { get; private set; }
    public Task<Phase5CommittedStateEvaluation> EvaluateAsync(string a,string b,string c,string d,string e,Phase5ExpectedPhase4Authority f,CancellationToken g=default) { Calls++; return evaluate(); }
    public static StubPhase5Evaluator Invalid(string code="P5REUSE_CHECKSUM_INVALID") => new(() => Task.FromResult(new Phase5CommittedStateEvaluation(false,code,["readback failed"],[],null)));
}
internal sealed class StubPhase5Recovery(Phase5PublicationRecoveryResult result) : IPhase5PublicationRecoveryService
{
    public int Calls { get; private set; }
    public Task<Phase5PublicationRecoveryResult> RecoverAsync(string a,string b,string c,string d,string e,Phase5ExpectedPhase4Authority f,CancellationToken g=default) { Calls++; return Task.FromResult(result); }
}
