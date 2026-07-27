using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using Xunit;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentaryMediaVerificationPolicyResolverTests {
 readonly DocumentaryMediaVerificationPolicyResolver resolver=new();
 [Theory][InlineData(DocumentaryPhysicalArtifactKind.NarrationAudio,DocumentaryMediaAssetType.NarrationAudio,DocumentaryMediaAssetFormat.Wav,DocumentaryMediaVerificationPolicyIds.NarrationAudioVerificationV1)][InlineData(DocumentaryPhysicalArtifactKind.SceneVideo,DocumentaryMediaAssetType.SceneVideo,DocumentaryMediaAssetFormat.Mp4,DocumentaryMediaVerificationPolicyIds.SceneVideoVerificationV1)][InlineData(DocumentaryPhysicalArtifactKind.VariantVideo,DocumentaryMediaAssetType.VariantVideo,DocumentaryMediaAssetFormat.Mp4,DocumentaryMediaVerificationPolicyIds.VariantVideoVerificationV1)] public void Resolves_deterministic_policy(DocumentaryPhysicalArtifactKind kind,DocumentaryMediaAssetType type,DocumentaryMediaAssetFormat format,string id){var r=new DocumentaryMediaVerificationRequest("asset",type,format,kind,"correlation",1);Assert.Equal(id,resolver.Resolve(r).PolicyId);Assert.Equal(id,resolver.Resolve(r).PolicyId);}
 [Fact] public void Rejects_images()=>Assert.Throws<NotSupportedException>(()=>resolver.Resolve(new("asset",DocumentaryMediaAssetType.VisualImage,DocumentaryMediaAssetFormat.Png,DocumentaryPhysicalArtifactKind.VisualImage,"correlation",1)));
}
