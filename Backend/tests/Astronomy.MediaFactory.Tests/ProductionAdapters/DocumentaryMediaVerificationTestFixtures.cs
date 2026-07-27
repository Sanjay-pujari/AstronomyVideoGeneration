using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

internal sealed class A39RecordingProcessRunner : IProcessRunner
{
    public int InvocationCount { get; private set; }
    public string? CapturedFileName { get; private set; }
    public string? CapturedArguments { get; private set; }
    public CancellationToken CapturedCancellationToken { get; private set; }
    public TimeSpan? CapturedTimeout { get; private set; }
    public ProcessExecutionResult Output { get; set; } = Result("{\"format\":{\"format_name\":\"wav\",\"duration\":\"1.0\"},\"streams\":[{\"codec_type\":\"audio\",\"sample_rate\":\"48000\",\"channels\":1}]}");
    public Exception? Exception { get; set; }
    public bool WaitForCancellation { get; set; }
    public async Task<ProcessExecutionResult> ExecuteAsync(string fileName,string arguments,CancellationToken token,TimeSpan? timeout=null){InvocationCount++;CapturedFileName=fileName;CapturedArguments=arguments;CapturedCancellationToken=token;CapturedTimeout=timeout;if(WaitForCancellation){await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan,token);}if(Exception is not null)throw Exception;return Output;}
    public static ProcessExecutionResult Result(string stdout="",int exit=0,bool timedOut=false,string stderr="",string exception="")=>new(exit,stdout,stderr,DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch,"ffprobe","",exception,timedOut);
}

internal sealed class FakeFfprobeProbe : IDocumentaryFfprobeProbe
{
    public int InvocationCount {get;private set;} public string? Path {get;private set;} public TimeSpan Timeout {get;private set;} public CancellationToken Token {get;private set;}
    public DocumentaryMediaProbeResult? Result {get;set;}=new(true,1000,false,true,false,AudioSampleRate:48000,AudioChannelCount:1,ContainerFormat:"wav"); public bool WaitForCancellation {get;set;}
    public async Task<DocumentaryMediaProbeResult> ProbeAsync(string path,TimeSpan timeout,CancellationToken token){InvocationCount++;Path=path;Timeout=timeout;Token=token;if(WaitForCancellation)await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan,token);return Result!;}
}

internal sealed class FakeMediaVerificationProviderBinding : IDocumentaryMediaVerificationProviderBinding
{
    public string ProviderId {get;set;}=DocumentaryMediaVerificationProviderIds.ExistingFFprobeMediaVerifier; public int InvocationCount {get;private set;} public DocumentaryMediaVerificationProviderRequest? CapturedRequest {get;private set;} public CancellationToken CapturedCancellationToken {get;private set;}
    public DocumentaryMediaVerificationProviderResponse? Response {get;set;}=new(true,new(true,1000,false,true,false,AudioSampleRate:48000,AudioChannelCount:1,ContainerFormat:"wav"),null); public Exception? Exception {get;set;} public bool WaitForCancellation {get;set;}
    public async Task<DocumentaryMediaVerificationProviderResponse> ProbeAsync(DocumentaryMediaVerificationProviderRequest request,CancellationToken token){InvocationCount++;CapturedRequest=request;CapturedCancellationToken=token;if(WaitForCancellation)await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan,token);if(Exception is not null)throw Exception;return Response!;}
}

