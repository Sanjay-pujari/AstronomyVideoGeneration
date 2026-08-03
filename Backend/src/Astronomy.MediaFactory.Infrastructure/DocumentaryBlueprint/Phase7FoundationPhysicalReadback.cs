using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Performs physical, typed readback of the closed nine-artifact P7.1 complete set.</summary>
public sealed class Phase7FoundationPhysicalReadback
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly (string Path,Type Contract)[] Expected =
    [
        ("07-narration/narration-input-authority.json",typeof(Phase7CommittedInputAuthority)),
        ("07-narration/family-narration-profile.json",typeof(FamilyNarrationProfile)),
        ("07-narration/knowledge-resolution-report.json",typeof(ResolvedNarrationKnowledge)),
        ("07-narration/phase7-foundation-diagnostics.json",typeof(Phase7FoundationDiagnostics)),
        ("07-narration/long/scene-knowledge-packets.json",typeof(SceneKnowledgePacket[])),
        ("07-narration/long/narration-planning.json",typeof(VariantNarrationPlan)),
        ("07-narration/short/scene-knowledge-packets.json",typeof(SceneKnowledgePacket[])),
        ("07-narration/short/narration-planning.json",typeof(VariantNarrationPlan)),
        ("validation/phase-07-foundation-validation.json",typeof(Phase7FoundationValidation))
    ];

    public async Task<Phase7FoundationCompleteSetReadback> ReadAsync(string executionRoot,
        Phase7CommittedInputAuthority expectedIdentity, CancellationToken token = default)
        =>await ReadAsync(executionRoot,expectedIdentity,null,token);
    public async Task<Phase7FoundationCompleteSetReadback> ReadAsync(string executionRoot,
        Phase7CommittedInputAuthority expectedIdentity, Phase7FoundationArtifactInventory? expectedInventory, CancellationToken token = default)
    {
        var root=Path.GetFullPath(executionRoot); var evidence=new List<Phase7FoundationPhysicalReadbackEvidence>();
        foreach(var item in Expected)
        {
            token.ThrowIfCancellationRequested(); var errors=new List<string>(); var safe=Safe(item.Path);
            var path=Path.GetFullPath(Path.Combine(root,item.Path.Replace('/',Path.DirectorySeparatorChar)));
            if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal)) safe=false;
            var exists=safe&&File.Exists(path); var size=exists?new FileInfo(path).Length:0; var hash=""; object? value=null;
            if(!safe)errors.Add("P7READBACK_UNSAFE_PATH"); if(!exists)errors.Add("P7READBACK_MISSING"); else if(size<=0)errors.Add("P7READBACK_EMPTY");
            if(exists&&size>0)
            {
                try { var bytes=await File.ReadAllBytesAsync(path,token); hash="sha256:"+Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); value=JsonSerializer.Deserialize(bytes,item.Contract,Json); if(value is null)errors.Add("P7READBACK_NULL_CONTRACT"); }
                catch(JsonException ex){errors.Add("P7READBACK_JSON_INVALID:"+ex.Message);}
            }
            var identity=value is null?false:Identity(value,expectedIdentity); var semantic=value is not null&&Semantic(value); var lineage=value is not null&&Lineage(value,expectedIdentity);
            if(value is not null&&!identity)errors.Add("P7READBACK_IDENTITY_MISMATCH");
            if(value is not null&&!semantic)errors.Add("P7READBACK_SEMANTIC_CHECKSUM_MISMATCH");
            if(value is not null&&!lineage)errors.Add("P7READBACK_LINEAGE_MISMATCH");
            var expectedArtifact=expectedInventory?.Artifacts.SingleOrDefault(x=>x.RelativePath==item.Path);
            if(expectedInventory is null) errors.Add("P7READBACK_EXPECTED_INVENTORY_MISSING");
            else if(expectedArtifact is null) errors.Add("P7READBACK_EXPECTED_ARTIFACT_MISSING");
            else
            {
                if(!string.Equals(hash,expectedArtifact.PhysicalSha256,StringComparison.OrdinalIgnoreCase))errors.Add("P7READBACK_PHYSICAL_HASH_MISMATCH");
                if(size!=expectedArtifact.SizeBytes)errors.Add("P7READBACK_SIZE_MISMATCH");
            }
            evidence.Add(new(item.Path,exists,size,hash,value is not null,item.Contract.Name,Phase7FoundationContract.Version,identity,semantic,lineage,safe,errors));
        }
        var allErrors=evidence.SelectMany(x=>x.Errors.Select(e=>$"{x.ArtifactPath}:{e}")).ToArray();
        return new(evidence,allErrors.Length==0&&evidence.Count==Expected.Length,allErrors){ExpectedInventory=expectedInventory};
    }
    private static bool Identity(object value,Phase7CommittedInputAuthority expected)=>value switch
    {
        Phase7CommittedInputAuthority x=>x.StoryFrameAuthority.Authority.ExecutionId==expected.StoryFrameAuthority.Authority.ExecutionId&&x.StoryFrameAuthority.Authority.EventId==expected.StoryFrameAuthority.Authority.EventId&&x.Language==expected.Language,
        FamilyNarrationProfile x=>x.ProfileId==expected.FamilyProfile.ProfileId,
        ResolvedNarrationKnowledge x=>x.PayloadId==expected.Knowledge.PayloadId&&x.Language==expected.Language,
        Phase7FoundationDiagnostics x=>x.ExecutionId==expected.StoryFrameAuthority.Authority.ExecutionId&&x.EventId==expected.StoryFrameAuthority.Authority.EventId,
        SceneKnowledgePacket[] x=>x.All(p=>p.ExecutionId==expected.StoryFrameAuthority.Authority.ExecutionId&&p.EventId==expected.StoryFrameAuthority.Authority.EventId),
        VariantNarrationPlan x=>x.ExecutionId==expected.StoryFrameAuthority.Authority.ExecutionId&&x.EventId==expected.StoryFrameAuthority.Authority.EventId,
        Phase7FoundationValidation=>true,
        _=>false
    };
    private static bool Semantic(object value)=>value switch
    {
        FamilyNarrationProfile x=>x.DeterministicChecksum==Phase7Determinism.Hash(x with{DeterministicChecksum=""}),
        ResolvedNarrationKnowledge x=>x.DeterministicChecksum==Phase7Determinism.Hash(x with{DeterministicChecksum=""}),
        Phase7FoundationDiagnostics x=>x.DeterministicChecksum==Phase7Determinism.Hash(x with{DeterministicChecksum=""}),
        SceneKnowledgePacket[] x=>x.All(p=>p.DeterministicChecksum==Phase7Determinism.Hash(p with{DeterministicChecksum=""})),
        VariantNarrationPlan x=>x.DeterministicChecksum==Phase7Determinism.Hash(x with{DeterministicChecksum=""}),
        Phase7FoundationValidation x=>x.DeterministicChecksum==Phase7Determinism.Hash(x with{DeterministicChecksum=""}),
        Phase7CommittedInputAuthority=>true,
        _=>false
    };
    private static bool Lineage(object value,Phase7CommittedInputAuthority expected)=>value switch
    {
        SceneKnowledgePacket[] x=>x.All(p=>p.ProfileId==expected.Profile&&p.ProfileVersion==expected.ProfileVersion),
        VariantNarrationPlan x=>x.ProfileId==expected.Profile&&x.SourceStoryFrameAuthorityId==expected.StoryFrameAuthority.Authority.AuthorityId,
        _=>true
    };
    private static bool Safe(string p)=>!Path.IsPathRooted(p)&&!p.Contains('\\')&&!p.Split('/').Any(x=>x is "" or "." or "..")&&!p.Contains("staging",StringComparison.OrdinalIgnoreCase)&&!p.Contains("backup",StringComparison.OrdinalIgnoreCase);
}
