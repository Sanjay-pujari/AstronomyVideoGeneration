using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

internal static class NarrationPlanningPublicationJson
{
    internal static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web){WriteIndented=true};
    internal static byte[] Bytes<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,Options);
    internal static string Hash(object value)=>Phase7Determinism.Hash(value);
    internal static string Sha(byte[] value)=>Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    internal static string Full(string root,string relative)=>Path.Combine(Path.GetFullPath(root),relative.Replace('/',Path.DirectorySeparatorChar));
}
public static class NarrationPlanningPublicationCanonicalizer
{
    private static SortedDictionary<string,string> Sorted(IReadOnlyDictionary<string,string> x)
    { var result=new SortedDictionary<string,string>(StringComparer.Ordinal);foreach(var pair in x)result[pair.Key]=pair.Value;return result; }
    private static NarrationPlanningArtifact[] Inventory(IReadOnlyList<NarrationPlanningArtifact> x)
        => x.OrderBy(a=>a.RelativePath,StringComparer.Ordinal).ToArray();
    public static string ComputeDiagnosticsArtifactChecksum(NarrationPlanningDiagnosticsArtifact x)
        => Phase7Determinism.Hash(x with { DeterministicChecksum="" });
    public static string ComputeReportChecksum(NarrationPlanningPublicationReport x)
        => Phase7Determinism.Hash(x with { DeterministicChecksum="" });
    public static string ComputePhysicalValidationChecksum(NarrationPlanningPhysicalValidation x)
        => Phase7Determinism.Hash(x with { ArtifactInventory=Inventory(x.ArtifactInventory),LineageEvidence=Sorted(x.LineageEvidence),
            Errors=x.Errors.Order(StringComparer.Ordinal).ToArray(),Warnings=x.Warnings.Order(StringComparer.Ordinal).ToArray(),DeterministicChecksum="" });
    public static string ComputeManifestEntryChecksum(NarrationPlanningManifestEntry x)
        => Phase7Determinism.Hash(x with { CommittedAtUtc=default,ArtifactInventory=Inventory(x.ArtifactInventory),DeterministicChecksum="" });
    public static string ComputePublicationEvidenceChecksum(NarrationPlanningPublicationEvidence x)
        => Phase7Determinism.Hash(x with { PublishedAtUtc=default,DeterministicChecksum="" });
    /// <summary>The inventory projection sorts paths and excludes evidence's physical hash to avoid checksum self-reference.</summary>
    public static string ComputeArtifactInventoryChecksum(IReadOnlyList<NarrationPlanningArtifact> x)
        => Phase7Determinism.Hash(Inventory(x).Select(a=>a.RelativePath==NarrationPlanningArtifactPaths.PublicationEvidence
            ? a with { PhysicalSha256="",SizeBytes=0,SemanticChecksum="" } : a).ToArray());
}
public sealed class Phase7NarrationPlanningClock: IPhase7NarrationPlanningClock { public DateTimeOffset UtcNow=>DateTimeOffset.UtcNow; }
public sealed class Phase7NarrationPlanningPublicationFaultInjector:IPhase7NarrationPlanningPublicationFaultInjector
{ public void Inject(NarrationPlanningPublicationFaultPoint point) { } }
public sealed class Phase7NarrationPlanningFileSystem:IPhase7NarrationPlanningFileSystem
{
    public bool FileExists(string p)=>File.Exists(p); public bool DirectoryExists(string p)=>Directory.Exists(p);
    public void CreateDirectory(string p)=>Directory.CreateDirectory(p);
    public Task<byte[]> ReadAsync(string p,CancellationToken t=default)=>File.ReadAllBytesAsync(p,t);
    public async Task WriteAsync(string p,byte[] b,CancellationToken t=default){Directory.CreateDirectory(Path.GetDirectoryName(p)!);await File.WriteAllBytesAsync(p,b,t);}
    public void MoveFile(string s,string d,bool o){Directory.CreateDirectory(Path.GetDirectoryName(d)!);File.Move(s,d,o);}
    public void MoveDirectory(string s,string d){Directory.CreateDirectory(Path.GetDirectoryName(d)!);Directory.Move(s,d);}
    public void DeleteFile(string p)=>File.Delete(p); public void DeleteDirectory(string p){if(Directory.Exists(p))Directory.Delete(p,true);}
    public IReadOnlyList<string> Directories(string r,string p)=>Directory.Exists(r)?Directory.GetDirectories(r,p).Order(StringComparer.Ordinal).ToArray():[];
}
public sealed class Phase7NarrationPlanningExecutionLock(IPhase7NarrationPlanningFileSystem fs):IPhase7NarrationPlanningExecutionLock
{
    private static readonly ConcurrentDictionary<string,SemaphoreSlim> Gates=new(StringComparer.Ordinal);
    public async Task<IAsyncDisposable?> TryAcquireAsync(string root,string plan,CancellationToken token=default)
    {
        var key=Path.GetFullPath(root)+"|"+plan;var gate=Gates.GetOrAdd(key,_=>new(1,1));
        if(!await gate.WaitAsync(0,token))return null;var path=Path.Combine(Path.GetFullPath(root),plan+".phase-07-narration-planning.lock");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.Asynchronous);
            stream.SetLength(0);await JsonSerializer.SerializeAsync(stream,new { ProcessId=Environment.ProcessId,PlanId=plan,AcquiredAtUtc=DateTimeOffset.UtcNow },cancellationToken:token);
            await stream.FlushAsync(token);return new Lease(gate,stream,path);
        }
        catch(IOException){gate.Release();return null;}catch{gate.Release();throw;}
    }
    private sealed class Lease(SemaphoreSlim gate,FileStream stream,string path):IAsyncDisposable
    { public async ValueTask DisposeAsync(){await stream.DisposeAsync();try{File.Delete(path);}finally{gate.Release();}} }
}
public sealed class Phase7NarrationPlanningRecoveryService(IPhase7NarrationPlanningFileSystem fs):IPhase7NarrationPlanningRecoveryService
{
    public Task RecoverAsync(string root,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var narration=Path.Combine(Path.GetFullPath(root),"07-narration");
        foreach(var backup in fs.Directories(narration,".planning-backup-*"))
        {
            bool Stable(string p)=>fs.FileExists(NarrationPlanningPublicationJson.Full(root,p));
            bool Saved(string p)=>fs.FileExists(Path.Combine(backup,p.Replace('/',Path.DirectorySeparatorChar)));
            var stableValid=NarrationPlanningArtifactPaths.Governed.All(Stable);
            var backupValid=fs.DirectoryExists(Path.Combine(backup,"planning"))&&new[]{NarrationPlanningArtifactPaths.Validation,NarrationPlanningArtifactPaths.Manifest,NarrationPlanningArtifactPaths.PublicationEvidence}.All(Saved);
            if(stableValid){fs.DeleteDirectory(backup);continue;}
            if(!backupValid)throw new IOException("NARRATION_PLANNING_RECOVERY_FAILED: neither stable nor backup is complete; diagnostic state retained.");
            var stable=Path.Combine(narration,"planning");if(fs.DirectoryExists(stable))fs.DeleteDirectory(stable);
            fs.MoveDirectory(Path.Combine(backup,"planning"),stable);
            foreach(var p in new[]{NarrationPlanningArtifactPaths.Validation,NarrationPlanningArtifactPaths.Manifest,NarrationPlanningArtifactPaths.PublicationEvidence})
            {var live=NarrationPlanningPublicationJson.Full(root,p);if(fs.FileExists(live))fs.DeleteFile(live);fs.MoveFile(Path.Combine(backup,p.Replace('/',Path.DirectorySeparatorChar)),live,true);}
            fs.DeleteDirectory(backup);
        }
        foreach(var stage in fs.Directories(narration,".planning-staging-*"))fs.DeleteDirectory(stage);
        return Task.CompletedTask;
    }
}
public sealed class Phase7NarrationPlanningPhysicalReadback(IPhase7NarrationPlanningFileSystem fs):IPhase7NarrationPlanningPhysicalReadback
{
    private static readonly JsonSerializerOptions J=NarrationPlanningPublicationJson.Options;
    public async Task<NarrationPlanningPhysicalResult> ReadCommittedAsync(string root,CancellationToken token=default)
    {
        var errors=new List<string>();var artifacts=new List<NarrationPlanningArtifact>();
        foreach(var relative in NarrationPlanningArtifactPaths.Governed)
        {
            var path=NarrationPlanningPublicationJson.Full(root,relative);if(!fs.FileExists(path)){errors.Add("NARRATION_PLANNING_COMMITTED_ARTIFACT_MISSING:"+relative);continue;}
            var bytes=await fs.ReadAsync(path,token);try
            {
                object? value=relative switch
                {
                    NarrationPlanningArtifactPaths.Authority=>JsonSerializer.Deserialize<NarrationPlanningAuthority>(bytes,J),
                    NarrationPlanningArtifactPaths.Diagnostics=>JsonSerializer.Deserialize<NarrationPlanningDiagnosticsArtifact>(bytes,J),
                    NarrationPlanningArtifactPaths.Report=>JsonSerializer.Deserialize<NarrationPlanningPublicationReport>(bytes,J),
                    NarrationPlanningArtifactPaths.Validation=>JsonSerializer.Deserialize<NarrationPlanningPhysicalValidation>(bytes,J),
                    NarrationPlanningArtifactPaths.PublicationEvidence=>JsonSerializer.Deserialize<NarrationPlanningPublicationEvidence>(bytes,J),
                    _=>JsonNode.Parse(bytes)
                }; if(value is null)throw new JsonException("Empty artifact");
                var semantic=value switch { NarrationPlanningAuthority x=>x.DeterministicChecksum,NarrationPlanningDiagnosticsArtifact x=>x.DeterministicChecksum,
                    NarrationPlanningPublicationReport x=>x.DeterministicChecksum,NarrationPlanningPhysicalValidation x=>x.DeterministicChecksum,
                    NarrationPlanningPublicationEvidence x=>x.DeterministicChecksum,_=>NarrationPlanningPublicationJson.Sha(bytes)};
                artifacts.Add(new(relative,NarrationPlanningPublicationJson.Sha(bytes),bytes.LongLength,semantic));
            }catch(Exception ex) when(ex is JsonException or NotSupportedException){errors.Add("NARRATION_PLANNING_COMMITTED_CHECKSUM_INVALID:"+relative+":"+ex.Message);}
        }
        return new(errors.Count==0,errors.Count==0?NarrationPlanningPublicationReasonCodes.ReuseValid:NarrationPlanningPublicationReasonCodes.PhysicalReadbackInvalid,artifacts,errors);
    }
}
public sealed class Phase7NarrationPlanningCandidateReadback(IPhase7NarrationPlanningFileSystem fs):IPhase7NarrationPlanningCandidateReadback
{
    private static readonly JsonSerializerOptions J=NarrationPlanningPublicationJson.Options;
    public async Task<NarrationPlanningCandidateReadbackResult> ReadAsync(string root,CancellationToken token=default)
    {
        var errors=new List<string>();var artifacts=new List<NarrationPlanningArtifact>();
        foreach(var relative in NarrationPlanningArtifactPaths.Governed)
        {
            var path=Path.Combine(Path.GetFullPath(root),relative.Replace('/',Path.DirectorySeparatorChar));
            if(!fs.FileExists(path)){errors.Add("NARRATION_PLANNING_CANDIDATE_ARTIFACT_MISSING:"+relative);continue;}
            var bytes=await fs.ReadAsync(path,token);
            try
            {
                object value=relative switch {
                    NarrationPlanningArtifactPaths.Authority=>JsonSerializer.Deserialize<NarrationPlanningAuthority>(bytes,J)!,
                    NarrationPlanningArtifactPaths.Diagnostics=>JsonSerializer.Deserialize<NarrationPlanningDiagnosticsArtifact>(bytes,J)!,
                    NarrationPlanningArtifactPaths.Report=>JsonSerializer.Deserialize<NarrationPlanningPublicationReport>(bytes,J)!,
                    NarrationPlanningArtifactPaths.Validation=>JsonSerializer.Deserialize<NarrationPlanningPhysicalValidation>(bytes,J)!,
                    NarrationPlanningArtifactPaths.PublicationEvidence=>JsonSerializer.Deserialize<NarrationPlanningPublicationEvidence>(bytes,J)!,
                    _=>JsonNode.Parse(bytes)! };
                if(value is null)throw new JsonException("Empty candidate artifact.");
                var checksumValid=value switch {
                    NarrationPlanningAuthority x=>x.DeterministicChecksum==NarrationPlanningCanonicalizer.AuthorityChecksum(x),
                    NarrationPlanningDiagnosticsArtifact x=>x.Diagnostics.DeterministicChecksum==NarrationPlanningCanonicalizer.DiagnosticsChecksum(x.Diagnostics)&&x.DeterministicChecksum==NarrationPlanningPublicationCanonicalizer.ComputeDiagnosticsArtifactChecksum(x),
                    NarrationPlanningPublicationReport x=>x.DeterministicChecksum==NarrationPlanningPublicationCanonicalizer.ComputeReportChecksum(x),
                    NarrationPlanningPhysicalValidation x=>x.DeterministicChecksum==NarrationPlanningPublicationCanonicalizer.ComputePhysicalValidationChecksum(x),
                    NarrationPlanningPublicationEvidence x=>x.DeterministicChecksum==NarrationPlanningPublicationCanonicalizer.ComputePublicationEvidenceChecksum(x),
                    _=>true };
                if(!checksumValid)errors.Add("NARRATION_PLANNING_CANDIDATE_CHECKSUM_INVALID:"+relative);
                string semantic=value switch { NarrationPlanningAuthority x=>x.DeterministicChecksum,
                    NarrationPlanningDiagnosticsArtifact x=>x.DeterministicChecksum,NarrationPlanningPublicationReport x=>x.DeterministicChecksum,
                    NarrationPlanningPhysicalValidation x=>x.DeterministicChecksum,NarrationPlanningPublicationEvidence x=>x.DeterministicChecksum,
                    _=>NarrationPlanningPublicationJson.Sha(bytes)};
                artifacts.Add(new(relative,NarrationPlanningPublicationJson.Sha(bytes),bytes.LongLength,semantic));
            }catch(Exception ex) when(ex is JsonException or NotSupportedException){errors.Add("NARRATION_PLANNING_CANDIDATE_INVALID:"+relative+":"+ex.Message);}
        }
        var actual=Directory.Exists(root)?Directory.GetFiles(root,"*",SearchOption.AllDirectories).Select(p=>Path.GetRelativePath(root,p).Replace('\\','/')).ToHashSet(StringComparer.Ordinal):[];
        foreach(var extra in actual.Except(NarrationPlanningArtifactPaths.Governed,StringComparer.Ordinal))errors.Add("NARRATION_PLANNING_CANDIDATE_RESIDUE:"+extra);
        return new(errors.Count==0,errors.Count==0?"NARRATION_PLANNING_CANDIDATE_VALID":NarrationPlanningPublicationReasonCodes.PhysicalReadbackInvalid,artifacts,errors,[]);
    }
}
public sealed class Phase7NarrationPlanningArtifactReconciler:IPhase7NarrationPlanningArtifactReconciler
{
    public NarrationPlanningArtifactReconciliationResult Reconcile(NarrationPlanningAuthority a,NarrationPlanningDiagnosticsArtifact d,
        NarrationPlanningPublicationReport r,NarrationPlanningPhysicalValidation v,NarrationPlanningManifestEntry m,
        NarrationPlanningPublicationEvidence e,IReadOnlyList<NarrationPlanningArtifact> inventory,Phase7NarrationPlanningInputAuthorityRequest? input=null)
    {
        var errors=new List<string>();void Require(bool condition,string error){if(!condition)errors.Add(error);}
        Require(d.AuthorityId==a.AuthorityId&&d.AuthorityChecksum==a.DeterministicChecksum&&d.Diagnostics==a.Diagnostics,"diagnostics/authority mismatch");
        Require(r.AuthorityId==a.AuthorityId&&r.AuthorityChecksum==a.DeterministicChecksum&&r.TotalPlanningSceneCount==a.LongScenes.Count+a.ShortScenes.Count,"report/authority mismatch");
        Require(v.AuthorityId==a.AuthorityId&&v.AuthorityChecksum==a.DeterministicChecksum,"validation/authority mismatch");
        Require(m.AuthorityId==a.AuthorityId&&m.AuthorityChecksum==a.DeterministicChecksum&&m.DiagnosticsChecksum==a.Diagnostics.DeterministicChecksum&&m.ReportChecksum==r.DeterministicChecksum&&m.ValidationChecksum==v.DeterministicChecksum,"manifest mismatch");
        Require(e.AuthorityChecksum==a.DeterministicChecksum&&e.ValidationChecksum==v.DeterministicChecksum&&e.ManifestEntryChecksum==m.DeterministicChecksum,"evidence mismatch");
        Require(m.PublicationStatus=="Committed"&&e.State=="Committed"&&e.CommittedPhysical,"publication is not committed");
        Require(inventory.Select(x=>x.RelativePath).Distinct(StringComparer.Ordinal).Count()==inventory.Count,"duplicate inventory path");
        if(input is not null)Require(a.ExecutionId==input.ExecutionId&&a.PlanId==input.PlanId&&a.EventId==input.EventId&&a.ProfileId==input.ProfileId&&a.ProfileVersion==input.ProfileVersion&&string.Equals(a.Language,input.Language,StringComparison.OrdinalIgnoreCase)&&a.PacketCollectionChecksum==input.SceneKnowledgePacketCollection.DeterministicChecksum,"current lineage mismatch");
        return new(errors.Count==0,errors.Count==0?"NARRATION_PLANNING_RECONCILED":NarrationPlanningPublicationReasonCodes.CommittedStateInvalid,errors,[]);
    }
}
public sealed class Phase7NarrationPlanningCommittedStateEvaluator(IPhase7NarrationPlanningFileSystem fs,IPhase7NarrationPlanningPhysicalReadback readback):IPhase7NarrationPlanningCommittedStateEvaluator
{
    private static readonly JsonSerializerOptions J=NarrationPlanningPublicationJson.Options;
    public async Task<NarrationPlanningCommittedStateEvaluation> EvaluateAsync(Phase7NarrationPlanningInputAuthorityRequest input,CancellationToken token=default)
    {
        var physical=await readback.ReadCommittedAsync(input.ExecutionRoot,token);if(!physical.IsValid)return new(false,null,physical.ReasonCode,physical.Errors,[]);
        try
        {
            async Task<T> Get<T>(string p)=>(JsonSerializer.Deserialize<T>(await fs.ReadAsync(NarrationPlanningPublicationJson.Full(input.ExecutionRoot,p),token),J)??throw new JsonException(p));
            var a=await Get<NarrationPlanningAuthority>(NarrationPlanningArtifactPaths.Authority);var da=await Get<NarrationPlanningDiagnosticsArtifact>(NarrationPlanningArtifactPaths.Diagnostics);var d=da.Diagnostics;
            var r=await Get<NarrationPlanningPublicationReport>(NarrationPlanningArtifactPaths.Report);var v=await Get<NarrationPlanningPhysicalValidation>(NarrationPlanningArtifactPaths.Validation);
            var e=await Get<NarrationPlanningPublicationEvidence>(NarrationPlanningArtifactPaths.PublicationEvidence);
            var manifest=JsonNode.Parse(await fs.ReadAsync(NarrationPlanningPublicationJson.Full(input.ExecutionRoot,NarrationPlanningArtifactPaths.Manifest),token))?.AsObject()??throw new JsonException("manifest");
            var entries=manifest["phase7NarrationPlanningAuthorities"]?.Deserialize<NarrationPlanningManifestEntry[]>(J)??[];var me=entries.SingleOrDefault(x=>x.AuthorityId==a.AuthorityId)??throw new JsonException("planning manifest entry missing");
            var checksumOk=a.DeterministicChecksum==NarrationPlanningCanonicalizer.AuthorityChecksum(a)&&d.DeterministicChecksum==NarrationPlanningCanonicalizer.DiagnosticsChecksum(d)&&da.DeterministicChecksum==NarrationPlanningPublicationCanonicalizer.ComputeDiagnosticsArtifactChecksum(da)&&r.DeterministicChecksum==NarrationPlanningPublicationCanonicalizer.ComputeReportChecksum(r)&&v.DeterministicChecksum==NarrationPlanningPublicationCanonicalizer.ComputePhysicalValidationChecksum(v)&&e.DeterministicChecksum==NarrationPlanningPublicationCanonicalizer.ComputePublicationEvidenceChecksum(e)&&me.DeterministicChecksum==NarrationPlanningPublicationCanonicalizer.ComputeManifestEntryChecksum(me);
            if(!checksumOk)return new(false,null,"NARRATION_PLANNING_COMMITTED_CHECKSUM_INVALID",["A deterministic checksum did not recompute."],[]);
            if(a.ExecutionId!=input.ExecutionId||a.PlanId!=input.PlanId||a.EventId!=input.EventId||!string.Equals(a.Language,input.Language,StringComparison.OrdinalIgnoreCase)||a.ProfileId!=input.ProfileId||a.ProfileVersion!=input.ProfileVersion)return new(false,null,NarrationPlanningPublicationReasonCodes.LineageStale,["Requested identity differs from committed identity."],[]);
            if(a.PacketCollectionChecksum!=input.SceneKnowledgePacketCollection.DeterministicChecksum||e.PacketCollectionChecksum!=input.SceneKnowledgePacketCollection.DeterministicChecksum)return new(false,null,NarrationPlanningPublicationReasonCodes.LineageStale,["Packet collection lineage changed."],[]);
            if(!v.CandidateReadbackPassed||!v.CommittedReadbackPassed||!v.PhysicalReadbackPassed||!v.CommittedStatePassed||v.Errors.Count>0||v.GateResults.Any(x=>!x.Passed)||e.State!="Committed"||!e.CommittedPhysical||me.PublicationStatus!="Committed"||e.ManifestEntryChecksum!=me.DeterministicChecksum||e.ValidationChecksum!=v.DeterministicChecksum)return new(false,null,NarrationPlanningPublicationReasonCodes.CommittedStateInvalid,["Validation, manifest, or publication evidence is not committed."],[]);
            var published=new PublishedNarrationPlanningAuthority(a,d,r,v,me,e,physical.Artifacts.Select(x=>x.RelativePath).ToArray(),physical.Artifacts.ToDictionary(x=>x.RelativePath,x=>x.PhysicalSha256),[]);
            return new(true,published,NarrationPlanningPublicationReasonCodes.ReuseValid,[],d.Warnings);
        }catch(Exception ex) when(ex is JsonException or InvalidOperationException){return new(false,null,NarrationPlanningPublicationReasonCodes.CommittedStateInvalid,[ex.Message],[]);}
    }
}
public sealed class Phase7NarrationPlanningTransactionCoordinator(IPhase7NarrationPlanningFileSystem fs,
    IPhase7NarrationPlanningExecutionLock executionLock,IPhase7NarrationPlanningRecoveryService recovery,
    IPhase7NarrationPlanningInputAuthorityEvaluator inputEvaluator,INarrationPlanningAuthorityBuilder builder,
    INarrationPlanningValidator validator,IPhase7NarrationPlanningPhysicalReadback readback,
    IPhase7NarrationPlanningCandidateReadback candidateReadback,IPhase7NarrationPlanningClock clock,
    IPhase7NarrationPlanningPublicationFaultInjector fault,IPhase7NarrationPlanningCommittedStateEvaluator committed):IPhase7NarrationPlanningTransactionCoordinator
{
    private static readonly JsonSerializerOptions J=NarrationPlanningPublicationJson.Options;
    public async Task<Phase7NarrationPlanningPublicationResult> ExecuteAsync(Phase7NarrationPlanningPublicationRequest request,CancellationToken token=default)
    {
        var input=request.Input;await using var lease=await executionLock.TryAcquireAsync(input.ExecutionRoot,input.PlanId,token);
        if(lease is null)return Fail(NarrationPlanningPublicationReasonCodes.LockUnavailable,"The phase-specific execution lock is held.");
        await recovery.RecoverAsync(input.ExecutionRoot,token);
        // Precedence: overwrite always rebuilds; otherwise every valid complete state is reused. Retry-failed-only
        // consequently rebuilds only a missing/invalid/stale state and never replaces a valid one.
        if(!request.OverwriteExisting)
        {
            var reuse=await committed.EvaluateAsync(input,token);
            if(reuse.IsValid&&reuse.Authority is not null)return Result(reuse.Authority.Authority,NarrationPlanningPublicationReasonCodes.ReuseValid,true,true,true,reuse.Warnings,[]);
        }
        var evaluated=await inputEvaluator.EvaluateAsync(input,token);
        if(!evaluated.IsValid||evaluated.Authority is null)return Fail(NarrationPlanningPublicationReasonCodes.InputInvalid,evaluated.Errors.ToArray());
        var built=builder.Build(evaluated.Authority);
        if(!built.IsValid||built.Authority is null||built.Errors.Count>0||built.BlockingIssues.Count>0)return Fail(NarrationPlanningPublicationReasonCodes.BuildInvalid,[..built.Errors,..built.BlockingIssues]);
        var authority=built.Authority;var semantic=validator.Validate(evaluated.Authority,authority);
        if(!semantic.IsValid||semantic.ReasonCode!="NARRATION_PLANNING_VALID"||semantic.Errors.Count>0||semantic.Gates.Any(x=>!x.Passed)||semantic.DeterministicChecksum!=NarrationPlanningCanonicalizer.ValidationChecksum(semantic))
            return Fail(NarrationPlanningPublicationReasonCodes.ValidationInvalid,semantic.Errors.ToArray());
        var tx=Guid.NewGuid().ToString("N");var root=Path.GetFullPath(input.ExecutionRoot);var narration=Path.Combine(root,"07-narration");
        var stage=Path.Combine(narration,".planning-staging-"+tx);var backup=Path.Combine(narration,".planning-backup-"+tx);
        try
        {
            fs.CreateDirectory(stage);var report=Report(authority,semantic,built.Warnings,request.OverwriteExisting?"Overwrite":"Publish");
            var stagedArtifacts=new List<NarrationPlanningArtifact>();
            async Task Write(string relative,object value)
            {
                var bytes=NarrationPlanningPublicationJson.Bytes(value);var path=Path.Combine(stage,relative.Replace('/',Path.DirectorySeparatorChar));await fs.WriteAsync(path,bytes,token);
                var checksum=value switch{NarrationPlanningAuthority x=>x.DeterministicChecksum,NarrationPlanningDiagnostics x=>x.DeterministicChecksum,NarrationPlanningPublicationReport x=>x.DeterministicChecksum,_=>NarrationPlanningPublicationJson.Sha(bytes)};
                stagedArtifacts.Add(new(relative,NarrationPlanningPublicationJson.Sha(bytes),bytes.LongLength,checksum));
            }
            await Write(NarrationPlanningArtifactPaths.Authority,authority);fault.Inject(NarrationPlanningPublicationFaultPoint.AfterAuthorityStageWrite);
            var diagnosticsDraft=new NarrationPlanningDiagnosticsArtifact(NarrationPlanningPublicationContract.DiagnosticsVersion,authority.AuthorityId,authority.DeterministicChecksum,authority.ExecutionId,authority.PlanId,authority.EventId,authority.Language,authority.ProfileId,authority.ProfileVersion,authority.Diagnostics,"");
            var diagnostics=diagnosticsDraft with{DeterministicChecksum=NarrationPlanningPublicationCanonicalizer.ComputeDiagnosticsArtifactChecksum(diagnosticsDraft)};
            await Write(NarrationPlanningArtifactPaths.Diagnostics,diagnostics);fault.Inject(NarrationPlanningPublicationFaultPoint.AfterDiagnosticsStageWrite);
            await Write(NarrationPlanningArtifactPaths.Report,report);fault.Inject(NarrationPlanningPublicationFaultPoint.AfterReportStageWrite);
            var lineage=new Dictionary<string,string>{{"phase6AuthorityId",evaluated.Authority.PublishedStoryFrameAuthority.Authority.AuthorityId},{"phase6AuthorityChecksum",authority.StoryFrameAuthorityChecksum},{"phase7KnowledgeAuthorityId",evaluated.Authority.PublishedPhase7KnowledgeAuthority.KnowledgeAuthority.AuthorityId},{"phase7KnowledgeAuthorityChecksum",authority.KnowledgeAuthorityChecksum},{"packetCollectionChecksum",authority.PacketCollectionChecksum}};
            var validationDraft=new NarrationPlanningPhysicalValidation(NarrationPlanningPublicationContract.ValidationVersion,authority.AuthorityId,authority.DeterministicChecksum,authority.ExecutionId,authority.PlanId,authority.EventId,authority.Language,authority.ProfileId,authority.ProfileVersion,"Candidate",semantic.Gates,semantic.Errors,[..built.Warnings,..authority.Diagnostics.Warnings],stagedArtifacts,lineage,false,false,""){CandidateReadbackPassed=true,CommittedReadbackPassed=false};
            var validation=validationDraft with{DeterministicChecksum=NarrationPlanningPublicationCanonicalizer.ComputePhysicalValidationChecksum(validationDraft)};await Write(NarrationPlanningArtifactPaths.Validation,validation);fault.Inject(NarrationPlanningPublicationFaultPoint.AfterValidationStageWrite);
            var manifestDraft=new NarrationPlanningManifestEntry(7,"P7.1B-BB","NarrationPlanningAuthority",NarrationPlanningContract.Version,authority.ExecutionId,authority.PlanId,authority.EventId,authority.AuthorityId,authority.DeterministicChecksum,authority.Diagnostics.DeterministicChecksum,report.DeterministicChecksum,validation.DeterministicChecksum,lineage["phase6AuthorityId"],authority.StoryFrameAuthorityChecksum,lineage["phase7KnowledgeAuthorityId"],authority.KnowledgeAuthorityChecksum,authority.PacketCollectionChecksum,authority.ProfileId,authority.ProfileVersion,authority.Language,"Committed",clock.UtcNow,stagedArtifacts.ToArray(),"");
            var entry=manifestDraft with{DeterministicChecksum=NarrationPlanningPublicationCanonicalizer.ComputeManifestEntryChecksum(manifestDraft)};
            var manifestPath=NarrationPlanningPublicationJson.Full(root,NarrationPlanningArtifactPaths.Manifest);JsonObject manifest;
            if(fs.FileExists(manifestPath))manifest=JsonNode.Parse(await fs.ReadAsync(manifestPath,token))?.AsObject()??new();else manifest=new(){{"schemaVersion","phase-manifest.v1"}};
            var existing=manifest["phase7NarrationPlanningAuthorities"]?.Deserialize<List<NarrationPlanningManifestEntry>>(J)??[];
            existing.RemoveAll(x=>x.Name=="NarrationPlanningAuthority"&&x.ExecutionId==authority.ExecutionId&&x.PlanId==authority.PlanId);existing.Add(entry);manifest["phase7NarrationPlanningAuthorities"]=JsonSerializer.SerializeToNode(existing,J);
            await fs.WriteAsync(Path.Combine(stage,NarrationPlanningArtifactPaths.Manifest),NarrationPlanningPublicationJson.Bytes(manifest),token);fault.Inject(NarrationPlanningPublicationFaultPoint.AfterManifestStageWrite);
            var evidenceDraft=new NarrationPlanningPublicationEvidence(NarrationPlanningPublicationContract.PublicationVersion,"Committed",authority.ExecutionId,authority.PlanId,authority.EventId,authority.Language,authority.ProfileId,authority.ProfileVersion,authority.AuthorityId,authority.DeterministicChecksum,validation.DeterministicChecksum,entry.DeterministicChecksum,NarrationPlanningPublicationCanonicalizer.ComputeArtifactInventoryChecksum(stagedArtifacts),lineage["phase6AuthorityId"],authority.StoryFrameAuthorityChecksum,lineage["phase7KnowledgeAuthorityId"],authority.KnowledgeAuthorityChecksum,authority.PacketCollectionChecksum,true,clock.UtcNow,"");
            var evidence=evidenceDraft with{DeterministicChecksum=NarrationPlanningPublicationCanonicalizer.ComputePublicationEvidenceChecksum(evidenceDraft)};await fs.WriteAsync(Path.Combine(stage,NarrationPlanningArtifactPaths.PublicationEvidence),NarrationPlanningPublicationJson.Bytes(evidence),token);fault.Inject(NarrationPlanningPublicationFaultPoint.AfterEvidenceStageWrite);
            var candidate=await candidateReadback.ReadAsync(stage,token);if(!candidate.IsValid)throw new InvalidDataException(string.Join(";",candidate.Errors));fault.Inject(NarrationPlanningPublicationFaultPoint.AfterCandidateReadback);
            // Final projection is produced only after the complete candidate has passed. Evidence remains last.
            validation=validation with{ValidationMode="CommittedPhysical",PhysicalReadbackPassed=true,CommittedReadbackPassed=true,CommittedStatePassed=true,DeterministicChecksum=""};
            validation=validation with{DeterministicChecksum=NarrationPlanningPublicationCanonicalizer.ComputePhysicalValidationChecksum(validation)};
            await fs.WriteAsync(Path.Combine(stage,NarrationPlanningArtifactPaths.Validation),NarrationPlanningPublicationJson.Bytes(validation),token);
            entry=entry with{ValidationChecksum=validation.DeterministicChecksum,DeterministicChecksum=""};entry=entry with{DeterministicChecksum=NarrationPlanningPublicationCanonicalizer.ComputeManifestEntryChecksum(entry)};
            existing.RemoveAll(x=>x.Name=="NarrationPlanningAuthority"&&x.ExecutionId==authority.ExecutionId&&x.PlanId==authority.PlanId);existing.Add(entry);manifest["phase7NarrationPlanningAuthorities"]=JsonSerializer.SerializeToNode(existing,J);
            await fs.WriteAsync(Path.Combine(stage,NarrationPlanningArtifactPaths.Manifest),NarrationPlanningPublicationJson.Bytes(manifest),token);
            evidence=evidence with{ValidationChecksum=validation.DeterministicChecksum,ManifestEntryChecksum=entry.DeterministicChecksum,DeterministicChecksum=""};evidence=evidence with{DeterministicChecksum=NarrationPlanningPublicationCanonicalizer.ComputePublicationEvidenceChecksum(evidence)};
            await fs.WriteAsync(Path.Combine(stage,NarrationPlanningArtifactPaths.PublicationEvidence),NarrationPlanningPublicationJson.Bytes(evidence),token);
            fs.CreateDirectory(backup);Backup(root,backup,NarrationPlanningArtifactPaths.Authority,true); // moves the whole planning directory
            fault.Inject(NarrationPlanningPublicationFaultPoint.AfterBackup);
            Backup(root,backup,NarrationPlanningArtifactPaths.Validation);Backup(root,backup,NarrationPlanningArtifactPaths.Manifest);Backup(root,backup,NarrationPlanningArtifactPaths.PublicationEvidence);
            try
            {
                var stagedPlanning=Path.Combine(stage,"07-narration","planning");if(fs.DirectoryExists(stagedPlanning))fs.MoveDirectory(stagedPlanning,Path.Combine(root,"07-narration","planning"));
                fault.Inject(NarrationPlanningPublicationFaultPoint.AfterPlanningSwap);
                PublishFile(stage,root,NarrationPlanningArtifactPaths.Validation);fault.Inject(NarrationPlanningPublicationFaultPoint.AfterValidationSwap);
                PublishFile(stage,root,NarrationPlanningArtifactPaths.Manifest);fault.Inject(NarrationPlanningPublicationFaultPoint.AfterManifestSwap);
                PublishFile(stage,root,NarrationPlanningArtifactPaths.PublicationEvidence);fault.Inject(NarrationPlanningPublicationFaultPoint.AfterEvidenceSwap); // commit marker is deliberately last
                // Prove deserialization while rollback remains available.
                _=JsonSerializer.Deserialize<NarrationPlanningPublicationEvidence>(await fs.ReadAsync(NarrationPlanningPublicationJson.Full(root,NarrationPlanningArtifactPaths.PublicationEvidence),token),J)??throw new InvalidDataException("Evidence readback empty.");
            }catch{Restore(root,backup);throw;}
            fault.Inject(NarrationPlanningPublicationFaultPoint.BeforeCommittedReadback);var physical=await readback.ReadCommittedAsync(root,token);if(!physical.IsValid)throw new InvalidDataException(string.Join(";",physical.Errors));fault.Inject(NarrationPlanningPublicationFaultPoint.AfterCommittedReadback);
            var final=await committed.EvaluateAsync(input,token);if(!final.IsValid)throw new InvalidDataException(string.Join(";",final.Errors));
            fault.Inject(NarrationPlanningPublicationFaultPoint.BeforeBackupDeletion);fs.DeleteDirectory(backup);fs.DeleteDirectory(stage);
            return Result(authority,NarrationPlanningPublicationReasonCodes.Committed,false,false,true,[..evaluated.Warnings,..built.Warnings],[]);
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        { if(fs.DirectoryExists(backup))Restore(root,backup);return Fail(NarrationPlanningPublicationReasonCodes.TransactionFailed,ex.Message); }
        finally{if(fs.DirectoryExists(stage))fs.DeleteDirectory(stage);if(fs.DirectoryExists(backup))fs.DeleteDirectory(backup);}
    }
    private void Backup(string root,string backup,string relative,bool planning=false){var source=planning?Path.Combine(root,"07-narration","planning"):NarrationPlanningPublicationJson.Full(root,relative);if(planning){if(fs.DirectoryExists(source))fs.MoveDirectory(source,Path.Combine(backup,"planning"));}else if(fs.FileExists(source))fs.MoveFile(source,Path.Combine(backup,relative.Replace('/',Path.DirectorySeparatorChar)),true);}
    private void PublishFile(string stage,string root,string relative){var source=Path.Combine(stage,relative.Replace('/',Path.DirectorySeparatorChar));fs.MoveFile(source,NarrationPlanningPublicationJson.Full(root,relative),true);}
    private void Restore(string root,string backup){var stable=Path.Combine(root,"07-narration","planning");if(fs.DirectoryExists(stable))fs.DeleteDirectory(stable);var saved=Path.Combine(backup,"planning");if(fs.DirectoryExists(saved))fs.MoveDirectory(saved,stable);foreach(var p in new[]{NarrationPlanningArtifactPaths.Validation,NarrationPlanningArtifactPaths.Manifest,NarrationPlanningArtifactPaths.PublicationEvidence}){var live=NarrationPlanningPublicationJson.Full(root,p);if(fs.FileExists(live))fs.DeleteFile(live);var old=Path.Combine(backup,p.Replace('/',Path.DirectorySeparatorChar));if(fs.FileExists(old))fs.MoveFile(old,live,true);}}
    private static NarrationPlanningPublicationReport Report(NarrationPlanningAuthority a,NarrationPlanningValidation v,IReadOnlyList<string> warnings,string mode){var d=a.Diagnostics;var x=new NarrationPlanningPublicationReport(NarrationPlanningPublicationContract.PublicationVersion,a.ExecutionId,a.PlanId,a.EventId,a.Language,a.ProfileId,a.ProfileVersion,a.AuthorityId,a.DeterministicChecksum,a.LongScenes.Count,a.ShortScenes.Count,a.LongScenes.Count+a.ShortScenes.Count,d.PrimaryReferenceCount,d.SupportingReferenceCount,d.RequiredClaimCount,d.OptionalClaimCount,d.DeferredClaimCount,d.TransitionCount,d.BlockingIssueCount,d.FailedGateCount,warnings.Count+d.WarningCount,v.ReasonCode,mode,false,"");return x with{DeterministicChecksum=NarrationPlanningPublicationCanonicalizer.ComputeReportChecksum(x)};}
    private static Phase7NarrationPlanningPublicationResult Result(NarrationPlanningAuthority a,string code,bool already,bool reused,bool committed,IReadOnlyList<string> warnings,IReadOnlyList<string> errors)=>new(true,code,already,reused,committed,true,a.AuthorityId,a.DeterministicChecksum,a.LongScenes.Count,a.ShortScenes.Count,a.LongScenes.Count+a.ShortScenes.Count,warnings,errors,NarrationPlanningArtifactPaths.Governed,NarrationPlanningArtifactPaths.Validation,NarrationPlanningArtifactPaths.PublicationEvidence);
    private static Phase7NarrationPlanningPublicationResult Fail(string code,params string[] errors)=>new(false,code,false,false,false,false,"","",0,0,0,[],errors,[],NarrationPlanningArtifactPaths.Validation,NarrationPlanningArtifactPaths.PublicationEvidence);
}
public sealed class Phase7NarrationPlanningPublicationService(IPhase7NarrationPlanningTransactionCoordinator transaction):IPhase7NarrationPlanningPublicationService
{ public Task<Phase7NarrationPlanningPublicationResult> ExecuteAsync(Phase7NarrationPlanningPublicationRequest request,CancellationToken token=default)=>transaction.ExecuteAsync(request,token); }
