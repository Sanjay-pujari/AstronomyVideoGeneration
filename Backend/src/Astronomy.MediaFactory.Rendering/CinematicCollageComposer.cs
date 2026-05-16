using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using IOPath = System.IO.Path;

namespace Astronomy.MediaFactory.Rendering;

public sealed class CinematicCollageComposer : ICinematicCollageComposer
{
    private readonly ThumbnailOptions _options;

    public CinematicCollageComposer(IOptions<ThumbnailOptions> options) => _options = options.Value;

    public async Task<string> ComposeAsync(CinematicCollageRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(IOPath.GetDirectoryName(request.OutputPath)!);
        var width = request.GenerationRequest.IsShortForm ? _options.PortraitWidth : _options.LandscapeWidth;
        var height = request.GenerationRequest.IsShortForm ? _options.PortraitHeight : _options.LandscapeHeight;
        using var canvas = await BuildBackgroundAsync(request.BackgroundPath, width, height, request.Selection, cancellationToken);
        var assets = request.Selection.AssetSources.Where(a => File.Exists(a.LocalPath)).ToArray();
        var hero = assets.FirstOrDefault();
        var supports = assets.Skip(1).Take(request.GenerationRequest.IsShortForm ? 1 : 2).ToArray();

        canvas.Mutate(ctx =>
        {
            ApplyDocumentaryGrade(ctx, width, height);
            DrawNegativeSpaceGradient(ctx, width, height, request.GenerationRequest.IsShortForm);
        });

        if (hero is not null)
            await DrawAssetAsync(canvas, hero, request.GenerationRequest.IsShortForm ? HeroPortraitRect(width, height, request.Selection.SpecialEventMode) : HeroLandscapeRect(width, height, request.Selection.SpecialEventMode), true, cancellationToken);

        for (var i = 0; i < supports.Length; i++)
            await DrawAssetAsync(canvas, supports[i], request.GenerationRequest.IsShortForm ? SupportPortraitRect(width, height, i) : SupportLandscapeRect(width, height, i), false, cancellationToken);

        canvas.Mutate(ctx =>
        {
            DrawTitle(ctx, request.Selection.SelectedHook, width, height, request.GenerationRequest.IsShortForm, request.GenerationRequest.Context.Localization.ResolvedLanguage);
            DrawBrand(ctx, width, height);
            ApplyVignette(ctx, width, height);
        });

        await canvas.SaveAsJpegAsync(request.OutputPath, new JpegEncoder { Quality = 92 }, cancellationToken);
        return request.OutputPath;
    }

