using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentaryVariantCompatibilityValidatorTests
{
 static DocumentaryResolvedVariantDependencies Dependencies(int width=1920,int height=1080,decimal rate=30,bool? audio=true)=>new([new("scene","scene",1,"/scene.mp4",1000,width,height,rate,audio,"sha256:"+new string('a',64),new string('a',64))],"long-en",DocumentaryMediaVariantType.LongEnglish,"correlation",1000,1920,1080,30,true);
 [Fact] public void Matching_mp4_scene_is_accepted()=>new DocumentaryVariantCompatibilityValidator().Validate(Dependencies(),DocumentaryMediaAssetFormat.Mp4,DocumentaryVariantAudioPolicy.RequireAudio,.01m).Should().BeNull();
 [Fact] public void Non_mp4_output_is_rejected()=>new DocumentaryVariantCompatibilityValidator().Validate(Dependencies(),DocumentaryMediaAssetFormat.Wav,DocumentaryVariantAudioPolicy.RequireAudio,.01m)!.Code.Should().Be(DocumentaryProductionFailureCode.ProviderRejectedRequest);
 [Fact] public void Dimension_mismatch_is_rejected()=>new DocumentaryVariantCompatibilityValidator().Validate(Dependencies(width:1080),DocumentaryMediaAssetFormat.Mp4,DocumentaryVariantAudioPolicy.RequireAudio,.01m)!.Code.Should().Be(DocumentaryProductionFailureCode.DimensionMismatch);
 [Fact] public void Frame_rate_tolerance_is_enforced(){var validator=new DocumentaryVariantCompatibilityValidator();validator.Validate(Dependencies(rate:30.01m),DocumentaryMediaAssetFormat.Mp4,DocumentaryVariantAudioPolicy.RequireAudio,.01m).Should().BeNull();validator.Validate(Dependencies(rate:29m),DocumentaryMediaAssetFormat.Mp4,DocumentaryVariantAudioPolicy.RequireAudio,.01m)!.Code.Should().Be(DocumentaryProductionFailureCode.OutputFormatInvalid);}
 [Fact] public void Audio_policy_is_enforced(){var validator=new DocumentaryVariantCompatibilityValidator();validator.Validate(Dependencies(audio:false),DocumentaryMediaAssetFormat.Mp4,DocumentaryVariantAudioPolicy.RequireAudio,.01m)!.Code.Should().Be(DocumentaryProductionFailureCode.AudioStreamMissing);validator.Validate(Dependencies(audio:false),DocumentaryMediaAssetFormat.Mp4,DocumentaryVariantAudioPolicy.VideoOnly,.01m).Should().BeNull();}
}
