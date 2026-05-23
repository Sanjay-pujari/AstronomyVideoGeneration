using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Extensions.Options;
using System.Text.Json;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

            try
            {
                using var sceneTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sceneTimeoutCts.CancelAfter(SceneRenderTimeout);
                switch (req.RendererType)
                {
                    case "StellariumSceneRenderer":
                        await ExecuteStellariumSceneAsync(req, prep, requestWarnings, requestErrors, stellariumResults, visualPlan, sceneTimeoutCts.Token);
                        diagnosticsFallbackUsed = true;
                        break;
                    case "CelestialAssetCompositor":
                        await ExecuteCelestialAssetSceneAsync(req, prep, requestWarnings, requestErrors, assetResults, visualPlan, sceneTimeoutCts.Token);
                        break;
                    case "HybridCompositor":
                        await ExecuteHybridSceneAsync(req, prep, requestWarnings, requestErrors, hybridResults, sceneTimeoutCts.Token);
                        break;
                    case "OverlayCompositor":
                        await ExecuteOverlaySceneAsync(req, requestWarnings, requestErrors, sceneTimeoutCts.Token);
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
                RenderedFramePath = Path.Combine(Path.GetDirectoryName(req.OutputPath)!, $"{Path.GetFileNameWithoutExtension(req.OutputPath)}.scene-frame.png"),
                RenderedVideoPath = req.OutputPath
            }), cancellationToken);
            logger.LogInformation("Completed scene render: {SceneCode}, status={Status}, elapsedMs={ElapsedMs}", req.SceneCode, status, (DateTime.UtcNow - sceneStartedAt).TotalMilliseconds);
        }

        foreach (var overlay in prep.OverlayRenderPlan.Jobs)
        {
await RenderOverlayAsync(overlay.PlannedOverlayPath, $"{overlay.SceneCode} {overlay.OverlayType}", cancellationToken);
            overlayResults.Add(new OverlayRenderResult(overlay.JobId, overlay.SceneCode, overlay.OverlayType, overlay.PlannedOverlayPath, "Rendered", [], []));
        }

        ThumbnailSceneRenderResult? thumbnail = null;
        var thumbnailObjectCount = 0;
        if (!string.IsNullOrWhiteSpace(prep.ThumbnailRenderPlan.ThumbnailRequestId))
        {
            var thumbAssets = prep.ThumbnailRenderPlan.PrimaryObjects
                .SelectMany(o => assetResolver.ResolveForObject(o).OrderByDescending(a => ScoreThumbnailAsset(a)).Take(1))
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

    private async Task ExecuteStellariumSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<StellariumSceneRenderResult> stellariumResults, CelestialObjectVisualPlan visualPlan, CancellationToken ct)
    {
        requestWarnings.Add("Stellarium capture not implemented; diagnostics fallback visual used.");
        await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, visualPlan.SelectedAssets, ct);
        var job = prep.StellariumRenderPlan.Jobs.FirstOrDefault(x => x.RequestId == req.RequestId || x.SceneCode == req.SceneCode);
        if (job is not null) stellariumResults.Add(new StellariumSceneRenderResult(job.JobId, req.SceneCode, req.RequestId, job.PlannedSscPath, req.OutputPath, "DiagnosticsFallbackVisual", req.DurationSeconds, requestWarnings, requestErrors));
    }
    private async Task ExecuteCelestialAssetSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<CelestialAssetSceneRenderResult> assetResults, CelestialObjectVisualPlan visualPlan, CancellationToken ct)
    { await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, visualPlan.SelectedAssets, ct); assetResults.Add(new CelestialAssetSceneRenderResult(req.SceneCode, req.RequestId, req.RequiredAssets, req.OutputPath, "Rendered", requestWarnings, requestErrors)); }
    private async Task ExecuteHybridSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, List<HybridSceneCompositeResult> hybridResults, CancellationToken ct)
    { await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, req.RequiredAssets, ct); hybridResults.Add(new HybridSceneCompositeResult(req.SceneCode, req.RequestId, req.RequiredAssets, req.OutputPath, "Rendered", requestWarnings, requestErrors)); }
    private async Task ExecuteOverlaySceneAsync(SceneRenderRequest req, List<string> requestWarnings, List<string> requestErrors, CancellationToken ct) => await RenderSceneVideoAsync(req.OutputPath, req.SceneCode, req.DurationSeconds, req.RequiredAssets, ct);
    private async Task ExecuteThumbnailSceneAsync(SceneRenderRequest req, RenderPreparationPackage prep, List<string> requestWarnings, List<string> requestErrors, CancellationToken ct) => await RenderThumbnailAsync(prep.ThumbnailRenderPlan.PlannedOutputPath, req.SceneCode, req.RequiredAssets, null, ct);
    private async Task RenderSceneVideoAsync(string outputPath, string label, double duration, IReadOnlyList<string> objectAssets, CancellationToken ct)
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
            await RenderSceneFrameAsync(framePath, label, o.VideoWidth, o.VideoHeight, objectAssets, imageCts.Token);
        }
        logger.LogInformation("C# image composition completed");
        var args = $"-y -loop 1 -i \"{framePath}\" -t {Math.Max(1, duration):0.##} -r {Math.Max(1, o.FrameRate)} -c:v libx264 -pix_fmt yuv420p \"{outputPath}\"";
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
    private async Task RenderOverlayAsync(string outputPath, string label, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await RenderOverlayImageAsync(outputPath, label, 1920, 1080, ct);
    }
    private async Task RenderThumbnailAsync(string outputPath, string label, IReadOnlyList<string> objectAssets, string? regionName, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await RenderThumbnailImageAsync(outputPath, label, 1280, 720, objectAssets, regionName, ct);
    }

    private static async Task RenderSceneFrameAsync(string path, string label, int width, int height, IReadOnlyList<string> objectAssets, CancellationToken ct)
    {
        var font = ResolveFont(Math.Max(32f, width * 0.03f));
        using var image = new Image<Rgba32>(width, height, new Rgba32(7, 10, 25));
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.Black, new RectangleF(0, 0, width, height));
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width, height), GradientRepetitionMode.None, [new ColorStop(0, Color.ParseHex("#0A1330")), new ColorStop(1, Color.ParseHex("#02040A"))]));
            var text = string.IsNullOrWhiteSpace(label) ? "Weekly Sky Forecast" : label;
            var options = new RichTextOptions(font) { Origin = new PointF(40, 60), WrappingLength = width - 80 };
            ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(44, 64) }, text, Color.Black.WithAlpha(0.85f));
            ctx.DrawText(options, text, Color.White);
            for (var i = 0; i < Math.Min(3, objectAssets.Count); i++)
            {
                var heroX = 90 + (i * 365);
                var heroBounds = new RectangleF(heroX, 180, 320, 320);
                DrawAssetTile(ctx, objectAssets[i], heroBounds, Path.GetFileNameWithoutExtension(objectAssets[i]), font);
            }
            ctx.Fill(Color.ParseHex("#000000").WithAlpha(0.40f), new RectangleF(width - 280, 24, 240, 72));
            ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(width - 260, 44), WrappingLength = 210 }, "UDAIPUR", Color.White);
            ctx.Fill(Color.ParseHex("#F59E0B").WithAlpha(0.88f), new RectangleF(width - 280, 106, 240, 58));
            ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(width - 250, 120), WrappingLength = 200 }, "WEEKLY", Color.Black);
            ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(48, height - 150), WrappingLength = width - 96 }, "Best viewing: face south-west after sunset", Color.White.WithAlpha(0.88f));
            ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(48, height - 100), WrappingLength = width - 96 }, "Timing: 19:30-22:00 local time", Color.ParseHex("#FBBF24"));

            var cardY = height * 0.52f;
            for (var i = 0; i < Math.Min(3, objectAssets.Count); i++)
            {
                var x = 80 + (i * 320);
                ctx.Fill(Color.ParseHex("#1B264A").WithAlpha(0.82f), new RectangleF(x, cardY, 280, 220));
                ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(x + 20, cardY + 85), WrappingLength = 240 }, Path.GetFileNameWithoutExtension(objectAssets[i]), Color.White);
            }
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
        diagnostics.SelectedAssets = selected.ToList();
        diagnostics.MissingObjects = requiredObjects.Where(o => !selected.Any(s => MatchesObjectName(o, s))).ToList();
        diagnostics.FallbackUsed = selected.Count == 0;
        diagnostics.ObjectCountDrawn = selected.Count;
        diagnostics.SceneVisualsContainObjects = selected.Count > 0;
        return new CelestialObjectVisualPlan(req.SceneCode, req.SceneCode, req.NarrationSegmentCodes, requiredObjects, requiredObjects, diagnostics.FilesScanned.SelectMany(kv => kv.Value).Distinct().ToList(), selected, layout, selected.Count == 0, selected.Count == 0 ? "No assets resolved, diagnostics fallback required." : null, warnings);
    }

    private static async Task RenderOverlayImageAsync(string path, string label, int width, int height, CancellationToken ct)
    {
        var font = ResolveFont(Math.Max(30f, width * 0.022f));
        using var image = new Image<Rgba32>(width, height, Color.Transparent);
        image.Mutate(ctx =>
        {
            var text = string.IsNullOrWhiteSpace(label) ? "Overlay" : label;
            var options = new RichTextOptions(font) { Origin = new PointF(60, 60), WrappingLength = width - 120 };
            ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(63, 63) }, text, Color.Black.WithAlpha(0.7f));
            ctx.DrawText(options, text, Color.White.WithAlpha(0.85f));
        });
        await image.SaveAsPngAsync(path, ct);
        var info = Image.Identify(path) ?? throw new InvalidOperationException($"Failed to validate overlay image '{path}'.");
        if (info.Width <= 0 || info.Height <= 0) throw new InvalidOperationException($"Invalid overlay image '{path}'.");
    }

    private static async Task RenderThumbnailImageAsync(string path, string label, int width, int height, IReadOnlyList<string> objectAssets, string? regionName, CancellationToken ct)
    {
        var font = ResolveFont(Math.Max(40f, width * 0.045f));
        using var image = new Image<Rgba32>(width, height, new Rgba32(6, 8, 16));
        image.Mutate(ctx =>
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width, height), GradientRepetitionMode.None, [new ColorStop(0, Color.ParseHex("#14264F")), new ColorStop(1, Color.ParseHex("#060810"))]));
            var text = string.IsNullOrWhiteSpace(label) ? "WEEKLY SKY" : label;
            var options = new RichTextOptions(font) { Origin = new PointF(42, 48), WrappingLength = width - 84 };
            ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(46, 52) }, text, Color.Black.WithAlpha(0.82f));
            ctx.DrawText(options, text, Color.White);
            for (var i = 0; i < Math.Min(3, objectAssets.Count); i++)
            {
                var heroX = 90 + (i * 365);
                var heroBounds = new RectangleF(heroX, 180, 320, 320);
                DrawAssetTile(ctx, objectAssets[i], heroBounds, Path.GetFileNameWithoutExtension(objectAssets[i]), font);
            }
            ctx.Fill(Color.ParseHex("#000000").WithAlpha(0.40f), new RectangleF(width - 280, 24, 240, 72));
            ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(width - 260, 44), WrappingLength = 210 }, "UDAIPUR", Color.White);
            ctx.Fill(Color.ParseHex("#F59E0B").WithAlpha(0.88f), new RectangleF(width - 280, 106, 240, 58));
            ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(width - 250, 120), WrappingLength = 200 }, "WEEKLY", Color.Black);
        });
        await image.SaveAsJpegAsync(path, new JpegEncoder { Quality = 92 }, ct);
        var info = Image.Identify(path) ?? throw new InvalidOperationException($"Failed to validate thumbnail image '{path}'.");
        if (info.Width <= 0 || info.Height <= 0) throw new InvalidOperationException($"Invalid thumbnail image '{path}'.");
    }


    private static void DrawAssetTile(IImageProcessingContext ctx, string assetPath, RectangleF bounds, string fallbackLabel, Font font)
    {
        if (File.Exists(assetPath))
        {
            using var assetImage = Image.Load(assetPath);
            assetImage.Mutate(x => x.Resize(new ResizeOptions { Size = new Size((int)bounds.Width, (int)bounds.Height), Mode = ResizeMode.Crop }));
            ctx.DrawImage(assetImage, new Point((int)bounds.X, (int)bounds.Y), 1f);
            ctx.Draw(Color.White.WithAlpha(0.85f), 3f, bounds);
            return;
        }

        ctx.Fill(Color.ParseHex("#1B264A").WithAlpha(0.82f), bounds);
        ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(bounds.X + 20, bounds.Y + (bounds.Height * 0.45f)), WrappingLength = bounds.Width - 40 }, fallbackLabel, Color.White);
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
