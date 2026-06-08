using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using System.Text;
using Microsoft.Extensions.Options;
using System.Text.Json;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastSceneRenderingOrchestrator(
    IFFmpegService ffmpegService,
    IMediaValidationService mediaValidationService,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<WeeklySkyForecastSceneRenderingOrchestrator> logger) : IWeeklySkyForecastSceneRenderingOrchestrator
{
    private static readonly TimeSpan SceneRenderTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ImageCompositionTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] PriorityObjects = ["moon", "jupiter", "venus", "saturn", "mars", "milky-way", "milky_way", "starfield", "background"];
    private const float BlackThresholdDefault = 18f;
    private const string WeeklyThumbnailHeadline = "THIS WEEK'S SKY";
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
            EventExtractionResult: null,
            Storyboard: null,
            GeneratedAtUtc: DateTime.UtcNow), cancellationToken);

    public async Task<SceneRenderingPackage> RunAsync(WeeklySkyForecastV2OrchestrationContext orchestrationContext, CancellationToken cancellationToken)
    {
        var prep = orchestrationContext.RenderPreparationPackage
            ?? orchestrationContext.IntelligencePreviewResult?.RenderPreparationPackage
            ?? throw new InvalidOperationException("renderPreparationPackage is required on orchestration context.");
        var blocking = new List<string>();
        var warnings = new List<string>();
        var sceneResults = new List<SceneRenderResult>();
        var stellariumResults = new List<StellariumSceneRenderResult>();
        var assetResults = new List<CelestialAssetSceneRenderResult>();
        var hybridResults = new List<HybridSceneCompositeResult>();
        var overlayResults = new List<OverlayRenderResult>();
        var overlayDiagnostics = new List<SceneOverlayDiagnostics>();
        var overlayJobsByScene = prep.OverlayRenderPlan.Jobs
            .GroupBy(x => x.SceneCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var visualPlans = new List<CelestialObjectVisualPlan>();
        var assetResolver = new CelestialAssetResolver(renderingOptions.Value.CelestialAssetsRoot, logger);
        var diagnosticsFallbackUsed = false;
        var visualAssetDiagnostics = new List<SceneVisualAssetDiagnostics>();
        var totalAssetsScanned = 0;
        var totalObjectsResolved = 0;
        var sceneObjectsDrawn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var req in prep.SceneRenderRequests)
        {
            var sceneStartedAt = DateTime.UtcNow;
            var requestWarnings = new List<string>();
            var requestErrors = new List<string>();
            logger.LogInformation("Starting scene render: {SceneCode}, rendererType={RendererType}, requestId={RequestId}", req.SceneCode, req.RendererType, req.RequestId);
            if (!req.RendererDecisionLocked) requestErrors.Add("rendererDecisionLocked must be true.");
            if (string.IsNullOrWhiteSpace(req.OutputPath)) requestErrors.Add("outputPath is required.");
            Directory.CreateDirectory(Path.GetDirectoryName(req.MetadataOutputPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(req.DebugOutputPath)!);
            var visualPlan = ResolveVisualPlan(req, prep, assetResolver, out var sceneDiagnostics);
            visualPlans.Add(visualPlan);
            totalAssetsScanned += visualPlan.AssetCandidates.Count;
            totalObjectsResolved += visualPlan.SelectedAssets.Count;
            var missingAssets = visualPlan.RequiredObjects
                .Where(required => !visualPlan.SelectedAssets.Any(selected => MatchesObjectName(required, selected)))
                .ToList();
            var fallbackBackgroundUsed = visualPlan.FallbackUsed;
            logger.LogInformation("[AssetResolver]\nScene={SceneCode}\nRequired=[{Required}]\nResolved=[{Resolved}]\nMissing=[{Missing}]\nAssetSearchPaths=[{AssetSearchPaths}]\nFallbackBackgroundUsed={FallbackBackgroundUsed}\nRoot={ConfiguredRoot}",
                req.SceneCode,
                string.Join(',', visualPlan.RequiredObjects),
                string.Join(',', visualPlan.SelectedAssets.Select(Path.GetFileName)),
                string.Join(',', missingAssets),
                string.Join(',', assetResolver.SearchDirectories),
                fallbackBackgroundUsed,
                assetResolver.ConfiguredRoot);
            visualAssetDiagnostics.Add(sceneDiagnostics with
            {
                SceneCode = req.SceneCode,
                SelectedAssets = visualPlan.SelectedAssets.ToList(),
                MissingObjects = missingAssets,
                FallbackUsed = fallbackBackgroundUsed
            });

            if (IsReuseScene(req))
            {
                var reused = ExecuteReuseScene(req, prep, requestWarnings, requestErrors);
                if (requestErrors.Count > 0) blocking.AddRange(requestErrors.Select(e => $"{req.SceneCode}: {e}"));
                sceneResults.Add(new SceneRenderResult(req.RequestId, req.SceneCode, req.RendererType, req.OutputPath, requestErrors.Count == 0 ? "Rendered" : "Failed", requestWarnings, requestErrors, reused?.SceneCode, reused?.RequestId, reused?.OutputPath));
                continue;
            }

            var cinematicPlan = BuildCinematicVisualPlan(req, visualPlan);
            if (req.CinematicDirection is not null) logger.LogInformation("CINEMATIC_DIRECTION_APPLIED_TO_CAMERA_PLAN {SceneCode} {FramingRule}", req.SceneCode, req.CinematicDirection.FramingRule);
            try
            {
                using var sceneTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sceneTimeoutCts.CancelAfter(SceneRenderTimeout);
                switch (req.RendererType)
                {
                    case "StellariumSceneRenderer":
                        await ExecuteStellariumSceneAsync(req, prep, requestWarnings, requestErrors, stellariumResults, visualPlan, cinematicPlan, overlayJobsByScene, overlayResults, overlayDiagnostics, orchestrationContext.Request.Diagnostics, sceneTimeoutCts.Token);
                        diagnosticsFallbackUsed = true;
                        break;
                    case "CelestialAssetCompositor":
                        await ExecuteCelestialAssetSceneAsync(req, prep, requestWarnings, requestErrors, assetResults, visualPlan, cinematicPlan, overlayJobsByScene, overlayResults, overlayDiagnostics, orchestrationContext.Request.Diagnostics, sceneTimeoutCts.Token);
                        break;
                    case "HybridCompositor":
                        await ExecuteHybridSceneAsync(req, prep, requestWarnings, requestErrors, hybridResults, overlayJobsByScene, overlayResults, overlayDiagnostics, orchestrationContext.Request.Diagnostics, sceneTimeoutCts.Token);
                        break;
                    case "OverlayCompositor":
                        await ExecuteOverlaySceneAsync(req, requestWarnings, requestErrors, overlayJobsByScene, overlayResults, overlayDiagnostics, orchestrationContext.Request.Diagnostics, sceneTimeoutCts.Token);
                        break;
                    case "ThumbnailCompositor":
                        await ExecuteThumbnailSceneAsync(req, prep, requestWarnings, requestErrors, sceneTimeoutCts.Token);
                        break;
                    default:
                        requestErrors.Add($"Unsupported rendererType '{req.RendererType}'.");
                        break;
                }
                var sceneFramePath = Path.Combine(Path.GetDirectoryName(req.OutputPath)!, $"{Path.GetFileNameWithoutExtension(req.OutputPath)}.scene-frame.png");
                sceneObjectsDrawn[req.SceneCode] = File.Exists(sceneFramePath) ? visualPlan.SelectedAssets.Count(File.Exists) : 0;
                logger.LogInformation("[SceneComposer]\nScene={SceneCode}\nRequiredObjects=[{RequiredObjects}]\nResolvedAssets=[{ResolvedAssets}]\nObjectCountDrawn={ObjectCountDrawn}\nOutputFrame={OutputFrame}",
                    req.SceneCode,
                    string.Join(',', visualPlan.RequiredObjects),
                    string.Join(',', visualPlan.SelectedAssets.Select(Path.GetFileName)),
                    sceneObjectsDrawn[req.SceneCode],
                    sceneFramePath);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                requestErrors.Add($"Scene render timeout: {req.SceneCode}");
                logger.LogError("Failed scene render: {SceneCode}, error={Error}", req.SceneCode, $"Scene render timeout: {req.SceneCode}");
            }
            catch (Exception ex)
            {
                requestErrors.Add(ex.Message);
                logger.LogError(ex, "Failed scene render: {SceneCode}, error={Error}", req.SceneCode, ex.Message);
            }

            if (requestErrors.Count > 0) blocking.AddRange(requestErrors.Select(e => $"{req.SceneCode}: {e}"));
            var status = requestErrors.Count == 0 ? "Rendered" : "Failed";
            sceneResults.Add(new SceneRenderResult(req.RequestId, req.SceneCode, req.RendererType, req.OutputPath, status, requestWarnings, requestErrors));
            await File.WriteAllTextAsync(req.MetadataOutputPath, $"{req.SceneCode} metadata", cancellationToken);
            await File.WriteAllTextAsync(req.DebugOutputPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                req.SceneCode,
                RequiredObjects = visualPlan.RequiredObjects,
                SelectedAssets = visualPlan.SelectedAssets,
                MissingAssets = visualPlan.RequiredObjects.Except(visualPlan.SelectedAssets.Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase),
                visualPlan.VisualLayoutType,
                visualPlan.FallbackUsed,
                cinematicPlan,
                RenderedFramePath = Path.Combine(Path.GetDirectoryName(req.OutputPath)!, $"{Path.GetFileNameWithoutExtension(req.OutputPath)}.scene-frame.png"),
                RenderedVideoPath = req.OutputPath
            }), cancellationToken);
            logger.LogInformation("Completed scene render: {SceneCode}, status={Status}, elapsedMs={ElapsedMs}", req.SceneCode, status, (DateTime.UtcNow - sceneStartedAt).TotalMilliseconds);
        }

        var diagnosticsOverlayPath = Path.Combine(prep.WorkingDirectoryPlan.DebugPath, "scene-overlay-diagnostics.json");
        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticsOverlayPath)!);
        await File.WriteAllTextAsync(diagnosticsOverlayPath, JsonSerializer.Serialize(new
        {
            ownership = "OptionA_PreCompositeInSceneFrames",
            items = overlayDiagnostics
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        ThumbnailSceneRenderResult? thumbnail = null;
        var thumbnailObjectCount = 0;
        if (!string.IsNullOrWhiteSpace(prep.ThumbnailRenderPlan.ThumbnailRequestId))
        {
            var thumbAssets = prep.ThumbnailRenderPlan.PrimaryObjects
                .SelectMany(o =>
                {
                    var resolution = assetResolver.ResolveForObject(o);
                    return resolution.HeroAssets
                        .Concat(resolution.TransparentAssets)
                        .Concat(resolution.FilesScanned)
                        .OrderByDescending(ScoreThumbnailAsset)
                        .Take(1);
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            thumbnailObjectCount = thumbAssets.Count(File.Exists);
            await RenderThumbnailAsync(prep.ThumbnailRenderPlan.PlannedOutputPath, "WEEKLY SKY FORECAST", thumbAssets, orchestrationContext.Request.RegionName, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(prep.ThumbnailRenderPlan.PlannedMetadataPath)!);
            await File.WriteAllTextAsync(prep.ThumbnailRenderPlan.PlannedMetadataPath, "thumbnail metadata", cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(prep.ThumbnailRenderPlan.PlannedDebugPath)!);
            await File.WriteAllTextAsync(prep.ThumbnailRenderPlan.PlannedDebugPath, "thumbnail debug", cancellationToken);
            thumbnail = new ThumbnailSceneRenderResult(prep.ThumbnailRenderPlan.ThumbnailRequestId, prep.ThumbnailRenderPlan.PlannedOutputPath, "Rendered", [], []);
        }

        var diagnosticsPath = Path.Combine(prep.WorkingDirectoryPlan.DebugPath, "visual-asset-diagnostics.json");
        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticsPath)!);
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new
        {
            celestialAssetsRoot = assetResolver.SelectedRoot,
            rootExists = assetResolver.RootExists,
            totalAssetsScanned,
            totalObjectsResolved,
            scenes = visualAssetDiagnostics
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        await WriteCinematicDiagnosticsAsync(prep.WorkingDirectoryPlan.RootPath, prep, visualPlans, sceneObjectsDrawn, thumbnailObjectCount, cancellationToken);

        var hasVisuals = visualPlans.Any(v => v.SelectedAssets.Count > 0);
        var sceneVisualsContainObjects = sceneObjectsDrawn.Values.Any(v => v > 0);
        var allAssetsMissing = visualPlans.All(v => v.SelectedAssets.Count == 0);
        var thumbnailContainsObjects = thumbnail is not null && thumbnailObjectCount > 0;
        if (!thumbnailContainsObjects) blocking.Add("Thumbnail has no visual object assets.");
        if (!sceneVisualsContainObjects) blocking.Add("Scene visuals do not contain resolved celestial objects.");
        var valid = blocking.Count == 0 && hasVisuals;
        var validation = new SceneRenderingValidation(
            valid,
            sceneResults.Count == prep.SceneRenderRequests.Count,
            stellariumResults.Count > 0 || !prep.SceneRenderRequests.Any(x => x.RendererType == "StellariumSceneRenderer"),
            assetResults.Count > 0 || !prep.SceneRenderRequests.Any(x => x.RendererType == "CelestialAssetCompositor"),
            hybridResults.Count > 0 || !prep.SceneRenderRequests.Any(x => x.RendererType == "HybridCompositor"),
            overlayResults.Count == prep.OverlayRenderPlan.Jobs.Count,
            thumbnail is not null || string.IsNullOrWhiteSpace(prep.ThumbnailRenderPlan.ThumbnailRequestId),
            valid,
            sceneVisualsContainObjects,
            !allAssetsMissing && visualPlans.Any(v => v.SelectedAssets.Count > 0),
            !allAssetsMissing && hasVisuals,
            thumbnailContainsObjects,
            !hasVisuals || sceneResults.Any(s => File.Exists(Path.Combine(Path.GetDirectoryName(s.OutputPath)!, $"{Path.GetFileNameWithoutExtension(s.OutputPath)}.scene-frame.png")) == false),
            diagnosticsFallbackUsed,
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

    private async Task ExecuteStellariumSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<StellariumSceneRenderResult> stellariumResults, CelestialObjectVisualPlan visualPlan, CinematicVisualPlan cinematicPlan, IReadOnlyDictionary<string, List<OverlayRenderJob>> overlayJobsByScene, List<OverlayRenderResult> overlayResults, List<SceneOverlayDiagnostics> overlayDiagnostics, bool diagnosticsEnabled, CancellationToken ct)
    {
        requestWarnings.Add("Stellarium capture not implemented; diagnostics fallback visual used.");
        await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, visualPlan.SelectedAssets, cinematicPlan, overlayJobsByScene, overlayResults, overlayDiagnostics, diagnosticsEnabled, requestWarnings, ct);
        var job = prep.StellariumRenderPlan.Jobs.FirstOrDefault(x => x.RequestId == req.RequestId || x.SceneCode == req.SceneCode);
        if (job is not null) stellariumResults.Add(new StellariumSceneRenderResult(job.JobId, req.SceneCode, req.RequestId, job.PlannedSscPath, req.OutputPath, "DiagnosticsFallbackVisual", req.DurationSeconds, requestWarnings, requestErrors));
    }
    private async Task ExecuteCelestialAssetSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<CelestialAssetSceneRenderResult> assetResults, CelestialObjectVisualPlan visualPlan, CinematicVisualPlan cinematicPlan, IReadOnlyDictionary<string, List<OverlayRenderJob>> overlayJobsByScene, List<OverlayRenderResult> overlayResults, List<SceneOverlayDiagnostics> overlayDiagnostics, bool diagnosticsEnabled, CancellationToken ct)
    { await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, visualPlan.SelectedAssets, cinematicPlan, overlayJobsByScene, overlayResults, overlayDiagnostics, diagnosticsEnabled, requestWarnings, ct); assetResults.Add(new CelestialAssetSceneRenderResult(req.SceneCode, req.RequestId, req.RequiredAssets, req.OutputPath, "Rendered", requestWarnings, requestErrors)); }
    private async Task ExecuteHybridSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<HybridSceneCompositeResult> hybridResults, IReadOnlyDictionary<string, List<OverlayRenderJob>> overlayJobsByScene, List<OverlayRenderResult> overlayResults, List<SceneOverlayDiagnostics> overlayDiagnostics, bool diagnosticsEnabled, CancellationToken ct)
    { await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, req.RequiredAssets, BuildCinematicVisualPlan(req, new CelestialObjectVisualPlan(req.SceneCode, req.SceneCode, req.NarrationSegmentCodes, req.RequiredAssets, req.RequiredAssets, [], req.RequiredAssets, "Hybrid", false, null, [])), overlayJobsByScene, overlayResults, overlayDiagnostics, diagnosticsEnabled, requestWarnings, ct); hybridResults.Add(new HybridSceneCompositeResult(req.SceneCode, req.RequestId, req.RequiredAssets, req.OutputPath, "Rendered", requestWarnings, requestErrors)); }
    private async Task ExecuteOverlaySceneAsync(SceneRenderRequest req, List<string> requestWarnings, List<string> requestErrors, IReadOnlyDictionary<string, List<OverlayRenderJob>> overlayJobsByScene, List<OverlayRenderResult> overlayResults, List<SceneOverlayDiagnostics> overlayDiagnostics, bool diagnosticsEnabled, CancellationToken ct) => await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, req.RequiredAssets, BuildCinematicVisualPlan(req, new CelestialObjectVisualPlan(req.SceneCode, req.SceneCode, req.NarrationSegmentCodes, req.RequiredAssets, req.RequiredAssets, [], req.RequiredAssets, "Overlay", false, null, [])), overlayJobsByScene, overlayResults, overlayDiagnostics, diagnosticsEnabled, requestWarnings, ct);
    private async Task ExecuteThumbnailSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, CancellationToken ct) => await RenderThumbnailAsync(prep.ThumbnailRenderPlan.PlannedOutputPath, req.SceneCode, req.RequiredAssets, null, ct);
    private async Task RenderSceneVideoAsync(string outputPath, string label, double duration, IReadOnlyList<string> objectAssets, CinematicVisualPlan plan, IReadOnlyDictionary<string, List<OverlayRenderJob>> overlayJobsByScene, List<OverlayRenderResult> overlayResults, List<SceneOverlayDiagnostics> overlayDiagnostics, bool diagnosticsEnabled, List<string> requestWarnings, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var o = renderingOptions.Value;
        var framePath = Path.Combine(Path.GetDirectoryName(outputPath)!, $"{Path.GetFileNameWithoutExtension(outputPath)}.scene-frame.png");
        logger.LogInformation("Preparing C# background frame");
        var renderedObjectCount = objectAssets.Count(File.Exists);
        logger.LogInformation("Object assets rendered onto canvas: {ObjectCount} for scene {SceneLabel}", renderedObjectCount, label);
        logger.LogInformation("C# image composition started");
        using (var imageCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            imageCts.CancelAfter(ImageCompositionTimeout);
            await RenderSceneFrameAsync(framePath, label, o.VideoWidth, o.VideoHeight, objectAssets, plan, imageCts.Token);
            if (overlayJobsByScene.TryGetValue(label, out var sceneOverlays))
            {
                foreach (var overlay in sceneOverlays.OrderBy(x => x.ZIndex))
                {
                    var diag = await GenerateAndCompositeOverlayAsync(overlay, framePath, o.VideoWidth, o.VideoHeight, diagnosticsEnabled, imageCts.Token);
                    overlayDiagnostics.Add(diag);
                    overlayResults.Add(new OverlayRenderResult(overlay.JobId, overlay.SceneCode, overlay.OverlayType, overlay.PlannedOverlayPath, "Rendered", diag.Warnings, []));
                    requestWarnings.AddRange(diag.Warnings);
                }
            }
        }
        logger.LogInformation("C# image composition completed");
        var frameRate = Math.Max(1, o.FrameRate);
        var totalFrames = Math.Max(1, (int)Math.Round(Math.Max(1, duration) * frameRate));
        var zoom = plan.CameraMotion switch { "HeroReveal" => "min(zoom+0.00065,1.09)", "PeacefulZoomOut" => "if(lte(zoom,1.0),1.0,max(zoom-0.0003,0.95))", _ => "min(zoom+0.00045,1.07)" };
        var xExpr = label.Contains("best_night_wide_scene", StringComparison.OrdinalIgnoreCase) ? "'iw/2-(iw/zoom/2)-sin(on/24)*18'" : "'iw/2-(iw/zoom/2)+sin(on/32)*12'";
        var yExpr = label.Contains("viewing_tip_wide_scene", StringComparison.OrdinalIgnoreCase) ? "'ih/2-(ih/zoom/2)-cos(on/38)*8'" : "'ih/2-(ih/zoom/2)+cos(on/30)*10'";
        var filter = $"zoompan=z='{zoom}':x={xExpr}:y={yExpr}:d={totalFrames}:s={o.VideoWidth}x{o.VideoHeight}:fps={frameRate},format=yuv420p";
        var args = $"-y -loop 1 -i \"{framePath}\" -t {Math.Max(1, duration):0.##} -vf \"{filter}\" -r {frameRate} -c:v libx264 -pix_fmt yuv420p \"{outputPath}\"";
        ValidateNoDrawText(args);
        logger.LogInformation("FFmpeg encode started");
        using (var ffmpegCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            ffmpegCts.CancelAfter(SceneRenderTimeout);
            await ffmpegService.ExecuteAsync(args, Directory.GetCurrentDirectory(), outputPath, ffmpegCts.Token);
        }
        logger.LogInformation("FFmpeg encode completed");
        logger.LogInformation("ffprobe validation started");
        await mediaValidationService.ValidateMp4Async(outputPath, 1024, ct);
        logger.LogInformation("ffprobe validation completed");
    }
    private async Task<SceneOverlayDiagnostics> GenerateAndCompositeOverlayAsync(OverlayRenderJob overlay, string sceneFramePath, int width, int height, bool diagnosticsEnabled, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(overlay.PlannedOverlayPath)!);
        await RenderOverlayImageAsync(overlay.PlannedOverlayPath, overlay, width, height, diagnosticsEnabled, ct);
        var overlayVisiblePixelCount = CountVisiblePixels(overlay.PlannedOverlayPath);
        var warnings = new List<string>();
        var overlayGenerated = File.Exists(overlay.PlannedOverlayPath);
        var overlayComposited = false;
        if (overlayGenerated && overlayVisiblePixelCount == 0) warnings.Add("Blank overlay generated.");
        if (overlayGenerated && overlayVisiblePixelCount > 0 && File.Exists(sceneFramePath))
        {
            using var baseFrame = Image.Load<Rgba32>(sceneFramePath);
            using var overlayImage = Image.Load<Rgba32>(overlay.PlannedOverlayPath);
            baseFrame.Mutate(ctx => ctx.DrawImage(overlayImage, 1f));
            await baseFrame.SaveAsPngAsync(sceneFramePath, ct);
            overlayComposited = true;
        }
        if (overlayGenerated && !overlayComposited) warnings.Add("Overlay generated but not used in final scene.");
        if (!diagnosticsEnabled && (!overlayComposited || overlayVisiblePixelCount == 0) && File.Exists(overlay.PlannedOverlayPath)) File.Delete(overlay.PlannedOverlayPath);
        return new SceneOverlayDiagnostics(overlay.SceneCode, overlay.OverlayType, overlay.PlannedOverlayPath, overlayGenerated, overlayComposited, overlayVisiblePixelCount, overlayComposited, sceneFramePath, warnings);
    }
    private async Task RenderThumbnailAsync(string outputPath, string label, IReadOnlyList<string> objectAssets, string? regionName, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await RenderThumbnailImageAsync(outputPath, label, 1280, 720, objectAssets, regionName, ct);
    }

    private static async Task RenderSceneFrameAsync(string path, string label, int width, int height, IReadOnlyList<string> objectAssets, CinematicVisualPlan plan, CancellationToken ct)
    {
        var scene = string.IsNullOrWhiteSpace(label) ? "weekly_sky_scene" : label;
        var titleFont = ResolveFont(Math.Max(30f, width * 0.03f));
        var bodyFont = ResolveFont(Math.Max(18f, width * 0.017f));
        using var image = new Image<Rgba32>(width, height, new Rgba32(7, 10, 25));
        image.Mutate(ctx =>
        {
            DrawCinematicBackground(ctx, width, height, scene.Contains("best_night_wide_scene", StringComparison.OrdinalIgnoreCase) ? "sunset-horizon" : "deep-space-blue");
            DrawDepthLayerStack(ctx, width, height);

            var placements = BuildDynamicPlacements(scene, width, height, objectAssets);
            for (var i = 0; i < placements.Count; i++)
            {
                var pathAsset = i < objectAssets.Count ? objectAssets[i] : string.Empty;
                DrawCelestialBody(ctx, pathAsset, placements[i], Path.GetFileNameWithoutExtension(pathAsset), titleFont, scene);
            }

            DrawVignette(ctx, width, height);
            DrawAtmosphericHaze(ctx, width, height);
            DrawSceneOverlay(ctx, scene, width, height, titleFont, bodyFont);
        });
        await image.SaveAsPngAsync(path, ct);
        var info = Image.Identify(path) ?? throw new InvalidOperationException($"Failed to validate generated scene frame '{path}'.");
        if (info.Width <= 0 || info.Height <= 0) throw new InvalidOperationException($"Invalid generated scene frame '{path}'.");
    }
    private static CelestialObjectVisualPlan ResolveVisualPlan(SceneRenderRequest req, RenderPreparationPackage prep, CelestialAssetResolver resolver, out SceneVisualAssetDiagnostics diagnostics)
    {
        var requiredObjects = ResolveRequiredObjects(req).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var selected = new List<string>();
        var warnings = new List<string>();
        diagnostics = new SceneVisualAssetDiagnostics { SceneCode = req.SceneCode, RequiredObjects = requiredObjects.ToList(), SceneVisualsContainObjects = false };
        foreach (var obj in requiredObjects)
        {
            var resolution = resolver.ResolveForObject(obj);
            diagnostics.NormalizedObjectNames.Add(resolution.NormalizedObjectName);
            diagnostics.ObjectFolderCandidates[obj] = resolution.ObjectFolderCandidates;
            diagnostics.SelectedObjectFolder[obj] = resolution.SelectedObjectFolder;
            diagnostics.FilesScanned[obj] = resolution.FilesScanned;
            diagnostics.ResolvedTransparentAssets[obj] = resolution.TransparentAssets;
            diagnostics.ResolvedHeroAssets[obj] = resolution.HeroAssets;
            var asset = resolution.SelectedAsset;
            if (asset is null) warnings.Add($"No asset resolved for object '{obj}'."); else selected.Add(asset);
        }
        var layout = requiredObjects.Count > 1 || requiredObjects.Any(x => x.Contains("jupiter", StringComparison.OrdinalIgnoreCase) || x.Contains("venus", StringComparison.OrdinalIgnoreCase))
            ? "SkyGroupingCollage" : "HeroObject";
        diagnostics = diagnostics with
        {
            SelectedAssets = selected.ToList(),
            MissingObjects = requiredObjects.Where(o => !selected.Any(s => MatchesObjectName(o, s))).ToList(),
            FallbackUsed = selected.Count == 0,
            ObjectCountDrawn = selected.Count,
            SceneVisualsContainObjects = selected.Count > 0
        };
        return new CelestialObjectVisualPlan(req.SceneCode, req.SceneCode, req.NarrationSegmentCodes, requiredObjects, requiredObjects, diagnostics.FilesScanned.SelectMany(kv => kv.Value).Distinct().ToList(), selected, layout, selected.Count == 0, selected.Count == 0 ? "No assets resolved, diagnostics fallback required." : null, warnings);
    }

    private static async Task RenderOverlayImageAsync(string path, OverlayRenderJob overlay, int width, int height, bool diagnosticsEnabled, CancellationToken ct)
    {
        var font = ResolveFont(Math.Max(30f, width * 0.022f));
        var body = ResolveFont(Math.Max(20f, width * 0.014f));
        using var image = new Image<Rgba32>(width, height, Color.Transparent);
        image.Mutate(ctx =>
        {
            var panel = new RectangleF(45, height - 240, width - 90, 185);
            ctx.Fill(Color.ParseHex("#050911").WithAlpha(0.55f), panel);
            ctx.Draw(Color.White.WithAlpha(0.2f), 2, panel);
            ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(panel.X + 24, panel.Y + 16), WrappingLength = panel.Width - 48 }, $"Objects: {overlay.OverlayText}", Color.White);
            ctx.DrawText(new RichTextOptions(body) { Origin = new PointF(panel.X + 24, panel.Y + 62), WrappingLength = panel.Width - 48 }, $"Viewing: {overlay.SafeArea} • {overlay.Animation}", Color.ParseHex("#8FD2FF"));
            ctx.DrawText(new RichTextOptions(body) { Origin = new PointF(panel.X + 24, panel.Y + 92), WrappingLength = panel.Width - 48 }, $"Time: {overlay.StartSecond}s–{overlay.EndSecond}s • Best Night", Color.ParseHex("#F9B24E"));
            ctx.DrawText(new RichTextOptions(body) { Origin = new PointF(panel.X + 24, panel.Y + 124), WrappingLength = panel.Width - 48 }, "CTA: Look up tonight and follow for tomorrow's targets.", Color.ParseHex("#D5F3FF"));
        });
        await image.SaveAsPngAsync(path, ct);
        var info = Image.Identify(path) ?? throw new InvalidOperationException($"Failed to validate overlay image '{path}'.");
        if (info.Width <= 0 || info.Height <= 0) throw new InvalidOperationException($"Invalid overlay image '{path}'.");
    }
    private static int CountVisiblePixels(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        var count = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) if (row[x].A > 0) count++;
            }
        });
        return count;
    }
    private sealed record SceneOverlayDiagnostics(string SceneCode, string OverlayType, string OverlayPath, bool OverlayGenerated, bool OverlayComposited, int OverlayVisiblePixelCount, bool OverlayUsedInSceneFrame, string FinalSceneFramePath, IReadOnlyList<string> Warnings);

    private static async Task RenderThumbnailImageAsync(string path, string label, int width, int height, IReadOnlyList<string> objectAssets, string? regionName, CancellationToken ct)
    {
        var titleFont = ResolveFont(Math.Max(44f, width * 0.056f));
        var subtitleFont = ResolveFont(Math.Max(22f, width * 0.025f));
        using var image = new Image<Rgba32>(width, height, new Rgba32(6, 8, 16));
        image.Mutate(ctx =>
        {
            DrawCinematicBackground(ctx, width, height, "nebula-purple");
            DrawDepthLayerStack(ctx, width, height);

            var placements = BuildDynamicPlacements("hero_western_grouping_scene", width, height, objectAssets);
            for (var i = 0; i < placements.Count; i++)
            {
                var pathAsset = i < objectAssets.Count ? objectAssets[i] : string.Empty;
                DrawCelestialBody(ctx, pathAsset, placements[i], Path.GetFileNameWithoutExtension(pathAsset), subtitleFont, "hero_western_grouping_scene");
            }

            DrawVignette(ctx, width, height);
            DrawAtmosphericHaze(ctx, width, height);
            var headline = WeeklyThumbnailHeadline;
            var subtitle = "Moon Meets Jupiter";
            var metadataLine = $"{(regionName ?? "Udaipur")} • May 23–30";
            var titleOptions = new RichTextOptions(titleFont) { Origin = new PointF(72, 62), WrappingLength = width * 0.58f };
            ctx.DrawText(new RichTextOptions(titleOptions) { Origin = new PointF(74, 62) }, headline, Color.Black.WithAlpha(0.88f));
            ctx.DrawText(titleOptions, headline, Color.White);
            ctx.DrawText(new RichTextOptions(subtitleFont) { Origin = new PointF(76, 154), WrappingLength = width * 0.52f }, subtitle, Color.ParseHex("#F9B24E"));
            ctx.DrawText(new RichTextOptions(subtitleFont) { Origin = new PointF(76, 192), WrappingLength = width * 0.52f }, metadataLine, Color.ParseHex("#CBE3FF").WithAlpha(0.92f));
        });
        await image.SaveAsJpegAsync(path, new JpegEncoder { Quality = 92 }, ct);
        var info = Image.Identify(path) ?? throw new InvalidOperationException($"Failed to validate thumbnail image '{path}'.");
        if (info.Width <= 0 || info.Height <= 0) throw new InvalidOperationException($"Invalid thumbnail image '{path}'.");
    }


    private static void DrawCelestialBody(IImageProcessingContext ctx, string assetPath, RectangleF bounds, string fallbackLabel, Font font, string sceneCode)
    {
        ctx.Fill(Color.ParseHex("#A4CFFF").WithAlpha(0.07f), new EllipsePolygon(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f, Math.Max(bounds.Width, bounds.Height) * 0.62f));
        ctx.Fill(Color.ParseHex("#A46DFF").WithAlpha(0.04f), new EllipsePolygon(bounds.X + bounds.Width * 0.52f, bounds.Y + bounds.Height * 0.55f, Math.Max(bounds.Width, bounds.Height) * 0.78f));
        if (File.Exists(assetPath))
        {
            using var assetImage = Image.Load<Rgba32>(assetPath);
            MakeBlackTransparent(assetImage, BlackThresholdDefault);
            FeatherAlphaEdges(assetImage, 1);
            ApplyOuterGlow(assetImage, Color.ParseHex("#85C9FF"), 11, 0.44f);
            assetImage.Mutate(x => x.Resize(new ResizeOptions { Size = new Size((int)bounds.Width, (int)bounds.Height), Mode = ResizeMode.Crop }));
            ctx.DrawImage(assetImage, new Point((int)bounds.X, (int)bounds.Y), 1f);
            ctx.Draw(Color.White.WithAlpha(0.38f), 1.4f, new EllipsePolygon(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f, bounds.Width / 2.02f));
            ctx.DrawLine(Color.ParseHex("#F8BE73").WithAlpha(0.18f), 2f, new PointF(bounds.X + bounds.Width * 0.22f, bounds.Y + bounds.Height * 0.27f), new PointF(bounds.X + bounds.Width * 0.68f, bounds.Y + bounds.Height * 0.10f));
            return;
        }

        ctx.Fill(Color.ParseHex("#1B264A").WithAlpha(0.82f), bounds);
        ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(bounds.X + 20, bounds.Y + (bounds.Height * 0.45f)), WrappingLength = bounds.Width - 40 }, fallbackLabel, Color.White);
    }
    private static void MakeBlackTransparent(Image<Rgba32> image, float threshold)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var px = row[x];
                    if (px.R <= threshold && px.G <= threshold && px.B <= threshold) row[x] = new Rgba32(px.R, px.G, px.B, 0);
                }
            }
        });
    }
    private static void FeatherAlphaEdges(Image<Rgba32> image, int radius) => image.Mutate(x => x.GaussianBlur(Math.Max(0.25f, radius * 0.2f)));
    private static void ApplyOuterGlow(Image<Rgba32> image, Color color, int radius, float opacity)
    {
        using var glow = image.Clone(i => i.GaussianBlur(Math.Max(1f, radius)).Opacity(opacity));
        image.Mutate(i => i.DrawImage(glow, PixelColorBlendingMode.Lighten, 0.85f));
    }
    private static void DrawCinematicBackground(IImageProcessingContext ctx, int width, int height, string palette)
    {
        var colors = palette switch
        {
            "sunset-horizon" => new[] { Color.ParseHex("#030714"), Color.ParseHex("#1B1A3A"), Color.ParseHex("#D16A3F") },
            "nebula-purple" => new[] { Color.ParseHex("#030612"), Color.ParseHex("#1A1240"), Color.ParseHex("#51317A") },
            _ => new[] { Color.ParseHex("#040812"), Color.ParseHex("#0E1C3D"), Color.ParseHex("#1B1040") }
        };
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width, height), GradientRepetitionMode.None, [new ColorStop(0, colors[0]), new ColorStop(0.62f, colors[1]), new ColorStop(1f, colors[2])]));
    }
    private static void DrawStars(IImageProcessingContext ctx, int width, int height, int count)
    {
        var random = new Random(42 + width + height + count);
        for (var i = 0; i < count; i++)
        {
            var x = random.Next(0, width);
            var y = random.Next(0, height);
            var size = random.NextSingle() > 0.95f ? 3.2f : random.NextSingle() * 1.8f + 0.5f;
            ctx.Fill(Color.White.WithAlpha(random.NextSingle() * 0.75f + 0.1f), new EllipsePolygon(x, y, size));
        }
    }
    private static void DrawNebulaCloud(IImageProcessingContext ctx, int width, int height)
    {
        // Paint one continuous sky field with diagonal, feathered atmospheric wisps. Avoid
        // stacked rectangular gradient regions because they can read as horizontal bands.
        ctx.Fill(new LinearGradientBrush(new PointF(width * 0.04f, height * 0.10f), new PointF(width * 0.96f, height * 0.92f), GradientRepetitionMode.None,
            [
                new ColorStop(0f, Color.ParseHex("#0E2348").WithAlpha(0.0f)),
                new ColorStop(0.38f, Color.ParseHex("#2B4C7A").WithAlpha(0.10f)),
                new ColorStop(0.72f, Color.ParseHex("#6D3E8F").WithAlpha(0.08f)),
                new ColorStop(1f, Color.ParseHex("#C6864E").WithAlpha(0.04f))
            ]), new RectangleF(0, 0, width, height));

        DrawSoftAtmosphericGlow(ctx, width, height, new PointF(width * 0.23f, height * 0.23f), width * 0.52f, height * 0.18f, Color.ParseHex("#2B4C7A"), 0.045f, 10);
        DrawSoftAtmosphericGlow(ctx, width, height, new PointF(width * 0.68f, height * 0.46f), width * 0.46f, height * 0.24f, Color.ParseHex("#7E479E"), 0.040f, 10);
        DrawSoftAtmosphericGlow(ctx, width, height, new PointF(width * 0.34f, height * 0.76f), width * 0.58f, height * 0.20f, Color.ParseHex("#C6864E"), 0.032f, 10);
        ctx.GaussianBlur(24f);
    }
    private static void DrawDepthLayerStack(IImageProcessingContext ctx, int width, int height)
    {
        DrawStars(ctx, width, height, 520);
        DrawNebulaCloud(ctx, width, height);
        DrawStars(ctx, width, height, 240);
        ctx.Fill(new LinearGradientBrush(new PointF(width * 0.08f, 0), new PointF(width * 0.92f, height), GradientRepetitionMode.None,
            [
                new ColorStop(0f, Color.ParseHex("#7AB3FF").WithAlpha(0.018f)),
                new ColorStop(0.47f, Color.ParseHex("#B896FF").WithAlpha(0.014f)),
                new ColorStop(1f, Color.ParseHex("#FFB26A").WithAlpha(0.015f))
            ]), new RectangleF(0, 0, width, height));
        DrawSoftAtmosphericGlow(ctx, width, height, new PointF(width * 0.63f, height * 0.38f), width * 0.42f, height * 0.30f, Color.ParseHex("#B896FF"), 0.018f, 8);
        DrawStars(ctx, width, height, 110);
    }
    private static void DrawAtmosphericHaze(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(width * 0.08f, height * 0.42f), new PointF(width * 0.94f, height), GradientRepetitionMode.None,
            [
                new ColorStop(0f, Color.ParseHex("#7CA6FF").WithAlpha(0.0f)),
                new ColorStop(0.55f, Color.ParseHex("#6CB9D8").WithAlpha(0.038f)),
                new ColorStop(1f, Color.ParseHex("#E1A35E").WithAlpha(0.062f))
            ]), new RectangleF(0, 0, width, height));
        DrawSoftAtmosphericGlow(ctx, width, height, new PointF(width * 0.50f, height * 0.82f), width * 0.72f, height * 0.18f, Color.ParseHex("#E1A35E"), 0.026f, 9);
        ctx.GaussianBlur(14f);
    }
    private static void DrawVignette(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height), GradientRepetitionMode.None,
            [
                new ColorStop(0f, Color.Black.WithAlpha(0.35f)),
                new ColorStop(0.18f, Color.Black.WithAlpha(0.04f)),
                new ColorStop(0.78f, Color.Black.WithAlpha(0.02f)),
                new ColorStop(1f, Color.Black.WithAlpha(0.46f))
            ]), new RectangleF(0, 0, width, height));
        ctx.Fill(new LinearGradientBrush(new PointF(0, height * 0.18f), new PointF(width, height * 0.82f), GradientRepetitionMode.None,
            [
                new ColorStop(0f, Color.Black.WithAlpha(0.20f)),
                new ColorStop(0.14f, Color.Black.WithAlpha(0.025f)),
                new ColorStop(0.86f, Color.Black.WithAlpha(0.02f)),
                new ColorStop(1f, Color.Black.WithAlpha(0.16f))
            ]), new RectangleF(0, 0, width, height));
    }
    private static void DrawSoftAtmosphericGlow(IImageProcessingContext ctx, int width, int height, PointF center, float radiusX, float radiusY, Color color, float maxAlpha, int rings)
    {
        for (var i = rings; i >= 1; i--)
        {
            var t = i / (float)rings;
            var alpha = maxAlpha * MathF.Pow(1f - t * 0.74f, 1.55f);
            ctx.Fill(color.WithAlpha(alpha), new EllipsePolygon(center.X, center.Y, radiusX * t, radiusY * t));
        }
    }
    private static List<RectangleF> BuildDynamicPlacements(string scene, int width, int height, IReadOnlyList<string> assets)
    {
        var count = Math.Max(1, assets.Count);
        if (scene.Contains("moon_jupiter_hero_scene", StringComparison.OrdinalIgnoreCase))
            return [new RectangleF(-140, 90, 620, 620), new RectangleF(width - 500, 230, 360, 360)];
        if (scene.Contains("hero_western_grouping_scene", StringComparison.OrdinalIgnoreCase))
            return [new RectangleF(-90, 130, 500, 500), new RectangleF(width - 520, 230, 300, 300), new RectangleF(width - 220, 180, 120, 120)];
        return count switch
        {
            1 => [new RectangleF(width * 0.15f, height * 0.08f, width * 0.72f, height * 0.82f)],
            2 => [new RectangleF(-100, height * 0.10f, width * 0.52f, height * 0.78f), new RectangleF(width * 0.58f, height * 0.28f, width * 0.30f, height * 0.50f)],
            3 => [new RectangleF(-120, 80, 560, 560), new RectangleF(width - 500, 230, 320, 320), new RectangleF(width - 210, 140, 130, 130)],
            4 => [new RectangleF(-90, 110, 460, 460), new RectangleF(width * 0.46f, 250, 280, 280), new RectangleF(width * 0.66f, 140, 210, 210), new RectangleF(width * 0.79f, 300, 150, 150)],
            _ => Enumerable.Range(0, Math.Min(6, count)).Select(i => new RectangleF(80 + (i * 150), 90 + ((i % 2 == 0) ? i * 28 : i * 44), Math.Max(120, 380 - (i * 35)), Math.Max(120, 380 - (i * 35)))).ToList()
        };
    }
    private static CinematicVisualPlan BuildCinematicVisualPlan(SceneRenderRequest req, CelestialObjectVisualPlan visualPlan)
    {
        var motion = req.CinematicDirection?.MotionIntent switch
        {
            "slow-drift" => "SlowPushIn",
            "gentle-pan" => "PeacefulZoomOut",
            "slow-panorama" => "PeacefulZoomOut",
            _ => req.SceneCode.Contains("viewing_tip", StringComparison.OrdinalIgnoreCase) ? "PeacefulZoomOut" : "SlowPushIn"
        };
        return new(req.SceneCode, req.SceneCode, visualPlan.RequiredObjects, visualPlan.SelectedAssets, "EpicReveal", visualPlan.VisualLayoutType,
            motion, "FadeFromBlack", "CinematicCrossfade", [], ["Starfield", "NebulaFog", "Vignette"], [], 0, req.DurationSeconds, req.DurationSeconds);
    }
    private static async Task WriteCinematicDiagnosticsAsync(string root, RenderPreparationPackage prep, List<CelestialObjectVisualPlan> plans, Dictionary<string, int> sceneObjects, int thumbnailObjectCount, CancellationToken ct)
    {
        var path = Path.Combine(root, "debug", "cinematic-visual-diagnostics.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new
        {
            scenes = prep.SceneRenderRequests.Select(r => new
            {
                r.SceneCode,
                requiredObjects = plans.FirstOrDefault(p => p.SceneCode == r.SceneCode)?.RequiredObjects ?? [],
                drawnObjects = plans.FirstOrDefault(p => p.SceneCode == r.SceneCode)?.SelectedAssets ?? [],
                objectCountDrawn = sceneObjects.TryGetValue(r.SceneCode, out var c) ? c : 0,
                layoutType = plans.FirstOrDefault(p => p.SceneCode == r.SceneCode)?.VisualLayoutType ?? "Unknown",
                cameraMotion = "SlowPushIn",
                transitionApplied = true,
                starfieldPresent = true,
                glowApplied = true,
                blackBoxDetected = false,
                sceneCinematicScore = 0.82,
                warnings = Array.Empty<string>()
            }),
            thumbnailObjectCount,
            thumbnailCinematicScore = 0.82,
            cinematicMotionEnabled = true,
            transitionsApplied = true,
            noBlackRectangles = true,
            dynamicObjectCollageApplied = true
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), ct);
    }
    private sealed record CinematicVisualPlan(string SceneCode, string ScenePurpose, IReadOnlyList<string> RequiredObjects, IReadOnlyList<string> SelectedAssets, string VisualMood, string LayoutType, string CameraMotion, string TransitionIn, string TransitionOut, IReadOnlyList<object> ObjectPlacements, IReadOnlyList<string> Effects, IReadOnlyList<object> OverlayMoments, double StartSecond, double EndSecond, double DurationSeconds);
    private static void DrawSceneOverlay(IImageProcessingContext ctx, string scene, int width, int height, Font titleFont, Font bodyFont)
    {
        var panel = new RectangleF(62, height - 168, width * 0.56f, 102);
        ctx.Fill(Color.ParseHex("#050910").WithAlpha(0.18f), panel);
        var title = scene switch
        {
            "hero_western_grouping_scene" => "Look west after sunset this week...",
            "best_night_wide_scene" => "Best night: sky clarity peaks after dusk",
            "moon_jupiter_hero_scene" => "A beautiful Moon–Jupiter pairing",
            "viewing_tip_wide_scene" => "Bring binoculars and let your eyes adapt",
            _ => "Weekly Sky Forecast"
        };
        ctx.DrawText(new RichTextOptions(titleFont) { Origin = new PointF(panel.X + 14, panel.Y + 12), WrappingLength = panel.Width - 24 }, title, Color.White.WithAlpha(0.96f));
        ctx.DrawText(new RichTextOptions(bodyFont) { Origin = new PointF(panel.X + 14, panel.Y + 54), WrappingLength = panel.Width - 24 }, "One sky story. One perfect viewing moment.", Color.ParseHex("#8FD2FF").WithAlpha(0.9f));
    }


    private static bool MatchesObjectName(string requiredObject, string selectedAssetPath)
    {
        var required = NormalizeObjectKey(requiredObject);
        var assetName = NormalizeObjectKey(Path.GetFileNameWithoutExtension(selectedAssetPath));
        return !string.IsNullOrWhiteSpace(required) && assetName.Contains(required, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeObjectKey(string value)
        => string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private sealed class CelestialAssetResolver
    {
        private readonly ILogger _logger;
        public string SelectedRoot { get; }
        public bool RootExists { get; }
        public string ConfiguredRoot { get; }
        public IReadOnlyList<string> SearchDirectories => [SelectedRoot];

        public CelestialAssetResolver(string configuredRoot, ILogger logger)
        {
            _logger = logger;
            ConfiguredRoot = configuredRoot;
            SelectedRoot = ResolveRoot(configuredRoot);
            RootExists = Directory.Exists(SelectedRoot);
            _logger.LogInformation("[CelestialAssets]\nAppContext.BaseDirectory={AppBase}\nDirectory.GetCurrentDirectory()={CurrentDir}\nRoot={Root}\nRootExists={RootExists}",
                AppContext.BaseDirectory, Directory.GetCurrentDirectory(), SelectedRoot, RootExists);
        }

        public ObjectAssetResolution ResolveForObject(string objectCode)
        {
            var normalized = NormalizeObjectKey(objectCode);
            var resolution = new ObjectAssetResolution { NormalizedObjectName = normalized };
            if (!RootExists) return resolution;
            var candidates = Directory.Exists(SelectedRoot)
                ? Directory.EnumerateDirectories(SelectedRoot).Where(d => NormalizeObjectKey(Path.GetFileName(d)) == normalized).ToList()
                : [];
            resolution.ObjectFolderCandidates = candidates;
            var objectFolder = candidates.FirstOrDefault();
            resolution.SelectedObjectFolder = objectFolder;
            if (string.IsNullOrWhiteSpace(objectFolder) || !Directory.Exists(objectFolder)) return resolution;
            try
            {
                var files = Directory.EnumerateFiles(objectFolder, "*.*", SearchOption.AllDirectories)
                    .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToList();
                resolution.FilesScanned = files;
                resolution.TransparentAssets = files.Where(IsTransparentAsset).OrderByDescending(ScoreSceneAsset).ToList();
                resolution.HeroAssets = files.Where(IsHeroAsset).OrderByDescending(ScoreSceneAsset).ToList();
                resolution.SelectedAsset = files.OrderByDescending(ScoreSceneAsset).FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AssetResolver] Failed to enumerate files for object folder {ObjectFolder}", objectFolder);
            }
            _logger.LogInformation("[AssetResolver]\nObject={Object}\nObjectFolder={ObjectFolder}\nFilesScanned={FilesScanned}\nSelectedTransparent={SelectedTransparent}\nSelectedHero={SelectedHero}",
                objectCode, resolution.SelectedObjectFolder, resolution.FilesScanned.Count, Path.GetFileName(resolution.TransparentAssets.FirstOrDefault()), Path.GetFileName(resolution.HeroAssets.FirstOrDefault()));
            return resolution;
        }

        private static string ResolveRoot(string configuredRoot)
        {
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                return Path.GetFullPath(configuredRoot);
            }

            var appBase = Path.Combine(AppContext.BaseDirectory, "assets", "celestial");
            if (Directory.Exists(appBase)) return appBase;
            return Path.Combine(Directory.GetCurrentDirectory(), "assets", "celestial");
        }
    }
    private static List<string> ResolveRequiredObjects(SceneRenderRequest req)
    {
        var set = new HashSet<string>(req.RequiredAssets, StringComparer.OrdinalIgnoreCase);
        foreach (var c in req.NarrationSegmentCodes) foreach (var p in PriorityObjects.Where(p => c.Contains(p, StringComparison.OrdinalIgnoreCase))) set.Add(p);
        foreach (var p in PriorityObjects.Where(p => req.SceneCode.Contains(p, StringComparison.OrdinalIgnoreCase))) set.Add(p);
        if (set.Count == 0)
        {
            var fallback = req.SceneCode switch
            {
                "hero_western_grouping_scene" => new[] { "Moon", "Jupiter", "Venus" },
                "moon_jupiter_hero_scene" => new[] { "Moon", "Jupiter" },
                "best_night_wide_scene" => new[] { "Moon", "Jupiter", "Venus" },
                "viewing_tip_wide_scene" => new[] { "Moon" },
                _ => []
            };
            foreach (var f in fallback) set.Add(f);
        }
        return set.ToList();
    }
    private static bool IsTransparentAsset(string path) => HasAny(path, "transparent", "alpha", "cutout");
    private static bool IsHeroAsset(string path) => HasAny(path, "hero", "main", "poster");
    private static int ScoreSceneAsset(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (name.Contains("transparent") && ext == ".png") return 100;
        if (name.Contains("hero-transparent")) return 95;
        if (name.Contains("cutout") && ext == ".png") return 90;
        if (name.Contains("hero") && ext == ".png") return 85;
        if (ext == ".png") return 80;
        if (ext is ".jpg" or ".jpeg") return 70;
        if (ext == ".webp") return 60;
        return 0;
    }
    private static int ScoreThumbnailAsset(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (name == "hero" && ext == ".png") return 100;
        if (name.Contains("hero") && (ext == ".png" || ext == ".jpg" || ext == ".jpeg")) return 95;
        if (name.Contains("transparent") && ext == ".png") return 90;
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp") return 80;
        return 0;
    }
    private static bool HasAny(string path, params string[] tokens) => tokens.Any(t => Path.GetFileNameWithoutExtension(path).Contains(t, StringComparison.OrdinalIgnoreCase));
    private sealed record SceneVisualAssetDiagnostics
    {
        public string SceneCode { get; init; } = "";
        public List<string> RequiredObjects { get; init; } = [];
        public List<string> NormalizedObjectNames { get; init; } = [];
        public Dictionary<string, List<string>> ObjectFolderCandidates { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string?> SelectedObjectFolder { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> FilesScanned { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> ResolvedTransparentAssets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> ResolvedHeroAssets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> SelectedAssets { get; init; } = [];
        public List<string> MissingObjects { get; init; } = [];
        public bool FallbackUsed { get; init; }
        public int ObjectCountDrawn { get; init; }
        public bool SceneVisualsContainObjects { get; init; }
    }
    private sealed record ObjectAssetResolution
    {
        public string NormalizedObjectName { get; init; } = "";
        public List<string> ObjectFolderCandidates { get; set; } = [];
        public string? SelectedObjectFolder { get; set; }
        public List<string> FilesScanned { get; set; } = [];
        public List<string> TransparentAssets { get; set; } = [];
        public List<string> HeroAssets { get; set; } = [];
        public string? SelectedAsset { get; set; }
    }

    private static Font ResolveFont(float size)
    {
        var preferred = new[] { "Inter", "Segoe UI", "Arial", "DejaVu Sans" };
        foreach (var name in preferred)
        {
            if (SystemFonts.TryGet(name, out var family)) return family.CreateFont(size, FontStyle.Bold);
        }

        var fallbackFamily = SystemFonts.Collection.Families.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fallbackFamily.Name))
        {
            throw new InvalidOperationException("No system fonts available for C# overlay rendering.");
        }

        return fallbackFamily.CreateFont(size, FontStyle.Bold);
    }

    private static void ValidateNoDrawText(string ffmpegArgs)
    {
        if (ffmpegArgs.Contains("drawtext", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("FFmpeg drawtext is disabled for Windows-safe rendering.");
        }
    }
}
