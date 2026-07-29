using System.Diagnostics;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class StoryFrameIntegrationService(ICertifiedStoryFrameBuilder builder) : IStoryFrameIntegrationService, IStoryFrameRuntimeIdentityProvider
{
    public const string Version = "RC2-Phase6-Integration-v1";

    public StoryFrameValidationCompatibilityContext GetCompatibilityContext() => new(
        builder.BuilderType, builder.BuilderVersion, nameof(StoryFrameIntegrationService), Version,
        StoryFrameContractCompatibility.CurrentVersion, StoryFrameContractCompatibility.CurrentVersion,
        StoryFrameContractCompatibility.CurrentVersion);

    public async Task<StoryFrameIntegrationResult> BuildAsync(StoryFrameIntegrationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var watch=Stopwatch.StartNew();
        var frames=await builder.BuildAsync(request.EditorialContract, request.RequestedVariants, cancellationToken);
        var now=DateTimeOffset.UtcNow;
        var authority=new StoryFramesAuthority(StoryFrameAuthorityIdentity.BuildAuthorityId(request.ExecutionId), request.ExecutionId, request.PlanId,
            request.EventId, request.Language, request.Profile, request.Certification.CertificationId,
            request.Certification.SemanticChecksum, request.EditorialContract.ContractId, request.EditorialContract.Checksum,
            request.EditorialContract.SourcePhase4Checksum, builder.BuilderType, builder.BuilderVersion,
            request.RequestedVariants, frames, now, "");
        authority=authority with { SemanticChecksum=StoryFrameAuthorityChecksum.Authority(authority) };
        var index=StoryFrameIndexProjector.Project(authority, request.EditorialContract.Checksum);
        var diagnostics=new StoryFrameDiagnostics(request.ExecutionId,builder.BuilderType,builder.BuilderVersion,
            nameof(StoryFrameIntegrationService),Version,["05-blueprint-certification/blueprint-certification.json","05-blueprint-certification/editorial-contract.json","05-blueprint-certification/certification-diagnostics.json"],
            new Dictionary<string,string>{{"certification",request.Certification.SemanticChecksum},{"editorialContract",request.EditorialContract.Checksum},{"phase4",request.EditorialContract.SourcePhase4Checksum}},
            request.Certification.SemanticChecksum,request.EditorialContract.Checksum,request.EditorialContract.SourcePhase4Checksum,request.RequestedVariants,
            request.EditorialContract.SceneOrder.Count,frames.Select(x=>x.SceneId).Distinct().Count(),frames.Count,
            frames.GroupBy(x=>x.Variant).ToDictionary(x=>x.Key,x=>x.Count()),frames.GroupBy(x=>$"{x.Variant}:{x.SceneId}").ToDictionary(x=>x.Key,x=>x.Count()),
            frames.Count(x=>x.NarrationRequired),frames.Count(x=>x.ImageRequirements.Count+x.BrollRequirements.Count>0),frames.Sum(x=>x.ImageRequirements.Count),frames.Sum(x=>x.BrollRequirements.Count),frames.Sum(x=>x.OverlayRequirements.Count),frames.Sum(x=>x.Warnings.Count),frames.Sum(x=>x.BlockingConstraints.Count),
            ["Phase5CompleteSet","Authority","Variants","Scenes","Frames","Relationships","ProductionIntent","Index","Diagnostics","Checksums"],watch.ElapsedMilliseconds)
            { GeneratedVariantSceneCount=frames.Select(x=>$"{x.Variant}:{x.SceneId}").Distinct(StringComparer.OrdinalIgnoreCase).Count() };
        return new(authority,index,diagnostics);
    }
}
