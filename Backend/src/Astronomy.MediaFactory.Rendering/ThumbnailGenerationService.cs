using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

public sealed class ThumbnailGenerationService : IThumbnailGenerationService
{
    private static readonly Rgba32 WarmYellow = new(255, 215, 64);
    private readonly IThumbnailStrategyService _thumbnailStrategyService;
    private readonly ILogger<ThumbnailGenerationService> _logger;

    public ThumbnailGenerationService(IThumbnailStrategyService thumbnailStrategyService, ILogger<ThumbnailGenerationService> logger)
    {
        _thumbnailStrategyService = thumbnailStrategyService;
        _logger = logger;
    }

    public async Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
    {
        var plan = _thumbnailStrategyService.BuildPlan(request);
        var outputPrefix = request.IsShortForm ? "short-cover" : "thumbnail";

        Directory.CreateDirectory(request.OutputDirectory);

        foreach (var layout in plan.LayoutCandidates.DefaultIfEmpty(plan.LayoutType))
        {
            try
            {
                var variantPaths = new List<string>(capacity: 3);
                for (var i = 0; i < 3; i++)
                {
                    using var canvas = await BuildBaseCanvasAsync(plan.SelectedVisualPath, cancellationToken);
                    var variantText = ResolveVariantText(plan, i);
                    ApplyLayout(canvas, layout, variantText, i);
                    var outputPath = Path.Combine(request.OutputDirectory, $"{outputPrefix}-{i + 1}.png");
                    await canvas.SaveAsPngAsync(outputPath, cancellationToken);
                    variantPaths.Add(outputPath);
                }

                return new ThumbnailPlan
                {
                    PrimaryThumbnailText = plan.PrimaryThumbnailText,
                    AlternateThumbnailTexts = plan.AlternateThumbnailTexts,
                    LayoutType = layout,
                    LayoutCandidates = plan.LayoutCandidates,
                    SelectedVisualPath = plan.SelectedVisualPath,
                    ThumbnailPath = variantPaths[0],
                    ThumbnailVariantPaths = variantPaths,
                    Variants = plan.Variants
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail generation failed for layout {LayoutType}. Trying fallback layout when available.", layout);
            }
        }

        _logger.LogError("Thumbnail generation failed for all layout candidates. Falling back to source visual when possible.");
        return new ThumbnailPlan
        {
            PrimaryThumbnailText = plan.PrimaryThumbnailText,
            AlternateThumbnailTexts = plan.AlternateThumbnailTexts,
            LayoutType = plan.LayoutType,
            LayoutCandidates = plan.LayoutCandidates,
            SelectedVisualPath = plan.SelectedVisualPath,
            ThumbnailPath = plan.SelectedVisualPath,
            ThumbnailVariantPaths = plan.SelectedVisualPath is null ? [] : [plan.SelectedVisualPath],
            Variants = plan.Variants
        };
    }

    private static async Task<Image<Rgba32>> BuildBaseCanvasAsync(string? selectedVisualPath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(selectedVisualPath) && File.Exists(selectedVisualPath))
        {
            using var source = await Image.LoadAsync<Rgba32>(selectedVisualPath, cancellationToken);
            source.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(1280, 720),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            }));
            return source.Clone();
        }

        return new Image<Rgba32>(1280, 720, Color.Black);
    }

    private static void ApplyLayout(Image<Rgba32> canvas, ThumbnailLayoutType layoutType, string text, int variantIndex)
    {
        var font = SystemFonts.CreateFont("Arial", 84, FontStyle.Bold);
        var displayText = LimitToThreeWords(text);
        var textColor = ShouldUseYellow(displayText) ? Color.FromPixel(WarmYellow) : Color.White;
        var shadowColor = Color.Black.WithAlpha(0.85f);

        canvas.Mutate(ctx =>
        {
            ApplySubtleGradient(ctx, variantIndex);
            ApplyVignette(ctx);
            ApplyBottomReadabilityGradient(ctx);

            switch (layoutType)
            {
                case ThumbnailLayoutType.TextLeftVisualRight:
                    DrawTextWithGlow(ctx, new RichTextOptions(font)
                    {
                        Origin = new PointF(640, 560),
                        WrappingLength = 820,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, displayText, textColor, shadowColor);
                    break;
                case ThumbnailLayoutType.TopBanner:
                    DrawTextWithGlow(ctx, new RichTextOptions(font)
                    {
                        Origin = new PointF(640, 618),
                        WrappingLength = 1180,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, displayText, textColor, shadowColor);
                    break;
                default:
                    DrawTextWithGlow(ctx, new RichTextOptions(font)
                    {
                        Origin = new PointF(640, 620),
                        WrappingLength = 1180,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, displayText, textColor, shadowColor);
                    break;
            }
        });
    }

    private static string ResolveVariantText(ThumbnailPlan plan, int index)
    {
        if (index == 0)
        {
            return plan.PrimaryThumbnailText;
        }

        return plan.AlternateThumbnailTexts.ElementAtOrDefault(index - 1) ?? plan.PrimaryThumbnailText;
    }

    private static string LimitToThreeWords(string text)
    {
        var words = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(3)
            .ToArray();
        return words.Length == 0 ? "ASTRONOMY UPDATE" : string.Join(' ', words);
    }

    private static bool ShouldUseYellow(string text)
        => text.Length % 2 == 0;

    private static void DrawTextWithGlow(IImageProcessingContext ctx, RichTextOptions options, string text, Color textColor, Color shadowColor)
    {
        var glowOptions = new RichTextOptions(options)
        {
            Origin = new PointF(options.Origin.X + 2, options.Origin.Y + 2)
        };
        var shadowOptions = new RichTextOptions(options)
        {
            Origin = new PointF(options.Origin.X + 5, options.Origin.Y + 5)
        };

        ctx.DrawText(glowOptions, text, textColor.WithAlpha(0.35f));
        ctx.DrawText(shadowOptions, text, shadowColor);
        ctx.DrawText(options, text, textColor);
    }

    private static void ApplyBottomReadabilityGradient(IImageProcessingContext ctx)
    {
        var gradientBrush = new LinearGradientBrush(
            new PointF(0, 420),
            new PointF(0, 720),
            GradientRepetitionMode.None,
            new ColorStop(0f, Color.Transparent),
            new ColorStop(1f, Color.Black.WithAlpha(0.82f)));

        ctx.Fill(gradientBrush, new RectangleF(0, 360, 1280, 360));
    }

    private static void ApplySubtleGradient(IImageProcessingContext ctx, int variantIndex)
    {
        var accentColor = variantIndex switch
        {
            1 => new Rgba32(59, 130, 246, 120),
            2 => new Rgba32(168, 85, 247, 110),
            _ => new Rgba32(30, 64, 175, 90)
        };

        var gradientBrush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(1280, 720),
            GradientRepetitionMode.None,
            new ColorStop(0f, Color.Transparent),
            new ColorStop(1f, Color.FromPixel(accentColor)));

        ctx.Fill(gradientBrush, new RectangleF(0, 0, 1280, 720));
    }

    private static void ApplyVignette(IImageProcessingContext ctx)
    {
        ctx.Fill(Color.Black.WithAlpha(0.18f), new EllipsePolygon(640, 360, 900, 500));
    }
}
