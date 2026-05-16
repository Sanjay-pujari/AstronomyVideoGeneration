using Astronomy.MediaFactory.Contracts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

internal static class SpaceFogRenderer
{
    public static SpaceFogRenderResult Render(Image<Rgba32> canvas, RectangleF heroRect, RectangleF textRect, RectangleF dateRect, RectangleF brandRect, int seed, bool portrait, string mode)
    {
        using var fog = new Image<Rgba32>(canvas.Width, canvas.Height, Color.Transparent);
        var random = new Random(seed ^ 0x5f3759df);
        var protectedRects = new[] { Inflate(textRect, 28), Inflate(dateRect, 18), Inflate(brandRect, 18), Inflate(heroRect, Math.Min(heroRect.Width, heroRect.Height) * 0.12f) };
        var wisps = portrait ? 18 : 22;
        var maxAlpha = mode is "DeepSpaceMode" or "WideSkyMode" ? 0.050f : 0.038f;

        fog.Mutate(ctx =>
        {
            for (var i = 0; i < wisps; i++)
            {
                var edgeBias = random.Next(4);
                var x = edgeBias switch
                {
                    0 => canvas.Width * (0.02f + random.NextSingle() * 0.26f),
                    1 => canvas.Width * (0.72f + random.NextSingle() * 0.26f),
                    _ => canvas.Width * (0.12f + random.NextSingle() * 0.76f)
                };
                var y = edgeBias switch
                {
                    2 => canvas.Height * (0.02f + random.NextSingle() * 0.26f),
                    3 => canvas.Height * (0.72f + random.NextSingle() * 0.24f),
                    _ => canvas.Height * (0.10f + random.NextSingle() * 0.78f)
                };
                var rx = canvas.Width * (0.12f + random.NextSingle() * 0.24f);
                var ry = canvas.Height * (0.035f + random.NextSingle() * 0.12f);
                var candidate = new RectangleF(x - rx, y - ry, rx * 2, ry * 2);
                var safety = protectedRects.Any(r => Intersects(r, candidate)) ? 0.28f : 1f;
                var color = i % 4 == 0 ? Color.FromRgb(88, 72, 132) : Color.FromRgb(54, 84, 136);
                FillSoftEllipse(ctx, new PointF(x, y), rx, ry, color, maxAlpha * safety * (0.32f + random.NextSingle() * 0.50f), 9);
            }
        });

        fog.Mutate(ctx => ctx.GaussianBlur(MathF.Max(18f, MathF.Min(canvas.Width, canvas.Height) * 0.035f)));
        SuppressProtectedAreas(fog, protectedRects, heroRect);
        canvas.Mutate(ctx => ctx.DrawImage(fog, new Point(0, 0), 1f));
        return new SpaceFogRenderResult(true, Math.Round(maxAlpha * 16d, 3), protectedRects.Length);
    }

    public static void RenderMicroStarfield(Image<Rgba32> canvas, RectangleF textRect, RectangleF dateRect, RectangleF brandRect, int seed, bool portrait)
    {
        var random = new Random(seed ^ 0x51ed270b);
        var protectedRects = new[] { Inflate(textRect, 20), Inflate(dateRect, 12), Inflate(brandRect, 14) };
        var count = portrait ? canvas.Width * canvas.Height / 5400 : canvas.Width * canvas.Height / 7600;
        canvas.Mutate(ctx =>
        {
            for (var i = 0; i < count; i++)
            {
                var layer = random.Next(3);
                var x = random.NextSingle() * canvas.Width;
                var y = random.NextSingle() * canvas.Height;
                if (protectedRects.Any(r => Contains(r, x, y)))
                    continue;
                var lowerPortraitBoost = portrait && y > canvas.Height * 0.50f ? 1.55f : 1f;
                var radius = layer switch { 0 => 0.22f, 1 => 0.34f, _ => 0.52f } + random.NextSingle() * 0.18f;
                var alpha = (0.035f + layer * 0.018f + random.NextSingle() * 0.035f) * lowerPortraitBoost;
                var color = layer == 2 ? Color.FromRgb(210, 226, 255) : Color.FromRgb(150, 176, 220);
                ctx.Fill(color.WithAlpha(Math.Clamp(alpha, 0.025f, 0.13f)), new EllipsePolygon(x, y, radius));
            }
        });
    }

    public static void ApplyCinematicColorGrade(Image<Rgba32> canvas)
    {
        canvas.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    var r = p.R / 255f; var g = p.G / 255f; var b = p.B / 255f;
                    var lum = r * 0.2126f + g * 0.7152f + b * 0.0722f;
                    var shadow = 1f - SmoothStep(0.08f, 0.46f, lum);
                    var highlight = SmoothStep(0.58f, 0.96f, lum);
                    r = r * (1f + highlight * 0.035f) - shadow * 0.010f;
                    g = g * (1f + highlight * 0.014f) + shadow * 0.002f;
                    b = b * (1f - highlight * 0.020f) + shadow * 0.030f;
                    lum = r * 0.2126f + g * 0.7152f + b * 0.0722f;
                    r = lum + (r - lum) * 0.92f;
                    g = lum + (g - lum) * 0.92f;
                    b = lum + (b - lum) * 0.92f;
                    r = SoftContrast(r); g = SoftContrast(g); b = SoftContrast(b);
                    row[x] = new Rgba32(ToByte(r), ToByte(g), ToByte(b), p.A);
                }
            }
        });
    }

    private static void SuppressProtectedAreas(Image<Rgba32> image, IReadOnlyCollection<RectangleF> protectedRects, RectangleF heroRect)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var multiplier = protectedRects.Any(r => Contains(r, x, y)) ? 0.18f : 1f;
                    if (Contains(heroRect, x, y)) multiplier = Math.Min(multiplier, 0.36f);
                    if (multiplier >= 0.99f) continue;
                    var p = row[x];
                    p.A = (byte)Math.Clamp(p.A * multiplier, 0, 255);
                    row[x] = p;
                }
            }
        });
    }

    private static void FillSoftEllipse(IImageProcessingContext ctx, PointF center, float radiusX, float radiusY, Color color, float maxAlpha, int rings)
    {
        for (var i = rings; i >= 1; i--)
        {
            var t = i / (float)rings;
            ctx.Fill(color.WithAlpha(maxAlpha * MathF.Pow(1f - t * 0.76f, 1.45f)), new EllipsePolygon(center.X, center.Y, radiusX * t, radiusY * t));
        }
    }

    private static RectangleF Inflate(RectangleF rect, float amount) => new(rect.X - amount, rect.Y - amount, rect.Width + amount * 2, rect.Height + amount * 2);
    private static bool Contains(RectangleF rect, float x, float y) => rect.Width > 0 && rect.Height > 0 && x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
    private static bool Intersects(RectangleF a, RectangleF b) => a.Width > 0 && a.Height > 0 && b.Width > 0 && b.Height > 0 && a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
    private static float SmoothStep(float e0, float e1, float x) { var t = Math.Clamp((x - e0) / Math.Max(0.0001f, e1 - e0), 0f, 1f); return t * t * (3f - 2f * t); }
    private static float SoftContrast(float v) => Math.Clamp(0.5f + (v - 0.5f) * 1.045f - (v - 0.5f) * (v - 0.5f) * (v < 0.5f ? -0.035f : 0.025f), 0f, 1f);
    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0, 255);
}

internal sealed record SpaceFogRenderResult(bool Applied, double FogBlendScore, int ProtectedRegionCount);
