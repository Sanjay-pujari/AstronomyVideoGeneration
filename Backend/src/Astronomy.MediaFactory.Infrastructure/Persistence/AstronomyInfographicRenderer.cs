using Astronomy.MediaFactory.Core;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyInfographicRenderer(
    AstronomyBackgroundLayerRenderer backgroundLayer,
    CelestialObjectLayerRenderer celestialObjectLayer,
    SkyGuidanceLayerRenderer skyGuidanceLayer,
    EducationalLayerRenderer educationalLayer,
    AnnotationLayerRenderer annotationLayer) : IAstronomyInfographicRenderer
{
    public async Task RenderAsync(string finalPath, QuestionDrivenVisualSpec spec, string venusAssetPath, string jupiterAssetPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!File.Exists(venusAssetPath)) throw new ArgumentException($"Required local Venus asset was not found at '{venusAssetPath}'.", nameof(venusAssetPath));
        if (!File.Exists(jupiterAssetPath)) throw new ArgumentException($"Required local Jupiter asset was not found at '{jupiterAssetPath}'.", nameof(jupiterAssetPath));

        using var image = new Image<Rgba32>(1920, 1080, Color.ParseHex("#061124"));
        var fonts = EditorialFonts.Create();
        image.Mutate(ctx =>
        {
            backgroundLayer.Render(ctx, spec);
            skyGuidanceLayer.Render(ctx, spec, fonts);
            celestialObjectLayer.Render(ctx, spec, venusAssetPath, jupiterAssetPath);
            educationalLayer.Render(ctx, spec, fonts);
            annotationLayer.Render(ctx, spec, fonts);
            backgroundLayer.RenderVignette(ctx);
        });

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? ".");
        await image.SaveAsPngAsync(finalPath, new PngEncoder(), cancellationToken);
    }
}

public sealed class AstronomyBackgroundLayerRenderer
{
    private const int CanvasWidth = 1920;
    private const int CanvasHeight = 1080;

    public void Render(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec)
    {
        var sceneNumber = spec.SceneNumber;
        RenderSmoothSky(ctx, sceneNumber);
        RenderStars(ctx, sceneNumber);
        RenderLandscape(ctx, sceneNumber);
    }

