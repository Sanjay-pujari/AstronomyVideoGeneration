using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7KnowledgeFileSystem : IPhase7KnowledgeFileSystem
{
    public bool FileExists(string p)=>File.Exists(p); public bool DirectoryExists(string p)=>Directory.Exists(p);
    public void CreateDirectory(string p)=>Directory.CreateDirectory(p);
    public Task<string> ReadAllTextAsync(string p,CancellationToken t=default)=>File.ReadAllTextAsync(p,t);
    public Task<byte[]> ReadAllBytesAsync(string p,CancellationToken t=default)=>File.ReadAllBytesAsync(p,t);
    public Task WriteAllTextAsync(string p,string c,CancellationToken t=default)=>File.WriteAllTextAsync(p,c,t);
    public Task WriteAllBytesAsync(string p,byte[] c,CancellationToken t=default)=>File.WriteAllBytesAsync(p,c,t);
    public void DeleteFile(string p)=>File.Delete(p); public void DeleteDirectory(string p,bool r=true){if(Directory.Exists(p))Directory.Delete(p,r);}
    public void MoveDirectory(string s,string d)=>Directory.Move(s,d); public void MoveFile(string s,string d,bool o=false)=>File.Move(s,d,o);
    public void CopyFile(string s,string d,bool o=false)=>File.Copy(s,d,o);
    public IReadOnlyList<string> EnumerateOwnedPaths(string p)=>Directory.Exists(p)?Directory.EnumerateFileSystemEntries(p).Order().ToArray():[];
}

public sealed class Phase7KnowledgeExecutionLock : IPhase7KnowledgeExecutionLock
{
    private static readonly ConcurrentDictionary<string,SemaphoreSlim> Locks=new(StringComparer.Ordinal);
    public async Task<IAsyncDisposable> AcquireAsync(string key,CancellationToken token=default)
    { var gate=Locks.GetOrAdd(key,_=>new(1,1)); await gate.WaitAsync(token); return new Release(gate); }
    private sealed class Release(SemaphoreSlim gate):IAsyncDisposable { public ValueTask DisposeAsync(){gate.Release();return ValueTask.CompletedTask;} }
}

