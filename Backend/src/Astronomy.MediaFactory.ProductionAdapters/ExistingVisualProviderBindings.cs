using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.ProductionAdapters;

internal static class DocumentaryVisualBindingFiles
{
    public static string Target(DocumentaryVisualProviderRequest request, string stem) =>
        Path.Combine(request.OutputDirectory, $"{stem}.{(request.Format == DocumentaryMediaAssetFormat.Png ? "png" : "jpg")}");

    public static async Task<string> OwnAsync(string source, string target, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) throw new FileNotFoundException("Provider output is missing.", source);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!Path.GetFullPath(source).Equals(Path.GetFullPath(target), DocumentaryPathComparison.Comparison))
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
            await output.FlushAsync(token);
            output.Flush(true);
        }
        if (!DocumentaryPathComparison.IsBelow(Path.GetDirectoryName(target)!, target))
            throw new UnauthorizedAccessException("Provider output escaped its owned directory.");
        return target;
    }

    public static DocumentaryVisualProviderResponse Failed(DocumentaryProductionFailureCode code, string message) => new(null, new(code, message));
}

public sealed class StellariumDocumentaryVisualProviderBinding(StellariumVisualGenerationService service) : IDocumentaryVisualProviderBinding
{
    public string ProviderId => DocumentaryVisualProviderIds.Stellarium;
    public async Task<DocumentaryVisualProviderResponse> GenerateAsync(DocumentaryVisualProviderRequest request, CancellationToken token)
    {
        if (request.VisualType is not (DocumentaryMediaAssetType.SkySimulationImage or DocumentaryMediaAssetType.TelescopeViewImage or DocumentaryMediaAssetType.StarChartImage) || string.IsNullOrWhiteSpace(request.SceneId) || string.IsNullOrWhiteSpace(request.Prompt))
            return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProviderRejectedRequest, "Stellarium requires a supported type, scene, and astronomical instruction.");
        try
        {
            var translated = new StellariumDocumentaryVisualRequest(request.SceneId, request.Prompt, request.Prompt, DateOnly.FromDateTime(DateTime.UnixEpoch), string.Empty, request.Width, request.Height, request.OutputDirectory);
            var source = await service.GenerateSingleVisualAsync(translated, token);
            return new(await DocumentaryVisualBindingFiles.OwnAsync(source, DocumentaryVisualBindingFiles.Target(request, "provider-stellarium"), token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (TimeoutException) { return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProcessTimedOut, "Stellarium capture timed out."); }
        catch (FileNotFoundException) { return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.DependencyMissing, "Stellarium or its capture is missing."); }
        catch (InvalidDataException) { return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProviderInvalidResponse, "Stellarium returned an ambiguous or missing capture."); }
        catch (System.ComponentModel.Win32Exception) { return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProcessStartFailed, "Stellarium could not be started."); }
    }
}

public sealed class AzureOpenAICinematicDocumentaryVisualProviderBinding(IAICinematicImageGenerator generator) : IDocumentaryVisualProviderBinding
{
    public string ProviderId => DocumentaryVisualProviderIds.AzureOpenAICinematicImage;
    public async Task<DocumentaryVisualProviderResponse> GenerateAsync(DocumentaryVisualProviderRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt)) return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProviderRejectedRequest, "An image prompt is required.");
        var target = DocumentaryVisualBindingFiles.Target(request, "provider-azure-openai");
        try
        {
            var translated = new AICinematicAssetRequest(request.AssetId, request.SceneId ?? request.SourceInstructionId, request.VisualType.ToString(), "Documentary", request.AssetId, request.VariantId, "neutral", "support", "documentary", request.Prompt, string.Empty, request.Width, request.Height, target);
            var result = await generator.GenerateAsync(translated, token);
            if (!result.ProviderConfigured) return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProviderUnavailable, "The cinematic image provider is not configured.");
            if (!result.GenerationStatus.Equals("Generated", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(result.ImagePath)) return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProviderInvalidResponse, "The cinematic image provider returned no image.");
            return new(await DocumentaryVisualBindingFiles.OwnAsync(result.ImagePath, target, token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (TimeoutException) { return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProviderTimeout, "The cinematic image provider timed out."); }
        catch (UnauthorizedAccessException) { return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProviderAuthenticationFailed, "The cinematic image provider rejected authentication."); }
        catch (IOException) { return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.FileSystemFailure, "The cinematic image could not be materialized."); }
    }
}

