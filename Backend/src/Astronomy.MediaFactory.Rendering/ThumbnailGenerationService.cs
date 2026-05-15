using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

public sealed class ThumbnailGenerationService : IThumbnailGenerationService
{
    private static readonly Rgba32 WarmYellow = new(255, 215, 64);
    private readonly IThumbnailStrategyService _thumbnailStrategyService;
    private readonly IThumbnailScoringService _thumbnailScoringService;
    private readonly IThumbnailHookService _thumbnailHookService;
    private readonly IThumbnailAiOptimizationService? _thumbnailAiOptimizationService;
    private readonly ThumbnailOptions _options;
    private readonly ILogger<ThumbnailGenerationService> _logger;

    public ThumbnailGenerationService(
        IThumbnailStrategyService thumbnailStrategyService,
        ILogger<ThumbnailGenerationService> logger)
        : this(thumbnailStrategyService, new ThumbnailScoringService(), new ThumbnailHookService(), Options.Create(new ThumbnailOptions()), logger)
    {
    }

    public ThumbnailGenerationService(
        IThumbnailStrategyService thumbnailStrategyService,
        IThumbnailScoringService thumbnailScoringService,
        IThumbnailHookService thumbnailHookService,
        IOptions<ThumbnailOptions> options,
        ILogger<ThumbnailGenerationService> logger,
        IThumbnailAiOptimizationService? thumbnailAiOptimizationService = null)
    {
        _thumbnailStrategyService = thumbnailStrategyService;
        _thumbnailScoringService = thumbnailScoringService;
        _thumbnailHookService = thumbnailHookService;
        _thumbnailAiOptimizationService = thumbnailAiOptimizationService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
    {
        var strategyPlan = _thumbnailStrategyService.BuildPlan(request);
        Directory.CreateDirectory(request.OutputDirectory);

        if (!_options.Enabled)
            return BuildDisabledPlan(strategyPlan);

        try
        {
            var candidates = await BuildCandidatesAsync(request, cancellationToken);
            var scores = new List<ThumbnailCandidateScore>(candidates.Count);
            foreach (var candidate in candidates)
            {
                scores.Add(await _thumbnailScoringService.ScoreAsync(candidate.Path, new ThumbnailScoringContext
                {
                    MaxBlackPixelPercentage = _options.MaxBlackPixelPercentage,
                    MinimumBrightnessScore = _options.MinimumBrightnessScore,
                    RejectDarkFrames = _options.RejectDarkFrames,
                    SceneId = candidate.SceneId,
                    TimestampSeconds = candidate.TimestampSeconds
                }, cancellationToken));
            }

            var selected = scores.Where(x => !x.IsRejected).OrderByDescending(x => x.Score).FirstOrDefault();

            if (selected is null)
            {
                selected = await CreateFallbackCandidateAsync(request, cancellationToken);
                scores.Add(selected);
            }

            var hook = _options.EnableHookText
                ? await GenerateOptimizedHookAsync(request, cancellationToken)
                : string.Empty;

            var outputPath = System.IO.Path.Combine(request.OutputDirectory, request.IsShortForm ? _options.ShortThumbnailOutputName : _options.LongThumbnailOutputName);
            if (request.IsShortForm)
                await RenderShortThumbnailAsync(selected.Path, outputPath, hook, cancellationToken);
            else
                await RenderLongThumbnailAsync(request, selected.Path, outputPath, hook, cancellationToken);

            await WriteAnalysisReportAsync(request, outputPath, selected, scores, hook, cancellationToken);

            return new ThumbnailPlan
            {
                PrimaryThumbnailText = string.IsNullOrWhiteSpace(hook) ? strategyPlan.PrimaryThumbnailText : hook,
                AlternateThumbnailTexts = strategyPlan.AlternateThumbnailTexts,
                LayoutType = strategyPlan.LayoutType,
                LayoutCandidates = strategyPlan.LayoutCandidates,
                SelectedVisualPath = selected.Path,
                ThumbnailPath = outputPath,
                LongThumbnailPath = request.IsShortForm ? null : outputPath,
                ShortThumbnailPath = request.IsShortForm ? outputPath : null,
                ThumbnailVariantPaths = [outputPath],
                CandidateScores = scores,
                Variants = strategyPlan.Variants
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Production thumbnail generation failed. Falling back to source visual when possible.");
            return BuildFallbackPlan(strategyPlan);
        }
    }

    private async Task<string> GenerateOptimizedHookAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
    {
        if (_thumbnailAiOptimizationService is not null)
        {
            var optimization = await _thumbnailAiOptimizationService.OptimizeAsync(new ThumbnailAiOptimizationRequest
            {
                GenerationRequest = request,
                SeoTitle = request.Metadata.PrimaryTitle,
                Language = request.Context.Localization.ResolvedLanguage,
                Region = request.Context.LocationName,
                TopPerformingHooks = request.FeedbackSignals?.BestHooks ?? []
            }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(optimization.SelectedHook))
                return optimization.SelectedHook;
        }

        return _thumbnailHookService.GenerateHook(request, _options.MaxHookWords);
    }

    private async Task<List<ThumbnailCandidate>> BuildCandidatesAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
    {
        var candidatesDirectory = System.IO.Path.Combine(request.OutputDirectory, "thumbnails", "candidates");
        Directory.CreateDirectory(candidatesDirectory);
        var visuals = request.AvailableVisuals.Where(File.Exists).ToArray();
        var candidates = new List<ThumbnailCandidate>();

        for (var i = 0; i < visuals.Length; i++)
        {
            var scene = request.Scenes.ElementAtOrDefault(i);
            var sceneId = scene?.SceneId ?? request.Context.SceneObservationContexts.ElementAtOrDefault(i)?.SceneId ?? $"scene-{i + 1:000}";
            var duration = Math.Max(1, scene?.DurationSeconds ?? EstimateSceneDurationSeconds(request));
            foreach (var timestamp in BuildSampleTimes(duration))
            {
                var safeTimestamp = AvoidFadeFrame(timestamp, duration);
                if (safeTimestamp is null)
                    continue;

                var suffix = Math.Round(safeTimestamp.Value / duration * 100).ToString("000");
                var output = System.IO.Path.Combine(candidatesDirectory, $"{SanitizeFileName(sceneId)}-{suffix}.jpg");
                await CopyAsCandidateAsync(visuals[i], output, cancellationToken);
                candidates.Add(new ThumbnailCandidate(output, sceneId, safeTimestamp.Value));
            }
        }

        return candidates;
    }

    private IEnumerable<double> BuildSampleTimes(double durationSeconds)
    {
        var ratios = durationSeconds > 8 && _options.CandidateFramesPerScene >= 3
            ? new[] { 0.25, 0.50, 0.75 }
            : new[] { 0.50 };

        foreach (var ratio in ratios.Take(Math.Max(1, _options.CandidateFramesPerScene)))
            yield return durationSeconds * ratio;
    }

    private double? AvoidFadeFrame(double timestamp, double durationSeconds)
    {
        if (!_options.AvoidFadeFrames)
            return timestamp;

        var fade = Math.Max(0, _options.FadeAvoidanceSeconds);
        if (durationSeconds <= fade * 2)
            return durationSeconds / 2d;

        return Math.Clamp(timestamp, fade, durationSeconds - fade);
    }

    private static int EstimateSceneDurationSeconds(ThumbnailGenerationRequest request)
    {
        if (request.IsShortForm)
            return 6;

        return request.Context.SceneObservationContexts.Count > 0 ? 10 : 6;
    }

    private static async Task CopyAsCandidateAsync(string input, string output, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(input, cancellationToken);
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 92 }, cancellationToken);
    }

