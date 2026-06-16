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
    private const string DefaultHookText = "CURRENT\nASTRONOMY";

    private static readonly IReadOnlyList<PhotoCinematicThumbnailSpec> Specs =
    [
        new("Landscape", "thumbnail-landscape.png", 1280, 720),
        new("Square", "thumbnail-square.png", 1080, 1080),
        new("Portrait", "thumbnail-portrait.png", 1080, 1920)
    ];

    public static IReadOnlyList<string> PlannedOutputFiles(string thumbnailRoot)
        => Specs.Select(spec => NormalizePath(Path.Combine(thumbnailRoot, spec.FileName))).ToArray();

    public static Task<PhotoCinematicThumbnailRenderResult> RenderAsync(string thumbnailRoot, CancellationToken cancellationToken)
        => RenderAsync(thumbnailRoot, PhotoCinematicThumbnailRenderRequest.Default, cancellationToken);

    public static async Task<PhotoCinematicThumbnailRenderResult> RenderAsync(string thumbnailRoot, PhotoCinematicThumbnailRenderRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(thumbnailRoot);
        var writtenFiles = new List<string>();
        var visualObjects = NormalizeObjects(request.VisualObjects).DefaultIfEmpty("Current Event").ToArray();
        var labels = NormalizeObjects(request.Labels).DefaultIfEmpty(CleanText(request.ShortTitle, request.Title, "Current Event", 26)).ToArray();
        foreach (var spec in Specs)
        {
            using var image = new Image<Rgba32>(spec.Width, spec.Height, Color.ParseHex("#040617"));
            image.Mutate(ctx =>
            {
                if (!TryDrawSourceImage(ctx, spec, request.SourceImagePath))
                {
                    DrawTwilightSky(ctx, spec);
                    DrawStars(ctx, spec);
                    DrawHorizonGlow(ctx, spec);
                    DrawMountains(ctx, spec);
                }

                DrawEventObjectsAndLabels(ctx, spec, visualObjects, labels);
                DrawTypography(ctx, spec, request);
                DrawCinematicFinish(ctx, spec);
            });

            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            var outputPath = Path.Combine(thumbnailRoot, spec.FileName);
            await image.SaveAsPngAsync(outputPath, new PngEncoder(), cancellationToken);
            writtenFiles.Add(NormalizePath(outputPath));
        }

        return new PhotoCinematicThumbnailRenderResult(true, true, writtenFiles, visualObjects, labels);
    }

    private static bool TryDrawSourceImage(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec, string? sourceImagePath)
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath)) return false;
        using var source = Image.Load<Rgba32>(sourceImagePath);
        var scale = Math.Max(spec.Width / (float)source.Width, spec.Height / (float)source.Height);
        var width = (int)MathF.Ceiling(source.Width * scale);
        var height = (int)MathF.Ceiling(source.Height * scale);
        source.Mutate(x => x.Resize(width, height));
        var x = (spec.Width - width) / 2;
        var y = (spec.Height - height) / 2;
        ctx.DrawImage(source, new Point(x, y), 1f);
        ctx.Fill(Color.Black.WithAlpha(0.18f), new RectangleF(0, 0, spec.Width, spec.Height));
        return true;
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

    private static void DrawEventObjectsAndLabels(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec, IReadOnlyList<string> visualObjects, IReadOnlyList<string> labels)
    {
        var labelFont = ResolveFont(spec.LabelFontSize, FontStyle.Bold);
        var primary = visualObjects.FirstOrDefault() ?? "Current Event";
        var label = labels.FirstOrDefault() ?? primary;
        if (ContainsObject(visualObjects, "meteor"))
        {
            DrawMeteorStreaks(ctx, spec);
            DrawCleanLabel(ctx, label, labelFont, spec.PrimaryLabelOrigin, spec.PrimaryObjectCenter, Color.ParseHex("#BFE6FF"));
            return;
        }
        if (ContainsObject(visualObjects, "eclipse"))
        {
            DrawEclipse(ctx, spec.PrimaryObjectCenter, spec.PlanetScale * 1.25f, visualObjects.Any(value => value.Contains("solar", StringComparison.OrdinalIgnoreCase)));
            DrawCleanLabel(ctx, label, labelFont, spec.PrimaryLabelOrigin, spec.PrimaryObjectCenter, Color.ParseHex("#FFD15E"));
            return;
        }
        if (ContainsObject(visualObjects, "moon") || ContainsObject(visualObjects, "full moon") || label.Contains("moon", StringComparison.OrdinalIgnoreCase))
        {
            DrawMoon(ctx, spec.PrimaryObjectCenter, spec.PlanetScale * 1.35f);
            DrawCleanLabel(ctx, label, labelFont, spec.PrimaryLabelOrigin, spec.PrimaryObjectCenter, Color.ParseHex("#F4F0DC"));
            return;
        }
        if (ContainsObject(visualObjects, "comet"))
        {
            DrawComet(ctx, spec);
            DrawCleanLabel(ctx, label, labelFont, spec.PrimaryLabelOrigin, spec.PrimaryObjectCenter, Color.ParseHex("#BFE6FF"));
            return;
        }
        if (ContainsObject(visualObjects, "nebula") || ContainsObject(visualObjects, "galaxy") || ContainsObject(visualObjects, "cluster") || ContainsObject(visualObjects, "deep sky"))
        {
            DrawDeepSkyObject(ctx, spec.PrimaryObjectCenter, spec.PlanetScale * 1.25f);
            DrawCleanLabel(ctx, label, labelFont, spec.PrimaryLabelOrigin, spec.PrimaryObjectCenter, Color.ParseHex("#C9B8FF"));
            return;
        }
        var planets = visualObjects.Where(IsPlanetObject).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (planets.Length > 1)
        {
            DrawPlanetGroup(ctx, spec, labelFont, planets);
            return;
        }
        if (ContainsObject(visualObjects, "venus"))
        {
            DrawVenusStarPoint(ctx, spec.PrimaryObjectCenter, spec.PlanetScale * 1.2f);
            DrawCleanLabel(ctx, label, labelFont, spec.PrimaryLabelOrigin, spec.PrimaryObjectCenter, Color.ParseHex("#FFF1A8"));
            return;
        }
        if (ContainsObject(visualObjects, "jupiter"))
        {
            DrawJupiterPlanet(ctx, spec.PrimaryObjectCenter, spec.PlanetScale * 1.2f);
            DrawCleanLabel(ctx, label, labelFont, spec.PrimaryLabelOrigin, spec.PrimaryObjectCenter, Color.ParseHex("#D8EBFF"));
            return;
        }
        if (ContainsObject(visualObjects, "mars"))
        {
            DrawMars(ctx, spec.PrimaryObjectCenter, spec.PlanetScale * 1.2f);
            DrawCleanLabel(ctx, label, labelFont, spec.PrimaryLabelOrigin, spec.PrimaryObjectCenter, Color.ParseHex("#FFB28A"));
            return;
        }

        DrawGlow(ctx, spec.PrimaryObjectCenter, 34f * spec.PlanetScale, 34f * spec.PlanetScale, Color.ParseHex("#BFE6FF"), 0.14f, 10);
        ctx.Fill(Color.White.WithAlpha(0.94f), new EllipsePolygon(spec.PrimaryObjectCenter.X, spec.PrimaryObjectCenter.Y, 7f * spec.PlanetScale));
        DrawCleanLabel(ctx, label, labelFont, spec.PrimaryLabelOrigin, spec.PrimaryObjectCenter, Color.ParseHex("#D8EBFF"));
    }



    private static void DrawComet(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec)
    {
        var nucleus = spec.PrimaryObjectCenter;
        var tailStart = new PointF(nucleus.X + spec.Width * 0.02f, nucleus.Y - spec.Height * 0.01f);
        var tailEnd = new PointF(nucleus.X - spec.Width * 0.26f, nucleus.Y + spec.Height * 0.08f);
        ctx.DrawLine(Pens.Solid(Color.ParseHex("#BFE6FF").WithAlpha(0.38f), Math.Max(18f, spec.Width * 0.028f)), tailStart, tailEnd);
        ctx.DrawLine(Pens.Solid(Color.White.WithAlpha(0.65f), Math.Max(6f, spec.Width * 0.010f)), nucleus, tailEnd);
        DrawGlow(ctx, nucleus, 46f * spec.PlanetScale, 34f * spec.PlanetScale, Color.ParseHex("#BFE6FF"), 0.14f, 12);
        ctx.Fill(Color.White.WithAlpha(0.96f), new EllipsePolygon(nucleus.X, nucleus.Y, 7f * spec.PlanetScale));
    }

    private static void DrawEclipse(IImageProcessingContext ctx, PointF center, float scale, bool solar)
    {
        var radius = 38f * scale;
        if (solar)
        {
            DrawGlow(ctx, center, radius * 2.1f, radius * 2.1f, Color.ParseHex("#FFD15E"), 0.20f, 16);
            ctx.Fill(Color.Black.WithAlpha(0.94f), new EllipsePolygon(center.X, center.Y, radius, radius));
            ctx.Draw(Color.ParseHex("#FFF2A5").WithAlpha(0.78f), Math.Max(2f, scale * 2.5f), new EllipsePolygon(center.X, center.Y, radius * 1.04f));
        }
        else
        {
            DrawGlow(ctx, center, radius * 1.8f, radius * 1.8f, Color.ParseHex("#B3452E"), 0.16f, 14);
            ctx.Fill(Color.ParseHex("#A6412E"), new EllipsePolygon(center.X, center.Y, radius, radius));
            ctx.Fill(Color.ParseHex("#E07B58").WithAlpha(0.22f), new EllipsePolygon(center.X - radius * 0.20f, center.Y - radius * 0.18f, radius * 0.35f));
        }
    }

    private static void DrawDeepSkyObject(IImageProcessingContext ctx, PointF center, float scale)
    {
        DrawGlow(ctx, center, 68f * scale, 42f * scale, Color.ParseHex("#8D71FF"), 0.18f, 18);
        DrawGlow(ctx, new PointF(center.X + 18f * scale, center.Y - 8f * scale), 42f * scale, 28f * scale, Color.ParseHex("#50D8FF"), 0.12f, 12);
        ctx.Draw(Color.White.WithAlpha(0.52f), Math.Max(2f, scale * 2f), new EllipsePolygon(center.X, center.Y, 58f * scale, 22f * scale));
        ctx.Fill(Color.White.WithAlpha(0.92f), new EllipsePolygon(center.X, center.Y, 4f * scale));
    }

    private static void DrawPlanetGroup(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec, Font labelFont, IReadOnlyList<string> planets)
    {
        var count = Math.Max(1, planets.Count);
        for (var i = 0; i < count; i++)
        {
            var t = count == 1 ? 0.5f : i / (float)(count - 1);
            var center = new PointF(
                spec.Width * (spec.IsPortrait ? 0.34f + t * 0.38f : spec.IsSquare ? 0.52f + t * 0.32f : 0.64f + t * 0.25f),
                spec.Height * (spec.IsPortrait ? 0.42f + t * 0.12f : spec.IsSquare ? 0.41f + t * 0.10f : 0.39f + t * 0.10f));
            var origin = new PointF(center.X - spec.Width * 0.12f, center.Y - spec.Height * 0.09f + (i % 2) * spec.Height * 0.08f);
            DrawPlanetByName(ctx, planets[i], center, spec.PlanetScale * (planets.Count > 3 ? 0.82f : 1f));
            DrawCleanLabel(ctx, planets[i], labelFont, origin, center, ResolvePlanetLabelColor(planets[i]));
        }
    }

    private static void DrawPlanetByName(IImageProcessingContext ctx, string planet, PointF center, float scale)
    {
        if (planet.Contains("venus", StringComparison.OrdinalIgnoreCase)) { DrawVenusStarPoint(ctx, center, scale); return; }
        if (planet.Contains("jupiter", StringComparison.OrdinalIgnoreCase)) { DrawJupiterPlanet(ctx, center, scale); return; }
        if (planet.Contains("mars", StringComparison.OrdinalIgnoreCase)) { DrawMars(ctx, center, scale); return; }
        if (planet.Contains("saturn", StringComparison.OrdinalIgnoreCase)) { DrawSaturn(ctx, center, scale); return; }
        var color = planet.Contains("mercury", StringComparison.OrdinalIgnoreCase) ? Color.ParseHex("#B7B2A8") : planet.Contains("uranus", StringComparison.OrdinalIgnoreCase) ? Color.ParseHex("#9DE8F2") : planet.Contains("neptune", StringComparison.OrdinalIgnoreCase) ? Color.ParseHex("#3D6FE8") : Color.ParseHex("#D8EBFF");
        DrawGlow(ctx, center, 24f * scale, 24f * scale, color, 0.10f, 9);
        ctx.Fill(color, new EllipsePolygon(center.X, center.Y, 18f * scale));
        ctx.Draw(Color.White.WithAlpha(0.30f), Math.Max(1.1f, scale), new EllipsePolygon(center.X, center.Y, 18f * scale));
    }

    private static void DrawSaturn(IImageProcessingContext ctx, PointF center, float scale)
    {
        var radius = 21f * scale;
        DrawGlow(ctx, center, radius * 2.0f, radius * 1.3f, Color.ParseHex("#E7D0A0"), 0.10f, 10);
        ctx.Draw(Color.ParseHex("#EAD8AD").WithAlpha(0.75f), Math.Max(2f, scale * 2.4f), new EllipsePolygon(center.X, center.Y, radius * 1.65f, radius * 0.45f));
        ctx.Fill(Color.ParseHex("#D9BF85"), new EllipsePolygon(center.X, center.Y, radius, radius * 0.88f));
        ctx.Draw(Color.White.WithAlpha(0.30f), Math.Max(1.1f, scale), new EllipsePolygon(center.X, center.Y, radius, radius * 0.88f));
    }

    private static Color ResolvePlanetLabelColor(string planet)
        => planet.ToLowerInvariant() switch
        {
            var p when p.Contains("venus") => Color.ParseHex("#FFF1A8"),
            var p when p.Contains("jupiter") => Color.ParseHex("#D8EBFF"),
            var p when p.Contains("mars") => Color.ParseHex("#FFB28A"),
            var p when p.Contains("saturn") => Color.ParseHex("#EAD8AD"),
            _ => Color.ParseHex("#D8EBFF")
        };

    private static bool IsPlanetObject(string value)
        => ContainsObject([value], "mercury") || ContainsObject([value], "venus") || ContainsObject([value], "mars") || ContainsObject([value], "jupiter") || ContainsObject([value], "saturn") || ContainsObject([value], "uranus") || ContainsObject([value], "neptune");

    private static void DrawMoon(IImageProcessingContext ctx, PointF center, float scale)
    {
        var radius = 38f * scale;
        DrawGlow(ctx, center, radius * 1.8f, radius * 1.8f, Color.ParseHex("#DDE8FF"), 0.12f, 14);
        ctx.Fill(Color.ParseHex("#ECE7D5"), new EllipsePolygon(center.X, center.Y, radius, radius));
        ctx.Fill(Color.ParseHex("#BFB9AA").WithAlpha(0.22f), new EllipsePolygon(center.X - radius * 0.30f, center.Y - radius * 0.18f, radius * 0.16f));
        ctx.Fill(Color.ParseHex("#AFA895").WithAlpha(0.18f), new EllipsePolygon(center.X + radius * 0.22f, center.Y + radius * 0.24f, radius * 0.22f));
        ctx.Fill(Color.White.WithAlpha(0.20f), new EllipsePolygon(center.X - radius * 0.20f, center.Y - radius * 0.35f, radius * 0.35f, radius * 0.18f));
    }

    private static void DrawMars(IImageProcessingContext ctx, PointF center, float scale)
    {
        var radius = 25f * scale;
        DrawGlow(ctx, center, radius * 1.7f, radius * 1.7f, Color.ParseHex("#E06A3D"), 0.11f, 10);
        ctx.Fill(Color.ParseHex("#D66A3D"), new EllipsePolygon(center.X, center.Y, radius, radius * 0.96f));
        ctx.Fill(Color.ParseHex("#7E392C").WithAlpha(0.24f), new EllipsePolygon(center.X + radius * 0.18f, center.Y + radius * 0.10f, radius * 0.44f, radius * 0.16f));
    }

    private static void DrawMeteorStreaks(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec)
    {
        var random = new Random(4109 + spec.Width + spec.Height);
        for (var i = 0; i < 9; i++)
        {
            var start = new PointF(spec.Width * (0.38f + random.NextSingle() * 0.42f), spec.Height * (0.18f + random.NextSingle() * 0.22f));
            var end = new PointF(start.X - spec.Width * (0.12f + random.NextSingle() * 0.12f), start.Y + spec.Height * (0.08f + random.NextSingle() * 0.08f));
            ctx.DrawLine(Pens.Solid(Color.FromRgba(190, 225, 255, 210), Math.Max(2, spec.Width / 420)), start, end);
            ctx.DrawLine(Pens.Solid(Color.White.WithAlpha(0.86f), Math.Max(1, spec.Width / 900)), start, end);
        }
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

    private static void DrawTypography(IImageProcessingContext ctx, PhotoCinematicThumbnailSpec spec, PhotoCinematicThumbnailRenderRequest request)
    {
        var hookFont = ResolveFont(spec.HookFontSize, FontStyle.BoldItalic);
        var hook = BuildHookText(request);
        DrawTextWithShadow(ctx, hook, hookFont, spec.HookOrigin, Color.White, 0.86f);

        if (IsPlanetFamilyEventType(request.EventType)) return;

        // Thumbnail V6.2 deliberately avoids guide-card secondary/micro panels.
        // All CTR copy is constrained to the hook text only.
    }

    private static string BuildHookText(PhotoCinematicThumbnailRenderRequest request)
    {
        if (IsPlanetFamilyEventType(request.EventType))
        {
            var objectLine = CleanText(request.ShortTitle, request.Title, DefaultHookText, 28).ToUpperInvariant();
            objectLine = objectLine.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? objectLine;
            var subheadline = CleanText(request.SecondaryText, string.Empty, string.Empty, 24).ToUpperInvariant();
            return LimitHookWords(string.IsNullOrWhiteSpace(subheadline) ? objectLine : $"{objectLine}\n{subheadline}", 6);
        }

        var text = CleanText(request.ShortTitle, request.Title, DefaultHookText, 24).ToUpperInvariant();
        if (text.Contains('\n')) return LimitHookWords(text, 6);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return LimitHookWords(words.Length <= 2 ? text : string.Join(' ', words.Take((words.Length + 1) / 2)) + "\n" + string.Join(' ', words.Skip((words.Length + 1) / 2)), 6);
    }

    private static string LimitHookWords(string value, int maxWords)
    {
        var lines = (value ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(2).ToArray();
        var words = string.Join(' ', lines).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(maxWords).ToArray();
        if (words.Length <= 3) return string.Join(' ', words);
        var firstCount = (words.Length + 1) / 2;
        return string.Join(' ', words.Take(firstCount)) + "\n" + string.Join(' ', words.Skip(firstCount));
    }

    private static bool IsPlanetFamilyEventType(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType)) return false;
        var normalized = new string(eventType.Where(char.IsLetterOrDigit).ToArray());
        return normalized.Equals("PLANETPAIRING", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("PLANETCONJUNCTION", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("CONJUNCTION", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("PLANETGROUPING", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("BRIGHTPLANETVISIBILITY", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("PLANETPARADE", StringComparison.OrdinalIgnoreCase);
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

    private static IReadOnlyList<string> NormalizeObjects(IEnumerable<string>? objects)
        => (objects ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool ContainsObject(IEnumerable<string> objects, string objectName)
        => objects.Any(value => value.Contains(objectName, StringComparison.OrdinalIgnoreCase));

    private static string CleanText(string? preferred, string? fallback, string defaultValue, int maxLength)
    {
        var value = !string.IsNullOrWhiteSpace(preferred) ? preferred.Trim() : !string.IsNullOrWhiteSpace(fallback) ? fallback.Trim() : defaultValue;
        value = string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    public sealed record PhotoCinematicThumbnailRenderRequest(
        string Title,
        string ShortTitle,
        string EventType,
        IReadOnlyList<string> VisualObjects,
        IReadOnlyList<string> Labels,
        string? SecondaryText,
        string? MicroText,
        string? SourceImagePath,
        object? CurrentEventLock = null,
        object? VisualResolverResult = null,
        string? SourceManifestPath = null,
        string? SourceScenePath = null)
    {
        public string? SkyDirectionHint => SecondaryText;
        public string? LocalPeakTime => MicroText;
        public static PhotoCinematicThumbnailRenderRequest Default { get; } = new("Current Astronomy Event", "Current Event", "Unknown", ["Current Event"], ["Current Event"], null, null, null);
    }

    public sealed record PhotoCinematicThumbnailRenderResult(bool Entered, bool Completed, IReadOnlyList<string> WrittenFiles, IReadOnlyList<string> VisualObjectsUsed, IReadOnlyList<string> LabelsUsed);

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
        public PointF PrimaryObjectCenter => IsPortrait ? new PointF(Width * 0.57f, Height * 0.48f) : IsSquare ? new PointF(Width * 0.70f, Height * 0.48f) : new PointF(Width * 0.78f, Height * 0.45f);
        public PointF PrimaryLabelOrigin => IsPortrait ? new PointF(Width * 0.20f, Height * 0.51f) : IsSquare ? new PointF(Width * 0.42f, Height * 0.36f) : new PointF(Width * 0.56f, Height * 0.33f);
        public PointF VenusCenter => IsPortrait ? new PointF(Width * 0.40f, Height * 0.43f) : IsSquare ? new PointF(Width * 0.57f, Height * 0.43f) : new PointF(Width * 0.70f, Height * 0.40f);
        public PointF JupiterCenter => IsPortrait ? new PointF(Width * 0.63f, Height * 0.50f) : IsSquare ? new PointF(Width * 0.76f, Height * 0.49f) : new PointF(Width * 0.84f, Height * 0.48f);
        public PointF VenusLabelOrigin => IsPortrait ? new PointF(Width * 0.19f, Height * 0.48f) : IsSquare ? new PointF(Width * 0.39f, Height * 0.36f) : new PointF(Width * 0.58f, Height * 0.32f);
        public PointF JupiterLabelOrigin => IsPortrait ? new PointF(Width * 0.68f, Height * 0.55f) : IsSquare ? new PointF(Width * 0.69f, Height * 0.57f) : new PointF(Width * 0.74f, Height * 0.57f);
    }
}
