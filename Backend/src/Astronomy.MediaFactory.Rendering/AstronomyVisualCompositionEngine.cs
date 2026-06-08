using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

public enum AstronomyVisualCompositionMode
{
    SceneInfographic,
    HeroAsset,
    Thumbnail,
    SocialAsset
}

public static class AstronomyVisualCompositionEngine
{
    public static readonly IReadOnlyDictionary<string, Size> PlatformAspectRatios = new Dictionary<string, Size>(StringComparer.OrdinalIgnoreCase)
    {
        ["Landscape"] = new(1280, 720),
        ["YouTube"] = new(1280, 720),
        ["Square"] = new(1080, 1080),
        ["Instagram"] = new(1080, 1080),
        ["Portrait"] = new(1080, 1920),
        ["Shorts"] = new(1080, 1920)
    };

    public static RectangleF SafeContentBounds(int width, int height)
    {
        var marginX = Math.Max(36, width * 0.06f);
        var marginY = Math.Max(36, height * 0.06f);
        return new RectangleF(marginX, marginY, width - marginX * 2, height - marginY * 2);
    }

    public static async Task ComposePngAsync(AstronomyVisualCompositionRequest request, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath)!);
        using var image = await ComposeAsync(request, cancellationToken);
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        await image.SaveAsPngAsync(outputPath, new PngEncoder(), cancellationToken);
    }

    public static async Task ComposeJpegAsync(AstronomyVisualCompositionRequest request, string outputPath, CancellationToken cancellationToken, int quality = 92)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath)!);
        using var image = await ComposeAsync(request, cancellationToken);
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        await image.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = quality }, cancellationToken);
    }

    public static async Task<Image<Rgba32>> ComposeAsync(AstronomyVisualCompositionRequest request, CancellationToken cancellationToken)
    {
        var image = await BuildBaseImageAsync(request, cancellationToken);
        image.Mutate(ctx =>
        {
            var usesApprovedHeroSceneBackground = request.CompositionMode == AstronomyVisualCompositionMode.HeroAsset
                && !string.IsNullOrWhiteSpace(request.BackgroundImagePath);
            if (!usesApprovedHeroSceneBackground)
            {
                DrawSmoothTwilightSky(ctx, request.Width, request.Height, request.Mood);
                DrawStars(ctx, request.Width, request.Height, request.StarDensity);
                DrawConstellationAndReferenceStarOverlay(ctx, request.Width, request.Height, request.ReferenceStars, request.ShowReferenceOverlays);
                DrawHorizonAndLandscape(ctx, request.Width, request.Height, request.Mood);
            }

            DrawVisualModeLayers(ctx, request);
            DrawWestMarker(ctx, request.Width, request.Height, request.WestMarkerLabel);
            DrawSafeMarginGuide(ctx, request.Width, request.Height, request.ShowSafeMarginGuide);
            DrawFinishingGrade(ctx, request.Width, request.Height);
        });

        return image;
    }

    private static void DrawVisualModeLayers(IImageProcessingContext ctx, AstronomyVisualCompositionRequest request)
    {
        switch (request.CompositionMode)
        {
            case AstronomyVisualCompositionMode.SceneInfographic:
                DrawSceneInfographicMode(ctx, request);
                break;
            case AstronomyVisualCompositionMode.HeroAsset:
                DrawHeroAssetMode(ctx, request);
                break;
            case AstronomyVisualCompositionMode.Thumbnail:
            case AstronomyVisualCompositionMode.SocialAsset:
                DrawPosterAssetMode(ctx, request);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), $"Unsupported astronomy visual composition mode '{request.CompositionMode}'.");
        }
    }

    private static void DrawSceneInfographicMode(IImageProcessingContext ctx, AstronomyVisualCompositionRequest request)
    {
        // Scene infographics own their celestial-object and editorial overlay layers.
        // Keep the shared composer to base sky/reference-safe-margin work only so
        // hero foreground planets, hooks, labels, and poster scaling cannot pollute
        // the approved What/Where/When/How/Why/Action scene layouts.
    }

    private static void DrawHeroAssetMode(IImageProcessingContext ctx, AstronomyVisualCompositionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BackgroundImagePath))
            DrawPlanetTextures(ctx, request.Width, request.Height, request.PlanetAssets, allowDefaultHeroObjects: true);
        DrawLabelsAndTypography(ctx, request.Width, request.Height, request.Title, request.Subtitle, request.MetadataLine, request.Labels);
    }

    private static void DrawPosterAssetMode(IImageProcessingContext ctx, AstronomyVisualCompositionRequest request)
    {
        DrawPlanetTextures(ctx, request.Width, request.Height, request.PlanetAssets, allowDefaultHeroObjects: false);
        DrawLabelsAndTypography(ctx, request.Width, request.Height, request.Title, request.Subtitle, request.MetadataLine, request.Labels);
    }

    private static async Task<Image<Rgba32>> BuildBaseImageAsync(AstronomyVisualCompositionRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.BackgroundImagePath) && File.Exists(request.BackgroundImagePath))
        {
            using var source = await Image.LoadAsync<Rgba32>(request.BackgroundImagePath, cancellationToken);
            source.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(request.Width, request.Height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center }).Brightness(0.66f).Saturate(0.88f));
            return source.Clone();
        }

        return new Image<Rgba32>(request.Width, request.Height, Color.ParseHex("#050817"));
    }

    private static void DrawSmoothTwilightSky(IImageProcessingContext ctx, int width, int height, string mood)
    {
        var warmHorizon = mood.Contains("warm", StringComparison.OrdinalIgnoreCase) || mood.Contains("hero", StringComparison.OrdinalIgnoreCase);
        var top = warmHorizon ? Color.ParseHex("#030616") : Color.ParseHex("#020615");
        var mid = warmHorizon ? Color.ParseHex("#18224A") : Color.ParseHex("#0B1A3B");
        var horizon = warmHorizon ? Color.ParseHex("#D9864A") : Color.ParseHex("#6A4E8E");
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height), GradientRepetitionMode.None,
            new ColorStop(0f, top.WithAlpha(0.96f)),
            new ColorStop(0.50f, mid.WithAlpha(0.88f)),
            new ColorStop(0.82f, horizon.WithAlpha(0.55f)),
            new ColorStop(1f, Color.ParseHex("#15101B").WithAlpha(0.92f))),
            new RectangleF(0, 0, width, height));

        DrawGlow(ctx, new PointF(width * 0.25f, height * 0.78f), width * 0.55f, height * 0.18f, Color.ParseHex("#F6A45F"), 0.055f, 12);
        DrawGlow(ctx, new PointF(width * 0.72f, height * 0.36f), width * 0.44f, height * 0.24f, Color.ParseHex("#83B7FF"), 0.030f, 10);
    }

    private static void DrawStars(IImageProcessingContext ctx, int width, int height, int requestedCount)
    {
        var count = Math.Clamp(requestedCount, 80, 900);
        var random = new Random(1387 + width + height + count);
        for (var i = 0; i < count; i++)
        {
            var x = random.NextSingle() * width;
            var y = random.NextSingle() * height * 0.76f;
            var altitudeFade = Math.Clamp(1f - y / (height * 0.84f), 0.12f, 1f);
            var radius = random.NextSingle() > 0.972f ? 2.4f + random.NextSingle() * 1.8f : 0.55f + random.NextSingle() * 1.15f;
            var alpha = (0.20f + random.NextSingle() * 0.65f) * altitudeFade;
            ctx.Fill(Color.White.WithAlpha(alpha), new EllipsePolygon(x, y, radius));
        }
    }

    private static void DrawConstellationAndReferenceStarOverlay(IImageProcessingContext ctx, int width, int height, IReadOnlyList<AstronomyReferenceStar> referenceStars, bool showOverlays)
    {
        if (!showOverlays) return;
        var stars = referenceStars.Count > 0
            ? referenceStars
            : [new AstronomyReferenceStar("Vega", 0.24f, 0.21f), new AstronomyReferenceStar("Altair", 0.48f, 0.38f), new AstronomyReferenceStar("Deneb", 0.68f, 0.18f)];

        for (var i = 0; i < stars.Count - 1; i++)
        {
            var a = ToPoint(stars[i], width, height);
            var b = ToPoint(stars[i + 1], width, height);
            ctx.DrawLine(Color.ParseHex("#9ED8FF").WithAlpha(0.19f), Math.Max(1.2f, width / 900f), a, b);
        }

        var labelFont = ResolveFont(Math.Max(13f, width * 0.012f), FontStyle.Regular);
        foreach (var star in stars)
        {
            var p = ToPoint(star, width, height);
            ctx.Fill(Color.ParseHex("#D9F5FF").WithAlpha(0.82f), new EllipsePolygon(p.X, p.Y, Math.Max(2.2f, width * 0.0023f)));
            ctx.DrawText(new RichTextOptions(labelFont) { Origin = new PointF(p.X + 9, p.Y - 7) }, star.Label, Color.ParseHex("#BDE9FF").WithAlpha(0.70f));
        }
    }

    private static PointF ToPoint(AstronomyReferenceStar star, int width, int height) => new(Math.Clamp(star.X, 0, 1) * width, Math.Clamp(star.Y, 0, 0.74f) * height);

    private static void DrawHorizonAndLandscape(IImageProcessingContext ctx, int width, int height, string mood)
    {
        var horizonY = height * 0.80f;
        ctx.Fill(new LinearGradientBrush(new PointF(0, horizonY - height * 0.12f), new PointF(0, height), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Transparent),
            new ColorStop(0.45f, Color.ParseHex("#140E19").WithAlpha(0.72f)),
            new ColorStop(1f, Color.ParseHex("#05040A").WithAlpha(1f))), new RectangleF(0, horizonY - height * 0.12f, width, height * 0.22f));

        var ridgePoints = new List<PointF> { new(0, height), new(0, horizonY + height * 0.035f) };
        for (var i = 0; i <= 18; i++)
        {
            var x = width * (i / 18f);
            var y = horizonY + MathF.Sin(i * 0.85f) * height * 0.022f + MathF.Sin(i * 0.31f + 1.4f) * height * 0.018f;
            ridgePoints.Add(new PointF(x, y));
        }
        ridgePoints.Add(new PointF(width, height));
        ctx.Fill(Color.ParseHex("#070711"), new Polygon(new LinearLineSegment(ridgePoints.ToArray())));

        var ground = new RectangleF(0, height * 0.88f, width, height * 0.12f);
        ctx.Fill(Color.ParseHex("#040309").WithAlpha(0.96f), ground);
    }

    private static void DrawPlanetTextures(IImageProcessingContext ctx, int width, int height, IReadOnlyList<AstronomyVisualPlanetAsset> assets, bool allowDefaultHeroObjects)
    {
        var count = assets.Count == 0 && allowDefaultHeroObjects ? 2 : assets.Count;
        if (count == 0) return;

        var placements = BuildPlanetPlacements(width, height, count);
        for (var i = 0; i < count && i < placements.Count; i++)
        {
            var placement = placements[i];
            var asset = i < assets.Count ? assets[i] : new AstronomyVisualPlanetAsset(i == 0 ? "Venus" : "Jupiter", null);
            DrawGlow(ctx, new PointF(placement.X + placement.Width / 2f, placement.Y + placement.Height / 2f), placement.Width * 0.78f, placement.Height * 0.78f, PlanetGlow(asset.Label), 0.060f, 9);

            if (!string.IsNullOrWhiteSpace(asset.TexturePath) && File.Exists(asset.TexturePath))
            {
                using var planet = Image.Load<Rgba32>(asset.TexturePath);
                MakeNearBlackTransparent(planet, 16);
                planet.Mutate(x => x.Resize(new ResizeOptions { Size = new Size((int)placement.Width, (int)placement.Height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center }).Saturate(1.05f).Contrast(1.04f));
                ctx.DrawImage(planet, new Point((int)placement.X, (int)placement.Y), 0.96f);
            }
            else
            {
                DrawProceduralPlanet(ctx, placement, asset.Label);
            }

            var font = ResolveFont(Math.Max(14f, width * 0.014f), FontStyle.Bold);
            ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(placement.X, placement.Bottom + 8), WrappingLength = Math.Max(140, placement.Width * 2.2f) }, asset.Label, Color.ParseHex("#F5E7C6").WithAlpha(0.88f));
        }
    }

    private static List<RectangleF> BuildPlanetPlacements(int width, int height, int count)
    {
        var safe = SafeContentBounds(width, height);
        if (height > width)
        {
            return [
                new RectangleF(safe.X + safe.Width * 0.18f, height * 0.31f, width * 0.22f, width * 0.22f),
                new RectangleF(safe.X + safe.Width * 0.58f, height * 0.40f, width * 0.16f, width * 0.16f),
                new RectangleF(safe.X + safe.Width * 0.45f, height * 0.23f, width * 0.10f, width * 0.10f)
            ];
        }

        return count switch
        {
            1 => [new RectangleF(width * 0.50f, height * 0.29f, width * 0.18f, width * 0.18f)],
            2 => [new RectangleF(width * 0.48f, height * 0.34f, width * 0.13f, width * 0.13f), new RectangleF(width * 0.64f, height * 0.42f, width * 0.09f, width * 0.09f)],
            _ => [new RectangleF(width * 0.43f, height * 0.36f, width * 0.13f, width * 0.13f), new RectangleF(width * 0.59f, height * 0.43f, width * 0.095f, width * 0.095f), new RectangleF(width * 0.73f, height * 0.34f, width * 0.055f, width * 0.055f)]
        };
    }

    private static void DrawProceduralPlanet(IImageProcessingContext ctx, RectangleF bounds, string label)
    {
        var isJupiter = label.Contains("jupiter", StringComparison.OrdinalIgnoreCase);
        var isVenus = label.Contains("venus", StringComparison.OrdinalIgnoreCase);
        var baseColor = isVenus ? Color.ParseHex("#FFF0B9") : isJupiter ? Color.ParseHex("#D9B17A") : Color.ParseHex("#D6E4FF");
        ctx.Fill(baseColor, new EllipsePolygon(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f, bounds.Width / 2f, bounds.Height / 2f));
        if (isJupiter)
        {
            for (var i = 0; i < 5; i++)
            {
                var y = bounds.Y + bounds.Height * (0.25f + i * 0.11f);
                ctx.DrawLine(Color.ParseHex(i % 2 == 0 ? "#A97145" : "#F0D0A0").WithAlpha(0.32f), Math.Max(1.2f, bounds.Height * 0.035f), new PointF(bounds.X + bounds.Width * 0.16f, y), new PointF(bounds.Right - bounds.Width * 0.16f, y + bounds.Height * 0.025f));
            }
        }
        ctx.Fill(Color.White.WithAlpha(0.18f), new EllipsePolygon(bounds.X + bounds.Width * 0.38f, bounds.Y + bounds.Height * 0.34f, bounds.Width * 0.18f, bounds.Height * 0.12f));
        ctx.Draw(Color.White.WithAlpha(0.36f), Math.Max(1.1f, bounds.Width * 0.012f), new EllipsePolygon(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f, bounds.Width / 2.02f, bounds.Height / 2.02f));
    }

    private static void DrawWestMarker(IImageProcessingContext ctx, int width, int height, string westMarkerLabel)
    {
        var safe = SafeContentBounds(width, height);
        var font = ResolveFont(Math.Max(22f, width * 0.022f), FontStyle.Bold);
        var x = safe.Right - Math.Max(120, width * 0.13f);
        var y = height * 0.77f;
        ctx.DrawLine(Color.ParseHex("#FBCB69").WithAlpha(0.84f), Math.Max(3, width * 0.004f), new PointF(x, y), new PointF(x + width * 0.09f, y));
        ctx.DrawLine(Color.ParseHex("#FBCB69").WithAlpha(0.84f), Math.Max(3, width * 0.004f), new PointF(x, y), new PointF(x + width * 0.025f, y - height * 0.025f));
        ctx.DrawLine(Color.ParseHex("#FBCB69").WithAlpha(0.84f), Math.Max(3, width * 0.004f), new PointF(x, y), new PointF(x + width * 0.025f, y + height * 0.025f));
        ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(x + width * 0.095f, y - font.Size * 0.62f) }, westMarkerLabel, Color.ParseHex("#FFE0A0"));
    }

    private static void DrawSafeMarginGuide(IImageProcessingContext ctx, int width, int height, bool showSafeMarginGuide)
    {
        if (!showSafeMarginGuide) return;
        ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(0.18f), 1f, SafeContentBounds(width, height));
    }

    private static void DrawLabelsAndTypography(IImageProcessingContext ctx, int width, int height, string title, string subtitle, string metadataLine, IReadOnlyList<AstronomyVisualLabel> labels)
    {
        var safe = SafeContentBounds(width, height);
        var titleFont = ResolveFont(Math.Max(38f, Math.Min(width, height) * 0.060f), FontStyle.Bold);
        var subtitleFont = ResolveFont(Math.Max(20f, Math.Min(width, height) * 0.026f), FontStyle.Bold);
        var bodyFont = ResolveFont(Math.Max(16f, Math.Min(width, height) * 0.020f), FontStyle.Regular);
        var titleOrigin = height > width ? new PointF(safe.X, safe.Y) : new PointF(safe.X, safe.Y * 0.85f);
        if (!string.IsNullOrWhiteSpace(title))
        {
            var options = new RichTextOptions(titleFont) { Origin = titleOrigin, WrappingLength = Math.Min(safe.Width * 0.68f, width * 0.62f) };
            ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(titleOrigin.X + 3, titleOrigin.Y + 3) }, title, Color.Black.WithAlpha(0.72f));
            ctx.DrawText(options, title, Color.White);
        }

        var subtitleY = titleOrigin.Y + Math.Max(52, titleFont.Size * 1.16f);
        if (!string.IsNullOrWhiteSpace(subtitle))
            ctx.DrawText(new RichTextOptions(subtitleFont) { Origin = new PointF(safe.X + 2, subtitleY), WrappingLength = safe.Width * 0.66f }, subtitle, Color.ParseHex("#FFD48A"));
        if (!string.IsNullOrWhiteSpace(metadataLine))
            ctx.DrawText(new RichTextOptions(bodyFont) { Origin = new PointF(safe.X + 2, subtitleY + subtitleFont.Size * 1.22f), WrappingLength = safe.Width * 0.70f }, metadataLine, Color.ParseHex("#CBE8FF").WithAlpha(0.92f));

        foreach (var label in labels)
        {
            var origin = new PointF(safe.X + safe.Width * Math.Clamp(label.X, 0, 1), safe.Y + safe.Height * Math.Clamp(label.Y, 0, 1));
            ctx.DrawText(new RichTextOptions(bodyFont) { Origin = origin, WrappingLength = Math.Max(160, safe.Width * 0.28f) }, label.Text, label.Color.WithAlpha(label.Opacity));
        }
    }

    private static void DrawFinishingGrade(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Black.WithAlpha(0.28f)),
            new ColorStop(0.18f, Color.Transparent),
            new ColorStop(0.78f, Color.Transparent),
            new ColorStop(1f, Color.Black.WithAlpha(0.45f))), new RectangleF(0, 0, width, height));
        ctx.Fill(new LinearGradientBrush(new PointF(0, height * 0.1f), new PointF(width, height * 0.88f), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Black.WithAlpha(0.17f)),
            new ColorStop(0.50f, Color.Transparent),
            new ColorStop(1f, Color.Black.WithAlpha(0.12f))), new RectangleF(0, 0, width, height));
    }

    private static void DrawGlow(IImageProcessingContext ctx, PointF center, float radiusX, float radiusY, Color color, float alpha, int rings)
    {
        for (var i = rings; i >= 1; i--)
        {
            var t = i / (float)rings;
            ctx.Fill(color.WithAlpha(alpha * MathF.Pow(1f - t * 0.72f, 1.45f)), new EllipsePolygon(center.X, center.Y, radiusX * t, radiusY * t));
        }
    }

    private static Color PlanetGlow(string label)
        => label.Contains("venus", StringComparison.OrdinalIgnoreCase) ? Color.ParseHex("#FFE9A6")
            : label.Contains("jupiter", StringComparison.OrdinalIgnoreCase) ? Color.ParseHex("#D5B07B")
            : label.Contains("moon", StringComparison.OrdinalIgnoreCase) ? Color.ParseHex("#DCE8FF")
            : Color.ParseHex("#8FD2FF");

    private static void MakeNearBlackTransparent(Image<Rgba32> image, byte threshold)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var px = row[x];
                    if (px.R <= threshold && px.G <= threshold && px.B <= threshold)
                        row[x] = new Rgba32(px.R, px.G, px.B, 0);
                }
            }
        });
    }

    private static Font ResolveFont(float size, FontStyle style)
    {
        foreach (var name in new[] { "Inter", "Segoe UI", "Arial", "DejaVu Sans", "Liberation Sans" })
        {
            if (SystemFonts.TryGet(name, out var family)) return family.CreateFont(size, style);
        }

        var fallbackFamily = SystemFonts.Collection.Families.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fallbackFamily.Name))
            throw new InvalidOperationException("No system fonts available for astronomy visual composition.");

        return fallbackFamily.CreateFont(size, style);
    }
}

