using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Reads only the committed Phase 6 publication and proves its typed checksum/index boundary.</summary>
public sealed class Phase6CommittedAuthorityEvaluator : IPhase6CommittedAuthorityEvaluator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<Phase6CommittedAuthorityEvaluation> EvaluateAsync(Phase6CommittedAuthorityRequest request, CancellationToken token = default)
    {
        static Phase6CommittedAuthorityEvaluation Bad(string code,string message)=>new(false,null,code,[message],[]);
        token.ThrowIfCancellationRequested();
        var root=Path.GetFullPath(request.ExecutionRoot); var directory=Path.Combine(root,"06-story-frames");
        var authorityPath=Path.Combine(directory,"story-frames.json"); var indexPath=Path.Combine(directory,"story-frame-index.json");
        var diagnosticsPath=Path.Combine(directory,"story-frame-diagnostics.json"); var validationPath=Path.Combine(root,"validation","phase-06-validation.json");
        if (!File.Exists(authorityPath)||!File.Exists(indexPath)||!File.Exists(diagnosticsPath)||!File.Exists(validationPath)) return Bad("P6COMMITTED_ARTIFACT_MISSING","Committed Phase 6 artifacts or validation evidence are missing.");
        try
        {
            var authority=await Read<StoryFramesAuthority>(authorityPath,token); var index=await Read<StoryFrameIndex>(indexPath,token); var diagnostics=await Read<StoryFrameDiagnostics>(diagnosticsPath,token);
            if (authority.ExecutionId!=request.ExecutionId||authority.PlanId!=request.PlanId||authority.EventId!=request.EventId||!authority.Language.Equals(request.Language,StringComparison.OrdinalIgnoreCase)) return Bad("P6COMMITTED_IDENTITY_MISMATCH","Committed authority identity does not match request.");
            if (!StoryFrameContractCompatibility.IsSupported(authority.AuthorityContractVersion)||authority.SemanticChecksum!=StoryFrameAuthorityChecksum.Authority(authority)) return Bad("P6COMMITTED_CHECKSUM_INVALID","Story Frame authority contract or checksum is invalid.");
            if (index.Checksum!=StoryFrameAuthorityChecksum.Index(index)||index.SourceStoryFramesAuthorityId!=authority.AuthorityId||index.SourceStoryFramesChecksum!=authority.SemanticChecksum) return Bad("P6COMMITTED_INDEX_INVALID","Story Frame index checksum or lineage is invalid.");
            using var validation=JsonDocument.Parse(await File.ReadAllTextAsync(validationPath,token));
            if (!Accepted(validation.RootElement)) return Bad("P6COMMITTED_VALIDATION_INVALID","Phase 6 committed validation is not accepted.");
            var relative=new[]{"06-story-frames/story-frames.json","06-story-frames/story-frame-index.json","06-story-frames/story-frame-diagnostics.json","validation/phase-06-validation.json"};
            var published=new PublishedStoryFrameAuthority(authority,index,diagnostics,$"phase4-{request.ExecutionId}",authority.SourcePhase4Checksum,
                Phase7Determinism.Hash(authority.Frames.Where(x=>x.Variant.Equals("Long",StringComparison.OrdinalIgnoreCase))),Phase7Determinism.Hash(authority.Frames.Where(x=>x.Variant.Equals("Short",StringComparison.OrdinalIgnoreCase))),
                authority.SourceCertificationId,relative,["phase-manifest.json"],["validation/phase-06-validation.json"],authority.AuthorityContractVersion,
                new Dictionary<string,string>{{"builderType",authority.BuilderType},{"builderVersion",authority.BuilderVersion},{"integrationServiceType",diagnostics.IntegrationServiceType},{"integrationServiceVersion",diagnostics.IntegrationServiceVersion}});
            return new(true,published,"P6COMMITTED_VALID",[],[]);
        }
        catch(OperationCanceledException){throw;} catch(Exception ex) when(ex is JsonException or IOException or UnauthorizedAccessException){return Bad("P6COMMITTED_ARTIFACT_INVALID",ex.Message);}
    }
    private static async Task<T> Read<T>(string path,CancellationToken token){await using var stream=File.OpenRead(path);return(await JsonSerializer.DeserializeAsync<T>(stream,Json,token))!;}
    private static bool Accepted(JsonElement root)
    {
        foreach(var name in new[]{"isValid","accepted","success"}) if(root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.True)return true;
        return root.TryGetProperty("reasonCode",out var code)&&code.GetString()?.Contains("VALID",StringComparison.OrdinalIgnoreCase)==true;
    }
}
