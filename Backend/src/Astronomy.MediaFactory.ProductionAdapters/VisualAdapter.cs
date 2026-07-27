using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace Astronomy.MediaFactory.ProductionAdapters;

public static class DocumentaryVisualProviderIds
{
    public const string Stellarium = "Stellarium";
    public const string AzureOpenAICinematicImage = "AzureOpenAICinematicImage";
    public const string AstronomyInfographic = "AstronomyInfographic";
    public const string FileVisualAsset = "FileVisualAsset";
    public const string CelestialAsset = "CelestialAsset";
    public static readonly IReadOnlySet<string> All = new HashSet<string>([Stellarium, AzureOpenAICinematicImage, AstronomyInfographic, FileVisualAsset, CelestialAsset], StringComparer.Ordinal);
}

public sealed class DocumentaryVisualAdapterOptions
{
    public const string SectionName = "DocumentaryProductionAdapters:Visual";
    public bool Enabled { get; set; }
    public bool AllowFallback { get; set; }
    public bool AllowRepresentativeTelescopeFallback { get; set; }
    public bool AllowGeneratedScientificDiagramFallback { get; set; }
    public bool RetainProviderNativeImages { get; set; }
}

public sealed record DocumentaryVisualProviderRoute(
    DocumentaryMediaAssetType RequestedVisualType,
    string PrimaryProvider,
    IReadOnlyList<string> OrderedFallbackProviders,
    bool FallbackAllowed,
    string RequiredSemanticClass,
    string Reason)
{
    public bool Equals(DocumentaryVisualProviderRoute? other) =>
        other is not null &&
        RequestedVisualType == other.RequestedVisualType &&
        StringComparer.Ordinal.Equals(PrimaryProvider, other.PrimaryProvider) &&
        OrderedFallbackProviders.SequenceEqual(other.OrderedFallbackProviders, StringComparer.Ordinal) &&
        FallbackAllowed == other.FallbackAllowed &&
        StringComparer.Ordinal.Equals(RequiredSemanticClass, other.RequiredSemanticClass) &&
        StringComparer.Ordinal.Equals(Reason, other.Reason);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RequestedVisualType);
        hash.Add(PrimaryProvider, StringComparer.Ordinal);
        foreach (var fallbackProvider in OrderedFallbackProviders)
            hash.Add(fallbackProvider, StringComparer.Ordinal);
        hash.Add(FallbackAllowed);
        hash.Add(RequiredSemanticClass, StringComparer.Ordinal);
        hash.Add(Reason, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

public interface IDocumentaryVisualProviderRouter
{
    DocumentaryVisualProviderRoute Route(DocumentaryVisualGenerationRequest request);
}

public sealed class DocumentaryVisualProviderRouter(IOptions<DocumentaryVisualAdapterOptions> options) : IDocumentaryVisualProviderRouter
{
    public DocumentaryVisualProviderRoute Route(DocumentaryVisualGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var o = options.Value;
        var (primary, fallbacks, semantic, reason) = request.AssetPlan.AssetType switch
        {
            DocumentaryMediaAssetType.SkySimulationImage => (DocumentaryVisualProviderIds.Stellarium, Array.Empty<string>(), "actual-sky-simulation", "A sky simulation requires the existing Stellarium capability."),
            DocumentaryMediaAssetType.TelescopeViewImage => (DocumentaryVisualProviderIds.Stellarium, o.AllowRepresentativeTelescopeFallback ? new[] { DocumentaryVisualProviderIds.CelestialAsset } : [], "telescope-view", "Representative imagery is separately labelled and opt-in."),
            DocumentaryMediaAssetType.StarChartImage => (DocumentaryVisualProviderIds.AstronomyInfographic, new[] { DocumentaryVisualProviderIds.Stellarium }, "scientific-star-chart", "The existing infographic renderer owns star charts."),
            DocumentaryMediaAssetType.ScientificDiagramImage => (DocumentaryVisualProviderIds.AstronomyInfographic, o.AllowGeneratedScientificDiagramFallback ? new[] { DocumentaryVisualProviderIds.AzureOpenAICinematicImage } : [], "scientific-diagram", "Generated illustration is opt-in and non-equivalent."),
            DocumentaryMediaAssetType.HistoricalIllustrationImage => (DocumentaryVisualProviderIds.AzureOpenAICinematicImage, new[] { DocumentaryVisualProviderIds.FileVisualAsset }, "historical-illustration", "Approved local art may replace generated historical art."),
            DocumentaryMediaAssetType.VisualImage => (DocumentaryVisualProviderIds.AzureOpenAICinematicImage, new[] { DocumentaryVisualProviderIds.FileVisualAsset, DocumentaryVisualProviderIds.CelestialAsset }, "generic-celestial-visual", "Generic visual routing prefers the existing cinematic generator."),
            _ => throw new NotSupportedException($"Asset type '{request.AssetPlan.AssetType}' is not a supported documentary visual type.")
        };
        var copy = Array.AsReadOnly(fallbacks.ToArray());
        return new(request.AssetPlan.AssetType, primary, copy, o.AllowFallback && copy.Count > 0, semantic, reason);
    }
}

public sealed record DocumentaryVisualFallbackDecision(bool Allowed, bool IsSemanticallyEquivalent, string? Reason);
public interface IDocumentaryVisualFallbackPolicy
{
    DocumentaryVisualFallbackDecision Evaluate(DocumentaryVisualGenerationRequest request, DocumentaryVisualProviderRoute route, string fallbackProvider, DocumentaryProductionFailure primaryFailure, DocumentaryProductionExecutionMode mode);
}

public sealed class DocumentaryVisualFallbackPolicy : IDocumentaryVisualFallbackPolicy
{
    private static readonly HashSet<DocumentaryProductionFailureCode> Eligible = [DocumentaryProductionFailureCode.ProviderUnavailable, DocumentaryProductionFailureCode.ProviderRateLimited, DocumentaryProductionFailureCode.ProviderTimeout, DocumentaryProductionFailureCode.ProcessTimedOut, DocumentaryProductionFailureCode.ProcessExitedWithError, DocumentaryProductionFailureCode.ProviderInvalidResponse, DocumentaryProductionFailureCode.OutputArtifactMissing, DocumentaryProductionFailureCode.OutputArtifactEmpty, DocumentaryProductionFailureCode.OutputFormatInvalid, DocumentaryProductionFailureCode.DimensionMismatch];
    public DocumentaryVisualFallbackDecision Evaluate(DocumentaryVisualGenerationRequest request, DocumentaryVisualProviderRoute route, string fallbackProvider, DocumentaryProductionFailure failure, DocumentaryProductionExecutionMode mode)
    {
        var configured = route.FallbackAllowed && route.OrderedFallbackProviders.Contains(fallbackProvider, StringComparer.Ordinal);
        var equivalent = IsEquivalent(request.AssetPlan.AssetType, fallbackProvider);
        var allowed = configured && Eligible.Contains(failure.Code) && equivalent;
        var reason = allowed ? $"{failure.Code}: primary provider did not produce a valid artifact." : null;
        return new(allowed, equivalent, reason);
    }
    private static bool IsEquivalent(DocumentaryMediaAssetType type, string provider) => (type, provider) switch
    {
        (DocumentaryMediaAssetType.TelescopeViewImage, DocumentaryVisualProviderIds.CelestialAsset) => true,
        (DocumentaryMediaAssetType.StarChartImage, DocumentaryVisualProviderIds.Stellarium) => true,
        (DocumentaryMediaAssetType.HistoricalIllustrationImage, DocumentaryVisualProviderIds.FileVisualAsset) => true,
        (DocumentaryMediaAssetType.VisualImage, DocumentaryVisualProviderIds.FileVisualAsset or DocumentaryVisualProviderIds.CelestialAsset) => true,
        _ => false
    };
}

public sealed record DocumentaryImageInspectionResult(bool Succeeded, DocumentaryMediaAssetFormat? Format, int Width, int Height, bool HasAlpha, DocumentaryProductionFailure? Failure);
public interface IDocumentaryImageInspector { Task<DocumentaryImageInspectionResult> InspectAsync(string path, CancellationToken cancellationToken); }
public sealed class DocumentaryImageInspector : IDocumentaryImageInspector
{
    public async Task<DocumentaryImageInspectionResult> InspectAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path)) return Failed(DocumentaryProductionFailureCode.OutputArtifactMissing, "Provider image is missing.");
        if (new FileInfo(path).Length == 0) return Failed(DocumentaryProductionFailureCode.OutputArtifactEmpty, "Provider image is empty.");
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            using var image = await Image.LoadAsync(stream, token);
            var format = image.Metadata.DecodedImageFormat switch { PngFormat => DocumentaryMediaAssetFormat.Png, JpegFormat => DocumentaryMediaAssetFormat.Jpeg, _ => (DocumentaryMediaAssetFormat?)null };
            if (format is null || image.Width <= 0 || image.Height <= 0) return Failed(DocumentaryProductionFailureCode.OutputFormatInvalid, "Only decodable PNG and JPEG images are supported.");
            return new(true, format, image.Width, image.Height, image.PixelType.AlphaRepresentation != SixLabors.ImageSharp.PixelFormats.PixelAlphaRepresentation.None, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is InvalidImageContentException or UnknownImageFormatException or NotSupportedException) { return Failed(DocumentaryProductionFailureCode.OutputFormatInvalid, "Provider output is not a supported image."); }
    }
    private static DocumentaryImageInspectionResult Failed(DocumentaryProductionFailureCode code, string message) => new(false, null, 0, 0, false, new(code, message));
}

