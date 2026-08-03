using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7FoundationFileSystem : IPhase7FoundationFileSystem { }
public sealed class Phase7FoundationExecutionLock : IPhase7FoundationExecutionLock
{
    private static readonly SemaphoreSlim Gate = new(1,1);
    public async Task<IAsyncDisposable> AcquireAsync(string executionRoot,CancellationToken cancellationToken) { await Gate.WaitAsync(cancellationToken); return new Releaser(); }
    private sealed class Releaser:IAsyncDisposable { public ValueTask DisposeAsync(){Gate.Release();return ValueTask.CompletedTask;} }
}
public sealed class Phase7FoundationRecoveryService : IPhase7FoundationRecoveryService
{
    public Task RecoverAsync(string root,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested(); root=Path.GetFullPath(root); if(!Directory.Exists(root))return Task.CompletedTask;
        foreach(var staging in Directory.EnumerateDirectories(root,".07-narration-foundation-staging-*"))Directory.Delete(staging,true);
        var backups=Directory.EnumerateDirectories(root,".07-narration-foundation-backup-*").OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray();
        var active=Path.Combine(root,"07-narration");
        if(!Directory.Exists(active)&&backups.Length>0)Directory.Move(backups[0],active);
        foreach(var backup in backups.Where(Directory.Exists))Directory.Delete(backup,true);
        var validation=Path.Combine(root,"validation","phase-07-foundation-validation.json");
        var validationBackups=Directory.EnumerateFiles(root,".phase-07-foundation-validation-backup-*").OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        if(!File.Exists(validation)&&validationBackups.Length>0){Directory.CreateDirectory(Path.GetDirectoryName(validation)!);File.Move(validationBackups[0],validation);}
        foreach(var backup in validationBackups.Where(File.Exists))File.Delete(backup);
        foreach(var marker in Directory.EnumerateFiles(root,".phase-07-foundation-transaction-*"))File.Delete(marker);
        return Task.CompletedTask;
    }
}
public sealed class Phase7FoundationCommittedStateEvaluator : IPhase7FoundationCommittedStateEvaluator
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static readonly string[] Paths=["07-narration/narration-input-authority.json","07-narration/family-narration-profile.json","07-narration/knowledge-resolution-report.json","07-narration/phase7-foundation-diagnostics.json","07-narration/long/scene-knowledge-packets.json","07-narration/long/narration-planning.json","07-narration/short/scene-knowledge-packets.json","07-narration/short/narration-planning.json","validation/phase-07-foundation-validation.json"];
    public async Task<Phase7FoundationCommittedStateEvaluation> EvaluateAsync(string executionRoot,CancellationToken token=default)
    {
        var root=Path.GetFullPath(executionRoot);var errors=new List<string>();
        foreach(var relative in Paths){if(!Safe(relative)||!File.Exists(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)))||new FileInfo(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar))).Length==0)errors.Add($"P7COMMITTED_ARTIFACT_MISSING:{relative}");}
        if(errors.Count>0)return new(false,null,"P7COMMITTED_COMPLETE_SET_INVALID",errors);
        try
        {
            var input=await Read<Phase7CommittedInputAuthority>(root,Paths[0],token);var profile=await Read<FamilyNarrationProfile>(root,Paths[1],token);var knowledge=await Read<ResolvedNarrationKnowledge>(root,Paths[2],token);var diagnostics=await Read<Phase7FoundationDiagnostics>(root,Paths[3],token);
            var longs=await Read<SceneKnowledgePacket[]>(root,Paths[4],token);var lp=await Read<VariantNarrationPlan>(root,Paths[5],token);var shorts=await Read<SceneKnowledgePacket[]>(root,Paths[6],token);var sp=await Read<VariantNarrationPlan>(root,Paths[7],token);var validation=await Read<Phase7FoundationValidation>(root,Paths[8],token);
            if(!validation.IsValid)errors.Add("P7COMMITTED_VALIDATION_REJECTED");if(input.FamilyProfile.ProfileId!=profile.ProfileId||input.Knowledge.DeterministicChecksum!=knowledge.DeterministicChecksum)errors.Add("P7COMMITTED_LINEAGE_MISMATCH");
            if(longs.Concat(shorts).SelectMany(x=>x.RequiredClaims).Any(x=>x.SourceIds.Count==0))errors.Add("P7COMMITTED_PROVENANCE_MISMATCH");
            if(errors.Count>0)return new(false,null,"P7COMMITTED_INVALID",errors);
            var physical=Paths.ToDictionary(x=>x,x=>"sha256:"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,x.Replace('/',Path.DirectorySeparatorChar))))).ToLowerInvariant());
            var semantic=new Dictionary<string,string>{{Paths[0],input.KnowledgePayloadChecksum},{Paths[1],profile.DeterministicChecksum},{Paths[2],knowledge.DeterministicChecksum},{Paths[3],diagnostics.DeterministicChecksum},{Paths[5],lp.DeterministicChecksum},{Paths[7],sp.DeterministicChecksum},{Paths[8],validation.DeterministicChecksum}};
            var authority=new PublishedPhase7FoundationAuthority(input,profile,knowledge,longs,shorts,lp,sp,diagnostics,validation,Paths,semantic,physical,new Dictionary<string,string>{{"foundation",Phase7FoundationContract.Version}},input.RuntimeProviderCompatibilityMetadata);
            return new(true,authority,"P7COMMITTED_VALID",[]);
        } catch(Exception ex) when(ex is JsonException or InvalidDataException){return new(false,null,"P7COMMITTED_READBACK_INVALID",[ex.Message]);}
    }
    private static async Task<T> Read<T>(string root,string relative,CancellationToken token){await using var stream=File.OpenRead(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));var value=await JsonSerializer.DeserializeAsync<T>(stream,Json,token);if(value is null)throw new InvalidDataException($"Empty artifact: {relative}");return value;}
    private static bool Safe(string p)=>!Path.IsPathRooted(p)&&!p.Contains('\\')&&!p.Split('/').Any(x=>x is "" or "." or "..");
}