    public void RenderVignette(IImageProcessingContext ctx)
    {
        using var vignette = new Image<Rgba32>(CanvasWidth, CanvasHeight, Color.Transparent);
        vignette.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var vertical = SmoothStep(0f, 0.10f, y / (float)(CanvasHeight - 1));
                var bottom = 1f - SmoothStep(0.90f, 1f, y / (float)(CanvasHeight - 1));

                for (var x = 0; x < row.Length; x++)
                {
                    var horizontal = SmoothStep(0f, 0.08f, x / (float)(CanvasWidth - 1));
                    var right = 1f - SmoothStep(0.92f, 1f, x / (float)(CanvasWidth - 1));
                    var edge = MathF.Max(MathF.Max(1f - vertical, 1f - bottom), MathF.Max(1f - horizontal, 1f - right));
                    var alpha = (byte)Math.Clamp(edge * 30f, 0f, 30f);
                    row[x] = new Rgba32(0, 0, 0, alpha);
                }
            }
        });
        ctx.DrawImage(vignette, 1f);
    }

    private static void RenderSmoothSky(IImageProcessingContext ctx, int sceneNumber)
    {
        var stops = GetSkyStops(sceneNumber);
        var random = new Random(18400 + sceneNumber);
        var horizonHazeColor = Color.ParseHex("#FFF1C4").ToPixel<Rgba32>();
        var upperHazeColor = Color.ParseHex("#8FD2FF").ToPixel<Rgba32>();
        var glowColor = Color.ParseHex(sceneNumber is 1 or 3 or 6 ? "#FF9A45" : "#B7E0FF").ToPixel<Rgba32>();
        using var sky = new Image<Rgba32>(CanvasWidth, CanvasHeight);

        sky.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var yRatio = y / (float)(CanvasHeight - 1);
                var baseColor = InterpolateStops(stops, yRatio);
                var horizonLift = SmoothStep(0.58f, 0.92f, yRatio) * (1f - SmoothStep(0.92f, 1f, yRatio));
                var coolUpperHaze = 1f - SmoothStep(0.18f, 0.72f, Math.Abs(yRatio - 0.44f));
                var warmHorizon = sceneNumber is 1 or 3 or 6 ? 1f : 0.35f;
                var row = accessor.GetRowSpan(y);

                for (var x = 0; x < row.Length; x++)
                {
                    var xRatio = x / (float)(CanvasWidth - 1);
                    var color = baseColor;
                    color = Composite(color, horizonHazeColor, horizonLift * warmHorizon * 0.10f);
                    color = Composite(color, upperHazeColor, coolUpperHaze * 0.018f);

                    var glowFalloff = 1f - Math.Clamp(MathF.Abs(xRatio - 0.52f) / 0.58f, 0f, 1f);
                    var glow = horizonLift * SmoothStep(0f, 1f, glowFalloff) * (sceneNumber is 1 or 3 or 6 ? 0.11f : 0.032f);
                    color = Composite(color, glowColor, glow);

                    var dither = random.Next(-4, 5);
                    row[x] = new Rgba32(
                        ClampByte(color.R + dither),
                        ClampByte(color.G + dither),
                        ClampByte(color.B + dither),
                        255);
                }
            }
        });

        ctx.DrawImage(sky, 1f);
    }

    private static SkyColorStop[] GetSkyStops(int sceneNumber) => sceneNumber switch
    {
        1 =>
        [
            new(0f, Color.ParseHex("#06101F")),
            new(.42f, Color.ParseHex("#172A4D")),
            new(.70f, Color.ParseHex("#4D4F6A")),
            new(.88f, Color.ParseHex("#D87848")),
            new(1f, Color.ParseHex("#F28D45"))
        ],
        2 =>
        [
            new(0f, Color.ParseHex("#06182E")),
            new(.54f, Color.ParseHex("#102B48")),
            new(.82f, Color.ParseHex("#203B47")),
            new(1f, Color.ParseHex("#293A3B"))
        ],
        3 =>
        [
            new(0f, Color.ParseHex("#142042")),
            new(.40f, Color.ParseHex("#3A3B64")),
            new(.67f, Color.ParseHex("#756080")),
            new(.86f, Color.ParseHex("#D67B55")),
            new(1f, Color.ParseHex("#FF9F52"))
        ],
        4 =>
        [
            new(0f, Color.ParseHex("#041827")),
            new(.55f, Color.ParseHex("#12364D")),
            new(.82f, Color.ParseHex("#24434A")),
            new(1f, Color.ParseHex("#35433E"))
        ],
        5 =>
        [
            new(0f, Color.ParseHex("#01050E")),
            new(.48f, Color.ParseHex("#07183C")),
            new(.78f, Color.ParseHex("#0B1C3B")),
            new(1f, Color.ParseHex("#101E34"))
        ],
        6 =>
        [
            new(0f, Color.ParseHex("#030A1A")),
            new(.36f, Color.ParseHex("#151F3E")),
            new(.64f, Color.ParseHex("#414469")),
            new(.84f, Color.ParseHex("#BE644C")),
            new(1f, Color.ParseHex("#ED7B45"))
        ],
        _ =>
        [
            new(0f, Color.ParseHex("#061124")),
            new(.58f, Color.ParseHex("#163158")),
            new(.84f, Color.ParseHex("#223A48")),
            new(1f, Color.ParseHex("#293C3F"))
        ]
    };

    private static Rgba32 InterpolateStops(IReadOnlyList<SkyColorStop> stops, float position)
    {
        if (position <= stops[0].Position) return stops[0].Color.ToPixel<Rgba32>();
        for (var i = 1; i < stops.Count; i++)
        {
            if (position > stops[i].Position) continue;
            var previous = stops[i - 1];
            var next = stops[i];
            var local = SmoothStep(0f, 1f, (position - previous.Position) / (next.Position - previous.Position));
            return Blend(previous.Color, next.Color, local).ToPixel<Rgba32>();
        }

        return stops[^1].Color.ToPixel<Rgba32>();
    }

    private static Rgba32 Composite(Rgba32 destination, Rgba32 source, float alpha)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        return new Rgba32(
            ClampByte(destination.R + (source.R - destination.R) * alpha),
            ClampByte(destination.G + (source.G - destination.G) * alpha),
            ClampByte(destination.B + (source.B - destination.B) * alpha),
            255);
    }

    private static void DrawSoftHorizonGlow(IImageProcessingContext ctx, float centerY, float height, string color, float maxAlpha)
    {
        using var glow = new Image<Rgba32>(CanvasWidth, CanvasHeight, Color.Transparent);
        var glowColor = Color.ParseHex(color).ToPixel<Rgba32>();
        glow.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var dy = (y - centerY) / Math.Max(1f, height * 0.5f);
                var vertical = MathF.Exp(-dy * dy * 2.2f);
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var xRatio = x / (float)(CanvasWidth - 1);
                    var horizontal = 0.72f + 0.28f * (1f - Math.Clamp(MathF.Abs(xRatio - 0.52f) / 0.58f, 0f, 1f));
                    row[x] = new Rgba32(glowColor.R, glowColor.G, glowColor.B, (byte)Math.Clamp(maxAlpha * vertical * horizontal * 255f, 0f, 255f));
                }
            }
        });
        ctx.DrawImage(glow, 1f);
    }


    private static void RenderStars(IImageProcessingContext ctx, int sceneNumber)
    {
        var stars = sceneNumber switch
        {
            2 => new[] { new PointF(520, 275), new PointF(675, 360), new PointF(810, 295), new PointF(1005, 386), new PointF(1225, 315), new PointF(1420, 430), new PointF(1515, 265), new PointF(610, 610), new PointF(1350, 650) },
            5 => new[] { new PointF(300, 145), new PointF(520, 265), new PointF(770, 155), new PointF(1210, 220), new PointF(1515, 125), new PointF(1710, 310), new PointF(420, 430), new PointF(1445, 475), new PointF(980, 150) },
            _ => new[] { new PointF(250, 150), new PointF(475, 250), new PointF(745, 120), new PointF(990, 205), new PointF(1320, 145), new PointF(1610, 260), new PointF(1780, 95), new PointF(1185, 330), new PointF(380, 370), new PointF(1540, 360), new PointF(720, 315) }
        };
        foreach (var star in stars) ctx.Fill(Color.White.WithAlpha(sceneNumber is 1 or 6 ? .34f : .58f), new EllipsePolygon(star.X, star.Y, sceneNumber is 2 ? 2.2f : 1.5f));
        if (sceneNumber == 2) ctx.Draw(Color.White.WithAlpha(.18f), 2, new PathBuilder().AddLine(stars[0], stars[1]).AddLine(stars[1], stars[2]).AddLine(stars[2], stars[3]).AddLine(stars[3], stars[4]).Build());
    }

    private static void RenderLandscape(IImageProcessingContext ctx, int sceneNumber)
    {
        switch (sceneNumber)
        {
            case 1:
                DrawSoftHorizonGlow(ctx, 830, 180, "#FF9A45", .16f);
                DrawDunes(ctx, 812, "#111318", "#1B171A");
                ctx.Fill(Color.Black.WithAlpha(.38f), new RectangleF(0, 914, CanvasWidth, 166));
                break;
            case 2:
                ctx.Fill(Color.ParseHex("#14202A").WithAlpha(.92f), new RectangleF(0, 810, CanvasWidth, 270));
                ctx.Draw(Color.ParseHex("#B7E0FF").WithAlpha(.64f), 4, new PathBuilder().AddLine(new PointF(360, 760), new PointF(1600, 760)).Build());
                ctx.Draw(Color.ParseHex("#F6C177").WithAlpha(.45f), 3, new PathBuilder().AddLine(new PointF(360, 810), new PointF(1600, 810)).Build());
                break;
            case 3:
                DrawSoftHorizonGlow(ctx, 830, 145, "#FFAA5D", .12f);
                ctx.Fill(Color.ParseHex("#1A222A").WithAlpha(.90f), new RectangleF(0, 852, CanvasWidth, 228));
                ctx.Draw(Color.ParseHex("#FFAA5D").WithAlpha(.50f), 3, new PathBuilder().AddLine(new PointF(0, 832), new PointF(1920, 832)).Build());
                break;
            case 4:
                DrawSoftHorizonGlow(ctx, 800, 120, "#F6C177", .10f);
                DrawLowHorizon(ctx, 804, "#0D1A1E");
                ctx.Draw(Color.ParseHex("#F6C177").WithAlpha(.50f), 3, new PathBuilder().AddLine(new PointF(0, 804), new PointF(1920, 804)).Build());
                break;
            case 5:
                ctx.Fill(Color.ParseHex("#0A101A").WithAlpha(.90f), new RectangleF(0, 930, CanvasWidth, 150));
                break;
            case 6:
                DrawSoftHorizonGlow(ctx, 830, 210, "#FF8A3D", .14f);
                DrawSoftHorizonGlow(ctx, 900, 180, "#F6C177", .12f);
                DrawDunes(ctx, 835, "#10151C", "#161820");
                ctx.Fill(Color.Black.WithAlpha(.42f), new RectangleF(0, 968, CanvasWidth, 112));
                break;
        }
    }

    private static void DrawDunes(IImageProcessingContext ctx, float horizon, string nearColor, string farColor)
    {
        var far = new PathBuilder().AddLine(new PointF(0, horizon + 40), new PointF(270, horizon + 5)).AddLine(new PointF(270, horizon + 5), new PointF(615, horizon + 58)).AddLine(new PointF(615, horizon + 58), new PointF(1025, horizon - 10)).AddLine(new PointF(1025, horizon - 10), new PointF(1415, horizon + 34)).AddLine(new PointF(1415, horizon + 34), new PointF(CanvasWidth, horizon - 5)).AddLine(new PointF(CanvasWidth, horizon - 5), new PointF(CanvasWidth, CanvasHeight)).AddLine(new PointF(CanvasWidth, CanvasHeight), new PointF(0, CanvasHeight)).CloseFigure().Build();
        ctx.Fill(Color.ParseHex(farColor).WithAlpha(.94f), far);
        DrawLowHorizon(ctx, horizon + 88, nearColor);
    }

    private static void DrawLowHorizon(IImageProcessingContext ctx, float horizon, string color)
    {
        var path = new PathBuilder().AddLine(new PointF(0, horizon), new PointF(320, horizon + 24)).AddLine(new PointF(320, horizon + 24), new PointF(720, horizon - 12)).AddLine(new PointF(720, horizon - 12), new PointF(1190, horizon + 18)).AddLine(new PointF(1190, horizon + 18), new PointF(CanvasWidth, horizon - 8)).AddLine(new PointF(CanvasWidth, horizon - 8), new PointF(CanvasWidth, CanvasHeight)).AddLine(new PointF(CanvasWidth, CanvasHeight), new PointF(0, CanvasHeight)).CloseFigure().Build();
        ctx.Fill(Color.ParseHex(color).WithAlpha(.96f), path);
    }

    private static Color Blend(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var ap = a.ToPixel<Rgba32>();
        var bp = b.ToPixel<Rgba32>();
        return Color.FromRgb(ClampByte(ap.R + (bp.R - ap.R) * amount), ClampByte(ap.G + (bp.G - ap.G) * amount), ClampByte(ap.B + (bp.B - ap.B) * amount));
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        var x = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    private static byte ClampByte(float value) => (byte)Math.Clamp(MathF.Round(value), 0f, 255f);

    private readonly record struct SkyColorStop(float Position, Color Color);
}

