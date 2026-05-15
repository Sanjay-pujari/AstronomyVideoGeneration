using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
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

public sealed class ThumbnailCompositionService : IThumbnailCompositionService
{
    private const long MaxThumbnailSizeBytes = 2 * 1024 * 1024;
    private static readonly Rgba32 WarmYellow = new(255, 215, 64);
    private readonly ThumbnailOptions _options;

    public ThumbnailCompositionService(IOptions<ThumbnailOptions> options) => _options = options.Value;

    public async Task<string> ComposeAsync(ThumbnailCompositionRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        if (request.GenerationRequest.IsShortForm)
            await RenderShortThumbnailAsync(request.SelectedCandidate.Path, request.OutputPath, request.HookText, cancellationToken);
        else
            await RenderLongThumbnailAsync(request.GenerationRequest, request.SelectedCandidate.Path, request.OutputPath, request.HookText, cancellationToken);

        await EnsureUnderSizeLimitAsync(request.OutputPath, cancellationToken);
        return request.OutputPath;
    }

    private async Task RenderLongThumbnailAsync(ThumbnailGenerationRequest request, string sourcePath, string outputPath, string hook, CancellationToken cancellationToken)
    {
        using var canvas = await LoadCanvasAsync(sourcePath, _options.LandscapeWidth, _options.LandscapeHeight, cancellationToken);
        var objectPoint = FindBrightestPoint(canvas);
        canvas.Mutate(ctx =>
        {
            ApplyEnhancements(ctx, canvas.Width, canvas.Height, objectPoint);
            if (_options.EnableGradientBackground)
            {
                ApplyBottomGradient(ctx, canvas.Width, canvas.Height, 0.54f, 0.82f);
                ApplyTopGradient(ctx, canvas.Width, canvas.Height);
            }

            var topText = $"{request.Context.Date:MMM d} • {request.Context.LocationName}";
            DrawLabel(ctx, topText, 36, new PointF(46, 36), Color.White.WithAlpha(0.88f), HorizontalAlignment.Left);

            if (_options.EnableHookText && !string.IsNullOrWhiteSpace(hook))
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
            if (_options.EnableGradientBackground)
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
        if (_options.EnableGlowEnhancement && _options.EnableGlowEffect)
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
        => ctx.Fill(new LinearGradientBrush(new PointF(0, height * startRatio), new PointF(0, height), GradientRepetitionMode.None,
            new ColorStop(0, Color.Transparent),
            new ColorStop(1, Color.Black.WithAlpha(alpha))), new RectangleF(0, height * startRatio, width, height * (1 - startRatio)));

    private static void ApplyTopGradient(IImageProcessingContext ctx, int width, int height)
        => ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height * 0.24f), GradientRepetitionMode.None,
            new ColorStop(0, Color.Black.WithAlpha(0.58f)),
            new ColorStop(1, Color.Transparent)), new RectangleF(0, 0, width, height * 0.25f));

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
        }, displayText, displayText.Length % 2 == 0 ? Color.FromPixel(WarmYellow) : Color.White);
    }

    private static void DrawLabel(IImageProcessingContext ctx, string text, float size, PointF origin, Color color, HorizontalAlignment alignment)
    {
        var font = CreateFont(size, FontStyle.Bold);
        DrawTextWithShadow(ctx, new RichTextOptions(font) { Origin = origin, HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Top }, text, color);
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

    private async Task EnsureUnderSizeLimitAsync(string outputPath, CancellationToken cancellationToken)
    {
        if (new FileInfo(outputPath).Length <= MaxThumbnailSizeBytes)
            return;

        using var image = await Image.LoadAsync(outputPath, cancellationToken);
        for (var quality = 88; quality >= 50; quality -= 8)
        {
            await image.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = quality }, cancellationToken);
            if (new FileInfo(outputPath).Length <= MaxThumbnailSizeBytes)
                return;
        }
    }

    private static string LimitWords(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(Math.Clamp(maxWords, 2, 5)).ToArray();
        return words.Length == 0 ? "Visible Tonight" : string.Join(' ', words);
    }
}
