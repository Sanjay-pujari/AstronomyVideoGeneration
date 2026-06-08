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
    public void Render(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec)
    {
        var sceneNumber = spec.SceneNumber;
        var palette = sceneNumber switch
        {
            1 => (Top: "#041126", Middle: "#152D5A", Bottom: "#E8894F"),
            2 => (Top: "#071D3A", Middle: "#102F4F", Bottom: "#233447"),
            3 => (Top: "#17254A", Middle: "#56538A", Bottom: "#FFAA5D"),
            4 => (Top: "#041827", Middle: "#0B3750", Bottom: "#314348"),
            5 => (Top: "#020813", Middle: "#081F49", Bottom: "#16243A"),
            6 => (Top: "#041026", Middle: "#172D58", Bottom: "#D56F4C"),
            _ => (Top: "#061124", Middle: "#163158", Bottom: "#293C3F")
        };

        for (var y = 0; y < 1080; y += 4)
        {
            var t = y / 1080f;
            var color = t < .68f ? Blend(Color.ParseHex(palette.Top), Color.ParseHex(palette.Middle), t / .68f) : Blend(Color.ParseHex(palette.Middle), Color.ParseHex(palette.Bottom), (t - .68f) / .32f);
            ctx.Fill(color, new RectangleF(0, y, 1920, 5));
        }

        RenderSceneAtmosphere(ctx, sceneNumber);
        RenderStars(ctx, sceneNumber);
        RenderLandscape(ctx, sceneNumber);
    }

    public void RenderVignette(IImageProcessingContext ctx) => ctx.Draw(Color.Black.WithAlpha(.26f), 64, new RectangleF(28, 28, 1864, 1024));

    private static void RenderSceneAtmosphere(IImageProcessingContext ctx, int sceneNumber)
    {
        switch (sceneNumber)
        {
            case 1:
                ctx.Fill(Color.ParseHex("#F6C177").WithAlpha(.18f), new EllipsePolygon(1460, 720, 360));
                ctx.Draw(Color.ParseHex("#F6C177").WithAlpha(.20f), 3, new RectangleF(92, 82, 1728, 868));
                ctx.Fill(Color.White.WithAlpha(.05f), new EllipsePolygon(1130, 355, 520));
                break;
            case 2:
                ctx.Fill(Color.ParseHex("#8FD2FF").WithAlpha(.10f), new RectangleF(360, 200, 1240, 610));
                ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(.28f), 2, new RectangleF(360, 200, 1240, 610));
                break;
            case 3:
                ctx.Fill(Color.ParseHex("#FFAA5D").WithAlpha(.20f), new EllipsePolygon(350, 720, 220));
                ctx.Fill(Color.ParseHex("#8FD2FF").WithAlpha(.07f), new RectangleF(210, 468, 1360, 180));
                break;
            case 4:
                ctx.Fill(Color.Black.WithAlpha(.16f), new RectangleF(104, 96, 590, 430));
                ctx.Draw(Color.ParseHex("#F6C177").WithAlpha(.35f), 2, new RectangleF(104, 96, 590, 430));
                ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(.18f), 3, new RectangleF(760, 260, 690, 360));
                break;
            case 5:
                ctx.Fill(Color.ParseHex("#FFF2B8").WithAlpha(.08f), new EllipsePolygon(960, 440, 520));
                ctx.Draw(Color.ParseHex("#F6C177").WithAlpha(.18f), 2, new RectangleF(135, 720, 1065, 185));
                break;
            case 6:
                ctx.Fill(Color.ParseHex("#F6C177").WithAlpha(.16f), new EllipsePolygon(1510, 720, 390));
                ctx.Draw(Color.ParseHex("#FFFFFF").WithAlpha(.20f), 2, new RectangleF(115, 96, 1690, 870));
                break;
        }
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
                DrawDunes(ctx, 812, "#111318", "#1B171A");
                ctx.Fill(Color.Black.WithAlpha(.38f), new RectangleF(0, 914, 1920, 166));
                break;
            case 2:
                ctx.Fill(Color.ParseHex("#14202A").WithAlpha(.92f), new RectangleF(0, 810, 1920, 270));
                ctx.Draw(Color.ParseHex("#B7E0FF").WithAlpha(.64f), 4, new PathBuilder().AddLine(new PointF(360, 760), new PointF(1600, 760)).Build());
                ctx.Draw(Color.ParseHex("#F6C177").WithAlpha(.45f), 3, new PathBuilder().AddLine(new PointF(360, 810), new PointF(1600, 810)).Build());
                break;
            case 3:
                ctx.Fill(Color.ParseHex("#1A222A").WithAlpha(.90f), new RectangleF(0, 852, 1920, 228));
                ctx.Draw(Color.ParseHex("#FFAA5D").WithAlpha(.50f), 3, new PathBuilder().AddLine(new PointF(0, 832), new PointF(1920, 832)).Build());
                break;
            case 4:
                DrawLowHorizon(ctx, 804, "#0D1A1E");
                ctx.Draw(Color.ParseHex("#F6C177").WithAlpha(.50f), 3, new PathBuilder().AddLine(new PointF(0, 804), new PointF(1920, 804)).Build());
                break;
            case 5:
                ctx.Fill(Color.ParseHex("#0A101A").WithAlpha(.90f), new RectangleF(0, 930, 1920, 150));
                break;
            case 6:
                DrawDunes(ctx, 835, "#10151C", "#161820");
                ctx.Fill(Color.ParseHex("#F6C177").WithAlpha(.12f), new RectangleF(0, 900, 1920, 80));
                ctx.Fill(Color.Black.WithAlpha(.42f), new RectangleF(0, 968, 1920, 112));
                break;
        }
    }

    private static void DrawDunes(IImageProcessingContext ctx, float horizon, string nearColor, string farColor)
    {
        var far = new PathBuilder().AddLine(new PointF(0, horizon + 40), new PointF(270, horizon + 5)).AddLine(new PointF(270, horizon + 5), new PointF(615, horizon + 58)).AddLine(new PointF(615, horizon + 58), new PointF(1025, horizon - 10)).AddLine(new PointF(1025, horizon - 10), new PointF(1415, horizon + 34)).AddLine(new PointF(1415, horizon + 34), new PointF(1920, horizon - 5)).AddLine(new PointF(1920, horizon - 5), new PointF(1920, 1080)).AddLine(new PointF(1920, 1080), new PointF(0, 1080)).CloseFigure().Build();
        ctx.Fill(Color.ParseHex(farColor).WithAlpha(.94f), far);
        DrawLowHorizon(ctx, horizon + 88, nearColor);
    }

    private static void DrawLowHorizon(IImageProcessingContext ctx, float horizon, string color)
    {
        var path = new PathBuilder().AddLine(new PointF(0, horizon), new PointF(320, horizon + 24)).AddLine(new PointF(320, horizon + 24), new PointF(720, horizon - 12)).AddLine(new PointF(720, horizon - 12), new PointF(1190, horizon + 18)).AddLine(new PointF(1190, horizon + 18), new PointF(1920, horizon - 8)).AddLine(new PointF(1920, horizon - 8), new PointF(1920, 1080)).AddLine(new PointF(1920, 1080), new PointF(0, 1080)).CloseFigure().Build();
        ctx.Fill(Color.ParseHex(color).WithAlpha(.96f), path);
    }

    private static Color Blend(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var ap = a.ToPixel<Rgba32>();
        var bp = b.ToPixel<Rgba32>();
        return Color.FromRgb((byte)(ap.R + (bp.R - ap.R) * amount), (byte)(ap.G + (bp.G - ap.G) * amount), (byte)(ap.B + (bp.B - ap.B) * amount));
    }
}