public sealed class CelestialObjectLayerRenderer
{
    public void Render(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, string venusAssetPath, string jupiterAssetPath)
    {
        if (spec.QuestionType.Equals("when", StringComparison.OrdinalIgnoreCase)) return;
        var positions = PlanetLayout.GetPlacements(spec.QuestionType);
        DrawAsset(ctx, venusAssetPath, positions.Venus.Center, positions.Venus.Diameter, "#FFF2B8");
        DrawAsset(ctx, jupiterAssetPath, positions.Jupiter.Center, positions.Jupiter.Diameter, "#E5C18D");
    }

    private static void DrawAsset(IImageProcessingContext ctx, string assetPath, PointF center, int diameter, string glowColor)
    {
        // Soft alpha glow only. Do not paint an opaque or dark circular backing behind the transparent asset.
        ctx.Fill(Color.ParseHex(glowColor).WithAlpha(.030f), new EllipsePolygon(center.X, center.Y, diameter * .82f));
        ctx.Fill(Color.ParseHex(glowColor).WithAlpha(.052f), new EllipsePolygon(center.X, center.Y, diameter * .58f));
        using var asset = Image.Load<Rgba32>(assetPath);
        asset.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(diameter, diameter), Mode = ResizeMode.Max }));
        ctx.DrawImage(asset, new Point((int)(center.X - asset.Width / 2f), (int)(center.Y - asset.Height / 2f)), .86f);
    }
}

