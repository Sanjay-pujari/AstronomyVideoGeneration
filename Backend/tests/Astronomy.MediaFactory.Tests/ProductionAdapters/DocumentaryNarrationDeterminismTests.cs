using Astronomy.MediaFactory.ProductionAdapters;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryNarrationDeterminismTests
{
    [Fact] public async Task Same_inputs_produce_same_voice_ssml_checksum_and_identity(){var request=DocumentaryNarrationTestFixtures.Request();var resolver=new DocumentaryNarrationVoiceResolver(Options.Create(DocumentaryNarrationTestFixtures.SpeechOptions()));Assert.Equal(resolver.Resolve(request),resolver.Resolve(request));var ssml=new SsmlBuilder();Assert.Equal(ssml.BuildSsml(request.NarrationBlock.Text,"en-US-JennyNeural"),ssml.BuildSsml(request.NarrationBlock.Text,"en-US-JennyNeural"));var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));try{var a=await DocumentaryNarrationTestFixtures.WritePcmWavAsync(Path.Combine(root,"a.wav"),24000,1,100);var b=await DocumentaryNarrationTestFixtures.WritePcmWavAsync(Path.Combine(root,"b.wav"),24000,1,100);var checksums=new DocumentaryChecksumService();var x=await checksums.ComputeSha256Async(a,default);var y=await checksums.ComputeSha256Async(b,default);Assert.Equal(x,y);var identities=new DocumentaryContentIdentityFactory();Assert.Equal(identities.Create(x),identities.Create(y));}finally{if(Directory.Exists(root))Directory.Delete(root,true);}}
    [Fact] public void Same_failure_input_has_same_stable_code(){var n=new DocumentaryProductionFailureNormalizer();Assert.Equal(n.Normalize(new TimeoutException(),DocumentaryProductionOperationKind.NarrationSynthesis,false).Code,n.Normalize(new TimeoutException(),DocumentaryProductionOperationKind.NarrationSynthesis,false).Code);}
}
