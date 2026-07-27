using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

internal static class DocumentarySceneCompositionTestFixtures
{
    public const string Correlation = "scene-correlation";
    public static DocumentarySceneCompositionProviderRequest ProviderRequest(string directory, DocumentarySceneSubtitleMode subtitles=DocumentarySceneSubtitleMode.None, IReadOnlyList<string>? visuals=null) =>
        new("scene-video","instruction","scene-1",1,"LongEnglish",Correlation,1,directory,Path.Combine(directory,"ignored.mp4"),DocumentaryMediaAssetFormat.Mp4,1920,1080,30,2000,visuals??["one.png","two.jpg"],"narration.wav",subtitles==DocumentarySceneSubtitleMode.BurnIn?"subtitle.srt":null,subtitles,DocumentaryCameraMotion.SlowZoomIn,DocumentarySceneTransition.CrossFade,"IntermediateSegment","AAC");
    public static DocumentarySceneCompositionRequest Request(IReadOnlyList<DocumentaryMediaAssetResult>? visuals=null, DocumentaryMediaAssetStatus narration=DocumentaryMediaAssetStatus.Planned, DocumentaryMediaAssetStatus subtitle=DocumentaryMediaAssetStatus.Planned, bool emptyVisualPrompts=false)
    {
        var reference=new DocumentaryMediaKnowledgeReference("ref","payload",default,"source","artifact","v1","/",0,Correlation);
        var narrationBlock=new DocumentaryNarrationBlock("narration",DocumentaryMediaLanguage.English,"Mars rises.",0,2000,[reference],Correlation);
        var cue=new DocumentarySubtitleCue("cue",DocumentaryMediaLanguage.English,"Mars rises.","Mars rises.",null,default,0,2000,0,"narration",[reference],Correlation);
        var visualPrompt=new DocumentaryVisualPrompt("prompt",default,"Mars in the sky",null,"","16:9",DocumentaryCameraMotion.SlowZoomIn,["mars"],[reference],0,Correlation);
        var scene=new DocumentaryMediaScene("scene-1",DocumentaryMediaVariantType.LongEnglish,default,"Mars",1,[narrationBlock],[cue],emptyVisualPrompts?[]:[visualPrompt],new("timing",0,2000,2000,2000,0,0,Correlation),DocumentarySceneTransition.CrossFade,[reference],Correlation);
        var dependencies=(visuals??[Asset("visual",DocumentaryMediaAssetType.VisualImage,DocumentaryMediaAssetFormat.Png,DocumentaryMediaAssetStatus.Generated)]).Select((x,i)=>new DocumentaryMediaAssetDependency("dependency-"+i,x.AssetId,"scene-video",i,Correlation)).ToArray();
        var plan=new DocumentaryMediaAssetPlan("scene-video",DocumentaryMediaAssetType.SceneVideo,DocumentaryMediaAssetFormat.Mp4,DocumentaryMediaVariantType.LongEnglish,"scene-1","instruction",DocumentaryMediaProviderCapability.SceneComposition,1,dependencies,1920,1080,2000,30,0,0,[reference],Correlation);
        return new(plan,scene,visuals??[Asset("visual",DocumentaryMediaAssetType.VisualImage,DocumentaryMediaAssetFormat.Png,DocumentaryMediaAssetStatus.Generated)],Asset("narration-asset",DocumentaryMediaAssetType.NarrationAudio,DocumentaryMediaAssetFormat.Wav,narration),Asset("subtitle-asset",DocumentaryMediaAssetType.SubtitleDocument,DocumentaryMediaAssetFormat.Srt,subtitle),2000,2000,2000,DocumentarySceneTransition.CrossFade,1920,1080,30,Correlation);
    }
    public static DocumentaryMediaAssetResult Asset(string id,DocumentaryMediaAssetType type,DocumentaryMediaAssetFormat format,DocumentaryMediaAssetStatus status) => new(id,type,format,status,"test",null,0,status==DocumentaryMediaAssetStatus.Generated?2000:0,0,0,0,0,0,null,null,null,1,Correlation);
    public static DocumentaryPhysicalArtifactDescriptor Descriptor(string id,string path,string type,long? duration=null,string correlation=Correlation,string checksum="abc") =>
        new(id,"sha256:"+checksum,path,type,new FileInfo(path).Length,checksum,duration,null,null,null,null,null,"test",1,correlation);
}

