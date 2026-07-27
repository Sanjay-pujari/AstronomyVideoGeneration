using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.ProductionAdapters;

public enum DocumentarySceneSubtitleMode { None, BurnIn, Muxed }
public static class DocumentarySceneCompositionProviderIds { public const string ExistingFFmpegSceneComposer = "ExistingFFmpegSceneComposer"; }

public sealed class DocumentarySceneCompositionAdapterOptions
{
    public const string SectionName = "DocumentaryProductionAdapters:SceneComposition";
    public bool Enabled { get; set; }
    public int DurationToleranceMilliseconds { get; set; } = 250;
    public decimal FrameRateTolerance { get; set; } = 0.01m;
    public bool RetainProviderNativeVideo { get; set; }
}

public sealed record DocumentaryResolvedSceneDependencies(
    IReadOnlyList<DocumentaryPhysicalArtifactDescriptor> VisualArtifacts,
    DocumentaryPhysicalArtifactDescriptor? NarrationArtifact,
    DocumentaryPhysicalArtifactDescriptor? SubtitleArtifact,
    long SceneDurationMilliseconds,
    int SceneSequence,
    string VariantId,
    string CorrelationId);

public sealed record DocumentarySceneDependencyResolution(DocumentaryResolvedSceneDependencies? Dependencies, DocumentaryProductionFailure? Failure)
{ public bool Succeeded => Dependencies is not null && Failure is null; }

public interface IDocumentarySceneDependencyResolver
{
    Task<DocumentarySceneDependencyResolution> ResolveAsync(DocumentarySceneCompositionRequest request, DocumentaryProductionWorkspace workspace, CancellationToken cancellationToken);
}