    private async Task<ThumbnailCandidateScore> CreateFallbackCandidateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
    {
        var path = System.IO.Path.Combine(request.OutputDirectory, "thumbnails", "candidates", "fallback.jpg");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var image = new Image<Rgba32>(request.IsShortForm ? _options.PortraitWidth : _options.LandscapeWidth, request.IsShortForm ? _options.PortraitHeight : _options.LandscapeHeight, new Rgba32(6, 12, 32));
        image.Mutate(ctx =>
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(image.Width, image.Height), GradientRepetitionMode.None,
                new ColorStop(0, Color.FromRgb(8, 16, 42)),
                new ColorStop(1, Color.FromRgb(54, 38, 92))), new RectangleF(0, 0, image.Width, image.Height));
            ctx.Fill(Color.White.WithAlpha(0.85f), new EllipsePolygon(image.Width * 0.58f, image.Height * 0.38f, image.Width * 0.08f));
        });
        await image.SaveAsJpegAsync(path, new JpegEncoder { Quality = 92 }, cancellationToken);
        return await _thumbnailScoringService.ScoreAsync(path, new ThumbnailScoringContext { RejectDarkFrames = false }, cancellationToken);
    }

    private async Task RenderLongThumbnailAsync(ThumbnailGenerationRequest request, string sourcePath, string outputPath, string hook, CancellationToken cancellationToken)
    {
        using var canvas = await LoadCanvasAsync(sourcePath, _options.LandscapeWidth, _options.LandscapeHeight, cancellationToken);
        var objectPoint = FindBrightestPoint(canvas);
        canvas.Mutate(ctx =>
        {
            ApplyEnhancements(ctx, canvas.Width, canvas.Height, objectPoint);
            ApplyBottomGradient(ctx, canvas.Width, canvas.Height, 0.54f, 0.82f);
            ApplyTopGradient(ctx, canvas.Width, canvas.Height);

            var topText = $"{request.Context.Date:MMM d} • {request.Context.LocationName}";
            DrawLabel(ctx, topText, 36, new PointF(46, 36), Color.White.WithAlpha(0.88f), HorizontalAlignment.Left);

            if (!string.IsNullOrWhiteSpace(hook))
                DrawHeadline(ctx, hook, canvas.Width, canvas.Height);

            DrawBrand(ctx, canvas.Width, canvas.Height);
        });

        await canvas.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = 92 }, cancellationToken);
    }

    private async Task RenderShortThumbnailAsync(string sourcePath, string outputPath, string hook, CancellationToken cancellationToken)
    {
        using var canvas = await LoadCanvasAsync(sourcePath, _options.PortraitWidth, _options.PortraitHeight, cancellationToken);
        var objectPoint = FindBrightestPoint(canvas);
        canvas.Mutate(ctx =>
        {
            ApplyEnhancements(ctx, canvas.Width, canvas.Height, objectPoint);
            ApplyBottomGradient(ctx, canvas.Width, canvas.Height, 0.72f, 0.64f);
            if (_options.EnableHookText && !string.IsNullOrWhiteSpace(hook))
            {
                var font = CreateFont(72, FontStyle.Bold);
                var text = LimitWords(hook, Math.Min(4, _options.MaxHookWords));
                DrawTextWithShadow(ctx, new RichTextOptions(font)
                {
                    Origin = new PointF(canvas.Width / 2f, canvas.Height - 185),
                    WrappingLength = canvas.Width - 120,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, text, Color.White);
            }
            DrawBrand(ctx, canvas.Width, canvas.Height);
        });

        await canvas.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = 92 }, cancellationToken);
    }

    private static async Task<Image<Rgba32>> LoadCanvasAsync(string sourcePath, int width, int height, CancellationToken cancellationToken)
    {
        using var source = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken);
        source.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(width, height),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));
        return source.Clone();
    }

    private void ApplyEnhancements(IImageProcessingContext ctx, int width, int height, PointF brightPoint)
    {
        if (_options.EnableContrastBoost)
            ctx.Contrast(1.18f).Saturate(1.14f);
        if (_options.EnableSharpnessBoost)
            ctx.GaussianSharpen(0.65f);
        if (_options.EnableGlowEnhancement)
            ctx.Fill(Color.FromPixel(WarmYellow).WithAlpha(0.16f), new EllipsePolygon(brightPoint.X, brightPoint.Y, width * 0.16f, height * 0.16f));
        if (_options.EnableVignette)
            ApplyVignette(ctx, width, height);
    }

    private static void ApplyVignette(IImageProcessingContext ctx, int width, int height)
    {
        var edge = Color.Black.WithAlpha(0.18f);
        ctx.Fill(edge, new RectangleF(0, 0, width, height * 0.10f));
        ctx.Fill(edge, new RectangleF(0, height * 0.90f, width, height * 0.10f));
        ctx.Fill(edge, new RectangleF(0, 0, width * 0.08f, height));
        ctx.Fill(edge, new RectangleF(width * 0.92f, 0, width * 0.08f, height));
    }

    private static void ApplyBottomGradient(IImageProcessingContext ctx, int width, int height, float startRatio, float alpha)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, height * startRatio), new PointF(0, height), GradientRepetitionMode.None,
            new ColorStop(0, Color.Transparent),
            new ColorStop(1, Color.Black.WithAlpha(alpha))), new RectangleF(0, height * startRatio, width, height * (1 - startRatio)));
    }

    private static void ApplyTopGradient(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height * 0.24f), GradientRepetitionMode.None,
            new ColorStop(0, Color.Black.WithAlpha(0.58f)),
            new ColorStop(1, Color.Transparent)), new RectangleF(0, 0, width, height * 0.25f));
    }

    private void DrawHeadline(IImageProcessingContext ctx, string hook, int width, int height)
    {
        var displayText = LimitWords(hook, _options.MaxHookWords);
        var font = CreateFont(92, FontStyle.Bold);
        DrawTextWithShadow(ctx, new RichTextOptions(font)
        {
            Origin = new PointF(width / 2f, height - 68),
            WrappingLength = width - 110,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        }, displayText, ShouldUseYellow(displayText) ? Color.FromPixel(WarmYellow) : Color.White);
    }

    private static void DrawLabel(IImageProcessingContext ctx, string text, float size, PointF origin, Color color, HorizontalAlignment alignment)
    {
        var font = CreateFont(size, FontStyle.Bold);
        DrawTextWithShadow(ctx, new RichTextOptions(font)
        {
            Origin = origin,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top
        }, text, color);
    }

    private void DrawBrand(IImageProcessingContext ctx, int width, int height)
    {
        if (!_options.EnableBranding || string.IsNullOrWhiteSpace(_options.BrandText))
            return;

        var font = CreateFont(width > height ? 30 : 34, FontStyle.Bold);
        DrawTextWithShadow(ctx, new RichTextOptions(font)
        {
            Origin = new PointF(width - 42, height - 42),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        }, _options.BrandText, Color.White.WithAlpha(0.56f));
    }

    private static void DrawTextWithShadow(IImageProcessingContext ctx, RichTextOptions options, string text, Color color)
    {
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(options.Origin.X + 4, options.Origin.Y + 4) }, text, Color.Black.WithAlpha(0.82f));
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(options.Origin.X + 1, options.Origin.Y + 1) }, text, color.WithAlpha(0.35f));
        ctx.DrawText(options, text, color);
    }

    private static Font CreateFont(float size, FontStyle style)
    {
        var family = SystemFonts.Collection.Families.FirstOrDefault(f => f.Name.Equals("Arial", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(family.Name))
            family = SystemFonts.Collection.Families.First();

        return family.CreateFont(size, style);
    }

    private static PointF FindBrightestPoint(Image<Rgba32> image)
    {
        var best = 0d;
        var point = new PointF(image.Width / 2f, image.Height / 2f);
        for (var y = 0; y < image.Height; y += Math.Max(1, image.Height / 80))
        {
            var row = image.DangerousGetPixelRowMemory(y).Span;
            for (var x = 0; x < image.Width; x += Math.Max(1, image.Width / 120))
            {
                var px = row[x];
                var lum = (0.2126 * px.R) + (0.7152 * px.G) + (0.0722 * px.B);
                if (lum > best)
                {
                    best = lum;
                    point = new PointF(x, y);
                }
            }
        }
        return point;
    }

    private async Task WriteAnalysisReportAsync(ThumbnailGenerationRequest request, string outputPath, ThumbnailCandidateScore selected, IReadOnlyCollection<ThumbnailCandidateScore> scores, string hook, CancellationToken cancellationToken)
    {
        var payload = new
        {
            selectedThumbnail = outputPath,
            selectedSceneId = selected.SceneId,
            candidateScores = scores.Where(x => !x.IsRejected),
            rejectedCandidates = scores.Where(x => x.IsRejected),
            brightness = selected.Brightness,
            blackPixelPercentage = selected.BlackPixelPercentage,
            hookText = hook,
            brandText = _options.EnableBranding ? _options.BrandText : string.Empty,
            thumbnailStyle = request.IsShortForm ? _options.ShortThumbnailStyle : _options.LongThumbnailStyle,
            localizationLanguage = request.Context.Localization.ResolvedLanguage
        };

        await File.WriteAllTextAsync(System.IO.Path.Combine(request.OutputDirectory, "thumbnail-analysis-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static ThumbnailPlan BuildDisabledPlan(ThumbnailPlan strategyPlan) => strategyPlan;

    private static ThumbnailPlan BuildFallbackPlan(ThumbnailPlan strategyPlan)
    {
        var fallback = strategyPlan.SelectedVisualPath;
        return new ThumbnailPlan
        {
            PrimaryThumbnailText = strategyPlan.PrimaryThumbnailText,
            AlternateThumbnailTexts = strategyPlan.AlternateThumbnailTexts,
            LayoutType = strategyPlan.LayoutType,
            LayoutCandidates = strategyPlan.LayoutCandidates,
            SelectedVisualPath = fallback,
            ThumbnailPath = fallback,
            ThumbnailVariantPaths = fallback is null ? [] : [fallback],
            Variants = strategyPlan.Variants
        };
    }

    private static string LimitWords(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(Math.Clamp(maxWords, 2, 5))
            .ToArray();
        return words.Length == 0 ? "Visible Tonight" : string.Join(' ', words);
    }

    private static bool ShouldUseYellow(string text) => text.Length % 2 == 0;

    private static string SanitizeFileName(string value)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "scene" : cleaned;
    }

    private sealed record ThumbnailCandidate(string Path, string SceneId, double TimestampSeconds);
}
