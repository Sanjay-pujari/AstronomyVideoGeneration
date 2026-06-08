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
        var (marginX, marginY) = ResolveSafeMargins(width, height);
        return new RectangleF(marginX, marginY, width - marginX * 2, height - marginY * 2);
    }

    private static (float MarginX, float MarginY) ResolveSafeMargins(int width, int height)
        => (width, height) switch
        {
            (1280, 720) => (80f, 50f),
            (1080, 1080) => (70f, 70f),
            (1080, 1920) => (70f, 100f),
            _ => (Math.Max(36, width * 0.06f), Math.Max(36, height * 0.06f))
        };

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
            if (request.CompositionMode != AstronomyVisualCompositionMode.HeroAsset)
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
        DrawPlanetTextures(ctx, request.Width, request.Height, request.PlanetAssets, allowDefaultHeroObjects: true);
        DrawHeroLabelsAndTypography(ctx, request.Width, request.Height, request.Title, request.Subtitle, request.Labels);
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
            if (request.CompositionMode == AstronomyVisualCompositionMode.HeroAsset)
                return BuildHeroContainFitBackground(source, request.Width, request.Height);

            source.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(request.Width, request.Height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center }).Brightness(0.66f).Saturate(0.88f));
            return source.Clone();
        }

        return new Image<Rgba32>(request.Width, request.Height, Color.ParseHex("#050817"));
    }


    private static Image<Rgba32> BuildHeroContainFitBackground(Image<Rgba32> source, int width, int height)
    {
        var canvas = new Image<Rgba32>(width, height, Color.ParseHex("#050817"));
        using var backdrop = source.Clone(ctx => ctx
            .Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center })
            .GaussianBlur(Math.Max(8f, Math.Min(width, height) * 0.018f))
            .Brightness(0.46f)
            .Saturate(0.72f));
        canvas.Mutate(ctx => ctx.DrawImage(backdrop, new Point(0, 0), 1f));

        var fitScale = Math.Min(width / (float)source.Width, height / (float)source.Height);
        var fitWidth = Math.Max(1, (int)MathF.Round(source.Width * fitScale));
        var fitHeight = Math.Max(1, (int)MathF.Round(source.Height * fitScale));
        using var foreground = source.Clone(ctx => ctx
            .Resize(new ResizeOptions { Size = new Size(fitWidth, fitHeight), Mode = ResizeMode.Max, Position = AnchorPositionMode.Center })
            .Brightness(0.78f)
            .Saturate(0.92f));
        var origin = new Point((width - foreground.Width) / 2, (height - foreground.Height) / 2);
        canvas.Mutate(ctx => ctx.DrawImage(foreground, origin, 0.96f));
        return canvas;
    }

    private static void DrawSmoothTwilightSky(IImageProcessingContext ctx, int width, int height, string mood)
    {
        var warmHorizon = mood.Contains("warm", StringComparison.OrdinalIgnoreCase) || mood.Contains("hero", StringComparison.OrdinalIgnoreCase);
        var top = warmHorizon ? Color.ParseHex("#020515") : Color.ParseHex("#020615");
        var mid = warmHorizon ? Color.ParseHex("#141F45") : Color.ParseHex("#0B1A3B");
        var horizon = warmHorizon ? Color.ParseHex("#C97845") : Color.ParseHex("#6A4E8E");
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height), GradientRepetitionMode.None,
            new ColorStop(0f, top.WithAlpha(0.98f)),
            new ColorStop(0.46f, mid.WithAlpha(0.90f)),
            new ColorStop(0.77f, horizon.WithAlpha(0.46f)),
            new ColorStop(1f, Color.ParseHex("#100D18").WithAlpha(0.95f))),
            new RectangleF(0, 0, width, height));

        DrawGlow(ctx, new PointF(width * 0.31f, height * 0.80f), width * 0.70f, height * 0.16f, Color.ParseHex("#F0A45D"), 0.045f, 14);
        DrawGlow(ctx, new PointF(width * 0.70f, height * 0.36f), width * 0.44f, height * 0.24f, Color.ParseHex("#83B7FF"), 0.026f, 10);
        DrawGlow(ctx, new PointF(width * 0.52f, height * 0.86f), width * 0.86f, height * 0.055f, Color.ParseHex("#F6C177"), 0.038f, 12);
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
        DrawPlanetGroupAtmosphere(ctx, placements.Take(Math.Min(count, placements.Count)).ToArray());
        for (var i = 0; i < count && i < placements.Count; i++)
        {
            var placement = placements[i];
            var asset = i < assets.Count ? assets[i] : new AstronomyVisualPlanetAsset(i == 0 ? "Venus" : "Jupiter", null);
            DrawGlow(ctx, new PointF(placement.X + placement.Width / 2f, placement.Y + placement.Height / 2f), placement.Width * 0.78f, placement.Height * 0.78f, PlanetGlow(asset.Label), 0.060f, 9);

            if (!string.IsNullOrWhiteSpace(asset.TexturePath) && File.Exists(asset.TexturePath))
            {
                using var planet = Image.Load<Rgba32>(asset.TexturePath);
                MakeNearBlackTransparent(planet, 16);
                planet.Mutate(x => x.Resize(new ResizeOptions { Size = new Size((int)placement.Width, (int)placement.Height), Mode = ResizeMode.Max, Position = AnchorPositionMode.Center }).Saturate(1.05f).Contrast(1.04f));
                ctx.DrawImage(planet, new Point((int)placement.X, (int)placement.Y), 0.96f);
            }
            else
            {
                DrawProceduralPlanet(ctx, placement, asset.Label);
            }

            var labelScale = height > width ? 0.042f : width == height ? 0.036f : 0.047f;
            var font = ResolveFont(Math.Max(28f, Math.Min(width, height) * labelScale), FontStyle.Regular);
            var labelOrigin = new PointF(placement.X + placement.Width * 0.08f, placement.Bottom + Math.Max(10f, height * 0.008f));
            ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(labelOrigin.X + 2, labelOrigin.Y + 2), WrappingLength = Math.Max(150, placement.Width * 2.3f) }, asset.Label, Color.Black.WithAlpha(0.48f));
            ctx.DrawText(new RichTextOptions(font) { Origin = labelOrigin, WrappingLength = Math.Max(150, placement.Width * 2.3f) }, asset.Label, Color.ParseHex("#F5E7C6").WithAlpha(0.76f));
        }
    }

    private static List<RectangleF> BuildPlanetPlacements(int width, int height, int count)
    {
        if (height > width)
        {
            return [
                CenteredPlanet(width * 0.42f, height * 0.36f, width * 0.22f),
                CenteredPlanet(width * 0.59f, height * 0.43f, width * 0.16f),
                CenteredPlanet(width * 0.52f, height * 0.28f, width * 0.10f)
            ];
        }

        if (width == height)
        {
            return count switch
            {
                1 => [CenteredPlanet(width * 0.52f, height * 0.43f, width * 0.18f)],
                2 => [CenteredPlanet(width * 0.48f, height * 0.42f, width * 0.16f), CenteredPlanet(width * 0.64f, height * 0.49f, width * 0.12f)],
                _ => [CenteredPlanet(width * 0.44f, height * 0.42f, width * 0.15f), CenteredPlanet(width * 0.60f, height * 0.49f, width * 0.11f), CenteredPlanet(width * 0.73f, height * 0.42f, width * 0.07f)]
            };
        }

        return count switch
        {
            1 => [CenteredPlanet(width * 0.70f, height * 0.42f, width * 0.155f)],
            2 => [CenteredPlanet(width * 0.66f, height * 0.43f, width * 0.115f), CenteredPlanet(width * 0.74f, height * 0.405f, width * 0.080f)],
            _ => [CenteredPlanet(width * 0.64f, height * 0.43f, width * 0.115f), CenteredPlanet(width * 0.73f, height * 0.405f, width * 0.084f), CenteredPlanet(width * 0.80f, height * 0.39f, width * 0.048f)]
        };
    }

    private static RectangleF CenteredPlanet(float centerX, float centerY, float size)
        => new(centerX - size / 2f, centerY - size / 2f, size, size);

    private static void DrawPlanetGroupAtmosphere(IImageProcessingContext ctx, IReadOnlyList<RectangleF> placements)
    {
        if (placements.Count == 0) return;
        var left = placements.Min(p => p.Left);
        var right = placements.Max(p => p.Right);
        var top = placements.Min(p => p.Top);
        var bottom = placements.Max(p => p.Bottom);
        var center = new PointF((left + right) / 2f, (top + bottom) / 2f);
        DrawGlow(ctx, center, Math.Max(90f, (right - left) * 0.82f), Math.Max(70f, (bottom - top) * 1.10f), Color.ParseHex("#CFE7FF"), 0.070f, 12);
        DrawGlow(ctx, new PointF(center.X, center.Y + (bottom - top) * 0.16f), Math.Max(80f, (right - left) * 0.68f), Math.Max(54f, (bottom - top) * 0.72f), Color.ParseHex("#FFE2A7"), 0.035f, 10);
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


    private static void DrawHeroLabelsAndTypography(IImageProcessingContext ctx, int width, int height, string title, string subtitle, IReadOnlyList<AstronomyVisualLabel> labels)
    {
        var isPortrait = height > width;
        var isSquare = width == height;
        var titleFont = ResolveFont(isPortrait ? 76f : isSquare ? 64f : 56f, FontStyle.Bold);
        var subtitleFont = ResolveFont(isPortrait ? 30f : 26f, FontStyle.Regular);
        var bodyFont = ResolveFont(isPortrait ? 50f : isSquare ? 42f : 36f, FontStyle.Bold);
        var landscapeCtaFont = ResolveFont(36f, FontStyle.Bold);
        var landscapeDirectionFont = ResolveFont(40f, FontStyle.Bold);
        var landscapeTimingFont = ResolveFont(38f, FontStyle.Bold);

        var textBlocks = BuildHeroTemplateTextBlocks(width, height, title, subtitle, labels);
        foreach (var block in textBlocks)
        {
            var font = block.Name switch
            {
                "Hook" => titleFont,
                "Subtitle" => subtitleFont,
                "CTA" when !isPortrait && !isSquare => landscapeCtaFont,
                "Direction" when !isPortrait && !isSquare => landscapeDirectionFont,
                "Timing" when !isPortrait && !isSquare => landscapeTimingFont,
                _ => bodyFont
            };
            var color = block.Name switch
            {
                "Hook" => Color.White,
                "Subtitle" => Color.ParseHex("#FFD48A"),
                "Direction" => Color.ParseHex("#FFD48A"),
                "Timing" => Color.ParseHex("#BFE6FF"),
                "CTA" => Color.ParseHex("#8FD2FF"),
                _ => Color.ParseHex("#CBE8FF")
            };
            DrawHeroTextBackdrop(ctx, block.Bounds, block.Name);
            var options = new RichTextOptions(font) { Origin = new PointF(block.Bounds.X, block.Bounds.Y), WrappingLength = block.Bounds.Width };
            ctx.DrawText(new RichTextOptions(options) { Origin = new PointF(block.Bounds.X + 3, block.Bounds.Y + 3) }, block.Text, Color.Black.WithAlpha(0.72f));
            ctx.DrawText(options, block.Text, color);
        }
    }

    private static void DrawHeroTextBackdrop(IImageProcessingContext ctx, RectangleF textBounds, string blockName)
    {
        var center = new PointF(textBounds.X + textBounds.Width * 0.46f, textBounds.Y + textBounds.Height * 0.58f);
        var radiusX = Math.Max(120f, textBounds.Width * (blockName is "Hook" ? 0.54f : 0.46f));
        var radiusY = Math.Max(34f, textBounds.Height * (blockName is "Hook" ? 0.90f : 0.82f));
        var alpha = blockName switch
        {
            "Hook" => 0.145f,
            "CTA" => 0.135f,
            "Direction" => 0.120f,
            _ => 0.105f
        };
        DrawGlow(ctx, center, radiusX, radiusY, Color.Black, alpha, 8);
        if (blockName is "Hook" or "CTA")
            DrawGlow(ctx, new PointF(center.X, center.Y + radiusY * 0.18f), radiusX * 0.72f, radiusY * 0.44f, Color.ParseHex("#8FD2FF"), 0.028f, 6);
    }

    private static IReadOnlyList<(string Name, string Text, RectangleF Bounds)> BuildHeroTemplateTextBlocks(int width, int height, string title, string subtitle, IReadOnlyList<AstronomyVisualLabel> labels)
    {
        var timing = labels.Count > 0 ? labels[0].Text : string.Empty;
        var direction = labels.Count > 1 ? labels[1].Text : string.Empty;
        var cta = labels.Count > 2 ? labels[2].Text : string.Empty;
        var blocks = new List<(string Name, string Text, RectangleF Bounds)>();
        AddBlock(blocks, "Hook", title, HeroTemplateBounds(width, height, "Hook"));
        AddBlock(blocks, "Subtitle", subtitle, HeroTemplateBounds(width, height, "Subtitle"));
        AddBlock(blocks, "Timing", timing, HeroTemplateBounds(width, height, "Timing"));
        AddBlock(blocks, "Direction", direction, HeroTemplateBounds(width, height, "Direction"));
        AddBlock(blocks, "CTA", cta, HeroTemplateBounds(width, height, "CTA"));
        return blocks;
    }

    private static void AddBlock(List<(string Name, string Text, RectangleF Bounds)> blocks, string name, string text, RectangleF bounds)
    {
        if (!string.IsNullOrWhiteSpace(text))
            blocks.Add((name, text, bounds));
    }

    private static RectangleF HeroTemplateBounds(int width, int height, string blockName)
        => (width, height, blockName) switch
        {
            (1280, 720, "Hook") => new RectangleF(80, 54, 690, 82),
            (1280, 720, "Subtitle") => new RectangleF(80, 142, 520, 34),
            (1280, 720, "Timing") => new RectangleF(80, 552, 305, 48),
            (1280, 720, "CTA") => new RectangleF(420, 568, 500, 52),
            (1280, 720, "Direction") => new RectangleF(980, 540, 240, 54),
            (1080, 1080, "Hook") => new RectangleF(70, 82, 760, 84),
            (1080, 1080, "Subtitle") => new RectangleF(70, 178, 700, 38),
            (1080, 1080, "Timing") => new RectangleF(70, 790, 315, 52),
            (1080, 1080, "Direction") => new RectangleF(700, 790, 270, 52),
            (1080, 1080, "CTA") => new RectangleF(70, 908, 840, 58),
            (1080, 1920, "Hook") => new RectangleF(70, 130, 860, 100),
            (1080, 1920, "Subtitle") => new RectangleF(70, 252, 820, 44),
            (1080, 1920, "Timing") => new RectangleF(70, 1300, 350, 64),
            (1080, 1920, "Direction") => new RectangleF(620, 1300, 300, 64),
            (1080, 1920, "CTA") => new RectangleF(70, 1608, 880, 70),
            _ => RectangleF.Empty
        };

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
