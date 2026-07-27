using Astronomy.MediaFactory.ProductionAdapters;
using Xunit;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryVariantVideoInspectorTests : IDisposable
{
 readonly string root=Path.Combine(Path.GetTempPath(),"a38-inspector-"+Guid.NewGuid().ToString("N"));
 public DocumentaryVariantVideoInspectorTests()=>Directory.CreateDirectory(root);
 [Fact] public async Task Valid_probe_result_is_mapped_to_variant_inspection(){var (i,p,path)=Create();var x=await i.InspectAsync(path,default);Assert.True(x.Succeeded);Assert.Equal("mp4",x.Format);Assert.Equal(6000,x.DurationMilliseconds);Assert.Equal(1920,x.Width);Assert.Equal(1080,x.Height);Assert.Equal(30,x.FrameRate);Assert.True(x.HasVideo);Assert.True(x.HasAudio);Assert.Equal(path,p.CapturedPath);}
 [Fact] public async Task Variant_without_audio_is_reported_without_failure(){var (i,p,path)=Create();p.Output=p.Output with{HasAudioStream=false};var x=await i.InspectAsync(path,default);Assert.True(x.Succeeded);Assert.False(x.HasAudio);}
 [Fact] public async Task Missing_file_returns_OutputArtifactMissing(){var p=new FakeDocumentaryMediaProbe();var x=await new DocumentaryVariantVideoInspector(p).InspectAsync(Path.Combine(root,"missing.mp4"),default);Assert.Equal(DocumentaryProductionFailureCode.OutputArtifactMissing,x.Failure?.Code);Assert.Equal(0,p.InvocationCount);}
 [Fact] public async Task Empty_file_returns_OutputArtifactEmpty(){var path=Path.Combine(root,"empty.mp4");File.WriteAllBytes(path,[]);var p=new FakeDocumentaryMediaProbe();var x=await new DocumentaryVariantVideoInspector(p).InspectAsync(path,default);Assert.Equal(DocumentaryProductionFailureCode.OutputArtifactEmpty,x.Failure?.Code);Assert.Equal(0,p.InvocationCount);}
 [Fact] public async Task Probe_failure_is_preserved_safely(){var (i,p,path)=Create();p.Output=new(false,Failure:new(DocumentaryProductionFailureCode.ProcessExitedWithError,"raw secret"));var x=await i.InspectAsync(path,default);Assert.Equal(DocumentaryProductionFailureCode.ProcessExitedWithError,x.Failure?.Code);Assert.DoesNotContain("raw secret",x.Failure!.Message);}
 [Fact] public async Task Missing_video_stream_returns_VideoStreamMissing(){var (i,p,path)=Create();p.Output=p.Output with{HasVideoStream=false};Assert.Equal(DocumentaryProductionFailureCode.VideoStreamMissing,(await i.InspectAsync(path,default)).Failure?.Code);}
 [Theory] [InlineData("duration-null")] [InlineData("duration-zero")] [InlineData("width-null")] [InlineData("width-zero")] [InlineData("height-null")] [InlineData("height-zero")] [InlineData("rate-null")] [InlineData("rate-zero")]
 public async Task Invalid_metadata_returns_OutputFormatInvalid(string field){var (i,p,path)=Create();p.Output=field switch{"duration-null"=>p.Output with{DurationMilliseconds=null},"duration-zero"=>p.Output with{DurationMilliseconds=0},"width-null"=>p.Output with{Width=null},"width-zero"=>p.Output with{Width=0},"height-null"=>p.Output with{Height=null},"height-zero"=>p.Output with{Height=0},"rate-null"=>p.Output with{FrameRate=null},_=>p.Output with{FrameRate=0}};Assert.Equal(DocumentaryProductionFailureCode.OutputFormatInvalid,(await i.InspectAsync(path,default)).Failure?.Code);}
 [Fact] public async Task Inspector_propagates_caller_cancellation(){var (i,p,path)=Create();using(var before=new CancellationTokenSource()){before.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>i.InspectAsync(path,before.Token));Assert.Equal(0,p.InvocationCount);}p.WaitForCancellation=true;using var during=new CancellationTokenSource();var task=i.InspectAsync(path,during.Token);during.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);Assert.Equal(during.Token,p.CapturedCancellationToken);}
 [Fact] public async Task Probe_exception_is_left_for_adapter_boundary_normalization(){var (i,p,path)=Create();p.Exception=new InvalidOperationException("probe");await Assert.ThrowsAsync<InvalidOperationException>(()=>i.InspectAsync(path,default));}
 (DocumentaryVariantVideoInspector,FakeDocumentaryMediaProbe,string) Create(){var path=Path.Combine(root,"variant.mp4");File.WriteAllText(path,"video");var p=new FakeDocumentaryMediaProbe();return(new(p),p,path);}
 public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
internal sealed class FakeDocumentaryMediaProbe:IDocumentaryMediaProbe
{
 public int InvocationCount{get;private set;} public string? CapturedPath{get;private set;} public CancellationToken CapturedCancellationToken{get;private set;} public DocumentaryMediaProbeResult Output{get;set;}=new(true,6000,true,true,false,1920,1080,30,48000,2,"mp4");public Exception? Exception{get;set;}public bool WaitForCancellation{get;set;}
 public async Task<DocumentaryMediaProbeResult> ProbeAsync(string path,CancellationToken token){InvocationCount++;CapturedPath=path;CapturedCancellationToken=token;if(WaitForCancellation)await Task.Delay(Timeout.Infinite,token);if(Exception is not null)throw Exception;return Output;}
}