public sealed class CelestialObjectLayerRenderer
{
    public void Render(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, string venusAssetPath, string jupiterAssetPath)
    {
        var positions = spec.QuestionType.ToLowerInvariant() switch
        {
            "what" => (Venus: new PlanetPlacement(new PointF(1220, 360), 140), Jupiter: new PlanetPlacement(new PointF(1410, 410), 96)),
            "where" => (Venus: new PlanetPlacement(new PointF(1060, 505), 92), Jupiter: new PlanetPlacement(new PointF(1255, 545), 68)),
            "how" => (Venus: new PlanetPlacement(new PointF(950, 430), 112), Jupiter: new PlanetPlacement(new PointF(1195, 470), 76)),
            "why" => (Venus: new PlanetPlacement(new PointF(900, 420), 128), Jupiter: new PlanetPlacement(new PointF(1060, 445), 98)),
            "action" => (Venus: new PlanetPlacement(new PointF(1010, 390), 110), Jupiter: new PlanetPlacement(new PointF(1165, 430), 78)),
            _ => (Venus: new PlanetPlacement(new PointF(-100, -100), 1), Jupiter: new PlanetPlacement(new PointF(-100, -100), 1))
        };

        if (spec.QuestionType.Equals("when", StringComparison.OrdinalIgnoreCase)) return;
        DrawAsset(ctx, venusAssetPath, positions.Venus.Center, positions.Venus.Diameter, "#FFF2B8");
        DrawAsset(ctx, jupiterAssetPath, positions.Jupiter.Center, positions.Jupiter.Diameter, "#E5C18D");
    }

