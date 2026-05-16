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
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Rendering;

public sealed class LocalAssetCollageThumbnailService : ICinematicThumbnailService
{
    private static readonly string[] DefaultPreferredAssetNames = ["hero-transparent.png", "hero.png", "cinematic.png", "closeup.png"];
    private static readonly HashSet<string> PlanetKeys = new(StringComparer.OrdinalIgnoreCase) { "mercury", "venus", "mars", "jupiter", "saturn", "uranus", "neptune" };
    private static readonly HashSet<string> GenericHookPhrases = new(StringComparer.OrdinalIgnoreCase) { "TONIGHT'S SKY", "LOOK W TONIGHT", "PLANETS VISIBLE", "SKY WATCH", "VISIBLE TONIGHT" };
    private static readonly HashSet<string> DeepSpaceKeys = new(StringComparer.OrdinalIgnoreCase) { "milky-way", "andromeda-galaxy", "orion-nebula", "pleiades" };

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

            var hook = _options.EnableHookText ? BuildHook(selection, request) : string.Empty;
            var composition = await ComposeAsync(request, outputPath, width, height, assets, selection, hook, warnings, cancellationToken);
            await EnsureJpegSizeAsync(outputPath, width, height, cancellationToken);

            var celestialSelection = new CelestialThumbnailSelection
            {
                HeroObject = selection.Hero.Name,
                SupportObjects = selection.Support.Select(s => s.Name).ToArray(),
                SelectedHook = hook,
                SelectedLayout = composition.LayoutUsed,
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

            await WriteSelectionAsync(plan, thumbnailsDirectory, composition, cancellationToken);
            await WriteAnalysisAsync(plan, request, width, height, assets, warnings, composition, cancellationToken);
            await WriteCinematicReportAsync(plan, selection, composition, thumbnailsDirectory, cancellationToken);
            return plan;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Local asset thumbnail generation failed; falling back to Stellarium/extracted frame if available.");
            warnings.Add(ex.Message);
            var fallback = BuildFallbackPlan(strategyPlan, request);
            await WriteFallbackAnalysisAsync(request, fallback, warnings, cancellationToken);
            await WriteFallbackCinematicReportAsync(request, fallback, warnings, cancellationToken);
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
            .Where(e => !string.IsNullOrWhiteSpace(e.ObjectName) || !string.IsNullOrWhiteSpace(e.Category))
            .OrderByDescending(e => e.Score)
            .Select(e =>
            {
                var name = string.IsNullOrWhiteSpace(e.ObjectName) ? e.Category : e.ObjectName;
                return new SelectedObject(name, e.Category, CelestialObjectKeyMapper.Map(name, e.Category), true, null, e);
            })
            .ToList();

        var duplicatedKeys = visible.Concat(events)
            .GroupBy(o => o.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = visible.Concat(events).DistinctBy(o => o.Key).ToList();
        var isSpecial = IsSpecialEvent(request);
        var conjunction = DetectConjunction(candidates, request);
        var scores = candidates
            .Select(o => ScoreHeroCandidate(o, request, duplicatedKeys, conjunction))
            .OrderByDescending(s => s.TotalScore)
            .ThenBy(s => s.Object.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hero = scores.FirstOrDefault()?.Object;
        hero ??= new SelectedObject("Milky Way", "MilkyWay", "milky-way", true);
        var mode = SelectCinematicMode(hero, candidates, request, conjunction);
        var supportsAllowed = mode is "EclipseMode" or "MeteorShowerMode" ? Math.Min(maxSupport, 1) : maxSupport;
        var support = candidates
            .Where(o => !o.Key.Equals(hero.Key, StringComparison.OrdinalIgnoreCase))
            .Where(o => HasAsset(o.Key))
            .OrderByDescending(o => SupportScore(o, hero, mode))
            .Take(supportsAllowed)
            .ToArray();

        var visibilityData = new List<object>();
        foreach (var obj in new[] { hero }.Concat(support))
        {
            if (obj.Scene is not null)
                visibilityData.Add(new { obj.Scene.SceneId, obj.Scene.ObjectName, obj.Scene.ObjectType, obj.Scene.IsVisible, obj.Scene.AltitudeDegrees, obj.Scene.DirectionLabel, obj.Scene.VisibilityReason, obj.Scene.LocalObservationTime });
            else if (obj.Event is not null)
                visibilityData.Add(new { obj.Event.ObjectName, obj.Event.Category, obj.Event.VisibilityWindow, obj.Event.Direction, obj.Event.Score });
        }

        return new Selection(hero, support, visibilityData, isSpecial, mode, scores, conjunction);
    }

    private List<CelestialAsset> ResolveAssets(Selection selection, List<string> warnings)
    {
        var assets = new List<CelestialAsset>();
        foreach (var obj in new[] { selection.Hero }.Concat(selection.Support))
        {
            var resolved = FindAsset(obj.Key);
            if (resolved is null)
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

            if (resolved.OldAssetIgnoredBecauseHeroExists)
                warnings.Add($"Ignored legacy JPG/NASA asset for {obj.Name} because an extracted hero asset exists.");
            assets.Add(ToAsset(obj, resolved, false));
        }

        return assets;
    }

    private async Task<CompositionDiagnostics> ComposeAsync(ThumbnailGenerationRequest request, string outputPath, int width, int height, IReadOnlyList<CelestialAsset> assets, Selection selection, string hook, List<string> warnings, CancellationToken cancellationToken)
    {
        var portrait = request.IsShortForm;
        var random = CreateDeterministicRandom(request, selection.CinematicMode);
        using var canvas = new Image<Rgba32>(width, height, Color.FromRgb(3, 8, 22));
        await DrawBackgroundAsync(canvas, request, width, height, warnings, cancellationToken);
        canvas.Mutate(ctx =>
        {
            DrawCinematicGradient(ctx, width, height, portrait, selection.CinematicMode);
            DrawStars(ctx, width, height, StableSeedText(request, selection.CinematicMode));
            DrawCinematicOverlays(ctx, width, height, portrait, selection.CinematicMode, random);
        });

        var textBox = CalculateTextSafeRect(hook, width, height, portrait, request.Context.Localization.ResolvedLanguage);
        var hero = assets[0];
        var heroRect = ResolveHeroRect(width, height, portrait, selection.CinematicMode, random);
        heroRect = AvoidCollision(heroRect, textBox, width, height, preferRight: !portrait);
        await DrawObjectAsync(canvas, hero, heroRect, true, selection.CinematicMode, cancellationToken);

        var supports = assets.Skip(1).Take(portrait ? 1 : 2).ToArray();
        var supportScales = new List<double>();
        var objectRects = new List<RectangleF> { heroRect };
        var overlapWarnings = new List<string>();
        for (var i = 0; i < supports.Length; i++)
        {
            var supportRect = ResolveSupportRect(width, height, portrait, selection.CinematicMode, i, random);
            supportRect = AvoidCollisions(supportRect, objectRects.Append(textBox), width, height);
            if (Intersects(supportRect, heroRect, 0.08f))
                overlapWarnings.Add($"support-{i}-near-hero");
            supportScales.Add(Math.Round(supportRect.Width / width, 3));
            objectRects.Add(supportRect);
            await DrawObjectAsync(canvas, supports[i], supportRect, false, selection.CinematicMode, cancellationToken);
        }

        canvas.Mutate(ctx =>
        {
            DrawDateLocation(ctx, request, width, height, portrait);
            if (_options.EnableHookText) DrawHook(ctx, hook, textBox, portrait, request.Context.Localization.ResolvedLanguage);
            DrawBrand(ctx, width, height, portrait);
            DrawVignette(ctx, width, height);
        });

        var safeZoneWarnings = BuildSafeZoneWarnings(textBox, objectRects, width, height, portrait).ToArray();
        var layoutWarnings = BuildLayoutWarnings(portrait, heroRect.Width / width, supportScales)
            .Concat(overlapWarnings)
            .Concat(safeZoneWarnings)
            .ToArray();
        var compositionScore = ScoreComposition(heroRect, textBox, objectRects.Skip(1), width, height, layoutWarnings);
        var readabilityScore = ScoreReadability(hook, textBox, width, height, portrait);
        var clickabilityScore = ScoreClickability(hook, selection.CinematicMode, selection.HeroScore?.TotalScore ?? 0, readabilityScore);

        await canvas.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = Math.Clamp(_options.JpegQuality, 1, 100) }, cancellationToken);
        return new CompositionDiagnostics(
            LayoutUsed: portrait ? "PortraitObjectUpperTextLowerThird" : "LandscapeHeroRightTextLeft",
            HeroObjectScale: Math.Round(heroRect.Width / width, 3),
            SupportObjectScales: supportScales.ToArray(),
            TransparentAssetsUsed: assets.Count(a => IsTransparentAsset(a.LocalPath)),
            CardStyleRemoved: true,
            ObjectCount: 1 + supports.Length,
            LayoutWarnings: layoutWarnings,
            CinematicMode: selection.CinematicMode,
            CompositionScore: compositionScore,
            ReadabilityScore: readabilityScore,
            ClickabilityScore: clickabilityScore,
            ObjectOverlapWarnings: overlapWarnings.ToArray(),
            SafeZoneWarnings: safeZoneWarnings,
            TextBounds: ToBounds(textBox));
    }