public sealed record DocumentaryVisualProviderRequest(string AssetId, string SourceInstructionId, string? SceneId, string VariantId, string CorrelationId, DocumentaryMediaAssetType VisualType, string Prompt, int Width, int Height, DocumentaryMediaAssetFormat Format, int Attempt, string OutputDirectory);
public sealed record DocumentaryVisualProviderResponse(string? OutputPath, DocumentaryProductionFailure? Failure = null, string? DiagnosticsReference = null);
public interface IDocumentaryVisualProviderBinding
{
    string ProviderId { get; }
    Task<DocumentaryVisualProviderResponse> GenerateAsync(DocumentaryVisualProviderRequest request, CancellationToken cancellationToken);
}

public sealed record DocumentaryProductionVisualAdapterResult
{
    private DocumentaryProductionVisualAdapterResult(bool succeeded, DocumentaryPhysicalArtifactDescriptor? artifact, DocumentaryProductionFailure? failure, string requested, string actual, bool fallback, string? reason, bool? equivalent, string? diagnostics)
    { Succeeded=succeeded; Artifact=artifact; Failure=failure; RequestedProviderId=requested; ActualProviderId=actual; FallbackUsed=fallback; FallbackReason=reason; IsSemanticallyEquivalentFallback=equivalent; ProviderDiagnosticsReference=diagnostics; }
    public bool Succeeded { get; }
    public DocumentaryPhysicalArtifactDescriptor? Artifact { get; }
    public DocumentaryProductionFailure? Failure { get; }
    public string RequestedProviderId { get; }
    public string ActualProviderId { get; }
    public bool FallbackUsed { get; }
    public string? FallbackReason { get; }
    public bool? IsSemanticallyEquivalentFallback { get; }
    public string? ProviderDiagnosticsReference { get; }
    public static DocumentaryProductionVisualAdapterResult Success(DocumentaryPhysicalArtifactDescriptor artifact,string requested,string actual,bool fallback=false,string? reason=null,bool? equivalent=null,string? diagnostics=null)
    { ArgumentNullException.ThrowIfNull(artifact); if(string.IsNullOrWhiteSpace(requested)||string.IsNullOrWhiteSpace(actual)||fallback && (string.IsNullOrWhiteSpace(reason)||equivalent is null)||!fallback&&(reason is not null||equivalent is not null))throw new ArgumentException("Invalid visual adapter success."); return new(true,artifact,null,requested,actual,fallback,reason,equivalent,diagnostics); }
    public static DocumentaryProductionVisualAdapterResult Failed(DocumentaryProductionFailure failure,string requested,string actual,string? diagnostics=null)
    { ArgumentNullException.ThrowIfNull(failure); if(string.IsNullOrWhiteSpace(requested)||string.IsNullOrWhiteSpace(actual))throw new ArgumentException("Provider IDs are required."); return new(false,null,failure,requested,actual,false,null,null,diagnostics); }
}