public sealed class DocumentarySceneDependencyResolver(IDocumentaryPhysicalArtifactRegistry registry) : IDocumentarySceneDependencyResolver
{
    private static readonly HashSet<string> Images = new(["image/png", "image/jpeg"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Audio = new(["audio/wav", "audio/mpeg", "audio/aac", "audio/mp4"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Subtitles = new(["application/x-subrip", "text/vtt"], StringComparer.OrdinalIgnoreCase);

    public async Task<DocumentarySceneDependencyResolution> ResolveAsync(DocumentarySceneCompositionRequest request, DocumentaryProductionWorkspace workspace, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); token.ThrowIfCancellationRequested();
        if (!string.Equals(request.CorrelationId, request.AssetPlan.CorrelationId, StringComparison.Ordinal) || !string.Equals(request.CorrelationId, request.MediaScene.CorrelationId, StringComparison.Ordinal))
            return Fail(DocumentaryProductionFailureCode.SourceArtifactInvalid, "Scene correlation does not match its certified plan.");

        var visuals = new List<(int Sequence, DocumentaryPhysicalArtifactDescriptor Descriptor)>();
        foreach (var source in request.VisualAssets.OrderBy(x => DependencySequence(request.AssetPlan, x.AssetId)).ThenBy(x => x.AssetId, StringComparer.Ordinal))
        {
            var descriptor = await registry.GetAsync(source.AssetId, token);
            var error = Validate(descriptor, Images, request.CorrelationId, workspace);
            if (descriptor is null) return Fail(DocumentaryProductionFailureCode.SourceArtifactMissing, "A finalized visual artifact is missing.");
            if (error is not null) return Fail(DocumentaryProductionFailureCode.SourceArtifactInvalid, "A finalized visual artifact is invalid.");
            visuals.Add((DependencySequence(request.AssetPlan, source.AssetId), descriptor));
        }
        if (visuals.Count == 0) return Fail(DocumentaryProductionFailureCode.SourceArtifactMissing, "A finalized visual artifact is missing.");

        var narration = await ResolveOptionalAsync(request.NarrationAsset, registry, token);
        if (request.NarrationAsset.Status is DocumentaryMediaAssetStatus.Generated or DocumentaryMediaAssetStatus.Verified)
        {
            if (narration is null) return Fail(DocumentaryProductionFailureCode.SourceArtifactMissing, "The finalized narration artifact is missing.");
            if (Validate(narration, Audio, request.CorrelationId, workspace) is not null || narration.DurationMilliseconds is null or <= 0)
                return Fail(DocumentaryProductionFailureCode.SourceArtifactInvalid, "The finalized narration artifact is invalid.");
        }
        var subtitle = await ResolveOptionalAsync(request.SubtitleAsset, registry, token);
        if (request.SubtitleAsset.Status is DocumentaryMediaAssetStatus.Generated or DocumentaryMediaAssetStatus.Verified)
        {
            if (subtitle is null) return Fail(DocumentaryProductionFailureCode.SubtitleMissing, "The finalized subtitle artifact is missing.");
            if (Validate(subtitle, Subtitles, request.CorrelationId, workspace) is not null)
                return Fail(DocumentaryProductionFailureCode.SourceArtifactInvalid, "The finalized subtitle artifact is invalid.");
        }
        var duration = request.EffectiveSceneDurationMilliseconds > 0 ? request.EffectiveSceneDurationMilliseconds : narration?.DurationMilliseconds ?? 0;
        if (duration <= 0) return Fail(DocumentaryProductionFailureCode.DurationMeasurementFailed, "No certified scene duration is available.");
        var ordered = visuals.OrderBy(x => x.Sequence).ThenBy(x => x.Descriptor.AssetId, StringComparer.Ordinal).Select(x => x.Descriptor).ToArray();
        return new(new(new ReadOnlyCollection<DocumentaryPhysicalArtifactDescriptor>(ordered), narration, subtitle, duration, request.MediaScene.Sequence, request.MediaScene.VariantType.ToString(), request.CorrelationId), null);
    }

    private static int DependencySequence(DocumentaryMediaAssetPlan plan, string assetId) => plan.Dependencies.Where(x => x.SourceAssetId == assetId).Select(x => x.Sequence).DefaultIfEmpty(int.MaxValue).Min();
    private static Task<DocumentaryPhysicalArtifactDescriptor?> ResolveOptionalAsync(DocumentaryMediaAssetResult result, IDocumentaryPhysicalArtifactRegistry registry, CancellationToken token) =>
        result.Status is DocumentaryMediaAssetStatus.Generated or DocumentaryMediaAssetStatus.Verified ? registry.GetAsync(result.AssetId, token) : Task.FromResult<DocumentaryPhysicalArtifactDescriptor?>(null);
    private static string? Validate(DocumentaryPhysicalArtifactDescriptor? d, HashSet<string> types, string correlation, DocumentaryProductionWorkspace workspace)
    {
        if (d is null) return "missing";
        if (!types.Contains(d.ContentType) || d.Length <= 0 || !File.Exists(d.PhysicalPath) || !string.Equals(d.CorrelationId, correlation, StringComparison.Ordinal)) return "invalid";
        if (DocumentaryPathComparison.IsBelow(workspace.AttemptsDirectory, d.PhysicalPath)) return "temporary";
        return null;
    }
    private static DocumentarySceneDependencyResolution Fail(DocumentaryProductionFailureCode code, string message) => new(null, new(code, message));
}

public sealed record DocumentarySceneCompositionProviderRequest(string AssetId,string SourceInstructionId,string SceneId,int SceneSequence,string VariantId,string CorrelationId,int Attempt,string OutputDirectory,string OutputPath,DocumentaryMediaAssetFormat OutputFormat,int Width,int Height,decimal FrameRate,long DurationMilliseconds,IReadOnlyList<string> OrderedVisualPaths,string? NarrationAudioPath,string? SubtitlePath,DocumentarySceneSubtitleMode SubtitleMode,DocumentaryCameraMotion MotionPolicy,DocumentarySceneTransition TransitionPolicy,string VideoProfile,string AudioProfile);
public sealed record DocumentarySceneCompositionProviderResponse(string? OutputPath,DocumentaryProductionFailure? Failure = null,int? ExitCode = null,long? ElapsedMilliseconds = null,string? SanitizedStandardErrorHash = null,string? FfmpegVersion = null)
{ public bool Succeeded => Failure is null && !string.IsNullOrWhiteSpace(OutputPath); }
public interface IDocumentarySceneCompositionProviderBinding { string ProviderId { get; } Task<DocumentarySceneCompositionProviderResponse> ComposeAsync(DocumentarySceneCompositionProviderRequest request,CancellationToken cancellationToken); }

public sealed class ExistingFFmpegDocumentarySceneProviderBinding(IProcessRunner runner,FfmpegArgumentBuilder builder,IOptions<RenderingOptions> rendering) : IDocumentarySceneCompositionProviderBinding
{
    public string ProviderId => DocumentarySceneCompositionProviderIds.ExistingFFmpegSceneComposer;
    public async Task<DocumentarySceneCompositionProviderResponse> ComposeAsync(DocumentarySceneCompositionProviderRequest r,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (r.OutputFormat != DocumentaryMediaAssetFormat.Mp4 || r.Width <= 0 || r.Height <= 0 || r.FrameRate <= 0 || r.DurationMilliseconds <= 0 || r.OrderedVisualPaths.Count == 0)
            return Failed(DocumentaryProductionFailureCode.ProviderRejectedRequest, "The scene profile is unsupported.");
        if (r.SubtitleMode == DocumentarySceneSubtitleMode.Muxed) return Failed(DocumentaryProductionFailureCode.ProviderRejectedRequest, "The existing scene renderer supports subtitle burn-in, not subtitle muxing.");
        if (r.SubtitleMode == DocumentarySceneSubtitleMode.BurnIn && string.IsNullOrWhiteSpace(r.SubtitlePath)) return Failed(DocumentaryProductionFailureCode.SubtitleMissing, "Subtitle burn-in requires a finalized subtitle.");
        var owned = Path.Combine(Path.GetFullPath(r.OutputDirectory), "provider-scene.mp4");
        if (!DocumentaryPathComparison.IsBelow(r.OutputDirectory, owned)) return Failed(DocumentaryProductionFailureCode.FileSystemFailure, "Provider output escaped its attempt directory.");
        Directory.CreateDirectory(r.OutputDirectory);
        var args = builder.BuildScene(rendering.Value,r.OrderedVisualPaths,r.NarrationAudioPath,r.SubtitleMode == DocumentarySceneSubtitleMode.BurnIn ? r.SubtitlePath : null,owned,r.Width,r.Height,(int)r.FrameRate,r.DurationMilliseconds);
        ProcessExecutionResult result;
        try { result = await runner.ExecuteAsync(rendering.Value.FfmpegPath,args,token,TimeSpan.FromSeconds(Math.Max(1, rendering.Value.FfmpegSegmentTimeoutSeconds))); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (System.ComponentModel.Win32Exception) { return Failed(DocumentaryProductionFailureCode.DependencyMissing, "The FFmpeg executable is unavailable."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Failed(DocumentaryProductionFailureCode.ProcessStartFailed, "FFmpeg could not be started."); }
        var stderrHash = Hash(result.StandardError);
        if (result.TimedOut) return new(null,new(DocumentaryProductionFailureCode.ProcessTimedOut,"FFmpeg scene composition timed out.",true,ProviderId),result.ExitCode,(long)result.Duration.TotalMilliseconds,stderrHash);
        if (!string.IsNullOrEmpty(result.ExceptionText)) return new(null,new(DocumentaryProductionFailureCode.ProcessStartFailed,"FFmpeg scene composition could not start."),result.ExitCode,(long)result.Duration.TotalMilliseconds,stderrHash);
        if (result.ExitCode != 0) return new(null,new(DocumentaryProductionFailureCode.ProcessExitedWithError,"FFmpeg scene composition failed."),result.ExitCode,(long)result.Duration.TotalMilliseconds,stderrHash);
        if (!File.Exists(owned)) return new(null,new(DocumentaryProductionFailureCode.OutputArtifactMissing,"FFmpeg produced no scene video."),result.ExitCode,(long)result.Duration.TotalMilliseconds,stderrHash);
        if (new FileInfo(owned).Length == 0) return new(null,new(DocumentaryProductionFailureCode.OutputArtifactEmpty,"FFmpeg produced an empty scene video."),result.ExitCode,(long)result.Duration.TotalMilliseconds,stderrHash);
        return new(owned,null,result.ExitCode,(long)result.Duration.TotalMilliseconds,stderrHash);
    }
    private static DocumentarySceneCompositionProviderResponse Failed(DocumentaryProductionFailureCode c,string m)=>new(null,new(c,m));
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value??string.Empty))).ToLowerInvariant();
}

public sealed record DocumentarySceneVideoInspection(bool Succeeded,string? Format,long DurationMilliseconds,int Width,int Height,decimal FrameRate,bool HasVideo,bool HasAudio,DocumentaryProductionFailure? Failure=null);
public interface IDocumentarySceneVideoInspector { Task<DocumentarySceneVideoInspection> InspectAsync(string path,CancellationToken cancellationToken); }
public sealed class DocumentarySceneVideoInspector(IDocumentaryMediaProbe probe) : IDocumentarySceneVideoInspector
{
    public async Task<DocumentarySceneVideoInspection> InspectAsync(string path,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path)) return Fail(DocumentaryProductionFailureCode.OutputArtifactMissing,"The scene video is missing.");
        if (new FileInfo(path).Length == 0) return Fail(DocumentaryProductionFailureCode.OutputArtifactEmpty,"The scene video is empty.");
        var p=await probe.ProbeAsync(path,token);
        if(!p.Succeeded)return Fail(p.Failure?.Code??DocumentaryProductionFailureCode.OutputFormatInvalid,"The scene video could not be inspected.");
        if(p.HasVideoStream!=true)return Fail(DocumentaryProductionFailureCode.VideoStreamMissing,"The scene video has no video stream.");
        if(p.DurationMilliseconds is null or <=0||p.Width is null or <=0||p.Height is null or <=0||p.FrameRate is null or <=0)return Fail(DocumentaryProductionFailureCode.OutputFormatInvalid,"The scene video metadata is invalid.");
        return new(true,p.ContainerFormat,p.DurationMilliseconds.Value,p.Width.Value,p.Height.Value,p.FrameRate.Value,true,p.HasAudioStream==true);
    }
    private static DocumentarySceneVideoInspection Fail(DocumentaryProductionFailureCode c,string m)=>new(false,null,0,0,0,0,false,false,new(c,m));
}

