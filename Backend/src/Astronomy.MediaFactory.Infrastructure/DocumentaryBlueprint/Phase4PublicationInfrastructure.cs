using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase4ArtifactSerializer:IPhase4ArtifactSerializer
{
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public byte[] Serialize<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,Options);
    public T Deserialize<T>(byte[] bytes)=>JsonSerializer.Deserialize<T>(bytes,Options) ?? throw new JsonException($"Could not deserialize {typeof(T).Name}.");
    public string SemanticChecksum<T>(T value,Func<T,T> clearChecksum)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(clearChecksum(value),new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant();
}
public sealed class Phase4FileSystem:IPhase4FileSystem
{
    public async Task WriteAsync(string path,byte[] bytes,CancellationToken token){Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,bytes,token);}
    public byte[] Read(string path)=>File.ReadAllBytes(path);
    public string Sha256(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
public sealed class Phase4ExecutionLock:IPhase4ExecutionLock
{
    private static readonly ConcurrentDictionary<string,SemaphoreSlim> Locks=new(StringComparer.OrdinalIgnoreCase);
    public async ValueTask<IAsyncDisposable> AcquireAsync(string root,string id,CancellationToken token){var gate=Locks.GetOrAdd(Path.GetFullPath(root)+"|"+id,_=>new(1,1));await gate.WaitAsync(token);return new Releaser(gate);}
    private sealed class Releaser(SemaphoreSlim gate):IAsyncDisposable{public ValueTask DisposeAsync(){gate.Release();return ValueTask.CompletedTask;}}
}
public sealed class Phase4RecoveryService:IPhase4RecoveryService
{
    public Task<bool> RecoverAsync(string root,CancellationToken token){var recovered=false;foreach(var path in Directory.Exists(root)?Directory.EnumerateDirectories(root,".*.phase-04.tmp"):[]){token.ThrowIfCancellationRequested();Directory.Delete(path,true);recovered=true;}foreach(var backup in Directory.Exists(root)?Directory.EnumerateDirectories(root,".*.phase-04.backup"):[]){token.ThrowIfCancellationRequested();var target=Path.Combine(root,"04-blueprint");var saved=Path.Combine(backup,"04-blueprint");if(!Directory.Exists(target)&&Directory.Exists(saved)){Directory.Move(saved,target);recovered=true;}if(Directory.Exists(backup))Directory.Delete(backup,true);}return Task.FromResult(recovered);}
}
public sealed class Phase4ManifestUpdater(IPhase4ArtifactSerializer serializer):IPhase4ManifestUpdater
{
    public byte[] Merge(byte[]? existing,IReadOnlyList<Phase4ArtifactEntry> entries){JsonObject root;if(existing is {Length:>0})root=JsonNode.Parse(existing)?.AsObject()??new();else root=new JsonObject{{"schemaVersion","phase-manifest.v1"}};root["phase4Artifacts"]=JsonSerializer.SerializeToNode(entries.OrderBy(x=>x.RelativePath,StringComparer.Ordinal),new JsonSerializerOptions(JsonSerializerDefaults.Web));return serializer.Serialize(root);}
}
public sealed class Phase4PublishedAuthorityValidator(IPhase4ArtifactSerializer serializer):IPhase4PublishedAuthorityValidator
{
    public Task<IReadOnlyList<Phase4PublicationDiagnostic>> ValidateAsync(string dir,DocumentaryBlueprintAggregate expected,CancellationToken token)
    {var errors=new List<Phase4PublicationDiagnostic>();try{var aggregate=serializer.Deserialize<DocumentaryBlueprintAggregate>(File.ReadAllBytes(Path.Combine(dir,"documentary-blueprint.json")));var lng=serializer.Deserialize<DocumentaryBlueprintVariantArtifact>(File.ReadAllBytes(Path.Combine(dir,"documentary-blueprint.long.json")));var sh=serializer.Deserialize<DocumentaryBlueprintVariantArtifact>(File.ReadAllBytes(Path.Combine(dir,"documentary-blueprint.short.json")));if(!DocumentaryBlueprintProjectionChecksum.HasValidAggregateChecksum(aggregate)||aggregate.DeterministicChecksum!=expected.DeterministicChecksum)errors.Add(new(Phase4PublicationReasonCodes.ChecksumFailed,"Aggregate checksum mismatch."));if(!DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(lng)||lng.DeterministicChecksum!=aggregate.LongVariant.DeterministicChecksum||!serializer.Serialize(lng).SequenceEqual(serializer.Serialize(aggregate.LongVariant)))errors.Add(new(Phase4PublicationReasonCodes.TemporaryValidationFailed,"Long projection is not the embedded variant."));if(!DocumentaryBlueprintProjectionChecksum.HasValidVariantChecksum(sh)||sh.DeterministicChecksum!=aggregate.ShortVariant.DeterministicChecksum||!serializer.Serialize(sh).SequenceEqual(serializer.Serialize(aggregate.ShortVariant)))errors.Add(new(Phase4PublicationReasonCodes.TemporaryValidationFailed,"Short projection is not the embedded variant."));var required=new[]{"knowledge-selection.json","long-scene-index.json","short-scene-index.json","blueprint-build-report.json"};foreach(var name in required){var path=Path.Combine(dir,name);if(!File.Exists(path)||new FileInfo(path).Length==0)errors.Add(new(Phase4PublicationReasonCodes.TemporaryValidationFailed,$"Required artifact is missing: {name}",path));else JsonNode.Parse(File.ReadAllBytes(path));}}catch(Exception ex){errors.Add(new(Phase4PublicationReasonCodes.TemporaryValidationFailed,ex.Message));}return Task.FromResult<IReadOnlyList<Phase4PublicationDiagnostic>>(errors);}
}
