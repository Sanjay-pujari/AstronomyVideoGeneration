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
        var top = sceneNumber switch { 1 => "#06142E", 2 => "#071B35", 3 => "#17254A", 4 => "#06172A", 5 => "#030D1E", 6 => "#07132B", _ => "#061124" };
        var middle = sceneNumber switch { 3 => "#56538A", 6 => "#243B63", 5 => "#0E2450", _ => "#163158" };
        var bottom = sceneNumber switch { 1 => "#E07A45", 2 => "#344040", 3 => "#FFAA5D", 4 => "#263A3D", 5 => "#101B2A", 6 => "#C86548", _ => "#293C3F" };
        for (var y = 0; y < 1080; y += 4)
        {
            var t = y / 1080f;
            var color = t < .68f ? Blend(Color.ParseHex(top), Color.ParseHex(middle), t / .68f) : Blend(Color.ParseHex(middle), Color.ParseHex(bottom), (t - .68f) / .32f);
            ctx.Fill(color, new RectangleF(0, y, 1920, 5));
        }

        RenderStars(ctx, sceneNumber);
        RenderLandscape(ctx, sceneNumber);
    }

    public void RenderVignette(IImageProcessingContext ctx) => ctx.Draw(Color.Black.WithAlpha(.26f), 64, new RectangleF(28, 28, 1864, 1024));

    private static void RenderStars(IImageProcessingContext ctx, int sceneNumber)
    {
        var stars = new[] { new PointF(250, 150), new PointF(475, 250), new PointF(745, 120), new PointF(990, 205), new PointF(1320, 145), new PointF(1610, 260), new PointF(1780, 95), new PointF(1185, 330), new PointF(380, 370), new PointF(1540, 360), new PointF(720, 315) };
        foreach (var star in stars) ctx.Fill(Color.White.WithAlpha(sceneNumber is 1 or 6 ? .34f : .58f), new EllipsePolygon(star.X, star.Y, sceneNumber is 2 ? 2.2f : 1.5f));
        if (sceneNumber == 2) ctx.Draw(Color.White.WithAlpha(.16f), 2, new PathBuilder().AddLine(stars[1], stars[3]).AddLine(stars[3], stars[5]).Build());
    }

    private static void RenderLandscape(IImageProcessingContext ctx, int sceneNumber)
    {
        var horizon = sceneNumber is 2 ? 760 : sceneNumber is 3 ? 820 : 790;
        var ridge = new PathBuilder()
            .AddLine(new PointF(0, horizon + 30), new PointF(180, horizon - 18))
            .AddLine(new PointF(180, horizon - 18), new PointF(360, horizon - 70))
            .AddLine(new PointF(360, horizon - 70), new PointF(650, horizon + 55))
            .AddLine(new PointF(650, horizon + 55), new PointF(960, horizon - 20))
            .AddLine(new PointF(960, horizon - 20), new PointF(1250, horizon - 90))
            .AddLine(new PointF(1250, horizon - 90), new PointF(1550, horizon + 40))
            .AddLine(new PointF(1550, horizon + 40), new PointF(1920, horizon - 35))
            .AddLine(new PointF(1920, horizon - 35), new PointF(1920, 1080))
            .AddLine(new PointF(1920, 1080), new PointF(0, 1080))
            .CloseFigure()
            .Build();
        ctx.Fill(Color.ParseHex("#10181B").WithAlpha(.96f), ridge);
        ctx.Draw(Color.ParseHex("#B7E0FF").WithAlpha(sceneNumber is 2 or 4 ? .62f : .22f), sceneNumber is 2 ? 4 : 2, new PathBuilder().AddLine(new PointF(0, horizon), new PointF(1920, horizon)).Build());
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
            "where" => (new PlanetPlacement(new PointF(1060, 505), 92), new PlanetPlacement(new PointF(1255, 545), 68)),
            "how" => (new PlanetPlacement(new PointF(950, 430), 112), new PlanetPlacement(new PointF(1195, 470), 76)),
            "why" => (new PlanetPlacement(new PointF(900, 420), 128), new PlanetPlacement(new PointF(1060, 445), 98)),
            "action" => (new PlanetPlacement(new PointF(1010, 390), 110), new PlanetPlacement(new PointF(1165, 430), 78)),
            _ => (new PlanetPlacement(new PointF(-100, -100), 1), new PlanetPlacement(new PointF(-100, -100), 1))
        };

        if (spec.QuestionType.Equals("when", StringComparison.OrdinalIgnoreCase)) return;
        DrawAsset(ctx, venusAssetPath, positions.Venus.Center, positions.Venus.Diameter, "#FFF2B8");
        DrawAsset(ctx, jupiterAssetPath, positions.Jupiter.Center, positions.Jupiter.Diameter, "#E5C18D");
    }

    private static void DrawAsset(IImageProcessingContext ctx, string assetPath, PointF center, int diameter, string glowColor)
    {
        ctx.Fill(Color.ParseHex(glowColor).WithAlpha(.20f), new EllipsePolygon(center.X, center.Y, diameter * .62f));
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
        var items = new[] { ("1", "Find Venus", new PointF(140, 165)), ("2", "Look nearby for Jupiter", new PointF(140, 255)), ("3", "Face west", new PointF(140, 345)) };
        foreach (var (n, text, p) in items)
        {
            ctx.Fill(Color.ParseHex("#F6C177"), new EllipsePolygon(p.X, p.Y + 21, 24));
            Text(ctx, n, font, p.X - 9, p.Y - 4, Color.ParseHex("#061124"), 40);
            Text(ctx, text, font, p.X + 50, p.Y, Color.White, 560);
        }
    }

    private static void DrawComparisonStrip(IImageProcessingContext ctx, Font font)
    {
        ctx.Draw(Color.White.WithAlpha(.24f), 2, new PathBuilder().AddLine(new PointF(180, 842), new PointF(870, 842)).Build());
        ctx.Draw(Color.ParseHex("#FFF2B8"), 5, new PathBuilder().AddLine(new PointF(235, 792), new PointF(285, 792)).Build());
        ctx.Draw(Color.ParseHex("#F0C88B"), 5, new PathBuilder().AddLine(new PointF(495, 792), new PointF(545, 792)).Build());
        Text(ctx, "Venus: very bright", font, 300, 768, Color.White, 230);
        Text(ctx, "Jupiter: bright nearby", font, 555, 768, Color.White, 300);
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
                Text(ctx, "Venus & Jupiter Tonight", fonts.TitleFont, 115, 110, Color.White, 760);
                Text(ctx, "After sunset", fonts.SubtitleFont, 122, 195, Color.ParseHex("#F6C177"), 520);
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
                Text(ctx, "Two bright planets close together", fonts.SubtitleFont, 170, 145, Color.White, 850);
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
    public static EditorialFonts Create() => new(Resolve(68, FontStyle.Bold), Resolve(38, FontStyle.Bold), Resolve(30, FontStyle.Bold), Resolve(24, FontStyle.Regular));

    private static Font Resolve(float size, FontStyle style)
    {
        var collection = new FontCollection();
        foreach (var candidate in new[] { "Backend/src/Astronomy.MediaFactory.Api/assets/fonts/Montserrat-Bold.ttf", "Backend/src/Astronomy.MediaFactory.Api/assets/fonts/Montserrat-ExtraBold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" })
            if (File.Exists(candidate)) return collection.Add(candidate).CreateFont(size, style);
        return SystemFonts.CreateFont("Arial", size, style);
    }
}