public interface IDocumentaryProductionVisualAdapter
{
    Task<DocumentaryProductionVisualAdapterResult> GenerateAsync(DocumentaryVisualGenerationRequest request, DocumentaryProductionExecutionContext executionContext, DocumentaryProductionAttemptContext attemptContext, DocumentaryProductionWorkspace workspace, CancellationToken cancellationToken);
}

public sealed class ExistingDocumentaryVisualProductionAdapter : IDocumentaryProductionVisualAdapter
{
    private readonly DocumentaryVisualAdapterOptions options; private readonly IDocumentaryVisualProviderRouter router; private readonly IDocumentaryVisualFallbackPolicy fallbackPolicy; private readonly IReadOnlyDictionary<string,IDocumentaryVisualProviderBinding> providers; private readonly IDocumentaryProductionWorkspaceManager workspaces; private readonly IDocumentaryImageInspector images; private readonly IDocumentaryPhysicalArtifactInspector artifacts; private readonly IDocumentaryPhysicalArtifactDescriptorValidator validator; private readonly IDocumentaryPhysicalArtifactRegistry registry; private readonly IDocumentaryProductionDiagnosticsWriter diagnostics; private readonly IDocumentaryProductionFailureNormalizer failures;
    public ExistingDocumentaryVisualProductionAdapter(IOptions<DocumentaryVisualAdapterOptions> options,IDocumentaryVisualProviderRouter router,IDocumentaryVisualFallbackPolicy fallbackPolicy,IEnumerable<IDocumentaryVisualProviderBinding> providers,IDocumentaryProductionWorkspaceManager workspaces,IDocumentaryImageInspector images,IDocumentaryPhysicalArtifactInspector artifacts,IDocumentaryPhysicalArtifactDescriptorValidator validator,IDocumentaryPhysicalArtifactRegistry registry,IDocumentaryProductionDiagnosticsWriter diagnostics,IDocumentaryProductionFailureNormalizer failures)
    { this.options=options.Value;this.router=router;this.fallbackPolicy=fallbackPolicy;var providerList=providers.ToArray();var duplicates=providerList.GroupBy(x=>x.ProviderId,StringComparer.Ordinal).Where(x=>x.Count()>1).Select(x=>x.Key).OrderBy(x=>x,StringComparer.Ordinal).ToArray();if(duplicates.Length>0)throw new InvalidOperationException($"Duplicate documentary visual provider bindings: {string.Join(", ",duplicates)}");this.providers=providerList.ToDictionary(x=>x.ProviderId,StringComparer.Ordinal);this.workspaces=workspaces;this.images=images;this.artifacts=artifacts;this.validator=validator;this.registry=registry;this.diagnostics=diagnostics;this.failures=failures; }
    public async Task<DocumentaryProductionVisualAdapterResult> GenerateAsync(DocumentaryVisualGenerationRequest request,DocumentaryProductionExecutionContext execution,DocumentaryProductionAttemptContext attempt,DocumentaryProductionWorkspace workspace,CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ValidateIdentities(request,execution,attempt); var route=router.Route(request);
        if(!options.Enabled)return DocumentaryProductionVisualAdapterResult.Failed(new(DocumentaryProductionFailureCode.AdapterUnavailable,"The visual adapter is disabled."),route.PrimaryProvider,route.PrimaryProvider);
        var primary=await InvokeAsync(route.PrimaryProvider,false,null,null); if(primary.Succeeded)return primary;
        foreach(var candidate in route.OrderedFallbackProviders){var decision=fallbackPolicy.Evaluate(request,route,candidate,primary.Failure!,execution.ExecutionMode);if(!decision.Allowed)continue;var result=await InvokeAsync(candidate,true,decision.Reason,decision.IsSemanticallyEquivalent);if(result.Succeeded)return result;}
        return primary;

        async Task<DocumentaryProductionVisualAdapterResult> InvokeAsync(string providerId,bool fallback,string? fallbackReason,bool? equivalent)
        {
            if(!providers.TryGetValue(providerId,out var provider))return DocumentaryProductionVisualAdapterResult.Failed(new(DocumentaryProductionFailureCode.AdapterUnavailable,"The selected visual provider is not registered.",ProviderId:providerId),route.PrimaryProvider,providerId);
            var attemptDir=workspaces.GetAttemptDirectory(workspace,DocumentaryProductionOperationKind.VisualGeneration,request.AssetPlan.AssetId,attempt.AttemptNumber);Directory.CreateDirectory(attemptDir);var translated=Translate(request,attemptDir);
            var watch=Stopwatch.StartNew(); DocumentaryVisualProviderResponse response;
            try { response=await provider.GenerateAsync(translated,token); }
            catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
            catch(Exception ex){var failure=failures.Normalize(ex,DocumentaryProductionOperationKind.VisualGeneration,false) with { ProviderId=providerId };return DocumentaryProductionVisualAdapterResult.Failed(failure,route.PrimaryProvider,providerId);}
            if(response.Failure is not null)return DocumentaryProductionVisualAdapterResult.Failed(response.Failure with{ProviderId=providerId},route.PrimaryProvider,providerId,response.DiagnosticsReference);
            if(string.IsNullOrWhiteSpace(response.OutputPath))return DocumentaryProductionVisualAdapterResult.Failed(new(DocumentaryProductionFailureCode.ProviderInvalidResponse,"The provider returned no output path.",ProviderId:providerId),route.PrimaryProvider,providerId,response.DiagnosticsReference);
            var native=Path.GetFullPath(response.OutputPath);if(!IsBelow(attemptDir,native))return DocumentaryProductionVisualAdapterResult.Failed(new(DocumentaryProductionFailureCode.SourceArtifactInvalid,"Provider output escaped the attempt workspace.",ProviderId:providerId),route.PrimaryProvider,providerId);
            var inspection=await images.InspectAsync(native,token);if(!inspection.Succeeded)return DocumentaryProductionVisualAdapterResult.Failed(inspection.Failure! with{ProviderId=providerId},route.PrimaryProvider,providerId,response.DiagnosticsReference);
            if(inspection.Format!=request.AssetFormat)return DocumentaryProductionVisualAdapterResult.Failed(new(DocumentaryProductionFailureCode.OutputFormatInvalid,"Provider output format does not match the requested format.",ProviderId:providerId),route.PrimaryProvider,providerId);
            if(inspection.Width!=request.Width||inspection.Height!=request.Height)return DocumentaryProductionVisualAdapterResult.Failed(new(DocumentaryProductionFailureCode.DimensionMismatch,"Measured image dimensions do not match the request.",ProviderId:providerId),route.PrimaryProvider,providerId);
            var extension=request.AssetFormat==DocumentaryMediaAssetFormat.Png?"png":"jpg";var final=workspaces.GetFinalArtifactPath(workspace,request.AssetPlan.VariantType.ToString(),request.AssetPlan.Sequence+1,DocumentaryPhysicalArtifactKind.VisualImage,request.AssetPlan.AssetId,extension);await workspaces.FinalizeArtifactAsync(workspace,native,final,true,token);
            var descriptor=await artifacts.InspectAsync(new(request.AssetPlan.AssetId,final,request.AssetFormat==DocumentaryMediaAssetFormat.Png?"image/png":"image/jpeg",providerId,attempt.AttemptNumber,request.CorrelationId),token);descriptor=descriptor with{Width=inspection.Width,Height=inspection.Height};var errors=validator.Validate(descriptor);if(errors.Count>0)return DocumentaryProductionVisualAdapterResult.Failed(new(DocumentaryProductionFailureCode.SourceArtifactInvalid,"Final visual descriptor was invalid: "+string.Join(',',errors),ProviderId:providerId),route.PrimaryProvider,providerId);
            await registry.RegisterAsync(descriptor,DocumentaryPhysicalArtifactKind.VisualImage,token);watch.Stop();var diagnosticName=$"visual-{Sanitize(request.AssetPlan.AssetId)}-{attempt.AttemptNumber:D2}.json";await diagnostics.WriteAsync(workspace.DiagnosticsDirectory,diagnosticName,new{execution.ExecutionId,request.CorrelationId,request.AssetPlan.AssetId,request.AssetPlan.SourceInstructionId,request.AssetPlan.SceneId,VisualType=request.AssetPlan.AssetType.ToString(),RequestedProvider=route.PrimaryProvider,ActualProvider=providerId,FallbackUsed=fallback,FallbackReason=fallbackReason,SemanticEquivalence=equivalent,Attempt=attempt.AttemptNumber,RequestedWidth=request.Width,RequestedHeight=request.Height,ActualWidth=inspection.Width,ActualHeight=inspection.Height,RequestedFormat=request.AssetFormat.ToString(),ActualFormat=inspection.Format.ToString(),FinalPath=final,descriptor.Checksum,descriptor.ContentIdentity,DurationMilliseconds=watch.ElapsedMilliseconds,Outcome="Succeeded",PromptHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.VisualPrompt.PromptEnglish))).ToLowerInvariant(),PromptLength=request.VisualPrompt.PromptEnglish.Length},token);
            return DocumentaryProductionVisualAdapterResult.Success(descriptor,route.PrimaryProvider,providerId,fallback,fallbackReason,equivalent,response.DiagnosticsReference);
        }
    }
    private static DocumentaryVisualProviderRequest Translate(DocumentaryVisualGenerationRequest r,string output)=>new(r.AssetPlan.AssetId,r.AssetPlan.SourceInstructionId,r.AssetPlan.SceneId,r.AssetPlan.VariantType.ToString(),r.CorrelationId,r.AssetPlan.AssetType,r.VisualPrompt.PromptEnglish,r.Width,r.Height,r.AssetFormat,r.Attempt,output);
    private static void ValidateIdentities(DocumentaryVisualGenerationRequest r,DocumentaryProductionExecutionContext e,DocumentaryProductionAttemptContext a){ArgumentNullException.ThrowIfNull(r);if(r.Width<=0||r.Height<=0||r.Attempt<1)throw new ArgumentException("Positive dimensions and attempt are required.");if(r.AssetFormat is not (DocumentaryMediaAssetFormat.Png or DocumentaryMediaAssetFormat.Jpeg))throw new NotSupportedException("Only PNG and JPEG visual output is supported.");if(r.CorrelationId!=r.AssetPlan.CorrelationId||r.CorrelationId!=e.CorrelationId||r.CorrelationId!=a.CorrelationId||r.AssetPlan.AssetId!=a.AssetId)throw new ArgumentException("Visual execution identity mismatch.");}
    private static bool IsBelow(string root,string path)=>DocumentaryPathComparison.IsBelow(root,path);
    private static string Sanitize(string value)=>new(value.Select(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_'?c:'_').ToArray());
}

public interface IDocumentaryVisualGenerationResultMapper { DocumentaryVisualGenerationResult Map(DocumentaryVisualGenerationRequest request,DocumentaryProductionVisualAdapterResult result); }
public sealed class DocumentaryVisualGenerationResultMapper : IDocumentaryVisualGenerationResultMapper
{
    public DocumentaryVisualGenerationResult Map(DocumentaryVisualGenerationRequest request,DocumentaryProductionVisualAdapterResult result)
    {
        var d=result.Artifact;var status=result.Succeeded?DocumentaryMediaAssetStatus.Generated:DocumentaryMediaAssetStatus.Failed;
        var asset=new DocumentaryMediaAssetResult(request.AssetPlan.AssetId,request.AssetPlan.AssetType,request.AssetFormat,status,result.ActualProviderId,d?.ContentIdentity,d?.Length??0,0,d?.Width??0,d?.Height??0,0,0,0,d?.Checksum,result.Failure?.Code.ToString(),result.Failure?.Message,d?.AttemptCount??request.Attempt,request.CorrelationId);
        return new(status,asset,result.Failure?.Code.ToString(),result.Failure?.Message);
    }
}

public sealed class DocumentaryProductionAdapterRegistry(IDocumentaryProductionVisualAdapter visual, IDocumentaryProductionNarrationAdapter narration, IDocumentaryProductionSubtitleAdapter subtitle, IDocumentaryProductionSceneCompositionAdapter scene, IDocumentaryProductionVariantCompositionAdapter variant) : IDocumentaryProductionAdapterRegistry
{ public bool IsAvailable(DocumentaryProductionOperationKind operation)=>operation is DocumentaryProductionOperationKind.VisualGeneration or DocumentaryProductionOperationKind.NarrationSynthesis or DocumentaryProductionOperationKind.SubtitleGeneration or DocumentaryProductionOperationKind.SceneComposition or DocumentaryProductionOperationKind.VariantComposition; public IDocumentaryProductionVisualAdapter VisualGeneration=>visual; public IDocumentaryProductionNarrationAdapter NarrationSynthesis=>narration; public IDocumentaryProductionSubtitleAdapter SubtitleGeneration=>subtitle; public IDocumentaryProductionSceneCompositionAdapter SceneComposition=>scene; public IDocumentaryProductionVariantCompositionAdapter VariantComposition=>variant; }
