using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryVisualAdapterTests
{
    [Theory]
    [InlineData(DocumentaryMediaAssetType.SkySimulationImage, DocumentaryVisualProviderIds.Stellarium)]
    [InlineData(DocumentaryMediaAssetType.TelescopeViewImage, DocumentaryVisualProviderIds.Stellarium)]
    [InlineData(DocumentaryMediaAssetType.StarChartImage, DocumentaryVisualProviderIds.AstronomyInfographic)]
    [InlineData(DocumentaryMediaAssetType.ScientificDiagramImage, DocumentaryVisualProviderIds.AstronomyInfographic)]
    [InlineData(DocumentaryMediaAssetType.HistoricalIllustrationImage, DocumentaryVisualProviderIds.AzureOpenAICinematicImage)]
    [InlineData(DocumentaryMediaAssetType.VisualImage, DocumentaryVisualProviderIds.AzureOpenAICinematicImage)]
    public void Every_visual_type_has_an_explicit_primary(DocumentaryMediaAssetType type,string expected)
    {
        var router=Router();
        var first=router.Route(Request(type));var second=router.Route(Request(type));
        Assert.Equal(expected,first.PrimaryProvider);Assert.Equal(first,second);
        Assert.DoesNotContain(first.OrderedFallbackProviders,x=>x.Contains("Thumbnail",StringComparison.OrdinalIgnoreCase));
    }

    [Fact] public void Fallback_order_and_feature_gates_are_deterministic()
    {
        var visual=Router().Route(Request(DocumentaryMediaAssetType.VisualImage));
        Assert.Equal(new[]{DocumentaryVisualProviderIds.FileVisualAsset,DocumentaryVisualProviderIds.CelestialAsset},visual.OrderedFallbackProviders);
        Assert.True(visual.FallbackAllowed);
        Assert.Empty(Router().Route(Request(DocumentaryMediaAssetType.SkySimulationImage)).OrderedFallbackProviders);
        Assert.Equal(DocumentaryVisualProviderIds.CelestialAsset,Router().Route(Request(DocumentaryMediaAssetType.TelescopeViewImage)).OrderedFallbackProviders.Single());
    }

    [Fact] public void Nonvisual_and_unknown_values_fail_instead_of_using_generic_generation()
    {
        Assert.Throws<NotSupportedException>(()=>Router().Route(Request(DocumentaryMediaAssetType.NarrationAudio)));
        Assert.Throws<NotSupportedException>(()=>Router().Route(Request((DocumentaryMediaAssetType)999)));
    }

    [Theory]
    [InlineData(DocumentaryProductionFailureCode.ProviderTimeout,true)]
    [InlineData(DocumentaryProductionFailureCode.ProviderRateLimited,true)]
    [InlineData(DocumentaryProductionFailureCode.ProviderAuthenticationFailed,false)]
    [InlineData(DocumentaryProductionFailureCode.ConfigurationMissing,false)]
    [InlineData(DocumentaryProductionFailureCode.ProviderContentPolicyRejected,false)]
    public void Fallback_requires_eligible_failure(DocumentaryProductionFailureCode code,bool expected)
    {
        var request=Request(DocumentaryMediaAssetType.VisualImage);var route=Router().Route(request);
        var decision=new DocumentaryVisualFallbackPolicy().Evaluate(request,route,DocumentaryVisualProviderIds.FileVisualAsset,new(code,"failure"),DocumentaryProductionExecutionMode.Certified);
        Assert.Equal(expected,decision.Allowed);Assert.True(decision.IsSemanticallyEquivalent);
    }

    [Fact] public void Generated_scientific_fallback_is_never_certification_equivalent()
    {
        var request=Request(DocumentaryMediaAssetType.ScientificDiagramImage);var route=Router().Route(request);
        var decision=new DocumentaryVisualFallbackPolicy().Evaluate(request,route,DocumentaryVisualProviderIds.AzureOpenAICinematicImage,new(DocumentaryProductionFailureCode.ProviderTimeout,"timeout"),DocumentaryProductionExecutionMode.Shadow);
        Assert.False(decision.Allowed);Assert.False(decision.IsSemanticallyEquivalent);
    }

    [Fact] public void Result_factories_enforce_mutually_exclusive_success_and_failure()
    {
        var failure=new DocumentaryProductionFailure(DocumentaryProductionFailureCode.ProviderTimeout,"timeout");
        var failed=DocumentaryProductionVisualAdapterResult.Failed(failure,"p","p");
        Assert.False(failed.Succeeded);Assert.Null(failed.Artifact);Assert.Same(failure,failed.Failure);
        Assert.Throws<ArgumentException>(()=>DocumentaryProductionVisualAdapterResult.Failed(failure,"", "p"));
    }

    private static DocumentaryVisualProviderRouter Router()=>new(Options.Create(new DocumentaryVisualAdapterOptions{AllowFallback=true,AllowRepresentativeTelescopeFallback=true,AllowGeneratedScientificDiagramFallback=true}));
    private static DocumentaryVisualGenerationRequest Request(DocumentaryMediaAssetType type)
    {
        var plan=new DocumentaryMediaAssetPlan("asset",type,DocumentaryMediaAssetFormat.Png,DocumentaryMediaVariantType.LongEnglish,"scene","instruction",DocumentaryMediaProviderCapability.GeneratedIllustration,0,Array.Empty<DocumentaryMediaAssetDependency>(),1920,1080,0,0,0,0,Array.Empty<DocumentaryMediaKnowledgeReference>(),"correlation");
        return new(plan,null!,1920,1080,DocumentaryMediaAssetFormat.Png,1,"correlation");
    }
}
