using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaPipelineInventoryTests
{
 [Theory]
 [InlineData(typeof(DocumentaryMediaPipelineStatus),"Planned,Complete,PartiallyComplete,Rejected")]
 [InlineData(typeof(DocumentaryMediaPipelineMode),"PlanOnly,Execute")]
 [InlineData(typeof(DocumentaryMediaAssetStatus),"Planned,Generated,Verified,Failed")]
 [InlineData(typeof(DocumentaryMediaAssetType),"VisualImage,SkySimulationImage,StarChartImage,TelescopeViewImage,ScientificDiagramImage,HistoricalIllustrationImage,NarrationAudio,SubtitleDocument,SceneVideo,VariantVideo")]
 [InlineData(typeof(DocumentaryMediaAssetFormat),"Png,Jpeg,WebP,Wav,Mp3,Aac,Srt,Vtt,Mp4")]
 [InlineData(typeof(DocumentaryMediaExecutionStage),"ValidateProject,PlanAssets,GenerateVisuals,SynthesizeNarration,GenerateSubtitles,ComposeScenes,ComposeVariant,VerifyVariant,BuildManifest")]
 [InlineData(typeof(DocumentaryMediaProviderCapability),"GeneratedIllustration,SkySimulation,StarChart,TelescopeView,ScientificDiagram,HistoricalIllustration,TextToSpeech,SubtitleGeneration,SceneComposition,VideoComposition,RenderVerification")]
 public void Inventories_have_certified_order(Type type,string expected)=>Assert.Equal(expected,string.Join(',',Enum.GetNames(type)));
 [Fact] public void Rejection_inventory_is_contiguous_and_stable(){var values=Enum.GetValues<DocumentaryMediaPipelineRejectionReason>();Assert.Equal(28,values.Length);Assert.Equal(Enumerable.Range(0,28),values.Select(Convert.ToInt32));Assert.Equal(nameof(DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch),values[^1].ToString());}
 [Fact] public void Checksum_profile_is_SHA256_schema_1(){var x=new DocumentaryMediaChecksumProfile();Assert.Equal("SHA-256",x.Algorithm);Assert.Equal("1.0",x.SchemaVersion);Assert.Throws<ArgumentException>(()=>new("MD5"));}
}
