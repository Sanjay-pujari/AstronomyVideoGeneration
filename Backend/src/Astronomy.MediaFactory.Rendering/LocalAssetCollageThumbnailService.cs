using System.Globalization;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
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
    private static readonly string[] PlanetHeroPriority = ["jupiter", "venus", "saturn", "moon", "mars"];
    private static readonly HashSet<string> GenericHookPhrases = new(StringComparer.OrdinalIgnoreCase) { "TONIGHT'S SKY", "LOOK W TONIGHT", "PLANETS VISIBLE", "SKY WATCH", "VISIBLE TONIGHT" };
    private static readonly HashSet<string> DeepSpaceKeys = new(StringComparer.OrdinalIgnoreCase) { "milky-way", "andromeda-galaxy", "orion-nebula", "pleiades" };
    private static readonly HashSet<string> BackgroundDeepSpaceKeys = new(StringComparer.OrdinalIgnoreCase) { "milky-way", "andromeda-galaxy" };

    private readonly IThumbnailStrategyService _strategyService;
    private readonly ThumbnailOptions _options;
    private readonly ThumbnailFontOptions _fontOptions;
    private readonly RenderingOptions _renderingOptions;
    private readonly IProcessRunner _processRunner;
    private readonly IRuntimeAssetPathResolver _assetPathResolver;
    private readonly ILogger<LocalAssetCollageThumbnailService> _logger;

    public LocalAssetCollageThumbnailService(
        IThumbnailStrategyService strategyService,
        IOptions<ThumbnailOptions> options,
        ILogger<LocalAssetCollageThumbnailService> logger,
        IOptions<ThumbnailFontOptions>? fontOptions = null,
        IOptions<RenderingOptions>? renderingOptions = null,
        IProcessRunner? processRunner = null,
        IRuntimeAssetPathResolver? assetPathResolver = null)
    {
        _strategyService = strategyService;
        _options = options.Value;
        _fontOptions = fontOptions?.Value ?? new ThumbnailFontOptions();
        _renderingOptions = renderingOptions?.Value ?? new RenderingOptions();
        _processRunner = processRunner ?? new ProcessRunner();
        _assetPathResolver = assetPathResolver ?? new RuntimeAssetPathResolver();
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
            ValidateThumbnailOutput(outputPath, request.IsShortForm);

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
            await WriteThumbnailRuntimeAssetsReportAsync(thumbnailsDirectory, request.Context.Localization.ResolvedLanguage, ResolveThumbnailFont(request.Context.Localization.ResolvedLanguage, hook), assets, cancellationToken);
            return plan;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !IsThumbnailTextRenderException(ex) && !IsThumbnailTextRenderingFailure(ex) && !IsThumbnailOutputValidationFailure(ex))
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
        var allowDeepSpaceHero = IsDeepSpaceHeroAllowed(request);
        var scores = candidates
            .Select(o => ScoreHeroCandidate(o, request, duplicatedKeys, conjunction, allowDeepSpaceHero, _options.DeepSpaceHeroPenalty))
            .OrderByDescending(s => s.TotalScore)
            .ThenBy(s => PlanetPriorityRank(s.Object.Key))
            .ThenBy(s => s.Object.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hero = SelectHeroObject(scores, allowDeepSpaceHero);
        hero ??= new SelectedObject("Milky Way", "MilkyWay", "milky-way", true);
        var mode = SelectCinematicMode(hero, candidates, request, conjunction);
        var supportsAllowed = mode is "EclipseMode" or "MeteorShowerMode" ? Math.Min(maxSupport, 1) : Math.Min(maxSupport, 2);
        var support = candidates
            .Where(o => !o.Key.Equals(hero.Key, StringComparison.OrdinalIgnoreCase))
            .Where(o => HasAsset(o.Key))
            .Where(o => !BackgroundDeepSpaceKeys.Contains(o.Key))
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

    private CelestialAsset? ResolveDeepSpaceBackgroundAsset(ThumbnailGenerationRequest request, IReadOnlyCollection<CelestialAsset> assets, string heroKey)
    {
        var existing = assets.FirstOrDefault(a => BackgroundDeepSpaceKeys.Contains(a.Category) && !a.Category.Equals(heroKey, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        var candidate = request.Context.SceneObservationContexts
            .Where(s => s.IsVisible || s.AltitudeDegrees.HasValue)
            .Select(s => new SelectedObject(s.ObjectName, s.ObjectType, CelestialObjectKeyMapper.Map(s.ObjectName, s.ObjectType), false, s))
            .Where(o => BackgroundDeepSpaceKeys.Contains(o.Key) && !o.Key.Equals(heroKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => o.Key.Equals("milky-way", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
        if (candidate is null)
            return null;

        var resolved = FindAsset(candidate.Key);
        return resolved is null ? null : ToAsset(candidate, resolved, false);
    }

    private async Task<CompositionDiagnostics> ComposeAsync(ThumbnailGenerationRequest request, string outputPath, int width, int height, IReadOnlyList<CelestialAsset> assets, Selection selection, string hook, List<string> warnings, CancellationToken cancellationToken)
    {
        var portrait = request.IsShortForm;
        var random = CreateDeterministicRandom(request, selection.CinematicMode);
        using var canvas = new Image<Rgba32>(width, height, Color.FromRgb(3, 8, 22));
        await DrawBackgroundAsync(canvas, request, width, height, warnings, cancellationToken);
        if (ResolveDeepSpaceBackgroundAsset(request, assets, selection.Hero.Key) is { } backgroundAsset)
            await DrawDeepSpaceBackgroundLayerAsync(canvas, backgroundAsset, width, height, random, cancellationToken);
        canvas.Mutate(ctx =>
        {
            DrawCinematicGradient(ctx, width, height, portrait, selection.CinematicMode);
            DrawEnvironmentalDepthTexture(ctx, width, height, portrait, selection.CinematicMode, random);
            DrawStars(ctx, width, height, StableSeedText(request, selection.CinematicMode));
        });

        var textBox = CalculateTextSafeRect(hook, width, height, portrait, request.Context.Localization.ResolvedLanguage);
        var hero = assets[0];
        var heroRect = ResolveHeroRect(width, height, portrait, selection.CinematicMode, random);
        heroRect = AvoidCollision(heroRect, textBox, width, height, preferRight: !portrait);
        var dateBox = CalculateDateLocationRect(width, height, portrait);
        var brandBox = CalculateBrandRect(width, height, portrait);
        var seed = StableHash(StableSeedText(request, selection.CinematicMode));
        ProceduralAtmosphereBuffer.BlendIntoScene(canvas, seed, selection.CinematicMode, 0.62f, _options.Atmosphere, InflateRect(textBox, 32));
        SpaceFogRenderer.RenderMicroStarfield(canvas, textBox, dateBox, brandBox, seed, portrait);
        var fogResult = SpaceFogRenderer.Render(canvas, heroRect, textBox, dateBox, brandBox, seed, portrait, selection.CinematicMode);

        await DrawObjectAsync(canvas, hero, heroRect, true, selection.CinematicMode, 0, cancellationToken);

        var supports = assets.Skip(1).Where(a => !BackgroundDeepSpaceKeys.Contains(a.Category)).Take(portrait ? 1 : 2).ToArray();
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
            await DrawObjectAsync(canvas, supports[i], supportRect, false, selection.CinematicMode, i + 1, cancellationToken);
        }

        canvas.Mutate(ctx =>
        {
            DrawDateLocation(ctx, request, width, height, portrait);
            DrawBrand(ctx, width, height, portrait);
            DrawVignette(ctx, width, height);
        });
        SpaceFogRenderer.ApplyCinematicColorGrade(canvas);

        var safeZoneWarnings = BuildSafeZoneWarnings(textBox, objectRects, width, height, portrait).ToArray();
        var layoutWarnings = BuildLayoutWarnings(portrait, heroRect.Width / width, supportScales)
            .Concat(overlapWarnings)
            .Concat(safeZoneWarnings)
            .ToArray();
        var foregroundObjectAreaPercent = Math.Round(objectRects.Sum(r => (r.Width * r.Height) / (width * (double)height)) * 100d, 2);
        var overlapPenaltyApplied = layoutWarnings.Any(w => w.Contains("overlap", StringComparison.OrdinalIgnoreCase) || w.Contains("near-hero", StringComparison.OrdinalIgnoreCase));
        var oversizedGlowPenaltyApplied = GlowRadiusScale(hero.Category, true) > 0.40f;
        var compositionScore = ScoreComposition(heroRect, textBox, objectRects.Skip(1), width, height, layoutWarnings, hero, foregroundObjectAreaPercent, oversizedGlowPenaltyApplied);
        var readabilityScore = ScoreReadability(hook, textBox, width, height, portrait);
        var clickabilityScore = ScoreClickability(hook, selection.CinematicMode, selection.HeroScore?.TotalScore ?? 0, readabilityScore);
        var compositionBalanceScore = ScoreCompositionBalance(heroRect, textBox, objectRects.Skip(1), width, height, foregroundObjectAreaPercent, overlapPenaltyApplied);
        var depthScore = ScoreDepthSeparation(heroRect, objectRects.Skip(1), width, height, foregroundObjectAreaPercent, overlapPenaltyApplied);
        var atmosphericBlendScore = ScoreAtmosphericBlend(selection.CinematicMode, supports.Length, compositionBalanceScore, oversizedGlowPenaltyApplied);
        var negativeSpaceScore = ScoreNegativeSpace(heroRect, textBox, objectRects.Skip(1), width, height, foregroundObjectAreaPercent);
        var heroIsolationScore = ScoreHeroIsolation(heroRect, textBox, objectRects.Skip(1), width, height, overlapPenaltyApplied);
        var visualArtifactPenalty = ScoreVisualArtifactPenalty(oversizedGlowPenaltyApplied, overlapPenaltyApplied, foregroundObjectAreaPercent);
        var rectangularAtmosphereRisk = ProceduralAtmosphereBuffer.EstimateRectangularGeometryRisk(canvas, Math.Max(1, width / 240), Math.Max(1, height / 180));
        var compositingVisibilityPenalty = ScoreCompositingVisibilityPenalty(heroRect, width, height, compositionBalanceScore, oversizedGlowPenaltyApplied, rectangularAtmosphereRisk);
        var organicAtmosphereScore = ScoreOrganicAtmosphere(atmosphericBlendScore, depthScore, selection.CinematicMode);
        var naturalLightingScore = ScoreNaturalLighting(heroRect, width, height, oversizedGlowPenaltyApplied);
        var edgeIntegrationScore = ScoreEdgeIntegration(hero.Category, compositingVisibilityPenalty, atmosphericBlendScore, naturalLightingScore, oversizedGlowPenaltyApplied);
        var compositingSeamPenalty = ScoreCompositingSeamPenalty(visualArtifactPenalty, compositingVisibilityPenalty, edgeIntegrationScore);
        var atmosphereContinuityScore = ScoreAtmosphereContinuity(organicAtmosphereScore, atmosphericBlendScore, compositingSeamPenalty, rectangularAtmosphereRisk);
        var proceduralAtmosphereScore = ProceduralAtmosphereBuffer.ScoreProceduralAtmosphere(organicAtmosphereScore, atmosphereContinuityScore, compositingSeamPenalty, rectangularAtmosphereRisk);
        var environmentalDepthScore = ScoreEnvironmentalDepth(selection.CinematicMode, supports.Length, organicAtmosphereScore, atmosphereContinuityScore);
        var supportObjectDepthScore = ScoreSupportObjectDepth(supportScales, depthScore, atmosphericBlendScore, supports.Length);
        var cinematicSubtletyScore = ScoreCinematicSubtlety(organicAtmosphereScore, naturalLightingScore, visualArtifactPenalty, compositingVisibilityPenalty, readabilityScore, edgeIntegrationScore, atmosphereContinuityScore);
        var atmosphereDepthScore = ScoreAtmosphereDepth(depthScore, environmentalDepthScore, atmosphereContinuityScore, fogResult.FogBlendScore);
        var fogBlendScore = ScoreFogBlend(fogResult.FogBlendScore, readabilityScore, heroIsolationScore);
        var proceduralArtifactPenalty = ScoreProceduralArtifactPenalty(rectangularAtmosphereRisk, _options.Atmosphere.ProceduralShapeOpacity, _options.Atmosphere.ProceduralShapeContrast);
        var cinematicSoftnessScore = ScoreCinematicSoftness(cinematicSubtletyScore, fogBlendScore, edgeIntegrationScore, proceduralArtifactPenalty);
        var atmosphericRealismScore = ScoreAtmosphericRealism(atmosphereDepthScore, fogBlendScore, edgeIntegrationScore, cinematicSoftnessScore, proceduralArtifactPenalty, readabilityScore);
        var cinematicRealismScore = ScoreCinematicRealism(depthScore, atmosphericBlendScore, negativeSpaceScore, heroIsolationScore, compositionBalanceScore, readabilityScore, cinematicSubtletyScore, edgeIntegrationScore, environmentalDepthScore, supportObjectDepthScore, compositingSeamPenalty);

        await canvas.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = Math.Clamp(_options.JpegQuality, 1, 100) }, cancellationToken);
        if (_options.EnableHookText)
            await RenderHookTextAsync(outputPath, hook, textBox, portrait, request.Context.Localization.ResolvedLanguage, assets, cancellationToken);

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
            TextBounds: ToBounds(textBox),
            GlowIntensity: Math.Round(GlowAlpha(hero.Category, true, selection.CinematicMode), 3),
            DeepSpacePenaltyApplied: selection.HeroScore?.DeepSpacePenalty < 0 || selection.HeroScores.Any(s => s.DeepSpacePenalty < 0),
            ForegroundObjectAreaPercent: foregroundObjectAreaPercent,
            OverlapPenaltyApplied: overlapPenaltyApplied,
            CompositionBalanceScore: compositionBalanceScore,
            DepthScore: depthScore,
            AtmosphericBlendScore: atmosphericBlendScore,
            NegativeSpaceScore: negativeSpaceScore,
            HeroIsolationScore: heroIsolationScore,
            CinematicRealismScore: cinematicRealismScore,
            VisualPreset: string.IsNullOrWhiteSpace(_options.VisualPreset) ? "Premium Documentary" : _options.VisualPreset,
            OrganicAtmosphereScore: organicAtmosphereScore,
            ProceduralAtmosphereScore: proceduralAtmosphereScore,
            NaturalLightingScore: naturalLightingScore,
            VisualArtifactPenalty: visualArtifactPenalty,
            CompositingVisibilityPenalty: compositingVisibilityPenalty,
            CinematicSubtletyScore: cinematicSubtletyScore,
            EdgeIntegrationScore: edgeIntegrationScore,
            CompositingSeamPenalty: compositingSeamPenalty,
            AtmosphereContinuityScore: atmosphereContinuityScore,
            EnvironmentalDepthScore: environmentalDepthScore,
            SupportObjectDepthScore: supportObjectDepthScore,
            AtmosphereDepthScore: atmosphereDepthScore,
            FogBlendScore: fogBlendScore,
            ProceduralArtifactPenalty: proceduralArtifactPenalty,
            CinematicSoftnessScore: cinematicSoftnessScore,
            AtmosphericRealismScore: atmosphericRealismScore);
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
            var deepSpaceBackground = background.Contains($"{Path.DirectorySeparatorChar}milky-way{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || background.Contains($"{Path.AltDirectorySeparatorChar}milky-way{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
            image.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Crop }).GaussianBlur(deepSpaceBackground ? 2.2f : 1.3f).Brightness(deepSpaceBackground ? 0.58f : 0.45f).Contrast(1.04f));
            canvas.Mutate(ctx => ctx.DrawImage(image, new Point(0, 0), deepSpaceBackground ? 0.16f : 1f));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Background asset could not be loaded: {ex.Message}");
        }
    }

    private static async Task DrawObjectAsync(Image<Rgba32> canvas, CelestialAsset asset, RectangleF rect, bool hero, string cinematicMode, int depthRank, CancellationToken cancellationToken)
    {
        using var obj = await Image.LoadAsync<Rgba32>(asset.LocalPath, cancellationToken);
        if (DeepSpaceKeys.Contains(asset.Category) && IsTransparentAsset(asset.LocalPath))
            CleanDeepSpaceAlpha(obj);
        var depth = Math.Clamp(depthRank, 0, 3);
        var supportOpacity = hero ? 1f : Math.Max(0.30f, 0.52f - depth * 0.08f);
        obj.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions { Size = new Size((int)rect.Width, (int)rect.Height), Mode = ResizeMode.Max });
            if (DeepSpaceKeys.Contains(asset.Category)) ctx.GaussianBlur(hero ? 0.45f : 1.35f + depth * 0.32f).Brightness(hero ? 0.90f : 0.66f).Contrast(hero ? 0.92f : 0.76f);
            if (!hero) ctx.GaussianBlur(1.75f + depth * 0.48f).Brightness(0.60f - depth * 0.035f).Contrast(0.74f).Saturate(0.50f);
            else ctx.GaussianBlur(0.16f).Contrast(1.04f).Brightness(1.0f).Saturate(0.94f);
        });
        ApplyOrganicObjectAlpha(obj, asset.Category, hero, depth);
        ObjectEdgeIntegrationService.ApplyEdgeFeather(obj, asset.Category, hero, depth);
        var x = (int)(rect.X + (rect.Width - obj.Width) / 2f);
        var y = (int)(rect.Y + (rect.Height - obj.Height) / 2f);
        canvas.Mutate(ctx =>
        {
            var glowColor = ResolveGlow(asset.Category).WithAlpha(GlowAlpha(asset.Category, hero, cinematicMode) * (hero ? 0.78f : 0.38f));
            if (asset.Category.Equals("meteor-shower", StringComparison.OrdinalIgnoreCase))
                ctx.DrawLine(glowColor, Math.Max(4, rect.Width * 0.024f), new PointF(rect.Left + rect.Width * 0.12f, rect.Top + rect.Height * 0.25f), new PointF(rect.Right - rect.Width * 0.08f, rect.Bottom - rect.Height * 0.22f));
            if (!asset.Category.Equals("meteor-shower", StringComparison.OrdinalIgnoreCase))
            {
                DrawDirectionalLight(ctx, rect, asset.Category, hero);
            }
            DrawOrganicDepthHaze(ctx, rect, depth, hero);
            if (hero)
            {
                DrawEdgeLightWrap(ctx, obj, new Point(x, y), asset.Category, cinematicMode);
                ObjectEdgeIntegrationService.DrawAtmosphericIntegration(ctx, obj, new Point(x, y), rect, asset.Category, true, cinematicMode);
            }
            else
            {
                ObjectEdgeIntegrationService.DrawAtmosphericIntegration(ctx, obj, new Point(x, y), rect, asset.Category, false, cinematicMode);
                DrawSupportAtmosphericShadow(ctx, rect, depth);
            }
            if (IsTransparentAsset(asset.LocalPath))
                ctx.DrawImage(obj, new Point(x + (hero ? 4 : 2), y + (hero ? 6 : 3)), hero ? 0.035f : 0.020f);
            var objectOpacity = DeepSpaceKeys.Contains(asset.Category) ? (hero ? 0.62f : supportOpacity * 0.40f) : supportOpacity;
            ctx.DrawImage(obj, new Point(x, y), objectOpacity);
            if (!hero)
                DrawSupportAtmosphericFade(ctx, rect, depth);
        });
    }



    private static void ApplyOrganicObjectAlpha(Image<Rgba32> image, string category, bool hero, int depth)
    {
        var transparentAsset = HasTransparentPixels(image);
        var featherStart = transparentAsset ? (hero ? 0.90f : 0.72f) : (hero ? 0.76f : 0.58f);
        var featherEnd = transparentAsset ? (hero ? 1.10f : 0.98f) : (hero ? 1.02f : 0.88f);
        if (DeepSpaceKeys.Contains(category))
        {
            featherStart -= hero ? 0.08f : 0.12f;
            featherEnd -= hero ? 0.04f : 0.08f;
        }

        image.ProcessPixelRows(accessor =>
        {
            var centerX = (accessor.Width - 1) / 2f;
            var centerY = (accessor.Height - 1) / 2f;
            var radiusX = Math.Max(1f, accessor.Width * (hero ? 0.50f : 0.47f));
            var radiusY = Math.Max(1f, accessor.Height * (hero ? 0.50f : 0.47f));
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.A == 0) continue;
                    var dx = (x - centerX) / radiusX;
                    var dy = (y - centerY) / radiusY;
                    var radial = MathF.Sqrt(dx * dx + dy * dy);
                    var noise = ValueNoise01(x * 0.035f, y * 0.035f, StableHash(category)) - 0.5f;
                    var warped = radial + noise * (hero ? 0.055f : 0.105f) + MathF.Sin((x + y) * 0.019f) * 0.018f;
                    var fade = 1f - SmoothStep(featherStart, featherEnd, warped);
                    if (!hero)
                        fade *= 0.82f - Math.Clamp(depth, 0, 3) * 0.055f;
                    if (transparentAsset && p.A < 248)
                        fade *= 0.92f;
                    p.A = (byte)Math.Clamp(p.A * fade, 0, 255);
                    row[x] = p;
                }
            }
        });
    }

    private static bool HasTransparentPixels(Image<Rgba32> image)
    {
        var found = false;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height && !found; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A < 250)
                    {
                        found = true;
                        break;
                    }
                }
            }
        });
        return found;
    }

    private static void DrawEdgeLightWrap(IImageProcessingContext ctx, Image<Rgba32> obj, Point position, string category, string cinematicMode)
    {
        using var wrap = obj.Clone();
        var glowPixel = ResolveGlow(category).ToPixel<Rgba32>();
        var modeBoost = cinematicMode is "EclipseMode" or "MeteorShowerMode" ? 1.20f : 1f;
        wrap.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var a = row[x].A / 255f;
                    var directional = 0.55f + (1f - x / (float)Math.Max(1, accessor.Width - 1)) * 0.45f;
                    row[x] = new Rgba32(glowPixel.R, glowPixel.G, glowPixel.B, (byte)Math.Clamp(a * 34f * directional * modeBoost, 0, 54));
                }
            }
        });
        wrap.Mutate(x => x.GaussianBlur(3.2f));
        ctx.DrawImage(wrap, new Point(position.X - 3, position.Y - 1), 0.46f);
    }


    private static void FillSoftEllipse(IImageProcessingContext ctx, PointF center, float radiusX, float radiusY, Color color, float maxAlpha, int rings)
    {
        for (var i = rings; i >= 1; i--)
        {
            var t = i / (float)rings;
            var alpha = maxAlpha * MathF.Pow(1f - t * 0.72f, 1.35f);
            ctx.Fill(color.WithAlpha(Math.Clamp(alpha, 0f, maxAlpha)), new EllipsePolygon(center.X, center.Y, radiusX * t, radiusY * t));
        }
    }

    private static void DrawDirectionalLight(IImageProcessingContext ctx, RectangleF rect, string category, bool hero)
    {
        var rimColor = ResolveGlow(category);
        FillSoftEllipse(ctx, new PointF(rect.Left + rect.Width * 0.44f, rect.Top + rect.Height * 0.40f), rect.Width * 0.72f, rect.Height * 0.56f, rimColor, hero ? 0.030f : 0.010f, 7);
        FillSoftEllipse(ctx, new PointF(rect.Left + rect.Width * 0.24f, rect.Top + rect.Height * 0.22f), rect.Width * 0.38f, rect.Height * 0.28f, Color.FromRgb(220, 232, 255), hero ? 0.010f : 0.004f, 5);
    }

    private static void DrawOrganicDepthHaze(IImageProcessingContext ctx, RectangleF rect, int depth, bool hero)
    {
        var hazeAlpha = hero ? 0.010f : 0.052f + Math.Clamp(depth, 1, 3) * 0.026f;
        var center = new PointF(rect.Left + rect.Width * 0.48f, rect.Top + rect.Height * 0.52f);
        FillSoftEllipse(ctx, center, rect.Width * 0.68f, rect.Height * 0.58f, Color.FromRgb(74, 102, 162), hazeAlpha * 0.42f, 5);
    }

    private static void DrawSupportAtmosphericShadow(IImageProcessingContext ctx, RectangleF rect, int depth)
    {
        var alpha = 0.026f + Math.Clamp(depth, 1, 3) * 0.012f;
        var center = new PointF(rect.Left + rect.Width * 0.54f, rect.Top + rect.Height * 0.59f);
        FillSoftEllipse(ctx, center, rect.Width * 0.78f, rect.Height * 0.54f, Color.Black, alpha, 6);
    }

    private static void DrawSupportAtmosphericFade(IImageProcessingContext ctx, RectangleF rect, int depth)
    {
        var alpha = 0.022f + Math.Clamp(depth, 1, 3) * 0.018f;
        var center = new PointF(rect.Left + rect.Width * 0.50f, rect.Top + rect.Height * 0.50f);
        FillSoftEllipse(ctx, center, rect.Width * 0.86f, rect.Height * 0.72f, Color.FromRgb(56, 76, 118), alpha, 6);
    }

    private static async Task DrawDeepSpaceBackgroundLayerAsync(Image<Rgba32> canvas, CelestialAsset asset, int width, int height, Random random, CancellationToken cancellationToken)
    {
        using var layer = await Image.LoadAsync<Rgba32>(asset.LocalPath, cancellationToken);
        if (IsTransparentAsset(asset.LocalPath))
            CleanDeepSpaceAlpha(layer);
        layer.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Crop }).GaussianBlur(2.6f).Brightness(0.58f).Saturate(0.86f));
        var opacity = 0.08f + random.NextSingle() * 0.10f;
        canvas.Mutate(ctx => ctx.DrawImage(layer, new Point(0, 0), opacity));
    }

    private static void CleanDeepSpaceAlpha(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    var max = Math.Max(p.R, Math.Max(p.G, p.B));
                    var avg = (p.R + p.G + p.B) / 3;
                    if (p.A < 10 || (max < 30 && p.A < 235))
                    {
                        p.A = 0;
                        row[x] = p;
                        continue;
                    }

                    if (avg < 42)
                        p.A = (byte)Math.Clamp(p.A * 0.34f, 0, 255);
                    else if (p.A is > 0 and < 90)
                        p.A = (byte)Math.Clamp(p.A * 0.55f, 0, 255);
                    else if (p.A is > 90 and < 210)
                        p.A = (byte)Math.Clamp((p.A + 210) / 2, 0, 255);
                    row[x] = p;
                }
            }
        });
        image.Mutate(ctx => ctx.GaussianBlur(0.35f));
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

    private static void ValidateThumbnailOutput(string outputPath, bool isShortForm)
    {
        var expectedName = isShortForm ? "thumbnail-short.jpg" : "thumbnail-long.jpg";
        if (!File.Exists(outputPath))
            throw new InvalidOperationException($"Thumbnail stage failed: {expectedName} was not generated at {outputPath}.");

        if (new FileInfo(outputPath).Length <= 0)
            throw new InvalidOperationException($"Thumbnail stage failed: {expectedName} is empty at {outputPath}.");
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
        var directory = ResolveCelestialObjectDirectory(key);
        Directory.CreateDirectory(directory);
        var path = _assetPathResolver.ResolveCelestialAssetPath(key, "hero-fallback.jpg");
        if (!File.Exists(path))
            await ProceduralCelestialFallback.CreateAsync(path, name, key, cancellationToken);
        return new CelestialAsset { ObjectName = name, ObjectType = key, Category = key, LocalPath = path, Source = "GeneratedFallback", Title = name, FallbackUsed = true, BaseDirectory = _assetPathResolver.BaseDirectory };
    }

    private ResolvedAsset? FindAsset(string key)
    {
        var directory = ResolveCelestialObjectDirectory(key);
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
                var path = _assetPathResolver.ResolveCelestialAssetPath(key, name);
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
        var hero = _assetPathResolver.ResolveCelestialAssetPath("milky-way", "hero.png");
        if (File.Exists(hero))
            return hero;
        return FindAsset("milky-way")?.Path;
    }

    private static bool IsTransparentAsset(string path)
        => Path.GetFileName(path).Equals("hero-transparent.png", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> BuildLayoutWarnings(bool portrait, double heroScale, IReadOnlyCollection<double> supportScales)
    {
        if (portrait && (heroScale < 0.40d || heroScale > 0.45d))
            yield return "portrait-hero-scale-outside-target";
        if (!portrait && (heroScale < 0.32d || heroScale > 0.45d))
            yield return "landscape-hero-scale-outside-target";
        if (supportScales.Any(s => s < 0.12d || s > 0.22d))
            yield return "support-scale-outside-target";
        if (portrait && supportScales.Count > 1)
            yield return "portrait-support-count-exceeds-cap";
        if (!portrait && supportScales.Count > 2)
            yield return "landscape-support-count-exceeds-cap";
    }

    private bool HasAsset(string key) => FindAsset(key) is not null;

    private string ResolveCelestialObjectDirectory(string key) => Path.GetDirectoryName(_assetPathResolver.ResolveCelestialAssetPath(key, "placeholder.txt")) ?? _assetPathResolver.GetCelestialRoot();

    private static bool IsImage(string path) => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyJpgAsset(string path) => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static bool IsAssetPackFileName(string fileName) => fileName.Equals("hero-transparent.png", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("hero.png", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("cinematic.png", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("closeup.png", StringComparison.OrdinalIgnoreCase);

    private ResolvedAsset CreateResolvedAsset(string path, string key, bool oldAssetIgnoredBecauseHeroExists)
    {
        var fileName = Path.GetFileName(path);
        var source = IsAssetPackFileName(fileName)
            ? "AssetPack"
            : IsLegacyJpgAsset(path) ? "LegacyJpgAsset" : "LocalCuratedAsset";
        return new ResolvedAsset(path, fileName, source, oldAssetIgnoredBecauseHeroExists, _assetPathResolver.BaseDirectory);
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
        FallbackUsed = fallback,
        BaseDirectory = resolved.BaseDirectory
    };

    private static SelectedObject? SelectHeroObject(IReadOnlyCollection<HeroObjectScore> scores, bool allowDeepSpaceHero)
    {
        var preferredPlanet = scores
            .Where(s => !DeepSpaceKeys.Contains(s.Object.Key))
            .Where(s => PlanetHeroPriority.Contains(s.Object.Key, StringComparer.OrdinalIgnoreCase))
            .OrderBy(s => PlanetPriorityRank(s.Object.Key))
            .ThenByDescending(s => s.TotalScore)
            .FirstOrDefault();

        var top = scores.FirstOrDefault();
        if (preferredPlanet is not null && top is not null && DeepSpaceKeys.Contains(top.Object.Key) && !allowDeepSpaceHero)
            return preferredPlanet.Object;

        return top?.Object;
    }

    private static int PlanetPriorityRank(string key)
    {
        var index = Array.FindIndex(PlanetHeroPriority, p => p.Equals(key, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 99 : index;
    }

    private static HeroObjectScore ScoreHeroCandidate(SelectedObject obj, ThumbnailGenerationRequest request, IReadOnlySet<string> duplicatedKeys, bool conjunction, bool allowDeepSpaceHero, double configuredDeepSpacePenalty)
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
        var planetPreferenceBoost = obj.Key switch { "jupiter" => 0.70d, "venus" => 0.62d, "saturn" => 0.58d, "moon" => 0.36d, "mars" => 0.34d, _ => 0d };
        var deepSpacePenalty = DeepSpaceKeys.Contains(obj.Key) && !allowDeepSpaceHero ? -Math.Abs(configuredDeepSpacePenalty) : 0d;
        var total = visibilityWeight + brightnessWeight + rarityWeight + astronomyEventWeight + moonPenalty + duplicationPenalty + planetPreferenceBoost + deepSpacePenalty;
        return new HeroObjectScore(obj, Math.Round(total, 3), Math.Round(visibilityWeight, 3), Math.Round(brightnessWeight, 3), Math.Round(rarityWeight, 3), Math.Round(astronomyEventWeight, 3), moonPenalty, duplicationPenalty, Math.Round(deepSpacePenalty, 3));
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

    private static bool IsDeepSpaceHeroAllowed(ThumbnailGenerationRequest request)
    {
        var text = $"{request.Context.SpecialEvent?.EventType} {request.Context.SpecialEvent?.EventTitle} {request.ContentType} {string.Join(' ', request.Context.Events.Select(e => e.Category + ' ' + e.ObjectName + ' ' + e.Details))}";
        if (text.Contains("eclipse", StringComparison.OrdinalIgnoreCase) || text.Contains("meteor", StringComparison.OrdinalIgnoreCase))
            return true;
        return text.Contains("deep-space", StringComparison.OrdinalIgnoreCase)
            || text.Contains("deep space", StringComparison.OrdinalIgnoreCase)
            || text.Contains("deep sky", StringComparison.OrdinalIgnoreCase)
            || text.Contains("milky way", StringComparison.OrdinalIgnoreCase)
            || text.Contains("andromeda", StringComparison.OrdinalIgnoreCase)
            || text.Contains("galaxy", StringComparison.OrdinalIgnoreCase)
            || text.Contains("nebula", StringComparison.OrdinalIgnoreCase);
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


    private static void DrawEnvironmentalDepthTexture(IImageProcessingContext ctx, int width, int height, bool portrait, string mode, Random random)
    {
        var dustCount = portrait ? 34 : 46;
        var baseAlpha = mode is "DeepSpaceMode" or "WideSkyMode" ? 0.020f : 0.014f;
        for (var i = 0; i < dustCount; i++)
        {
            var x = width * (0.08f + random.NextSingle() * 0.84f);
            var y = height * (0.08f + random.NextSingle() * 0.76f);
            var rx = width * (0.030f + random.NextSingle() * 0.105f);
            var ry = height * (0.010f + random.NextSingle() * 0.052f);
            var color = i % 5 == 0 ? Color.FromRgb(126, 86, 166) : i % 3 == 0 ? Color.FromRgb(92, 126, 176) : Color.FromRgb(72, 92, 134);
            ctx.Fill(color.WithAlpha(baseAlpha * (0.34f + random.NextSingle() * 0.86f)), new EllipsePolygon(x, y, rx, ry));
        }

        var clusters = portrait ? 3 : 4;
        for (var c = 0; c < clusters; c++)
        {
            var centerX = width * (0.18f + random.NextSingle() * 0.70f);
            var centerY = height * (0.16f + random.NextSingle() * 0.64f);
            var stars = 14 + random.Next(20);
            for (var i = 0; i < stars; i++)
            {
                var x = centerX + NextGaussian(random) * width * 0.045f;
                var y = centerY + NextGaussian(random) * height * 0.035f;
                if (x < 0 || y < 0 || x >= width || y >= height) continue;
                var radius = 0.22f + random.NextSingle() * 0.42f;
                ctx.Fill(Color.FromRgb(205, 224, 255).WithAlpha(0.045f + random.NextSingle() * 0.075f), new EllipsePolygon(x, y, radius));
            }
        }
    }

    private static void DrawStars(IImageProcessingContext ctx, int width, int height, string seedText)
    {
        var random = new Random(StableHash(seedText));
        var stars = Math.Clamp(width * height / 11800, 95, 315);
        var clusterA = new PointF(width * (0.22f + random.NextSingle() * 0.12f), height * (0.20f + random.NextSingle() * 0.22f));
        var clusterB = new PointF(width * (0.66f + random.NextSingle() * 0.14f), height * (0.58f + random.NextSingle() * 0.16f));
        for (var i = 0; i < stars; i++)
        {
            var clustered = random.NextSingle() < 0.38f;
            var center = random.NextSingle() < 0.56f ? clusterA : clusterB;
            var x = clustered ? center.X + NextGaussian(random) * width * 0.16f : random.NextSingle() * width;
            var y = clustered ? center.Y + NextGaussian(random) * height * 0.14f : random.NextSingle() * height;
            if (x < 0 || x >= width || y < 0 || y >= height)
                continue;

            var rareBright = random.NextSingle() > 0.92f;
            var radius = rareBright ? 1.55f + random.NextSingle() * 1.15f : 0.42f + random.NextSingle() * 1.08f;
            var twinkle = 0.72f + random.NextSingle() * 0.42f;
            var alpha = Math.Clamp((rareBright ? 0.42f : 0.12f + random.NextSingle() * 0.22f) * twinkle, 0.06f, 0.56f);
            var starColor = random.Next(5) switch
            {
                0 => Color.FromRgb(190, 214, 255),
                1 => Color.FromRgb(255, 227, 188),
                2 => Color.FromRgb(220, 235, 255),
                _ => Color.White
            };
            ctx.Fill(starColor.WithAlpha(alpha), new EllipsePolygon(x, y, radius));
            if (rareBright)
                ctx.Fill(Color.FromRgb(160, 190, 255).WithAlpha(0.045f), new EllipsePolygon(x, y, radius * 2.8f));
        }
    }

    private static void DrawDateLocation(IImageProcessingContext ctx, ThumbnailGenerationRequest request, int width, int height, bool portrait)
    {
        var text = $"{request.Context.Date:MMM d} • {request.Context.LocationName}".Trim(' ', '•');
        if (string.IsNullOrWhiteSpace(text)) return;
        var font = CreateFont(portrait ? 32 : 26, FontStyle.Regular, false);
        ctx.DrawText(text, font, Color.White.WithAlpha(0.72f), new PointF(portrait ? 46 : 50, portrait ? 56 : 42));
    }

    private async Task RenderHookTextAsync(string thumbnailPath, string hook, RectangleF bounds, bool portrait, string language, IReadOnlyCollection<CelestialAsset> selectedAssets, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hook)) return;

        var text = FormatHookForMobile(hook);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Thumbnail text is empty after formatting.");

        var fontSize = ResolveFontSize(text, portrait);
        var selection = ResolveThumbnailFont(language, text);
        var thumbnailDirectory = Path.GetDirectoryName(thumbnailPath) ?? Path.GetTempPath();
        var textFilePath = Path.Combine(thumbnailDirectory, selection.Language == "hi" ? "thumbnail-title-hi.txt" : "thumbnail-title-en.txt");
        var renderedPath = Path.Combine(thumbnailDirectory, $"temp-thumbnail-rendered-{Guid.NewGuid():N}.jpg");
        var thumbnailType = portrait ? "Short" : "Long";
        await WriteThumbnailRuntimeAssetsReportAsync(thumbnailDirectory, language, selection, selectedAssets, cancellationToken);

        if (!selection.FontExists)
        {
            await WriteThumbnailFontReportAsync(thumbnailDirectory, language, text, selection, textFilePath, string.Empty, string.Empty, null, string.Empty, cancellationToken);
            throw new FileNotFoundException($"Thumbnail font missing from executable assets folder: {selection.FontPath}", selection.FontPath);
        }

        await File.WriteAllTextAsync(textFilePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        await ValidateThumbnailTextFileAsync(textFilePath, text, cancellationToken);

        var drawtext = BuildDrawTextFilter(selection.FontPath, textFilePath, fontSize, bounds, portrait);
        ValidateDrawTextFilter(drawtext, text, fontSize, bounds);

        var arguments = string.Join(' ',
            "-y",
            $"-i {QuoteFfmpegPath(thumbnailPath)}",
            $"-vf \"{drawtext}\"",
            "-frames:v 1",
            "-q:v 2",
            QuoteFfmpegPath(renderedPath));
        var ffmpegCommand = $"{_renderingOptions.FfmpegPath} {arguments}";
        await File.WriteAllTextAsync(Path.Combine(thumbnailDirectory, "thumbnail-text-debug-command.txt"), ffmpegCommand, cancellationToken);
        await WriteThumbnailFontReportAsync(thumbnailDirectory, language, text, selection, textFilePath, drawtext, ffmpegCommand, null, string.Empty, cancellationToken);

        try
        {
            _logger.LogInformation(
                "Thumbnail text rendering: Language={Language}; Font={Font}; TextFile={TextFile}; ThumbnailType={ThumbnailType}",
                selection.Language,
                Path.GetFileName(selection.FontPath),
                textFilePath,
                thumbnailType);

            var textAreaBefore = await CalculateTextAreaSignatureAsync(thumbnailPath, bounds, cancellationToken);
            var result = await _processRunner.ExecuteAsync(_renderingOptions.FfmpegPath, arguments, cancellationToken, TimeSpan.FromSeconds(60));
            var stderr = string.Join(Environment.NewLine, new[] { result.StandardError, result.ExceptionText }.Where(value => !string.IsNullOrWhiteSpace(value)));
            await WriteThumbnailFontReportAsync(thumbnailDirectory, language, text, selection, textFilePath, drawtext, ffmpegCommand, result.ExitCode, stderr, cancellationToken);
            await WriteThumbnailRuntimeAssetsReportAsync(thumbnailDirectory, language, selection, selectedAssets, cancellationToken);

            if (result.ExitCode != 0 || !File.Exists(renderedPath))
            {
                var message = $"Thumbnail text rendering failed for {thumbnailType} thumbnail. ExitCode={result.ExitCode}; Error={result.StandardError}; Exception={result.ExceptionText}";
                _logger.LogWarning("{Message}", message);
                throw new InvalidOperationException(message);
            }

            var textAreaAfter = await CalculateTextAreaSignatureAsync(renderedPath, bounds, cancellationToken);
            if (textAreaBefore == textAreaAfter)
            {
                _logger.LogWarning(
                    "Thumbnail text render validation warning: selected hook is non-empty but final {ThumbnailType} thumbnail text area appears unchanged. Language={Language}; Hook={SelectedHook}; Font={FontPath}; TextFile={TextFilePath}",
                    thumbnailType,
                    selection.Language,
                    hook,
                    selection.FontPath,
                    textFilePath);
            }

            File.Move(renderedPath, thumbnailPath, overwrite: true);
        }
        finally
        {
            TryDelete(renderedPath);
        }
    }

    private ThumbnailFontSelection ResolveThumbnailFont(string language, string text)
    {
        var containsDevanagari = ContainsDevanagari(text);
        var selectedLanguage = LocalizationResolver.IsHindi(language) || containsDevanagari ? "hi" : "en";
        var preferredFont = selectedLanguage == "hi" ? _fontOptions.HindiFont : _fontOptions.DefaultEnglishFont;
        var resolvedFont = _assetPathResolver.ResolveFontPath(preferredFont);
        return new ThumbnailFontSelection(selectedLanguage, preferredFont, resolvedFont, IsReadableFile(resolvedFont), containsDevanagari);
    }

    public static string? ResolveFontPath(string fontPath)
    {
        if (string.IsNullOrWhiteSpace(fontPath)) return null;
        var resolvedPath = new RuntimeAssetPathResolver().ResolveFontPath(fontPath);
        return File.Exists(resolvedPath) ? resolvedPath : null;
    }

    private static bool IsReadableFile(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.CanRead;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsThumbnailTextRenderException(Exception exception)
        => exception is FileNotFoundException fileNotFoundException
            && fileNotFoundException.Message.StartsWith("Thumbnail font missing from executable assets folder:", StringComparison.Ordinal);

    private static bool IsThumbnailTextRenderingFailure(Exception exception)
        => exception is InvalidOperationException invalidOperationException
            && invalidOperationException.Message.StartsWith("Thumbnail text rendering failed", StringComparison.Ordinal);

    private static bool IsThumbnailOutputValidationFailure(Exception exception)
        => exception is InvalidOperationException invalidOperationException
            && invalidOperationException.Message.StartsWith("Thumbnail stage failed:", StringComparison.Ordinal);

    private static async Task<int> CalculateTextAreaSignatureAsync(string imagePath, RectangleF bounds, CancellationToken cancellationToken)
    {
        if (!File.Exists(imagePath)) return 0;

        using var image = await Image.LoadAsync<Rgba32>(imagePath, cancellationToken);
        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, Math.Max(0, image.Width - 1));
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, Math.Max(0, image.Height - 1));
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left + 1, image.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top + 1, image.Height);
        var stepX = Math.Max(1, (right - left) / 64);
        var stepY = Math.Max(1, (bottom - top) / 32);

        unchecked
        {
            var hash = 17;
            for (var y = top; y < bottom; y += stepY)
            {
                var row = image.DangerousGetPixelRowMemory(y).Span;
                for (var x = left; x < right; x += stepX)
                {
                    var pixel = row[x];
                    hash = (hash * 31) + pixel.R;
                    hash = (hash * 31) + pixel.G;
                    hash = (hash * 31) + pixel.B;
                    hash = (hash * 31) + pixel.A;
                }
            }

            return hash;
        }
    }

    private static async Task ValidateThumbnailTextFileAsync(string textFilePath, string expectedText, CancellationToken cancellationToken)
    {
        if (!File.Exists(textFilePath))
            throw new InvalidOperationException($"Thumbnail text file was not created: {textFilePath}");

        var info = new FileInfo(textFilePath);
        if (info.Length <= 0)
            throw new InvalidOperationException($"Thumbnail text file is empty: {textFilePath}");

        var actualText = await File.ReadAllTextAsync(textFilePath, Encoding.UTF8, cancellationToken);
        if (!string.Equals(actualText, expectedText, StringComparison.Ordinal))
            throw new InvalidOperationException("Thumbnail text file content does not match the selected hook text.");
    }

    private static void ValidateDrawTextFilter(string drawtext, string text, float fontSize, RectangleF bounds)
    {
        if (!drawtext.Contains("drawtext=", StringComparison.Ordinal))
            throw new InvalidOperationException("FFmpeg thumbnail filter chain is missing drawtext.");
        if (!drawtext.Contains("fontcolor=white@", StringComparison.Ordinal))
            throw new InvalidOperationException("FFmpeg thumbnail drawtext font color is not visible.");
        if (fontSize <= 0)
            throw new InvalidOperationException("FFmpeg thumbnail drawtext fontsize must be greater than zero.");
        if (bounds.X < 0 || bounds.Y < 0 || bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("FFmpeg thumbnail drawtext coordinates are invalid.");
        if (drawtext.Contains("white@0", StringComparison.Ordinal))
            throw new InvalidOperationException("FFmpeg thumbnail drawtext opacity is not visible.");
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("FFmpeg thumbnail drawtext text is empty.");
    }

    private static async Task WriteThumbnailFontReportAsync(
        string thumbnailDirectory,
        string language,
        string selectedHook,
        ThumbnailFontSelection selection,
        string textFilePath,
        string drawTextFilter,
        string ffmpegCommand,
        int? ffmpegExitCode,
        string ffmpegStdErr,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(thumbnailDirectory);
        var reportPath = Path.Combine(thumbnailDirectory, "thumbnail-font-report.json");
        var textFileExists = File.Exists(textFilePath);
        var payload = new
        {
            language = selection.Language,
            requestLanguage = language,
            selectedHook,
            selectedFontConfigPath = selection.ConfigPath,
            selectedFontResolvedPath = selection.FontPath,
            selectedFontEscapedPath = FfmpegPathEscaper.ToDrawTextPath(selection.FontPath),
            selectedFontPath = selection.FontPath,
            containsDevanagari = selection.ContainsDevanagari,
            fontExists = selection.FontExists,
            textFilePath,
            textFileEscapedPath = FfmpegPathEscaper.ToDrawTextPath(textFilePath),
            textFileExists,
            textFileSizeBytes = textFileExists ? new FileInfo(textFilePath).Length : 0,
            textFileEncoding = "UTF-8",
            drawTextMode = "textfile",
            drawTextFilter,
            ffmpegCommand,
            ffmpegExitCode,
            ffmpegStdErr = ffmpegStdErr ?? string.Empty
        };

        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private async Task WriteThumbnailRuntimeAssetsReportAsync(
        string thumbnailDirectory,
        string requestLanguage,
        ThumbnailFontSelection selection,
        IReadOnlyCollection<CelestialAsset> selectedAssets,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(thumbnailDirectory);
        var selectedAssetPaths = selectedAssets.Select(asset => asset.LocalPath).ToArray();
        var missingAssets = selectedAssetPaths.Where(path => !File.Exists(path)).ToArray();
        var payload = new
        {
            appBaseDirectory = _assetPathResolver.BaseDirectory,
            assetsRoot = _assetPathResolver.GetAssetsRoot(),
            fontsRoot = _assetPathResolver.GetFontsRoot(),
            celestialRoot = _assetPathResolver.GetCelestialRoot(),
            requestLanguage,
            selectedFontLanguage = selection.Language,
            selectedFontConfigPath = selection.ConfigPath,
            selectedFontResolvedPath = selection.FontPath,
            selectedFontExists = selection.FontExists,
            selectedAssetPaths,
            missingAssets,
            fallbackUsed = selectedAssets.Any(asset => asset.FallbackUsed)
        };

        await File.WriteAllTextAsync(Path.Combine(thumbnailDirectory, "thumbnail-runtime-assets-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    public static bool IsHindiThumbnailText(string language, string text)
        => LocalizationResolver.IsHindi(language) || ContainsDevanagari(text);

    public static bool ContainsDevanagari(string text)
        => text.Any(ch => ch is >= '\u0900' and <= '\u097F');

    public static string BuildDrawTextFilter(string fontPath, string textFilePath, float fontSize, RectangleF bounds, bool portrait)
    {
        var alpha = portrait ? "0.92" : "0.97";
        var x = portrait ? "(w-text_w)/2" : FormatInvariant(bounds.X);
        var y = FormatInvariant(bounds.Y);
        return string.Join(':',
            "drawtext=" + $"fontfile='{FfmpegPathEscaper.ToDrawTextPath(fontPath)}'",
            $"textfile='{FfmpegPathEscaper.ToDrawTextPath(textFilePath)}'",
            $"fontsize={FormatInvariant(fontSize)}",
            $"fontcolor=white@{alpha}",
            "shadowcolor=black@0.82",
            "shadowx=4",
            "shadowy=4",
            $"x={x}",
            $"y={y}",
            $"line_spacing={FormatInvariant(fontSize * -0.16f)}");
    }

    private static string FormatInvariant(float value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string QuoteFfmpegPath(string path)
    {
        if (path.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new ArgumentException("Path contains control characters.", nameof(path));
        return $"\"{path.Replace('\\', '/').Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort cleanup for thumbnail text render scratch files.
        }
    }

    private void DrawBrand(IImageProcessingContext ctx, int width, int height, bool portrait)
    {
        if (!_options.EnableBranding || string.IsNullOrWhiteSpace(_options.BrandText)) return;
        var font = CreateFont(portrait ? 34 : 30, FontStyle.Bold, false);
        ctx.DrawText(new RichTextOptions(font) { Origin = new PointF(width - 40, height - (portrait ? 66 : 34)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom }, _options.BrandText, Color.White.WithAlpha(0.62f));
    }

    private static void DrawVignette(IImageProcessingContext ctx, int width, int height)
    {
        ctx.Fill(new LinearGradientBrush(new PointF(0, 0), new PointF(0, height * 0.22f), GradientRepetitionMode.None,
            new ColorStop(0, Color.Black.WithAlpha(0.22f)),
            new ColorStop(1, Color.Transparent)), new RectangleF(0, 0, width, height * 0.24f));
        ctx.Fill(new LinearGradientBrush(new PointF(0, height), new PointF(0, height * 0.72f), GradientRepetitionMode.None,
            new ColorStop(0, Color.Black.WithAlpha(0.28f)),
            new ColorStop(1, Color.Transparent)), new RectangleF(0, height * 0.70f, width, height * 0.30f));
        FillSoftEllipse(ctx, new PointF(width * 0.50f, height * 0.48f), width * 0.62f, height * 0.52f, Color.Black, 0.045f, 7);
    }

    private static Color ResolveGlow(string key) => key switch
    {
        "jupiter" or "saturn" or "venus" => Color.FromRgb(255, 205, 116),
        "mars" or "lunar-eclipse" => Color.FromRgb(255, 118, 82),
        "moon" or "solar-eclipse" => Color.FromRgb(245, 242, 220),
        "uranus" or "neptune" => Color.FromRgb(100, 175, 255),
        _ => Color.FromRgb(125, 155, 255)
    };


    private static float GlowAlpha(string key, bool hero, string cinematicMode)
        => hero ? ResolveHeroGlowAlpha(key, cinematicMode) : DeepSpaceKeys.Contains(key) ? 0.035f : 0.056f;

    private static float GlowRadiusScale(string key, bool hero)
    {
        if (key.Equals("meteor-shower", StringComparison.OrdinalIgnoreCase)) return 0f;
        if (DeepSpaceKeys.Contains(key)) return hero ? 0.24f : 0.18f;
        if (key.Equals("moon", StringComparison.OrdinalIgnoreCase)) return hero ? 0.35f : 0.22f;
        return hero ? 0.32f : 0.21f;
    }

    private static float ResolveHeroGlowAlpha(string key, string cinematicMode)
    {
        var baseAlpha = key switch
        {
            "moon" => 0.12f,
            "mars" => 0.14f,
            "jupiter" or "saturn" or "venus" => 0.13f,
            "neptune" or "uranus" => 0.12f,
            "meteor-shower" => 0.18f,
            _ when DeepSpaceKeys.Contains(key) => 0.05f,
            _ => 0.11f
        };
        return cinematicMode is "EclipseMode" or "MeteorShowerMode" ? Math.Min(0.20f, baseAlpha + 0.03f) : baseAlpha;
    }

    private static RectangleF ResolveHeroRect(int width, int height, bool portrait, string mode, Random random)
    {
        var jitterX = (random.NextSingle() - 0.5f) * width * (portrait ? 0.035f : 0.045f);
        var jitterY = (random.NextSingle() - 0.5f) * height * 0.035f;
        RectangleF rect = portrait
            ? new RectangleF(width * 0.30f + jitterX, height * 0.12f + jitterY, width * 0.405f, width * 0.405f)
            : mode switch
            {
                "MoonDominant" => new RectangleF(width * 0.54f + jitterX, height * 0.11f + jitterY, width * 0.37f, height * 0.61f),
                "WideSkyMode" or "DeepSpaceMode" => new RectangleF(width * 0.61f + jitterX, height * 0.16f + jitterY, width * 0.30f, height * 0.52f),
                _ => new RectangleF(width * 0.52f + jitterX, height * 0.14f + jitterY, width * 0.39f, height * 0.58f)
            };
        return ClampRect(rect, width, height, 24);
    }

    private static RectangleF ResolveSupportRect(int width, int height, bool portrait, string mode, int index, Random random)
    {
        var offset = (random.NextSingle() - 0.5f) * width * 0.035f;
        if (portrait)
            return ClampRect(new RectangleF(width * 0.70f + offset, height * 0.40f, width * 0.122f, width * 0.122f), width, height, 28);
        var size = mode is "ConjunctionMode" or "RareAlignment" ? width * 0.142f : width * 0.116f;
        var x = index == 0 ? width * 0.38f : width * 0.45f;
        var y = index == 0 ? height * 0.14f : height * 0.59f;
        return ClampRect(new RectangleF(x + offset, y, size, size), width, height, 28);
    }

    private static RectangleF CalculateTextSafeRect(string hook, int width, int height, bool portrait, string language)
    {
        var margin = portrait ? 82f : 64f;
        var text = FormatHookForMobile(hook);
        var fontSize = ResolveFontSize(text, portrait);
        var lineCount = Math.Max(1, text.Split('\n').Length);
        var boxHeight = Math.Min(height * (portrait ? 0.22f : 0.26f), fontSize * lineCount * 1.08f + 24f);
        return portrait
            ? new RectangleF(margin, height * 0.625f, width - margin * 2, boxHeight)
            : new RectangleF(margin, height * 0.62f, width * 0.43f, boxHeight);
    }

    private static RectangleF CalculateDateLocationRect(int width, int height, bool portrait)
        => new(portrait ? 38 : 44, portrait ? 46 : 32, portrait ? width * 0.56f : width * 0.40f, portrait ? 48 : 40);

    private RectangleF CalculateBrandRect(int width, int height, bool portrait)
        => !_options.EnableBranding || string.IsNullOrWhiteSpace(_options.BrandText)
            ? new RectangleF(0, 0, 0, 0)
            : new RectangleF(width - (portrait ? 360 : 300), height - (portrait ? 96 : 64), portrait ? 320 : 260, portrait ? 58 : 48);

    private static RectangleF InflateRect(RectangleF rect, float amount)
        => new(rect.X - amount, rect.Y - amount, rect.Width + amount * 2, rect.Height + amount * 2);

    private static float ResolveFontSize(string text, bool portrait)
    {
        var length = text.Replace("\n", string.Empty).Length;
        var shortBase = portrait ? 101f : 104f;
        var longBase = portrait ? 77f : 78f;
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
        var haze = mode switch { "DeepSpaceMode" => 0.030f, "EclipseMode" => 0.026f, _ => 0.020f };
        var wisps = portrait ? 30 : 38;
        for (var i = 0; i < wisps; i++)
        {
            var x = width * (i / (float)wisps) + (random.NextSingle() - 0.5f) * width * 0.18f;
            var y = height * (0.16f + random.NextSingle() * 0.56f) + MathF.Sin(i * 0.66f) * height * 0.08f;
            var w = width * (0.040f + random.NextSingle() * 0.080f);
            var h = height * (0.012f + random.NextSingle() * 0.028f);
            var color = mode is "DeepSpaceMode" or "WideSkyMode" && i % 3 == 0 ? Color.FromRgb(119, 74, 180) : Color.FromRgb(110, 132, 176);
            ctx.Fill(color.WithAlpha(haze * (0.22f + random.NextSingle() * 0.46f)), new EllipsePolygon(x, y, w, h));
        }
    }

    private static RectangleF AvoidCollision(RectangleF objectRect, RectangleF textRect, int width, int height, bool preferRight)
    {
        if (!Intersects(objectRect, textRect, 0f))
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

    private static double ScoreComposition(RectangleF heroRect, RectangleF textBox, IEnumerable<RectangleF> supports, int width, int height, IReadOnlyCollection<string> warnings, CelestialAsset hero, double foregroundObjectAreaPercent, bool oversizedGlowPenaltyApplied)
    {
        var thirdX = width * 2d / 3d;
        var heroCenter = heroRect.Left + heroRect.Width / 2d;
        var thirdScore = 1d - Math.Min(1d, Math.Abs(heroCenter - thirdX) / (width * 0.5d));
        var textObjectClearance = Intersects(heroRect, textBox, 0.02f) ? 0.55d : 1d;
        var supportList = supports.ToArray();
        var supportPenalty = supportList.Count(r => Intersects(r, heroRect, 0.02f)) * 0.10d;
        var warningPenalty = warnings.Count * 0.05d;
        var oversizedHeroPenalty = heroRect.Width / width > 0.45d ? 0.12d : 0d;
        var deepSpaceForegroundPenalty = DeepSpaceKeys.Contains(hero.Category) && foregroundObjectAreaPercent > 18d ? 0.12d : 0d;
        var focalHierarchyPenalty = supportList.Any(r => r.Width > heroRect.Width * 0.55f) ? 0.08d : 0d;
        var glowPenalty = oversizedGlowPenaltyApplied ? 0.05d : 0d;
        return Math.Round(Math.Clamp(0.52d + thirdScore * 0.30d + textObjectClearance * 0.18d - supportPenalty - warningPenalty - oversizedHeroPenalty - deepSpaceForegroundPenalty - focalHierarchyPenalty - glowPenalty, 0, 1), 3);
    }

    private static double ScoreCompositionBalance(RectangleF heroRect, RectangleF textBox, IEnumerable<RectangleF> supports, int width, int height, double foregroundObjectAreaPercent, bool overlapPenaltyApplied)
    {
        var focalZone = new RectangleF(width * 0.48f, height * 0.08f, width * 0.48f, height * 0.78f);
        var focalScore = Intersects(heroRect, focalZone, 0f) ? 1d : 0.72d;
        var scaleScore = heroRect.Width / width <= 0.45f ? 1d : 0.68d;
        var supportCount = supports.Count();
        var supportScore = supportCount <= 2 ? 1d : 0.62d;
        var textClearance = Intersects(heroRect, textBox, 0.03f) ? 0.62d : 1d;
        var areaScore = foregroundObjectAreaPercent <= 30d ? 1d : Math.Max(0.55d, 1d - (foregroundObjectAreaPercent - 30d) / 55d);
        var overlapScore = overlapPenaltyApplied ? 0.70d : 1d;
        return Math.Round((focalScore * 0.22d) + (scaleScore * 0.20d) + (supportScore * 0.16d) + (textClearance * 0.20d) + (areaScore * 0.12d) + (overlapScore * 0.10d), 3);
    }


    private static double ScoreDepthSeparation(RectangleF heroRect, IEnumerable<RectangleF> supports, int width, int height, double foregroundObjectAreaPercent, bool overlapPenaltyApplied)
    {
        var supportList = supports.ToArray();
        var scaleLadder = supportList.Length == 0 ? 0.88d : supportList.Average(r => r.Width <= heroRect.Width * 0.46f ? 1d : Math.Max(0.55d, 1d - ((r.Width / heroRect.Width) - 0.46d)));
        var verticalSeparation = supportList.Length == 0 ? 0.86d : supportList.Average(r => Math.Clamp(Math.Abs((r.Top + r.Height / 2d) - (heroRect.Top + heroRect.Height / 2d)) / (height * 0.38d), 0.58d, 1d));
        var areaScore = foregroundObjectAreaPercent <= 24d ? 1d : Math.Max(0.58d, 1d - (foregroundObjectAreaPercent - 24d) / 50d);
        var overlapScore = overlapPenaltyApplied ? 0.62d : 1d;
        return Math.Round((scaleLadder * 0.34d) + (verticalSeparation * 0.26d) + (areaScore * 0.22d) + (overlapScore * 0.18d), 3);
    }

    private static double ScoreAtmosphericBlend(string mode, int supportCount, double compositionBalanceScore, bool oversizedGlowPenaltyApplied)
    {
        var modeScore = mode is "DeepSpaceMode" or "WideSkyMode" or "EpicPlanetFocus" ? 0.96d : 0.90d;
        var supportScore = supportCount <= 2 ? 0.96d : 0.74d;
        var glowScore = oversizedGlowPenaltyApplied ? 0.78d : 1d;
        return Math.Round((modeScore * 0.30d) + (supportScore * 0.22d) + (compositionBalanceScore * 0.28d) + (glowScore * 0.20d), 3);
    }

    private static double ScoreNegativeSpace(RectangleF heroRect, RectangleF textBox, IEnumerable<RectangleF> supports, int width, int height, double foregroundObjectAreaPercent)
    {
        var occupied = foregroundObjectAreaPercent / 100d + (textBox.Width * textBox.Height) / (width * (double)height);
        var targetEmpty = 0.68d;
        var empty = Math.Clamp(1d - occupied, 0d, 1d);
        var emptyScore = 1d - Math.Min(1d, Math.Abs(empty - targetEmpty) / 0.34d);
        var rightEdgeBreathing = Math.Clamp((width - heroRect.Right) / (width * 0.08d), 0.45d, 1d);
        var supportBreathing = supports.Any() ? supports.Average(r => Intersects(r, textBox, 0.06f) ? 0.62d : 1d) : 0.92d;
        return Math.Round((emptyScore * 0.46d) + (rightEdgeBreathing * 0.22d) + (supportBreathing * 0.18d) + (Math.Min(empty / targetEmpty, 1d) * 0.14d), 3);
    }

    private static double ScoreHeroIsolation(RectangleF heroRect, RectangleF textBox, IEnumerable<RectangleF> supports, int width, int height, bool overlapPenaltyApplied)
    {
        var textClearance = Intersects(heroRect, textBox, 0.04f) ? 0.58d : 1d;
        var supportList = supports.ToArray();
        var supportClearance = supportList.Length == 0 ? 0.94d : supportList.Average(r => Intersects(heroRect, r, 0.10f) ? 0.56d : 1d);
        var dominance = supportList.Length == 0 ? 0.92d : supportList.Average(r => heroRect.Width >= r.Width * 2.15f ? 1d : 0.68d);
        var cropSafety = heroRect.Left >= width * 0.045f && heroRect.Right <= width * 0.955f && heroRect.Top >= height * 0.045f && heroRect.Bottom <= height * 0.90f ? 1d : 0.72d;
        var overlapScore = overlapPenaltyApplied ? 0.70d : 1d;
        return Math.Round((textClearance * 0.26d) + (supportClearance * 0.26d) + (dominance * 0.22d) + (cropSafety * 0.16d) + (overlapScore * 0.10d), 3);
    }

    private static double ScoreAtmosphereDepth(double depthScore, double environmentalDepthScore, double atmosphereContinuityScore, double fogSignal)
        => Math.Round(Math.Clamp(depthScore * 0.32d + environmentalDepthScore * 0.30d + atmosphereContinuityScore * 0.24d + fogSignal * 0.14d, 0, 1), 3);

    private static double ScoreFogBlend(double fogSignal, double readabilityScore, double heroIsolationScore)
        => Math.Round(Math.Clamp(0.44d + fogSignal * 0.22d + readabilityScore * 0.18d + heroIsolationScore * 0.16d, 0, 1), 3);

    private static double ScoreProceduralArtifactPenalty(double rectangularAtmosphereRisk, double configuredOpacity, double configuredContrast)
    {
        var opacityRisk = Math.Max(0d, configuredOpacity - 0.06d) * 2.8d;
        var contrastRisk = Math.Max(0d, configuredContrast - 0.35d) * 0.55d;
        return Math.Round(Math.Clamp(rectangularAtmosphereRisk * 0.55d + opacityRisk + contrastRisk, 0, 1), 3);
    }

    private static double ScoreCinematicSoftness(double cinematicSubtletyScore, double fogBlendScore, double edgeIntegrationScore, double proceduralArtifactPenalty)
        => Math.Round(Math.Clamp(cinematicSubtletyScore * 0.34d + fogBlendScore * 0.28d + edgeIntegrationScore * 0.26d + (1d - proceduralArtifactPenalty) * 0.12d, 0, 1), 3);

    private static double ScoreAtmosphericRealism(double atmosphereDepthScore, double fogBlendScore, double edgeIntegrationScore, double cinematicSoftnessScore, double proceduralArtifactPenalty, double readabilityScore)
        => Math.Round(Math.Clamp(atmosphereDepthScore * 0.26d + fogBlendScore * 0.20d + edgeIntegrationScore * 0.20d + cinematicSoftnessScore * 0.18d + (1d - proceduralArtifactPenalty) * 0.10d + readabilityScore * 0.06d, 0, 1), 3);

    private static double ScoreCinematicRealism(double depthScore, double atmosphericBlendScore, double negativeSpaceScore, double heroIsolationScore, double compositionBalanceScore, double readabilityScore, double cinematicSubtletyScore, double edgeIntegrationScore, double environmentalDepthScore, double supportObjectDepthScore, double compositingSeamPenalty)
        => Math.Round(Math.Clamp((depthScore * 0.15d) + (atmosphericBlendScore * 0.15d) + (negativeSpaceScore * 0.12d) + (heroIsolationScore * 0.13d) + (compositionBalanceScore * 0.09d) + (readabilityScore * 0.05d) + (cinematicSubtletyScore * 0.11d) + (edgeIntegrationScore * 0.08d) + (environmentalDepthScore * 0.07d) + (supportObjectDepthScore * 0.05d) - (compositingSeamPenalty * 0.10d), 0, 1), 3);

    private static double ScoreOrganicAtmosphere(double atmosphericBlendScore, double depthScore, string mode)
    {
        var modeBonus = mode is "DeepSpaceMode" or "WideSkyMode" ? 0.06d : 0.02d;
        return Math.Round(Math.Clamp(atmosphericBlendScore * 0.50d + depthScore * 0.32d + 0.12d + modeBonus, 0, 1), 3);
    }

    private static double ScoreNaturalLighting(RectangleF heroRect, int width, int height, bool oversizedGlowPenaltyApplied)
    {
        var centerX = (heroRect.Left + heroRect.Width / 2d) / width;
        var centerY = (heroRect.Top + heroRect.Height / 2d) / height;
        var offCenter = Math.Clamp(Math.Sqrt(Math.Pow(centerX - 0.5d, 2) + Math.Pow(centerY - 0.48d, 2)) / 0.34d, 0, 1);
        var glowPenalty = oversizedGlowPenaltyApplied ? 0.12d : 0d;
        return Math.Round(Math.Clamp(0.58d + offCenter * 0.24d - glowPenalty + 0.10d, 0, 1), 3);
    }

    private static double ScoreVisualArtifactPenalty(bool oversizedGlowPenaltyApplied, bool overlapPenaltyApplied, double foregroundObjectAreaPercent)
    {
        var crowdedPenalty = Math.Max(0, foregroundObjectAreaPercent - 24d) / 70d;
        return Math.Round(Math.Clamp((oversizedGlowPenaltyApplied ? 0.16d : 0.03d) + (overlapPenaltyApplied ? 0.12d : 0d) + crowdedPenalty, 0, 1), 3);
    }

    private static double ScoreCompositingVisibilityPenalty(RectangleF heroRect, int width, int height, double compositionBalanceScore, bool oversizedGlowPenaltyApplied, double rectangularAtmosphereRisk)
    {
        var centerX = (heroRect.Left + heroRect.Width / 2d) / width;
        var centerY = (heroRect.Top + heroRect.Height / 2d) / height;
        var centeredBias = Math.Max(0, 1d - Math.Sqrt(Math.Pow(centerX - 0.5d, 2) + Math.Pow(centerY - 0.5d, 2)) / 0.25d);
        var symmetryPenalty = Math.Max(0, 0.84d - compositionBalanceScore) * 0.55d;
        return Math.Round(Math.Clamp(centeredBias * 0.20d + symmetryPenalty + rectangularAtmosphereRisk * 0.34d + (oversizedGlowPenaltyApplied ? 0.12d : 0.02d), 0, 1), 3);
    }

    private static double ScoreEdgeIntegration(string category, double compositingVisibilityPenalty, double atmosphericBlendScore, double naturalLightingScore, bool oversizedGlowPenaltyApplied)
    {
        var planetBonus = PlanetKeys.Contains(category) || category.Equals("moon", StringComparison.OrdinalIgnoreCase) ? 0.08d : 0.03d;
        var glowPenalty = oversizedGlowPenaltyApplied ? 0.08d : 0d;
        return Math.Round(Math.Clamp(0.42d + atmosphericBlendScore * 0.24d + naturalLightingScore * 0.18d + (1d - compositingVisibilityPenalty) * 0.18d + planetBonus - glowPenalty, 0, 1), 3);
    }

    private static double ScoreCompositingSeamPenalty(double visualArtifactPenalty, double compositingVisibilityPenalty, double edgeIntegrationScore)
        => Math.Round(Math.Clamp(visualArtifactPenalty * 0.36d + compositingVisibilityPenalty * 0.42d + (1d - edgeIntegrationScore) * 0.22d, 0, 1), 3);

    private static double ScoreAtmosphereContinuity(double organicAtmosphereScore, double atmosphericBlendScore, double compositingSeamPenalty, double rectangularAtmosphereRisk)
        => Math.Round(Math.Clamp(organicAtmosphereScore * 0.44d + atmosphericBlendScore * 0.38d + (1d - compositingSeamPenalty) * 0.12d + (1d - rectangularAtmosphereRisk) * 0.06d, 0, 1), 3);

    private static double ScoreEnvironmentalDepth(string mode, int supportCount, double organicAtmosphereScore, double atmosphereContinuityScore)
    {
        var modeBoost = mode is "DeepSpaceMode" or "WideSkyMode" ? 0.09d : 0.05d;
        var supportBoost = supportCount > 0 ? 0.04d : 0.02d;
        return Math.Round(Math.Clamp(organicAtmosphereScore * 0.40d + atmosphereContinuityScore * 0.43d + modeBoost + supportBoost, 0, 1), 3);
    }

    private static double ScoreSupportObjectDepth(IReadOnlyCollection<double> supportScales, double depthScore, double atmosphericBlendScore, int supportCount)
    {
        if (supportCount == 0)
            return Math.Round(Math.Clamp(depthScore * 0.54d + atmosphericBlendScore * 0.34d + 0.08d, 0, 1), 3);
        var scaleScore = supportScales.Count == 0 ? 0.76d : supportScales.Average(s => s <= 0.18d ? 1d : Math.Max(0.58d, 1d - (s - 0.18d) * 4.5d));
        return Math.Round(Math.Clamp(depthScore * 0.42d + atmosphericBlendScore * 0.32d + scaleScore * 0.26d, 0, 1), 3);
    }

    private static double ScoreCinematicSubtlety(double organicAtmosphereScore, double naturalLightingScore, double visualArtifactPenalty, double compositingVisibilityPenalty, double readabilityScore, double edgeIntegrationScore, double atmosphereContinuityScore)
        => Math.Round(Math.Clamp(organicAtmosphereScore * 0.24d + naturalLightingScore * 0.24d + edgeIntegrationScore * 0.16d + atmosphereContinuityScore * 0.14d + (1d - visualArtifactPenalty) * 0.12d + (1d - compositingVisibilityPenalty) * 0.07d + readabilityScore * 0.03d, 0, 1), 3);

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

    private static float NextGaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
    }


    private static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / Math.Max(0.0001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float ValueNoise01(float x, float y, int seed)
    {
        var xi = (int)MathF.Floor(x);
        var yi = (int)MathF.Floor(y);
        var tx = x - xi;
        var ty = y - yi;
        var a = HashNoise01(xi, yi, seed);
        var b = HashNoise01(xi + 1, yi, seed);
        var c = HashNoise01(xi, yi + 1, seed);
        var d = HashNoise01(xi + 1, yi + 1, seed);
        var sx = SmoothStep(0, 1, tx);
        var sy = SmoothStep(0, 1, ty);
        return Lerp(Lerp(a, b, sx), Lerp(c, d, sx), sy);
    }

    private static float HashNoise01(int x, int y, int seed)
    {
        unchecked
        {
            var n = seed;
            n = (n * 397) ^ x;
            n = (n * 397) ^ (y * 668265263);
            n = (n ^ (n >> 13)) * 1274126177;
            return ((n ^ (n >> 16)) & 0x7fffffff) / (float)int.MaxValue;
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

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

    private sealed record ThumbnailFontSelection(string Language, string ConfigPath, string FontPath, bool FontExists, bool ContainsDevanagari);

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
            compositionBalanceScore = composition.CompositionBalanceScore,
            depthScore = composition.DepthScore,
            atmosphericBlendScore = composition.AtmosphericBlendScore,
            negativeSpaceScore = composition.NegativeSpaceScore,
            heroIsolationScore = composition.HeroIsolationScore,
            cinematicRealismScore = composition.CinematicRealismScore,
            organicAtmosphereScore = composition.OrganicAtmosphereScore,
            proceduralAtmosphereScore = composition.ProceduralAtmosphereScore,
            naturalLightingScore = composition.NaturalLightingScore,
            visualArtifactPenalty = composition.VisualArtifactPenalty,
            compositingVisibilityPenalty = composition.CompositingVisibilityPenalty,
            edgeIntegrationScore = composition.EdgeIntegrationScore,
            compositingSeamPenalty = composition.CompositingSeamPenalty,
            atmosphereContinuityScore = composition.AtmosphereContinuityScore,
            environmentalDepthScore = composition.EnvironmentalDepthScore,
            supportObjectDepthScore = composition.SupportObjectDepthScore,
            atmosphereDepthScore = composition.AtmosphereDepthScore,
            fogBlendScore = composition.FogBlendScore,
            proceduralArtifactPenalty = composition.ProceduralArtifactPenalty,
            cinematicSoftnessScore = composition.CinematicSoftnessScore,
            atmosphericRealismScore = composition.AtmosphericRealismScore,
            cinematicSubtletyScore = composition.CinematicSubtletyScore,
            visualPreset = composition.VisualPreset,
            readabilityScore = composition.ReadabilityScore,
            clickabilityScore = composition.ClickabilityScore,
            glowIntensity = composition.GlowIntensity,
            deepSpacePenaltyApplied = composition.DeepSpacePenaltyApplied,
            foregroundObjectAreaPercent = composition.ForegroundObjectAreaPercent,
            overlapPenaltyApplied = composition.OverlapPenaltyApplied,
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
            compositionBalanceScore = composition.CompositionBalanceScore,
            depthScore = composition.DepthScore,
            atmosphericBlendScore = composition.AtmosphericBlendScore,
            negativeSpaceScore = composition.NegativeSpaceScore,
            heroIsolationScore = composition.HeroIsolationScore,
            cinematicRealismScore = composition.CinematicRealismScore,
            organicAtmosphereScore = composition.OrganicAtmosphereScore,
            proceduralAtmosphereScore = composition.ProceduralAtmosphereScore,
            naturalLightingScore = composition.NaturalLightingScore,
            visualArtifactPenalty = composition.VisualArtifactPenalty,
            compositingVisibilityPenalty = composition.CompositingVisibilityPenalty,
            edgeIntegrationScore = composition.EdgeIntegrationScore,
            compositingSeamPenalty = composition.CompositingSeamPenalty,
            atmosphereContinuityScore = composition.AtmosphereContinuityScore,
            environmentalDepthScore = composition.EnvironmentalDepthScore,
            supportObjectDepthScore = composition.SupportObjectDepthScore,
            atmosphereDepthScore = composition.AtmosphereDepthScore,
            fogBlendScore = composition.FogBlendScore,
            proceduralArtifactPenalty = composition.ProceduralArtifactPenalty,
            cinematicSoftnessScore = composition.CinematicSoftnessScore,
            atmosphericRealismScore = composition.AtmosphericRealismScore,
            cinematicSubtletyScore = composition.CinematicSubtletyScore,
            visualPreset = composition.VisualPreset,
            readabilityScore = composition.ReadabilityScore,
            clickabilityScore = composition.ClickabilityScore,
            glowIntensity = composition.GlowIntensity,
            deepSpacePenaltyApplied = composition.DeepSpacePenaltyApplied,
            foregroundObjectAreaPercent = composition.ForegroundObjectAreaPercent,
            overlapPenaltyApplied = composition.OverlapPenaltyApplied,
            objectOverlapWarnings = composition.ObjectOverlapWarnings,
            safeZoneWarnings = composition.SafeZoneWarnings,
            textBounds = composition.TextBounds,
            language = request.Context.Localization.ResolvedLanguage,
            selectedHook = plan.CelestialSelection?.SelectedHook ?? plan.PrimaryThumbnailText,
            dimensions = new { width, height, fileSizeBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0 },
            warnings
        };
        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "thumbnails", "thumbnail-analysis-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static async Task WriteFallbackAnalysisAsync(ThumbnailGenerationRequest request, ThumbnailPlan fallback, IReadOnlyCollection<string> warnings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(request.OutputDirectory, "thumbnails"));
        var payload = new { assetsFound = Array.Empty<string>(), assetsMissing = Array.Empty<string>(), assetPriorityUsed = Array.Empty<string>(), selectedAssetFileName = string.Empty, selectedAssetSource = string.Empty, oldAssetIgnoredBecauseHeroExists = false, transparentAssetUsed = false, transparentAssetsUsed = 0, cardStyleRemoved = true, objectCount = 0, layoutWarnings = new[] { "fallback-plan" }, heroObjectScale = 0, supportObjectScales = Array.Empty<double>(), layoutUsed = "FallbackToStellariumFrame", compositionBalanceScore = 0d, depthScore = 0d, atmosphericBlendScore = 0d, negativeSpaceScore = 0d, heroIsolationScore = 0d, cinematicRealismScore = 0d, visualPreset = "Premium Documentary", glowIntensity = 0d, deepSpacePenaltyApplied = false, foregroundObjectAreaPercent = 0d, overlapPenaltyApplied = false, language = request.Context.Localization.ResolvedLanguage, dimensions = new { width = 0, height = 0, fileSizeBytes = 0 }, warnings };
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
            compositionBalanceScore = 0d,
            depthScore = 0d,
            atmosphericBlendScore = 0d,
            negativeSpaceScore = 0d,
            heroIsolationScore = 0d,
            cinematicRealismScore = 0d,
            organicAtmosphereScore = 0d,
            naturalLightingScore = 0d,
            visualArtifactPenalty = 0d,
            compositingVisibilityPenalty = 0d,
            edgeIntegrationScore = 0d,
            compositingSeamPenalty = 1d,
            atmosphereContinuityScore = 0d,
            environmentalDepthScore = 0d,
            supportObjectDepthScore = 0d,
            atmosphereDepthScore = 0d,
            fogBlendScore = 0d,
            proceduralArtifactPenalty = 0d,
            cinematicSoftnessScore = 0d,
            atmosphericRealismScore = 0d,
            cinematicSubtletyScore = 0d,
            visualPreset = "Premium Documentary",
            readabilityScore = 0d,
            clickabilityScore = 0d,
            glowIntensity = 0d,
            deepSpacePenaltyApplied = false,
            foregroundObjectAreaPercent = 0d,
            overlapPenaltyApplied = false,
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
            compositionBalanceScore = composition.CompositionBalanceScore,
            depthScore = composition.DepthScore,
            atmosphericBlendScore = composition.AtmosphericBlendScore,
            negativeSpaceScore = composition.NegativeSpaceScore,
            heroIsolationScore = composition.HeroIsolationScore,
            cinematicRealismScore = composition.CinematicRealismScore,
            organicAtmosphereScore = composition.OrganicAtmosphereScore,
            proceduralAtmosphereScore = composition.ProceduralAtmosphereScore,
            naturalLightingScore = composition.NaturalLightingScore,
            visualArtifactPenalty = composition.VisualArtifactPenalty,
            compositingVisibilityPenalty = composition.CompositingVisibilityPenalty,
            edgeIntegrationScore = composition.EdgeIntegrationScore,
            compositingSeamPenalty = composition.CompositingSeamPenalty,
            atmosphereContinuityScore = composition.AtmosphereContinuityScore,
            environmentalDepthScore = composition.EnvironmentalDepthScore,
            supportObjectDepthScore = composition.SupportObjectDepthScore,
            atmosphereDepthScore = composition.AtmosphereDepthScore,
            fogBlendScore = composition.FogBlendScore,
            proceduralArtifactPenalty = composition.ProceduralArtifactPenalty,
            cinematicSoftnessScore = composition.CinematicSoftnessScore,
            atmosphericRealismScore = composition.AtmosphericRealismScore,
            cinematicSubtletyScore = composition.CinematicSubtletyScore,
            visualPreset = composition.VisualPreset,
            readabilityScore = composition.ReadabilityScore,
            clickabilityScore = composition.ClickabilityScore,
            glowIntensity = composition.GlowIntensity,
            deepSpacePenaltyApplied = composition.DeepSpacePenaltyApplied,
            foregroundObjectAreaPercent = composition.ForegroundObjectAreaPercent,
            overlapPenaltyApplied = composition.OverlapPenaltyApplied,
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
                s.DuplicationPenalty,
                s.DeepSpacePenalty
            }).ToArray(),
            textBounds = composition.TextBounds,
            layoutUsed = composition.LayoutUsed
        };
        await File.WriteAllTextAsync(Path.Combine(thumbnailsDirectory, "thumbnail-cinematic-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static string[] BuildAssetPriorityUsed(IReadOnlyCollection<CelestialAsset> assets) => assets.Select(a => $"{a.Source}:{Path.GetFileName(a.LocalPath)}").ToArray();

    private sealed record CompositionDiagnostics(string LayoutUsed, double HeroObjectScale, IReadOnlyCollection<double> SupportObjectScales, int TransparentAssetsUsed, bool CardStyleRemoved, int ObjectCount, IReadOnlyCollection<string> LayoutWarnings, string CinematicMode, double CompositionScore, double ReadabilityScore, double ClickabilityScore, IReadOnlyCollection<string> ObjectOverlapWarnings, IReadOnlyCollection<string> SafeZoneWarnings, object TextBounds, double GlowIntensity, bool DeepSpacePenaltyApplied, double ForegroundObjectAreaPercent, bool OverlapPenaltyApplied, double CompositionBalanceScore, double DepthScore, double AtmosphericBlendScore, double NegativeSpaceScore, double HeroIsolationScore, double CinematicRealismScore, string VisualPreset, double OrganicAtmosphereScore, double ProceduralAtmosphereScore, double NaturalLightingScore, double VisualArtifactPenalty, double CompositingVisibilityPenalty, double CinematicSubtletyScore, double EdgeIntegrationScore, double CompositingSeamPenalty, double AtmosphereContinuityScore, double EnvironmentalDepthScore, double SupportObjectDepthScore, double AtmosphereDepthScore, double FogBlendScore, double ProceduralArtifactPenalty, double CinematicSoftnessScore, double AtmosphericRealismScore);
    private sealed record ResolvedAsset(string Path, string FileName, string Source, bool OldAssetIgnoredBecauseHeroExists, string BaseDirectory);
    private sealed record SelectedObject(string Name, string Type, string Key, bool FallbackAllowed, SceneObservationContext? Scene = null, AstronomyEventModel? Event = null);
    private sealed record Selection(SelectedObject Hero, IReadOnlyCollection<SelectedObject> Support, IReadOnlyCollection<object> VisibilityData, bool IsSpecialEvent, string CinematicMode, IReadOnlyCollection<HeroObjectScore> HeroScores, bool HasConjunction)
    {
        public HeroObjectScore? HeroScore => HeroScores.FirstOrDefault(s => s.Object.Key.Equals(Hero.Key, StringComparison.OrdinalIgnoreCase));
    }
    private sealed record HeroObjectScore(SelectedObject Object, double TotalScore, double VisibilityWeight, double BrightnessWeight, double RarityWeight, double AstronomyEventWeight, double MoonPenalty, double DuplicationPenalty, double DeepSpacePenalty);
}