internal static class PlanetLayout
{
    public static PlanetPairPlacement GetPlacements(string questionType) => questionType.ToLowerInvariant() switch
    {
        "what" => new(new(new PointF(1220, 360), 91), new(new PointF(1410, 410), 62)),
        "where" => new(new(new PointF(1060, 505), 60), new(new PointF(1255, 545), 44)),
        "how" => new(new(new PointF(950, 430), 73), new(new PointF(1195, 470), 49)),
        "why" => new(new(new PointF(890, 430), 83), new(new PointF(1105, 455), 64)),
        "action" => new(new(new PointF(1010, 390), 72), new(new PointF(1165, 430), 51)),
        _ => new(new(new PointF(-100, -100), 1), new(new PointF(-100, -100), 1))
    };
}

internal readonly record struct PlanetPlacement(PointF Center, int Diameter)
{
    public RectangleF Bounds => new(Center.X - Diameter / 2f, Center.Y - Diameter / 2f, Diameter, Diameter);
}

internal readonly record struct PlanetPairPlacement(PlanetPlacement Venus, PlanetPlacement Jupiter);

public sealed class SkyGuidanceLayerRenderer
{
    public void Render(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, EditorialFonts fonts)
    {
        switch (spec.QuestionType.ToLowerInvariant())
        {
            case "where":
                DrawSkyGrid(ctx);
                DrawReferenceConstellation(ctx, fonts.SmallFont, subtle: false);
                DrawWestMarker(ctx, new PointF(245, 760), fonts.LabelFont);
                Text(ctx, "Western horizon", fonts.SmallFont, 820, 785, Color.ParseHex("#B7E0FF"), 300);
                Text(ctx, "altitude guide", fonts.SmallFont, 450, 304, Color.ParseHex("#B7E0FF"), 240);
                break;
            case "how":
                DrawReferenceConstellation(ctx, fonts.SmallFont, subtle: true);
                DrawWestMarker(ctx, new PointF(230, 780), fonts.LabelFont);
                DrawArrow(ctx, new PointF(1015, 440), new PointF(1150, 465), Color.ParseHex("#8FD2FF"));
                break;
            case "why":
                DrawClosenessBracket(ctx, PlanetLayout.GetPlacements(spec.QuestionType), fonts.SmallFont);
                break;
        }
    }