public sealed record DocumentaryProductionSceneCompositionAdapterResult(bool Succeeded,DocumentaryPhysicalArtifactDescriptor? Artifact,DocumentaryProductionFailure? Failure,string RequestedProviderId,string? ActualProviderId,string SceneId,string VariantId,DocumentaryMediaAssetFormat OutputFormat,long? MeasuredDurationMilliseconds,int? MeasuredWidth,int? MeasuredHeight,decimal? MeasuredFrameRate,bool HasAudio,DocumentarySceneSubtitleMode SubtitleMode,string? ProviderDiagnosticsReference)
{
    public static DocumentaryProductionSceneCompositionAdapterResult Success(DocumentaryPhysicalArtifactDescriptor d,string provider,string scene,string variant,long duration,int width,int height,decimal fps,bool audio,DocumentarySceneSubtitleMode subtitle,string diagnostic)=>new(true,d,null,provider,provider,scene,variant,DocumentaryMediaAssetFormat.Mp4,duration,width,height,fps,audio,subtitle,diagnostic);
    public static DocumentaryProductionSceneCompositionAdapterResult Failed(DocumentaryProductionFailure f,string scene,string variant,DocumentarySceneSubtitleMode subtitle)=>new(false,null,f,DocumentarySceneCompositionProviderIds.ExistingFFmpegSceneComposer,null,scene,variant,DocumentaryMediaAssetFormat.Mp4,null,null,null,null,false,subtitle,null);
}
public interface IDocumentaryProductionSceneCompositionAdapter { Task<DocumentaryProductionSceneCompositionAdapterResult> ComposeAsync(DocumentarySceneCompositionRequest request,DocumentaryProductionExecutionContext executionContext,DocumentaryProductionAttemptContext attemptContext,DocumentaryProductionWorkspace workspace,CancellationToken cancellationToken); }