public sealed class Phase7KnowledgePhysicalReadback(IPhase7KnowledgeFileSystem fs) : IPhase7KnowledgePhysicalReadback
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static readonly (string Name,Type Type)[] Set=[("knowledge-authority.json",typeof(Phase7KnowledgeAuthority)),
        ("knowledge-resolution-report.json",typeof(ResolvedNarrationKnowledge)),("knowledge-diagnostics.json",typeof(Phase7KnowledgeDiagnostics))];
    public async Task<Phase7KnowledgeArtifactInventory> CreateCandidateInventoryAsync(string dir,Phase7KnowledgeAuthority a,CancellationToken token=default)
    {
        var entries=new List<Phase7KnowledgeArtifactInventoryEntry>();
        foreach(var item in Set)
        {
            var path=Path.Combine(dir,item.Name); var bytes=await fs.ReadAllBytesAsync(path,token);
            var value=JsonSerializer.Deserialize(bytes,item.Type,Json)??throw new InvalidDataException($"Empty artifact: {item.Name}");
            var semantic=Semantic(value); var relative="07-narration/knowledge/"+item.Name;
            if(semantic=="INVALID")throw new InvalidDataException($"Invalid embedded semantic checksum: {item.Name}");
            if(!Identity(value,a))throw new InvalidDataException($"Artifact identity or lineage mismatch: {item.Name}");
            entries.Add(new(relative,item.Type.Name,Phase7KnowledgeContract.Version,semantic,Sha(bytes),bytes.LongLength,
                a.ExecutionId,a.PlanId,a.EventId,a.AuthorityId,a.SemanticChecksum,a.SourcePhase6AuthorityId,
                a.SourcePhase6AuthorityChecksum,Phase7Determinism.Hash(new{a.SourcePhase4Checksum,a.SourcePhase5PublicationId,a.SourcePhase6AuthorityChecksum}),true));
        }
        var draft=new Phase7KnowledgeArtifactInventory(entries,""); return draft with{DeterministicChecksum=Phase7Determinism.Hash(draft)};
    }
    public Task<Phase7KnowledgeCompleteSetReadback> ValidateCandidateCompleteSetAsync(string root,Phase7KnowledgeAuthority a,Phase7KnowledgeArtifactInventory i,CancellationToken token=default)
        =>Validate(root,a,i,null,true,token);
    public Task<Phase7KnowledgeCompleteSetReadback> ValidateCommittedCompleteSetAsync(string root,Phase7KnowledgeAuthority a,Phase7KnowledgeArtifactInventory i,string validationHash,bool manifest,CancellationToken token=default)
        =>Validate(root,a,i,validationHash,manifest,token);
    private async Task<Phase7KnowledgeCompleteSetReadback> Validate(string root,Phase7KnowledgeAuthority a,Phase7KnowledgeArtifactInventory inventory,string? expectedValidationHash,bool manifest,CancellationToken token)
    {
        root=Path.GetFullPath(root);var all=new List<Phase7KnowledgeArtifactReadbackEvidence>();var errors=new List<string>();
        foreach(var entry in inventory.Artifacts)
        {
            var safe=Safe(entry.RelativePath);var path=safe?Path.Combine(root,entry.RelativePath.Replace('/',Path.DirectorySeparatorChar)):"";
            var local=new List<string>();byte[] bytes=[];object? value=null;
            if(!safe)local.Add("P7KNOWLEDGE_UNSAFE_PATH"); else if(!fs.FileExists(path))local.Add("P7KNOWLEDGE_ARTIFACT_MISSING");
            else { bytes=await fs.ReadAllBytesAsync(path,token);try{value=JsonSerializer.Deserialize(bytes,Set.Single(x=>"07-narration/knowledge/"+x.Name==entry.RelativePath).Type,Json);}catch(JsonException ex){local.Add(ex.Message);} }
            var identity=value switch{Phase7KnowledgeAuthority x=>x.AuthorityId==a.AuthorityId&&x.ExecutionId==a.ExecutionId&&x.PlanId==a.PlanId&&x.EventId==a.EventId,
                ResolvedNarrationKnowledge x=>x.PayloadId==a.EventKnowledgePayloadId,Phase7KnowledgeDiagnostics x=>x.AuthorityId==a.AuthorityId,_=>false};
            var semantic=value is not null&&Semantic(value)==entry.SemanticChecksum;var hash=bytes.Length==0?"":Sha(bytes);
            if(bytes.Length>0&&(hash!=entry.PhysicalSha256||bytes.LongLength!=entry.SizeBytes))local.Add("P7KNOWLEDGE_PHYSICAL_HASH_OR_SIZE_MISMATCH");
            if(!identity)local.Add("P7KNOWLEDGE_IDENTITY_MISMATCH");if(!semantic)local.Add("P7KNOWLEDGE_SEMANTIC_CHECKSUM_MISMATCH");
            all.Add(new(entry.RelativePath,bytes.Length>0,bytes.LongLength,hash,value is not null,entry.ContractType,entry.ContractVersion,identity,semantic,true,safe,local));errors.AddRange(local);
        }
        Phase7KnowledgeArtifactReadbackEvidence? validation=null;var vp=Path.Combine(root,"validation","phase-07-knowledge-validation.json");
        if(fs.FileExists(vp)){var b=await fs.ReadAllBytesAsync(vp,token);Phase7KnowledgeValidation? v=null;try{v=JsonSerializer.Deserialize<Phase7KnowledgeValidation>(b,Json);}catch(JsonException ex){errors.Add(ex.Message);}
            var ok=v?.AuthorityId==a.AuthorityId&&v.ArtifactInventory?.DeterministicChecksum==inventory.DeterministicChecksum;
            if(expectedValidationHash is not null&&Sha(b)!=expectedValidationHash){ok=false;errors.Add("P7KNOWLEDGE_VALIDATION_HASH_MISMATCH");}
            validation=new("validation/phase-07-knowledge-validation.json",true,b.LongLength,Sha(b),v is not null,nameof(Phase7KnowledgeValidation),Phase7KnowledgeContract.Version,ok,ok,true,true,ok?[]:["P7KNOWLEDGE_VALIDATION_MISMATCH"]);}
        else errors.Add("P7KNOWLEDGE_VALIDATION_MISSING");
        if(!manifest)errors.Add("P7KNOWLEDGE_MANIFEST_EVIDENCE_MISSING");
        return new(all,validation,manifest,errors.Count==0,errors,inventory);
    }
    private static bool Safe(string p)=>!Path.IsPathRooted(p)&&!p.Contains('\\')&&!p.Split('/').Any(x=>x is "" or "." or "..");
    private static string Sha(byte[] b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    private static bool Identity(object value,Phase7KnowledgeAuthority a)=>value switch
    {
        Phase7KnowledgeAuthority x=>x.ContractVersion==Phase7KnowledgeContract.Version&&x.ExecutionId==a.ExecutionId&&x.PlanId==a.PlanId&&x.EventId==a.EventId&&x.AuthorityId==a.AuthorityId&&x.SourcePhase4Checksum==a.SourcePhase4Checksum&&x.SourcePhase5PublicationId==a.SourcePhase5PublicationId&&x.SourcePhase6AuthorityId==a.SourcePhase6AuthorityId&&x.SourcePhase6AuthorityChecksum==a.SourcePhase6AuthorityChecksum&&x.SourcePhase6IndexId==a.SourcePhase6IndexId&&x.SourcePhase6IndexChecksum==a.SourcePhase6IndexChecksum,
        ResolvedNarrationKnowledge x=>x.PayloadId==a.EventKnowledgePayloadId&&x.PayloadChecksum==a.EventKnowledgeChecksum&&x.SourceRegistryId==a.SourceRegistryId&&x.SourceRegistryChecksum==a.SourceRegistryChecksum,
        Phase7KnowledgeDiagnostics x=>x.ContractVersion==Phase7KnowledgeContract.Version&&x.ExecutionId==a.ExecutionId&&x.PlanId==a.PlanId&&x.EventId==a.EventId&&x.AuthorityId==a.AuthorityId,
        _=>false
    };
    private static string Semantic(object o)=>o switch{Phase7KnowledgeAuthority x when x.SemanticChecksum==Phase7Determinism.Hash(x with{SemanticChecksum=""})=>x.SemanticChecksum,
        ResolvedNarrationKnowledge x when x.DeterministicChecksum==Phase7Determinism.Hash(x with{DeterministicChecksum=""})=>x.DeterministicChecksum,
        Phase7KnowledgeDiagnostics x when x.DeterministicChecksum==Phase7Determinism.Hash(x with{DeterministicChecksum=""})=>x.DeterministicChecksum,_=>"INVALID"};
}

