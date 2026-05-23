using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
            await File.WriteAllTextAsync(req.MetadataOutputPath, $"{req.SceneCode} metadata", cancellationToken);
            await File.WriteAllTextAsync(req.DebugOutputPath, $"{req.SceneCode} debug", cancellationToken);

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
                        await ExecuteStellariumSceneAsync(req, prep, requestWarnings, requestErrors, stellariumResults, sceneTimeoutCts.Token);
                        break;
                    case "CelestialAssetCompositor":
                        await ExecuteCelestialAssetSceneAsync(req, prep, requestWarnings, requestErrors, assetResults, sceneTimeoutCts.Token);
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
            logger.LogInformation("Completed scene render: {SceneCode}, status={Status}, elapsedMs={ElapsedMs}", req.SceneCode, status, (DateTime.UtcNow - sceneStartedAt).TotalMilliseconds);
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
        var framePath = Path.Combine(Path.GetDirectoryName(outputPath)!, $"{Path.GetFileNameWithoutExtension(outputPath)}.scene-frame.png");
        logger.LogInformation("Preparing C# background frame");
        logger.LogInformation("C# image composition started");
        using (var imageCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            imageCts.CancelAfter(ImageCompositionTimeout);
            await RenderSceneFrameAsync(framePath, label, o.VideoWidth, o.VideoHeight, imageCts.Token);
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
    private async Task RenderThumbnailAsync(string outputPath, string label, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await RenderThumbnailImageAsync(outputPath, label, 1280, 720, ct);
    }

    private static async Task RenderSceneFrameAsync(string path, string label, int width, int height, CancellationToken ct)
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
        });
        await image.SaveAsPngAsync(path, ct);
        var info = Image.Identify(path) ?? throw new InvalidOperationException($"Failed to validate generated scene frame '{path}'.");
        if (info.Width <= 0 || info.Height <= 0) throw new InvalidOperationException($"Invalid generated scene frame '{path}'.");
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

    private static async Task RenderThumbnailImageAsync(string path, string label, int width, int height, CancellationToken ct)
    {
        var font = ResolveFont(Math.Max(40f, width * 0.045f));
        using var image = new Image<Rgba32>(width, height, new Rgba32(6, 8, 16));
        image.Mutate(ctx =>
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width, height), GradientRepetitionMode.None, [new ColorStop(0, Color.ParseHex("#14264F")), new ColorStop(1, Color.ParseHex("#060810"))]));
            var text = string.IsNullOrWhiteSpace(label) ? "Weekly story" : label;
            var options = new RichTextOptions(font) { Origin = new PointF(42, 48), WrappingLength = width - 84 };
            ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(46, 52) }, text, Color.Black.WithAlpha(0.82f));
            ctx.DrawText(options, text, Color.White);
        });
        await image.SaveAsJpegAsync(path, new JpegEncoder { Quality = 92 }, ct);
        var info = Image.Identify(path) ?? throw new InvalidOperationException($"Failed to validate thumbnail image '{path}'.");
        if (info.Width <= 0 || info.Height <= 0) throw new InvalidOperationException($"Invalid thumbnail image '{path}'.");
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