internal sealed class RecordingProcessRunner : IProcessRunner
{
    public int InvocationCount { get; private set; }
    public string? CapturedFileName { get; private set; }
    public string? CapturedArguments { get; private set; }
    public CancellationToken CapturedCancellationToken { get; private set; }
    public TimeSpan? CapturedTimeout { get; private set; }
    public ProcessExecutionResult Output { get; set; } = Result();
    public Exception? Exception { get; set; }
    public bool WaitForCancellation { get; set; }
    public Func<string,string,Task>? OnExecute { get; set; }
    public async Task<ProcessExecutionResult> ExecuteAsync(string fileName,string arguments,CancellationToken token,TimeSpan? timeout=null)
    { InvocationCount++;CapturedFileName=fileName;CapturedArguments=arguments;CapturedCancellationToken=token;CapturedTimeout=timeout;if(WaitForCancellation)await Task.Delay(Timeout.Infinite,token);if(Exception is not null)throw Exception;if(OnExecute is not null)await OnExecute(fileName,arguments);return Output; }
    public static ProcessExecutionResult Result(int exit=0,bool timedOut=false,string exception="") { var now=DateTimeOffset.UtcNow;return new(exit,"","sanitized-test-stderr",now,now.AddMilliseconds(10),"ffmpeg","",exception,timedOut); }
}

internal sealed class FakeSceneCompositionProviderBinding : IDocumentarySceneCompositionProviderBinding
{
    public string ProviderId { get; set; } = DocumentarySceneCompositionProviderIds.ExistingFFmpegSceneComposer;
    public int InvocationCount { get; private set; }
    public DocumentarySceneCompositionProviderRequest? CapturedRequest { get; private set; }
    public CancellationToken CapturedCancellationToken { get; private set; }
    public DocumentarySceneCompositionProviderResponse? Output { get; set; }
    public DocumentaryProductionFailure? Failure { get; set; }
    public Exception? Exception { get; set; }
    public bool WaitForCancellation { get; set; }
    public async Task<DocumentarySceneCompositionProviderResponse> ComposeAsync(DocumentarySceneCompositionProviderRequest request,CancellationToken token)
    { InvocationCount++;CapturedRequest=request;CapturedCancellationToken=token;if(WaitForCancellation)await Task.Delay(Timeout.Infinite,token);if(Exception is not null)throw Exception;if(Output is not null)return Output;if(Failure is not null)return new(null,Failure);Directory.CreateDirectory(request.OutputDirectory);var path=Path.Combine(request.OutputDirectory,"provider-scene.mp4");await File.WriteAllBytesAsync(path,"certified-scene-bytes"u8.ToArray(),token);return new(path,null,0,17,"sanitized-stderr-hash"); }
}

internal class FakeSceneVideoInspector : IDocumentarySceneVideoInspector
{
    public int InvocationCount { get; private set; }
    public string? CapturedRequest { get; private set; }
    public CancellationToken CapturedCancellationToken { get; private set; }
    public DocumentarySceneVideoInspection Output { get; set; } = new(true,"mp4",2000,1920,1080,30,true,true);
    public Exception? Exception { get; set; }
    public bool WaitForCancellation { get; set; }
    public async Task<DocumentarySceneVideoInspection> InspectAsync(string path,CancellationToken token)
    { InvocationCount++;CapturedRequest=path;CapturedCancellationToken=token;if(WaitForCancellation)await Task.Delay(Timeout.Infinite,token);if(Exception is not null)throw Exception;return Output; }
}
internal sealed class CancellingSceneVideoInspector : FakeSceneVideoInspector { public CancellingSceneVideoInspector() { WaitForCancellation=true; } }

internal sealed class RecordingDiagnosticsWriter : IDocumentaryProductionDiagnosticsWriter
{
    public int InvocationCount { get; private set; } public object? CapturedRequest { get; private set; } public CancellationToken CapturedCancellationToken { get; private set; }
    public Exception? Exception { get; set; } public bool WaitForCancellation { get; set; }
    public async Task WriteAsync(string directory,string fileName,object value,CancellationToken token){InvocationCount++;CapturedRequest=value;CapturedCancellationToken=token;if(WaitForCancellation)await Task.Delay(Timeout.Infinite,token);if(Exception is not null)throw Exception;}
}
