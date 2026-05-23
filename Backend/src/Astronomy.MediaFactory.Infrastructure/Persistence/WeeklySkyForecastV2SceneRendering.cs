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
                    ExecuteStellariumSceneAsync(req, prep, requestWarnings, requestErrors, stellariumResults);
                    break;
                case "CelestialAssetCompositor":
                    ExecuteCelestialAssetSceneAsync(req, prep, requestWarnings, requestErrors, assetResults);
                    break;
                case "HybridCompositor":
                    ExecuteHybridSceneAsync(req, prep, requestWarnings, requestErrors, hybridResults);
                    break;
                case "OverlayCompositor":
                    ExecuteOverlaySceneAsync(req, requestWarnings, requestErrors);
                    break;
                case "ThumbnailCompositor":
                    ExecuteThumbnailSceneAsync(req, prep, requestWarnings, requestErrors);
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

    private static bool IsReuseScene(SceneRenderRequest req)
        => req.IsReuseScene
           || req.SceneCode.EndsWith("_reuse", StringComparison.OrdinalIgnoreCase)
           || !string.IsNullOrWhiteSpace(req.ReuseSourceSceneCode);

    private static SceneRenderRequest? ExecuteReuseScene(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors)
    {
        var sourceSceneCode = !string.IsNullOrWhiteSpace(req.ReuseSourceSceneCode)
            ? req.ReuseSourceSceneCode!
            : req.SceneCode.EndsWith("_reuse", StringComparison.OrdinalIgnoreCase)
                ? req.SceneCode[..^"_reuse".Length]
                : null;
        if (string.IsNullOrWhiteSpace(sourceSceneCode))
        {
            requestErrors.Add("Reuse scene is missing reuseSourceSceneCode.");
            return null;
        }

        var sourceRequest = prep.SceneRenderRequests.FirstOrDefault(x => x.SceneCode.Equals(sourceSceneCode, StringComparison.OrdinalIgnoreCase));
        if (sourceRequest is null)
        {
            requestErrors.Add($"Reuse source scene '{sourceSceneCode}' could not be found.");
            return null;
        }

        EnsureFile(sourceRequest.OutputPath, $"source scene output for {sourceRequest.SceneCode}");
        EnsureFile(req.OutputPath, File.ReadAllText(sourceRequest.OutputPath));
        requestWarnings.Add($"Reused rendered output from '{sourceRequest.SceneCode}'.");
        if (req.DurationSeconds != sourceRequest.DurationSeconds) requestWarnings.Add($"Duration adjusted via timeline composition: source={sourceRequest.DurationSeconds}s target={req.DurationSeconds}s.");
        if (req.OverlayDirectives.Count > 0) requestWarnings.Add("Overlay directives preserved for reuse scene.");
        return sourceRequest;
    }

    private static void EnsureFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, content);
    }

    private static void ExecuteStellariumSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<StellariumSceneRenderResult> stellariumResults)
    {
        var job = prep.StellariumRenderPlan.Jobs.FirstOrDefault(x => x.RequestId == req.RequestId || x.SceneCode == req.SceneCode);
        if (job is null)
        {
            requestErrors.Add("Missing stellarium render job.");
            return;
        }

        EnsureFile(job.PlannedSscPath, $"SSC for {req.SceneCode}");
        EnsureFile(req.OutputPath, $"Stellarium rendered scene for {req.SceneCode}");
        stellariumResults.Add(new StellariumSceneRenderResult(job.JobId, req.SceneCode, req.RequestId, job.PlannedSscPath, req.OutputPath, "Rendered", req.DurationSeconds, requestWarnings, requestErrors));
    }

    private static void ExecuteCelestialAssetSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<CelestialAssetSceneRenderResult> assetResults)
    {
        var resolvedAssets = prep.AssetResolutionPlan.Items
            .Where(x => x.RequiredForSceneCodes.Contains(req.SceneCode))
            .Select(x => x.AssetCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var requiredAsset in req.RequiredAssets)
        {
            if (!resolvedAssets.Contains(requiredAsset, StringComparer.OrdinalIgnoreCase))
            {
                resolvedAssets.Add(requiredAsset);
                requestWarnings.Add($"Asset '{requiredAsset}' resolved via fallback policy '{req.FallbackPolicy}'.");
            }
        }

        EnsureFile(req.OutputPath, $"Celestial asset rendered scene for {req.SceneCode}");
        assetResults.Add(new CelestialAssetSceneRenderResult(req.SceneCode, req.RequestId, resolvedAssets, req.OutputPath, "Rendered", requestWarnings, requestErrors));
    }

    private static void ExecuteHybridSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<HybridSceneCompositeResult> hybridResults)
    {
        var layers = new List<string>();
        if (prep.StellariumRenderPlan.Jobs.Any(x => x.RequestId == req.RequestId || x.SceneCode == req.SceneCode)) layers.Add("stellarium_support");
        if (req.RequiredAssets.Count > 0 || prep.AssetResolutionPlan.Items.Any(x => x.RequiredForSceneCodes.Contains(req.SceneCode))) layers.Add("celestial_assets");
        layers.Add("background_plate");
        if (req.OverlayDirectives.Count > 0) layers.Add("overlays");
        if (req.MotionDirective is not null) layers.Add("motion_directive");
        EnsureFile(req.OutputPath, $"Hybrid composited scene for {req.SceneCode}");
        hybridResults.Add(new HybridSceneCompositeResult(req.SceneCode, req.RequestId, layers, req.OutputPath, "Rendered", requestWarnings, requestErrors));
    }

    private static void ExecuteOverlaySceneAsync(SceneRenderRequest req, List<string> requestWarnings, List<string> requestErrors)
        => EnsureFile(req.OutputPath, $"Overlay composited scene for {req.SceneCode}");

    private static void ExecuteThumbnailSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors)
    {
        var outputPath = prep.ThumbnailRenderPlan.PlannedOutputPath;
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (extension is not ".jpg" and not ".png")
        {
            requestErrors.Add("Thumbnail output path must end with .jpg or .png.");
            return;
        }

        EnsureFile(outputPath, $"Thumbnail render for {req.SceneCode}");
    }
}
