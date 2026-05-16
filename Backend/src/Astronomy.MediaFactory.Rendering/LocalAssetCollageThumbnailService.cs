using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

public sealed class LocalAssetCollageThumbnailService : ICinematicThumbnailService
{
    private static readonly string[] AssetNames = ["hero.png", "hero.jpg", "hero.jpeg", "full.png", "crescent.png", "gibbous.png"];
    private static readonly HashSet<string> PlanetKeys = new(StringComparer.OrdinalIgnoreCase) { "mercury", "venus", "mars", "jupiter", "saturn", "uranus", "neptune" };

    private readonly IThumbnailStrategyService _strategyService;
    private readonly ThumbnailOptions _options;
    private readonly ILogger<LocalAssetCollageThumbnailService> _logger;

    public LocalAssetCollageThumbnailService(IThumbnailStrategyService strategyService, IOptions<ThumbnailOptions> options, ILogger<LocalAssetCollageThumbnailService> logger)
    {
        _strategyService = strategyService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
    {
        var strategyPlan = _strategyService.BuildPlan(request);
        var thumbnailsDirectory = Path.Combine(request.OutputDirectory, "thumbnails");
        Directory.CreateDirectory(thumbnailsDirectory);

        if (!_options.Enabled)
            return strategyPlan;

        var warnings = new List<string>();
        var outputPath = Path.Combine(thumbnailsDirectory, request.IsShortForm ? _options.ShortThumbnailOutputName : _options.LongThumbnailOutputName);
        var width = request.IsShortForm ? _options.ShortThumbnailWidth : _options.LongThumbnailWidth;
        var height = request.IsShortForm ? _options.ShortThumbnailHeight : _options.LongThumbnailHeight;
        var maxSupport = Math.Max(0, request.IsShortForm ? _options.MaxSupportObjectsShort : _options.MaxSupportObjectsLong);

        try
        {
            var selection = SelectObjects(request, maxSupport);
            var assets = ResolveAssets(selection, warnings);
            if (assets.Count == 0)
            {
                warnings.Add("No curated local hero asset was found; using generated milky-way fallback.");
                assets.Add(await CreateProceduralAssetAsync("Milky Way", "milky-way", cancellationToken));
                selection = selection with { Hero = new SelectedObject("Milky Way", "MilkyWay", "milky-way", true), Support = Array.Empty<SelectedObject>() };
            }

            var hook = _options.EnableHookText ? BuildHook(selection.Hero, request) : string.Empty;
            await ComposeAsync(request, outputPath, width, height, assets, hook, warnings, cancellationToken);
            await EnsureJpegSizeAsync(outputPath, width, height, cancellationToken);

            var celestialSelection = new CelestialThumbnailSelection
            {
                HeroObject = selection.Hero.Name,
                SupportObjects = selection.Support.Select(s => s.Name).ToArray(),
                SelectedHook = hook,
                SelectedLayout = request.IsShortForm ? "PortraitObjectFirst" : selection.IsSpecialEvent ? "SpecialEventHero" : "LandscapeHeroRightTextLeft",
                AssetSources = assets,
                VisibilityDataUsed = selection.VisibilityData,
                FallbackUsed = assets.Any(a => a.FallbackUsed) || warnings.Any(w => w.Contains("fallback", StringComparison.OrdinalIgnoreCase)),
                SpecialEventMode = selection.IsSpecialEvent
            };

            var plan = new ThumbnailPlan
            {
                PrimaryThumbnailText = string.IsNullOrWhiteSpace(hook) ? strategyPlan.PrimaryThumbnailText : hook,
                AlternateThumbnailTexts = strategyPlan.AlternateThumbnailTexts,
                SelectedVisualPath = assets.FirstOrDefault()?.LocalPath,
                ThumbnailPath = outputPath,
                LongThumbnailPath = request.IsShortForm ? null : outputPath,
                ShortThumbnailPath = request.IsShortForm ? outputPath : null,
                ThumbnailVariantPaths = [outputPath],
                LayoutType = request.IsShortForm ? ThumbnailLayoutType.CenteredTitleOverlay : ThumbnailLayoutType.TextLeftVisualRight,
                LayoutCandidates = [ThumbnailLayoutType.TextLeftVisualRight, ThumbnailLayoutType.CenteredTitleOverlay],
                Variants = strategyPlan.Variants,
                FallbackUsed = celestialSelection.FallbackUsed,
                Mode = "LocalAssetCollage",
                CelestialSelection = celestialSelection
            };

            await WriteSelectionAsync(plan, thumbnailsDirectory, cancellationToken);
            await WriteAnalysisAsync(plan, request, width, height, assets, warnings, cancellationToken);
            return plan;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Local asset thumbnail generation failed; falling back to Stellarium/extracted frame if available.");
            warnings.Add(ex.Message);
            var fallback = BuildFallbackPlan(strategyPlan, request);
            await WriteFallbackAnalysisAsync(request, fallback, warnings, cancellationToken);
            return fallback;
        }
    }

    private Selection SelectObjects(ThumbnailGenerationRequest request, int maxSupport)
    {
        var visible = request.Context.SceneObservationContexts
            .Where(s => (s.IsVisible || s.AltitudeDegrees.HasValue) && !string.IsNullOrWhiteSpace(s.ObjectName) && !s.ObjectName.Equals("Sky", StringComparison.OrdinalIgnoreCase))
            .Select(s => new SelectedObject(s.ObjectName, s.ObjectType, CelestialObjectKeyMapper.Map(s.ObjectName, s.ObjectType), false, s))
            .ToList();

        var events = request.Context.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.ObjectName))
            .OrderByDescending(e => e.Score)
            .Select(e => new SelectedObject(e.ObjectName, e.Category, CelestialObjectKeyMapper.Map(e.ObjectName, e.Category), true, null, e))
            .ToList();

