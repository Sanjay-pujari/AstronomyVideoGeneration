using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7KnowledgeFileSystem : IPhase7KnowledgeFileSystem
{
    public bool FileExists(string p)=>File.Exists(p); public bool DirectoryExists(string p)=>Directory.Exists(p);
    public void CreateDirectory(string p)=>Directory.CreateDirectory(p);
    public Task<string> ReadAllTextAsync(string p,CancellationToken t=default)=>File.ReadAllTextAsync(p,t);
    public Task<byte[]> ReadAllBytesAsync(string p,CancellationToken t=default)=>File.ReadAllBytesAsync(p,t);
    public async Task WriteAllTextAsync(string p,string c,CancellationToken t=default)
    { var directory=Path.GetDirectoryName(p);if(!string.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);var temp=p+".tmp-"+Guid.NewGuid().ToString("N");await File.WriteAllTextAsync(temp,c,t);File.Move(temp,p,true); }
    public async Task WriteAllBytesAsync(string p,byte[] c,CancellationToken t=default)
    { var directory=Path.GetDirectoryName(p);if(!string.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);var temp=p+".tmp-"+Guid.NewGuid().ToString("N");await File.WriteAllBytesAsync(temp,c,t);File.Move(temp,p,true); }
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
    private static readonly IReadOnlyDictionary<string,Type> Types=Set.ToDictionary(x=>"07-narration/knowledge/"+x.Name,x=>x.Type,StringComparer.Ordinal);
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
        var duplicates=inventory.Artifacts.GroupBy(x=>x.RelativePath,StringComparer.Ordinal).Where(x=>x.Count()>1).Select(x=>x.Key).ToHashSet(StringComparer.Ordinal);
        foreach(var entry in inventory.Artifacts)
        {
            var safe=Safe(entry.RelativePath);var path=safe?Path.Combine(root,entry.RelativePath.Replace('/',Path.DirectorySeparatorChar)):"";
            var local=new List<string>();byte[] bytes=[];object? value=null;
            if(duplicates.Contains(entry.RelativePath))local.Add("P7KNOWLEDGE_DUPLICATE_ARTIFACT");
            if(!Types.TryGetValue(entry.RelativePath,out var artifactType))local.Add("P7KNOWLEDGE_UNEXPECTED_ARTIFACT");
            if(!safe)local.Add("P7KNOWLEDGE_UNSAFE_PATH"); else if(!fs.FileExists(path))local.Add("P7KNOWLEDGE_ARTIFACT_MISSING");
            else if(artifactType is not null) { bytes=await fs.ReadAllBytesAsync(path,token);try{value=JsonSerializer.Deserialize(bytes,artifactType,Json);}catch(JsonException ex){local.Add(ex.Message);} }
            var identity=value switch{Phase7KnowledgeAuthority x=>x.AuthorityId==a.AuthorityId&&x.ExecutionId==a.ExecutionId&&x.PlanId==a.PlanId&&x.EventId==a.EventId,
                ResolvedNarrationKnowledge x=>x.PayloadId==a.EventKnowledgePayloadId,Phase7KnowledgeDiagnostics x=>x.AuthorityId==a.AuthorityId,_=>false};
            var semantic=value is not null&&Semantic(value)==entry.SemanticChecksum;var hash=bytes.Length==0?"":Sha(bytes);
            if(bytes.Length>0&&(hash!=entry.PhysicalSha256||bytes.LongLength!=entry.SizeBytes))local.Add("P7KNOWLEDGE_PHYSICAL_HASH_OR_SIZE_MISMATCH");
            if(!identity)local.Add("P7KNOWLEDGE_IDENTITY_MISMATCH");if(!semantic)local.Add("P7KNOWLEDGE_SEMANTIC_CHECKSUM_MISMATCH");
            var lineage=value is not null&&Lineage(value,a,entry);
            if(!lineage)local.Add("P7KNOWLEDGE_LINEAGE_MISMATCH");
            all.Add(new(entry.RelativePath,bytes.Length>0,bytes.LongLength,hash,value is not null,entry.ContractType,entry.ContractVersion,identity,semantic,lineage,safe,local));errors.AddRange(local);
        }
        foreach(var expected in Types.Keys.Where(x=>!inventory.Artifacts.Any(y=>y.RelativePath==x)))errors.Add("P7KNOWLEDGE_ARTIFACT_MISSING");
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
    private static bool Lineage(object value,Phase7KnowledgeAuthority a,Phase7KnowledgeArtifactInventoryEntry entry)
    {
        var inventoryLineage=entry.LineageChecksum==Phase7Determinism.Hash(new{a.SourcePhase4Checksum,a.SourcePhase5PublicationId,a.SourcePhase6AuthorityChecksum});
        return inventoryLineage&&Identity(value,a)&&(value switch
        {
            Phase7KnowledgeAuthority x=>x.EventKnowledgePayloadId==a.EventKnowledgePayloadId&&x.EventKnowledgeChecksum==a.EventKnowledgeChecksum&&x.EvergreenPayloadId==a.EvergreenPayloadId&&x.EvergreenChecksum==a.EvergreenChecksum&&x.SourceRegistryId==a.SourceRegistryId&&x.SourceRegistryChecksum==a.SourceRegistryChecksum,
            ResolvedNarrationKnowledge x=>x.PayloadId==a.EventKnowledgePayloadId&&x.PayloadChecksum==a.EventKnowledgeChecksum&&x.SourceRegistryId==a.SourceRegistryId&&x.SourceRegistryChecksum==a.SourceRegistryChecksum&&x.Language==a.Language,
            Phase7KnowledgeDiagnostics x=>x.ExecutionId==a.ExecutionId&&x.PlanId==a.PlanId&&x.EventId==a.EventId&&x.AuthorityId==a.AuthorityId,
            _=>false
        });
    }
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
        var validationPath=Path.Combine(root,"validation","phase-07-knowledge-validation.json");var evidencePath=Path.Combine(root,".phase-07-knowledge-publication.json");var manifestPath=Path.Combine(root,"phase-manifest.json");
        if(!fs.FileExists(authorityPath)||!fs.FileExists(validationPath)||!fs.FileExists(evidencePath)||!fs.FileExists(manifestPath))
            return new(false,null,"P7KNOWLEDGE_COMPLETE_SET_INVALID",["A required committed artifact or external publication evidence is missing."],[]);
        try
        {
            var a=JsonSerializer.Deserialize<Phase7KnowledgeAuthority>(await fs.ReadAllTextAsync(authorityPath,token),Json)!;
            var v=JsonSerializer.Deserialize<Phase7KnowledgeValidation>(await fs.ReadAllTextAsync(validationPath,token),Json)!;
            var e=JsonSerializer.Deserialize<Phase7KnowledgePublicationEvidence>(await fs.ReadAllTextAsync(evidencePath,token),Json)!;
            var manifest=JsonNode.Parse(await fs.ReadAllTextAsync(manifestPath,token))?.AsObject()??throw new JsonException("Manifest root missing.");
            var entries=manifest["phase7KnowledgeAuthorities"]?.Deserialize<Phase7KnowledgeManifestEntry[]>(Json)??[];
            var matchingEntries=entries.Where(x=>x.PhaseNo==7&&x.PhaseComponent=="Knowledge Authority").ToArray();
            var me=matchingEntries.Length==1?matchingEntries[0]:null;
            var errors=new List<string>();
            if(a.ExecutionId!=request.ExecutionId||a.PlanId!=request.PlanId||a.EventId!=request.EventId||a.Language!=request.Language)errors.Add("P7KNOWLEDGE_IDENTITY_MISMATCH");
            if(a.SemanticChecksum!=Phase7Determinism.Hash(a with{SemanticChecksum=""}))errors.Add("P7KNOWLEDGE_AUTHORITY_CHECKSUM_INVALID");
            if(v.ArtifactInventory is null||v.ArtifactInventory.Artifacts.Count!=3)errors.Add("P7KNOWLEDGE_INVENTORY_INVALID");
            if(!v.IsValid||v.Mode!=Phase7KnowledgeValidationMode.CommittedPhysical)errors.Add("P7KNOWLEDGE_VALIDATION_INVALID");
            var evidenceChecksum=Phase7Determinism.Hash(e with{DeterministicChecksum=""});
            if(e.ContractVersion!=Phase7KnowledgeContract.Version||string.IsNullOrWhiteSpace(e.PublicationId)||e.ExecutionId!=request.ExecutionId||e.PlanId!=request.PlanId||e.EventId!=request.EventId||!string.Equals(e.Language,request.Language,StringComparison.OrdinalIgnoreCase)||e.AuthorityId!=a.AuthorityId||e.AuthorityChecksum!=a.SemanticChecksum||!e.PublicationCommitted||!e.CommittedStateValidationPassed||e.DeterministicChecksum!=evidenceChecksum)errors.Add("P7KNOWLEDGE_PUBLICATION_EVIDENCE_INVALID");
            if(me is null||me.ContractVersion!=Phase7KnowledgeContract.Version||me.Status!="Succeeded"||me.ReasonCode!="P7KNOWLEDGE_COMMITTED"||!me.PublicationCommitted||!me.CommittedStateValidationPassed||me.AuthorityId!=a.AuthorityId||me.AuthorityChecksum!=a.SemanticChecksum||me.PublicationId!=e.PublicationId||me.ValidationPhysicalSha256!=e.ValidationPhysicalSha256||me.DeterministicChecksum!=Phase7Determinism.Hash(me with{DeterministicChecksum=""})||e.ManifestEntryChecksum!=me.DeterministicChecksum)errors.Add("P7KNOWLEDGE_MANIFEST_INVALID");
            if(errors.Count>0)return new(false,null,errors[0],errors,[]);
            var physical=await readback.ValidateCommittedCompleteSetAsync(root,a,v.ArtifactInventory!,e.ValidationPhysicalSha256,me is not null,token);
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
}

public sealed class Phase7KnowledgeRecoveryService(IPhase7KnowledgeFileSystem fs) : IPhase7KnowledgeRecoveryService
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public async Task<Phase7KnowledgeExecutionResult?> RecoverAsync(string root,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();root=Path.GetFullPath(root);
        if(!fs.DirectoryExists(root))return null;
        foreach(var markerPath in Directory.EnumerateFiles(root,".phase-07-knowledge-*-transaction.json",SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            Phase7KnowledgeTransactionMarker marker;
            try
            {
                marker=JsonSerializer.Deserialize<Phase7KnowledgeTransactionMarker>(await fs.ReadAllTextAsync(markerPath,token),Json)??throw new InvalidDataException("Marker is empty.");
                if(marker.ContractVersion!=Phase7KnowledgeContract.Version||marker.DeterministicChecksum!=Phase7Determinism.Hash(marker with{DeterministicChecksum=""}))
                    return Block(markerPath,marker?.TransactionId??"","P7KNOWLEDGE_INVALID_MARKER_CHECKSUM");
                var paths=new[]{markerPath,marker.StagingKnowledgeDirectory,marker.StableKnowledgeDirectory,marker.BackupKnowledgeDirectory,marker.CandidateValidationPath,marker.StableValidationPath,marker.BackupValidationPath,marker.StableManifestPath,marker.BackupManifestPath,marker.StablePublicationEvidencePath,marker.BackupPublicationEvidencePath};
                if(paths.Where(x=>!string.IsNullOrWhiteSpace(x)).Any(x=>!Inside(root,x)))return Block(markerPath,marker.TransactionId,"P7KNOWLEDGE_UNSAFE_MARKER_PATH");
                if(marker.State==Phase7KnowledgeTransactionState.RollbackFailed)return Block(markerPath,marker.TransactionId,"P7KNOWLEDGE_ROLLBACK_FAILED");
                if(marker.State is Phase7KnowledgeTransactionState.Created or Phase7KnowledgeTransactionState.CandidateWritten or Phase7KnowledgeTransactionState.CandidateReadbackPassed or Phase7KnowledgeTransactionState.CandidateValidated)
                { CleanupCandidate(marker);fs.DeleteFile(markerPath);continue; }
                if(marker.State is Phase7KnowledgeTransactionState.CommittedReadbackPassed or Phase7KnowledgeTransactionState.Completed)
                { CleanupCandidate(marker);DeleteBackup(marker);fs.DeleteFile(markerPath);continue; }
                await Rollback(marker,markerPath,token);fs.DeleteFile(markerPath);
            }
            catch(Exception ex) when(ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            { var failed=fs.FileExists(markerPath)&&(await fs.ReadAllTextAsync(markerPath,token)).Contains("rollbackFailed",StringComparison.OrdinalIgnoreCase);return Block(markerPath,"",failed?"P7KNOWLEDGE_ROLLBACK_FAILED: "+ex.Message:ex.Message); }
        }
        return null;
    }
    private async Task Rollback(Phase7KnowledgeTransactionMarker m,string markerPath,CancellationToken token)
    {
        var errors=new List<string>();
        RestoreDirectory(m.PreviousKnowledgeDirectoryExisted,m.StableKnowledgeDirectory,m.BackupKnowledgeDirectory,errors);
        RestoreFile(m.PreviousValidationExisted,m.StableValidationPath,m.BackupValidationPath,errors);
        RestoreFile(m.PreviousManifestExisted,m.StableManifestPath,m.BackupManifestPath,errors);
        RestoreFile(m.PreviousPublicationEvidenceExisted,m.StablePublicationEvidencePath,m.BackupPublicationEvidencePath,errors);
        Verify(m,errors);CleanupCandidate(m);
        if(errors.Count==0)return;
        var failed=m with{State=Phase7KnowledgeTransactionState.RollbackFailed,RollbackErrors=m.RollbackErrors.Concat(errors).ToArray(),UpdatedUtc=DateTimeOffset.UtcNow,DeterministicChecksum=""};
        failed=failed with{DeterministicChecksum=Phase7Determinism.Hash(failed)};
        await fs.WriteAllTextAsync(markerPath,JsonSerializer.Serialize(failed,Json),token);throw new InvalidDataException(string.Join("; ",errors));
    }
    private void RestoreDirectory(bool existed,string stable,string backup,List<string> errors){try{if(fs.DirectoryExists(stable))fs.DeleteDirectory(stable);if(existed){if(!fs.DirectoryExists(backup))throw new IOException("Required knowledge backup is missing.");fs.CreateDirectory(Path.GetDirectoryName(stable)!);fs.MoveDirectory(backup,stable);}}catch(Exception e){errors.Add(e.Message);}}
    private void RestoreFile(bool existed,string stable,string backup,List<string> errors){try{if(fs.FileExists(stable))fs.DeleteFile(stable);if(existed){if(!fs.FileExists(backup))throw new IOException($"Required backup is missing: {backup}");fs.CreateDirectory(Path.GetDirectoryName(stable)!);fs.MoveFile(backup,stable,true);}}catch(Exception e){errors.Add(e.Message);}}
    private void Verify(Phase7KnowledgeTransactionMarker m,List<string> errors)
    { var authorityPath=Path.Combine(m.StableKnowledgeDirectory,"knowledge-authority.json");Check(m.PreviousKnowledgeDirectoryExisted,m.StableKnowledgeDirectory,"",authorityPath,errors);if(m.PreviousKnowledgeDirectoryExisted&&!string.IsNullOrEmpty(m.PreviousAuthorityChecksum)){try{var a=JsonSerializer.Deserialize<Phase7KnowledgeAuthority>(fs.ReadAllTextAsync(authorityPath).GetAwaiter().GetResult(),Json);if(a?.AuthorityId!=m.PreviousAuthorityId||a.SemanticChecksum!=m.PreviousAuthorityChecksum)errors.Add("Restored authority identity or checksum mismatch.");}catch(Exception e){errors.Add(e.Message);}}Check(m.PreviousValidationExisted,m.StableValidationPath,m.PreviousValidationPhysicalSha256,m.StableValidationPath,errors);Check(m.PreviousManifestExisted,m.StableManifestPath,m.PreviousManifestPhysicalSha256,m.StableManifestPath,errors);Check(m.PreviousPublicationEvidenceExisted,m.StablePublicationEvidencePath,m.PreviousPublicationEvidencePhysicalSha256,m.StablePublicationEvidencePath,errors); }
    private void Check(bool existed,string component,string hash,string file,List<string> errors){var present=fs.FileExists(file)||fs.DirectoryExists(component);if(present!=existed){errors.Add($"Restoration existence mismatch: {component}");return;}if(existed&&!string.IsNullOrEmpty(hash)&&fs.FileExists(file)){var actual=Sha(fs.ReadAllBytesAsync(file).GetAwaiter().GetResult());if(actual!=hash)errors.Add($"Restoration checksum mismatch: {file}");}}
    private void CleanupCandidate(Phase7KnowledgeTransactionMarker m){var root=Directory.GetParent(Directory.GetParent(m.StagingKnowledgeDirectory)!.FullName)!.FullName;if(fs.DirectoryExists(root))fs.DeleteDirectory(root);}
    private void DeleteBackup(Phase7KnowledgeTransactionMarker m){var root=Directory.GetParent(m.BackupKnowledgeDirectory)!.FullName;if(fs.DirectoryExists(root))fs.DeleteDirectory(root);}
    private static bool Inside(string root,string path){var full=Path.GetFullPath(path);return full==root||full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal);}
    private static string Sha(byte[] b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    private static Phase7KnowledgeExecutionResult Block(string marker,string tx,string error)=>new(false,"Failed",error.StartsWith("P7KNOWLEDGE_ROLLBACK_FAILED",StringComparison.Ordinal)?"P7KNOWLEDGE_ROLLBACK_FAILED":error.StartsWith("P7KNOWLEDGE_",StringComparison.Ordinal)?error:"P7KNOWLEDGE_RECOVERY_FAILED",Path.GetDirectoryName(marker)??"","",false,false,false,null,null,[error,$"MarkerPath={marker}",$"TransactionId={tx}"],[]);
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
        await using var held=await executionLock.AcquireAsync(key,token);var recoveryResult=await recovery.RecoverAsync(request.ExecutionRoot,token);
        if(recoveryResult is not null)return recoveryResult;
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
        var previousKnowledge=fs.DirectoryExists(stableKnowledge);var previousValidation=fs.FileExists(stableValidation);
        var previousManifest=fs.FileExists(paths.StableManifestPath);var previousEvidence=fs.FileExists(paths.StablePublicationEvidencePath);
        var marker=new Phase7KnowledgeTransactionMarker(Phase7KnowledgeContract.Version,tx,request.ExecutionId,request.PlanId,request.EventId,request.Language,
            Phase7KnowledgeTransactionState.Created,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,stageKnowledge,stableKnowledge,paths.BackupKnowledgeDirectory,
            stageValidation,stableValidation,paths.BackupValidationPath,paths.StableManifestPath,paths.BackupManifestPath,"",[],a.AuthorityId,prior.Authority?.KnowledgeAuthority.AuthorityId??"","")
        { PreviousKnowledgeDirectoryExisted=previousKnowledge,PreviousValidationExisted=previousValidation,PreviousManifestExisted=previousManifest,
          PreviousPublicationEvidenceExisted=previousEvidence,StablePublicationEvidencePath=paths.StablePublicationEvidencePath,
          BackupPublicationEvidencePath=paths.BackupPublicationEvidencePath,PreviousAuthorityChecksum=prior.Authority?.KnowledgeAuthority.SemanticChecksum??"",
          PreviousValidationPhysicalSha256=previousValidation?Sha(await fs.ReadAllBytesAsync(stableValidation,token)):"",
          PreviousManifestPhysicalSha256=previousManifest?Sha(await fs.ReadAllBytesAsync(paths.StableManifestPath,token)):"",
          PreviousPublicationEvidencePhysicalSha256=previousEvidence?Sha(await fs.ReadAllBytesAsync(paths.StablePublicationEvidencePath,token)):"" };
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
            fs.CreateDirectory(backup);
            if(previousKnowledge)CopyDirectory(stableKnowledge,paths.BackupKnowledgeDirectory);
            if(previousValidation){fs.CreateDirectory(Path.GetDirectoryName(paths.BackupValidationPath)!);fs.CopyFile(stableValidation,paths.BackupValidationPath,true);}
            if(previousManifest)fs.CopyFile(paths.StableManifestPath,paths.BackupManifestPath,true);
            if(previousEvidence)fs.CopyFile(paths.StablePublicationEvidencePath,paths.BackupPublicationEvidencePath,true);
            await Mark(Phase7KnowledgeTransactionState.PreviousStateBackedUp,token);
            if(previousKnowledge)fs.DeleteDirectory(stableKnowledge);
            fs.CreateDirectory(Path.GetDirectoryName(stableKnowledge)!);fs.MoveDirectory(stageKnowledge,stableKnowledge);
            await Mark(Phase7KnowledgeTransactionState.AuthoritySwapped,token);
            fs.CreateDirectory(Path.GetDirectoryName(stableValidation)!);fs.MoveFile(stageValidation,stableValidation,true);
            await Mark(Phase7KnowledgeTransactionState.ValidationPublished,token);

            var committedReadback=await readback.ValidateCommittedCompleteSetAsync(request.ExecutionRoot,a,inventory,Sha(await fs.ReadAllBytesAsync(stableValidation,token)),true,token);
            var committedValidation=validator.Validate(a,resolvedKnowledge,d,Phase7KnowledgeValidationMode.CommittedPhysical,committedReadback);
            if(!committedValidation.IsValid)throw new InvalidDataException(committedValidation.ReasonCode);
            await Write(stableValidation,committedValidation,token);
            var validationHash=Sha(await fs.ReadAllBytesAsync(stableValidation,token));var publicationId="p7kp-"+a.AuthorityId;
            var manifestEntry=new Phase7KnowledgeManifestEntry(7,"Knowledge Authority","Succeeded","P7KNOWLEDGE_COMMITTED",true,true,a.AuthorityId,a.SemanticChecksum,validationHash,publicationId,Phase7KnowledgeContract.Version,"");
            manifestEntry=manifestEntry with{DeterministicChecksum=Phase7Determinism.Hash(manifestEntry)};
            JsonObject manifest;
            if(previousManifest)manifest=JsonNode.Parse(await fs.ReadAllTextAsync(paths.BackupManifestPath,token))?.AsObject()??throw new JsonException("Existing phase manifest is invalid.");
            else manifest=new JsonObject();
            var old=manifest["phase7KnowledgeAuthorities"]?.Deserialize<Phase7KnowledgeManifestEntry[]>(Json)??[];
            manifest["phase7KnowledgeAuthorities"]=JsonSerializer.SerializeToNode(old.Where(x=>x.PhaseComponent!="Knowledge Authority").Append(manifestEntry).ToArray(),Json);
            await fs.WriteAllTextAsync(paths.StableManifestPath,manifest.ToJsonString(Json),token);
            var evidence=new Phase7KnowledgePublicationEvidence(Phase7KnowledgeContract.Version,publicationId,request.ExecutionId,request.PlanId,request.EventId,request.Language,a.AuthorityId,a.SemanticChecksum,validationHash,manifestEntry.DeterministicChecksum,true,true,DateTimeOffset.UtcNow,"");
            evidence=evidence with{DeterministicChecksum=Phase7Determinism.Hash(evidence)};await Write(paths.StablePublicationEvidencePath,evidence,token);
            await Mark(Phase7KnowledgeTransactionState.ManifestPublished,token);
            var final=await committed.EvaluateAsync(new(request.ExecutionRoot,request.ExecutionId,request.PlanId,request.EventId,request.Language),token);
            if(!final.IsValid)throw new InvalidDataException(string.Join(';',final.Errors));
            await Mark(Phase7KnowledgeTransactionState.CommittedReadbackPassed,token);
            fs.DeleteDirectory(backup);fs.DeleteDirectory(stageRoot);
            await Mark(Phase7KnowledgeTransactionState.Completed,token);fs.DeleteFile(paths.TransactionMarkerPath);
            return new(true,"Succeeded","P7KNOWLEDGE_COMMITTED",request.ExecutionRoot,a.AuthorityId,false,true,true,committedValidation,d,[],a.Warnings);
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or JsonException or OperationCanceledException)
        {
            marker=marker with{OriginalError=ex.ToString(),State=Phase7KnowledgeTransactionState.RollingBack,UpdatedUtc=DateTimeOffset.UtcNow,DeterministicChecksum=""};
            marker=marker with{DeterministicChecksum=Phase7Determinism.Hash(marker)};
            try{await Write(paths.TransactionMarkerPath,marker,CancellationToken.None);var rollback=await recovery.RecoverAsync(request.ExecutionRoot,CancellationToken.None);
                if(rollback is not null)return rollback with{Errors=[ex.ToString(),..rollback.Errors]};}
            catch(Exception rollback){return new(false,"Failed","P7KNOWLEDGE_ROLLBACK_FAILED",request.ExecutionRoot,a.AuthorityId,false,false,false,null,d,[ex.ToString(),rollback.ToString(),$"MarkerPath={paths.TransactionMarkerPath}",$"TransactionId={tx}"],a.Warnings);}
            return new(false,"Failed","P7KNOWLEDGE_TRANSACTION_FAILED",request.ExecutionRoot,a.AuthorityId,false,false,false,null,d,[ex.ToString()],a.Warnings);
        }
    }
    private Task Write<T>(string p,T value,CancellationToken t)=>fs.WriteAllTextAsync(p,JsonSerializer.Serialize(value,Json),t);
    private void CopyDirectory(string source,string destination){fs.CreateDirectory(destination);foreach(var path in fs.EnumerateOwnedPaths(source)){if(fs.DirectoryExists(path))CopyDirectory(path,Path.Combine(destination,Path.GetFileName(path)));else fs.CopyFile(path,Path.Combine(destination,Path.GetFileName(path)),true);}}
    private static string Sha(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
        int DomainCount(IEnumerable<string> names,KnowledgeDomainStatus status)=>names.Count(name=>r.Domains.Any(x=>x.Domain==name&&x.Status==status));
        bool Qualified(CertifiedNarrationClaim x)=>!x.RequiresQualification||ev.Any(e=>e.ClaimId==x.ClaimId&&!string.IsNullOrWhiteSpace(e.QualificationReason));
        bool Scoped(CertifiedNarrationClaim x)=>ev.Any(e=>e.ClaimId==x.ClaimId&&!string.IsNullOrWhiteSpace(e.AuthorityScope))||a.MergeDecisions.Any(m=>m.SelectedClaimIds.Contains(x.ClaimId)&&(m.EventScope.HasExplicitEvidence||m.EvergreenScope.HasExplicitEvidence));
        var locationSafe=claims.Where(x=>x.IsLocationDependent||x.IsDateTimeDependent).All(x=>Scoped(x)||(x.RequiresQualification&&Qualified(x)));
        var culturalSafe=claims.Where(x=>x.IsCultural||x.IsMythological).All(x=>(x.Domain is "CultureAndMythology" or "RegionalTraditions")&&Qualified(x)&&ev.Any(e=>e.ClaimId==x.ClaimId&&e.SourceEligibility is Phase7SourceEligibility.EligibleForRequiredClaim or Phase7SourceEligibility.EligibleForOptionalClaim));
        var astrologySafe=claims.Where(x=>x.IsAstrologyRelated).All(x=>x.Domain=="AstrologyClarification"&&x.RequiresQualification&&Qualified(x));
        draft=draft with{
            AcceptedRequiredCount=claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Required),AcceptedOptionalCount=claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Optional),
            HumanReviewClaimCount=claims.Count(x=>x.Disposition==Phase7ClaimDisposition.HumanReview),
            RequiredExactClaimCount=ev.Count(x=>claims.Any(c=>c.ClaimId==x.ClaimId&&c.Disposition==Phase7ClaimDisposition.Required)&&x.ProvenancePrecision==Phase7ProvenancePrecision.ExactClaim),
            RequiredExactEntityCount=ev.Count(x=>claims.Any(c=>c.ClaimId==x.ClaimId&&c.Disposition==Phase7ClaimDisposition.Required)&&x.ProvenancePrecision==Phase7ProvenancePrecision.ExactKnowledgeEntity),
            RequiredExactFieldCount=ev.Count(x=>claims.Any(c=>c.ClaimId==x.ClaimId&&c.Disposition==Phase7ClaimDisposition.Required)&&x.ProvenancePrecision==Phase7ProvenancePrecision.ExactApprovedField),
            OptionalAuthoritativeEvidenceCount=ev.Count(x=>claims.Any(c=>c.ClaimId==x.ClaimId&&c.Disposition==Phase7ClaimDisposition.Optional)&&x.SourceEligibility==Phase7SourceEligibility.EligibleForRequiredClaim),
            OptionalReviewedEvidenceCount=ev.Count(x=>claims.Any(c=>c.ClaimId==x.ClaimId&&c.Disposition is Phase7ClaimDisposition.Optional or Phase7ClaimDisposition.HumanReview)&&x.SourceEligibility==Phase7SourceEligibility.EligibleForOptionalClaim),
            NoProvenanceClaimCount=claims.Count(x=>x.ProvenancePrecision==nameof(Phase7ProvenancePrecision.None)),
            MandatoryAvailableDomainCount=DomainCount(a.MandatoryDomains,KnowledgeDomainStatus.Available),MandatoryHumanReviewDomainCount=DomainCount(a.MandatoryDomains,KnowledgeDomainStatus.RequiresHumanReview),MandatoryDeferredDomainCount=DomainCount(a.MandatoryDomains,KnowledgeDomainStatus.Deferred),MandatoryMissingDomainCount=DomainCount(a.MandatoryDomains,KnowledgeDomainStatus.Missing),
            OptionalAvailableDomainCount=DomainCount(a.OptionalDomains,KnowledgeDomainStatus.Available),OptionalHumanReviewDomainCount=DomainCount(a.OptionalDomains,KnowledgeDomainStatus.RequiresHumanReview),OptionalDeferredDomainCount=DomainCount(a.OptionalDomains,KnowledgeDomainStatus.Deferred),OptionalNotApplicableDomainCount=DomainCount(a.OptionalDomains,KnowledgeDomainStatus.NotApplicable),
            LocationTimeSafetyPassed=locationSafe,CulturalSafetyPassed=culturalSafe,AstrologySeparationPassed=astrologySafe};
        var differences=new List<string>();
        void Match(string name,int actual,int expected){if(actual!=expected)differences.Add($"{name}: expected={expected}; actual={actual}");}
        Match(nameof(draft.AcceptedClaimCount),draft.AcceptedClaimCount,claims.Count);Match(nameof(draft.RequiredClaimCount),draft.RequiredClaimCount,draft.AcceptedRequiredCount);
        Match(nameof(draft.DeferredClaimCount),draft.DeferredClaimCount,claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Deferred));Match(nameof(draft.WarningCount),draft.WarningCount,a.Warnings.Count);Match(nameof(draft.BlockingIssueCount),draft.BlockingIssueCount,a.BlockingIssues.Count);
        Match(nameof(draft.KnowledgeEntityCount),draft.KnowledgeEntityCount,a.KnowledgeEntities.Count);Match(nameof(draft.UnknownSectionCount),draft.UnknownSectionCount,r.UnknownSections.Count);Match(nameof(draft.UnknownPropertyCount),draft.UnknownPropertyCount,r.UnknownProperties.Count);
        Match("DispositionTotal",draft.AcceptedRequiredCount+draft.AcceptedOptionalCount+draft.HumanReviewClaimCount+draft.DeferredClaimCount,claims.Count);
        Match("MandatoryDomainTotal",draft.MandatoryAvailableDomainCount+draft.MandatoryHumanReviewDomainCount+draft.MandatoryDeferredDomainCount+draft.MandatoryMissingDomainCount,a.MandatoryDomains.Count);
        Match("OptionalDomainTotal",draft.OptionalAvailableDomainCount+draft.OptionalHumanReviewDomainCount+draft.OptionalDeferredDomainCount+draft.OptionalNotApplicableDomainCount,a.OptionalDomains.Count);
        draft=draft with{DiagnosticsReconciled=differences.Count==0,ReconciliationDifferences=differences};
        return draft with{DeterministicChecksum=Phase7Determinism.Hash(draft)};
    }
}
