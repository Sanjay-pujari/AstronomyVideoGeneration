using System.Text;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

internal static class DocumentaryNarrationTestFixtures
{
    public const string Correlation = "narration-correlation";
    public static AzureSpeechOptions SpeechOptions() => new() { Key="fake-key", Region="eastus", Voices=new(StringComparer.OrdinalIgnoreCase){{"en","en-US-JennyNeural"},{"hi","hi-IN-SwaraNeural"}}, ProsodyRate=new(StringComparer.OrdinalIgnoreCase){{"en","medium"},{"hi","slow"}}, DefaultProsodyRate="medium", SsmlPitch="+2%" };
    public static DocumentaryNarrationSynthesisRequest Request(DocumentaryMediaLanguage language=DocumentaryMediaLanguage.English,string? text=null,int rate=24000,int channels=1)
    {
        text ??= language==DocumentaryMediaLanguage.Hindi ? "आज मंगल पूर्व में उगता है। फिर बृहस्पति दिखाई देता है। आकाश को ध्यान से देखें।" : "Mars rises in the east tonight.\nA few minutes later, Jupiter becomes visible.\nPause briefly, then look toward the southern horizon.\nThis narration would later produce several subtitle cues.";
        var reference=new DocumentaryMediaKnowledgeReference("ref","payload",default,"source","artifact","v1","/",0,Correlation);
        var block=new DocumentaryNarrationBlock("narration",language,text,0,1000,[reference],Correlation);
        var plan=new DocumentaryMediaAssetPlan("narration-asset",DocumentaryMediaAssetType.NarrationAudio,DocumentaryMediaAssetFormat.Wav,language==DocumentaryMediaLanguage.Hindi?DocumentaryMediaVariantType.LongHindi:DocumentaryMediaVariantType.LongEnglish,"scene","instruction",DocumentaryMediaProviderCapability.TextToSpeech,0,[],0,0,1000,0,rate,channels,[reference],Correlation);
        return new(plan,block,"default",language,DocumentaryMediaAssetFormat.Wav,rate,channels,1,Correlation);
    }
    public static async Task<string> WritePcmWavAsync(string path,int sampleRate,short channels,int durationMilliseconds,CancellationToken token=default)
    {
        var samples=sampleRate*durationMilliseconds/1000;var dataLength=samples*channels*2;var bytes=new byte[44+dataLength];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes,0);BitConverter.GetBytes(36+dataLength).CopyTo(bytes,4);Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(bytes,8);BitConverter.GetBytes(16).CopyTo(bytes,16);BitConverter.GetBytes((short)1).CopyTo(bytes,20);BitConverter.GetBytes(channels).CopyTo(bytes,22);BitConverter.GetBytes(sampleRate).CopyTo(bytes,24);BitConverter.GetBytes(sampleRate*channels*2).CopyTo(bytes,28);BitConverter.GetBytes((short)(channels*2)).CopyTo(bytes,32);BitConverter.GetBytes((short)16).CopyTo(bytes,34);Encoding.ASCII.GetBytes("data").CopyTo(bytes,36);BitConverter.GetBytes(dataLength).CopyTo(bytes,40);
        for(var i=44;i<bytes.Length;i++)bytes[i]=(byte)((i*31+7)&0xff);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,bytes,token);return path;
    }
    public static byte[] WavBytes(int rate=24000,short channels=1,int ms=100){var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".wav");WritePcmWavAsync(path,rate,channels,ms).GetAwaiter().GetResult();var b=File.ReadAllBytes(path);File.Delete(path);return b;}
    public static byte[] Mp3(bool id3=false){var frame=new byte[417];frame[0]=0xff;frame[1]=0xfb;frame[2]=0x90;frame[3]=0x64;if(!id3)return frame;return [0x49,0x44,0x33,4,0,0,0,0,0,0,..frame];}
}

internal sealed class FakeAzureSpeechClient : IAzureSpeechClient
{
    public int WavInvocationCount{get;private set;} public int Mp3InvocationCount{get;private set;} public string? CapturedSsml{get;private set;} public string? CapturedText{get;private set;} public AzureSpeechOptions? CapturedOptions{get;private set;} public CancellationToken CapturedCancellationToken{get;private set;} public byte[] Bytes{get;set;}=DocumentaryNarrationTestFixtures.WavBytes(); public Exception? Exception{get;set;} public bool WaitForCancellation{get;set;}
    public Task<byte[]> SynthesizeMp3Async(string text,AzureSpeechOptions options,CancellationToken token){Mp3InvocationCount++;CapturedText=text;CapturedOptions=options;CapturedCancellationToken=token;return Execute(token);}
    public Task<byte[]> SynthesizeWavSsmlAsync(string ssml,AzureSpeechOptions options,CancellationToken token){WavInvocationCount++;CapturedSsml=ssml;CapturedOptions=options;CapturedCancellationToken=token;return Execute(token);}
    private async Task<byte[]> Execute(CancellationToken token){if(WaitForCancellation)await Task.Delay(Timeout.Infinite,token);if(Exception is not null)throw Exception;return Bytes;}
}
internal sealed class FakeSsmlBuilder : ISsmlBuilder { public int InvocationCount{get;private set;} public string? CapturedText{get;private set;} public string? CapturedVoice{get;private set;} public string BuildSsml(string text,string voice,SsmlNarrationProfile? profile=null,string? rateOverride=null,string? pitchOverride=null){InvocationCount++;CapturedText=text;CapturedVoice=voice;return $"<speak voice='{voice}'>{text}</speak>";} }
internal sealed class FakeNarrationProviderBinding : IDocumentaryNarrationProviderBinding
{
    public string ProviderId{get;set;}=DocumentaryNarrationProviderIds.AzureSpeech;public int InvocationCount{get;private set;}public DocumentaryNarrationProviderRequest? CapturedRequest{get;private set;}public Exception? Exception{get;set;}public DocumentaryProductionFailure? Failure{get;set;}public bool WaitForCancellation{get;set;}public int OutputRate{get;set;}=24000;public short OutputChannels{get;set;}=1;
    public async Task<DocumentaryNarrationProviderResponse> SynthesizeAsync(DocumentaryNarrationProviderRequest request,CancellationToken token){InvocationCount++;CapturedRequest=request;if(WaitForCancellation)await Task.Delay(Timeout.Infinite,token);if(Exception is not null)throw Exception;if(Failure is not null)return new(null,null,Failure:Failure);var path=Path.Combine(request.OutputDirectory,"provider-azure-speech.wav");await DocumentaryNarrationTestFixtures.WritePcmWavAsync(path,OutputRate,OutputChannels,100,token);return new(path,DocumentaryMediaAssetFormat.Wav,"fake-request");}
}
internal sealed class FakeNarrationAudioNormalizer : IDocumentaryNarrationAudioNormalizer { public int InvocationCount{get;private set;}public string? Source{get;private set;}public string? Destination{get;private set;}public DocumentaryNarrationAudioProfile? Profile{get;private set;}public bool ReturnNull{get;set;}public bool WaitForCancellation{get;set;}public async Task<DocumentaryNarrationNormalizationResult> NormalizeAsync(string source,string destination,DocumentaryNarrationAudioProfile profile,CancellationToken token){InvocationCount++;Source=source;Destination=destination;Profile=profile;if(WaitForCancellation)await Task.Delay(Timeout.Infinite,token);if(ReturnNull)return new(true,null,null);await DocumentaryNarrationTestFixtures.WritePcmWavAsync(destination,profile.SampleRate,(short)profile.ChannelCount,100,token);return new(true,destination,null);} }