public sealed record AstronomyVisualCompositionRequest
{
    public AstronomyVisualCompositionRequest(
        int width,
        int height,
        string title,
        string subtitle,
        string metadataLine,
        IReadOnlyList<AstronomyVisualPlanetAsset> planetAssets,
        string mood = "WarmTwilightScene",
        string westMarkerLabel = "WEST",
        int starDensity = 460,
        bool showReferenceOverlays = true,
        bool showSafeMarginGuide = false,
        IReadOnlyList<AstronomyReferenceStar>? referenceStars = null,
        IReadOnlyList<AstronomyVisualLabel>? labels = null,
        string? backgroundImagePath = null,
        AstronomyVisualCompositionMode compositionMode = AstronomyVisualCompositionMode.SceneInfographic)
    {
        Width = width;
        Height = height;
        Title = title;
        Subtitle = subtitle;
        MetadataLine = metadataLine;
        PlanetAssets = planetAssets;
        Mood = mood;
        WestMarkerLabel = westMarkerLabel;
        StarDensity = starDensity;
        ShowReferenceOverlays = showReferenceOverlays;
        ShowSafeMarginGuide = showSafeMarginGuide;
        ReferenceStars = referenceStars ?? [];
        Labels = labels ?? [];
        BackgroundImagePath = backgroundImagePath;
        CompositionMode = compositionMode;
    }

    public int Width { get; init; }
    public int Height { get; init; }
    public string Title { get; init; }
    public string Subtitle { get; init; }
    public string MetadataLine { get; init; }
    public IReadOnlyList<AstronomyVisualPlanetAsset> PlanetAssets { get; init; }
    public string Mood { get; init; }
    public string WestMarkerLabel { get; init; }
    public int StarDensity { get; init; }
    public bool ShowReferenceOverlays { get; init; }
    public bool ShowSafeMarginGuide { get; init; }
    public IReadOnlyList<AstronomyReferenceStar> ReferenceStars { get; init; }
    public IReadOnlyList<AstronomyVisualLabel> Labels { get; init; }
    public string? BackgroundImagePath { get; init; }
    public AstronomyVisualCompositionMode CompositionMode { get; init; }
}

public sealed record AstronomyVisualPlanetAsset(string Label, string? TexturePath);

public sealed record AstronomyReferenceStar(string Label, float X, float Y);

public sealed record AstronomyVisualLabel(string Text, float X, float Y, Color Color, float Opacity = 0.9f);
