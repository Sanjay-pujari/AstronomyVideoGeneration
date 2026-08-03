using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>P7.1-only transaction. It never updates the final phase manifest or calls narration/speech providers.</summary>
public sealed class Phase7FoundationService(IPhase7InputAuthorityEvaluator inputEvaluator,
    IPhase7SceneKnowledgePacketBuilder packetBuilder, IPhase7NarrationPlanningBuilder planningBuilder,
    IPhase7FoundationValidator validator) : IPhase7FoundationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] OutputPaths = ["07-narration/narration-input-authority.json","07-narration/family-narration-profile.json",
        "07-narration/knowledge-resolution-report.json","07-narration/long/scene-knowledge-packets.json","07-narration/long/narration-planning.json",
        "07-narration/short/scene-knowledge-packets.json","07-narration/short/narration-planning.json","07-narration/phase7-foundation-diagnostics.json",
        "validation/phase-07-foundation-validation.json"];
    public async Task<Phase7FoundationExecutionResult> ExecuteAsync(Phase7InputAuthorityRequest request, CancellationToken token = default)
    {
        var evaluation = await inputEvaluator.EvaluateAsync(request, token);
        if (!evaluation.IsValid || evaluation.Authority is null) throw new InvalidOperationException($"{evaluation.ReasonCode}: {string.Join("; ",evaluation.Errors)}");
        var input = evaluation.Authority;
        var longs = packetBuilder.Build(input,"Long"); var shorts = packetBuilder.Build(input,"Short");
        var longPlan = planningBuilder.Build(input,longs,"Long"); var shortPlan = planningBuilder.Build(input,shorts,"Short");
        var validation = validator.Validate(input,longs,shorts,longPlan,shortPlan,OutputPaths);
        if (!validation.IsValid) throw new InvalidOperationException($"{validation.ReasonCode}: {string.Join("; ",validation.Errors)}");
        var root = Path.GetFullPath(request.ExecutionRoot); Directory.CreateDirectory(root);
        var tx = Guid.NewGuid().ToString("N"); var staging = Path.Combine(root,$".07-narration-foundation-staging-{tx}");
        var active = Path.Combine(root,"07-narration"); var backup = Path.Combine(root,$".07-narration-foundation-backup-{tx}");
        var diagnostic = Diagnostics(input,longs,shorts,longPlan,shortPlan,evaluation.Warnings,OutputPaths);
        try
        {
            Directory.CreateDirectory(Path.Combine(staging,"long")); Directory.CreateDirectory(Path.Combine(staging,"short"));
            await Write(staging,"narration-input-authority.json",input,token); await Write(staging,"family-narration-profile.json",input.FamilyProfile,token);
            await Write(staging,"knowledge-resolution-report.json",input.Knowledge,token); await Write(staging,"long/scene-knowledge-packets.json",longs,token);
            await Write(staging,"long/narration-planning.json",longPlan,token); await Write(staging,"short/scene-knowledge-packets.json",shorts,token);
            await Write(staging,"short/narration-planning.json",shortPlan,token); await Write(staging,"phase7-foundation-diagnostics.json",diagnostic,token);
            var rereadLong = await Read<SceneKnowledgePacket[]>(staging,"long/scene-knowledge-packets.json",token);
            var rereadShort = await Read<SceneKnowledgePacket[]>(staging,"short/scene-knowledge-packets.json",token);
            var rereadLongPlan = await Read<VariantNarrationPlan>(staging,"long/narration-planning.json",token);
            var rereadShortPlan = await Read<VariantNarrationPlan>(staging,"short/narration-planning.json",token);
            var physical = validator.Validate(input,rereadLong,rereadShort,rereadLongPlan,rereadShortPlan,OutputPaths);
            if (!physical.IsValid) throw new InvalidDataException($"Physical staging validation failed: {physical.ReasonCode}");
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(active)) Directory.Move(active,backup);
            try { Directory.Move(staging,active); } catch { if (Directory.Exists(backup)&&!Directory.Exists(active)) Directory.Move(backup,active); throw; }
            Directory.CreateDirectory(Path.Combine(root,"validation"));
            await Write(root,"validation/phase-07-foundation-validation.json",physical,CancellationToken.None);
            if (Directory.Exists(backup)) Directory.Delete(backup,true);
            return new(true,physical.ReasonCode,"07-narration",physical,diagnostic);
        }
        catch { if (Directory.Exists(staging)) Directory.Delete(staging,true); throw; }
    }
    private static async Task Write<T>(string root,string relative,T value,CancellationToken token)
    { var path=Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await using var stream=File.Create(path); await JsonSerializer.SerializeAsync(stream,value,Json,token); await stream.FlushAsync(token); }
    private static async Task<T> Read<T>(string root,string relative,CancellationToken token)
    { await using var stream=File.OpenRead(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar))); return (await JsonSerializer.DeserializeAsync<T>(stream,Json,token))!; }
    private static Phase7FoundationDiagnostics Diagnostics(Phase7CommittedInputAuthority i,IReadOnlyList<SceneKnowledgePacket> l,IReadOnlyList<SceneKnowledgePacket> s,VariantNarrationPlan lp,VariantNarrationPlan sp,IReadOnlyList<string> warnings,IReadOnlyList<string> outputs)
    {
        var packets=l.Concat(s).ToArray(); var claims=packets.SelectMany(x=>x.RequiredClaims.Concat(x.OptionalClaims)).DistinctBy(x=>x.ClaimId).ToArray();
        var detected=packets.Count(x=>x.UpstreamSemanticLineage.TryGetValue("phase7Enrichment",out var value)&&value=="generic-upstream-semantic-resolved");
        var draft=new Phase7FoundationDiagnostics(i.StoryFrameAuthority.Authority.ExecutionId,i.StoryFrameAuthority.Authority.PlanId,i.StoryFrameAuthority.Authority.EventId,i.EventFamily,i.Language,i.FamilyProfile.ProfileId,i.FamilyProfile.ContractVersion,true,true,true,true,i.LongStoryFrames.Count,i.ShortStoryFrames.Count,l.Count,s.Count,packets.Length,lp.ScenePlanCount,sp.ScenePlanCount,
            i.Knowledge.Domains.Where(x=>x.Status==KnowledgeDomainStatus.Available).Select(x=>x.Domain).ToArray(),i.Knowledge.Domains.Where(x=>x.Status==KnowledgeDomainStatus.Missing).Select(x=>x.Domain).ToArray(),i.Knowledge.Domains.Where(x=>x.Status==KnowledgeDomainStatus.Deferred).Select(x=>x.Domain).ToArray(),detected,detected,[],claims.Count(x=>x.IsLocationDependent),claims.Count(x=>x.IsDateTimeDependent),claims.Count(x=>x.IsApproximate),claims.Count(x=>x.RequiresHumanReview),warnings.Count+packets.Sum(x=>x.Warnings.Count),packets.Sum(x=>x.BlockingIssues.Count),i.InputArtifactPaths,outputs,"");
        return draft with { DeterministicChecksum=Phase7Determinism.Hash(draft with { DeterministicChecksum="" }) };
    }
}