        var isSpecial = IsSpecialEvent(request);
        var candidates = visible.Concat(events).DistinctBy(o => o.Key).ToList();
        SelectedObject? hero = null;

        if (isSpecial)
            hero = events.Concat(visible).FirstOrDefault(o => IsSpecialKey(o.Key) || !string.IsNullOrWhiteSpace(o.Name));
        hero ??= candidates.FirstOrDefault(o => o.Key == "moon" && IsMoonMajor(request));
        hero ??= visible.Where(o => PlanetKeys.Contains(o.Key)).OrderByDescending(ScoreVisibleObject).FirstOrDefault();
        hero ??= visible.OrderByDescending(ScoreVisibleObject).FirstOrDefault();
        hero ??= events.FirstOrDefault();
        hero ??= new SelectedObject("Milky Way", "MilkyWay", "milky-way", true);

        var support = isSpecial
            ? Array.Empty<SelectedObject>()
            : candidates.Where(o => !o.Key.Equals(hero.Key, StringComparison.OrdinalIgnoreCase)).Where(o => HasAsset(o.Key)).Take(maxSupport).ToArray();

        var visibilityData = new List<object>();
        foreach (var obj in new[] { hero }.Concat(support))
        {
            if (obj.Scene is not null)
                visibilityData.Add(new { obj.Scene.SceneId, obj.Scene.ObjectName, obj.Scene.ObjectType, obj.Scene.IsVisible, obj.Scene.AltitudeDegrees, obj.Scene.DirectionLabel, obj.Scene.VisibilityReason, obj.Scene.LocalObservationTime });
            else if (obj.Event is not null)
                visibilityData.Add(new { obj.Event.ObjectName, obj.Event.Category, obj.Event.VisibilityWindow, obj.Event.Direction, obj.Event.Score });
        }