public sealed class Phase7KnowledgeCommittedStateEvaluator(IPhase7KnowledgeFileSystem fs,
    IPhase7KnowledgePhysicalReadback readback) : IPhase7KnowledgeCommittedStateEvaluator
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<Phase7KnowledgeCommittedStateEvaluation> EvaluateAsync(Phase7KnowledgeCommittedStateRequest request,CancellationToken token=default)
    {
        var root=Path.GetFullPath(request.ExecutionRoot);var authorityPath=Path.Combine(root,"07-narration","knowledge","knowledge-authority.json");
        var validationPath=Path.Combine(root,"validation","phase-07-knowledge-validation.json");var evidencePath=Path.Combine(root,".phase-07-knowledge-publication.json");
        if(!fs.FileExists(authorityPath)||!fs.FileExists(validationPath)||!fs.FileExists(evidencePath))
            return new(false,null,"P7KNOWLEDGE_COMPLETE_SET_INVALID",["A required committed artifact or external publication evidence is missing."],[]);
        try
        {
            var a=JsonSerializer.Deserialize<Phase7KnowledgeAuthority>(await fs.ReadAllTextAsync(authorityPath,token),Json)!;
            var v=JsonSerializer.Deserialize<Phase7KnowledgeValidation>(await fs.ReadAllTextAsync(validationPath,token),Json)!;
            var e=JsonSerializer.Deserialize<PublicationEvidence>(await fs.ReadAllTextAsync(evidencePath,token),Json)!;
            var errors=new List<string>();
            if(a.ExecutionId!=request.ExecutionId||a.PlanId!=request.PlanId||a.EventId!=request.EventId||a.Language!=request.Language)errors.Add("P7KNOWLEDGE_IDENTITY_MISMATCH");
            if(a.SemanticChecksum!=Phase7Determinism.Hash(a with{SemanticChecksum=""}))errors.Add("P7KNOWLEDGE_AUTHORITY_CHECKSUM_INVALID");
            if(v.ArtifactInventory is null||v.ArtifactInventory.Artifacts.Count!=3)errors.Add("P7KNOWLEDGE_INVENTORY_INVALID");
            if(!v.IsValid||v.Mode!=Phase7KnowledgeValidationMode.StagedPhysical&&v.Mode!=Phase7KnowledgeValidationMode.CommittedPhysical)errors.Add("P7KNOWLEDGE_VALIDATION_INVALID");
            if(errors.Count>0)return new(false,null,errors[0],errors,[]);
            var physical=await readback.ValidateCommittedCompleteSetAsync(root,a,v.ArtifactInventory!,e.ValidationPhysicalSha256,e.ManifestEvidenceValid,token);
            if(!physical.IsValid)return new(false,null,"P7KNOWLEDGE_PHYSICAL_READBACK_INVALID",physical.Errors,[]);
            var paths=physical.Artifacts.Select(x=>x.RelativePath).Append("validation/phase-07-knowledge-validation.json").ToArray();
            var published=new PublishedPhase7KnowledgeAuthority(a,paths,
                physical.Artifacts.ToDictionary(x=>x.RelativePath,x=>v.ArtifactInventory!.Artifacts.Single(y=>y.RelativePath==x.RelativePath).SemanticChecksum),
                physical.Artifacts.ToDictionary(x=>x.RelativePath,x=>x.PhysicalSha256),physical.Artifacts.ToDictionary(x=>x.RelativePath,x=>x.SizeBytes),
                [v.DeterministicChecksum],[e.PublicationId],e.PublicationId,true,true,true,
                new Dictionary<string,string>{{"knowledge",Phase7KnowledgeContract.Version}},a.RuntimeCompatibilityEvidence);
            return new(true,published,"P7KNOWLEDGE_VALID",[],v.Warnings);
        }
        catch(Exception ex) when(ex is JsonException or InvalidDataException or IOException)
        { return new(false,null,"P7KNOWLEDGE_PHYSICAL_READBACK_INVALID",[ex.Message],[]); }
    }
    public sealed record PublicationEvidence(string PublicationId,string AuthorityId,string AuthorityChecksum,
        string ValidationPhysicalSha256,bool ManifestEvidenceValid,bool PublicationCommitted,bool CommittedStateValidationPassed);
}