public sealed class AstronomyInfographicDocumentaryVisualProviderBinding(IAstronomyInfographicRenderer renderer) : IDocumentaryVisualProviderBinding
{
    public string ProviderId => DocumentaryVisualProviderIds.AstronomyInfographic;
    public async Task<DocumentaryVisualProviderResponse> GenerateAsync(DocumentaryVisualProviderRequest request, CancellationToken token)
    {
        if (request.VisualType is not (DocumentaryMediaAssetType.StarChartImage or DocumentaryMediaAssetType.ScientificDiagramImage) || request.Format != DocumentaryMediaAssetFormat.Png)
            return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProviderRejectedRequest, "The infographic renderer supports PNG star charts and scientific diagrams only.");
        var target = DocumentaryVisualBindingFiles.Target(request, "provider-infographic");
        try
        {
            var spec = new QuestionDrivenVisualSpec(request.AssetId, request.CorrelationId, "en", request.Attempt, request.VisualType.ToString(), request.SceneId ?? request.AssetId, request.Prompt, request.Prompt, request.Prompt, request.Prompt, 1, request.Prompt, [], [], [], DateTimeOffset.UnixEpoch, request.VisualType.ToString(), false);
            await renderer.RenderAsync(target, spec, string.Empty, string.Empty, token);
            return File.Exists(target) ? new(target) : DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.OutputArtifactMissing, "The infographic renderer produced no image.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (IOException) { return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.FileSystemFailure, "The infographic could not be written."); }
        catch (ArgumentException) { return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.SourceArtifactInvalid, "The infographic source specification was invalid."); }
    }
}

public sealed class FileVisualAssetDocumentaryVisualProviderBinding(FileVisualAssetProvider provider) : IDocumentaryVisualProviderBinding
{
    public string ProviderId => DocumentaryVisualProviderIds.FileVisualAsset;
    public async Task<DocumentaryVisualProviderResponse> GenerateAsync(DocumentaryVisualProviderRequest request, CancellationToken token)
    {
        if (request.VisualType is not (DocumentaryMediaAssetType.VisualImage or DocumentaryMediaAssetType.HistoricalIllustrationImage)) return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProviderRejectedRequest, "Local assets are fallback-only for visual illustrations.");
        try
        {
            var source = await provider.SelectExistingAssetAsync(request.Prompt, token);
            if (source is null) return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.SourceArtifactMissing, "No deterministic local asset matched the requested key.");
            return new(await DocumentaryVisualBindingFiles.OwnAsync(source, DocumentaryVisualBindingFiles.Target(request, "provider-local"), token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.FileSystemFailure, "The local asset could not be copied."); }
    }
}

public sealed class CelestialAssetDocumentaryVisualProviderBinding(ICelestialAssetProvider provider) : IDocumentaryVisualProviderBinding
{
    public string ProviderId => DocumentaryVisualProviderIds.CelestialAsset;
    public async Task<DocumentaryVisualProviderResponse> GenerateAsync(DocumentaryVisualProviderRequest request, CancellationToken token)
    {
        if (request.VisualType is not (DocumentaryMediaAssetType.VisualImage or DocumentaryMediaAssetType.TelescopeViewImage) || string.IsNullOrWhiteSpace(request.Prompt)) return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProviderRejectedRequest, "A celestial fallback requires a subject.");
        try
        {
            var asset = await provider.GetAssetAsync(new CelestialAssetRequest { ObjectName = request.Prompt, ObjectType = request.VisualType.ToString(), PreferPortraitSafe = request.Height > request.Width }, token);
            if (asset is null || string.IsNullOrWhiteSpace(asset.LocalPath)) return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.ProviderInvalidResponse, "The celestial provider returned no asset path.");
            if (!File.Exists(asset.LocalPath)) return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.SourceArtifactMissing, "The celestial asset is unavailable.");
            return new(await DocumentaryVisualBindingFiles.OwnAsync(asset.LocalPath, DocumentaryVisualBindingFiles.Target(request, "provider-celestial"), token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DocumentaryVisualBindingFiles.Failed(DocumentaryProductionFailureCode.FileSystemFailure, "The celestial asset could not be copied."); }
    }
}
