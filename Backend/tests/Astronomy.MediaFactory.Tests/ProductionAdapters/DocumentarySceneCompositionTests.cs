using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.ProductionAdapters;
using Astronomy.MediaFactory.Rendering;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentarySceneVideoInspectorTests
{
    [Fact]
    public async Task Valid_MP4_scene_reports_measured_stream_metadata()
    {
        var path=Path.GetTempFileName();await File.WriteAllBytesAsync(path,[1]);
        try { var result=await new DocumentarySceneVideoInspector(new Probe(true,true,1920,1080,30,2000)).InspectAsync(path,default);result.Succeeded.Should().BeTrue();result.HasAudio.Should().BeTrue();result.Width.Should().Be(1920);result.DurationMilliseconds.Should().Be(2000); }
        finally { File.Delete(path); }
    }

    [Fact] public async Task Missing_scene_is_rejected(){var result=await new DocumentarySceneVideoInspector(new Probe(true,false,1,1,1,1)).InspectAsync(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".mp4"),default);result.Failure!.Code.Should().Be(DocumentaryProductionFailureCode.OutputArtifactMissing);}
    [Fact] public async Task Empty_scene_is_rejected(){var path=Path.GetTempFileName();try{var result=await new DocumentarySceneVideoInspector(new Probe(true,false,1,1,1,1)).InspectAsync(path,default);result.Failure!.Code.Should().Be(DocumentaryProductionFailureCode.OutputArtifactEmpty);}finally{File.Delete(path);}}
    [Fact] public async Task Video_stream_is_required(){var path=Path.GetTempFileName();await File.WriteAllBytesAsync(path,[1]);try{var result=await new DocumentarySceneVideoInspector(new Probe(false,false,1,1,1,1)).InspectAsync(path,default);result.Failure!.Code.Should().Be(DocumentaryProductionFailureCode.VideoStreamMissing);}finally{File.Delete(path);}}
    [Fact] public async Task Caller_cancellation_propagates(){using var cts=new CancellationTokenSource();cts.Cancel();var action=()=>new DocumentarySceneVideoInspector(new Probe(true,false,1,1,1,1)).InspectAsync("ignored",cts.Token);await action.Should().ThrowAsync<OperationCanceledException>();}

    private sealed class Probe(bool video,bool audio,int width,int height,decimal fps,long duration):IDocumentaryMediaProbe
    { public Task<DocumentaryMediaProbeResult> ProbeAsync(string path,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(new DocumentaryMediaProbeResult(true,duration,video,audio,false,width,height,fps,ContainerFormat:"mp4"));} }
}

public sealed class ExistingFFmpegDocumentarySceneCommandTests
{
    [Fact]
    public void Same_scene_request_produces_same_command_model()
    {
        var builder=new FfmpegArgumentBuilder();var options=new RenderingOptions();
        var first=builder.BuildScene(options,["a.png","b.jpg"],"n.wav","s.srt","provider-scene.mp4",1920,1080,30,2000);
        var second=builder.BuildScene(options,["a.png","b.jpg"],"n.wav","s.srt","provider-scene.mp4",1920,1080,30,2000);
        first.Should().Be(second);first.Should().Contain("force_original_aspect_ratio=decrease");first.Should().Contain("setsar=1");first.Should().Contain("concat=n=2");first.Should().Contain("-c:a aac");
    }
    [Fact] public void Silent_scene_has_no_artificial_audio(){new FfmpegArgumentBuilder().BuildScene(new(),["a.png"],null,null,"provider-scene.mp4",1080,1920,30,1000).Should().NotContain("-c:a");}
    [Fact] public void Subtitle_none_does_not_pass_subtitle_input(){new FfmpegArgumentBuilder().BuildScene(new(),["a.png"],null,null,"provider-scene.mp4",1080,1920,30,1000).Should().NotContain("subtitles=");}
    [Fact] public void Provider_identity_is_stable(){DocumentarySceneCompositionProviderIds.ExistingFFmpegSceneComposer.Should().Be("ExistingFFmpegSceneComposer");}
}