        return new Selection(hero, support, visibilityData, isSpecial);
    }

    private List<CelestialAsset> ResolveAssets(Selection selection, List<string> warnings)
    {
        var assets = new List<CelestialAsset>();
        foreach (var obj in new[] { selection.Hero }.Concat(selection.Support))
        {
            var path = FindAsset(obj.Key);
            if (path is null)
            {
                warnings.Add($"Missing local curated asset for {obj.Name} ({obj.Key}).");
                if (obj == selection.Hero)
                {
                    var fallback = FindAsset("milky-way");
                    if (fallback is not null)
                        assets.Add(ToAsset(obj with { Key = "milky-way" }, fallback, true));
                }
                continue;
            }

            assets.Add(ToAsset(obj, path, false));
        }

        return assets;
    }

    private async Task ComposeAsync(ThumbnailGenerationRequest request, string outputPath, int width, int height, IReadOnlyList<CelestialAsset> assets, string hook, List<string> warnings, CancellationToken cancellationToken)
    {
        var portrait = request.IsShortForm;
        using var canvas = new Image<Rgba32>(width, height, Color.FromRgb(3, 8, 22));
        await DrawBackgroundAsync(canvas, request, width, height, warnings, cancellationToken);
        canvas.Mutate(ctx =>
        {
            DrawCinematicGradient(ctx, width, height, portrait);
            DrawStars(ctx, width, height, request.Context.Date.ToString("yyyy-MM-dd"));
        });

        var hero = assets[0];
        await DrawObjectAsync(canvas, hero, portrait ? HeroPortraitRect(width, height) : HeroLandscapeRect(width, height), true, cancellationToken);
        var supports = assets.Skip(1).Take(portrait ? 1 : 2).ToArray();
        for (var i = 0; i < supports.Length; i++)
            await DrawObjectAsync(canvas, supports[i], portrait ? SupportPortraitRect(width, height, i) : SupportLandscapeRect(width, height, i), false, cancellationToken);

        canvas.Mutate(ctx =>
        {
            DrawDateLocation(ctx, request, width, height, portrait);
            if (_options.EnableHookText) DrawHook(ctx, hook, width, height, portrait, request.Context.Localization.ResolvedLanguage);
            DrawBrand(ctx, width, height, portrait);
            DrawVignette(ctx, width, height);
        });

        await canvas.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = Math.Clamp(_options.JpegQuality, 1, 100) }, cancellationToken);
    }

    private async Task DrawBackgroundAsync(Image<Rgba32> canvas, ThumbnailGenerationRequest request, int width, int height, List<string> warnings, CancellationToken cancellationToken)
    {
        var background = FindAsset("milky-way");
        background ??= _options.EnableStellariumBackground ? request.AvailableVisuals.FirstOrDefault(File.Exists) : null;
        if (background is null)
        {
            warnings.Add("Using generated dark star gradient background.");
            return;
        }

        try
        {
            using var image = await Image.LoadAsync<Rgba32>(background, cancellationToken);
            image.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Crop }).GaussianBlur(1.3f).Brightness(0.55f).Contrast(1.08f));
            canvas.Mutate(ctx => ctx.DrawImage(image, new Point(0, 0), 1f));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Background asset could not be loaded: {ex.Message}");
        }
    }

    private static async Task DrawObjectAsync(Image<Rgba32> canvas, CelestialAsset asset, RectangleF rect, bool hero, CancellationToken cancellationToken)
    {
        using var obj = await Image.LoadAsync<Rgba32>(asset.LocalPath, cancellationToken);
        obj.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size((int)rect.Width, (int)rect.Height), Mode = ResizeMode.Max }));
        var x = (int)(rect.X + (rect.Width - obj.Width) / 2f);
        var y = (int)(rect.Y + (rect.Height - obj.Height) / 2f);
        canvas.Mutate(ctx =>
        {
            var glowColor = ResolveGlow(asset.Category).WithAlpha(hero ? 0.32f : 0.18f);
            var glow = new EllipsePolygon(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f, Math.Max(rect.Width, rect.Height) * (hero ? 0.56f : 0.42f));
            ctx.Fill(glowColor, glow);
            ctx.DrawImage(obj, new Point(x + (hero ? 9 : 5), y + (hero ? 12 : 7)), hero ? 0.26f : 0.18f);
            ctx.DrawImage(obj, new Point(x, y), hero ? 1f : 0.82f);
        });
    }

    private async Task EnsureJpegSizeAsync(string outputPath, int width, int height, CancellationToken cancellationToken)
    {
        if (_options.MaxFileSizeBytes <= 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length <= _options.MaxFileSizeBytes)
            return;

        using var image = await Image.LoadAsync<Rgba32>(outputPath, cancellationToken);
        foreach (var quality in new[] { 85, 80, 75 })
        {
            await image.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = quality }, cancellationToken);
            if (new FileInfo(outputPath).Length <= _options.MaxFileSizeBytes)
                return;
        }
    }

    private ThumbnailPlan BuildFallbackPlan(ThumbnailPlan strategyPlan, ThumbnailGenerationRequest request)
    {
        var fallback = _options.FallbackToStellariumFrame || _options.FallbackToExtractedFrame ? request.AvailableVisuals.FirstOrDefault(File.Exists) ?? strategyPlan.SelectedVisualPath : null;
        return new ThumbnailPlan
        {
            PrimaryThumbnailText = strategyPlan.PrimaryThumbnailText,
            AlternateThumbnailTexts = strategyPlan.AlternateThumbnailTexts,
            SelectedVisualPath = fallback,
            ThumbnailPath = fallback,
            LongThumbnailPath = request.IsShortForm ? null : fallback,
            ShortThumbnailPath = request.IsShortForm ? fallback : null,
            ThumbnailVariantPaths = fallback is null ? Array.Empty<string>() : [fallback],
            LayoutType = strategyPlan.LayoutType,
            LayoutCandidates = strategyPlan.LayoutCandidates,
            Variants = strategyPlan.Variants,
            FallbackUsed = true,
            Mode = "LocalAssetCollage"
        };
    }

    private async Task<CelestialAsset> CreateProceduralAssetAsync(string name, string key, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(ResolveAssetRoot(), key);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "hero-fallback.jpg");
        if (!File.Exists(path))
            await ProceduralCelestialFallback.CreateAsync(path, name, key, cancellationToken);
        return new CelestialAsset { ObjectName = name, ObjectType = key, Category = key, LocalPath = path, Source = "GeneratedFallback", Title = name, FallbackUsed = true };
    }

    private string? FindAsset(string key)
    {
        var root = ResolveAssetRoot();
        var directory = Path.Combine(root, key);
        if (!Directory.Exists(directory))
            return null;
        foreach (var name in AssetNames)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
                return path;
        }
        return Directory.EnumerateFiles(directory).FirstOrDefault(p => IsImage(p) && !Path.GetFileName(p).Contains("metadata", StringComparison.OrdinalIgnoreCase));
    }

    private bool HasAsset(string key) => FindAsset(key) is not null;

    private string ResolveAssetRoot() => Path.IsPathRooted(_options.AssetRootPath) ? _options.AssetRootPath : Path.Combine(Directory.GetCurrentDirectory(), _options.AssetRootPath);

    private static bool IsImage(string path) => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static CelestialAsset ToAsset(SelectedObject obj, string path, bool fallback) => new()
    {
        ObjectName = obj.Name,
        ObjectType = obj.Type,
        Category = obj.Key,
        LocalPath = path,
        Source = "LocalCuratedAsset",
        Title = $"{obj.Name} curated asset",
        OriginalUrl = string.Empty,
        FallbackUsed = fallback
    };

    private static double ScoreVisibleObject(SelectedObject obj)
    {
        var score = obj.Scene?.AltitudeDegrees is double altitude ? Math.Clamp(altitude / 90d, 0, 1) : 0;
        score += obj.Key switch { "venus" => 1.00, "jupiter" => 0.95, "moon" => 0.92, "saturn" => 0.82, "mars" => 0.76, "mercury" => 0.65, _ => 0.45 };
        return score;
    }

    private static bool IsSpecialEvent(ThumbnailGenerationRequest request)
    {
        var text = $"{request.Context.SpecialEvent?.EventType} {request.Context.SpecialEvent?.EventTitle} {request.ContentType} {string.Join(' ', request.Context.Events.Select(e => e.Category + ' ' + e.ObjectName))}";
        return request.ContentType == ContentType.SpecialEventGuide || text.Contains("eclipse", StringComparison.OrdinalIgnoreCase) || text.Contains("meteor", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpecialKey(string key) => key is "meteor-shower" or "solar-eclipse" or "lunar-eclipse";

    private static bool IsMoonMajor(ThumbnailGenerationRequest request)
    {
        var text = $"{request.Context.SpecialEvent?.EventTitle} {string.Join(' ', request.Context.Events.Select(e => e.Category + ' ' + e.Details))}";
        return text.Contains("full moon", StringComparison.OrdinalIgnoreCase) || text.Contains("new moon", StringComparison.OrdinalIgnoreCase) || text.Contains("eclipse", StringComparison.OrdinalIgnoreCase) || text.Contains("supermoon", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildHook(SelectedObject hero, ThumbnailGenerationRequest request)
    {
        var hi = LocalizationResolver.IsHindi(request.Context.Localization.ResolvedLanguage);
        var key = hero.Key;
        var words = hi ? HindiHook(key) : EnglishHook(key);
        return LimitWords(words, 4);
    }

    private static string EnglishHook(string key) => key switch
    {
        "venus" => "Venus After Sunset",
        "saturn" => "Saturn Before Sunrise",
        "moon" => "Moon Tonight",
        "meteor-shower" => "Meteor Peak",
        "solar-eclipse" or "lunar-eclipse" => "Eclipse Tonight",
        "jupiter" => "Jupiter Tonight",
        _ => "Tonight’s Sky"
    };

    private static string HindiHook(string key) => key switch
    {
        "jupiter" => "आज रात बृहस्पति",
        "venus" => "आज शुक्र देखें",
        "moon" => "चांद आज रात",
        "meteor-shower" => "उल्का वर्षा",
        "solar-eclipse" or "lunar-eclipse" => "ग्रहण आज रात",
        _ => "आज रात आसमान"
    };

    private static string LimitWords(string value, int maxWords) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(Math.Max(1, maxWords)));

    private static RectangleF HeroLandscapeRect(int width, int height) => new(width * 0.48f, height * 0.06f, width * 0.48f, height * 0.82f);
    private static RectangleF SupportLandscapeRect(int width, int height, int index) => new(width * (index == 0 ? 0.38f : 0.78f), height * (index == 0 ? 0.60f : 0.10f), width * 0.17f, width * 0.17f);
    private static RectangleF HeroPortraitRect(int width, int height) => new(width * 0.05f, height * 0.09f, width * 0.90f, width * 0.90f);
    private static RectangleF SupportPortraitRect(int width, int height, int index) => new(width * 0.63f, height * 0.42f, width * 0.28f, width * 0.28f);

    private static void DrawCinematicGradient(IImageProcessingContext ctx, int width, int height, bool portrait)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height), GradientRepetitionMode.None, new ColorStop(0, Color.FromRgb(4, 10, 30).WithAlpha(0.25f)), new ColorStop(1, Color.Black.WithAlpha(0.74f))), new RectangleF(0, 0, width, height));
        if (!portrait)
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width * 0.65f, 0), GradientRepetitionMode.None, new ColorStop(0, Color.Black.WithAlpha(0.78f)), new ColorStop(1, Color.Transparent)), new RectangleF(0, 0, width * 0.72f, height));
    }

    private static void DrawStars(IImageProcessingContext ctx, int width, int height, string seedText)
    {
        var random = new Random(Math.Abs(seedText.GetHashCode()));
        var stars = Math.Clamp(width * height / 15000, 70, 240);
        for (var i = 0; i < stars; i++)
            ctx.Fill(Color.White.WithAlpha(0.12f + random.NextSingle() * 0.42f), new EllipsePolygon(random.NextSingle() * width, random.NextSingle() * height, 0.7f + random.NextSingle() * 1.4f));
    }

    private static void DrawDateLocation(IImageProcessingContext ctx, ThumbnailGenerationRequest request, int width, int height, bool portrait)
    {
        var text = $"{request.Context.Date:MMM d} • {request.Context.LocationName}".Trim(' ', '•');
        if (string.IsNullOrWhiteSpace(text)) return;
        var font = CreateFont(portrait ? 32 : 26, FontStyle.Regular, false);
        ctx.DrawText(text, font, Color.White.WithAlpha(0.72f), new PointF(portrait ? 46 : 50, portrait ? 56 : 42));
    }

    private static void DrawHook(IImageProcessingContext ctx, string hook, int width, int height, bool portrait, string language)
    {
        if (string.IsNullOrWhiteSpace(hook)) return;
        var font = CreateFont(portrait ? 82 : 72, FontStyle.Bold, LocalizationResolver.IsHindi(language));
        var origin = portrait ? new PointF(width / 2f, height * 0.73f) : new PointF(64, height * 0.72f);
        var opts = new RichTextOptions(font) { Origin = origin, WrappingLength = portrait ? width - 120 : width * 0.43f, HorizontalAlignment = portrait ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        ctx.DrawText(new RichTextOptions(opts) { Origin = new PointF(origin.X + 4, origin.Y + 5) }, hook, Color.Black.WithAlpha(0.9f));
        ctx.DrawText(opts, hook, Color.White);
    }

    private void DrawBrand(IImageProcessingContext ctx, int width, int height, bool portrait)
    {
        if (!_options.EnableBranding || string.IsNullOrWhiteSpace(_options.BrandText)) return;
        var font = CreateFont(portrait ? 34 : 30, FontStyle.Bold, false);
        ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(width - 40, height - (portrait ? 66 : 34)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom }, _options.BrandText, Color.White.WithAlpha(0.62f));
    }

    private static void DrawVignette(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Fill(Color.Black.WithAlpha(0.20f), new RectangleF(0, 0, width, 22));
        ctx.Fill(Color.Black.WithAlpha(0.24f), new RectangleF(0, height - 30, width, 30));
        ctx.Fill(Color.Black.WithAlpha(0.17f), new RectangleF(0, 0, 28, height));
        ctx.Fill(Color.Black.WithAlpha(0.17f), new RectangleF(width - 28, 0, 28, height));
    }

    private static Color ResolveGlow(string key) => key switch
    {
        "jupiter" or "saturn" or "venus" => Color.FromRgb(255, 205, 116),
        "mars" or "lunar-eclipse" => Color.FromRgb(255, 118, 82),
        "moon" or "solar-eclipse" => Color.FromRgb(245, 242, 220),
        "uranus" or "neptune" => Color.FromRgb(100, 175, 255),
        _ => Color.FromRgb(125, 155, 255)
    };

    private static Font CreateFont(float size, FontStyle style, bool preferHindi)
    {
        var families = SystemFonts.Collection.Families;
        var family = preferHindi ? families.FirstOrDefault(f => f.Name.Contains("Noto", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Devanagari", StringComparison.OrdinalIgnoreCase)) : default;
        if (string.IsNullOrWhiteSpace(family.Name)) family = families.FirstOrDefault(f => f.Name.Equals("Arial", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(family.Name)) family = families.First();
        return family.CreateFont(size, style);
    }

    private static async Task WriteSelectionAsync(ThumbnailPlan plan, string thumbnailsDirectory, CancellationToken cancellationToken)
    {
        var payload = new
        {
            mode = "LocalAssetCollage",
            heroObject = plan.CelestialSelection?.HeroObject ?? "",
            supportObjects = plan.CelestialSelection?.SupportObjects ?? Array.Empty<string>(),
            selectedHook = plan.CelestialSelection?.SelectedHook ?? plan.PrimaryThumbnailText,
            longThumbnailPath = plan.LongThumbnailPath,
            shortThumbnailPath = plan.ShortThumbnailPath,
            assetSources = plan.CelestialSelection?.AssetSources ?? Array.Empty<CelestialAsset>(),
            fallbackUsed = plan.FallbackUsed
        };
        await File.WriteAllTextAsync(Path.Combine(thumbnailsDirectory, "thumbnail-selection.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static async Task WriteAnalysisAsync(ThumbnailPlan plan, ThumbnailGenerationRequest request, int width, int height, IReadOnlyCollection<CelestialAsset> assets, IReadOnlyCollection<string> warnings, CancellationToken cancellationToken)
    {
        var outputPath = plan.ThumbnailPath ?? string.Empty;
        var payload = new
        {
            assetsFound = assets.Where(a => File.Exists(a.LocalPath)).Select(a => a.LocalPath).ToArray(),
            assetsMissing = warnings.Where(w => w.StartsWith("Missing", StringComparison.OrdinalIgnoreCase)).ToArray(),
            layoutUsed = plan.CelestialSelection?.SelectedLayout ?? "LocalAssetCollage",
            language = request.Context.Localization.ResolvedLanguage,
            dimensions = new { width, height, fileSizeBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0 },
            warnings
        };
        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "thumbnails", "thumbnail-analysis-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static async Task WriteFallbackAnalysisAsync(ThumbnailGenerationRequest request, ThumbnailPlan fallback, IReadOnlyCollection<string> warnings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(request.OutputDirectory, "thumbnails"));
        var payload = new { assetsFound = Array.Empty<string>(), assetsMissing = Array.Empty<string>(), layoutUsed = "FallbackToStellariumFrame", language = request.Context.Localization.ResolvedLanguage, dimensions = new { width = 0, height = 0, fileSizeBytes = 0 }, warnings };
        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "thumbnails", "thumbnail-analysis-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private sealed record SelectedObject(string Name, string Type, string Key, bool FallbackAllowed, SceneObservationContext? Scene = null, AstronomyEventModel? Event = null);
    private sealed record Selection(SelectedObject Hero, IReadOnlyCollection<SelectedObject> Support, IReadOnlyCollection<object> VisibilityData, bool IsSpecialEvent);
}
