using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

internal static class ProceduralCelestialFallback
{
    public static async Task CreateAsync(string outputPath, string objectName, string category, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = new Image<Rgba32>(1600, 1000, Color.FromRgb(2, 5, 18));
        var seed = Math.Abs(HashCode.Combine(objectName.ToLowerInvariant(), category));
        var random = new Random(seed);
        image.Mutate(ctx =>
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(1600, 1000), GradientRepetitionMode.None,
                new ColorStop(0, Color.FromRgb(2, 5, 18)),
                new ColorStop(0.55f, Color.FromRgb(8, 17, 48)),
                new ColorStop(1, Color.FromRgb(0, 1, 8))), new RectangleF(0, 0, 1600, 1000));

            for (var i = 0; i < 360; i++)
            {
                var x = random.NextSingle() * 1600;
                var y = random.NextSingle() * 1000;
                var r = 0.6f + random.NextSingle() * 1.8f;
                ctx.Fill(Color.White.WithAlpha(0.18f + random.NextSingle() * 0.55f), new EllipsePolygon(x, y, r));
            }

            var objectColor = ResolveColor(category);
            if (category.Contains("meteor", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 0; i < 4; i++)
                {
                    var x = 340 + i * 230;
                    var y = 170 + i * 80;
                    ctx.DrawLine(Color.White.WithAlpha(0.78f), 5, new PointF(x, y), new PointF(x + 300, y + 112));
                    ctx.DrawLine(objectColor.WithAlpha(0.36f), 14, new PointF(x - 25, y - 8), new PointF(x + 315, y + 118));
                }
                return;
            }

            var cx = category is "venus" or "moon" ? 820 : 980;
            var cy = category.EndsWith("eclipse", StringComparison.OrdinalIgnoreCase) ? 470 : 430;
            var radius = category is "saturn" ? 230 : 250;
            ctx.Fill(objectColor.WithAlpha(0.18f), new EllipsePolygon(cx, cy, radius + 85));
            if (category is "saturn")
            {
                ctx.DrawLine(Color.FromRgb(230, 210, 170).WithAlpha(0.70f), 30, new PointF(cx - 370, cy + 45), new PointF(cx + 370, cy - 45));
                ctx.DrawLine(Color.FromRgb(245, 232, 190).WithAlpha(0.45f), 12, new PointF(cx - 390, cy + 70), new PointF(cx + 390, cy - 70));
            }
            ctx.Fill(objectColor, new EllipsePolygon(cx, cy, radius));
            ctx.Fill(Color.White.WithAlpha(0.18f), new EllipsePolygon(cx - radius * 0.32f, cy - radius * 0.35f, radius * 0.42f));
            if (category is "jupiter")
            {
                ctx.DrawLine(Color.FromRgb(190, 130, 80).WithAlpha(0.42f), 46, new PointF(cx - 225, cy - 60), new PointF(cx + 225, cy - 60));
                ctx.DrawLine(Color.FromRgb(245, 218, 160).WithAlpha(0.38f), 34, new PointF(cx - 220, cy + 55), new PointF(cx + 220, cy + 55));
                ctx.Fill(Color.FromRgb(150, 62, 46).WithAlpha(0.76f), new EllipsePolygon(cx + 95, cy + 48, 42, 26));
            }
        });
        await image.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = 90 }, cancellationToken);
    }

    private static Color ResolveColor(string category) => category switch
    {
        "jupiter" => Color.FromRgb(215, 166, 105),
        "saturn" => Color.FromRgb(220, 198, 148),
        "mars" => Color.FromRgb(204, 95, 55),
        "venus" => Color.FromRgb(238, 218, 168),
        "moon" or "lunar-eclipse" => Color.FromRgb(210, 210, 198),
        "mercury" => Color.FromRgb(178, 170, 158),
        "uranus" => Color.FromRgb(126, 210, 215),
        "neptune" => Color.FromRgb(64, 112, 220),
        "solar-eclipse" => Color.FromRgb(250, 220, 150),
        _ => Color.FromRgb(120, 150, 230)
    };
}
