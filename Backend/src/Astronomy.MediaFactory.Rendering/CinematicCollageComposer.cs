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

namespace Astronomy.MediaFactory.Rendering;

public sealed class CinematicCollageComposer : ICinematicCollageComposer
{
    private readonly ThumbnailOptions _options;

    public CinematicCollageComposer(IOptions<ThumbnailOptions> options) => _options = options.Value;

    public async Task<string> ComposeAsync(CinematicCollageRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
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
        source.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size((int)target.Width, (int)target.Height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center }));
        canvas.Mutate(ctx =>
        {
            var glow = ResolveGlow(asset.Category).WithAlpha(hero ? 0.22f : 0.13f);
            ctx.Fill(glow, new EllipsePolygon(target.X + target.Width / 2, target.Y + target.Height / 2, target.Width * (hero ? 0.60f : 0.54f), target.Height * (hero ? 0.60f : 0.54f)));
            ctx.DrawImage(source, new Point((int)target.X, (int)target.Y), hero ? 0.96f : 0.86f);
            ctx.Fill(Color.White.WithAlpha(hero ? 0.035f : 0.02f), new EllipsePolygon(target.X + target.Width * 0.38f, target.Y + target.Height * 0.34f, target.Width * 0.20f, target.Height * 0.18f));
        });
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
        var size = width * 0.30f;
        return new RectangleF(width * 0.62f, height * 0.42f, size, size);
    }

    private static void DrawTitle(IImageProcessingContext ctx, string title, int width, int height, bool portrait, string language)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;

        var maxWords = portrait ? 4 : 5;
        var text = string.Join(' ', title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(maxWords));
        var font = CreateFont(portrait ? 74 : 70, FontStyle.Bold, LocalizationResolver.IsHindi(language));
        var origin = portrait ? new PointF(width / 2f, height - 420) : new PointF(74, height - 182);
        var options = new RichTextOptions(font)
        {
            Origin = origin,
            WrappingLength = portrait ? width - 150 : width * 0.47f,
            HorizontalAlignment = portrait ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(origin.X + 5, origin.Y + 5) }, text, Color.Black.WithAlpha(0.86f));
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(origin.X + 2, origin.Y + 2) }, text, Color.FromRgb(255, 222, 130).WithAlpha(0.45f));
        ctx.DrawText(options, text, Color.White);
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
        var stars = Math.Clamp(width * height / 13000, 80, 230);
        for (var i = 0; i < stars; i++)
        {
            var x = random.NextSingle() * width;
            var y = random.NextSingle() * height;
            var r = 0.55f + random.NextSingle() * 1.25f;
            ctx.Fill(Color.White.WithAlpha(0.10f + random.NextSingle() * 0.32f), new EllipsePolygon(x, y, r));
        }
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
        var family = SystemFonts.Collection.Families.FirstOrDefault(f => preferHindi && (f.Name.Contains("Noto", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Devanagari", StringComparison.OrdinalIgnoreCase)))
            ?? SystemFonts.Collection.Families.FirstOrDefault(f => f.Name.Equals("Arial", StringComparison.OrdinalIgnoreCase))
            ?? SystemFonts.Collection.Families.First();
        return family.CreateFont(size, style);
    }
}