public sealed class ExistingDocumentarySceneCompositionAdapter(IOptions<DocumentarySceneCompositionAdapterOptions> options,IDocumentarySceneDependencyResolver dependencies,IEnumerable<IDocumentarySceneCompositionProviderBinding> bindings,IDocumentarySceneVideoInspector inspector,IDocumentaryProductionWorkspaceManager workspaces,IDocumentaryPhysicalArtifactInspector artifacts,IDocumentaryPhysicalArtifactDescriptorValidator validator,IDocumentaryPhysicalArtifactRegistry registry,IDocumentaryProductionDiagnosticsWriter diagnostics) : IDocumentaryProductionSceneCompositionAdapter
{
    public async Task<DocumentaryProductionSceneCompositionAdapterResult> ComposeAsync(DocumentarySceneCompositionRequest r,DocumentaryProductionExecutionContext execution,DocumentaryProductionAttemptContext attempt,DocumentaryProductionWorkspace workspace,CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var subtitleMode=r.SubtitleAsset.Status is DocumentaryMediaAssetStatus.Generated or DocumentaryMediaAssetStatus.Verified?DocumentarySceneSubtitleMode.BurnIn:DocumentarySceneSubtitleMode.None;
        string scene=r.MediaScene.SceneId,variant=r.MediaScene.VariantType.ToString();
        if(!options.Value.Enabled)return Fail(DocumentaryProductionFailureCode.AdapterUnavailable,"Scene composition is disabled.");
        var selected=bindings.Where(x=>x.ProviderId==DocumentarySceneCompositionProviderIds.ExistingFFmpegSceneComposer).ToArray();
        if(selected.Length!=1)return Fail(DocumentaryProductionFailureCode.AdapterUnavailable,"Exactly one FFmpeg scene binding is required.");
        var resolved=await dependencies.ResolveAsync(r,workspace,token);if(!resolved.Succeeded)return DocumentaryProductionSceneCompositionAdapterResult.Failed(resolved.Failure!,scene,variant,subtitleMode);
        var d=resolved.Dependencies!;
        if(r.AssetPlan.AssetFormat!=DocumentaryMediaAssetFormat.Mp4||r.Width<=0||r.Height<=0||r.FrameRate<=0)return Fail(DocumentaryProductionFailureCode.ProviderRejectedRequest,"The requested scene profile is unsupported.");
        if(d.NarrationArtifact?.DurationMilliseconds is long nd&&Math.Abs(nd-d.SceneDurationMilliseconds)>options.Value.DurationToleranceMilliseconds)return Fail(DocumentaryProductionFailureCode.DurationMeasurementFailed,"Narration and scene durations disagree.");
        var attemptDir=workspaces.GetAttemptDirectory(workspace,DocumentaryProductionOperationKind.SceneComposition,r.AssetPlan.AssetId,attempt.AttemptNumber);Directory.CreateDirectory(attemptDir);
        var providerRequest=new DocumentarySceneCompositionProviderRequest(r.AssetPlan.AssetId,r.AssetPlan.SourceInstructionId,scene,d.SceneSequence,variant,r.CorrelationId,attempt.AttemptNumber,attemptDir,Path.Combine(attemptDir,"provider-scene.mp4"),r.AssetPlan.AssetFormat,r.Width,r.Height,r.FrameRate,d.SceneDurationMilliseconds,new ReadOnlyCollection<string>(d.VisualArtifacts.Select(x=>x.PhysicalPath).ToArray()),d.NarrationArtifact?.PhysicalPath,d.SubtitleArtifact?.PhysicalPath,subtitleMode,r.MediaScene.VisualPrompts[0].CameraMotion,r.Transition,"IntermediateSegment","AAC");
        var response=await selected[0].ComposeAsync(providerRequest,token);if(!response.Succeeded)return DocumentaryProductionSceneCompositionAdapterResult.Failed(response.Failure??new(DocumentaryProductionFailureCode.ProviderInvalidResponse,"The FFmpeg binding returned an invalid response."),scene,variant,subtitleMode);
        if(!DocumentaryPathComparison.IsBelow(attemptDir,response.OutputPath!))return Fail(DocumentaryProductionFailureCode.ProviderInvalidResponse,"The provider output is outside its attempt directory.");
        var measured=await inspector.InspectAsync(response.OutputPath!,token);if(!measured.Succeeded)return DocumentaryProductionSceneCompositionAdapterResult.Failed(measured.Failure!,scene,variant,subtitleMode);
        if(measured.Width!=r.Width||measured.Height!=r.Height)return Fail(DocumentaryProductionFailureCode.DimensionMismatch,"The scene dimensions do not match the requested profile.");
        if(Math.Abs(measured.FrameRate-r.FrameRate)>options.Value.FrameRateTolerance)return Fail(DocumentaryProductionFailureCode.OutputFormatInvalid,"The scene frame rate does not match the requested profile.");
        if(Math.Abs(measured.DurationMilliseconds-d.SceneDurationMilliseconds)>options.Value.DurationToleranceMilliseconds)return Fail(DocumentaryProductionFailureCode.DurationMeasurementFailed,"The measured scene duration is outside tolerance.");
        if(d.NarrationArtifact is not null&&!measured.HasAudio)return Fail(DocumentaryProductionFailureCode.AudioStreamMissing,"The narrated scene has no audio stream.");
        var final=workspaces.GetFinalArtifactPath(workspace,variant,d.SceneSequence,DocumentaryPhysicalArtifactKind.SceneVideo,r.AssetPlan.AssetId,"mp4");await workspaces.FinalizeArtifactAsync(workspace,response.OutputPath!,final,true,token);
        var descriptor=await artifacts.InspectAsync(new(r.AssetPlan.AssetId,final,"video/mp4",selected[0].ProviderId,attempt.AttemptNumber,r.CorrelationId),token);descriptor=descriptor with{DurationMilliseconds=measured.DurationMilliseconds,Width=measured.Width,Height=measured.Height,FrameRate=measured.FrameRate};
        if(validator.Validate(descriptor).Count>0)return Fail(DocumentaryProductionFailureCode.OutputFormatInvalid,"The finalized scene descriptor is invalid.");
        await registry.RegisterAsync(descriptor,DocumentaryPhysicalArtifactKind.SceneVideo,token);
        var diag=$"scene-{Safe(r.AssetPlan.AssetId)}-{attempt.AttemptNumber:D2}.json";await diagnostics.WriteAsync(workspace.DiagnosticsDirectory,diag,new{execution.ExecutionId,r.CorrelationId,r.AssetPlan.AssetId,r.AssetPlan.SourceInstructionId,SceneId=scene,SceneSequence=d.SceneSequence,VariantId=variant,ProviderId=selected[0].ProviderId,Attempt=attempt.AttemptNumber,RequestedFormat="Mp4",ActualFormat=measured.Format,RequestedDurationMilliseconds=d.SceneDurationMilliseconds,ActualDurationMilliseconds=measured.DurationMilliseconds,RequestedWidth=r.Width,ActualWidth=measured.Width,RequestedHeight=r.Height,ActualHeight=measured.Height,RequestedFrameRate=r.FrameRate,ActualFrameRate=measured.FrameRate,NarrationAssetId=d.NarrationArtifact?.AssetId,SubtitleAssetId=d.SubtitleArtifact?.AssetId,VisualAssetIds=d.VisualArtifacts.Select(x=>x.AssetId).ToArray(),SubtitleMode=subtitleMode.ToString(),MotionPolicy=r.MediaScene.VisualPrompts[0].CameraMotion.ToString(),TransitionPolicy=r.Transition.ToString(),response.ExitCode,response.ElapsedMilliseconds,response.SanitizedStandardErrorHash,descriptor.Length,descriptor.Checksum,descriptor.ContentIdentity,Outcome="Succeeded"},token);
        if(!options.Value.RetainProviderNativeVideo)await workspaces.CleanupSuccessfulAttemptAsync(workspace,attemptDir,token);
        return DocumentaryProductionSceneCompositionAdapterResult.Success(descriptor,selected[0].ProviderId,scene,variant,measured.DurationMilliseconds,measured.Width,measured.Height,measured.FrameRate,measured.HasAudio,subtitleMode,diag);
        DocumentaryProductionSceneCompositionAdapterResult Fail(DocumentaryProductionFailureCode c,string m)=>DocumentaryProductionSceneCompositionAdapterResult.Failed(new(c,m),scene,variant,subtitleMode);
    }
    private static string Safe(string value)=>new string(value.Select(c=>char.IsLetterOrDigit(c)||c is '-' or '_'?c:'_').ToArray());
}

