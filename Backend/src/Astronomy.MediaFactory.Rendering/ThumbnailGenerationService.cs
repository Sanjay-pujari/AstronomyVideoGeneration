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
        var outputPath = Path.Combine(request.OutputDirectory, request.IsShortForm ? "short-cover.png" : "thumbnail.png");

        Directory.CreateDirectory(request.OutputDirectory);

        foreach (var layout in plan.LayoutCandidates.DefaultIfEmpty(plan.LayoutType))
        {
            try
            {
                using var canvas = await BuildBaseCanvasAsync(plan.SelectedVisualPath, cancellationToken);
                ApplyLayout(canvas, layout, plan.PrimaryThumbnailText);
                await canvas.SaveAsPngAsync(outputPath, cancellationToken);

                return new ThumbnailPlan
                {
                    PrimaryThumbnailText = plan.PrimaryThumbnailText,
                    AlternateThumbnailTexts = plan.AlternateThumbnailTexts,
                    LayoutType = layout,
                    LayoutCandidates = plan.LayoutCandidates,
                    SelectedVisualPath = plan.SelectedVisualPath,
                    ThumbnailPath = outputPath
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
            ThumbnailPath = plan.SelectedVisualPath
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

    private static void ApplyLayout(Image<Rgba32> canvas, ThumbnailLayoutType layoutType, string text)
    {
        var font = SystemFonts.CreateFont("Arial", 74, FontStyle.Bold);
        var white = Color.White;

        canvas.Mutate(ctx =>
        {
            switch (layoutType)
            {
                case ThumbnailLayoutType.TextLeftVisualRight:
                    ctx.Fill(Color.Black.WithAlpha(0.55f), new RectangleF(0, 0, 620, 720));
                    ctx.DrawText(new RichTextOptions(font)
                    {
                        Origin = new PointF(40, 280),
                        WrappingLength = 540,
                        HorizontalAlignment = HorizontalAlignment.Left
                    }, text, white);
                    break;
                case ThumbnailLayoutType.TopBanner:
                    ctx.Fill(Color.Black.WithAlpha(0.65f), new RectangleF(0, 0, 1280, 180));
                    ctx.DrawText(new RichTextOptions(font)
                    {
                        Origin = new PointF(640, 96),
                        WrappingLength = 1200,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, text, white);
                    break;
                default:
                    ctx.Fill(Color.Black.WithAlpha(0.45f), new RectangleF(0, 190, 1280, 340));
                    ctx.DrawText(new RichTextOptions(font)
                    {
                        Origin = new PointF(640, 360),
                        WrappingLength = 1180,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, text, white);
                    break;
            }
        });
    }
}
