using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
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
    AnnotationLayerRenderer annotationLayer,
    IRuntimeAssetPathResolver? assetPathResolver = null) : IAstronomyInfographicRenderer
{
    private readonly IRuntimeAssetPathResolver _assetPathResolver = assetPathResolver ?? new RuntimeAssetPathResolver();
    private readonly bool _useRepositoryAssetDiscovery = assetPathResolver is null;
    public static ShortFormCompositionDecision NativeShortFormCompositionDecision { get; } = new(
        NativeComposerUsed: true,
        UsesLongFormImage: false,
        DrawsInnerFrame: false);

    public async Task RenderAsync(string finalPath, QuestionDrivenVisualSpec spec, string venusAssetPath, string jupiterAssetPath, CancellationToken cancellationToken, AstronomyInfographicRenderVariant? variant = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var planetAssetsAvailable = spec.UsesLocalPlanetAssets && !string.IsNullOrWhiteSpace(venusAssetPath) && !string.IsNullOrWhiteSpace(jupiterAssetPath) && File.Exists(venusAssetPath) && File.Exists(jupiterAssetPath);

        var targetVariant = variant ?? AstronomyInfographicRenderVariant.LongForm;
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? ".");

        if (targetVariant.VariantName.Equals(AstronomyInfographicRenderVariant.ShortForm.VariantName, StringComparison.OrdinalIgnoreCase))
        {
            using var shortFormImage = await RenderNativeShortFormAsync(spec, planetAssetsAvailable ? venusAssetPath : string.Empty, planetAssetsAvailable ? jupiterAssetPath : string.Empty, cancellationToken);
            await shortFormImage.SaveAsPngAsync(finalPath, new PngEncoder(), cancellationToken);
            return;
        }

        using var longFormImage = await RenderApprovedLongFormAsync(spec, planetAssetsAvailable ? venusAssetPath : string.Empty, planetAssetsAvailable ? jupiterAssetPath : string.Empty, cancellationToken);
        await longFormImage.SaveAsPngAsync(finalPath, new PngEncoder(), cancellationToken);
    }


    private static IReadOnlyList<AstronomyVisualPlanetAsset> BuildPlanetAssets(string venusAssetPath, string jupiterAssetPath)
        => File.Exists(venusAssetPath) && File.Exists(jupiterAssetPath)
            ? [new AstronomyVisualPlanetAsset("Venus", venusAssetPath), new AstronomyVisualPlanetAsset("Jupiter", jupiterAssetPath)]
            : [];

    private static bool IsNamedFullMoonVisual(QuestionDrivenVisualSpec spec)
        => spec.EventType.Equals("NamedFullMoon", StringComparison.OrdinalIgnoreCase)
            || spec.DrawableVisualObjects?.Any(obj => obj.ObjectType.Equals("Moon", StringComparison.OrdinalIgnoreCase)
                && (obj.Phase?.Equals("FullMoon", StringComparison.OrdinalIgnoreCase) ?? false)) == true
            || spec.ProgrammaticLayers.Any(layer => layer.Contains("drawable-object:Moon", StringComparison.OrdinalIgnoreCase));

    private static string ResolveMoonLabel(QuestionDrivenVisualSpec spec)
        => spec.DrawableVisualObjects?.FirstOrDefault(obj => obj.ObjectType.Equals("Moon", StringComparison.OrdinalIgnoreCase))?.Label
            ?? (spec.OverlayText.FirstOrDefault(text => text.Contains("Snow Moon", StringComparison.OrdinalIgnoreCase)) is not null ? "Snow Moon" : "Full Moon");

    private void DrawNamedFullMoonVisual(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, int width, int height)
    {
        if (!IsNamedFullMoonVisual(spec)) return;

        var shortForm = width < 1400;
        var center = ResolveMoonCenter(spec.SceneNumber, width, height, shortForm);
        var radius = ResolveMoonRadius(spec.SceneNumber, width, shortForm);
        var assetKey = ResolveMoonAssetKey(spec);
        var moonAssetPath = ResolveLocalMoonAssetPath(assetKey);

        if (string.IsNullOrWhiteSpace(moonAssetPath))
        {
            if (!IsDebugFallbackEnabled(spec))
                throw new InvalidOperationException($"Visual asset resolution failed for {assetKey}: no local Moon asset/texture was found in the celestial asset library. Production rendering may not silently draw a primitive circle; restore assets/celestial/moon/hero-transparent.png (or hero.png) or enable AI realistic object generation before Phase 8/9. Primitive circle fallback is allowed only when DebugFallbackEnabled=true.");

            DrawPrimitiveDebugFullMoon(ctx, center, radius);
        }
        else
        {
            DrawMoonTextureDisc(ctx, moonAssetPath, center, radius);
        }

        DrawMoonLabelAndGuide(ctx, spec, width, height, shortForm, center, radius);
    }

    private static void DrawMoonGlow(IImageProcessingContext ctx, PointF center, float radius)
    {
        var glow = Color.ParseHex("#DCEBFF");
        ctx.Fill(glow.WithAlpha(.10f), new EllipsePolygon(center.X, center.Y, radius * 2.65f));
        ctx.Fill(glow.WithAlpha(.18f), new EllipsePolygon(center.X, center.Y, radius * 1.85f));
        ctx.Fill(glow.WithAlpha(.28f), new EllipsePolygon(center.X, center.Y, radius * 1.32f));
    }

    private static void DrawMoonTextureDisc(IImageProcessingContext ctx, string assetPath, PointF center, float radius)
    {
        DrawMoonGlow(ctx, center, radius);
        var diameter = Math.Max(2, (int)MathF.Round(radius * 2f));
        using var asset = Image.Load<Rgba32>(assetPath);
        asset.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(diameter, diameter), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center }).GaussianBlur(.08f));
        var origin = new Point((int)MathF.Round(center.X - diameter / 2f), (int)MathF.Round(center.Y - diameter / 2f));
        var disc = new EllipsePolygon(center.X, center.Y, radius);
        ctx.Clip(disc, clipped => clipped.DrawImage(asset, origin, 1f));
        ctx.Draw(Color.ParseHex("#FFF6D7").WithAlpha(.46f), Math.Max(2f, radius * .018f), disc);
    }

    private static void DrawPrimitiveDebugFullMoon(IImageProcessingContext ctx, PointF center, float radius)
    {
        DrawMoonGlow(ctx, center, radius);
        ctx.Fill(Color.ParseHex("#FFF6D7"), new EllipsePolygon(center.X, center.Y, radius));
        ctx.Fill(Color.ParseHex("#D8C79D").WithAlpha(.18f), new EllipsePolygon(center.X - radius * .28f, center.Y - radius * .18f, radius * .20f));
        ctx.Fill(Color.ParseHex("#C9B88F").WithAlpha(.13f), new EllipsePolygon(center.X + radius * .25f, center.Y + radius * .08f, radius * .16f));
        ctx.Fill(Color.ParseHex("#EEE0B8").WithAlpha(.18f), new EllipsePolygon(center.X - radius * .02f, center.Y + radius * .28f, radius * .12f));
    }

    private static void DrawMoonLabelAndGuide(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, int width, int height, bool shortForm, PointF center, float radius)
    {
        var fonts = shortForm ? EditorialFonts.CreateScaled(.86f) : EditorialFonts.Create();
        var label = ResolveMoonLabel(spec);
        var labelX = Math.Clamp(center.X - radius * 1.22f, 48f, width - 420f);
        var labelY = Math.Clamp(center.Y + radius + (shortForm ? 38f : 24f), 80f, height - 230f);
        ctx.DrawText(new RichTextOptions(fonts.SmallFont) { Origin = new PointF(labelX, labelY), WrappingLength = shortForm ? 360 : 460 }, label, Color.ParseHex("#FFF6D7"));
        if (spec.QuestionType.Equals("Where", StringComparison.OrdinalIgnoreCase) || spec.QuestionType.Equals("Action", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Draw(Color.ParseHex("#F6C177").WithAlpha(.82f), shortForm ? 4 : 5, new PathBuilder().AddLine(new PointF(center.X - radius * 1.65f, center.Y + radius * 1.05f), new PointF(center.X - radius * .72f, center.Y + radius * .45f)).Build());
            ctx.DrawText(new RichTextOptions(fonts.SmallFont) { Origin = new PointF(Math.Max(42, center.X - radius * 2.15f), center.Y + radius * 1.12f), WrappingLength = 260 }, "E horizon", Color.ParseHex("#F6C177"));
        }
    }

    private static string ResolveMoonAssetKey(QuestionDrivenVisualSpec spec)
        => FirstNonEmpty(
            spec.DrawableVisualObjects?.FirstOrDefault(obj => obj.ObjectType.Equals("Moon", StringComparison.OrdinalIgnoreCase))?.AssetKey,
            TryGetVisualMetadata(spec, "assetKey", out var metadataAssetKey) ? metadataAssetKey : null,
            "Moon.FullMoon");

    private string? ResolveLocalMoonAssetPath(string assetKey)
    {
        if (!assetKey.Equals("Moon.FullMoon", StringComparison.OrdinalIgnoreCase)) return null;

        foreach (var candidate in EnumerateMoonAssetCandidates())
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private IEnumerable<string> EnumerateMoonAssetCandidates()
    {
        foreach (var fileName in new[] { "hero-transparent.png", "hero.png", "full-moon-transparent.png", "full-moon.png" })
            yield return _assetPathResolver.ResolveCelestialAssetPath("moon", fileName);

        if (!_useRepositoryAssetDiscovery) yield break;

        var current = Directory.GetCurrentDirectory();
        for (var directory = new DirectoryInfo(current); directory is not null; directory = directory.Parent)
        {
            foreach (var fileName in new[] { "hero-transparent.png", "hero.png", "full-moon-transparent.png", "full-moon.png" })
            {
                yield return Path.Combine(directory.FullName, "Backend", "src", "Astronomy.MediaFactory.Api", "assets", "celestial", "moon", fileName);
                yield return Path.Combine(directory.FullName, "assets", "celestial", "moon", fileName);
            }
        }
    }

    private static bool IsDebugFallbackEnabled(QuestionDrivenVisualSpec spec)
        => TryGetVisualMetadataBool(spec, "DebugFallbackEnabled", out var enabled) && enabled;

    private static bool TryGetVisualMetadata(QuestionDrivenVisualSpec spec, string key, out string value)
    {
        value = string.Empty;
        if (spec.StrategyValidationFacts is not null && TryGetDictionaryValue(spec.StrategyValidationFacts, key, out value)) return true;
        if (spec.VisualSourceResolution?.Metadata is not null && TryGetDictionaryValue(spec.VisualSourceResolution.Metadata, key, out value)) return true;
        return false;
    }

    private static bool TryGetVisualMetadataBool(QuestionDrivenVisualSpec spec, string key, out bool value)
    {
        value = false;
        return TryGetVisualMetadata(spec, key, out var text) && bool.TryParse(text, out value);
    }

    private static bool TryGetDictionaryValue(IReadOnlyDictionary<string, string> dictionary, string key, out string value)
    {
        foreach (var pair in dictionary)
        {
            if (!pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            value = pair.Value;
            return !string.IsNullOrWhiteSpace(value);
        }
        value = string.Empty;
        return false;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static PointF ResolveMoonCenter(int sceneNumber, int width, int height, bool shortForm)
    {
        var x = sceneNumber switch { 2 or 4 => .34f, 3 => .68f, 5 => .58f, 6 => .50f, _ => .56f };
        var y = shortForm
            ? sceneNumber switch { 2 => .38f, 3 => .32f, 4 => .36f, 5 => .35f, 6 => .34f, _ => .31f }
            : sceneNumber switch { 2 => .39f, 3 => .31f, 4 => .38f, 5 => .34f, 6 => .33f, _ => .30f };
        return new PointF(width * x, height * y);
    }

    private static float ResolveMoonRadius(int sceneNumber, int width, bool shortForm)
    {
        var baseRadius = shortForm ? width * .145f : width * .078f;
        return sceneNumber switch { 1 => baseRadius * 1.18f, 5 => baseRadius * 1.06f, 6 => baseRadius * 1.10f, _ => baseRadius };
    }

    private async Task<Image<Rgba32>> RenderApprovedLongFormAsync(QuestionDrivenVisualSpec spec, string venusAssetPath, string jupiterAssetPath, CancellationToken cancellationToken)
    {
        var image = await AstronomyVisualCompositionEngine.ComposeAsync(new AstronomyVisualCompositionRequest(
            AstronomyInfographicRenderVariant.LongForm.Width,
            AstronomyInfographicRenderVariant.LongForm.Height,
            spec.ViewerQuestion,
            spec.ViewerTakeaway,
            spec.QuestionType,
            spec.UsesLocalPlanetAssets ? BuildPlanetAssets(venusAssetPath, jupiterAssetPath) : [],
            mood: IsMeteorVisual(spec) ? "DarkMeteorShowerScene" : "WarmTwilightQuestionScene",
            westMarkerLabel: string.Empty,
            starDensity: 720,
            showReferenceOverlays: false,
            compositionMode: AstronomyVisualCompositionMode.SceneInfographic), cancellationToken);
        var fonts = EditorialFonts.Create();
        image.Mutate(ctx =>
        {
            backgroundLayer.RenderEventSpecificForeground(ctx, spec);
            DrawResolvedCelestialObjects(ctx, spec, AstronomyInfographicRenderVariant.LongForm.Width, AstronomyInfographicRenderVariant.LongForm.Height);
            DrawNamedFullMoonVisual(ctx, spec, AstronomyInfographicRenderVariant.LongForm.Width, AstronomyInfographicRenderVariant.LongForm.Height);
            skyGuidanceLayer.Render(ctx, spec, fonts);
            if (spec.UsesLocalPlanetAssets && File.Exists(venusAssetPath) && File.Exists(jupiterAssetPath)) celestialObjectLayer.Render(ctx, spec, venusAssetPath, jupiterAssetPath);
            educationalLayer.Render(ctx, spec, fonts);
            annotationLayer.Render(ctx, spec, fonts);
            backgroundLayer.RenderVignette(ctx);
        });

        return image;
    }


    private void DrawResolvedCelestialObjects(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, int width, int height)
    {
        if (spec.UsesLocalPlanetAssets || IsNamedFullMoonVisual(spec)) return;
        var objects = (spec.DrawableVisualObjects ?? [])
            .Where(obj => !string.IsNullOrWhiteSpace(obj.ObjectType) && !IsConceptualDrawableRequirement(obj.ObjectType))
            .DistinctBy(obj => obj.ObjectType, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (objects.Length == 0) return;

        var shortForm = width < 1400;
        var y = shortForm ? height * .42f : height * .43f;
        var minX = shortForm ? width * .20f : width * .55f;
        var maxX = shortForm ? width * .80f : width * .88f;
        var span = Math.Max(1, objects.Length - 1);
        var centers = objects.Select((obj, index) => new PointF(
            objects.Length == 1 ? (minX + maxX) / 2f : minX + (maxX - minX) * index / span,
            y + (float)Math.Sin(index * 1.35f) * (shortForm ? 46f : 34f))).ToArray();

        if (IsPlanetGroupingSpec(spec) && centers.Length > 1)
        {
            var path = new PathBuilder();
            path.AddLine(centers[0], centers[^1]);
            ctx.Draw(Color.ParseHex("#B7E0FF").WithAlpha(.46f), shortForm ? 4f : 3f, path.Build());
        }

        for (var i = 0; i < objects.Length; i++)
        {
            var obj = objects[i];
            var center = centers[i];
            var diameter = ResolveDrawableObjectDiameter(obj.ObjectType, shortForm, width);
            var assetPath = ResolveLocalCelestialAssetPath(obj.AssetKey, obj.ObjectType);
            var glowColor = ResolvePlanetGlowColor(obj.ObjectType);
            DrawResolvedObjectGlow(ctx, center, diameter, glowColor);
            if (!string.IsNullOrWhiteSpace(assetPath)) DrawResolvedObjectAsset(ctx, assetPath, center, diameter);
            else DrawTexturedFallbackPlanet(ctx, obj.ObjectType, center, diameter, glowColor);
            DrawResolvedObjectLabel(ctx, obj.Label ?? obj.ObjectType, center, diameter, shortForm);
        }
    }

    private static bool IsPlanetGroupingSpec(QuestionDrivenVisualSpec spec)
        => spec.EventType.Equals("PlanetGrouping", StringComparison.OrdinalIgnoreCase)
            || spec.EventType.Equals("PLANET_GROUPING", StringComparison.OrdinalIgnoreCase)
            || spec.StrategyId?.Equals("PlanetGrouping", StringComparison.OrdinalIgnoreCase) == true
            || spec.StrategyId?.Equals("PLANET_GROUPING", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsConceptualDrawableRequirement(string value)
        => value.Contains("streak", StringComparison.OrdinalIgnoreCase)
            || value.Contains("dark sky", StringComparison.OrdinalIgnoreCase)
            || value.Contains("radiant", StringComparison.OrdinalIgnoreCase)
            || value.Contains("glow", StringComparison.OrdinalIgnoreCase)
            || value.Contains("moonrise", StringComparison.OrdinalIgnoreCase)
            || value.Contains("close pairing", StringComparison.OrdinalIgnoreCase)
            || value.Contains("planet grouping", StringComparison.OrdinalIgnoreCase)
            || value.Contains("guided scan path", StringComparison.OrdinalIgnoreCase)
            || value.Contains("grouping arc", StringComparison.OrdinalIgnoreCase);

    private static int ResolveDrawableObjectDiameter(string objectType, bool shortForm, int width)
    {
        var baseSize = shortForm ? width * .095f : width * .045f;
        var factor = objectType.ToLowerInvariant() switch
        {
            "jupiter" => 1.22f,
            "saturn" => 1.18f,
            "venus" => .92f,
            "mars" => .86f,
            _ => 1f
        };
        return (int)MathF.Round(baseSize * factor);
    }

    private string? ResolveLocalCelestialAssetPath(string? assetKey, string objectType)
    {
        var normalized = NormalizeCelestialAssetName(FirstNonEmpty(assetKey?.Replace("Planet.", string.Empty, StringComparison.OrdinalIgnoreCase), objectType));
        foreach (var directory in EnumerateRepositoryDirectories())
        {
            foreach (var fileName in new[] { "hero-transparent.png", "hero.png" })
            {
                var apiPath = Path.Combine(directory.FullName, "Backend", "src", "Astronomy.MediaFactory.Api", "assets", "celestial", normalized, fileName);
                if (File.Exists(apiPath)) return apiPath;
                var rootPath = Path.Combine(directory.FullName, "assets", "celestial", normalized, fileName);
                if (File.Exists(rootPath)) return rootPath;
            }
        }
        return null;
    }

    private static string NormalizeCelestialAssetName(string value)
        => value.Trim().ToLowerInvariant().Replace(' ', '-');

    private static string ResolvePlanetGlowColor(string objectType)
        => objectType.ToLowerInvariant() switch
        {
            "saturn" => "#E8C98F",
            "mars" => "#E0734A",
            "jupiter" => "#E5C18D",
            "venus" => "#FFF2B8",
            "mercury" => "#C9BCA8",
            "uranus" => "#9FE6E8",
            "neptune" => "#79A7FF",
            _ => "#B7E0FF"
        };

    private static void DrawResolvedObjectGlow(IImageProcessingContext ctx, PointF center, int diameter, string color)
    {
        ctx.Fill(Color.ParseHex(color).WithAlpha(.040f), new EllipsePolygon(center.X, center.Y, diameter * 1.25f));
        ctx.Fill(Color.ParseHex(color).WithAlpha(.065f), new EllipsePolygon(center.X, center.Y, diameter * .82f));
    }

    private static void DrawResolvedObjectAsset(IImageProcessingContext ctx, string assetPath, PointF center, int diameter)
    {
        using var asset = Image.Load<Rgba32>(assetPath);
        asset.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(diameter, diameter), Mode = ResizeMode.Max }));
        ctx.DrawImage(asset, new Point((int)(center.X - asset.Width / 2f), (int)(center.Y - asset.Height / 2f)), 1f);
    }

    private static void DrawTexturedFallbackPlanet(IImageProcessingContext ctx, string objectType, PointF center, int diameter, string color)
    {
        var baseColor = Color.ParseHex(color);
        ctx.Fill(baseColor.WithAlpha(.82f), new EllipsePolygon(center.X, center.Y, diameter / 2f));
        ctx.Fill(Color.White.WithAlpha(.14f), new EllipsePolygon(center.X - diameter * .15f, center.Y - diameter * .18f, diameter * .18f));
        ctx.Draw(baseColor.WithAlpha(.70f), Math.Max(2, diameter * .04f), new EllipsePolygon(center.X, center.Y, diameter / 2f));
        if (objectType.Equals("Saturn", StringComparison.OrdinalIgnoreCase))
            ctx.Draw(Color.ParseHex("#E8C98F").WithAlpha(.70f), Math.Max(2, diameter * .05f), new EllipsePolygon(center.X, center.Y, diameter * .78f, diameter * .27f));
    }

    private static void DrawResolvedObjectLabel(IImageProcessingContext ctx, string label, PointF center, int diameter, bool shortForm)
    {
        var font = shortForm ? EditorialFonts.CreateScaled(.82f).SmallFont : EditorialFonts.Create().SmallFont;
        var origin = new PointF(center.X - diameter * .72f, center.Y + diameter * .62f);
        ctx.DrawText(new RichTextOptions(font) { Origin = origin, WrappingLength = diameter * 2.2f }, label, Color.White.WithAlpha(.94f));
        ctx.Draw(Color.White.WithAlpha(.36f), 1.5f, new PathBuilder().AddLine(new PointF(center.X, center.Y + diameter * .34f), new PointF(origin.X + 8, origin.Y - 4)).Build());
    }

    private async Task<Image<Rgba32>> RenderNativeShortFormAsync(QuestionDrivenVisualSpec spec, string venusAssetPath, string jupiterAssetPath, CancellationToken cancellationToken)
    {
        var variant = AstronomyInfographicRenderVariant.ShortForm;
        var image = await AstronomyVisualCompositionEngine.ComposeAsync(new AstronomyVisualCompositionRequest(
            variant.Width,
            variant.Height,
            GetShortFormCopy(spec).Title,
            GetShortFormCopy(spec).Subtitle,
            spec.QuestionType,
            spec.UsesLocalPlanetAssets ? BuildPlanetAssets(venusAssetPath, jupiterAssetPath) : [],
            mood: IsMeteorVisual(spec) ? "DarkMeteorShowerScene" : "WarmTwilightQuestionScene",
            westMarkerLabel: string.Empty,
            starDensity: 520,
            showReferenceOverlays: false,
            compositionMode: AstronomyVisualCompositionMode.SceneInfographic), cancellationToken);

        var fonts = EditorialFonts.CreateScaled(variant.TextScale);
        image.Mutate(ctx =>
        {
            DrawShortFormAtmosphere(ctx, spec.SceneNumber, variant.Width, variant.Height);
            DrawNativeShortFormVisual(ctx, spec, venusAssetPath, jupiterAssetPath);
            DrawResolvedCelestialObjects(ctx, spec, variant.Width, variant.Height);
            DrawNamedFullMoonVisual(ctx, spec, variant.Width, variant.Height);
            DrawNativeShortFormText(ctx, spec, fonts, variant);
            DrawPortraitVignette(ctx, variant.Width, variant.Height);
        });

        return image;
    }

    private static void DrawShortFormAtmosphere(IImageProcessingContext ctx, int sceneNumber, int width, int height)
    {
        ctx.Fill(Color.ParseHex("#050915").WithAlpha(.10f), new RectangleF(0, 0, width, height));
        ctx.Fill(Color.Black.WithAlpha(.16f), new RectangleF(0, 0, width, 112));
        ctx.Fill(Color.Black.WithAlpha(.18f), new RectangleF(0, height - 178, width, 178));

        if (sceneNumber == 6)
        {
            ctx.Fill(new LinearGradientBrush(new PointF(0, height * .54f), new PointF(0, height), GradientRepetitionMode.None,
                new ColorStop(0f, Color.Transparent),
                new ColorStop(.52f, Color.ParseHex("#B55D33").WithAlpha(.12f)),
                new ColorStop(.78f, Color.ParseHex("#F6A65D").WithAlpha(.18f)),
                new ColorStop(1f, Color.ParseHex("#05040A").WithAlpha(.30f))),
                new RectangleF(0, height * .54f, width, height * .46f));
            ctx.Fill(Color.ParseHex("#06040A").WithAlpha(.72f), new RectangleF(0, height * .905f, width, height * .095f));
        }
    }

    private static void DrawNativeShortFormVisual(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, string venusAssetPath, string jupiterAssetPath)
    {
        var layout = GetNativeShortFormLayout(spec.QuestionType);
        if (IsMeteorVisual(spec))
        {
            DrawPortraitMeteorVisual(ctx, spec.SceneNumber);
        }
        else if (spec.UsesLocalPlanetAssets && !spec.QuestionType.Equals("when", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(venusAssetPath) && File.Exists(jupiterAssetPath))
            {
                DrawPortraitPlanetGlow(ctx, layout.Venus.Center, layout.Venus.Diameter, "#FFF2B8", .080f);
                DrawPortraitPlanetGlow(ctx, layout.Jupiter.Center, layout.Jupiter.Diameter, "#E5C18D", .060f);
                DrawPortraitAsset(ctx, venusAssetPath, layout.Venus.Center, layout.Venus.Diameter, "#FFF2B8");
                DrawPortraitAsset(ctx, jupiterAssetPath, layout.Jupiter.Center, layout.Jupiter.Diameter, "#E5C18D");
            }
        }

        if (!spec.UsesLocalPlanetAssets) return;

        switch (spec.QuestionType.ToLowerInvariant())
        {
            case "where":
                DrawPortraitWestIndicator(ctx);
                break;
            case "when":
                DrawPortraitBestTimeCard(ctx);
                break;
            case "how":
                DrawPortraitGuideLine(ctx, layout.Venus.Center, layout.Jupiter.Center);
                break;
            case "why":
                DrawPortraitClosenessCue(ctx, layout.Venus.Center, layout.Jupiter.Center);
                break;
            case "action":
                DrawPortraitClosingHorizon(ctx);
                break;
        }
    }

    private static void DrawNativeShortFormText(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, EditorialFonts fonts, AstronomyInfographicRenderVariant variant)
    {
        var copy = GetShortFormCopy(spec);
        const float margin = 72f;
        var textWidth = variant.Width - margin * 2f;
        ctx.DrawText(new RichTextOptions(fonts.TitleFont) { Origin = new PointF(margin, 120), WrappingLength = textWidth }, copy.Title, Color.White);
        ctx.DrawText(new RichTextOptions(fonts.SubtitleFont) { Origin = new PointF(margin, 236), WrappingLength = textWidth }, copy.Subtitle, Color.ParseHex("#F6C177"));
        var captionFonts = EditorialFonts.CreateScaled(variant.TextScale * .88f);
        ctx.DrawText(new RichTextOptions(captionFonts.SmallFont) { Origin = new PointF(margin, 1572), WrappingLength = textWidth }, copy.Caption, Color.White.WithAlpha(.94f));
        ctx.DrawText(new RichTextOptions(captionFonts.SmallFont) { Origin = new PointF(margin, 1652), WrappingLength = textWidth }, spec.ViewerQuestion, Color.ParseHex("#B7E0FF").WithAlpha(.90f));
    }

    private static (string Title, string Subtitle, string Caption) GetShortFormCopy(QuestionDrivenVisualSpec spec)
    {
        if (IsMeteorVisual(spec))
        {
            return spec.QuestionType.ToLowerInvariant() switch
            {
                "what" => ("Meteor Shower Peak", "Dark sky alert", "Meteor streaks radiate from a subtle shower radiant."),
                "where" => ("Look East to Overhead", "Dark open sky", "Use the whole dark sky and a subtle radiant hint."),
                "when" => ("Best Window", "Midnight to pre-dawn", "Meteor activity is best under a dark sky."),
                "how" => ("No Telescope", "Let eyes adapt", "Avoid city lights and watch the open sky."),
                "why" => ("Strong Annual Shower", "Low Moon interference", "Dark skies help faint meteor streaks stand out."),
                "action" => ("Set a Reminder", "Check weather", "Pick a dark landscape and watch overhead."),
                _ => (spec.ViewerQuestion, spec.ViewerTakeaway, spec.CaptionText)
            };
        }

        if (!spec.UsesLocalPlanetAssets) return (spec.ViewerQuestion, spec.ViewerTakeaway, spec.CaptionText);

        return spec.QuestionType.ToLowerInvariant() switch
    {
        "what" => ("Venus & Jupiter", "After sunset", "Venus and Jupiter shine close tonight."),
        "where" => ("Look West", "Venus + Jupiter", "Look west, about one-third above the horizon."),
        "when" => ("Best Time", "7:23 PM IST", "Best around 7:23 PM IST after sunset."),
        "how" => ("Find Venus First", "Then Jupiter nearby", "Find Venus first, then Jupiter nearby."),
        "why" => ("Bright Pair Tonight", "Two bright planets", "Two bright planets make a rare-looking pair."),
        "action" => ("Step Outside Tonight", "Look west", "Clear skies? Step outside and look west."),
        _ => (spec.ViewerQuestion, spec.ViewerTakeaway, spec.CaptionText)
    };
    }

    private static PlanetPairPlacement GetNativeShortFormLayout(string questionType) => questionType.ToLowerInvariant() switch
    {
        "what" => new(new(new PointF(470, 820), 118), new(new PointF(635, 890), 82)),
        "where" => new(new(new PointF(506, 838), 96), new(new PointF(626, 888), 68)),
        "how" => new(new(new PointF(430, 840), 108), new(new PointF(650, 910), 74)),
        "why" => new(new(new PointF(485, 832), 112), new(new PointF(588, 862), 90)),
        "action" => new(new(new PointF(486, 772), 112), new(new PointF(620, 824), 82)),
        _ => new(new(new PointF(-100, -100), 1), new(new PointF(-100, -100), 1))
    };

    private static void DrawPortraitAsset(IImageProcessingContext ctx, string assetPath, PointF center, int diameter, string glowColor)
    {
        using var asset = Image.Load<Rgba32>(assetPath);
        asset.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(diameter, diameter), Mode = ResizeMode.Max }).GaussianBlur(.15f));
        var x = (int)MathF.Round(center.X - asset.Width / 2f);
        var y = (int)MathF.Round(center.Y - asset.Height / 2f);
        ctx.Fill(Color.ParseHex(glowColor).WithAlpha(.12f), new EllipsePolygon(center.X, center.Y, diameter * .66f, diameter * .66f));
        ctx.DrawImage(asset, new Point(x, y), 1f);
    }

    private static void DrawPortraitPlanetGlow(IImageProcessingContext ctx, PointF center, int diameter, string color, float alpha)
    {
        ctx.Fill(Color.ParseHex(color).WithAlpha(alpha * .36f), new EllipsePolygon(center.X, center.Y, diameter * 2.4f, diameter * 1.75f));
        ctx.Fill(Color.ParseHex(color).WithAlpha(alpha), new EllipsePolygon(center.X, center.Y, diameter * 1.25f, diameter * 1.25f));
    }

    private static void DrawPortraitGuideLine(IImageProcessingContext ctx, PointF from, PointF to)
    {
        ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(.80f), 5, new PathBuilder().AddLine(from, to).Build());
        var arrow = new PathBuilder().AddLine(to, new PointF(to.X - 24, to.Y - 20)).AddLine(to, new PointF(to.X - 10, to.Y - 32)).Build();
        ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(.80f), 5, arrow);
    }

    private static void DrawPortraitClosenessCue(IImageProcessingContext ctx, PointF a, PointF b)
    {
        ctx.Draw(Color.ParseHex("#FFF1C4").WithAlpha(.42f), 5, new PathBuilder().AddLine(new PointF(a.X + 42, a.Y + 72), new PointF(b.X - 34, b.Y + 72)).Build());
        DrawPortraitText(ctx, "together", (a.X + b.X) / 2f - 88, Math.Max(a.Y, b.Y) + 116, 220, Color.ParseHex("#FFF2B8"), 30, FontStyle.Bold);
    }

    private static void DrawPortraitWestIndicator(IImageProcessingContext ctx)
    {
        DrawPortraitText(ctx, "LOOK WEST", 72, 1194, 330, Color.ParseHex("#B7E0FF"), 38, FontStyle.Bold);
        ctx.Draw(Color.ParseHex("#B7E0FF").WithAlpha(.42f), 4, new PathBuilder().AddLine(new PointF(72, 1250), new PointF(252, 1250)).Build());
    }

    private static void DrawPortraitBestTimeCard(IImageProcessingContext ctx)
    {
        DrawPortraitText(ctx, "BEST TIME", 330, 712, 420, Color.White, 42, FontStyle.Bold);
        DrawPortraitText(ctx, "7:23 PM IST", 258, 802, 600, Color.ParseHex("#FFF2B8"), 66, FontStyle.Bold);
        DrawPortraitText(ctx, "Best viewing time", 330, 910, 420, Color.ParseHex("#F6C177"), 38, FontStyle.Bold);
    }

    private static void DrawPortraitClosingHorizon(IImageProcessingContext ctx)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, 1210), new PointF(0, 1840), GradientRepetitionMode.None,
            new ColorStop(0f, Color.Transparent),
            new ColorStop(.46f, Color.ParseHex("#D77A42").WithAlpha(.16f)),
            new ColorStop(1f, Color.ParseHex("#05040A").WithAlpha(.38f))),
            new RectangleF(0, 1210, 1080, 630));
        ctx.Fill(Color.ParseHex("#05040A").WithAlpha(.82f), new RectangleF(0, 1746, 1080, 174));
    }

    private static void DrawPortraitMeteorVisual(IImageProcessingContext ctx, int sceneNumber)
    {
        var random = new Random(53000 + sceneNumber);
        for (var i = 0; i < 9; i++)
        {
            var x = 130 + random.Next(780);
            var y = 500 + random.Next(690);
            var length = 110 + random.Next(190);
            var to = new PointF(x + length, y - length * .42f);
            ctx.Draw(Color.White.WithAlpha(.56f), 3, new PathBuilder().AddLine(new PointF(x, y), to).Build());
            ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(.20f), 8, new PathBuilder().AddLine(new PointF(x - 8, y + 4), to).Build());
        }
        ctx.Draw(Color.ParseHex("#B7E0FF").WithAlpha(.36f), 3, new EllipsePolygon(590, 720, 84, 52));
        DrawPortraitText(ctx, "subtle radiant", 492, 790, 260, Color.ParseHex("#B7E0FF"), 26, FontStyle.Bold);
    }

    private static bool IsMeteorVisual(QuestionDrivenVisualSpec spec)
        => spec.EventType.Equals("MeteorShower", StringComparison.OrdinalIgnoreCase)
            || spec.BackgroundPrompt.Contains("meteor", StringComparison.OrdinalIgnoreCase)
            || spec.ProgrammaticLayers.Any(layer => layer.Contains("meteor", StringComparison.OrdinalIgnoreCase));

    private static void DrawPortraitText(IImageProcessingContext ctx, string text, float x, float y, float width, Color color, float size, FontStyle style)
    {
        var scale = Math.Max(.5f, size / 24f);
        var fonts = EditorialFonts.CreateScaled(scale);
        var font = style == FontStyle.Bold ? fonts.SmallFont : fonts.SmallFont;
        ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(x, y), WrappingLength = width }, text, color);
    }

    private static void DrawPortraitVignette(IImageProcessingContext ctx, int width, int height)
    {
        using var vignette = new Image<Rgba32>(width, height, Color.Transparent);
        vignette.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var yRatio = y / (float)(accessor.Height - 1);
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var xRatio = x / (float)(accessor.Width - 1);
                    var edge = MathF.Max(MathF.Abs(xRatio - .5f) / .5f, MathF.Max(1f - yRatio, yRatio) * .74f);
                    row[x] = new Rgba32(0, 0, 0, (byte)Math.Clamp((edge - .60f) * 72f, 0f, 42f));
                }
            }
        });
        ctx.DrawImage(vignette, 1f);
    }
}