    private static void DrawSkyGrid(IImageProcessingContext ctx)
    {
        for (var x = 420; x <= 1540; x += 140) ctx.Draw(Color.White.WithAlpha(.10f), 1, new PathBuilder().AddLine(new PointF(x, 240), new PointF(x, 760)).Build());
        for (var y = 300; y <= 720; y += 105) ctx.Draw(Color.White.WithAlpha(.10f), 1, new PathBuilder().AddLine(new PointF(420, y), new PointF(1540, y)).Build());
        ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(.55f), 3, new PathBuilder().AddLine(new PointF(420, 760), new PointF(1540, 760)).Build());
        ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(.35f), 2, new PathBuilder().AddLine(new PointF(420, 760), new PointF(420, 250)).Build());
    }

    private static void DrawReferenceConstellation(IImageProcessingContext ctx, Font font, bool subtle)
    {
        var points = subtle
            ? new[] { new PointF(1360, 235), new PointF(1450, 282), new PointF(1532, 252), new PointF(1610, 315) }
            : new[] { new PointF(555, 330), new PointF(665, 382), new PointF(780, 345), new PointF(910, 425), new PointF(1045, 365) };
        var starAlpha = subtle ? .36f : .72f;
        var lineAlpha = subtle ? .10f : .18f;
        for (var i = 0; i < points.Length; i++)
        {
            ctx.Fill(Color.White.WithAlpha(starAlpha), new EllipsePolygon(points[i].X, points[i].Y, subtle ? 2.4f : 4f));
            if (i > 0) ctx.Draw(Color.White.WithAlpha(lineAlpha), subtle ? 1 : 2, new PathBuilder().AddLine(points[i - 1], points[i]).Build());
        }
        var label = subtle ? "guide stars" : "Leo / Regulus guide";
        var p = subtle ? new PointF(1350, 336) : new PointF(585, 410);
        Text(ctx, label, font, p.X, p.Y, Color.White.WithAlpha(subtle ? .36f : .58f), 280);
    }

    private static void DrawWestMarker(IImageProcessingContext ctx, PointF p, Font font)
    {
        ctx.Draw(Color.ParseHex("#F6C177"), 5, new PathBuilder().AddLine(new PointF(p.X + 210, p.Y), p).Build());
        ctx.Fill(Color.ParseHex("#F6C177"), new EllipsePolygon(p.X, p.Y, 12));
        Text(ctx, "W", font, p.X - 20, p.Y - 58, Color.ParseHex("#F6C177"), 70);
    }

    private static void DrawArrow(IImageProcessingContext ctx, PointF from, PointF to, Color color)
    {
        ctx.Draw(color, 5, new PathBuilder().AddLine(from, to).Build());
        ctx.Fill(color, new EllipsePolygon(to.X, to.Y, 9));
    }

    private static void DrawClosenessBracket(IImageProcessingContext ctx, PlanetPairPlacement planets, Font font)
    {
        var leftEdge = planets.Venus.Center.X + planets.Venus.Diameter / 2f + 18;
        var rightEdge = planets.Jupiter.Center.X - planets.Jupiter.Diameter / 2f - 18;
        var y = Math.Min(planets.Venus.Center.Y, planets.Jupiter.Center.Y) - 88;
        var bracket = new PathBuilder()
            .AddLine(new PointF(leftEdge, y + 24), new PointF(leftEdge, y))
            .AddLine(new PointF(leftEdge, y), new PointF(rightEdge, y))
            .AddLine(new PointF(rightEdge, y), new PointF(rightEdge, y + 24))
            .Build();
        ctx.Draw(Color.ParseHex("#F6C177"), 4, bracket);
        Text(ctx, "close together", font, leftEdge + 18, y - 36, Color.ParseHex("#F6C177"), 240);
    }

    private static void Text(IImageProcessingContext ctx, string text, Font font, float x, float y, Color color, float wrap) => ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(x, y), WrappingLength = wrap }, text, color);
}

