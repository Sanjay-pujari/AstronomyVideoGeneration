using System.Text.Json;
using System.Text;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

internal sealed class DocumentarySceneCompositionAdapterFixture : IAsyncDisposable
{
    private readonly DocumentaryChecksumService checksums = new();
    public string RootDirectory { get; } = Path.Combine(Environment.CurrentDirectory, "TestResults", "a3-7-" + Guid.NewGuid().ToString("N"));
    public DocumentaryProductionExecutionContext ExecutionContext { get; }
    public DocumentaryProductionAttemptContext AttemptContext { get; }
    public DocumentaryProductionWorkspace Workspace { get; private set; } = null!;
    public DocumentaryPhysicalArtifactRegistry Registry { get; } = new();
    public FakeSceneCompositionProviderBinding ProviderBinding { get; } = new();
    public FakeSceneVideoInspector SceneInspector { get; } = new();
    public DocumentaryProductionDiagnosticsWriter DiagnosticsWriter { get; } = new();
    public ExistingDocumentarySceneCompositionAdapter Adapter { get; private set; } = null!;
    public DocumentarySceneCompositionRequest Request { get; set; } = null!;
    public DocumentaryProductionWorkspaceManager WorkspaceManager { get; }

    private DocumentarySceneCompositionAdapterFixture()
    {
        ExecutionContext = new("execution-1", DocumentarySceneCompositionTestFixtures.Correlation, DocumentaryProductionExecutionMode.Certified, RootDirectory, DateTimeOffset.UnixEpoch, new Dictionary<string,string>());
        AttemptContext = new("execution-1", DocumentarySceneCompositionTestFixtures.Correlation, DocumentaryProductionOperationKind.SceneComposition, "scene-video", "LongEnglish", "scene-1", 1, DocumentarySceneCompositionProviderIds.ExistingFFmpegSceneComposer, DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30));
        WorkspaceManager = new(new DocumentarySafeFileNameGenerator(), checksums);
    }

    public static async Task<DocumentarySceneCompositionAdapterFixture> CreateAsync(bool narrated=true, bool subtitle=false, bool enabled=true)
    {
        var f = new DocumentarySceneCompositionAdapterFixture();
        f.Workspace = await f.WorkspaceManager.CreateAsync(f.ExecutionContext, default);
        f.Request = DocumentarySceneCompositionTestFixtures.Request(narration:narrated?DocumentaryMediaAssetStatus.Generated:DocumentaryMediaAssetStatus.Planned, subtitle:subtitle?DocumentaryMediaAssetStatus.Generated:DocumentaryMediaAssetStatus.Planned);
        await f.RegisterSourceAsync("visual", "visual.png", "image/png");
        if(narrated) await f.RegisterSourceAsync("narration-asset", "narration.wav", "audio/wav", 2000);
        if(subtitle) await f.RegisterSourceAsync("subtitle-asset", "subtitle.srt", "application/x-subrip");
        f.Rebuild(enabled);
        return f;
    }

    public void Rebuild(bool enabled=true, IEnumerable<IDocumentarySceneCompositionProviderBinding>? bindings=null, IDocumentaryPhysicalArtifactDescriptorValidator? validator=null)
    {
        var identities=new DocumentaryContentIdentityFactory();
        Adapter = new(Options.Create(new DocumentarySceneCompositionAdapterOptions{Enabled=enabled,DurationToleranceMilliseconds=250,FrameRateTolerance=.01m,RetainProviderNativeVideo=false}),new DocumentarySceneDependencyResolver(Registry),bindings??[ProviderBinding],SceneInspector,WorkspaceManager,new DocumentaryPhysicalArtifactInspector(checksums,identities,new NullDocumentaryMediaProbe()),validator??new DocumentaryPhysicalArtifactDescriptorValidator(identities),Registry,DiagnosticsWriter,new DocumentaryProductionFailureNormalizer());
    }

    private async Task RegisterSourceAsync(string id,string name,string contentType,long? duration=null)
    {
        var directory=Path.Combine(Workspace.ExecutionRoot,"certified-sources");Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,name);await File.WriteAllBytesAsync(path,Encoding.UTF8.GetBytes("certified-"+name));
        var checksum=await checksums.ComputeSha256Async(path,default);
        var descriptor=new DocumentaryPhysicalArtifactDescriptor(id,"sha256:"+checksum,path,contentType,new FileInfo(path).Length,checksum,duration,null,null,null,null,null,"fixture",1,DocumentarySceneCompositionTestFixtures.Correlation);
        var kind=contentType.StartsWith("image/")?DocumentaryPhysicalArtifactKind.VisualImage:contentType.StartsWith("audio/")?DocumentaryPhysicalArtifactKind.NarrationAudio:DocumentaryPhysicalArtifactKind.SubtitleDocument;
        await Registry.RegisterAsync(descriptor,kind,default);
    }

    public Task<DocumentaryProductionSceneCompositionAdapterResult> ComposeAsync(CancellationToken token=default)=>Adapter.ComposeAsync(Request,ExecutionContext,AttemptContext,Workspace,token);
    public async ValueTask DisposeAsync(){await Task.Yield();if(Directory.Exists(RootDirectory))Directory.Delete(RootDirectory,true);}
}

