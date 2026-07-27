using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryNarrationAudioInspectorTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"narration-inspection-"+Guid.NewGuid().ToString("N"));
    [Theory] [InlineData(16000,1)] [InlineData(24000,1)] [InlineData(48000,2)]
    public async Task Valid_pcm_wav_reports_profile_and_duration(int rate,int channels){var path=Path.Combine(root,$"{rate}-{channels}.wav");await DocumentaryNarrationTestFixtures.WritePcmWavAsync(path,rate,(short)channels,250);var result=await new DocumentaryNarrationAudioInspector().InspectAsync(path,default);Assert.True(result.Succeeded);Assert.Equal(rate,result.SampleRate);Assert.Equal(channels,result.ChannelCount);Assert.Equal(250,result.DurationMilliseconds);}
    [Fact] public async Task Missing_file_is_reported(){var r=await Inspect(Path.Combine(root,"missing.wav"));Assert.Equal(DocumentaryProductionFailureCode.OutputArtifactMissing,r.Failure!.Code);}
    [Fact] public async Task Empty_file_is_reported(){Directory.CreateDirectory(root);var p=Path.Combine(root,"empty.wav");await File.WriteAllBytesAsync(p,[]);var r=await Inspect(p);Assert.Equal(DocumentaryProductionFailureCode.OutputArtifactEmpty,r.Failure!.Code);}
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] public async Task Short_buffers_never_throw(int length){Directory.CreateDirectory(root);var p=Path.Combine(root,"short"+length);await File.WriteAllBytesAsync(p,new byte[length]);var r=await Inspect(p);Assert.False(r.Succeeded);Assert.Equal(length==0?DocumentaryProductionFailureCode.OutputArtifactEmpty:DocumentaryProductionFailureCode.OutputFormatInvalid,r.Failure!.Code);}
    [Theory] [InlineData("riff")] [InlineData("wave")] [InlineData("channels")] [InlineData("rate")] [InlineData("byte-rate")] [InlineData("data")] [InlineData("truncated")]
    public async Task Invalid_wav_headers_are_rejected(string defect){var b=DocumentaryNarrationTestFixtures.WavBytes();switch(defect){case"riff":b[0]=0;break;case"wave":b[8]=0;break;case"channels":b[22]=b[23]=0;break;case"rate":Array.Clear(b,24,4);break;case"byte-rate":Array.Clear(b,28,4);break;case"data":b[36]=(byte)'x';break;case"truncated":b=b[..^10];break;}var p=await Write(defect,b);var r=await Inspect(p);Assert.False(r.Succeeded);Assert.Equal(DocumentaryProductionFailureCode.OutputFormatInvalid,r.Failure!.Code);}
    [Theory] [InlineData(false)] [InlineData(true)] public async Task Supported_mp3_frame_paths_are_measured(bool id3){var r=await Inspect(await Write("audio.mp3",DocumentaryNarrationTestFixtures.Mp3(id3)));Assert.True(r.Succeeded);Assert.Equal(DocumentaryMediaAssetFormat.Mp3,r.Format);Assert.Equal(44100,r.SampleRate);Assert.Equal(2,r.ChannelCount);Assert.True(r.DurationMilliseconds>0);}
    [Theory] [InlineData(0x00,0x00)] [InlineData(0xff,0xfb)] public async Task Invalid_mp3_marker_or_header_is_stable(byte first,byte second){var b=DocumentaryNarrationTestFixtures.Mp3();b[0]=first;b[1]=second;if(first==0xff){b[2]=0xff;}var r=await Inspect(await Write("bad.mp3",b));Assert.False(r.Succeeded);Assert.Equal(first==0xff?DocumentaryProductionFailureCode.DurationMeasurementFailed:DocumentaryProductionFailureCode.OutputFormatInvalid,r.Failure!.Code);}
    [Theory] [InlineData(0)] [InlineData(15)] public async Task Invalid_mp3_bitrate_index_fails_duration(int index){var b=DocumentaryNarrationTestFixtures.Mp3();b[2]=(byte)((index<<4)|(b[2]&15));var r=await Inspect(await Write("bitrate.mp3",b));Assert.Equal(DocumentaryProductionFailureCode.DurationMeasurementFailed,r.Failure!.Code);}
    [Fact] public async Task Invalid_mp3_sample_rate_index_fails_duration(){var b=DocumentaryNarrationTestFixtures.Mp3();b[2]=(byte)((b[2]&0xf3)|0x0c);var r=await Inspect(await Write("rate.mp3",b));Assert.Equal(DocumentaryProductionFailureCode.DurationMeasurementFailed,r.Failure!.Code);}
    [Fact] public async Task Cancellation_is_checked_first(){using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new DocumentaryNarrationAudioInspector().InspectAsync("missing",cts.Token));}
    private static Task<DocumentaryNarrationAudioInspectionResult> Inspect(string p)=>new DocumentaryNarrationAudioInspector().InspectAsync(p,default);
    private async Task<string> Write(string name,byte[] bytes){Directory.CreateDirectory(root);var p=Path.Combine(root,name);await File.WriteAllBytesAsync(p,bytes);return p;}
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}

public sealed class ExistingDocumentaryNarrationAudioNormalizerTests
{
    [Fact] public async Task Default_normalizer_is_deterministically_unavailable(){var n=new ExistingDocumentaryNarrationAudioNormalizer();var a=await n.NormalizeAsync("a","b",new(DocumentaryMediaAssetFormat.Wav,24000,1),default);var b=await n.NormalizeAsync("a","b",new(DocumentaryMediaAssetFormat.Wav,24000,1),default);Assert.False(a.Succeeded);Assert.Null(a.OutputPath);Assert.Equal(DocumentaryProductionFailureCode.DependencyMissing,a.Failure!.Code);Assert.Equal(a,b);}
    [Fact] public async Task Default_normalizer_checks_cancellation_first(){using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ExistingDocumentaryNarrationAudioNormalizer().NormalizeAsync("a","b",new(DocumentaryMediaAssetFormat.Wav,24000,1),cts.Token));}
}
