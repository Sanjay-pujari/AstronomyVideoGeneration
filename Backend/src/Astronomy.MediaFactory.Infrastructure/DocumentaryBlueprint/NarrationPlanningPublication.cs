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
        try{await fs.WriteAsync(path,Array.Empty<byte>(),token);return new Lease(gate,fs,path);}catch{gate.Release();throw;}
    }
    private sealed class Lease(SemaphoreSlim gate,IPhase7NarrationPlanningFileSystem fs,string path):IAsyncDisposable
    { public ValueTask DisposeAsync(){fs.DeleteFile(path);gate.Release();return ValueTask.CompletedTask;} }
}
public sealed class Phase7NarrationPlanningRecoveryService(IPhase7NarrationPlanningFileSystem fs):IPhase7NarrationPlanningRecoveryService
{
    public Task RecoverAsync(string root,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var narration=Path.Combine(Path.GetFullPath(root),"07-narration");
        foreach(var stage in fs.Directories(narration,".planning-staging-*"))fs.DeleteDirectory(stage);
        foreach(var backup in fs.Directories(narration,".planning-backup-*"))
        {
            var stable=Path.Combine(narration,"planning");var saved=Path.Combine(backup,"planning");
            if(!fs.DirectoryExists(stable)&&fs.DirectoryExists(saved))fs.MoveDirectory(saved,stable);
            fs.DeleteDirectory(backup);
        }
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
                    NarrationPlanningArtifactPaths.Diagnostics=>JsonSerializer.Deserialize<NarrationPlanningDiagnostics>(bytes,J),
                    NarrationPlanningArtifactPaths.Report=>JsonSerializer.Deserialize<NarrationPlanningPublicationReport>(bytes,J),
                    NarrationPlanningArtifactPaths.Validation=>JsonSerializer.Deserialize<NarrationPlanningPhysicalValidation>(bytes,J),
                    NarrationPlanningArtifactPaths.PublicationEvidence=>JsonSerializer.Deserialize<NarrationPlanningPublicationEvidence>(bytes,J),
                    _=>JsonNode.Parse(bytes)
                }; if(value is null)throw new JsonException("Empty artifact");
                var semantic=value switch { NarrationPlanningAuthority x=>x.DeterministicChecksum,NarrationPlanningDiagnostics x=>x.DeterministicChecksum,
                    NarrationPlanningPublicationReport x=>x.DeterministicChecksum,NarrationPlanningPhysicalValidation x=>x.DeterministicChecksum,
                    NarrationPlanningPublicationEvidence x=>x.DeterministicChecksum,_=>NarrationPlanningPublicationJson.Sha(bytes)};
                artifacts.Add(new(relative,NarrationPlanningPublicationJson.Sha(bytes),bytes.LongLength,semantic));
            }catch(Exception ex) when(ex is JsonException or NotSupportedException){errors.Add("NARRATION_PLANNING_COMMITTED_CHECKSUM_INVALID:"+relative+":"+ex.Message);}
        }
        if(fs.Directories(Path.Combine(Path.GetFullPath(root),"07-narration"),".planning-*").Count>0)errors.Add("NARRATION_PLANNING_PHYSICAL_READBACK_INVALID:transaction residue");
        return new(errors.Count==0,errors.Count==0?NarrationPlanningPublicationReasonCodes.ReuseValid:NarrationPlanningPublicationReasonCodes.PhysicalReadbackInvalid,artifacts,errors);
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
            var a=await Get<NarrationPlanningAuthority>(NarrationPlanningArtifactPaths.Authority);var d=await Get<NarrationPlanningDiagnostics>(NarrationPlanningArtifactPaths.Diagnostics);
            var r=await Get<NarrationPlanningPublicationReport>(NarrationPlanningArtifactPaths.Report);var v=await Get<NarrationPlanningPhysicalValidation>(NarrationPlanningArtifactPaths.Validation);
            var e=await Get<NarrationPlanningPublicationEvidence>(NarrationPlanningArtifactPaths.PublicationEvidence);
            var manifest=JsonNode.Parse(await fs.ReadAsync(NarrationPlanningPublicationJson.Full(input.ExecutionRoot,NarrationPlanningArtifactPaths.Manifest),token))?.AsObject()??throw new JsonException("manifest");
            var entries=manifest["phase7NarrationPlanningAuthorities"]?.Deserialize<NarrationPlanningManifestEntry[]>(J)??[];var me=entries.SingleOrDefault(x=>x.AuthorityId==a.AuthorityId)??throw new JsonException("planning manifest entry missing");
            var checksumOk=a.DeterministicChecksum==NarrationPlanningPublicationJson.Hash(a with{DeterministicChecksum=""})&&d.DeterministicChecksum==NarrationPlanningPublicationJson.Hash(d with{DeterministicChecksum=""})&&r.DeterministicChecksum==NarrationPlanningPublicationJson.Hash(r with{DeterministicChecksum=""})&&v.DeterministicChecksum==NarrationPlanningPublicationJson.Hash(v with{DeterministicChecksum=""})&&e.DeterministicChecksum==NarrationPlanningPublicationJson.Hash(e with{DeterministicChecksum=""})&&me.DeterministicChecksum==NarrationPlanningPublicationJson.Hash(me with{DeterministicChecksum=""});
            if(!checksumOk)return new(false,null,"NARRATION_PLANNING_COMMITTED_CHECKSUM_INVALID",["A deterministic checksum did not recompute."],[]);
            if(a.ExecutionId!=input.ExecutionId||a.PlanId!=input.PlanId||a.EventId!=input.EventId||!string.Equals(a.Language,input.Language,StringComparison.OrdinalIgnoreCase)||a.ProfileId!=input.ProfileId||a.ProfileVersion!=input.ProfileVersion)return new(false,null,NarrationPlanningPublicationReasonCodes.LineageStale,["Requested identity differs from committed identity."],[]);
            if(a.PacketCollectionChecksum!=input.SceneKnowledgePacketCollection.DeterministicChecksum||e.PacketCollectionChecksum!=input.SceneKnowledgePacketCollection.DeterministicChecksum)return new(false,null,NarrationPlanningPublicationReasonCodes.LineageStale,["Packet collection lineage changed."],[]);
            if(!v.PhysicalReadbackPassed||!v.CommittedStatePassed||v.Errors.Count>0||v.GateResults.Any(x=>!x.Passed)||e.State!="Committed"||!e.CommittedPhysical||me.PublicationStatus!="Committed"||e.ManifestEntryChecksum!=me.DeterministicChecksum)return new(false,null,NarrationPlanningPublicationReasonCodes.CommittedStateInvalid,["Validation, manifest, or publication evidence is not committed."],[]);
            var published=new PublishedNarrationPlanningAuthority(a,d,r,v,me,e,physical.Artifacts.Select(x=>x.RelativePath).ToArray(),physical.Artifacts.ToDictionary(x=>x.RelativePath,x=>x.PhysicalSha256),[]);
            return new(true,published,NarrationPlanningPublicationReasonCodes.ReuseValid,[],d.Warnings);
        }catch(Exception ex) when(ex is JsonException or InvalidOperationException){return new(false,null,NarrationPlanningPublicationReasonCodes.CommittedStateInvalid,[ex.Message],[]);}
    }
}
public sealed class Phase7NarrationPlanningTransactionCoordinator(IPhase7NarrationPlanningFileSystem fs,
    IPhase7NarrationPlanningExecutionLock executionLock,IPhase7NarrationPlanningRecoveryService recovery,
    IPhase7NarrationPlanningInputAuthorityEvaluator inputEvaluator,INarrationPlanningAuthorityBuilder builder,
    INarrationPlanningValidator validator,IPhase7NarrationPlanningPhysicalReadback readback,
    IPhase7NarrationPlanningCommittedStateEvaluator committed):IPhase7NarrationPlanningTransactionCoordinator
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
        if(!semantic.IsValid||semantic.ReasonCode!="NARRATION_PLANNING_VALID"||semantic.Errors.Count>0||semantic.Gates.Any(x=>!x.Passed)||semantic.DeterministicChecksum!=NarrationPlanningPublicationJson.Hash(semantic with{DeterministicChecksum=""}))
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
            await Write(NarrationPlanningArtifactPaths.Authority,authority);await Write(NarrationPlanningArtifactPaths.Diagnostics,authority.Diagnostics);await Write(NarrationPlanningArtifactPaths.Report,report);
            var lineage=new Dictionary<string,string>{{"phase6AuthorityId",evaluated.Authority.PublishedStoryFrameAuthority.Authority.AuthorityId},{"phase6AuthorityChecksum",authority.StoryFrameAuthorityChecksum},{"phase7KnowledgeAuthorityId",evaluated.Authority.PublishedPhase7KnowledgeAuthority.KnowledgeAuthority.AuthorityId},{"phase7KnowledgeAuthorityChecksum",authority.KnowledgeAuthorityChecksum},{"packetCollectionChecksum",authority.PacketCollectionChecksum}};
            var validationDraft=new NarrationPlanningPhysicalValidation(NarrationPlanningPublicationContract.ValidationVersion,authority.AuthorityId,authority.DeterministicChecksum,authority.ExecutionId,authority.PlanId,authority.EventId,authority.Language,authority.ProfileId,authority.ProfileVersion,"CommittedPhysical",semantic.Gates,semantic.Errors,[..built.Warnings,..authority.Diagnostics.Warnings],stagedArtifacts,lineage,true,true,"");
            var validation=validationDraft with{DeterministicChecksum=NarrationPlanningPublicationJson.Hash(validationDraft)};await Write(NarrationPlanningArtifactPaths.Validation,validation);
            var manifestDraft=new NarrationPlanningManifestEntry(7,"P7.1B-BB","NarrationPlanningAuthority",NarrationPlanningContract.Version,authority.ExecutionId,authority.PlanId,authority.EventId,authority.AuthorityId,authority.DeterministicChecksum,authority.Diagnostics.DeterministicChecksum,report.DeterministicChecksum,validation.DeterministicChecksum,lineage["phase6AuthorityId"],authority.StoryFrameAuthorityChecksum,lineage["phase7KnowledgeAuthorityId"],authority.KnowledgeAuthorityChecksum,authority.PacketCollectionChecksum,authority.ProfileId,authority.ProfileVersion,authority.Language,"Committed",DateTimeOffset.UnixEpoch,stagedArtifacts.ToArray(),"");
            var entry=manifestDraft with{DeterministicChecksum=NarrationPlanningPublicationJson.Hash(manifestDraft)};
            var manifestPath=NarrationPlanningPublicationJson.Full(root,NarrationPlanningArtifactPaths.Manifest);JsonObject manifest;
            if(fs.FileExists(manifestPath))manifest=JsonNode.Parse(await fs.ReadAsync(manifestPath,token))?.AsObject()??new();else manifest=new(){{"schemaVersion","phase-manifest.v1"}};
            var existing=manifest["phase7NarrationPlanningAuthorities"]?.Deserialize<List<NarrationPlanningManifestEntry>>(J)??[];
            existing.RemoveAll(x=>x.Name=="NarrationPlanningAuthority"&&x.ExecutionId==authority.ExecutionId&&x.PlanId==authority.PlanId);existing.Add(entry);manifest["phase7NarrationPlanningAuthorities"]=JsonSerializer.SerializeToNode(existing,J);
            await fs.WriteAsync(Path.Combine(stage,NarrationPlanningArtifactPaths.Manifest),NarrationPlanningPublicationJson.Bytes(manifest),token);
            var evidenceDraft=new NarrationPlanningPublicationEvidence(NarrationPlanningPublicationContract.PublicationVersion,"Committed",authority.ExecutionId,authority.PlanId,authority.EventId,authority.Language,authority.ProfileId,authority.ProfileVersion,authority.AuthorityId,authority.DeterministicChecksum,validation.DeterministicChecksum,entry.DeterministicChecksum,NarrationPlanningPublicationJson.Hash(stagedArtifacts),lineage["phase6AuthorityId"],authority.StoryFrameAuthorityChecksum,lineage["phase7KnowledgeAuthorityId"],authority.KnowledgeAuthorityChecksum,authority.PacketCollectionChecksum,true,DateTimeOffset.UnixEpoch,"");
            var evidence=evidenceDraft with{DeterministicChecksum=NarrationPlanningPublicationJson.Hash(evidenceDraft)};await fs.WriteAsync(Path.Combine(stage,NarrationPlanningArtifactPaths.PublicationEvidence),NarrationPlanningPublicationJson.Bytes(evidence),token);
            // Deserialize every candidate before changing any committed path.
            _=JsonSerializer.Deserialize<NarrationPlanningAuthority>(await fs.ReadAsync(Path.Combine(stage,NarrationPlanningArtifactPaths.Authority),token),J)??throw new InvalidDataException("Staged authority empty.");
            fs.CreateDirectory(backup);Backup(root,backup,NarrationPlanningArtifactPaths.Authority,true); // moves the whole planning directory
            Backup(root,backup,NarrationPlanningArtifactPaths.Validation);Backup(root,backup,NarrationPlanningArtifactPaths.Manifest);Backup(root,backup,NarrationPlanningArtifactPaths.PublicationEvidence);
            try
            {
                var stagedPlanning=Path.Combine(stage,"07-narration","planning");if(fs.DirectoryExists(stagedPlanning))fs.MoveDirectory(stagedPlanning,Path.Combine(root,"07-narration","planning"));
                PublishFile(stage,root,NarrationPlanningArtifactPaths.Validation);PublishFile(stage,root,NarrationPlanningArtifactPaths.Manifest);
                PublishFile(stage,root,NarrationPlanningArtifactPaths.PublicationEvidence); // commit marker is deliberately last
                // Prove deserialization while rollback remains available.
                _=JsonSerializer.Deserialize<NarrationPlanningPublicationEvidence>(await fs.ReadAsync(NarrationPlanningPublicationJson.Full(root,NarrationPlanningArtifactPaths.PublicationEvidence),token),J)??throw new InvalidDataException("Evidence readback empty.");
            }catch{Restore(root,backup);throw;}
            fs.DeleteDirectory(stage);fs.DeleteDirectory(backup);
            var final=await committed.EvaluateAsync(input,token);if(!final.IsValid)throw new InvalidDataException(string.Join(";",final.Errors));
            return Result(authority,NarrationPlanningPublicationReasonCodes.Committed,false,false,true,[..evaluated.Warnings,..built.Warnings],[]);
        }
        catch(Exception ex) when(ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        { if(fs.DirectoryExists(backup))Restore(root,backup);return Fail(NarrationPlanningPublicationReasonCodes.TransactionFailed,ex.Message); }
        finally{if(fs.DirectoryExists(stage))fs.DeleteDirectory(stage);if(fs.DirectoryExists(backup))fs.DeleteDirectory(backup);}
    }
    private void Backup(string root,string backup,string relative,bool planning=false){var source=planning?Path.Combine(root,"07-narration","planning"):NarrationPlanningPublicationJson.Full(root,relative);if(planning){if(fs.DirectoryExists(source))fs.MoveDirectory(source,Path.Combine(backup,"planning"));}else if(fs.FileExists(source))fs.MoveFile(source,Path.Combine(backup,relative.Replace('/',Path.DirectorySeparatorChar)),true);}
    private void PublishFile(string stage,string root,string relative){var source=Path.Combine(stage,relative.Replace('/',Path.DirectorySeparatorChar));fs.MoveFile(source,NarrationPlanningPublicationJson.Full(root,relative),true);}
    private void Restore(string root,string backup){var stable=Path.Combine(root,"07-narration","planning");if(fs.DirectoryExists(stable))fs.DeleteDirectory(stable);var saved=Path.Combine(backup,"planning");if(fs.DirectoryExists(saved))fs.MoveDirectory(saved,stable);foreach(var p in new[]{NarrationPlanningArtifactPaths.Validation,NarrationPlanningArtifactPaths.Manifest,NarrationPlanningArtifactPaths.PublicationEvidence}){var live=NarrationPlanningPublicationJson.Full(root,p);if(fs.FileExists(live))fs.DeleteFile(live);var old=Path.Combine(backup,p.Replace('/',Path.DirectorySeparatorChar));if(fs.FileExists(old))fs.MoveFile(old,live,true);}}
    private static NarrationPlanningPublicationReport Report(NarrationPlanningAuthority a,NarrationPlanningValidation v,IReadOnlyList<string> warnings,string mode){var d=a.Diagnostics;var x=new NarrationPlanningPublicationReport(NarrationPlanningPublicationContract.PublicationVersion,a.ExecutionId,a.PlanId,a.EventId,a.Language,a.ProfileId,a.ProfileVersion,a.AuthorityId,a.DeterministicChecksum,a.LongScenes.Count,a.ShortScenes.Count,a.LongScenes.Count+a.ShortScenes.Count,d.PrimaryReferenceCount,d.SupportingReferenceCount,d.RequiredClaimCount,d.OptionalClaimCount,d.DeferredClaimCount,d.TransitionCount,d.BlockingIssueCount,d.FailedGateCount,warnings.Count+d.WarningCount,v.ReasonCode,mode,false,"");return x with{DeterministicChecksum=NarrationPlanningPublicationJson.Hash(x)};}
    private static Phase7NarrationPlanningPublicationResult Result(NarrationPlanningAuthority a,string code,bool already,bool reused,bool committed,IReadOnlyList<string> warnings,IReadOnlyList<string> errors)=>new(true,code,already,reused,committed,true,a.AuthorityId,a.DeterministicChecksum,a.LongScenes.Count,a.ShortScenes.Count,a.LongScenes.Count+a.ShortScenes.Count,warnings,errors,NarrationPlanningArtifactPaths.Governed,NarrationPlanningArtifactPaths.Validation,NarrationPlanningArtifactPaths.PublicationEvidence);
    private static Phase7NarrationPlanningPublicationResult Fail(string code,params string[] errors)=>new(false,code,false,false,false,false,"","",0,0,0,[],errors,[],NarrationPlanningArtifactPaths.Validation,NarrationPlanningArtifactPaths.PublicationEvidence);
}
public sealed class Phase7NarrationPlanningPublicationService(IPhase7NarrationPlanningTransactionCoordinator transaction):IPhase7NarrationPlanningPublicationService
{ public Task<Phase7NarrationPlanningPublicationResult> ExecuteAsync(Phase7NarrationPlanningPublicationRequest request,CancellationToken token=default)=>transaction.ExecuteAsync(request,token); }
