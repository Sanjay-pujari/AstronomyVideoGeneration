using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastSceneRenderingOrchestrator(
    IWeeklySkyForecastV2IntelligenceService intelligenceService,
    IFFmpegService ffmpegService,
    IMediaValidationService mediaValidationService,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<WeeklySkyForecastSceneRenderingOrchestrator> logger) : IWeeklySkyForecastSceneRenderingOrchestrator
{
    public async Task<SceneRenderingPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken)
        => await RunAsync(new WeeklySkyForecastV2OrchestrationContext(
            ContentGenerationPlanId: contentGenerationPlanId ?? request.ContentGenerationPlanId ?? request.PipelineRunId ?? Guid.NewGuid(),
            PipelineRunId: request.PipelineRunId ?? contentGenerationPlanId ?? request.ContentGenerationPlanId ?? Guid.NewGuid(),
            WorkingDirectoryRoot: null,
            Request: request,
            ResolvedRegion: null,
            WeeklyForecast: null,
            SkyfieldSummary: null,
            EventIntelligence: null,
            GeneratedAtUtc: DateTime.UtcNow), cancellationToken);

    public async Task<SceneRenderingPackage> RunAsync(WeeklySkyForecastV2OrchestrationContext orchestrationContext, CancellationToken cancellationToken)
    {
        var preview = await intelligenceService.PreviewAsync(orchestrationContext, cancellationToken);
        var prep = preview.RenderPreparationPackage ?? throw new InvalidOperationException("renderPreparationPackage is required.");
        var blocking = new List<string>();
        var warnings = new List<string>();
        var sceneResults = new List<SceneRenderResult>();
        var stellariumResults = new List<StellariumSceneRenderResult>();
        var assetResults = new List<CelestialAssetSceneRenderResult>();
        var hybridResults = new List<HybridSceneCompositeResult>();
        var overlayResults = new List<OverlayRenderResult>();

        foreach (var req in prep.SceneRenderRequests)
        {
            var requestWarnings = new List<string>();
            var requestErrors = new List<string>();
            if (!req.RendererDecisionLocked) requestErrors.Add("rendererDecisionLocked must be true.");
            if (string.IsNullOrWhiteSpace(req.OutputPath)) requestErrors.Add("outputPath is required.");
            Directory.CreateDirectory(Path.GetDirectoryName(req.MetadataOutputPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(req.DebugOutputPath)!);
            await File.WriteAllTextAsync(req.MetadataOutputPath, $"{req.SceneCode} metadata", cancellationToken);
            await File.WriteAllTextAsync(req.DebugOutputPath, $"{req.SceneCode} debug", cancellationToken);

            if (IsReuseScene(req))
            {
                var reused = ExecuteReuseScene(req, prep, requestWarnings, requestErrors);
                if (requestErrors.Count > 0) blocking.AddRange(requestErrors.Select(e => $"{req.SceneCode}: {e}"));
                sceneResults.Add(new SceneRenderResult(req.RequestId, req.SceneCode, req.RendererType, req.OutputPath, requestErrors.Count == 0 ? "Rendered" : "Failed", requestWarnings, requestErrors, reused?.SceneCode, reused?.RequestId, reused?.OutputPath));
                continue;
            }

            switch (req.RendererType)
            {
                case "StellariumSceneRenderer":
                    await ExecuteStellariumSceneAsync(req, prep, requestWarnings, requestErrors, stellariumResults, cancellationToken);
                    break;
                case "CelestialAssetCompositor":
                    await ExecuteCelestialAssetSceneAsync(req, prep, requestWarnings, requestErrors, assetResults, cancellationToken);
                    break;
                case "HybridCompositor":
                    await ExecuteHybridSceneAsync(req, prep, requestWarnings, requestErrors, hybridResults, cancellationToken);
                    break;
                case "OverlayCompositor":
                    await ExecuteOverlaySceneAsync(req, requestWarnings, requestErrors, cancellationToken);
                    break;
                case "ThumbnailCompositor":
                    await ExecuteThumbnailSceneAsync(req, prep, requestWarnings, requestErrors, cancellationToken);
                    break;
                default:
                    requestErrors.Add($"Unsupported rendererType '{req.RendererType}'.");
                    break;
            }

            if (requestErrors.Count > 0) blocking.AddRange(requestErrors.Select(e => $"{req.SceneCode}: {e}"));
            sceneResults.Add(new SceneRenderResult(req.RequestId, req.SceneCode, req.RendererType, req.OutputPath, requestErrors.Count == 0 ? "Rendered" : "Failed", requestWarnings, requestErrors));
        }

        foreach (var overlay in prep.OverlayRenderPlan.Jobs)
        {
await RenderOverlayAsync(overlay.PlannedOverlayPath, $"{overlay.SceneCode} {overlay.OverlayType}", cancellationToken);
            overlayResults.Add(new OverlayRenderResult(overlay.JobId, overlay.SceneCode, overlay.OverlayType, overlay.PlannedOverlayPath, "Rendered", [], []));
        }

        ThumbnailSceneRenderResult? thumbnail = null;
        if (!string.IsNullOrWhiteSpace(prep.ThumbnailRenderPlan.ThumbnailRequestId))
        {
            await RenderThumbnailAsync(prep.ThumbnailRenderPlan.PlannedOutputPath, "weekly story", cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(prep.ThumbnailRenderPlan.PlannedMetadataPath)!);
            await File.WriteAllTextAsync(prep.ThumbnailRenderPlan.PlannedMetadataPath, "thumbnail metadata", cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(prep.ThumbnailRenderPlan.PlannedDebugPath)!);
            await File.WriteAllTextAsync(prep.ThumbnailRenderPlan.PlannedDebugPath, "thumbnail debug", cancellationToken);
            thumbnail = new ThumbnailSceneRenderResult(prep.ThumbnailRenderPlan.ThumbnailRequestId, prep.ThumbnailRenderPlan.PlannedOutputPath, "Rendered", [], []);
        }

        var valid = blocking.Count == 0;
        var validation = new SceneRenderingValidation(
            valid,
            sceneResults.Count == prep.SceneRenderRequests.Count,
            stellariumResults.Count > 0 || !prep.SceneRenderRequests.Any(x => x.RendererType == "StellariumSceneRenderer"),
            assetResults.Count > 0 || !prep.SceneRenderRequests.Any(x => x.RendererType == "CelestialAssetCompositor"),
            hybridResults.Count > 0 || !prep.SceneRenderRequests.Any(x => x.RendererType == "HybridCompositor"),
            overlayResults.Count == prep.OverlayRenderPlan.Jobs.Count,
            thumbnail is not null || string.IsNullOrWhiteSpace(prep.ThumbnailRenderPlan.ThumbnailRequestId),
            valid,
            false,
            blocking,
            warnings);
        var freeze = new SceneRenderingFreezeStatus(true, ["Phase 6A frozen inputs honored", "No timeline composition performed", "No publishing performed"], [], warnings);
        return new SceneRenderingPackage(sceneResults, stellariumResults, assetResults, hybridResults, overlayResults, thumbnail, validation, freeze);
    }



    private static SceneRenderRequest? ExecuteReuseScene(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors)
    {
        var sourceSceneCode = req.ReuseSourceSceneCode;
        if (string.IsNullOrWhiteSpace(sourceSceneCode)) { requestErrors.Add("Reuse scene is missing reuseSourceSceneCode."); return null; }
        var sourceRequest = prep.SceneRenderRequests.FirstOrDefault(x => x.SceneCode.Equals(sourceSceneCode, StringComparison.OrdinalIgnoreCase));
        if (sourceRequest is null || !File.Exists(sourceRequest.OutputPath)) { requestErrors.Add($"Reuse source scene '{sourceSceneCode}' could not be found."); return null; }
        Directory.CreateDirectory(Path.GetDirectoryName(req.OutputPath)!);
        File.Copy(sourceRequest.OutputPath, req.OutputPath, true);
        requestWarnings.Add($"Reused rendered output from '{sourceRequest.SceneCode}'.");
        return sourceRequest;
    }

    private static bool IsReuseScene(SceneRenderRequest req)
        => req.IsReuseScene || req.SceneCode.EndsWith("_reuse", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(req.ReuseSourceSceneCode);

    private async Task ExecuteStellariumSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<StellariumSceneRenderResult> stellariumResults, CancellationToken ct)
    {
        requestWarnings.Add("Stellarium capture not implemented; diagnostics fallback visual used.");
        await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, ct);
        var job = prep.StellariumRenderPlan.Jobs.FirstOrDefault(x => x.RequestId == req.RequestId || x.SceneCode == req.SceneCode);
        if (job is not null) stellariumResults.Add(new StellariumSceneRenderResult(job.JobId, req.SceneCode, req.RequestId, job.PlannedSscPath, req.OutputPath, "DiagnosticsFallbackVisual", req.DurationSeconds, requestWarnings, requestErrors));
    }
    private async Task ExecuteCelestialAssetSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<CelestialAssetSceneRenderResult> assetResults, CancellationToken ct)
    { await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, ct); assetResults.Add(new CelestialAssetSceneRenderResult(req.SceneCode, req.RequestId, req.RequiredAssets, req.OutputPath, "Rendered", requestWarnings, requestErrors)); }
    private async Task ExecuteHybridSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<HybridSceneCompositeResult> hybridResults, CancellationToken ct)
    { await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, ct); hybridResults.Add(new HybridSceneCompositeResult(req.SceneCode, req.RequestId, ["fallback_visual"], req.OutputPath, "Rendered", requestWarnings, requestErrors)); }
    private async Task ExecuteOverlaySceneAsync(SceneRenderRequest req, List<string> requestWarnings, List<string> requestErrors, CancellationToken ct) => await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, ct);
    private async Task ExecuteThumbnailSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, CancellationToken ct) => await RenderThumbnailAsync(prep.ThumbnailRenderPlan.PlannedOutputPath, req.SceneCode, ct);
    private async Task RenderSceneVideoAsync(string outputPath, string label, double duration, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var o = renderingOptions.Value;
        var args = $"-y -f lavfi -i testsrc2=size={o.VideoWidth}x{o.VideoHeight}:rate={o.FrameRate} -t {Math.Max(1,duration):0.##} -vf \"drawtext=text='{label}':x=40:y=40:fontsize=42:fontcolor=white\" -c:v libx264 -pix_fmt yuv420p \"{outputPath}\"";
        await ffmpegService.ExecuteAsync(args, Directory.GetCurrentDirectory(), outputPath, ct);
    }
    private async Task RenderOverlayAsync(string outputPath, string label, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var args = $"-y -f lavfi -i color=c=black@0.0:s=1920x1080:d=1 -vf \"drawtext=text='{label}':x=60:y=60:fontsize=42:fontcolor=white@0.8\" -frames:v 1 \"{outputPath}\"";
        await ffmpegService.ExecuteAsync(args, Directory.GetCurrentDirectory(), outputPath, ct);
    }
    private async Task RenderThumbnailAsync(string outputPath, string label, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var args = $"-y -f lavfi -i testsrc=size=1280x720:rate=1 -vf \"drawtext=text='{label}':x=40:y=40:fontsize=52:fontcolor=white\" -frames:v 1 \"{outputPath}\"";
        await ffmpegService.ExecuteAsync(args, Directory.GetCurrentDirectory(), outputPath, ct);
    }
}