public sealed class EducationalLayerRenderer
{
    public void Render(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, EditorialFonts fonts)
    {
        switch (spec.QuestionType.ToLowerInvariant())
        {
            case "when":
                DrawTimingWindow(ctx, fonts.TitleFont, fonts.SubtitleFont, fonts.SmallFont);
                break;
            case "how":
                DrawGuideSteps(ctx, fonts.SubtitleFont);
                break;
            case "why":
                DrawComparisonStrip(ctx, fonts.SmallFont);
                break;
        }
    }

    private static void DrawTimingWindow(IImageProcessingContext ctx, Font titleFont, Font subtitleFont, Font smallFont)
    {
        Text(ctx, "Best viewing window", titleFont, 160, 135, Color.White, 790);
        var y = 575f;
        var start = 260f;
        var end = 1600f;
        ctx.Draw(Color.ParseHex("#B7E0FF"), 6, new PathBuilder().AddLine(new PointF(start, y), new PointF(end, y)).Build());
        ctx.Fill(Color.ParseHex("#F6C177"), new EllipsePolygon(385, y, 18));
        ctx.Fill(Color.ParseHex("#FFF2B8"), new EllipsePolygon(1110, y, 28));
        ctx.Fill(Color.ParseHex("#8FD2FF").WithAlpha(.08f), new RectangleF(385, 538, 725, 28));
        Text(ctx, "Sunset", smallFont, 330, y + 40, Color.White, 190);
        Text(ctx, "7:23 PM IST", subtitleFont, 990, y + 42, Color.ParseHex("#F6C177"), 350);
        Text(ctx, "after-sunset viewing window", smallFont, 650, 455, Color.ParseHex("#B7E0FF"), 460);
    }

    private static void DrawGuideSteps(IImageProcessingContext ctx, Font font)
    {
        var items = new[] { ("1", "Find Venus", new PointF(150, 155)), ("2", "Look nearby for Jupiter", new PointF(150, 265)), ("3", "Face west", new PointF(150, 375)) };
        foreach (var (n, text, p) in items)
        {
            ctx.Fill(Color.ParseHex("#F6C177"), new EllipsePolygon(p.X, p.Y + 21, 24));
            Text(ctx, n, font, p.X - 9, p.Y - 4, Color.ParseHex("#061124"), 40);
            Text(ctx, text, font, p.X + 50, p.Y, Color.White, 560);
        }
    }

    private static void DrawComparisonStrip(IImageProcessingContext ctx, Font font)
    {
        // Scene 5 significance layer: the visual relationship is the hero; text remains short and separated.
        ctx.Draw(Color.ParseHex("#F6C177").WithAlpha(.72f), 5, new PathBuilder().AddLine(new PointF(970, 438), new PointF(1026, 446)).Build());
        ctx.Fill(Color.ParseHex("#F6C177").WithAlpha(.88f), new EllipsePolygon(998, 442, 7));
        Text(ctx, "Two of the brightest worlds sharing the evening sky", font, 610, 595, Color.White, 760);

        ctx.Fill(Color.ParseHex("#FFF2B8").WithAlpha(.045f), new RectangleF(275, 760, 720, 88));
        ctx.Draw(Color.ParseHex("#FFF2B8"), 7, new PathBuilder().AddLine(new PointF(330, 804), new PointF(430, 804)).Build());
        ctx.Draw(Color.ParseHex("#F0C88B"), 5, new PathBuilder().AddLine(new PointF(695, 804), new PointF(760, 804)).Build());
        Text(ctx, "Venus: very bright", font, 455, 778, Color.White, 260);
        Text(ctx, "Jupiter: bright nearby", font, 785, 778, Color.White, 330);
    }