public sealed record ShortFormCompositionDecision(bool NativeComposerUsed, bool UsesLongFormImage, bool DrawsInnerFrame);

public sealed class AstronomyBackgroundLayerRenderer
{
    private const int CanvasWidth = 1920;
    private const int CanvasHeight = 1080;

    public void Render(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec)
    {
        var sceneNumber = spec.SceneNumber;
        RenderSmoothSky(ctx, sceneNumber, IsMeteorVisual(spec));
        RenderStars(ctx, sceneNumber);
        if (IsMeteorVisual(spec)) RenderMeteorStreaks(ctx, sceneNumber);
        RenderLandscape(ctx, sceneNumber, IsMeteorVisual(spec));
    }

    private static bool IsMeteorVisual(QuestionDrivenVisualSpec spec)
        => spec.EventType.Equals("MeteorShower", StringComparison.OrdinalIgnoreCase)
            || spec.BackgroundPrompt.Contains("meteor", StringComparison.OrdinalIgnoreCase)
            || spec.ProgrammaticLayers.Any(layer => layer.Contains("meteor", StringComparison.OrdinalIgnoreCase));

    public void RenderEventSpecificForeground(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec)
    {
        if (IsMeteorVisual(spec)) RenderMeteorStreaks(ctx, spec.SceneNumber);
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

    private static void RenderSmoothSky(IImageProcessingContext ctx, int sceneNumber, bool meteorVisual)
    {
        var stops = meteorVisual ? GetMeteorSkyStops(sceneNumber) : GetSkyStops(sceneNumber);
        var random = new Random(18400 + sceneNumber);
        var horizonHazeColor = Color.ParseHex("#FFF1C4").ToPixel<Rgba32>();
        var upperHazeColor = Color.ParseHex("#8FD2FF").ToPixel<Rgba32>();
        var glowColor = Color.ParseHex(sceneNumber is 1 or 3 or 6 ? "#FF9A45" : "#B7E0FF").ToPixel<Rgba32>();
        var warmHorizonStrength = meteorVisual ? 0.10f : sceneNumber switch { 1 => 1.42f, 6 => 1.34f, 3 => 1f, 5 => 0.54f, _ => 0.35f };
        var horizonHazeStrength = sceneNumber switch { 1 => 0.155f, 6 => 0.148f, 5 => 0.072f, _ => 0.10f };
        var focalGlowStrength = sceneNumber switch { 1 => 0.175f, 6 => 0.145f, 3 => 0.11f, 5 => 0.070f, _ => 0.032f };
        var upperHazeStrength = sceneNumber switch { 1 => 0.032f, 5 => 0.025f, 6 => 0.030f, _ => 0.018f };
        using var sky = new Image<Rgba32>(CanvasWidth, CanvasHeight);

        sky.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var yRatio = y / (float)(CanvasHeight - 1);
                var baseColor = InterpolateStops(stops, yRatio);
                var horizonLift = SmoothStep(0.58f, 0.92f, yRatio) * (1f - SmoothStep(0.92f, 1f, yRatio));
                var coolUpperHaze = 1f - SmoothStep(0.18f, 0.72f, Math.Abs(yRatio - 0.44f));
                var row = accessor.GetRowSpan(y);

                for (var x = 0; x < row.Length; x++)
                {
                    var xRatio = x / (float)(CanvasWidth - 1);
                    var color = baseColor;
                    color = Composite(color, horizonHazeColor, horizonLift * warmHorizonStrength * horizonHazeStrength);
                    color = Composite(color, upperHazeColor, coolUpperHaze * upperHazeStrength);

                    var glowFalloff = 1f - Math.Clamp(MathF.Abs(xRatio - 0.52f) / 0.58f, 0f, 1f);
                    var glow = horizonLift * SmoothStep(0f, 1f, glowFalloff) * focalGlowStrength;
                    color = Composite(color, glowColor, glow);

                    if (sceneNumber is 1 or 6)
                    {
                        var orangeShoulder = SmoothStep(0.46f, 0.86f, yRatio) * (1f - SmoothStep(0.97f, 1f, yRatio)) * (0.65f + 0.35f * glowFalloff);
                        color = Composite(color, Color.ParseHex("#FF7A38").ToPixel<Rgba32>(), orangeShoulder * 0.030f);
                    }
                    else if (sceneNumber == 5)
                    {
                        var editorialDepth = SmoothStep(0.22f, 0.68f, yRatio) * (1f - SmoothStep(0.78f, 1f, yRatio));
                        color = Composite(color, Color.ParseHex("#243F7A").ToPixel<Rgba32>(), editorialDepth * 0.030f);
                    }

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

    private static SkyColorStop[] GetMeteorSkyStops(int sceneNumber) =>
    [
        new(0f, Color.ParseHex("#02040B")),
        new(.38f, Color.ParseHex(sceneNumber is 5 ? "#07112A" : "#050B1B")),
        new(.72f, Color.ParseHex("#07101D")),
        new(1f, Color.ParseHex("#02040A"))
    ];

    private static void RenderMeteorStreaks(IImageProcessingContext ctx, int sceneNumber)
    {
        var random = new Random(44000 + sceneNumber);
        var count = sceneNumber is 1 or 5 or 6 ? 13 : 8;
        var radiant = new PointF(sceneNumber == 2 ? 1180 : 1040, sceneNumber == 2 ? 280 : 245);
        ctx.Draw(Color.ParseHex("#B7E0FF").WithAlpha(.24f), 2, new EllipsePolygon(radiant.X, radiant.Y, 58, 38));
        for (var i = 0; i < count; i++)
        {
            var angle = -0.92f + (float)(random.NextDouble() * .42 - .21);
            var length = 85 + random.Next(210);
            var start = new PointF(180 + random.Next(CanvasWidth - 360), 95 + random.Next(590));
            var end = new PointF(start.X + MathF.Cos(angle) * length, start.Y + MathF.Sin(angle) * length);
            ctx.Draw(Color.White.WithAlpha(.62f), 2, new PathBuilder().AddLine(start, end).Build());
            ctx.Draw(Color.ParseHex("#8FD2FF").WithAlpha(.16f), 7, new PathBuilder().AddLine(new PointF(start.X - 5, start.Y + 3), end).Build());
        }
    }

    private static SkyColorStop[] GetSkyStops(int sceneNumber) => sceneNumber switch
    {
        1 =>
        [
            new(0f, Color.ParseHex("#040B1B")),
            new(.34f, Color.ParseHex("#142653")),
            new(.62f, Color.ParseHex("#433F72")),
            new(.82f, Color.ParseHex("#B85F4E")),
            new(.94f, Color.ParseHex("#F08A40")),
            new(1f, Color.ParseHex("#FFB35E"))
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
            new(0f, Color.ParseHex("#01040C")),
            new(.42f, Color.ParseHex("#061638")),
            new(.70f, Color.ParseHex("#102855")),
            new(.90f, Color.ParseHex("#17253E")),
            new(1f, Color.ParseHex("#151E2C"))
        ],
        6 =>
        [
            new(0f, Color.ParseHex("#020817")),
            new(.32f, Color.ParseHex("#111F46")),
            new(.58f, Color.ParseHex("#393F70")),
            new(.79f, Color.ParseHex("#A95B54")),
            new(.93f, Color.ParseHex("#E8793E")),
            new(1f, Color.ParseHex("#FFAD58"))
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
        if (sceneNumber is 1 or 5 or 6)
        {
            RenderNaturalStarfield(ctx, sceneNumber);
            return;
        }

        var stars = sceneNumber switch
        {
            2 => new[] { new PointF(520, 275), new PointF(675, 360), new PointF(810, 295), new PointF(1005, 386), new PointF(1225, 315), new PointF(1420, 430), new PointF(1515, 265), new PointF(610, 610), new PointF(1350, 650) },
            5 => new[] { new PointF(300, 145), new PointF(520, 265), new PointF(770, 155), new PointF(1210, 220), new PointF(1515, 125), new PointF(1710, 310), new PointF(420, 430), new PointF(1445, 475), new PointF(980, 150) },
            _ => new[] { new PointF(250, 150), new PointF(475, 250), new PointF(745, 120), new PointF(990, 205), new PointF(1320, 145), new PointF(1610, 260), new PointF(1780, 95), new PointF(1185, 330), new PointF(380, 370), new PointF(1540, 360), new PointF(720, 315) }
        };
        foreach (var star in stars) ctx.Fill(Color.White.WithAlpha(sceneNumber is 1 or 6 ? .34f : .58f), new EllipsePolygon(star.X, star.Y, sceneNumber is 2 ? 2.2f : 1.5f));
        if (sceneNumber == 2) ctx.Draw(Color.White.WithAlpha(.18f), 2, new PathBuilder().AddLine(stars[0], stars[1]).AddLine(stars[1], stars[2]).AddLine(stars[2], stars[3]).AddLine(stars[3], stars[4]).Build());
    }

    private static void RenderNaturalStarfield(IImageProcessingContext ctx, int sceneNumber)
    {
        var random = new Random(91000 + sceneNumber);
        var baseCount = sceneNumber switch { 1 => 46, 5 => 62, 6 => 42, _ => 34 };
        var maxY = sceneNumber == 5 ? 620 : 500;
        var minimumAlpha = sceneNumber == 5 ? 0.22f : 0.15f;
        var maximumAlpha = sceneNumber == 5 ? 0.74f : 0.48f;

        for (var i = 0; i < baseCount; i++)
        {
            var xCluster = i % 5 == 0 ? 0.62f : i % 7 == 0 ? 0.28f : (float)random.NextDouble();
            var x = Math.Clamp((float)(xCluster * CanvasWidth + random.NextDouble() * 210 - 105), 60f, CanvasWidth - 60f);
            var y = (float)(70 + random.NextDouble() * maxY);
            var magnitude = (float)Math.Pow(random.NextDouble(), 1.85);
            var radius = 0.75f + magnitude * (sceneNumber == 5 ? 1.45f : 1.15f);
            var alpha = minimumAlpha + magnitude * (maximumAlpha - minimumAlpha);

            ctx.Fill(Color.White.WithAlpha(alpha), new EllipsePolygon(x, y, radius));
            if (magnitude > 0.82f)
            {
                ctx.Fill(Color.ParseHex("#DCEBFF").WithAlpha(alpha * 0.18f), new EllipsePolygon(x, y, radius * 2.25f));
            }
        }

        foreach (var brightStar in GetAnchorStars(sceneNumber))
        {
            ctx.Fill(Color.White.WithAlpha(brightStar.Alpha), new EllipsePolygon(brightStar.Position.X, brightStar.Position.Y, brightStar.Radius));
            ctx.Fill(Color.ParseHex("#DCEBFF").WithAlpha(brightStar.Alpha * 0.16f), new EllipsePolygon(brightStar.Position.X, brightStar.Position.Y, brightStar.Radius * 2.6f));
        }
    }

    private static AnchorStar[] GetAnchorStars(int sceneNumber) => sceneNumber switch
    {
        1 => [new(new PointF(250, 150), 1.9f, .38f), new(new PointF(745, 120), 1.7f, .34f), new(new PointF(1320, 145), 1.8f, .36f), new(new PointF(1610, 260), 1.5f, .30f)],
        5 => [new(new PointF(300, 145), 2.2f, .68f), new(new PointF(770, 155), 1.8f, .56f), new(new PointF(1210, 220), 2.0f, .62f), new(new PointF(1515, 125), 1.9f, .58f), new(new PointF(980, 150), 1.7f, .52f)],
        6 => [new(new PointF(475, 250), 1.7f, .34f), new(new PointF(990, 205), 1.8f, .36f), new(new PointF(1780, 95), 1.5f, .30f)],
        _ => []
    };

    private static void RenderLandscape(IImageProcessingContext ctx, int sceneNumber, bool meteorVisual)
    {
        if (meteorVisual)
        {
            DrawLowHorizon(ctx, sceneNumber is 2 ? 790 : 835, "#05070D");
            ctx.Fill(Color.Black.WithAlpha(.58f), new RectangleF(0, 900, CanvasWidth, 180));
            DrawForegroundHaze(ctx, 700, 310, "#8FB7FF", .026f);
            return;
        }

        switch (sceneNumber)
        {
            case 1:
                DrawSoftHorizonGlow(ctx, 820, 240, "#FF8A3D", .24f);
                DrawSoftHorizonGlow(ctx, 885, 180, "#FFD08A", .14f);
                DrawDunes(ctx, 812, "#0C1017", "#17131A");
                ctx.Fill(Color.Black.WithAlpha(.44f), new RectangleF(0, 914, CanvasWidth, 166));
                DrawForegroundHaze(ctx, 760, 240, "#FFD6A0", .055f);
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
                DrawSoftHorizonGlow(ctx, 760, 260, "#567CFF", .045f);
                DrawSoftHorizonGlow(ctx, 875, 180, "#F6C177", .055f);
                DrawForegroundHaze(ctx, 690, 360, "#B8CEFF", .032f);
                ctx.Fill(Color.ParseHex("#060A12").WithAlpha(.94f), new RectangleF(0, 930, CanvasWidth, 150));
                break;
            case 6:
                DrawSoftHorizonGlow(ctx, 820, 260, "#FF7A38", .22f);
                DrawSoftHorizonGlow(ctx, 900, 220, "#FFD08A", .17f);
                DrawDunes(ctx, 835, "#090E15", "#14151B");
                ctx.Fill(Color.Black.WithAlpha(.50f), new RectangleF(0, 968, CanvasWidth, 112));
                DrawForegroundHaze(ctx, 770, 280, "#FFD6A0", .048f);
                break;
        }
    }

    private static void DrawForegroundHaze(IImageProcessingContext ctx, float centerY, float height, string color, float maxAlpha)
    {
        using var haze = new Image<Rgba32>(CanvasWidth, CanvasHeight, Color.Transparent);
        var hazeColor = Color.ParseHex(color).ToPixel<Rgba32>();
        haze.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var dy = MathF.Abs(y - centerY) / Math.Max(1f, height);
                var vertical = MathF.Exp(-dy * dy * 3.0f);
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var xRatio = x / (float)(CanvasWidth - 1);
                    var feather = 0.64f + 0.36f * (1f - Math.Clamp(MathF.Abs(xRatio - 0.50f) / 0.58f, 0f, 1f));
                    row[x] = new Rgba32(hazeColor.R, hazeColor.G, hazeColor.B, (byte)Math.Clamp(maxAlpha * vertical * feather * 255f, 0f, 255f));
                }
            }
        });
        ctx.DrawImage(haze, 1f);
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

    private readonly record struct AnchorStar(PointF Position, float Radius, float Alpha);

    private readonly record struct SkyColorStop(float Position, Color Color);
}

public sealed class CelestialObjectLayerRenderer
{
    public void Render(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, string venusAssetPath, string jupiterAssetPath)
    {
        if (!spec.UsesLocalPlanetAssets || spec.QuestionType.Equals("when", StringComparison.OrdinalIgnoreCase)) return;
        var positions = PlanetLayout.GetPlacements(spec.QuestionType);
        DrawRelationshipGlow(ctx, spec.QuestionType, positions);
        DrawAsset(ctx, venusAssetPath, positions.Venus.Center, positions.Venus.Diameter, "#FFF2B8");
        DrawAsset(ctx, jupiterAssetPath, positions.Jupiter.Center, positions.Jupiter.Diameter, "#E5C18D");
    }

