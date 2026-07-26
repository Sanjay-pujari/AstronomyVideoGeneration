namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public interface IDocumentaryVisualAssetProvider { DocumentaryVisualGenerationResult Generate(DocumentaryVisualGenerationRequest request); }
public interface IDocumentaryNarrationAssetProvider { DocumentaryNarrationSynthesisResult Synthesize(DocumentaryNarrationSynthesisRequest request); }
public interface IDocumentarySubtitleAssetProvider { DocumentarySubtitleGenerationResult Generate(DocumentarySubtitleGenerationRequest request); }
public interface IDocumentarySceneCompositionProvider { DocumentarySceneCompositionResult Compose(DocumentarySceneCompositionRequest request); }
public interface IDocumentaryVariantCompositionProvider { DocumentaryVariantCompositionResult Compose(DocumentaryVariantCompositionRequest request); }
public interface IDocumentaryRenderVerificationProvider { DocumentaryRenderVerificationResult Verify(DocumentaryRenderVerificationRequest request); }

public sealed class DocumentaryMediaProviderRegistry
{
    public DocumentaryMediaProviderRegistry(IDocumentaryVisualAssetProvider? visual=null,IDocumentaryNarrationAssetProvider? narration=null,IDocumentarySubtitleAssetProvider? subtitle=null,IDocumentarySceneCompositionProvider? scene=null,IDocumentaryVariantCompositionProvider? variant=null,IDocumentaryRenderVerificationProvider? verifier=null)
    { Visual=visual;Narration=narration;Subtitle=subtitle;Scene=scene;Variant=variant;Verifier=verifier; }
    public IDocumentaryVisualAssetProvider? Visual{get;} public IDocumentaryNarrationAssetProvider? Narration{get;} public IDocumentarySubtitleAssetProvider? Subtitle{get;} public IDocumentarySceneCompositionProvider? Scene{get;} public IDocumentaryVariantCompositionProvider? Variant{get;} public IDocumentaryRenderVerificationProvider? Verifier{get;}
    public bool IsComplete=>Visual is not null&&Narration is not null&&Subtitle is not null&&Scene is not null&&Variant is not null&&Verifier is not null;
}