    private static void Text(IImageProcessingContext ctx, string text, Font font, float x, float y, Color color, float wrap) => ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(x, y), WrappingLength = wrap }, text, color);
}

public sealed class AnnotationLayerRenderer
{
    private const float PlanetLabelPadding = 12f;

    public void Render(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, EditorialFonts fonts)
    {
        var text = new CollisionAwareTextPainter(ctx);
        switch (spec.QuestionType.ToLowerInvariant())
        {
            case "what":
                DrawPlanetLabels(ctx, text, spec.QuestionType, fonts.LabelFont, Color.ParseHex("#FFF2B8"), Color.ParseHex("#F0C88B"));
                DrawTitleStack(text, "Venus & Jupiter", "After sunset", fonts, new RectangleF(115, 44, 760, 158));
                break;
            case "where":
                DrawPlanetLabels(ctx, text, spec.QuestionType, fonts.LabelFont, Color.White, Color.White);
                break;
            case "how":
                DrawPlanetLabels(ctx, text, spec.QuestionType, fonts.LabelFont, Color.White, Color.White);
                break;
            case "why":
                DrawTitleStack(text, "Why this view matters", "Two bright worlds, one evening sky", fonts, new RectangleF(145, 72, 900, 168));
                break;
            case "action":
                DrawTitleStack(text, "Step Outside Tonight", "Look west", fonts, new RectangleF(345, 864, 860, 92));
                break;
        }
    }

    private static void DrawTitleStack(CollisionAwareTextPainter text, string title, string subtitle, EditorialFonts fonts, RectangleF zone)
    {
        var titleFont = fonts.TitleFont;
        var subtitleFont = fonts.SubtitleFont;
        var titleBox = text.Draw(title, titleFont, new PointF(zone.X, zone.Y), Color.White, zone.Width, zone);
        var subtitleY = titleBox.Bottom + 14;
        var subtitleZone = new RectangleF(zone.X, subtitleY, zone.Width, Math.Max(0, zone.Bottom - subtitleY));
        if (subtitleZone.Height < 30) subtitleFont = fonts.SmallFont;
        var subtitleBox = text.Measure(subtitle, subtitleFont, new PointF(zone.X + 4, subtitleY), Math.Min(560, zone.Width - 8));
        if (subtitleBox.IntersectsWith(titleBox))
        {
            subtitleY = titleBox.Bottom + 18;
            subtitleBox = text.Measure(subtitle, subtitleFont, new PointF(zone.X + 4, subtitleY), Math.Min(560, zone.Width - 8));
        }
        if (subtitleBox.Bottom <= zone.Bottom)
            text.Draw(subtitle, subtitleFont, new PointF(zone.X + 4, subtitleY), Color.ParseHex("#F6C177"), Math.Min(560, zone.Width - 8), zone);
    }

    private static void DrawPlanetLabels(IImageProcessingContext ctx, CollisionAwareTextPainter text, string questionType, Font font, Color venusColor, Color jupiterColor)
    {
        var planets = PlanetLayout.GetPlacements(questionType);
        var venusLabel = PlaceLabelOutsidePlanet(text, "Venus", font, planets.Venus, preferLeft: true);
        var jupiterLabel = PlaceLabelOutsidePlanet(text, "Jupiter", font, planets.Jupiter, preferLeft: false);
        if (venusLabel.IntersectsWith(jupiterLabel))
            jupiterLabel = PlaceLabelOutsidePlanet(text, "Jupiter", font, planets.Jupiter, preferLeft: true);

        Leader(ctx, text, "Venus", planets.Venus, venusLabel, font, venusColor);
        Leader(ctx, text, "Jupiter", planets.Jupiter, jupiterLabel, font, jupiterColor);
    }

    private static RectangleF PlaceLabelOutsidePlanet(CollisionAwareTextPainter text, string label, Font font, PlanetPlacement planet, bool preferLeft)
    {
        var wrap = 220f;
        var y = planet.Center.Y - planet.Diameter / 2f - 34f;
        var measured = text.Measure(label, font, new PointF(0, y), wrap);
        var x = preferLeft
            ? planet.Center.X - planet.Diameter / 2f - PlanetLabelPadding - measured.Width - 44f
            : planet.Center.X + planet.Diameter / 2f + PlanetLabelPadding + 44f;
        var box = text.Measure(label, font, new PointF(x, y), wrap);
        if (box.IntersectsWith(Inflate(planet.Bounds, PlanetLabelPadding)))
        {
            x = preferLeft
                ? planet.Center.X + planet.Diameter / 2f + PlanetLabelPadding + 44f
                : planet.Center.X - planet.Diameter / 2f - PlanetLabelPadding - measured.Width - 44f;
            box = text.Measure(label, font, new PointF(x, y), wrap);
        }
        return ClampToCanvas(box);
    }

