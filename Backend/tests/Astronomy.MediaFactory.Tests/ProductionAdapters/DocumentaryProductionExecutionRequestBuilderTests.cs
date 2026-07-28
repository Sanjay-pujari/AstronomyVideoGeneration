using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionRequestBuilderTests
{
 [Theory]
 [InlineData(DocumentaryMediaLanguage.English,"en-IN-NeerjaNeural")]
 [InlineData(DocumentaryMediaLanguage.Hindi,"hi-IN-SwaraNeural")]
 public void Narration_request_uses_existing_configured_voice(DocumentaryMediaLanguage language,string voice)
 {
  var options=new AzureSpeechOptions{Voices=new Dictionary<string,string>{{"en","en-IN-NeerjaNeural"},{"hi","hi-IN-SwaraNeural"}}};
  var builder=new DocumentaryProductionExecutionRequestBuilder(new DocumentaryNarrationVoiceResolver(Options.Create(options)));
  var block=new DocumentaryNarrationBlock("narration",language,"The sky",0,1000,[],"correlation");
  var request=builder.Narration(Plan(),block,language,1);
  request.VoiceProfileId.Should().Be(voice);
 }

 [Fact]
 public void Verification_request_preserves_audio_and_subtitle_policy()
 {
  var builder=new DocumentaryProductionExecutionRequestBuilder(new DocumentaryNarrationVoiceResolver(Options.Create(new AzureSpeechOptions())));
  builder.Verification(Plan(),DocumentaryPhysicalArtifactKind.SceneVideo,"variant","scene",1,DocumentaryProductionSubtitleStrategy.Embedded,false).Should().Match<DocumentaryMediaVerificationRequest>(x=>!x.RequireAudio&&x.RequireSubtitle);
  builder.Verification(Plan(),DocumentaryPhysicalArtifactKind.SceneVideo,"variant","scene",1,DocumentaryProductionSubtitleStrategy.Sidecar,true).Should().Match<DocumentaryMediaVerificationRequest>(x=>x.RequireAudio&&!x.RequireSubtitle);
 }

 private static DocumentaryMediaAssetPlan Plan()=>new("asset",DocumentaryMediaAssetType.NarrationAudio,DocumentaryMediaAssetFormat.Wav,DocumentaryMediaVariantType.EnglishLong,"scene","source",DocumentaryMediaProviderCapability.TextToSpeech,0,[],1920,1080,1000,30,48000,2,[],"correlation");
}