    private static void DrawAsset(IImageProcessingContext ctx, string assetPath, PointF center, int diameter, string glowColor)
    {
        ctx.Fill(Color.ParseHex(glowColor).WithAlpha(.10f), new EllipsePolygon(center.X, center.Y, diameter * .46f));
        using var asset = Image.Load<Rgba32>(assetPath);
        asset.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(diameter, diameter), Mode = ResizeMode.Max }));
        ctx.DrawImage(asset, new Point((int)(center.X - asset.Width / 2f), (int)(center.Y - asset.Height / 2f)), 1f);
    }

    private readonly record struct PlanetPlacement(PointF Center, int Diameter);
}

public sealed class SkyGuidanceLayerRenderer
{
    public void Render(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, EditorialFonts fonts)
    {
        switch (spec.QuestionType.ToLowerInvariant())
        {
            case "where":
                DrawSkyGrid(ctx);
                DrawReferenceConstellation(ctx, fonts.SmallFont);
                DrawWestMarker(ctx, new PointF(245, 760), fonts.LabelFont);
                Text(ctx, "Western horizon", fonts.SmallFont, 820, 785, Color.ParseHex("#B7E0FF"), 300);
                Text(ctx, "altitude guide", fonts.SmallFont, 450, 304, Color.ParseHex("#B7E0FF"), 240);
                break;
            case "how":
                DrawWestMarker(ctx, new PointF(230, 780), fonts.LabelFont);
                DrawArrow(ctx, new PointF(1015, 440), new PointF(1150, 465), Color.ParseHex("#8FD2FF"));
                break;
            case "why":
                DrawClosenessBracket(ctx, new PointF(810, 330), new PointF(1140, 540), fonts.SmallFont);
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

    private static void DrawReferenceConstellation(IImageProcessingContext ctx, Font font)
    {
        var points = new[] { new PointF(555, 330), new PointF(665, 382), new PointF(780, 345), new PointF(910, 425), new PointF(1045, 365) };
        for (var i = 0; i < points.Length; i++)
        {
            ctx.Fill(Color.White.WithAlpha(.72f), new EllipsePolygon(points[i].X, points[i].Y, 4));
            if (i > 0) ctx.Draw(Color.White.WithAlpha(.18f), 2, new PathBuilder().AddLine(points[i - 1], points[i]).Build());
        }
        Text(ctx, "reference stars", font, 585, 410, Color.White.WithAlpha(.58f), 250);
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

    private static void DrawClosenessBracket(IImageProcessingContext ctx, PointF a, PointF b, Font font)
    {
        ctx.Draw(Color.ParseHex("#F6C177"), 4, new RectangleF(a.X, a.Y, b.X - a.X, b.Y - a.Y));
        Text(ctx, "close pairing", font, a.X + 95, a.Y - 42, Color.ParseHex("#F6C177"), 220);
        Text(ctx, "small angular gap", font, a.X + 72, b.Y + 18, Color.ParseHex("#B7E0FF"), 260);
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
        ctx.Fill(Color.ParseHex("#8FD2FF").WithAlpha(.18f), new RectangleF(430, 500, 860, 105));
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
        ctx.Fill(Color.Black.WithAlpha(.24f), new RectangleF(145, 720, 1050, 190));
        ctx.Draw(Color.White.WithAlpha(.24f), 2, new PathBuilder().AddLine(new PointF(205, 842), new PointF(1040, 842)).Build());
        ctx.Draw(Color.ParseHex("#FFF2B8"), 7, new PathBuilder().AddLine(new PointF(245, 792), new PointF(345, 792)).Build());
        ctx.Draw(Color.ParseHex("#F0C88B"), 5, new PathBuilder().AddLine(new PointF(535, 792), new PointF(595, 792)).Build());
        ctx.Draw(Color.ParseHex("#8FD2FF"), 4, new PathBuilder().AddLine(new PointF(780, 792), new PointF(1040, 792)).Build());
        Text(ctx, "brightness", font, 210, 728, Color.ParseHex("#F6C177"), 180);
        Text(ctx, "Venus: very bright", font, 360, 768, Color.White, 230);
        Text(ctx, "Jupiter: bright", font, 610, 768, Color.White, 220);
        Text(ctx, "close in the same western view", font, 770, 862, Color.ParseHex("#B7E0FF"), 370);
    }

    private static void Text(IImageProcessingContext ctx, string text, Font font, float x, float y, Color color, float wrap) => ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(x, y), WrappingLength = wrap }, text, color);
}

public sealed class AnnotationLayerRenderer
{
    public void Render(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, EditorialFonts fonts)
    {
        switch (spec.QuestionType.ToLowerInvariant())
        {
            case "what":
                Leader(ctx, "Venus", new PointF(1220, 360), new PointF(1085, 300), fonts.LabelFont, Color.ParseHex("#FFF2B8"));
                Leader(ctx, "Jupiter", new PointF(1410, 410), new PointF(1490, 345), fonts.LabelFont, Color.ParseHex("#F0C88B"));
                Text(ctx, "Venus & Jupiter Tonight", fonts.TitleFont, 115, 98, Color.White, 705);
                Text(ctx, "After sunset", fonts.SubtitleFont, 122, 205, Color.ParseHex("#F6C177"), 520);
                break;
            case "where":
                Leader(ctx, "Venus", new PointF(1060, 505), new PointF(940, 445), fonts.LabelFont, Color.White);
                Leader(ctx, "Jupiter", new PointF(1255, 545), new PointF(1320, 492), fonts.LabelFont, Color.White);
                break;
            case "how":
                Leader(ctx, "Venus", new PointF(950, 430), new PointF(820, 365), fonts.LabelFont, Color.White);
                Leader(ctx, "Jupiter", new PointF(1195, 470), new PointF(1260, 415), fonts.LabelFont, Color.White);
                break;
            case "why":
                Text(ctx, "Why this view matters", fonts.TitleFont, 145, 115, Color.White, 720);
                Text(ctx, "Bright + close + easy to compare", fonts.SubtitleFont, 150, 205, Color.ParseHex("#F6C177"), 820);
                break;
            case "action":
                Text(ctx, "Step Outside Tonight", fonts.TitleFont, 135, 150, Color.White, 740);
                Text(ctx, "Look west", fonts.SubtitleFont, 145, 235, Color.ParseHex("#F6C177"), 320);
                break;
        }
    }

    private static void Leader(IImageProcessingContext ctx, string text, PointF from, PointF label, Font font, Color color)
    {
        ctx.Draw(color.WithAlpha(.72f), 2, new PathBuilder().AddLine(from, label).Build());
        Text(ctx, text, font, label.X, label.Y, color, 220);
    }

    private static void Text(IImageProcessingContext ctx, string text, Font font, float x, float y, Color color, float wrap) => ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(x, y), WrappingLength = wrap }, text, color);
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
