using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal sealed class DocumentaryMediaPipelineFakeProviders : IDocumentaryVisualAssetProvider, IDocumentaryNarrationAssetProvider,
    IDocumentarySubtitleAssetProvider, IDocumentarySceneCompositionProvider, IDocumentaryVariantCompositionProvider, IDocumentaryRenderVerificationProvider
{
    internal readonly List<DocumentaryVisualGenerationRequest> VisualRequests=[];
    internal readonly List<DocumentaryNarrationSynthesisRequest> NarrationRequests=[];
    internal readonly List<DocumentarySubtitleGenerationRequest> SubtitleRequests=[];
    internal readonly List<DocumentarySceneCompositionRequest> SceneRequests=[];
    internal readonly List<DocumentaryVariantCompositionRequest> VariantRequests=[];
    internal readonly List<DocumentaryRenderVerificationRequest> VerificationRequests=[];
    internal readonly HashSet<string> FailedAssetIds=[];
    internal readonly HashSet<string> FailFirstAssetIds=[];
    internal readonly HashSet<DocumentaryMediaVariantType> InvalidVariants=[];
    internal long NarrationExtensionMilliseconds;
    internal Func<DocumentaryMediaAssetPlan, DocumentaryMediaAssetResult, DocumentaryMediaAssetResult>? AssetResultTransform;
    internal Func<DocumentaryRenderVerificationRequest, DocumentaryRenderVerificationResult, DocumentaryRenderVerificationResult>? VerificationResultTransform;
    internal DocumentaryMediaProviderRegistry Registry => new(this,this,this,this,this,this);

    public DocumentaryVisualGenerationResult Generate(DocumentaryVisualGenerationRequest request)
    {
        VisualRequests.Add(request); var failed=Fails(request.AssetPlan.AssetId,request.Attempt);
        var asset=Asset(request.AssetPlan,failed,request.Attempt,request.AssetPlan.ExpectedDurationMilliseconds);
        asset=Transform(request.AssetPlan,asset); return new(asset.Status,asset,failed?"fixture-failure":null,failed?"fixture failure":null);
    }
    public DocumentaryNarrationSynthesisResult Synthesize(DocumentaryNarrationSynthesisRequest request)
    {
        NarrationRequests.Add(request); var failed=Fails(request.AssetPlan.AssetId,request.Attempt);
        var duration=request.AssetPlan.ExpectedDurationMilliseconds+NarrationExtensionMilliseconds;
        var asset=Asset(request.AssetPlan,failed,request.Attempt,duration);
        asset=Transform(request.AssetPlan,asset); return new(asset.Status,asset,duration,failed?"fixture-failure":null,failed?"fixture failure":null);
    }
    public DocumentarySubtitleGenerationResult Generate(DocumentarySubtitleGenerationRequest request)
    {
        SubtitleRequests.Add(request); var failed=Fails(request.AssetPlan.AssetId,1); var asset=Asset(request.AssetPlan,failed,1,request.MeasuredNarrationDurationMilliseconds);
        asset=Transform(request.AssetPlan,asset); return new(asset.Status,asset,request.SubtitleCues.Count,failed?"fixture-failure":null,failed?"fixture failure":null);
    }
    public DocumentarySceneCompositionResult Compose(DocumentarySceneCompositionRequest request)
    {
        SceneRequests.Add(request); var failed=Fails(request.AssetPlan.AssetId,request.Attempt); var asset=Asset(request.AssetPlan,failed,request.Attempt,request.EffectiveSceneDurationMilliseconds);
        asset=Transform(request.AssetPlan,asset); return new(asset.Status,asset,request.EffectiveSceneDurationMilliseconds,failed?"fixture-failure":null,failed?"fixture failure":null);
    }
    public DocumentaryVariantCompositionResult Compose(DocumentaryVariantCompositionRequest request)
    {
        VariantRequests.Add(request); var failed=Fails(request.AssetPlan.AssetId,request.Attempt); var duration=request.SceneAssets.Sum(x=>x.DurationMilliseconds);
        var asset=Asset(request.AssetPlan,failed,request.Attempt,duration);
        asset=Transform(request.AssetPlan,asset); return new(asset.Status,asset,request.SceneAssets.Count,duration,failed?"fixture-failure":null,failed?"fixture failure":null);
    }
    public DocumentaryRenderVerificationResult Verify(DocumentaryRenderVerificationRequest request)
    {
        VerificationRequests.Add(request); var valid=!InvalidVariants.Contains(request.Variant.VariantType);
        var result = new DocumentaryRenderVerificationResult(valid,request.ExpectedSceneCount,request.ExpectedWidth,request.ExpectedHeight,request.ExpectedFrameRate,
            request.ExpectedAudioSampleRate,request.ExpectedAudioChannelCount,request.ExpectedMinimumDurationMilliseconds,true,true,true,true,
            valid?Array.Empty<string>():["fixture verification failure"]);
        return VerificationResultTransform?.Invoke(request,result) ?? result;
    }
    private DocumentaryMediaAssetResult Transform(DocumentaryMediaAssetPlan plan, DocumentaryMediaAssetResult result) =>
        AssetResultTransform?.Invoke(plan,result) ?? result;
    private bool Fails(string id,int attempt)=>FailedAssetIds.Contains(id)||(attempt==1&&FailFirstAssetIds.Contains(id));
    private static DocumentaryMediaAssetResult Asset(DocumentaryMediaAssetPlan plan,bool failed,int attempt,long duration)=>new(
        plan.AssetId,plan.AssetType,plan.AssetFormat,failed?DocumentaryMediaAssetStatus.Failed:DocumentaryMediaAssetStatus.Generated,
        "fixture.provider",failed?null:$"fixture://{plan.AssetId}",failed?0:128,duration,plan.ExpectedWidth,plan.ExpectedHeight,
        plan.ExpectedFrameRate,plan.ExpectedSampleRate,plan.ExpectedChannelCount,failed?null:"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        failed?"fixture-failure":null,failed?"fixture failure":null,attempt,plan.CorrelationId);
}