public interface IDocumentarySceneCompositionResultMapper { DocumentarySceneCompositionResult Map(DocumentarySceneCompositionRequest request,DocumentaryProductionSceneCompositionAdapterResult result); }
public sealed class DocumentarySceneCompositionResultMapper : IDocumentarySceneCompositionResultMapper
{
    public DocumentarySceneCompositionResult Map(DocumentarySceneCompositionRequest r,DocumentaryProductionSceneCompositionAdapterResult result)
    { var d=result.Artifact;var status=result.Succeeded?DocumentaryMediaAssetStatus.Generated:DocumentaryMediaAssetStatus.Failed;var asset=new DocumentaryMediaAssetResult(r.AssetPlan.AssetId,DocumentaryMediaAssetType.SceneVideo,r.AssetPlan.AssetFormat,status,result.ActualProviderId,d?.ContentIdentity,d?.Length??0,result.MeasuredDurationMilliseconds??0,result.MeasuredWidth??0,result.MeasuredHeight??0,(int)(result.MeasuredFrameRate??0),0,0,d?.Checksum,result.Failure?.Code.ToString(),result.Failure?.Message,d?.AttemptCount??r.Attempt,r.CorrelationId);return new(status,asset,result.MeasuredDurationMilliseconds??0,result.Failure?.Code.ToString(),result.Failure?.Message); }
}