public sealed class Phase7KnowledgeRecoveryService(IPhase7KnowledgeFileSystem fs) : IPhase7KnowledgeRecoveryService
{
    public Task<Phase7KnowledgeExecutionResult?> RecoverAsync(string root,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();root=Path.GetFullPath(root);
        if(!fs.DirectoryExists(root))return Task.FromResult<Phase7KnowledgeExecutionResult?>(null);
        foreach(var p in Directory.EnumerateDirectories(root,".phase-07-knowledge-*-staging"))fs.DeleteDirectory(p);
        return Task.FromResult<Phase7KnowledgeExecutionResult?>(null);
    }
}

public sealed class Phase7KnowledgeService(IPhase7KnowledgeTransactionCoordinator coordinator) : IPhase7KnowledgeService
{
    public Task<Phase7KnowledgeExecutionResult> ExecuteAsync(Phase7InputAuthorityRequest request,bool overwriteExisting=false,CancellationToken token=default)
        =>coordinator.ExecuteAsync(request,overwriteExisting,token);
}

public sealed class Phase7KnowledgeTransactionCoordinator(IPhase7KnowledgeExecutionLock executionLock,
    IPhase7KnowledgeRecoveryService recovery, IPhase7KnowledgeCommittedStateEvaluator committed,
    IPhase7InputAuthorityEvaluator inputEvaluator, IPhase7CertifiedKnowledgeSource source,
    IPhase7KnowledgeResolver knowledgeResolver,
    IPhase7KnowledgeAuthorityBuilder builder, IPhase7KnowledgeAuthorityValidator validator,
    IPhase7KnowledgePhysicalReadback readback, IPhase7KnowledgeFileSystem fs) : IPhase7KnowledgeTransactionCoordinator
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public async Task<Phase7KnowledgeExecutionResult> ExecuteAsync(Phase7InputAuthorityRequest request,bool overwriteExisting,CancellationToken token=default)
    {
        var key=Phase7Determinism.Hash(new{root=Path.GetFullPath(request.ExecutionRoot),request.PlanId,request.EventId,request.Language,component="P7.1A"});
        await using var held=await executionLock.AcquireAsync(key,token);await recovery.RecoverAsync(request.ExecutionRoot,token);
        var prior=await committed.EvaluateAsync(new(request.ExecutionRoot,request.ExecutionId,request.PlanId,request.EventId,request.Language),token);
        if(!overwriteExisting&&prior.IsValid)
            return new(true,"Skipped","P7KNOWLEDGE_REUSE_VALID",request.ExecutionRoot,prior.Authority!.KnowledgeAuthority.AuthorityId,true,true,true,null,null,[],prior.Warnings);
        var input=await inputEvaluator.EvaluateAsync(request,token);
        if(!input.IsValid||input.Authority is null)return Fail(request,input.ReasonCode,input.Errors,input.Warnings);
        var payload=await source.ResolveResultAsync(request.EventId,request.Language,token);
        if(!payload.IsValid||payload.Payload is null)return Fail(request,payload.ReasonCode,payload.Errors,payload.Warnings);
        var resolvedKnowledge=knowledgeResolver.Resolve(payload.Payload,input.Authority.FamilyProfile);
        var a=builder.Build(input.Authority,payload.Payload,resolvedKnowledge,input.Authority.FamilyProfile,input.Authority.RuntimeProviderCompatibilityMetadata);
        var d=Diagnostics(a,resolvedKnowledge,payload.Payload,input.Authority.InputArtifactPaths);
        var memory=validator.Validate(a,resolvedKnowledge,d);
        if(!memory.IsValid)return new(false,"Failed",memory.ReasonCode,request.ExecutionRoot,a.AuthorityId,false,false,false,memory,d,memory.Errors,memory.Warnings);
        var tx=Guid.NewGuid().ToString("N");var paths=Phase7KnowledgeTransactionPaths.Create(request.ExecutionRoot,tx);
        var stageKnowledge=paths.StagingKnowledgeDirectory;var stageValidation=paths.CandidateValidationPath;
        var stageRoot=Directory.GetParent(Directory.GetParent(stageKnowledge)!.FullName)!.FullName;
        var stableKnowledge=paths.StableKnowledgeDirectory;var stableValidation=paths.StableValidationPath;
        var backup=Directory.GetParent(paths.BackupKnowledgeDirectory)!.FullName;
        var backedUp=false;var swapped=false;var evidenceBackedUp=false;
        var marker=new Phase7KnowledgeTransactionMarker(Phase7KnowledgeContract.Version,tx,request.ExecutionId,request.PlanId,request.EventId,request.Language,
            Phase7KnowledgeTransactionState.Created,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,stageKnowledge,stableKnowledge,paths.BackupKnowledgeDirectory,
            stageValidation,stableValidation,paths.BackupValidationPath,paths.StableManifestPath,paths.BackupManifestPath,"",[],a.AuthorityId,prior.Authority?.KnowledgeAuthority.AuthorityId??"","");
        async Task Mark(Phase7KnowledgeTransactionState state,CancellationToken ct)
        {
            marker=marker with{State=state,UpdatedUtc=DateTimeOffset.UtcNow,DeterministicChecksum=""};
            marker=marker with{DeterministicChecksum=Phase7Determinism.Hash(marker)};
            await Write(paths.TransactionMarkerPath,marker,ct);
        }
        try
        {
            await Mark(Phase7KnowledgeTransactionState.Created,token);
            fs.CreateDirectory(stageKnowledge);fs.CreateDirectory(Path.GetDirectoryName(stageValidation)!);
            await Write(Path.Combine(stageKnowledge,"knowledge-authority.json"),a,token);
            await Write(Path.Combine(stageKnowledge,"knowledge-resolution-report.json"),resolvedKnowledge,token);
            await Write(Path.Combine(stageKnowledge,"knowledge-diagnostics.json"),d,token);
            await Mark(Phase7KnowledgeTransactionState.CandidateWritten,token);
            var inventory=await readback.CreateCandidateInventoryAsync(stageKnowledge,a,token);
            var seed=memory with{Mode=Phase7KnowledgeValidationMode.StagedPhysical,ArtifactInventory=inventory,DeterministicChecksum=""};seed=seed with{DeterministicChecksum=Phase7Determinism.Hash(seed)};
            await Write(stageValidation,seed,token);
            var rb=await readback.ValidateCandidateCompleteSetAsync(stageRoot,a,inventory,token);
            await Mark(Phase7KnowledgeTransactionState.CandidateReadbackPassed,token);
            var staged=validator.Validate(a,resolvedKnowledge,d,Phase7KnowledgeValidationMode.StagedPhysical,rb);
            if(!staged.IsValid)throw new InvalidDataException(staged.ReasonCode);
            await Write(stageValidation,staged,token);
            await Mark(Phase7KnowledgeTransactionState.CandidateValidated,token);
            fs.CreateDirectory(backup);if(fs.DirectoryExists(stableKnowledge))fs.MoveDirectory(stableKnowledge,paths.BackupKnowledgeDirectory);
            if(fs.FileExists(stableValidation)){fs.CreateDirectory(Path.GetDirectoryName(paths.BackupValidationPath)!);fs.MoveFile(stableValidation,paths.BackupValidationPath);}
            if(fs.FileExists(paths.StablePublicationEvidencePath)){fs.MoveFile(paths.StablePublicationEvidencePath,paths.BackupPublicationEvidencePath);evidenceBackedUp=true;}
            backedUp=true;await Mark(Phase7KnowledgeTransactionState.PreviousStateBackedUp,token);
            fs.CreateDirectory(Path.GetDirectoryName(stableKnowledge)!);fs.MoveDirectory(stageKnowledge,stableKnowledge);
            swapped=true;await Mark(Phase7KnowledgeTransactionState.AuthoritySwapped,token);
            fs.CreateDirectory(Path.GetDirectoryName(stableValidation)!);fs.MoveFile(stageValidation,stableValidation,true);
            await Mark(Phase7KnowledgeTransactionState.ValidationPublished,token);
            var validationBytes=await fs.ReadAllBytesAsync(stableValidation,token);var evidence=new Phase7KnowledgeCommittedStateEvaluator.PublicationEvidence(
                "p7kp-"+a.AuthorityId,a.AuthorityId,a.SemanticChecksum,Convert.ToHexString(SHA256.HashData(validationBytes)).ToLowerInvariant(),true,true,true);
            await Write(paths.StablePublicationEvidencePath,evidence,token);
            await Mark(Phase7KnowledgeTransactionState.ManifestPublished,token);
            var final=await committed.EvaluateAsync(new(request.ExecutionRoot,request.ExecutionId,request.PlanId,request.EventId,request.Language),token);
            if(!final.IsValid)throw new InvalidDataException(string.Join(';',final.Errors));
            await Mark(Phase7KnowledgeTransactionState.CommittedReadbackPassed,token);
            fs.DeleteDirectory(backup);fs.DeleteDirectory(stageRoot);
            await Mark(Phase7KnowledgeTransactionState.Completed,token);fs.DeleteFile(paths.TransactionMarkerPath);
            return new(true,"Succeeded","P7KNOWLEDGE_COMMITTED",request.ExecutionRoot,a.AuthorityId,false,true,true,staged,d,[],a.Warnings);
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or JsonException or OperationCanceledException)
        {
            try { await Mark(Phase7KnowledgeTransactionState.RollingBack,CancellationToken.None);
                if(swapped&&backedUp){if(fs.DirectoryExists(stableKnowledge))fs.DeleteDirectory(stableKnowledge);if(fs.DirectoryExists(paths.BackupKnowledgeDirectory))fs.MoveDirectory(paths.BackupKnowledgeDirectory,stableKnowledge);
                    if(fs.FileExists(stableValidation))fs.DeleteFile(stableValidation);if(fs.FileExists(paths.BackupValidationPath))fs.MoveFile(paths.BackupValidationPath,stableValidation,true);
                    if(fs.FileExists(paths.StablePublicationEvidencePath))fs.DeleteFile(paths.StablePublicationEvidencePath);if(evidenceBackedUp&&fs.FileExists(paths.BackupPublicationEvidencePath))fs.MoveFile(paths.BackupPublicationEvidencePath,paths.StablePublicationEvidencePath,true);}
                fs.DeleteDirectory(stageRoot);fs.DeleteFile(paths.TransactionMarkerPath); }
            catch(Exception rollback){return new(false,"Failed","P7KNOWLEDGE_ROLLBACK_FAILED",request.ExecutionRoot,a.AuthorityId,false,false,false,null,d,[ex.Message,rollback.Message],a.Warnings);}
            return new(false,"Failed","P7KNOWLEDGE_TRANSACTION_FAILED",request.ExecutionRoot,a.AuthorityId,false,false,false,null,d,[ex.Message],a.Warnings);
        }
    }
    private Task Write<T>(string p,T value,CancellationToken t)=>fs.WriteAllTextAsync(p,JsonSerializer.Serialize(value,Json),t);
    private static Phase7KnowledgeExecutionResult Fail(Phase7InputAuthorityRequest r,string code,IReadOnlyList<string> e,IReadOnlyList<string> w)=>new(false,"Failed",code,r.ExecutionRoot,"",false,false,false,null,null,e,w);
    private static Phase7KnowledgeDiagnostics Diagnostics(Phase7KnowledgeAuthority a,ResolvedNarrationKnowledge r,CertifiedKnowledgePayload p,IReadOnlyList<string> inputs)
    {
        var claims=a.Claims;var ev=a.ClaimSupportEvidence;int M(Phase7KnowledgeMergeClassification c)=>a.MergeDecisions.Count(x=>x.Classification==c);
        var draft=new Phase7KnowledgeDiagnostics(Phase7KnowledgeContract.Version,a.ExecutionId,a.PlanId,a.EventId,a.AuthorityId,a.EventFamily,a.Language,a.ProfileId,a.ProfileVersion,
            true,!string.IsNullOrEmpty(p.EvergreenPayloadId),p.VerificationStatus is "Verified" or "Certified",string.IsNullOrEmpty(p.EvergreenPayloadId)||!string.IsNullOrEmpty(p.EvergreenChecksum),
            !string.IsNullOrEmpty(a.SourceRegistryChecksum),ev.All(x=>x.SourceEligibility is not Phase7SourceEligibility.Rejected),a.AdapterDiagnostics.Count>0,
            claims.Select(x=>x.ClaimId).Distinct().Count()==claims.Count,ev.All(x=>!string.IsNullOrEmpty(x.SourceId)),a.MandatoryDomains.All(x=>r.Domains.Any(y=>y.Domain==x&&y.Status==KnowledgeDomainStatus.Available)),
            a.MergeDecisions.All(x=>x.BlockingIssues.Count==0),M(Phase7KnowledgeMergeClassification.Contradictory)==0,false,a.KnowledgeEntities.Count,a.AdapterDiagnostics.Sum(x=>x.ExtractedClaimCount),claims.Count,claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Deferred),0,claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Required),
            ev.Count(x=>x.ProvenancePrecision==Phase7ProvenancePrecision.ExactClaim),ev.Count(x=>x.ProvenancePrecision==Phase7ProvenancePrecision.ExactKnowledgeEntity),ev.Count(x=>x.ProvenancePrecision==Phase7ProvenancePrecision.ExactApprovedField),a.AdapterDiagnostics.Sum(x=>x.UnsupportedClaimCount),
            M(Phase7KnowledgeMergeClassification.Equivalent),M(Phase7KnowledgeMergeClassification.EventSpecificSpecialization),M(Phase7KnowledgeMergeClassification.EventMorePrecise),M(Phase7KnowledgeMergeClassification.EvergreenMorePrecise),M(Phase7KnowledgeMergeClassification.Contradictory),M(Phase7KnowledgeMergeClassification.Incomparable),
            p.AllResolvedSources.Count,p.CertifiedSupportingSources.Count,p.AllResolvedSources.Count(x=>x.Reviewed&&!x.Certified),p.RejectedSources.Count,p.UnverifiedSources.Count,
            r.UnknownSections.Count,r.UnknownProperties.Count,a.Warnings.Count,a.BlockingIssues.Count,inputs,["07-narration/knowledge/knowledge-authority.json","07-narration/knowledge/knowledge-resolution-report.json","07-narration/knowledge/knowledge-diagnostics.json"],"");
        draft=draft with{AcceptedRequiredCount=claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Required),AcceptedOptionalCount=claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Optional)};
        var reconciled=draft.AcceptedClaimCount==claims.Count&&draft.RequiredClaimCount==draft.AcceptedRequiredCount&&
            draft.DeferredClaimCount==claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Deferred)&&draft.WarningCount==a.Warnings.Count&&draft.BlockingIssueCount==a.BlockingIssues.Count&&
            draft.KnowledgeEntityCount==a.KnowledgeEntities.Count&&draft.UnknownSectionCount==r.UnknownSections.Count&&draft.UnknownPropertyCount==r.UnknownProperties.Count;
        draft=draft with{DiagnosticsReconciled=reconciled};
        return draft with{DeterministicChecksum=Phase7Determinism.Hash(draft)};
    }
}