public sealed class DocumentarySceneCompositionFullAdapterTests
{
    [Fact] public async Task Scene_composition_does_not_generate_upstream_assets(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();var result=await f.ComposeAsync();result.Succeeded.Should().BeTrue();f.ProviderBinding.InvocationCount.Should().Be(1);(await Scenes(f)).Should().ContainSingle();}
    [Fact] public async Task One_scene_request_produces_one_scene_video(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();var result=await f.ComposeAsync();result.Succeeded.Should().BeTrue();f.ProviderBinding.InvocationCount.Should().Be(1);f.ProviderBinding.CapturedRequest.Should().NotBeNull();(await Scenes(f)).Should().ContainSingle();Directory.GetFiles(f.Workspace.DiagnosticsDirectory,"*.json").Should().ContainSingle();Directory.GetFiles(f.Workspace.ExecutionRoot,"*.mp4",SearchOption.AllDirectories).Should().ContainSingle();}
    [Fact] public async Task Narrated_scene_is_finalized_hashed_registered_and_mapped(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();var result=await f.ComposeAsync();result.Succeeded.Should().BeTrue();result.HasAudio.Should().BeTrue();result.MeasuredDurationMilliseconds.Should().Be(2000);result.MeasuredWidth.Should().Be(1920);result.MeasuredHeight.Should().Be(1080);result.MeasuredFrameRate.Should().Be(30);result.Artifact!.Checksum.Should().MatchRegex("^[0-9a-f]{64}$");result.Artifact.ContentIdentity.Should().Be("sha256:"+result.Artifact.Checksum);result.Artifact.ProviderId.Should().Be(DocumentarySceneCompositionProviderIds.ExistingFFmpegSceneComposer);result.Artifact.PhysicalPath.Should().NotStartWith(f.Workspace.AttemptsDirectory);f.ProviderBinding.CapturedRequest!.NarrationAudioPath.Should().EndWith("narration.wav");new DocumentarySceneCompositionResultMapper().Map(f.Request,result).Status.Should().Be(DocumentaryMediaAssetStatus.Generated);}
    [Fact] public async Task Silent_scene_succeeds_without_artificial_audio(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync(false);f.SceneInspector.Output=f.SceneInspector.Output with{HasAudio=false};var result=await f.ComposeAsync();result.Succeeded.Should().BeTrue();result.HasAudio.Should().BeFalse();f.ProviderBinding.CapturedRequest!.NarrationAudioPath.Should().BeNull();f.ProviderBinding.CapturedRequest.SubtitlePath.Should().BeNull();}
    [Fact] public async Task Subtitle_burn_in_uses_finalized_subtitle_without_regeneration(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync(true,true);(await f.ComposeAsync()).Succeeded.Should().BeTrue();f.ProviderBinding.CapturedRequest!.SubtitleMode.Should().Be(DocumentarySceneSubtitleMode.BurnIn);f.ProviderBinding.CapturedRequest.SubtitlePath.Should().EndWith("subtitle.srt");f.ProviderBinding.InvocationCount.Should().Be(1);}
    [Fact] public async Task Disabled_adapter_returns_AdapterUnavailable_without_provider_invocation(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync(enabled:false);var r=await f.ComposeAsync();r.Failure!.Code.Should().Be(DocumentaryProductionFailureCode.AdapterUnavailable);f.ProviderBinding.InvocationCount.Should().Be(0);}
    [Fact] public async Task Missing_scene_provider_binding_returns_AdapterUnavailable(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();f.Rebuild(bindings:[]);(await f.ComposeAsync()).Failure!.Code.Should().Be(DocumentaryProductionFailureCode.AdapterUnavailable);}
    [Fact] public async Task Duplicate_scene_provider_bindings_return_AdapterUnavailable(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();f.Rebuild(bindings:[f.ProviderBinding,new FakeSceneCompositionProviderBinding()]);(await f.ComposeAsync()).Failure!.Code.Should().Be(DocumentaryProductionFailureCode.AdapterUnavailable);f.ProviderBinding.InvocationCount.Should().Be(0);}
    [Theory][InlineData(typeof(TimeoutException),DocumentaryProductionFailureCode.ProviderTimeout)][InlineData(typeof(IOException),DocumentaryProductionFailureCode.FileSystemFailure)][InlineData(typeof(InvalidOperationException),DocumentaryProductionFailureCode.ProviderRejectedRequest)] public async Task Provider_exceptions_are_normalized_through_adapter(Type type,DocumentaryProductionFailureCode code){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();f.ProviderBinding.Exception=(Exception)Activator.CreateInstance(type,"private provider detail")!;var r=await f.ComposeAsync();r.Failure!.Code.Should().Be(code);r.Failure.Message.Should().NotContain("private provider detail");f.SceneInspector.InvocationCount.Should().Be(0);}
    [Fact] public async Task Provider_cancellation_propagates_through_scene_adapter(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();f.ProviderBinding.WaitForCancellation=true;using var cts=new CancellationTokenSource();var task=f.ComposeAsync(cts.Token);while(f.ProviderBinding.InvocationCount==0)await Task.Yield();cts.Cancel();await FluentActions.Awaiting(()=>task).Should().ThrowAsync<OperationCanceledException>();(await Scenes(f)).Should().BeEmpty();}
    [Fact] public async Task Inspector_cancellation_propagates_through_scene_adapter(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();f.SceneInspector.WaitForCancellation=true;using var cts=new CancellationTokenSource();var task=f.ComposeAsync(cts.Token);while(f.SceneInspector.InvocationCount==0)await Task.Yield();cts.Cancel();await FluentActions.Awaiting(()=>task).Should().ThrowAsync<OperationCanceledException>();(await Scenes(f)).Should().BeEmpty();}
    [Fact] public async Task Provider_output_outside_attempt_directory_is_rejected(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();var path=Path.Combine(f.Workspace.ExecutionRoot,"external.mp4");await File.WriteAllBytesAsync(path,"external"u8.ToArray());f.ProviderBinding.Output=new(path);var r=await f.ComposeAsync();r.Failure!.Code.Should().Be(DocumentaryProductionFailureCode.ProviderInvalidResponse);File.Exists(path).Should().BeTrue();f.SceneInspector.InvocationCount.Should().Be(0);}
    [Fact] public async Task Narrated_scene_without_audio_stream_is_rejected(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();f.SceneInspector.Output=f.SceneInspector.Output with{HasAudio=false};(await f.ComposeAsync()).Failure!.Code.Should().Be(DocumentaryProductionFailureCode.AudioStreamMissing);}
    [Fact] public async Task Measured_frame_rate_outside_tolerance_is_rejected(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();f.SceneInspector.Output=f.SceneInspector.Output with{FrameRate=29};(await f.ComposeAsync()).Failure!.Code.Should().Be(DocumentaryProductionFailureCode.OutputFormatInvalid);}
    [Fact] public async Task Measured_duration_outside_tolerance_is_rejected(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();f.SceneInspector.Output=f.SceneInspector.Output with{DurationMilliseconds=2501};(await f.ComposeAsync()).Failure!.Code.Should().Be(DocumentaryProductionFailureCode.DurationMeasurementFailed);}
    [Fact] public async Task Success_diagnostic_is_written_and_sanitized(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();(await f.ComposeAsync()).Succeeded.Should().BeTrue();var json=await File.ReadAllTextAsync(Directory.GetFiles(f.Workspace.DiagnosticsDirectory).Single());using var document=JsonDocument.Parse(json);document.RootElement.GetProperty("outcome").GetString().Should().Be("Succeeded");json.Should().Contain("sanitizedStandardErrorHash").And.NotContain("authorization").And.NotContain("subscription key").And.NotContain("ffmpeg command");}
    [Fact] public async Task Same_scene_content_replay_is_idempotent(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();var first=await f.ComposeAsync();var second=await f.ComposeAsync();second.Succeeded.Should().BeTrue();second.Artifact!.Checksum.Should().Be(first.Artifact!.Checksum);(await Scenes(f)).Should().ContainSingle();}
    [Fact] public async Task Full_scene_adapter_does_not_mutate_request_or_contexts(){await using var f=await DocumentarySceneCompositionAdapterFixture.CreateAsync();var before=JsonSerializer.Serialize((f.Request,f.ExecutionContext,f.AttemptContext));(await f.ComposeAsync()).Succeeded.Should().BeTrue();JsonSerializer.Serialize((f.Request,f.ExecutionContext,f.AttemptContext)).Should().Be(before);}
    private static async Task<IReadOnlyCollection<DocumentaryPhysicalArtifactDescriptor>> Scenes(DocumentarySceneCompositionAdapterFixture f)=>(await f.Registry.GetAllAsync(DocumentarySceneCompositionTestFixtures.Correlation,default)).Where(x=>x.AssetId=="scene-video").ToArray();
}