    private static void Leader(IImageProcessingContext ctx, CollisionAwareTextPainter text, string label, PlanetPlacement planet, RectangleF labelBox, Font font, Color color)
    {
        var target = new PointF(labelBox.X + (labelBox.X < planet.Center.X ? labelBox.Width : 0), labelBox.Y + labelBox.Height / 2f);
        var edge = PointOnPlanetEdge(planet, target, PlanetLabelPadding);
        ctx.Draw(color.WithAlpha(.72f), 2, new PathBuilder().AddLine(edge, target).Build());
        text.Draw(label, font, new PointF(labelBox.X, labelBox.Y), color, 220, new RectangleF(0, 0, 1920, 1080));
    }

    private static PointF PointOnPlanetEdge(PlanetPlacement planet, PointF toward, float padding)
    {
        var dx = toward.X - planet.Center.X;
        var dy = toward.Y - planet.Center.Y;
        var length = MathF.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
        var radius = planet.Diameter / 2f + padding;
        return new PointF(planet.Center.X + dx / length * radius, planet.Center.Y + dy / length * radius);
    }

    private static RectangleF Inflate(RectangleF rect, float amount) => new(rect.X - amount, rect.Y - amount, rect.Width + amount * 2, rect.Height + amount * 2);
    private static RectangleF ClampToCanvas(RectangleF rect) => new(Math.Clamp(rect.X, 48, 1820 - rect.Width), Math.Clamp(rect.Y, 44, 1010 - rect.Height), rect.Width, rect.Height);
}

internal sealed class CollisionAwareTextPainter(IImageProcessingContext ctx)
{
    private readonly List<RectangleF> _occupied = [];

    public RectangleF Draw(string text, Font font, PointF origin, Color color, float wrap, RectangleF zone)
    {
        var currentFont = font;
        var box = Measure(text, currentFont, origin, wrap);
        for (var i = 0; i < 3 && (Collides(box) || !Contains(zone, box)); i++)
        {
            currentFont = new Font(currentFont, Math.Max(18, currentFont.Size - 4));
            box = Measure(text, currentFont, new PointF(box.X, Math.Min(box.Y + 10, zone.Bottom - box.Height)), wrap);
        }
        if (!Contains(zone, box)) box = new RectangleF(zone.X, Math.Min(zone.Y, zone.Bottom - box.Height), Math.Min(box.Width, zone.Width), box.Height);
        ctx.DrawText(new RichTextOptions(currentFont) { Origin = new PointF(box.X, box.Y), WrappingLength = wrap }, text, color);
        _occupied.Add(box);
        return box;
    }

    public RectangleF Measure(string text, Font font, PointF origin, float wrap)
    {
        var avg = font.Size * .58f;
        var charsPerLine = Math.Max(1, (int)(wrap / Math.Max(1, avg)));
        var lines = Math.Max(1, (int)Math.Ceiling(text.Length / (double)charsPerLine));
        var width = Math.Min(wrap, Math.Max(font.Size * 2, text.Length * avg));
        return new RectangleF(origin.X, origin.Y, width, lines * font.Size * 1.22f);
    }

    private bool Collides(RectangleF box) => _occupied.Any(existing => existing.IntersectsWith(box));
    private static bool Contains(RectangleF zone, RectangleF box) => box.X >= zone.X && box.Y >= zone.Y && box.Right <= zone.Right + 1 && box.Bottom <= zone.Bottom + 1;
}

public sealed record EditorialFonts(Font TitleFont, Font SubtitleFont, Font LabelFont, Font SmallFont)
{
    public static EditorialFonts Create() => new(Resolve(60, FontStyle.Bold), Resolve(36, FontStyle.Bold), Resolve(30, FontStyle.Bold), Resolve(24, FontStyle.Regular));

    private static Font Resolve(float size, FontStyle style)
    {
        var collection = new FontCollection();
        foreach (var candidate in new[] { "Backend/src/Astronomy.MediaFactory.Api/assets/fonts/Montserrat-Bold.ttf", "Backend/src/Astronomy.MediaFactory.Api/assets/fonts/Montserrat-ExtraBold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" })
            if (File.Exists(candidate)) return collection.Add(candidate).CreateFont(size, style);
        return SystemFonts.CreateFont("Arial", size, style);
    }
}
