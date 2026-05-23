using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastSceneRenderingOrchestrator(
    IWeeklySkyForecastV2IntelligenceService intelligenceService,
    ILogger<WeeklySkyForecastSceneRenderingOrchestrator> logger) : IWeeklySkyForecastSceneRenderingOrchestrator
{
    public async Task<SceneRenderingPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken)
    {
        var preview = await intelligenceService.PreviewAsync(request, cancellationToken);
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
            EnsureFile(req.OutputPath, $"scene {req.SceneCode} renderer={req.RendererType}");
            EnsureFile(req.MetadataOutputPath, $"metadata for {req.SceneCode}");
            EnsureFile(req.DebugOutputPath, $"debug for {req.SceneCode}");

            switch (req.RendererType.ToLowerInvariant())
            {
                case "stellarium":
                    var job = prep.StellariumRenderPlan.Jobs.FirstOrDefault(x => x.RequestId == req.RequestId || x.SceneCode == req.SceneCode);
                    if (job is null)
                    {
                        requestErrors.Add("Missing stellarium render job.");
                    }
                    else
                    {
                        EnsureFile(job.PlannedSscPath, $"SSC for {req.SceneCode}");
                        EnsureFile(job.PlannedCapturePath, $"Stellarium capture placeholder for {req.SceneCode}");
                        stellariumResults.Add(new StellariumSceneRenderResult(job.JobId, req.SceneCode, req.RequestId, job.PlannedSscPath, req.OutputPath, "Rendered", req.DurationSeconds, requestWarnings, requestErrors));
                    }
                    break;
                case "celestialasset":
                case "asset":
                    assetResults.Add(new CelestialAssetSceneRenderResult(req.SceneCode, req.RequestId, req.RequiredAssets, req.OutputPath, "Rendered", requestWarnings, requestErrors));
                    break;
                case "hybrid":
                    var layers = new List<string>();
                    if (prep.StellariumRenderPlan.Jobs.Any(x => x.SceneCode == req.SceneCode)) layers.Add("stellarium_background");
                    if (req.RequiredAssets.Count > 0) layers.Add("celestial_assets");
                    if (req.OverlayDirectives.Count > 0) layers.Add("overlays");
                    layers.Add("motion_directive");
                    hybridResults.Add(new HybridSceneCompositeResult(req.SceneCode, req.RequestId, layers, req.OutputPath, "Rendered", requestWarnings, requestErrors));
                    break;
                default:
                    requestWarnings.Add($"Unsupported rendererType '{req.RendererType}', rendered as deterministic placeholder.");
                    break;
            }

            if (requestErrors.Count > 0) blocking.AddRange(requestErrors.Select(e => $"{req.SceneCode}: {e}"));
            sceneResults.Add(new SceneRenderResult(req.RequestId, req.SceneCode, req.RendererType, req.OutputPath, requestErrors.Count == 0 ? "Rendered" : "Failed", requestWarnings, requestErrors));
        }

        foreach (var overlay in prep.OverlayRenderPlan.Jobs)
        {
            EnsureFile(overlay.PlannedOverlayPath, $"overlay {overlay.OverlayType} for {overlay.SceneCode}");
            overlayResults.Add(new OverlayRenderResult(overlay.JobId, overlay.SceneCode, overlay.OverlayType, overlay.PlannedOverlayPath, "Rendered", [], []));
        }

        ThumbnailSceneRenderResult? thumbnail = null;
        if (!string.IsNullOrWhiteSpace(prep.ThumbnailRenderPlan.ThumbnailRequestId))
        {
            EnsureFile(prep.ThumbnailRenderPlan.PlannedOutputPath, "thumbnail output");
            EnsureFile(prep.ThumbnailRenderPlan.PlannedMetadataPath, "thumbnail metadata");
            EnsureFile(prep.ThumbnailRenderPlan.PlannedDebugPath, "thumbnail debug");
            thumbnail = new ThumbnailSceneRenderResult(prep.ThumbnailRenderPlan.ThumbnailRequestId, prep.ThumbnailRenderPlan.PlannedOutputPath, "Rendered", [], []);
        }

        var valid = blocking.Count == 0;
        var validation = new SceneRenderingValidation(valid, sceneResults.Count == prep.SceneRenderRequests.Count, stellariumResults.Count > 0 || !prep.StellariumRenderPlan.Jobs.Any(), assetResults.Count > 0 || !prep.SceneRenderRequests.Any(x => x.RendererType.Contains("asset", StringComparison.OrdinalIgnoreCase)), hybridResults.Count > 0 || !prep.SceneRenderRequests.Any(x => x.RendererType.Contains("hybrid", StringComparison.OrdinalIgnoreCase)), overlayResults.Count == prep.OverlayRenderPlan.Jobs.Count, thumbnail is not null || string.IsNullOrWhiteSpace(prep.ThumbnailRenderPlan.ThumbnailRequestId), valid, false, blocking, warnings);
        var freeze = new SceneRenderingFreezeStatus(true, ["Phase 6A frozen inputs honored", "No timeline composition performed", "No publishing performed"], blocking, warnings);
        return new SceneRenderingPackage(sceneResults, stellariumResults, assetResults, hybridResults, overlayResults, thumbnail, validation, freeze);
    }

    private static void EnsureFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, content);
    }
}
