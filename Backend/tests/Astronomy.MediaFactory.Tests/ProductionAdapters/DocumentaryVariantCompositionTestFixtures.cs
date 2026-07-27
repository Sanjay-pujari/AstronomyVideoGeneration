using System.Text;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

internal sealed class FakeVariantCompositionProviderBinding : IDocumentaryVariantCompositionProviderBinding
{
    public string ProviderId { get; set; } = DocumentaryVariantCompositionProviderIds.ExistingFFmpegVariantComposer;
    public int InvocationCount { get; private set; }
    public DocumentaryVariantCompositionProviderRequest? CapturedRequest { get; private set; }
    public CancellationToken CapturedCancellationToken { get; private set; }
    public DocumentaryVariantCompositionProviderResponse? Output { get; set; }
    public DocumentaryProductionFailure? Failure { get; set; }
    public Exception? Exception { get; set; }
    public bool WaitForCancellation { get; set; }
    public async Task<DocumentaryVariantCompositionProviderResponse> ComposeAsync(DocumentaryVariantCompositionProviderRequest request,CancellationToken token)
    { InvocationCount++;CapturedRequest=request;CapturedCancellationToken=token;if(WaitForCancellation)await Task.Delay(Timeout.Infinite,token);if(Exception is not null)throw Exception;if(Output is not null)return Output;if(Failure is not null)return new(null,Failure);Directory.CreateDirectory(request.OutputDirectory);var path=Path.Combine(request.OutputDirectory,"provider-variant.mp4");await File.WriteAllBytesAsync(path,"certified-variant-bytes"u8.ToArray(),token);return new(path,null,0,23,"sanitized-stderr-hash","ConcatDemuxerFinalReencode"); }
}
internal sealed class FakeVariantVideoInspector : IDocumentaryVariantVideoInspector
{
    public int InvocationCount { get; private set; } public string? CapturedPath { get; private set; } public CancellationToken CapturedCancellationToken { get; private set; }
    public DocumentaryVariantVideoInspection Output { get; set; }=new(true,"mp4",6000,1920,1080,30,true,true); public Exception? Exception { get; set; } public bool WaitForCancellation { get; set; }
    public async Task<DocumentaryVariantVideoInspection> InspectAsync(string path,CancellationToken token){InvocationCount++;CapturedPath=path;CapturedCancellationToken=token;if(WaitForCancellation)await Task.Delay(Timeout.Infinite,token);if(Exception is not null)throw Exception;return Output;}
}
internal sealed class DocumentaryVariantCompositionAdapterFixture : IAsyncDisposable
{
    public const string Correlation="variant-correlation"; readonly DocumentaryChecksumService checksums=new();
    public string RootDirectory { get; }=Path.Combine(Environment.CurrentDirectory,"TestResults","a3-8-"+Guid.NewGuid().ToString("N"));
    public DocumentaryProductionExecutionContext ExecutionContext { get; } public DocumentaryProductionAttemptContext AttemptContext { get; }
    public DocumentaryProductionWorkspace Workspace { get; private set; }=null!; public DocumentaryPhysicalArtifactRegistry Registry { get; }=new();
    public FakeVariantCompositionProviderBinding ProviderBinding { get; }=new(); public FakeVariantVideoInspector VariantInspector { get; }=new();
    public DocumentaryProductionDiagnosticsWriter DiagnosticsWriter { get; }=new(); public ExistingDocumentaryVariantCompositionAdapter Adapter { get; private set; }=null!;
    public DocumentaryVariantCompositionRequest Request { get; set; }=null!; public DocumentaryProductionWorkspaceManager WorkspaceManager { get; }
    DocumentaryVariantCompositionAdapterFixture(){ExecutionContext=new("execution-1",Correlation,DocumentaryProductionExecutionMode.Certified,RootDirectory,DateTimeOffset.UnixEpoch,new Dictionary<string,string>());AttemptContext=new("execution-1",Correlation,DocumentaryProductionOperationKind.VariantComposition,"variant-video","LongEnglish",null,1,DocumentaryVariantCompositionProviderIds.ExistingFFmpegVariantComposer,DateTimeOffset.UnixEpoch,TimeSpan.FromSeconds(30));WorkspaceManager=new(new DocumentarySafeFileNameGenerator(),checksums);}
    public static async Task<DocumentaryVariantCompositionAdapterFixture> CreateAsync(bool portrait=false,bool audio=true,bool enabled=true)
    {var f=new DocumentaryVariantCompositionAdapterFixture();f.Workspace=await f.WorkspaceManager.CreateAsync(f.ExecutionContext,default);f.Request=f.CreateRequest(portrait,audio);foreach(var x in new[]{("scene-1",2000L),("scene-2",3000L),("scene-3",1000L)})await f.RegisterSceneAsync(x.Item1,x.Item2,portrait);f.Rebuild(enabled);if(portrait)f.VariantInspector.Output=f.VariantInspector.Output with{Width=1080,Height=1920};if(!audio)f.VariantInspector.Output=f.VariantInspector.Output with{HasAudio=false};return f;}
    public void Rebuild(bool enabled=true,IEnumerable<IDocumentaryVariantCompositionProviderBinding>? bindings=null,IDocumentaryPhysicalArtifactDescriptorValidator? validator=null){var identities=new DocumentaryContentIdentityFactory();Adapter=new(Options.Create(new DocumentaryVariantCompositionAdapterOptions{Enabled=enabled,DurationToleranceMilliseconds=500,FrameRateTolerance=.01m,RetainProviderNativeVideo=false}),new DocumentaryVariantDependencyResolver(Registry,identities),new DocumentaryVariantCompatibilityValidator(),bindings??[ProviderBinding],VariantInspector,WorkspaceManager,new DocumentaryPhysicalArtifactInspector(checksums,identities,new NullDocumentaryMediaProbe()),validator??new DocumentaryPhysicalArtifactDescriptorValidator(identities),Registry,DiagnosticsWriter,new DocumentaryProductionFailureNormalizer());}
    DocumentaryVariantCompositionRequest CreateRequest(bool portrait,bool audio){var type=portrait?DocumentaryMediaVariantType.ShortEnglish:DocumentaryMediaVariantType.LongEnglish;var width=portrait?1080:1920;var height=portrait?1920:1080;var reference=new DocumentaryMediaKnowledgeReference("ref","payload",default,"source","artifact","v1","/",0,Correlation);var durations=new[]{2000L,3000L,1000L};var scenes=durations.Select((duration,i)=>{var n=new DocumentaryNarrationBlock("n"+(i+1),DocumentaryMediaLanguage.English,"Narration",0,duration,[reference],Correlation);var cue=new DocumentarySubtitleCue("cue"+(i+1),DocumentaryMediaLanguage.English,"Narration","Narration",null,default,0,duration,0,n.NarrationId,[reference],Correlation);var visual=new DocumentaryVisualPrompt("visual"+(i+1),default,"Astronomy scene",null,"","16:9",default,["subject"],[reference],0,Correlation);return new DocumentaryMediaScene("scene-"+(i+1),type,default,"Scene",i+1,[n],[cue],[visual],new("timing-"+(i+1),0,duration,duration,duration,0,0,Correlation),DocumentarySceneTransition.Cut,[reference],Correlation);}).ToArray();var variant=new DocumentaryMediaVariant(portrait?"short-en":"long-en",type,default,DocumentaryMediaLanguage.English,"Title","Description","Hook",scenes,3,6000,portrait?"9:16":"16:9",Correlation);var assets=scenes.Select((s,i)=>new DocumentaryMediaAssetResult("scene-"+(i+1),DocumentaryMediaAssetType.SceneVideo,DocumentaryMediaAssetFormat.Mp4,DocumentaryMediaAssetStatus.Generated,"fixture",null,1,durations[i],width,height,30,48000,audio?2:0,null,null,null,1,Correlation)).ToArray();var deps=assets.Select((a,i)=>new DocumentaryMediaAssetDependency("dep-"+(i+1),a.AssetId,"variant-video",i+1,Correlation)).ToArray();var plan=new DocumentaryMediaAssetPlan("variant-video",DocumentaryMediaAssetType.VariantVideo,DocumentaryMediaAssetFormat.Mp4,type,null,"instruction",DocumentaryMediaProviderCapability.VideoComposition,1,deps,width,height,6000,30,48000,audio?2:0,[reference],Correlation);return new(plan,variant,assets,width,height,30,48000,audio?2:0,DocumentaryMediaAssetFormat.Mp4,Correlation);}
    async Task RegisterSceneAsync(string id,long duration,bool portrait){var dir=Path.Combine(Workspace.ExecutionRoot,"certified-sources");Directory.CreateDirectory(dir);var path=Path.Combine(dir,id+".mp4");await File.WriteAllBytesAsync(path,Encoding.UTF8.GetBytes("certified-"+id));var hash=await checksums.ComputeSha256Async(path,default);await Registry.RegisterAsync(new(id,new DocumentaryContentIdentityFactory().Create(hash),path,"video/mp4",new FileInfo(path).Length,hash,duration,portrait?1080:1920,portrait?1920:1080,30,48000,2,"fixture",1,Correlation),DocumentaryPhysicalArtifactKind.SceneVideo,default);}
    public Task<DocumentaryProductionVariantCompositionAdapterResult> ComposeAsync(CancellationToken token=default)=>Adapter.ComposeAsync(Request,ExecutionContext,AttemptContext,Workspace,token);
    public async Task<IReadOnlyCollection<DocumentaryPhysicalArtifactDescriptor>> Variants()=> (await Registry.GetAllAsync(Correlation,default)).Where(x=>x.AssetId=="variant-video").ToArray();
    public async ValueTask DisposeAsync(){await Task.Yield();if(Directory.Exists(RootDirectory))Directory.Delete(RootDirectory,true);}
}
