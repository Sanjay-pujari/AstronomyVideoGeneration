using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaPipelinePolicyTests
{
 [Fact] public void Schema_1_policy_freezes_certified_media_profile(){var p=DocumentaryMediaPipelineFixture.Execute();Assert.Equal(4,p.RequiredVariantTypes.Count);Assert.Equal(Enum.GetValues<DocumentaryMediaAssetType>(),p.SupportedAssetTypes);Assert.Equal(Enum.GetValues<DocumentaryMediaAssetFormat>(),p.SupportedAssetFormats);Assert.Equal(DocumentaryMediaAssetFormat.Png,p.VisualImageFormat);Assert.Equal(DocumentaryMediaAssetFormat.Wav,p.NarrationMasterFormat);Assert.Equal(DocumentaryMediaAssetFormat.Aac,p.NarrationDeliveryFormat);Assert.Equal(DocumentaryMediaAssetFormat.Srt,p.SubtitleFormat);Assert.Equal(DocumentaryMediaAssetFormat.Mp4,p.VideoFormat);Assert.Equal((1920,1080,1080,1920),(p.LongWidth,p.LongHeight,p.ShortWidth,p.ShortHeight));Assert.Equal((30,30,48000,2),(p.LongFrameRate,p.ShortFrameRate,p.AudioSampleRate,p.AudioChannelCount));Assert.Equal("1.0",p.PipelineSchemaVersion);}
 [Theory][InlineData(0,1,1)][InlineData(1,0,1)][InlineData(1,1,0)] public void Attempts_must_be_positive(int v,int n,int c)=>Assert.Throws<ArgumentOutOfRangeException>(()=>new(DocumentaryMediaPipelineMode.Execute,true,v,n,c));
 [Fact] public void Equivalent_policy_has_identical_web_json(){var a=DocumentaryMediaPipelineFixture.Execute();var b=new DocumentaryMediaPipelinePolicy(DocumentaryMediaPipelineMode.Execute,true,2,2,2);var o=new JsonSerializerOptions(JsonSerializerDefaults.Web);Assert.Equal(JsonSerializer.Serialize(a,o),JsonSerializer.Serialize(b,o));}
}