    private static async Task<Image<Rgba32>> BuildBackgroundAsync(string backgroundPath, int width, int height, CelestialThumbnailSelection selection, CancellationToken cancellationToken)
    {
        if (File.Exists(backgroundPath))
        {
            using var source = await Image.LoadAsync<Rgba32>(backgroundPath, cancellationToken);
            source.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Crop }).GaussianBlur(7.5f).Brightness(0.55f).Saturate(0.72f));
            return source.Clone();
        }

        var canvas = new Image<Rgba32>(width, height, Color.FromRgb(1, 4, 15));
        canvas.Mutate(ctx => DrawProceduralStarfield(ctx, width, height, selection.HeroObject));
        return canvas;
    }

    private static async Task DrawAssetAsync(Image<Rgba32> canvas, CelestialAsset asset, RectangleF target, bool hero, CancellationToken cancellationToken)
    {
        using var source = await Image.LoadAsync<Rgba32>(asset.LocalPath, cancellationToken);
        source.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions { Size = new Size((int)target.Width, (int)target.Height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center });
            if (hero)
                ApplyLocalDirectionalGrade(ctx, target.Width, target.Height, ResolveGlow(asset.Category));
            else
                ctx.GaussianBlur(1.1f).Brightness(0.78f).Saturate(0.70f);
        });

        canvas.Mutate(ctx =>
        {
            if (hero)
                DrawDirectionalEdgeScatter(ctx, target, ResolveGlow(asset.Category));
            else
                DrawDepthHaze(ctx, target);

            ctx.DrawImage(source, new Point((int)target.X, (int)target.Y), hero ? 0.94f : 0.64f);
            if (hero)
                DrawSubtleRimSpecular(ctx, target);
        });
    }


    private static void ApplyLocalDirectionalGrade(IImageProcessingContext ctx, float width, float height, Color rimColor)
    {
        ctx.Contrast(1.02f).Saturate(0.96f);
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width, height), GradientRepetitionMode.None,
            new ColorStop(0, rimColor.WithAlpha(0.070f)),
            new ColorStop(0.42f, Color.Transparent),
            new ColorStop(1, Color.Black.WithAlpha(0.16f))), new RectangleF(0, 0, width, height));
    }

    private static void DrawDirectionalEdgeScatter(IImageProcessingContext ctx, RectangleF target, Color rimColor)
    {
        var lightStart = new PointF(target.Left - target.Width * 0.16f, target.Top + target.Height * 0.08f);
        var lightEnd = new PointF(target.Right + target.Width * 0.08f, target.Bottom + target.Height * 0.22f);
        ctx.Fill(new LinearGradientBrush(lightStart, lightEnd, GradientRepetitionMode.None,
            new ColorStop(0, rimColor.WithAlpha(0.050f)),
            new ColorStop(0.36f, Color.White.WithAlpha(0.016f)),
            new ColorStop(1, Color.Transparent)), new RectangleF(
                Math.Max(0, target.X - target.Width * 0.12f),
                Math.Max(0, target.Y - target.Height * 0.12f),
                target.Width * 1.26f,
                target.Height * 1.26f));
    }

    private static void DrawDepthHaze(IImageProcessingContext ctx, RectangleF target)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(target.Left, target.Top), new PointF(target.Right, target.Bottom), GradientRepetitionMode.None,
            new ColorStop(0, Color.FromRgb(96, 116, 150).WithAlpha(0.035f)),
            new ColorStop(1, Color.FromRgb(10, 20, 42).WithAlpha(0.070f))), target);
    }

    private static void DrawSubtleRimSpecular(IImageProcessingContext ctx, RectangleF target)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(target.Left, target.Top), new PointF(target.Left + target.Width * 0.42f, target.Top + target.Height * 0.36f), GradientRepetitionMode.None,
            new ColorStop(0, Color.White.WithAlpha(0.030f)),
            new ColorStop(1, Color.Transparent)), new RectangleF(target.Left, target.Top, target.Width * 0.48f, target.Height * 0.42f));
    }

    private static void DrawClusteredStars(IImageProcessingContext ctx, int width, int height, Random random)
    {
        var stars = Math.Clamp(width * height / 15000, 65, 190);
        var clusters = 3 + random.Next(3);
        for (var c = 0; c < clusters; c++)
        {
            var cx = random.NextSingle() * width;
            var cy = random.NextSingle() * height;
            var spread = Math.Min(width, height) * (0.16f + random.NextSingle() * 0.18f);
            for (var i = 0; i < stars / clusters; i++)
            {
                var clustered = random.NextSingle() < 0.55f;
                var x = clustered ? cx + (random.NextSingle() - 0.5f) * spread : random.NextSingle() * width;
                var y = clustered ? cy + (random.NextSingle() - 0.5f) * spread : random.NextSingle() * height;
                if (x < 0 || x >= width || y < 0 || y >= height) continue;
                var r = 0.42f + MathF.Pow(random.NextSingle(), 2.1f) * 1.10f;
                var starColor = random.Next(5) switch
                {
                    0 => Color.FromRgb(184, 210, 255),
                    1 => Color.FromRgb(255, 226, 184),
                    2 => Color.FromRgb(218, 235, 255),
                    _ => Color.White
                };
                ctx.Fill(starColor.WithAlpha(0.07f + random.NextSingle() * 0.24f), new EllipsePolygon(x, y, r));
            }
        }
    }

    private static void DrawGalacticDust(IImageProcessingContext ctx, int width, int height, Random random)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, height * 0.18f), new PointF(width, height * 0.74f), GradientRepetitionMode.None,
            new ColorStop(0, Color.FromRgb(25, 42, 76).WithAlpha(0.030f)),
            new ColorStop(0.52f, Color.FromRgb(86, 62, 105).WithAlpha(0.022f)),
            new ColorStop(1, Color.Transparent)), new RectangleF(0, 0, width, height));

        for (var i = 0; i < 20; i++)
        {
            var x = random.NextSingle() * width;
            var y = height * (0.14f + random.NextSingle() * 0.62f);
            var w = width * (0.035f + random.NextSingle() * 0.070f);
            var h = height * (0.012f + random.NextSingle() * 0.028f);
            ctx.Fill(Color.FromRgb(78, 96, 132).WithAlpha(0.010f + random.NextSingle() * 0.014f), new EllipsePolygon(x, y, w, h));
        }
    }

    private static RectangleF HeroLandscapeRect(int width, int height, bool specialEvent)
    {
        var size = specialEvent ? height * 0.98f : height * 0.86f;
        return new RectangleF(width - size - width * 0.04f, height * (specialEvent ? 0.02f : 0.07f), size, size);
    }

    private static RectangleF SupportLandscapeRect(int width, int height, int index)
    {
        var size = height * (index == 0 ? 0.30f : 0.23f);
        return new RectangleF(width * (index == 0 ? 0.52f : 0.42f), height * (index == 0 ? 0.12f : 0.58f), size, size);
    }

    private static RectangleF HeroPortraitRect(int width, int height, bool specialEvent)
    {
        var size = width * (specialEvent ? 1.05f : 0.92f);
        return new RectangleF((width - size) / 2f, height * 0.10f, size, size);
    }

    private static RectangleF SupportPortraitRect(int width, int height, int index)
    {
        var size = width * 0.26f;
        return new RectangleF(width * 0.64f, height * 0.44f, size, size);
    }

    private static void DrawTitle(IImageProcessingContext ctx, string title, int width, int height, bool portrait, string language)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;

        var maxWords = portrait ? 4 : 5;
        var text = string.Join(' ', title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(maxWords));
        var font = CreateFont(portrait ? 64 : 70, portrait ? FontStyle.Regular : FontStyle.Bold, LocalizationResolver.IsHindi(language));
        var origin = portrait ? new PointF(width / 2f, height - 480) : new PointF(74, height - 182);
        var options = new RichTextOptions(font)
        {
            Origin = origin,
            WrappingLength = portrait ? width - 150 : width * 0.47f,
            HorizontalAlignment = portrait ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(origin.X + 4, origin.Y + 4) }, text, Color.Black.WithAlpha(portrait ? 0.48f : 0.72f));
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(origin.X + 1, origin.Y + 1) }, text, Color.FromRgb(255, 222, 130).WithAlpha(portrait ? 0.22f : 0.36f));
        ctx.DrawText(options, text, Color.White.WithAlpha(portrait ? 0.94f : 1f));
    }

    private void DrawBrand(IImageProcessingContext ctx, int width, int height)
    {
        if (!_options.EnableBranding || string.IsNullOrWhiteSpace(_options.BrandText))
            return;
        var font = CreateFont(width > height ? 28 : 32, FontStyle.Bold, false);
        ctx.DrawText(new RichTextOptions(font)
        {
            Origin = new PointF(width - 38, height - 34),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        }, _options.BrandText, Color.White.WithAlpha(0.52f));
    }

    private static void DrawNegativeSpaceGradient(IImageProcessingContext ctx, int width, int height, bool portrait)
    {
        if (portrait)
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, height * 0.48f), new PointF(0, height), GradientRepetitionMode.None,
                new ColorStop(0, Color.Transparent), new ColorStop(1, Color.Black.WithAlpha(0.82f))), new RectangleF(0, height * 0.44f, width, height * 0.56f));
            return;
        }
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width * 0.62f, 0), GradientRepetitionMode.None,
            new ColorStop(0, Color.Black.WithAlpha(0.76f)), new ColorStop(1, Color.Transparent)), new RectangleF(0, 0, width * 0.70f, height));
        ctx.Fill(new LinearGradientBrush(new PointF(0, height * 0.48f), new PointF(0, height), GradientRepetitionMode.None,
            new ColorStop(0, Color.Transparent), new ColorStop(1, Color.Black.WithAlpha(0.54f))), new RectangleF(0, height * 0.42f, width, height * 0.58f));
    }

    private static void ApplyDocumentaryGrade(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Contrast(1.12f).Saturate(1.04f).Brightness(0.96f);
        DrawProceduralStarfield(ctx, width, height, "grade");
        ctx.Fill(Color.FromRgb(3, 18, 46).WithAlpha(0.17f), new RectangleF(0, 0, width, height));
    }

    private static void DrawProceduralStarfield(IImageProcessingContext ctx, int width, int height, string seedText)
    {
        var random = new Random(Math.Abs(seedText.GetHashCode()));
        DrawClusteredStars(ctx, width, height, random);
        DrawGalacticDust(ctx, width, height, random);
    }

    private static void ApplyVignette(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Fill(Color.Black.WithAlpha(0.22f), new RectangleF(0, 0, width, 24));
        ctx.Fill(Color.Black.WithAlpha(0.20f), new RectangleF(0, height - 28, width, 28));
        ctx.Fill(Color.Black.WithAlpha(0.18f), new RectangleF(0, 0, 28, height));
        ctx.Fill(Color.Black.WithAlpha(0.18f), new RectangleF(width - 28, 0, 28, height));
    }

    private static Color ResolveGlow(string category) => category switch
    {
        "jupiter" or "saturn" or "venus" => Color.FromRgb(255, 206, 122),
        "mars" or "lunar-eclipse" => Color.FromRgb(255, 122, 82),
        "moon" or "solar-eclipse" => Color.FromRgb(245, 240, 220),
        "uranus" or "neptune" => Color.FromRgb(105, 175, 255),
        _ => Color.FromRgb(120, 150, 255)
    };

    private static Font CreateFont(float size, FontStyle style, bool preferHindi)
    {
        var families = SystemFonts.Collection.Families;
        var family = preferHindi
            ? families.FirstOrDefault(f => f.Name.Contains("Noto", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Devanagari", StringComparison.OrdinalIgnoreCase))
            : default;

        if (string.IsNullOrWhiteSpace(family.Name))
            family = families.FirstOrDefault(f => f.Name.Equals("Arial", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(family.Name))
            family = families.First();

        return family.CreateFont(size, style);
    }
}
