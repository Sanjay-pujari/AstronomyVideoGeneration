using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

internal static class ObjectEdgeIntegrationService
{
    private static readonly HashSet<string> PlanetLikeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "mercury", "venus", "mars", "jupiter", "saturn", "uranus", "neptune", "moon"
    };

    public static void ApplyEdgeFeather(Image<Rgba32> image, string category, bool hero, int depth)
    {
        var featherWidth = hero ? 0.055f : 0.105f + depth * 0.012f;
        var darken = hero ? 0.055f : 0.095f;
        image.ProcessPixelRows(accessor =>
        {
            var cx = (accessor.Width - 1) / 2f;
            var cy = (accessor.Height - 1) / 2f;
            var rx = Math.Max(1, accessor.Width * 0.50f);
            var ry = Math.Max(1, accessor.Height * 0.50f);
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A == 0) continue;
                    var dx = (x - cx) / rx;
                    var dy = (y - cy) / ry;
                    var radial = MathF.Sqrt(dx * dx + dy * dy);
                    var edge = SmoothStep(1f - featherWidth, 1.015f, radial);
                    p.A = (byte)Math.Clamp(p.A * (1f - edge * (hero ? 0.34f : 0.56f)), 0, 255);
                    var edgeDarken = 1f - edge * darken;
                    p.R = (byte)Math.Clamp(p.R * edgeDarken, 0, 255);
                    p.G = (byte)Math.Clamp(p.G * edgeDarken, 0, 255);
                    p.B = (byte)Math.Clamp(p.B * (edgeDarken + (hero ? 0.012f : 0f)), 0, 255);
                    row[x] = p;
                }
            }
        });
    }

    public static void DrawAtmosphericIntegration(IImageProcessingContext ctx, Image<Rgba32> source, Point position, RectangleF rect, string category, bool hero, string mode)
    {
        using var edgeMask = source.Clone();
        var cool = new Rgba32(84, 116, 176, 255);
        edgeMask.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var a = row[x].A / 255f;
                    var leftBias = 1f - x / (float)Math.Max(1, accessor.Width - 1);
                    row[x] = new Rgba32(cool.R, cool.G, cool.B, (byte)Math.Clamp(a * (hero ? 20f : 12f) * (0.55f + leftBias * 0.35f), 0, hero ? 28 : 16));
                }
            }
        });
        edgeMask.Mutate(x => x.GaussianBlur(hero ? 4.8f : 6.5f));
        ctx.DrawImage(edgeMask, new Point(position.X - (hero ? 4 : 2), position.Y), hero ? 0.34f : 0.22f);

        if (hero && IsPlanetLike(category))
            DrawPlanetAtmosphericRim(ctx, rect, mode);
    }

    public static bool IsPlanetLike(string category) => PlanetLikeKeys.Contains(category);

    private static void DrawPlanetAtmosphericRim(IImageProcessingContext ctx, RectangleF rect, string mode)
    {
        var warm = mode is "EclipseMode" ? Color.FromRgb(255, 176, 116) : Color.FromRgb(255, 206, 146);
        var cold = Color.FromRgb(136, 172, 238);
        var warmCenter = new PointF(rect.Left + rect.Width * 0.31f, rect.Top + rect.Height * 0.32f);
        var coldCenter = new PointF(rect.Left + rect.Width * 0.72f, rect.Top + rect.Height * 0.64f);
        FillSoftEllipse(ctx, warmCenter, rect.Width * 0.52f, rect.Height * 0.42f, warm, 0.072f, 9);
        FillSoftEllipse(ctx, coldCenter, rect.Width * 0.48f, rect.Height * 0.46f, cold, 0.052f, 9);
        ctx.Fill(Color.Black.WithAlpha(0.026f), new EllipsePolygon(rect.Left + rect.Width * 0.54f, rect.Top + rect.Height * 0.56f, rect.Width * 0.50f, rect.Height * 0.42f));
    }

    private static void FillSoftEllipse(IImageProcessingContext ctx, PointF center, float radiusX, float radiusY, Color color, float maxAlpha, int rings)
    {
        for (var i = rings; i >= 1; i--)
        {
            var t = i / (float)rings;
            ctx.Fill(color.WithAlpha(maxAlpha * MathF.Pow(1f - t * 0.72f, 1.55f)), new EllipsePolygon(center.X, center.Y, radiusX * t, radiusY * t));
        }
    }

    private static float SmoothStep(float e0, float e1, float x)
    {
        var t = Math.Clamp((x - e0) / Math.Max(0.0001f, e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