    private static void DrawRelationshipGlow(IImageProcessingContext ctx, string questionType, PlanetPairPlacement positions)
    {
        switch (questionType.ToLowerInvariant())
        {
            case "what":
                DrawEllipticalGlow(ctx, new PointF(1310, 386), 360, 210, "#FFD37A", .060f);
                DrawEllipticalGlow(ctx, new PointF(1310, 386), 220, 130, "#FFF2B8", .045f);
                break;
            case "why":
                var midpoint = new PointF((positions.Venus.Center.X + positions.Jupiter.Center.X) / 2f, (positions.Venus.Center.Y + positions.Jupiter.Center.Y) / 2f);
                DrawEllipticalGlow(ctx, midpoint, 390, 185, "#D9E7FF", .052f);
                DrawEllipticalGlow(ctx, midpoint, 250, 115, "#FFF0B8", .038f);
                ctx.Draw(Color.ParseHex("#FFF1C4").WithAlpha(.16f), 2, new PathBuilder().AddLine(positions.Venus.Center, positions.Jupiter.Center).Build());
                break;
            case "action":
                DrawEllipticalGlow(ctx, new PointF(1085, 410), 330, 185, "#FFD08A", .044f);
                break;
        }
    }

    private static void DrawEllipticalGlow(IImageProcessingContext ctx, PointF center, float width, float height, string color, float maxAlpha)
    {
        ctx.Fill(Color.ParseHex(color).WithAlpha(maxAlpha * .34f), new EllipsePolygon(center.X, center.Y, width / 2f, height / 2f));
        ctx.Fill(Color.ParseHex(color).WithAlpha(maxAlpha), new EllipsePolygon(center.X, center.Y, width / 3.2f, height / 3.2f));
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
        if (!spec.UsesLocalPlanetAssets)
        {
            if (IsMeteorVisual(spec)) DrawMeteorRadiantGuide(ctx, fonts.SmallFont);
            return;
        }

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
                DrawArrow(ctx, new PointF(1015, 440), new PointF(1150, 465), Color.ParseHex("#8FD2FF"));
                break;
            case "why":
                DrawClosenessBracket(ctx, PlanetLayout.GetPlacements(spec.QuestionType), fonts.SmallFont);
                break;
        }
    }

