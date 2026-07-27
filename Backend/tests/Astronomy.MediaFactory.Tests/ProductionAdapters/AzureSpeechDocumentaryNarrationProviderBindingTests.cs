using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class AzureSpeechDocumentaryNarrationProviderBindingTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"narration-binding-"+Guid.NewGuid().ToString("N"));
    [Fact] public async Task Wav_success_passes_ssml_once_and_writes_owned_file(){var client=new FakeAzureSpeechClient();var result=await Binding(client).SynthesizeAsync(Request(DocumentaryMediaAssetFormat.Wav),default);Assert.Null(result.Failure);Assert.Equal(1,client.WavInvocationCount);Assert.Equal(0,client.Mp3InvocationCount);Assert.Equal("<exact/>",client.CapturedSsml);Assert.Equal("provider-azure-speech.wav",Path.GetFileName(result.OutputPath));Assert.True(DocumentaryPathComparison.IsBelow(root,result.OutputPath!));Assert.Equal(DocumentaryMediaAssetFormat.Wav,result.Format);}
    [Fact] public async Task Mp3_success_passes_plain_text_and_clones_voice_options(){var original=DocumentaryNarrationTestFixtures.SpeechOptions();var client=new FakeAzureSpeechClient{Bytes=DocumentaryNarrationTestFixtures.Mp3()};var result=await Binding(client,original).SynthesizeAsync(Request(DocumentaryMediaAssetFormat.Mp3),default);Assert.Null(result.Failure);Assert.Equal(1,client.Mp3InvocationCount);Assert.Equal(0,client.WavInvocationCount);Assert.Equal("narration text",client.CapturedText);Assert.Equal("en-GB-SoniaNeural",client.CapturedOptions!.Voices["en"]);Assert.Equal("en-US-JennyNeural",original.Voices["en"]);Assert.Equal("provider-azure-speech.mp3",Path.GetFileName(result.OutputPath));}
    [Theory] [InlineData(false,"","","")] [InlineData(true,"eastus","","")] public async Task Missing_configuration_fails(bool managed,string region,string resource,string key){var o=DocumentaryNarrationTestFixtures.SpeechOptions();o.UseManagedIdentity=managed;o.Region=region;o.ResourceId=resource;o.Key=key;var result=await Binding(new(),o).SynthesizeAsync(Request(DocumentaryMediaAssetFormat.Wav),default);Assert.Equal(DocumentaryProductionFailureCode.ConfigurationMissing,result.Failure!.Code);}
    [Theory] [InlineData(false,"eastus","","")] [InlineData(false,"","https://example.invalid/speech","")] [InlineData(true,"eastus","","/subscriptions/fake/resource")] public async Task Valid_configuration_reaches_fake_client(bool managed,string region,string endpoint,string resource){var o=DocumentaryNarrationTestFixtures.SpeechOptions();o.UseManagedIdentity=managed;o.Region=region;o.Endpoint=endpoint;o.ResourceId=resource;var client=new FakeAzureSpeechClient();var result=await Binding(client,o).SynthesizeAsync(Request(DocumentaryMediaAssetFormat.Wav),default);Assert.True(result.Failure is null);Assert.Equal(1,client.WavInvocationCount);}
    [Theory]
    [InlineData(typeof(TimeoutException),"timeout",DocumentaryProductionFailureCode.ProviderTimeout)]
    [InlineData(typeof(UnauthorizedAccessException),"denied",DocumentaryProductionFailureCode.ProviderAuthenticationFailed)]
    [InlineData(typeof(InvalidOperationException),"quota",DocumentaryProductionFailureCode.ProviderRateLimited)]
    [InlineData(typeof(InvalidOperationException),"429",DocumentaryProductionFailureCode.ProviderRateLimited)]
    [InlineData(typeof(InvalidOperationException),"BadRequest",DocumentaryProductionFailureCode.ProviderRejectedRequest)]
    [InlineData(typeof(InvalidOperationException),"other",DocumentaryProductionFailureCode.ProviderUnavailable)]
    public async Task Provider_exceptions_are_stable(Type type,string message,DocumentaryProductionFailureCode code){var client=new FakeAzureSpeechClient{Exception=(Exception)Activator.CreateInstance(type,message)!};var result=await Binding(client).SynthesizeAsync(Request(DocumentaryMediaAssetFormat.Wav),default);Assert.Equal(code,result.Failure!.Code);Assert.Null(result.OutputPath);}
    [Fact] public async Task Empty_bytes_fail(){var result=await Binding(new(){Bytes=[]}).SynthesizeAsync(Request(DocumentaryMediaAssetFormat.Wav),default);Assert.Equal(DocumentaryProductionFailureCode.OutputArtifactEmpty,result.Failure!.Code);}
    [Theory] [InlineData(DocumentaryMediaAssetFormat.Aac)] [InlineData(DocumentaryMediaAssetFormat.Png)] public async Task Unsupported_format_is_rejected(DocumentaryMediaAssetFormat format){var result=await Binding(new()).SynthesizeAsync(Request(format),default);Assert.Equal(DocumentaryProductionFailureCode.ProviderRejectedRequest,result.Failure!.Code);}
    [Fact] public async Task Invalid_translated_request_is_rejected(){var result=await Binding(new()).SynthesizeAsync(Request(DocumentaryMediaAssetFormat.Wav) with{NarrationText=""},default);Assert.Equal(DocumentaryProductionFailureCode.ProviderRejectedRequest,result.Failure!.Code);}
    [Fact] public async Task Caller_cancellation_propagates_without_file(){var client=new FakeAzureSpeechClient{WaitForCancellation=true};using var cts=new CancellationTokenSource();var task=Binding(client).SynthesizeAsync(Request(DocumentaryMediaAssetFormat.Wav),cts.Token);cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);Assert.False(File.Exists(Path.Combine(root,"provider-azure-speech.wav")));}
    private AzureSpeechDocumentaryNarrationProviderBinding Binding(FakeAzureSpeechClient client,AzureSpeechOptions? o=null)=>new(client,new FakeSsmlBuilder(),Options.Create(o??DocumentaryNarrationTestFixtures.SpeechOptions()));
    private DocumentaryNarrationProviderRequest Request(DocumentaryMediaAssetFormat format)=>new("asset","instruction",null,"LongEnglish",DocumentaryNarrationTestFixtures.Correlation,DocumentaryMediaLanguage.English,"en-IN","en-GB-SoniaNeural","narration text","<exact/>",format,24000,1,1,root);
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