    private async Task DrawBackgroundAsync(Image<Rgba32> canvas, ThumbnailGenerationRequest request, int width, int height, List<string> warnings, CancellationToken cancellationToken)
    {
        var background = FindMilkyWayBackground();
        background ??= _options.EnableStellariumBackground ? request.AvailableVisuals.FirstOrDefault(File.Exists) : null;
        if (background is null)
        {
            warnings.Add("Using generated dark star gradient background.");
            return;
        }

        try
        {
            using var image = await Image.LoadAsync<Rgba32>(background, cancellationToken);
            image.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Crop }).GaussianBlur(1.3f).Brightness(0.45f).Contrast(1.04f));
            canvas.Mutate(ctx => ctx.DrawImage(image, new Point(0, 0), 1f));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Background asset could not be loaded: {ex.Message}");
        }
    }

    private static async Task DrawObjectAsync(Image<Rgba32> canvas, CelestialAsset asset, RectangleF rect, bool hero, string cinematicMode, CancellationToken cancellationToken)
    {
        using var obj = await Image.LoadAsync<Rgba32>(asset.LocalPath, cancellationToken);
        obj.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions { Size = new Size((int)rect.Width, (int)rect.Height), Mode = ResizeMode.Max });
            if (!hero) ctx.GaussianBlur(0.85f).Brightness(0.82f).Saturate(0.88f);
            else ctx.Contrast(1.12f).Brightness(1.05f);
        });
        var x = (int)(rect.X + (rect.Width - obj.Width) / 2f);
        var y = (int)(rect.Y + (rect.Height - obj.Height) / 2f);
        canvas.Mutate(ctx =>
        {
            var glowColor = ResolveGlow(asset.Category).WithAlpha(hero ? ResolveHeroGlowAlpha(asset.Category, cinematicMode) : 0.14f);
            if (asset.Category.Equals("meteor-shower", StringComparison.OrdinalIgnoreCase))
                ctx.DrawLine(glowColor, Math.Max(8, rect.Width * 0.045f), new PointF(rect.Left + rect.Width * 0.12f, rect.Top + rect.Height * 0.25f), new PointF(rect.Right - rect.Width * 0.08f, rect.Bottom - rect.Height * 0.22f));
            var glow = new EllipsePolygon(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f, Math.Max(rect.Width, rect.Height) * (hero ? 0.62f : 0.38f));
            ctx.Fill(glowColor, glow);
            if (IsTransparentAsset(asset.LocalPath))
                ctx.DrawImage(obj, new Point(x + (hero ? 9 : 5), y + (hero ? 12 : 7)), hero ? 0.22f : 0.14f);
            ctx.DrawImage(obj, new Point(x, y), hero ? 1f : 0.62f);
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

    private ResolvedAsset? FindAsset(string key)
    {
        var root = ResolveAssetRoot();
        var directory = Path.Combine(root, key);
        if (!Directory.Exists(directory))
            return null;

        var files = Directory.EnumerateFiles(directory)
            .Where(p => IsImage(p) && !Path.GetFileName(p).Contains("metadata", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (files.Length == 0)
            return null;

        var configuredPreferredNames = _options.PreferredAssetFileNames.Length > 0 ? _options.PreferredAssetFileNames : DefaultPreferredAssetNames;
        var preferredNames = new[] { "hero-transparent.png" }
            .Concat(configuredPreferredNames)
            .Concat(DefaultPreferredAssetNames)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var legacyExists = files.Any(IsLegacyJpgAsset);

        if (_options.PreferAssetPackImages)
        {
            foreach (var name in preferredNames)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path))
                    return CreateResolvedAsset(path, key, legacyExists && IsAssetPackFileName(Path.GetFileName(path)));
            }
        }

        if (_options.PreferPngAssets)
        {
            var png = files
                .Where(p => Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (png is not null)
                return CreateResolvedAsset(png, key, false);
        }

        var any = files
            .OrderBy(p => IsLegacyJpgAsset(p) ? 1 : 0)
            .ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return any is null ? null : CreateResolvedAsset(any, key, false);
    }


    private string? FindMilkyWayBackground()
    {
        var directory = Path.Combine(ResolveAssetRoot(), "milky-way");
        var hero = Path.Combine(directory, "hero.png");
        if (File.Exists(hero))
            return hero;
        return FindAsset("milky-way")?.Path;
    }

    private static bool IsTransparentAsset(string path)
        => Path.GetFileName(path).Equals("hero-transparent.png", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> BuildLayoutWarnings(bool portrait, double heroScale, IReadOnlyCollection<double> supportScales)
    {
        if (portrait && (heroScale < 0.55d || heroScale > 0.75d))
            yield return "portrait-hero-scale-outside-target";
        if (!portrait && (heroScale < 0.38d || heroScale > 0.55d))
            yield return "landscape-hero-scale-outside-target";
        if (supportScales.Any(s => s < 0.12d || s > 0.22d))
            yield return "support-scale-outside-target";
        if (portrait && supportScales.Count > 1)
            yield return "portrait-support-count-exceeds-cap";
        if (!portrait && supportScales.Count > 2)
            yield return "landscape-support-count-exceeds-cap";
    }

    private bool HasAsset(string key) => FindAsset(key) is not null;

    private string ResolveAssetRoot() => Path.IsPathRooted(_options.AssetRootPath) ? _options.AssetRootPath : Path.Combine(Directory.GetCurrentDirectory(), _options.AssetRootPath);

    private static bool IsImage(string path) => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyJpgAsset(string path) => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static bool IsAssetPackFileName(string fileName) => fileName.Equals("hero-transparent.png", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("hero.png", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("cinematic.png", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("closeup.png", StringComparison.OrdinalIgnoreCase);

    private static ResolvedAsset CreateResolvedAsset(string path, string key, bool oldAssetIgnoredBecauseHeroExists)
    {
        var fileName = Path.GetFileName(path);
        var source = IsAssetPackFileName(fileName)
            ? "AssetPack"
            : IsLegacyJpgAsset(path) ? "LegacyJpgAsset" : "LocalCuratedAsset";
        return new ResolvedAsset(path, fileName, source, oldAssetIgnoredBecauseHeroExists);
    }

    private static CelestialAsset ToAsset(SelectedObject obj, ResolvedAsset resolved, bool fallback) => new()
    {
        ObjectName = obj.Name,
        ObjectType = obj.Type,
        Category = obj.Key,
        LocalPath = resolved.Path,
        Source = resolved.Source,
        Title = $"{obj.Name} {resolved.Source} asset",
        OriginalUrl = resolved.FileName,
        OldAssetIgnoredBecauseHeroExists = resolved.OldAssetIgnoredBecauseHeroExists,
        FallbackUsed = fallback
    };

    private static HeroObjectScore ScoreHeroCandidate(SelectedObject obj, ThumbnailGenerationRequest request, IReadOnlySet<string> duplicatedKeys, bool conjunction)
    {
        var visibilityWeight = obj.Scene is { IsVisible: true } ? 1.00d : obj.Scene?.AltitudeDegrees.HasValue == true ? 0.65d : obj.Event is not null ? 0.72d : 0.35d;
        if (obj.Scene?.AltitudeDegrees is double altitude)
            visibilityWeight += Math.Clamp(altitude / 90d, 0, 1) * 0.55d;

        var brightnessWeight = obj.Key switch
        {
            "venus" => 1.20d,
            "jupiter" => 1.12d,
            "moon" => 1.02d,
            "mars" => 0.82d,
            "saturn" => 0.78d,
            "mercury" => 0.70d,
            "uranus" => 0.36d,
            "neptune" => 0.28d,
            "meteor-shower" => 1.15d,
            "solar-eclipse" or "lunar-eclipse" => 1.35d,
            _ when DeepSpaceKeys.Contains(obj.Key) => 0.72d,
            _ => 0.50d
        };
        var rarityWeight = obj.Key switch
        {
            "solar-eclipse" or "lunar-eclipse" => 2.80d,
            "meteor-shower" => 2.15d,
            "neptune" or "uranus" => 0.76d,
            _ when conjunction => 1.05d,
            _ when DeepSpaceKeys.Contains(obj.Key) => 0.72d,
            _ => 0.36d
        };
        var eventText = $"{request.Context.SpecialEvent?.EventType} {request.Context.SpecialEvent?.EventTitle} {obj.Event?.Category} {obj.Event?.Details} {obj.Scene?.VisibilityReason}";
        var astronomyEventWeight = 0d;
        if (eventText.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) astronomyEventWeight += 3.2d;
        if (eventText.Contains("meteor", StringComparison.OrdinalIgnoreCase)) astronomyEventWeight += 2.6d;
        if (eventText.Contains("conjunction", StringComparison.OrdinalIgnoreCase) || eventText.Contains("near", StringComparison.OrdinalIgnoreCase)) astronomyEventWeight += 1.4d;
        if (eventText.Contains("alignment", StringComparison.OrdinalIgnoreCase)) astronomyEventWeight += 1.7d;
        astronomyEventWeight += Math.Clamp(obj.Event?.Score ?? 0, 0, 1) * 1.2d;

        var moonPenalty = obj.Key == "moon" && !IsMoonMajor(request) ? -0.46d : 0d;
        var duplicationPenalty = duplicatedKeys.Contains(obj.Key) ? -0.18d : 0d;
        var total = visibilityWeight + brightnessWeight + rarityWeight + astronomyEventWeight + moonPenalty + duplicationPenalty;
        return new HeroObjectScore(obj, Math.Round(total, 3), Math.Round(visibilityWeight, 3), Math.Round(brightnessWeight, 3), Math.Round(rarityWeight, 3), Math.Round(astronomyEventWeight, 3), moonPenalty, duplicationPenalty);
    }

    private static double SupportScore(SelectedObject obj, SelectedObject hero, string mode)
    {
        var score = obj.Key switch { "venus" => 1.00, "jupiter" => 0.96, "moon" => 0.88, "saturn" => 0.80, "mars" => 0.72, _ => 0.42 };
        if (mode == "ConjunctionMode" && obj.Key is "moon" or "venus" or "jupiter") score += 0.55;
        if (obj.Key.Equals(hero.Key, StringComparison.OrdinalIgnoreCase)) score -= 1;
        return score;
    }

    private static string SelectCinematicMode(SelectedObject hero, IReadOnlyCollection<SelectedObject> candidates, ThumbnailGenerationRequest request, bool conjunction)
    {
        var text = $"{request.Context.SpecialEvent?.EventType} {request.Context.SpecialEvent?.EventTitle} {string.Join(' ', request.Context.Events.Select(e => e.Category + ' ' + e.ObjectName + ' ' + e.Details))}";
        if (hero.Key is "solar-eclipse" or "lunar-eclipse" || text.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "EclipseMode";
        if (hero.Key == "meteor-shower" || text.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return "MeteorShowerMode";
        if (text.Contains("alignment", StringComparison.OrdinalIgnoreCase)) return "RareAlignment";
        if (conjunction) return "ConjunctionMode";
        if (hero.Key == "moon" && IsMoonMajor(request)) return "MoonDominant";
        if (DeepSpaceKeys.Contains(hero.Key) || text.Contains("milky way", StringComparison.OrdinalIgnoreCase)) return "DeepSpaceMode";
        if (hero.Key is "jupiter" or "saturn" or "venus" or "mars") return "EpicPlanetFocus";
        return "WideSkyMode";
    }

    private static bool DetectConjunction(IReadOnlyCollection<SelectedObject> candidates, ThumbnailGenerationRequest request)
    {
        var text = $"{request.Context.SpecialEvent?.EventTitle} {string.Join(' ', request.Context.Events.Select(e => e.Category + ' ' + e.Details + ' ' + e.ObjectName))} {string.Join(' ', candidates.Select(c => c.Scene?.VisibilityReason))}";
        if (text.Contains("conjunction", StringComparison.OrdinalIgnoreCase) || text.Contains("near", StringComparison.OrdinalIgnoreCase) || text.Contains("close", StringComparison.OrdinalIgnoreCase))
            return true;
        return candidates.Any(c => c.Key == "moon") && candidates.Any(c => c.Key is "venus" or "jupiter" or "saturn");
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

    private static string BuildHook(Selection selection, ThumbnailGenerationRequest request)
    {
        var hi = LocalizationResolver.IsHindi(request.Context.Localization.ResolvedLanguage);
        var words = hi ? HindiHook(selection.Hero.Key) : EnglishHook(selection, request);
        var limited = LimitHook(words, hi ? 32 : 28, 4);
        return GenericHookPhrases.Contains(limited) ? EnglishHook(selection with { CinematicMode = "EpicPlanetFocus" }, request) : limited;
    }

    private static string EnglishHook(Selection selection, ThumbnailGenerationRequest request)
    {
        var hero = selection.Hero;
        return selection.CinematicMode switch
        {
            "EclipseMode" => "Eclipse Tonight",
            "MeteorShowerMode" => "Meteor Shower Peaks",
            "RareAlignment" => "Rare Planet Alignment",
            "ConjunctionMode" when selection.Support.Any(s => s.Key == "moon") && hero.Key != "moon" => $"Moon Near {hero.Name}",
            "ConjunctionMode" when selection.Support.FirstOrDefault() is { } support => $"{hero.Name} Near {support.Name}",
            "MoonDominant" => "Big Moon Tonight",
            "DeepSpaceMode" => "Milky Way Tonight",
            _ => hero.Key switch
            {
                "venus" => "Venus After Sunset",
                "saturn" => "Saturn Visible Tonight",
                "moon" => "Moon Tonight",
                "jupiter" => "Jupiter Tonight",
                "mars" => "Mars Tonight",
                "mercury" => "Mercury After Sunset",
                _ => "Don’t Miss This"
            }
        };
    }

    private static string HindiHook(string key) => key switch
    {
        "jupiter" => "आज रात बृहस्पति",
        "venus" => "आज शुक्र देखें",
        "moon" => "चांद आज रात",
        "meteor-shower" => "उल्का वर्षा",
        "solar-eclipse" or "lunar-eclipse" => "ग्रहण आज रात",
        _ => "आज रात घटना"
    };

    private static string LimitHook(string value, int maxChars, int maxWords)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(Math.Max(1, maxWords)).ToArray();
        while (words.Length > 1 && string.Join(' ', words).Length > maxChars)
            words = words.Take(words.Length - 1).ToArray();
        return string.Join(' ', words);
    }

    private static RectangleF HeroLandscapeRect(int width, int height) => new(width * 0.50f, height * 0.07f, width * 0.46f, height * 0.78f);
    private static RectangleF SupportLandscapeRect(int width, int height, int index) => new(width * (index == 0 ? 0.37f : 0.44f), height * (index == 0 ? 0.14f : 0.56f), width * 0.16f, width * 0.16f);
    private static RectangleF HeroPortraitRect(int width, int height) => new(width * 0.15f, height * 0.10f, width * 0.70f, width * 0.70f);
    private static RectangleF SupportPortraitRect(int width, int height, int index) => new(width * 0.67f, height * 0.39f, width * 0.18f, width * 0.18f);

    private static void DrawCinematicGradient(IImageProcessingContext ctx, int width, int height, bool portrait, string cinematicMode)
    {
        var topTint = cinematicMode is "DeepSpaceMode" ? Color.FromRgb(28, 11, 52) : Color.FromRgb(4, 10, 30);
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height), GradientRepetitionMode.None, new ColorStop(0, topTint.WithAlpha(0.30f)), new ColorStop(1, Color.Black.WithAlpha(0.74f))), new RectangleF(0, 0, width, height));
        if (!portrait)
            ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(width * 0.65f, 0), GradientRepetitionMode.None, new ColorStop(0, Color.Black.WithAlpha(0.78f)), new ColorStop(1, Color.Transparent)), new RectangleF(0, 0, width * 0.72f, height));
    }

    private static void DrawStars(IImageProcessingContext ctx, int width, int height, string seedText)
    {
        var random = new Random(StableHash(seedText));
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

    private static void DrawHook(IImageProcessingContext ctx, string hook, RectangleF bounds, bool portrait, string language)
    {
        if (string.IsNullOrWhiteSpace(hook)) return;
        var text = FormatHookForMobile(hook);
        var fontSize = ResolveFontSize(text, portrait);
        var font = CreateFont(fontSize, FontStyle.Bold, LocalizationResolver.IsHindi(language));
        var origin = new PointF(portrait ? bounds.X + bounds.Width / 2f : bounds.X, bounds.Y);
        var opts = new RichTextOptions(font) { Origin = origin, WrappingLength = bounds.Width, HorizontalAlignment = portrait ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, LineSpacing = 0.84f };
        ctx.DrawText(new RichTextOptions(opts) { Origin = new PointF(origin.X + 5, origin.Y + 6) }, text, Color.Black.WithAlpha(0.92f));
        ctx.DrawText(new RichTextOptions(opts) { Origin = new PointF(origin.X + 2, origin.Y + 2) }, text, Color.FromRgb(255, 214, 122).WithAlpha(0.34f));
        ctx.DrawText(opts, text, Color.White);
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


    private static float ResolveHeroGlowAlpha(string key, string cinematicMode)
    {
        var baseAlpha = key switch
        {
            "moon" => 0.30f,
            "mars" => 0.36f,
            "jupiter" => 0.40f,
            "neptune" or "uranus" => 0.34f,
            "meteor-shower" => 0.44f,
            _ => 0.32f
        };
        return cinematicMode is "EclipseMode" or "MeteorShowerMode" ? Math.Min(0.50f, baseAlpha + 0.08f) : baseAlpha;
    }

    private static RectangleF ResolveHeroRect(int width, int height, bool portrait, string mode, Random random)
    {
        var jitterX = (random.NextSingle() - 0.5f) * width * (portrait ? 0.035f : 0.045f);
        var jitterY = (random.NextSingle() - 0.5f) * height * 0.035f;
        RectangleF rect = portrait
            ? new RectangleF(width * 0.15f + jitterX, height * 0.10f + jitterY, width * 0.70f, width * 0.70f)
            : mode switch
            {
                "MoonDominant" => new RectangleF(width * 0.55f + jitterX, height * 0.02f + jitterY, width * 0.52f, height * 0.86f),
                "WideSkyMode" or "DeepSpaceMode" => new RectangleF(width * 0.57f + jitterX, height * 0.11f + jitterY, width * 0.38f, height * 0.66f),
                _ => new RectangleF(width * 0.50f + jitterX, height * 0.07f + jitterY, width * 0.46f, height * 0.78f)
            };
        return ClampRect(rect, width, height, 24);
    }

    private static RectangleF ResolveSupportRect(int width, int height, bool portrait, string mode, int index, Random random)
    {
        var offset = (random.NextSingle() - 0.5f) * width * 0.035f;
        if (portrait)
            return ClampRect(new RectangleF(width * 0.67f + offset, height * 0.39f, width * 0.18f, width * 0.18f), width, height, 28);
        var size = mode is "ConjunctionMode" or "RareAlignment" ? width * 0.18f : width * 0.15f;
        var x = index == 0 ? width * 0.36f : width * 0.44f;
        var y = index == 0 ? height * 0.13f : height * 0.57f;
        return ClampRect(new RectangleF(x + offset, y, size, size), width, height, 28);
    }

    private static RectangleF CalculateTextSafeRect(string hook, int width, int height, bool portrait, string language)
    {
        var margin = portrait ? 66f : 64f;
        var text = FormatHookForMobile(hook);
        var fontSize = ResolveFontSize(text, portrait);
        var lineCount = Math.Max(1, text.Split('\n').Length);
        var boxHeight = Math.Min(height * (portrait ? 0.22f : 0.26f), fontSize * lineCount * 1.08f + 24f);
        return portrait
            ? new RectangleF(margin, height * 0.70f, width - margin * 2, boxHeight)
            : new RectangleF(margin, height * 0.62f, width * 0.43f, boxHeight);
    }

    private static float ResolveFontSize(string text, bool portrait)
    {
        var length = text.Replace("\n", string.Empty).Length;
        var shortBase = portrait ? 116f : 104f;
        var longBase = portrait ? 88f : 78f;
        return length <= 15 ? shortBase : length <= 22 ? (shortBase + longBase) / 2f : longBase;
    }

    private static string FormatHookForMobile(string hook)
    {
        var words = hook.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hook.Length <= 16 || words.Length < 3)
            return hook;
        var split = (int)Math.Ceiling(words.Length / 2d);
        return string.Join(' ', words.Take(split)) + "\n" + string.Join(' ', words.Skip(split));
    }

    private static void DrawCinematicOverlays(IImageProcessingContext ctx, int width, int height, bool portrait, string mode, Random random)
    {
        var haze = mode switch { "DeepSpaceMode" => 0.13f, "EclipseMode" => 0.10f, _ => 0.07f };
        ctx.Fill(Color.FromRgb(54, 83, 140).WithAlpha(haze), new EllipsePolygon(width * (0.70f + random.NextSingle() * 0.08f), height * 0.28f, width * 0.44f));
        if (mode is "DeepSpaceMode" or "WideSkyMode")
            ctx.Fill(Color.FromRgb(119, 74, 180).WithAlpha(0.08f), new EllipsePolygon(width * 0.40f, height * 0.34f, width * 0.34f));
    }

    private static RectangleF AvoidCollision(RectangleF objectRect, RectangleF textRect, int width, int height, bool preferRight)
    {
        if (!Intersects(objectRect, textRect, 0.04f))
            return objectRect;
        var shifted = preferRight ? objectRect with { X = Math.Max(textRect.Right + width * 0.05f, objectRect.X) } : objectRect with { Y = Math.Max(40, textRect.Y - objectRect.Height - 24) };
        return ClampRect(shifted, width, height, 24);
    }

    private static RectangleF AvoidCollisions(RectangleF rect, IEnumerable<RectangleF> blockers, int width, int height)
    {
        var adjusted = rect;
        foreach (var blocker in blockers)
        {
            if (Intersects(adjusted, blocker, 0.05f))
                adjusted = adjusted with { Y = adjusted.Y + adjusted.Height * 0.55f };
        }
        return ClampRect(adjusted, width, height, 24);
    }

    private static bool Intersects(RectangleF a, RectangleF b, float paddingRatio)
    {
        var pad = Math.Min(a.Width, a.Height) * paddingRatio;
        var expanded = new RectangleF(a.X - pad, a.Y - pad, a.Width + pad * 2, a.Height + pad * 2);
        return expanded.Left < b.Right && expanded.Right > b.Left && expanded.Top < b.Bottom && expanded.Bottom > b.Top;
    }

    private static RectangleF ClampRect(RectangleF rect, int width, int height, float margin)
    {
        var x = Math.Clamp(rect.X, margin, Math.Max(margin, width - rect.Width - margin));
        var y = Math.Clamp(rect.Y, margin, Math.Max(margin, height - rect.Height - margin));
        return new RectangleF(x, y, rect.Width, rect.Height);
    }

    private static IEnumerable<string> BuildSafeZoneWarnings(RectangleF textBox, IReadOnlyCollection<RectangleF> objectRects, int width, int height, bool portrait)
    {
        var marginX = width * 0.045f;
        var marginY = height * 0.045f;
        if (textBox.Left < marginX || textBox.Right > width - marginX || textBox.Top < marginY || textBox.Bottom > height - marginY)
            yield return "text-outside-mobile-safe-zone";
        if (!portrait && objectRects.First().Left < width * 0.45f)
            yield return "hero-too-close-to-text-zone";
        foreach (var obj in objectRects)
        {
            if (obj.Left < marginX || obj.Right > width - marginX || obj.Top < marginY || obj.Bottom > height - marginY)
                yield return "object-outside-mobile-safe-zone";
        }
    }

    private static double ScoreComposition(RectangleF heroRect, RectangleF textBox, IEnumerable<RectangleF> supports, int width, int height, IReadOnlyCollection<string> warnings)
    {
        var thirdX = width * 2d / 3d;
        var heroCenter = heroRect.Left + heroRect.Width / 2d;
        var thirdScore = 1d - Math.Min(1d, Math.Abs(heroCenter - thirdX) / (width * 0.5d));
        var textObjectClearance = Intersects(heroRect, textBox, 0.02f) ? 0.55d : 1d;
        var supportPenalty = supports.Count(r => Intersects(r, heroRect, 0.02f)) * 0.08d;
        var warningPenalty = warnings.Count * 0.05d;
        return Math.Round(Math.Clamp(0.52d + thirdScore * 0.30d + textObjectClearance * 0.18d - supportPenalty - warningPenalty, 0, 1), 3);
    }

    private static double ScoreReadability(string hook, RectangleF textBox, int width, int height, bool portrait)
    {
        if (string.IsNullOrWhiteSpace(hook))
            return 0;
        var charScore = hook.Length <= 28 ? 1d : Math.Max(0.45d, 1d - (hook.Length - 28) / 20d);
        var safeScore = textBox.Left >= width * 0.04f && textBox.Right <= width * 0.96f && textBox.Bottom <= height * 0.93f ? 1d : 0.72d;
        var sizeScore = ResolveFontSize(FormatHookForMobile(hook), portrait) >= (portrait ? 86 : 72) ? 1d : 0.80d;
        return Math.Round((charScore * 0.45d) + (safeScore * 0.35d) + (sizeScore * 0.20d), 3);
    }

    private static double ScoreClickability(string hook, string mode, double heroScore, double readabilityScore)
    {
        var curiosity = hook.Contains("Rare", StringComparison.OrdinalIgnoreCase) || hook.Contains("Don’t", StringComparison.OrdinalIgnoreCase) || hook.Contains("Peaks", StringComparison.OrdinalIgnoreCase) ? 1d : 0.82d;
        var modeBoost = mode is "EclipseMode" or "MeteorShowerMode" or "RareAlignment" or "ConjunctionMode" ? 0.10d : 0.04d;
        return Math.Round(Math.Clamp(readabilityScore * 0.45d + curiosity * 0.35d + Math.Min(heroScore / 5d, 1d) * 0.10d + modeBoost, 0, 1), 3);
    }

    private static object ToBounds(RectangleF rect) => new { x = Math.Round(rect.X, 1), y = Math.Round(rect.Y, 1), width = Math.Round(rect.Width, 1), height = Math.Round(rect.Height, 1) };

    private static Random CreateDeterministicRandom(ThumbnailGenerationRequest request, string mode)
        => new(StableHash(StableSeedText(request, mode)));

    private static string StableSeedText(ThumbnailGenerationRequest request, string mode)
        => $"{request.Context.Date:yyyy-MM-dd}|{request.Context.LocationName}|{request.ContentType}|{request.IsShortForm}|{mode}";

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var ch in value)
                hash = hash * 31 + ch;
            return hash == int.MinValue ? 0 : Math.Abs(hash);
        }
    }

    private static Font CreateFont(float size, FontStyle style, bool preferHindi)
    {
        var families = SystemFonts.Collection.Families;
        var family = preferHindi ? families.FirstOrDefault(f => f.Name.Contains("Noto", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Devanagari", StringComparison.OrdinalIgnoreCase)) : default;
        if (string.IsNullOrWhiteSpace(family.Name)) family = families.FirstOrDefault(f => f.Name.Equals("Arial", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(family.Name)) family = families.First();
        return family.CreateFont(size, style);
    }

    private static async Task WriteSelectionAsync(ThumbnailPlan plan, string thumbnailsDirectory, CompositionDiagnostics composition, CancellationToken cancellationToken)
    {
        var payload = new
        {
            mode = "LocalAssetCollage",
            cinematicMode = composition.CinematicMode,
            heroObject = plan.CelestialSelection?.HeroObject ?? "",
            supportObjects = plan.CelestialSelection?.SupportObjects ?? Array.Empty<string>(),
            selectedHook = plan.CelestialSelection?.SelectedHook ?? plan.PrimaryThumbnailText,
            assetPriorityUsed = BuildAssetPriorityUsed(plan.CelestialSelection?.AssetSources ?? Array.Empty<CelestialAsset>()),
            longThumbnailPath = plan.LongThumbnailPath,
            shortThumbnailPath = plan.ShortThumbnailPath,
            assetSources = plan.CelestialSelection?.AssetSources ?? Array.Empty<CelestialAsset>(),
            fallbackUsed = plan.FallbackUsed,
            selectedAssetFileName = plan.CelestialSelection?.AssetSources.FirstOrDefault() is { } selected ? Path.GetFileName(selected.LocalPath) : string.Empty,
            selectedAssetSource = plan.CelestialSelection?.AssetSources.FirstOrDefault()?.Source ?? string.Empty,
            transparentAssetUsed = plan.CelestialSelection?.AssetSources.FirstOrDefault() is { } first && IsTransparentAsset(first.LocalPath),
            layoutUsed = composition.LayoutUsed,
            heroObjectScale = composition.HeroObjectScale,
            supportObjectScales = composition.SupportObjectScales,
            compositionScore = composition.CompositionScore,
            readabilityScore = composition.ReadabilityScore,
            clickabilityScore = composition.ClickabilityScore,
            objectOverlapWarnings = composition.ObjectOverlapWarnings,
            safeZoneWarnings = composition.SafeZoneWarnings
        };
        await File.WriteAllTextAsync(Path.Combine(thumbnailsDirectory, "thumbnail-selection.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static async Task WriteAnalysisAsync(ThumbnailPlan plan, ThumbnailGenerationRequest request, int width, int height, IReadOnlyCollection<CelestialAsset> assets, IReadOnlyCollection<string> warnings, CompositionDiagnostics composition, CancellationToken cancellationToken)
    {
        var outputPath = plan.ThumbnailPath ?? string.Empty;
        var payload = new
        {
            assetsFound = assets.Where(a => File.Exists(a.LocalPath)).Select(a => a.LocalPath).ToArray(),
            assetsMissing = warnings.Where(w => w.StartsWith("Missing", StringComparison.OrdinalIgnoreCase)).ToArray(),
            assetPriorityUsed = BuildAssetPriorityUsed(assets),
            selectedAssetFileName = assets.FirstOrDefault() is { } selected ? Path.GetFileName(selected.LocalPath) : string.Empty,
            selectedAssetSource = assets.FirstOrDefault()?.Source ?? string.Empty,
            oldAssetIgnoredBecauseHeroExists = assets.Any(a => a.OldAssetIgnoredBecauseHeroExists),
            transparentAssetUsed = assets.FirstOrDefault() is { } first && IsTransparentAsset(first.LocalPath),
            transparentAssetsUsed = composition.TransparentAssetsUsed,
            cardStyleRemoved = composition.CardStyleRemoved,
            objectCount = composition.ObjectCount,
            layoutWarnings = composition.LayoutWarnings,
            heroObjectScale = composition.HeroObjectScale,
            supportObjectScales = composition.SupportObjectScales,
            layoutUsed = composition.LayoutUsed,
            cinematicMode = composition.CinematicMode,
            compositionScore = composition.CompositionScore,
            readabilityScore = composition.ReadabilityScore,
            clickabilityScore = composition.ClickabilityScore,
            objectOverlapWarnings = composition.ObjectOverlapWarnings,
            safeZoneWarnings = composition.SafeZoneWarnings,
            textBounds = composition.TextBounds,
            language = request.Context.Localization.ResolvedLanguage,
            dimensions = new { width, height, fileSizeBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0 },
            warnings
        };
        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "thumbnails", "thumbnail-analysis-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static async Task WriteFallbackAnalysisAsync(ThumbnailGenerationRequest request, ThumbnailPlan fallback, IReadOnlyCollection<string> warnings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(request.OutputDirectory, "thumbnails"));
        var payload = new { assetsFound = Array.Empty<string>(), assetsMissing = Array.Empty<string>(), assetPriorityUsed = Array.Empty<string>(), selectedAssetFileName = string.Empty, selectedAssetSource = string.Empty, oldAssetIgnoredBecauseHeroExists = false, transparentAssetUsed = false, transparentAssetsUsed = 0, cardStyleRemoved = true, objectCount = 0, layoutWarnings = new[] { "fallback-plan" }, heroObjectScale = 0, supportObjectScales = Array.Empty<double>(), layoutUsed = "FallbackToStellariumFrame", language = request.Context.Localization.ResolvedLanguage, dimensions = new { width = 0, height = 0, fileSizeBytes = 0 }, warnings };
        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "thumbnails", "thumbnail-analysis-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }



    private static async Task WriteFallbackCinematicReportAsync(ThumbnailGenerationRequest request, ThumbnailPlan fallback, IReadOnlyCollection<string> warnings, CancellationToken cancellationToken)
    {
        var thumbnailsDirectory = Path.Combine(request.OutputDirectory, "thumbnails");
        Directory.CreateDirectory(thumbnailsDirectory);
        var payload = new
        {
            cinematicMode = "WideSkyMode",
            heroObject = fallback.CelestialSelection?.HeroObject ?? "FallbackFrame",
            supportObjects = Array.Empty<string>(),
            hookText = fallback.PrimaryThumbnailText,
            compositionScore = 0d,
            readabilityScore = 0d,
            clickabilityScore = 0d,
            objectOverlapWarnings = Array.Empty<string>(),
            safeZoneWarnings = warnings.ToArray()
        };
        await File.WriteAllTextAsync(Path.Combine(thumbnailsDirectory, "thumbnail-cinematic-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static async Task WriteCinematicReportAsync(ThumbnailPlan plan, Selection selection, CompositionDiagnostics composition, string thumbnailsDirectory, CancellationToken cancellationToken)
    {
        var payload = new
        {
            cinematicMode = selection.CinematicMode,
            heroObject = plan.CelestialSelection?.HeroObject ?? selection.Hero.Name,
            supportObjects = plan.CelestialSelection?.SupportObjects ?? selection.Support.Select(s => s.Name).ToArray(),
            hookText = plan.CelestialSelection?.SelectedHook ?? plan.PrimaryThumbnailText,
            compositionScore = composition.CompositionScore,
            readabilityScore = composition.ReadabilityScore,
            clickabilityScore = composition.ClickabilityScore,
            objectOverlapWarnings = composition.ObjectOverlapWarnings,
            safeZoneWarnings = composition.SafeZoneWarnings,
            heroScore = selection.HeroScore,
            candidateScores = selection.HeroScores.Select(s => new
            {
                objectName = s.Object.Name,
                objectKey = s.Object.Key,
                score = s.TotalScore,
                s.VisibilityWeight,
                s.BrightnessWeight,
                s.RarityWeight,
                s.AstronomyEventWeight,
                s.MoonPenalty,
                s.DuplicationPenalty
            }).ToArray(),
            textBounds = composition.TextBounds,
            layoutUsed = composition.LayoutUsed
        };
        await File.WriteAllTextAsync(Path.Combine(thumbnailsDirectory, "thumbnail-cinematic-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static string[] BuildAssetPriorityUsed(IReadOnlyCollection<CelestialAsset> assets) => assets.Select(a => $"{a.Source}:{Path.GetFileName(a.LocalPath)}").ToArray();

    private sealed record CompositionDiagnostics(string LayoutUsed, double HeroObjectScale, IReadOnlyCollection<double> SupportObjectScales, int TransparentAssetsUsed, bool CardStyleRemoved, int ObjectCount, IReadOnlyCollection<string> LayoutWarnings, string CinematicMode, double CompositionScore, double ReadabilityScore, double ClickabilityScore, IReadOnlyCollection<string> ObjectOverlapWarnings, IReadOnlyCollection<string> SafeZoneWarnings, object TextBounds);
    private sealed record ResolvedAsset(string Path, string FileName, string Source, bool OldAssetIgnoredBecauseHeroExists);
    private sealed record SelectedObject(string Name, string Type, string Key, bool FallbackAllowed, SceneObservationContext? Scene = null, AstronomyEventModel? Event = null);
    private sealed record Selection(SelectedObject Hero, IReadOnlyCollection<SelectedObject> Support, IReadOnlyCollection<object> VisibilityData, bool IsSpecialEvent, string CinematicMode, IReadOnlyCollection<HeroObjectScore> HeroScores, bool HasConjunction)
    {
        public HeroObjectScore? HeroScore => HeroScores.FirstOrDefault(s => s.Object.Key.Equals(Hero.Key, StringComparison.OrdinalIgnoreCase));
    }
    private sealed record HeroObjectScore(SelectedObject Object, double TotalScore, double VisibilityWeight, double BrightnessWeight, double RarityWeight, double AstronomyEventWeight, double MoonPenalty, double DuplicationPenalty);
}
