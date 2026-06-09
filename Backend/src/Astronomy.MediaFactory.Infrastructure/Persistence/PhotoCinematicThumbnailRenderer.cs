using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static class PhotoCinematicThumbnailRenderer
{
    private const string HookText = "DON'T MISS\nTHIS TONIGHT";
    private const string SecondaryText = "Venus + Jupiter";
    private const string MicroText = "After Sunset";

    private static readonly IReadOnlyList<PhotoCinematicThumbnailSpec> Specs =
    [
        new("Landscape", "thumbnail-landscape.png", 1280, 720),
        new("Square", "thumbnail-square.png", 1080, 1080),
        new("Portrait", "thumbnail-portrait.png", 1080, 1920)
    ];

    public static IReadOnlyList<string> PlannedOutputFiles(string thumbnailRoot)
        => Specs.Select(spec => NormalizePath(Path.Combine(thumbnailRoot, spec.FileName))).ToArray();

    public static async Task<PhotoCinematicThumbnailRenderResult> RenderAsync(string thumbnailRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(thumbnailRoot);
        var writtenFiles = new List<string>();
        foreach (var spec in Specs)
        {
            using var image = new Image<Rgba32>(spec.Width, spec.Height, Color.ParseHex("#040617"));
            image.Mutate(ctx =>
            {
                DrawTwilightSky(ctx, spec);
                DrawStars(ctx, spec);
                DrawHorizonGlow(ctx, spec);
                DrawMountains(ctx, spec);
                DrawPlanetsAndLabels(ctx, spec);
                DrawTypography(ctx, spec);
                DrawCinematicFinish(ctx, spec);
            });

            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            var outputPath = Path.Combine(thumbnailRoot, spec.FileName);
            await image.SaveAsPngAsync(outputPath, new PngEncoder(), cancellationToken);
            writtenFiles.Add(NormalizePath(outputPath));
        }

        return new PhotoCinematicThumbnailRenderResult(true, true, writtenFiles);
    }

    private static void DrawTwilightSky(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, spec.Height), GradientRepetitionMode.None,
            new ColorStop(0f, Color.ParseHex("#02030F")),
            new ColorStop(0.34f, Color.ParseHex("#101A4C")),
            new ColorStop(0.58f, Color.ParseHex("#452769")),
            new ColorStop(0.76f, Color.ParseHex("#BD5D35")),
            new ColorStop(0.88f, Color.ParseHex("#2A1426")),
            new ColorStop(1f, Color.ParseHex("#040309"))), new RectangleF(0, 0, spec.Width, spec.Height));

        DrawGlow(ctx, new PointF(spec.Width * 0.62f, spec.HorizonY + spec.Height * 0.035f), spec.Width * 0.42f, spec.Height * 0.17f, Color.ParseHex("#FF9D3A"), 0.18f, 18);
        DrawGlow(ctx, new PointF(spec.Width * 0.76f, spec.HorizonY - spec.Height * 0.06f), spec.Width * 0.32f, spec.Height * 0.16f, Color.ParseHex("#7D55D8"), 0.12f, 16);
        DrawGlow(ctx, new PointF(spec.Width * 0.42f, spec.Height * 0.46f), spec.Width * 0.52f, spec.Height * 0.30f, Color.ParseHex("#315CB8"), 0.08f, 14);
    }

    private static void DrawStars(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec)
    {
        var random = new Random(5801 + spec.Width * 3 + spec.Height);
        var count = Math.Clamp(spec.Width * spec.Height / 3600, 180, 620);
        for (var i = 0; i < count; i++)
        {
            var x = random.NextSingle() * spec.Width;
            var y = random.NextSingle() * spec.HorizonY * 0.88f;
            var fade = Math.Clamp(1f - y / (spec.Height * 0.86f), 0.12f, 1f);
            var radius = random.NextSingle() > 0.985f ? 1.8f + random.NextSingle() * 1.4f : 0.45f + random.NextSingle() * 0.85f;
            ctx.Fill(Color.White.WithAlpha((0.24f + random.NextSingle() * 0.58f) * fade), new EllipsePolygon(x, y, radius));
        }
    }

    private static void DrawHorizonGlow(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, spec.HorizonY - spec.Height * 0.18f), new PointF(0, spec.HorizonY + spec.Height * 0.07f), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Transparent),
            new ColorStop(0.46f, Color.ParseHex("#5A274E").WithAlpha(0.35f)),
            new ColorStop(0.74f, Color.ParseHex("#F28A36").WithAlpha(0.28f)),
            new ColorStop(1f, Color.Transparent)), new RectangleF(0, spec.HorizonY - spec.Height * 0.20f, spec.Width, spec.Height * 0.28f));
    }

    private static void DrawMountains(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec)
    {
        var random = new Random(8021 + spec.Width + spec.Height * 5);
        DrawMountainLayer(ctx, spec, spec.HorizonY + spec.Height * 0.035f, spec.Height * 0.055f, Color.ParseHex("#0D0914").WithAlpha(0.88f), random);
        DrawMountainLayer(ctx, spec, spec.HorizonY + spec.Height * 0.080f, spec.Height * 0.090f, Color.ParseHex("#050407").WithAlpha(0.99f), random);
    }

    private static void DrawMountainLayer(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec, float baseY, float amplitude, Color color, Random random)
    {
        var points = new List<PointF> { new(0, spec.Height), new(0, baseY) };
        for (var i = 0; i <= 18; i++)
        {
            var x = spec.Width * i / 18f;
            var y = baseY - amplitude * (0.28f + random.NextSingle() * 0.88f) + MathF.Sin(i * 0.9f) * amplitude * 0.24f;
            points.Add(new PointF(x, y));
        }
        points.Add(new PointF(spec.Width, spec.Height));
        ctx.Fill(color, new Polygon(new LinearLineSegment(points.ToArray())));
    }

    private static void DrawPlanetsAndLabels(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec)
    {
        var venus = spec.VenusCenter;
        var jupiter = spec.JupiterCenter;

        DrawVenusStarPoint(ctx, venus, spec.PlanetScale);
        DrawJupiterPlanet(ctx, jupiter, spec.PlanetScale);

        var labelFont = ResolveFont(spec.LabelFontSize, FontStyle.Bold);
        DrawCleanLabel(ctx, "Venus", labelFont, spec.VenusLabelOrigin, venus, Color.ParseHex("#FFF1A8"));
        DrawCleanLabel(ctx, "Jupiter", labelFont, spec.JupiterLabelOrigin, jupiter, Color.ParseHex("#D8EBFF"));
    }

    private static void DrawVenusStarPoint(IImageProcessingContext ctx, PointF center, float scale)
    {
        var glowScale = MathF.Sqrt(scale);
        DrawGlow(ctx, center, 22f * glowScale, 22f * glowScale, Color.ParseHex("#FFF3AA"), 0.16f, 12);
        DrawGlow(ctx, center, 9f * glowScale, 9f * glowScale, Color.White, 0.28f, 7);
        ctx.Fill(Color.White.WithAlpha(0.96f), new EllipsePolygon(center.X, center.Y, 3.6f * glowScale));
        ctx.Fill(Color.ParseHex("#FFF2A5"), new EllipsePolygon(center.X, center.Y, 1.9f * glowScale));
    }

    private static void DrawJupiterPlanet(IImageProcessingContext ctx, PointF center, float scale)
    {
        var radius = 26f * scale;
        var radiusY = radius * 0.84f;
        DrawGlow(ctx, center, radius * 1.72f, radius * 1.30f, Color.ParseHex("#D9B077"), 0.09f, 10);
        ctx.Fill(Color.ParseHex("#D7B07A"), new EllipsePolygon(center.X, center.Y, radius, radiusY));

        DrawJupiterBand(ctx, center, radius, radiusY, -0.48f, 0.62f, Color.ParseHex("#F2D6A8"), 0.30f);
        DrawJupiterBand(ctx, center, radius, radiusY, -0.30f, 0.80f, Color.ParseHex("#9E6844"), 0.30f);
        DrawJupiterBand(ctx, center, radius, radiusY, -0.13f, 0.93f, Color.ParseHex("#E9C492"), 0.34f);
        DrawJupiterBand(ctx, center, radius, radiusY, 0.02f, 0.97f, Color.ParseHex("#A46B45"), 0.24f);
        DrawJupiterBand(ctx, center, radius, radiusY, 0.22f, 0.85f, Color.ParseHex("#F0D2A2"), 0.30f);
        DrawJupiterBand(ctx, center, radius, radiusY, 0.40f, 0.68f, Color.ParseHex("#8E5B3E"), 0.22f);

        ctx.Fill(Color.ParseHex("#B65F42").WithAlpha(0.46f), new EllipsePolygon(center.X + radius * 0.35f, center.Y + radiusY * 0.18f, radius * 0.15f, radiusY * 0.075f));
        ctx.Fill(Color.White.WithAlpha(0.19f), new EllipsePolygon(center.X - radius * 0.27f, center.Y - radiusY * 0.34f, radius * 0.24f, radiusY * 0.12f));
        ctx.Fill(Color.Black.WithAlpha(0.09f), new EllipsePolygon(center.X + radius * 0.22f, center.Y + radiusY * 0.05f, radius * 0.76f, radiusY * 0.86f));
        ctx.Draw(Color.White.WithAlpha(0.34f), Math.Max(1.1f, radius * 0.045f), new EllipsePolygon(center.X, center.Y, radius, radiusY));
    }

    private static void DrawJupiterBand(IImageProcessingContext ctx, PointF center, float radius, float radiusY, float offset, float widthFactor, Color color, float alpha)
    {
        var y = center.Y + radiusY * offset;
        var bandWidth = radius * widthFactor * MathF.Sqrt(Math.Clamp(1f - offset * offset, 0.12f, 1f));
        ctx.Fill(color.WithAlpha(alpha), new EllipsePolygon(center.X, y, bandWidth, Math.Max(1.2f, radiusY * 0.045f)));
    }

    private static void DrawCleanLabel(IImageProcessingContext ctx, string label, Font font, PointF origin, PointF target, Color color)
    {
        var elbow = new PointF(target.X + (origin.X < target.X ? -32f : 32f), target.Y + (origin.Y < target.Y ? -20f : 20f));
        var lineEnd = ShortenToward(target, elbow, 14f);
        ctx.DrawLine(Color.White.WithAlpha(0.68f), 1.6f, origin, elbow, lineEnd);
        ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(origin.X + 1.5f, origin.Y + 1.5f) }, label, Color.Black.WithAlpha(0.58f));
        ctx.DrawText(new RichTextOptions(font) { Origin = origin }, label, color);
    }

    private static PointF ShortenToward(PointF target, PointF from, float distance)
    {
        var dx = from.X - target.X;
        var dy = from.Y - target.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001f) return target;
        return new PointF(target.X + dx / length * distance, target.Y + dy / length * distance);
    }

    private static void DrawTypography(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec)
    {
        var hookFont = ResolveFont(spec.HookFontSize, FontStyle.BoldItalic);
        var secondaryFont = ResolveFont(spec.SecondaryFontSize, FontStyle.Bold);
        var microFont = ResolveFont(spec.MicroFontSize, FontStyle.Bold);
        DrawTextWithShadow(ctx, HookText, hookFont, spec.HookOrigin, Color.White, 0.86f);
        DrawTextWithShadow(ctx, SecondaryText, secondaryFont, spec.SecondaryOrigin, Color.ParseHex("#FFD15E"), 0.92f);
        DrawTextWithShadow(ctx, MicroText, microFont, spec.MicroOrigin, Color.ParseHex("#7FD6FF"), 0.90f);
    }

    private static void DrawTextWithShadow(IImageProcessingContext ctx, string text, Font font, PointF origin, Color color, float lineSpacing)
    {
        var options = new RichTextOptions(font) { Origin = origin, LineSpacing = lineSpacing };
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(origin.X + 5f, origin.Y + 5f) }, text, Color.Black.WithAlpha(0.74f));
        ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(origin.X + 2f, origin.Y + 2f) }, text, Color.Black.WithAlpha(0.46f));
        ctx.DrawText(options, text, color);
    }

    private static void DrawCinematicFinish(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(spec.Width, 0), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Black.WithAlpha(spec.Height > spec.Width ? 0.08f : 0.20f)),
            new ColorStop(0.48f, Color.Transparent),
            new ColorStop(1f, Color.Black.WithAlpha(0.08f))), new RectangleF(0, 0, spec.Width, spec.Height));
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, spec.Height), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Black.WithAlpha(0.10f)),
            new ColorStop(0.74f, Color.Transparent),
            new ColorStop(1f, Color.Black.WithAlpha(0.32f))), new RectangleF(0, 0, spec.Width, spec.Height));
    }


    private static void DrawGlow(IImageProcessingContext ctx, PointF center, float radiusX, float radiusY, Color color, float alpha, int rings)
    {
        for (var i = rings; i >= 1; i--)
        {
            var t = i / (float)rings;
            ctx.Fill(color.WithAlpha(alpha * MathF.Pow(1f - t * 0.72f, 1.38f)), new EllipsePolygon(center.X, center.Y, radiusX * t, radiusY * t));
        }
    }

    private static Font ResolveFont(float size, FontStyle style)
    {
        foreach (var name in new[] { "Arial Narrow", "Roboto Condensed", "Oswald", "Bebas Neue", "Impact", "Inter", "DejaVu Sans Condensed", "Arial", "DejaVu Sans" })
        {
            if (SystemFonts.TryGet(name, out var family)) return family.CreateFont(size, style);
        }

        var fallbackFamily = SystemFonts.Collection.Families.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fallbackFamily.Name))
            throw new InvalidOperationException("No system fonts available for photo-cinematic thumbnail image generation.");

        return fallbackFamily.CreateFont(size, style);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    public sealed record PhotoCinematicThumbnailRenderResult(bool Entered, bool Completed, IReadOnlyList<string> WrittenFiles);

    private sealed record PhotoCinematicThumbnailSpec(string Variant, string FileName, int Width, int Height)
    {
        public bool IsPortrait => Height > Width;
        public bool IsSquare => Width == Height;
        public float HorizonY => IsPortrait ? Height * 0.66f : IsSquare ? Height * 0.70f : Height * 0.72f;
        public float PlanetScale => IsPortrait ? 1.85f : IsSquare ? 1.35f : 1.05f;
        public float HookFontSize => IsPortrait ? 116f : IsSquare ? 88f : 82f;
        public float SecondaryFontSize => IsPortrait ? 58f : IsSquare ? 44f : 40f;
        public float MicroFontSize => IsPortrait ? 42f : IsSquare ? 34f : 28f;
        public float LabelFontSize => IsPortrait ? 28f : IsSquare ? 24f : 21f;
        public PointF HookOrigin => IsPortrait ? new PointF(70, 100) : IsSquare ? new PointF(62, 70) : new PointF(58, 56);
        public PointF SecondaryOrigin => IsPortrait ? new PointF(76, 360) : IsSquare ? new PointF(72, 250) : new PointF(72, 250);
        public PointF MicroOrigin => IsPortrait ? new PointF(80, 430) : IsSquare ? new PointF(76, 306) : new PointF(76, 302);
        public PointF VenusCenter => IsPortrait ? new PointF(Width * 0.40f, Height * 0.43f) : IsSquare ? new PointF(Width * 0.57f, Height * 0.43f) : new PointF(Width * 0.70f, Height * 0.40f);
        public PointF JupiterCenter => IsPortrait ? new PointF(Width * 0.63f, Height * 0.50f) : IsSquare ? new PointF(Width * 0.76f, Height * 0.49f) : new PointF(Width * 0.84f, Height * 0.48f);
        public PointF VenusLabelOrigin => IsPortrait ? new PointF(Width * 0.19f, Height * 0.48f) : IsSquare ? new PointF(Width * 0.39f, Height * 0.36f) : new PointF(Width * 0.58f, Height * 0.32f);
        public PointF JupiterLabelOrigin => IsPortrait ? new PointF(Width * 0.68f, Height * 0.55f) : IsSquare ? new PointF(Width * 0.69f, Height * 0.57f) : new PointF(Width * 0.74f, Height * 0.57f);
    }
}