internal sealed class DocumentaryMediaVerificationAdapterFixture : IAsyncDisposable
{
    public string RootDirectory {get;}=Path.Combine(Path.GetTempPath(),"a39-full-"+Guid.NewGuid().ToString("N")); public DocumentaryProductionExecutionContext ExecutionContext {get;private set;}=null!; public DocumentaryProductionAttemptContext AttemptContext {get;private set;}=null!; public DocumentaryProductionWorkspace Workspace {get;private set;}=null!; public DocumentaryPhysicalArtifactRegistry Registry {get;}=new(); public DocumentaryPhysicalArtifactDescriptor Descriptor {get;private set;}=null!; public DocumentaryMediaVerificationRequest Request {get;private set;}=null!; public FakeMediaVerificationProviderBinding ProviderBinding {get;}=new(); public IDocumentaryProductionDiagnosticsWriter DiagnosticsWriter {get;set;}=new DocumentaryProductionDiagnosticsWriter(); public DocumentaryProductionMediaVerificationAdapter Adapter {get;private set;}=null!; public DocumentaryChecksumService ChecksumService {get;}=new(); public DocumentaryContentIdentityFactory ContentIdentityFactory {get;}=new();
    private DocumentaryMediaVerificationAdapterOptions options=new(){Enabled=true};
    public static Task<DocumentaryMediaVerificationAdapterFixture> NarrationAudio()=>Create(DocumentaryPhysicalArtifactKind.NarrationAudio,DocumentaryMediaAssetType.NarrationAudio,DocumentaryMediaAssetFormat.Wav,"narration-audio",1000);
    public static Task<DocumentaryMediaVerificationAdapterFixture> SceneVideo()=>Create(DocumentaryPhysicalArtifactKind.SceneVideo,DocumentaryMediaAssetType.SceneVideo,DocumentaryMediaAssetFormat.Mp4,"scene-video",2000);
    public static Task<DocumentaryMediaVerificationAdapterFixture> VariantVideo()=>Create(DocumentaryPhysicalArtifactKind.VariantVideo,DocumentaryMediaAssetType.VariantVideo,DocumentaryMediaAssetFormat.Mp4,"variant-video",6000);
    static async Task<DocumentaryMediaVerificationAdapterFixture> Create(DocumentaryPhysicalArtifactKind kind,DocumentaryMediaAssetType type,DocumentaryMediaAssetFormat format,string id,long duration){var f=new DocumentaryMediaVerificationAdapterFixture();Directory.CreateDirectory(f.RootDirectory);var attempts=Path.Combine(f.RootDirectory,"attempts");var diagnostics=Path.Combine(f.RootDirectory,"diagnostics");var final=Path.Combine(f.RootDirectory,"final");Directory.CreateDirectory(attempts);Directory.CreateDirectory(diagnostics);Directory.CreateDirectory(final);var path=Path.Combine(final,id+(format==DocumentaryMediaAssetFormat.Wav?".wav":".mp4"));await File.WriteAllBytesAsync(path,[1,3,3,7,9]);var sum=await f.ChecksumService.ComputeSha256Async(path,default);f.Descriptor=new(id,f.ContentIdentityFactory.Create(sum),path,format==DocumentaryMediaAssetFormat.Wav?"audio/wav":"video/mp4",5,sum,duration,type==DocumentaryMediaAssetType.NarrationAudio?null:1920,type==DocumentaryMediaAssetType.NarrationAudio?null:1080,type==DocumentaryMediaAssetType.NarrationAudio?null:30,48000,type==DocumentaryMediaAssetType.NarrationAudio?1:2,"producer",1,"verification-correlation");await f.Registry.RegisterAsync(f.Descriptor,kind,default);f.Workspace=new(f.RootDirectory,f.RootDirectory,Path.Combine(f.RootDirectory,"variants"),attempts,diagnostics);f.ExecutionContext=new("execution","verification-correlation",DocumentaryProductionExecutionMode.Legacy,f.RootDirectory,DateTimeOffset.UnixEpoch,new Dictionary<string,string>());f.AttemptContext=new("execution","verification-correlation",DocumentaryProductionOperationKind.MediaVerification,id,null,null,1,f.ProviderBinding.ProviderId,DateTimeOffset.UnixEpoch,TimeSpan.FromSeconds(5));f.Request=new(id,type,format,kind,"verification-correlation",1,ExpectedContentType:f.Descriptor.ContentType,ExpectedDurationMilliseconds:duration,ExpectedWidth:f.Descriptor.Width,ExpectedHeight:f.Descriptor.Height,ExpectedFrameRate:f.Descriptor.FrameRate,ExpectedAudioSampleRate:48000,ExpectedAudioChannelCount:f.Descriptor.AudioChannelCount,RequireVideo:type!=DocumentaryMediaAssetType.NarrationAudio,RequireAudio:type==DocumentaryMediaAssetType.NarrationAudio);f.Rebuild();return f;}
    public void Rebuild(){Adapter=new(Options.Create(options),Registry,new DocumentaryPhysicalArtifactDescriptorValidator(ContentIdentityFactory),ChecksumService,new DocumentaryMediaVerificationPolicyResolver(),[ProviderBinding],new DocumentaryMediaVerificationEvaluator(Options.Create(options)),DiagnosticsWriter,new DocumentarySafeFileNameGenerator(),new DocumentaryProductionFailureNormalizer());}
    public ValueTask DisposeAsync(){if(Directory.Exists(RootDirectory))Directory.Delete(RootDirectory,true);return ValueTask.CompletedTask;}
}