    private static bool IsMeteorVisual(QuestionDrivenVisualSpec spec)
        => spec.EventType.Equals("MeteorShower", StringComparison.OrdinalIgnoreCase)
            || spec.ProgrammaticLayers.Any(layer => layer.Contains("meteor", StringComparison.OrdinalIgnoreCase));

    private static void DrawMeteorRadiantGuide(IImageProcessingContext ctx, Font font)
    {
        var radiant = new PointF(1200, 255);
        ctx.Draw(Color.ParseHex("#B7E0FF").WithAlpha(.34f), 2, new EllipsePolygon(radiant.X, radiant.Y, 70, 46));
        Text(ctx, "subtle shower radiant", font, radiant.X + 52, radiant.Y + 28, Color.ParseHex("#B7E0FF").WithAlpha(.70f), 330);
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
        if (!spec.UsesLocalPlanetAssets)
        {
            if (IsMeteorVisual(spec)) DrawMeteorEducation(ctx, spec, fonts);
            return;
        }

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

    private static bool IsMeteorVisual(QuestionDrivenVisualSpec spec)
        => spec.EventType.Equals("MeteorShower", StringComparison.OrdinalIgnoreCase)
            || spec.ProgrammaticLayers.Any(layer => layer.Contains("meteor", StringComparison.OrdinalIgnoreCase));

    private static void DrawMeteorEducation(IImageProcessingContext ctx, QuestionDrivenVisualSpec spec, EditorialFonts fonts)
    {
        if (spec.QuestionType.Equals("When", StringComparison.OrdinalIgnoreCase))
        {
            Text(ctx, "Midnight to pre-dawn", fonts.TitleFont, 160, 135, Color.White, 820);
            ctx.Draw(Color.ParseHex("#8FD2FF"), 5, new PathBuilder().AddLine(new PointF(320, 575), new PointF(1540, 575)).Build());
            Text(ctx, "dark-sky meteor window", fonts.SmallFont, 610, 455, Color.ParseHex("#B7E0FF"), 460);
        }
        else if (spec.QuestionType.Equals("How", StringComparison.OrdinalIgnoreCase))
        {
            var items = new[] { ("1", "Avoid city lights", new PointF(150, 155)), ("2", "Let eyes adapt", new PointF(150, 265)), ("3", "Watch open sky", new PointF(150, 375)) };
            foreach (var (n, text, p) in items)
            {
                ctx.Fill(Color.ParseHex("#8FD2FF"), new EllipsePolygon(p.X, p.Y + 21, 24));
                Text(ctx, n, fonts.SubtitleFont, p.X - 9, p.Y - 4, Color.ParseHex("#061124"), 40);
                Text(ctx, text, fonts.SubtitleFont, p.X + 50, p.Y, Color.White, 560);
            }
        }
        else if (spec.QuestionType.Equals("Why", StringComparison.OrdinalIgnoreCase))
        {
            Text(ctx, "Strong annual shower + low Moon interference", fonts.SmallFont, 610, 595, Color.White, 760);
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
        if (!spec.UsesLocalPlanetAssets)
        {
            DrawTitleStack(text, spec.OverlayText.FirstOrDefault() ?? spec.ViewerTakeaway, spec.OverlayText.Skip(1).FirstOrDefault() ?? spec.CaptionText, fonts, spec.QuestionType.Equals("Action", StringComparison.OrdinalIgnoreCase) ? new RectangleF(345, 844, 1000, 118) : new RectangleF(115, 44, 900, 158));
            return;
        }

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
                DrawTitleStack(text, "STEP OUTSIDE TONIGHT", "CHECK YOUR SKY", fonts, new RectangleF(345, 844, 1000, 118));
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
    public static EditorialFonts Create() => CreateScaled(1f);

    public static EditorialFonts CreateScaled(float scale) => new(Resolve(60 * scale, FontStyle.Bold), Resolve(36 * scale, FontStyle.Bold), Resolve(30 * scale, FontStyle.Bold), Resolve(24 * scale, FontStyle.Regular));

    private static Font Resolve(float size, FontStyle style)
    {
        var collection = new FontCollection();
        foreach (var candidate in new[] { "Backend/src/Astronomy.MediaFactory.Api/assets/fonts/Montserrat-Bold.ttf", "Backend/src/Astronomy.MediaFactory.Api/assets/fonts/Montserrat-ExtraBold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" })
            if (File.Exists(candidate)) return collection.Add(candidate).CreateFont(size, style);
        return SystemFonts.CreateFont("Arial", size, style);
    }
}
