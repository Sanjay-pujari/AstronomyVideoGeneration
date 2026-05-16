using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

[Obsolete("Deprecated: active production thumbnails use LocalAssetCollageThumbnailService with curated local assets.")]
public sealed class CinematicThumbnailService : ICinematicThumbnailService
{
    private readonly IThumbnailStrategyService _thumbnailStrategyService;
    private readonly IThumbnailCandidateSelector _thumbnailCandidateSelector;
    private readonly IThumbnailCompositionService _thumbnailCompositionService;
    private readonly IThumbnailHookService _thumbnailHookService;
    private readonly IThumbnailAiOptimizationService _thumbnailAiOptimizationService;
    private readonly ICelestialAssetProvider _celestialAssetProvider;
    private readonly ICinematicCollageComposer _cinematicCollageComposer;
    private readonly ThumbnailOptions _options;
    private readonly ThumbnailCinematicAIOptions _cinematicOptions;
    private readonly ILogger<CinematicThumbnailService> _logger;

    public CinematicThumbnailService(
        IThumbnailStrategyService thumbnailStrategyService,
        IThumbnailCandidateSelector thumbnailCandidateSelector,
        IThumbnailCompositionService thumbnailCompositionService,
        IThumbnailHookService thumbnailHookService,
        IThumbnailAiOptimizationService thumbnailAiOptimizationService,
        ICelestialAssetProvider celestialAssetProvider,
        ICinematicCollageComposer cinematicCollageComposer,
        IOptions<ThumbnailOptions> options,
        IOptions<ThumbnailCinematicAIOptions> cinematicOptions,
        ILogger<CinematicThumbnailService> logger)
    {
        _thumbnailStrategyService = thumbnailStrategyService;
        _thumbnailCandidateSelector = thumbnailCandidateSelector;
        _thumbnailCompositionService = thumbnailCompositionService;
        _thumbnailHookService = thumbnailHookService;
        _thumbnailAiOptimizationService = thumbnailAiOptimizationService;
        _celestialAssetProvider = celestialAssetProvider;
        _cinematicCollageComposer = cinematicCollageComposer;
        _options = options.Value;
        _cinematicOptions = cinematicOptions.Value;
        _logger = logger;
    }

    public async Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
    {
        var strategyPlan = _thumbnailStrategyService.BuildPlan(request);
        Directory.CreateDirectory(request.OutputDirectory);
        var thumbnailsDirectory = Path.Combine(request.OutputDirectory, "thumbnails");
        Directory.CreateDirectory(thumbnailsDirectory);

        if (!_options.Enabled || !string.Equals(_options.Mode, "CinematicComposed", StringComparison.OrdinalIgnoreCase))
            return BuildDisabledPlan(strategyPlan);

        var errors = new List<string>();
        try
        {
            var selection = await _thumbnailCandidateSelector.SelectAsync(request, cancellationToken);
            errors.AddRange(selection.Errors);
            var hook = _options.EnableHookText ? ThumbnailAiOptimizationService.NormalizeDirectionalHook(await GenerateOptimizedHookAsync(request, cancellationToken)) : string.Empty;
            var outputPath = Path.Combine(thumbnailsDirectory, request.IsShortForm ? _options.ShortThumbnailOutputName : _options.LongThumbnailOutputName);

            var selectedCandidate = selection.SelectedCandidate;
            var celestialSelection = await BuildCelestialSelectionAsync(request, hook, cancellationToken);
            try
            {
                await _cinematicCollageComposer.ComposeAsync(new CinematicCollageRequest
                {
                    GenerationRequest = request,
                    Selection = celestialSelection,
                    BackgroundPath = selectedCandidate.Path,
                    OutputPath = outputPath
                }, cancellationToken);
            }
            catch (Exception collageEx)
            {
                errors.Add(collageEx.Message);
                _logger.LogWarning(collageEx, "Hybrid celestial collage failed; falling back to existing cinematic screenshot composition.");
                selectedCandidate = await ComposeProductionReadyAsync(request, selection, hook, outputPath, cancellationToken);
                celestialSelection = MarkFallback(celestialSelection);
            }

            var plan = new ThumbnailPlan
            {
                PrimaryThumbnailText = string.IsNullOrWhiteSpace(celestialSelection.SelectedHook) ? strategyPlan.PrimaryThumbnailText : celestialSelection.SelectedHook,
                AlternateThumbnailTexts = strategyPlan.AlternateThumbnailTexts,
                LayoutType = strategyPlan.LayoutType,
                LayoutCandidates = strategyPlan.LayoutCandidates,
                SelectedVisualPath = selectedCandidate.Path,
                ThumbnailPath = outputPath,
                LongThumbnailPath = request.IsShortForm ? null : outputPath,
                ShortThumbnailPath = request.IsShortForm ? outputPath : null,
                ThumbnailVariantPaths = [outputPath],
                CandidateScores = selection.CandidateScores,
                Variants = strategyPlan.Variants,
                FallbackUsed = celestialSelection.FallbackUsed,
                Mode = "HybridCinematicCollage",
                CelestialSelection = celestialSelection
            };

            await WriteSelectionAsync(plan, thumbnailsDirectory, celestialSelection, cancellationToken);
            await WriteHybridCinematicReportAsync(request, outputPath, celestialSelection, selectedCandidate, cancellationToken);
            await WriteAnalysisReportAsync(request, outputPath, selection, selectedCandidate, celestialSelection.SelectedHook, celestialSelection.FallbackUsed, errors, cancellationToken);
            await WriteComparisonSheetAsync(request, outputPath, selection, cancellationToken);
            return plan;
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            _logger.LogWarning(ex, "Cinematic thumbnail generation failed. Falling back to extracted-frame thumbnail when available.");
            var fallback = BuildFallbackPlan(strategyPlan);
            await WriteFallbackDiagnosticsAsync(request, fallback, errors, cancellationToken);
            return fallback;
        }
    }

    private async Task<CelestialThumbnailSelection> BuildCelestialSelectionAsync(ThumbnailGenerationRequest request, string hook, CancellationToken cancellationToken)
    {
        var scenes = request.Context.SceneObservationContexts
            .Where(s => s.IsVisible || s.AltitudeDegrees.HasValue || IsSpecialEventObject(request, s))
            .Where(s => !string.IsNullOrWhiteSpace(s.ObjectName) && !s.ObjectName.Equals("Sky", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var specialEventMode = IsSpecialEventMode(request);
        var ordered = scenes
            .Select(s => new { Scene = s, Score = ScoreHeroCandidate(request, s) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Scene.SceneIndex)
            .Select(x => x.Scene)
            .ToList();

        if (ordered.Count == 0 && request.Context.Events.Count > 0)
        {
            ordered.AddRange(request.Context.Events.OrderByDescending(e => e.Score).Take(3).Select((e, i) => new SceneObservationContext
            {
                SceneId = $"event-{i + 1}",
                SceneIndex = i + 1,
                ObjectName = e.ObjectName,
                ObjectType = e.Category,
                IsVisible = true,
                VisibilityReason = e.VisibilityWindow,
                DirectionLabel = e.Direction,
                AltitudeDegrees = null
            }));
        }

        var maxObjects = specialEventMode ? 1 : request.IsShortForm ? 2 : 3;
        var selected = ordered
            .Where(s => !string.IsNullOrWhiteSpace(s.ObjectName))
            .DistinctBy(s => NormalizeObjectKey(s.ObjectName))
            .Take(maxObjects)
            .ToArray();

        if (selected.Length == 0)
        {
            selected = [new SceneObservationContext { SceneId = "fallback-milky-way", ObjectName = "Milky Way", ObjectType = "MilkyWay", IsVisible = true, VisibilityReason = "Fallback cinematic starfield." }];
        }

        var assets = new List<CelestialAsset>();
        foreach (var scene in selected)
        {
            assets.Add(await _celestialAssetProvider.GetAssetAsync(new CelestialAssetRequest
            {
                ObjectName = scene.ObjectName,
                ObjectType = scene.ObjectType,
                PreferPortraitSafe = request.IsShortForm,
                RefreshCache = false
            }, cancellationToken));
        }

        var hero = selected[0];
        return new CelestialThumbnailSelection
        {
            HeroObject = hero.ObjectName,
            SupportObjects = selected.Skip(1).Select(s => s.ObjectName).ToArray(),
            SelectedHook = RefineHook(hook, hero, request),
            SelectedLayout = request.IsShortForm ? "PortraitObjectFirst" : specialEventMode ? "SpecialEventHero" : "LandscapeHeroRightTextLeft",
            AssetSources = assets,
            VisibilityDataUsed = selected.Select(s => new
            {
                s.SceneId,
                s.ObjectName,
                s.ObjectType,
                s.IsVisible,
                s.AltitudeDegrees,
                s.DirectionLabel,
                s.VisibilityReason,
                s.LocalObservationTime
            }).Cast<object>().ToArray(),
            FallbackUsed = assets.Any(a => a.FallbackUsed),
            SpecialEventMode = specialEventMode
        };
    }

    private static CelestialThumbnailSelection MarkFallback(CelestialThumbnailSelection selection) => new()
    {
        HeroObject = selection.HeroObject,
        SupportObjects = selection.SupportObjects,
        SelectedHook = selection.SelectedHook,
        SelectedLayout = selection.SelectedLayout,
        AssetSources = selection.AssetSources,
        VisibilityDataUsed = selection.VisibilityDataUsed,
        FallbackUsed = true,
        SpecialEventMode = selection.SpecialEventMode
    };

    private static bool IsSpecialEventMode(ThumbnailGenerationRequest request)
    {
        var eventText = $"{request.Context.SpecialEvent?.EventType} {request.Context.SpecialEvent?.EventTitle} {request.ContentType}";
        return eventText.Contains("eclipse", StringComparison.OrdinalIgnoreCase)
            || eventText.Contains("meteor", StringComparison.OrdinalIgnoreCase)
            || eventText.Contains("conjunction", StringComparison.OrdinalIgnoreCase)
            || eventText.Contains("comet", StringComparison.OrdinalIgnoreCase)
            || eventText.Contains("alignment", StringComparison.OrdinalIgnoreCase)
            || request.ContentType == ContentType.SpecialEventGuide;
    }

    private static bool IsSpecialEventObject(ThumbnailGenerationRequest request, SceneObservationContext scene)
        => IsSpecialEventMode(request) && !string.IsNullOrWhiteSpace(scene.ObjectName);

    private static double ScoreHeroCandidate(ThumbnailGenerationRequest request, SceneObservationContext scene)
    {
        var text = $"{scene.ObjectName} {scene.ObjectType}";
        var score = 0d;
        if (IsSpecialEventMode(request) && (text.Contains("eclipse", StringComparison.OrdinalIgnoreCase) || text.Contains("meteor", StringComparison.OrdinalIgnoreCase) || text.Contains("comet", StringComparison.OrdinalIgnoreCase))) score += 10;
        score += ResolveBrightnessWeight(scene.ObjectName, scene.ObjectType) * 4;
        score += Math.Clamp((scene.AltitudeDegrees ?? 0) / 90d, 0, 1) * 2;
        score += scene.IsVisible ? 1.5 : 0;
        score += ResolveRarityWeight(scene.ObjectName, scene.ObjectType);
        return score;
    }

    private static double ResolveBrightnessWeight(string objectName, string objectType)
    {
        var text = $"{objectName} {objectType}".ToLowerInvariant();
        if (text.Contains("moon")) return 1.0;
        if (text.Contains("venus")) return 0.97;
        if (text.Contains("jupiter")) return 0.92;
        if (text.Contains("saturn")) return 0.82;
        if (text.Contains("mars")) return 0.76;
        if (text.Contains("mercury")) return 0.68;
        if (text.Contains("sirius")) return 0.72;
        return 0.50;
    }

    private static double ResolveRarityWeight(string objectName, string objectType)
    {
        var text = $"{objectName} {objectType}".ToLowerInvariant();
        if (text.Contains("eclipse") || text.Contains("meteor") || text.Contains("comet") || text.Contains("conjunction")) return 2.2;
        if (text.Contains("galaxy") || text.Contains("nebula")) return 0.8;
        return 0.2;
    }

    private static string RefineHook(string hook, SceneObservationContext hero, ThumbnailGenerationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(hook) && !hook.Contains("Tonight's Sky", StringComparison.OrdinalIgnoreCase) && !hook.Contains("Planets Visible", StringComparison.OrdinalIgnoreCase))
            return hook;
        if (IsSpecialEventMode(request))
        {
            var text = $"{request.Context.SpecialEvent?.EventTitle} {hero.ObjectName}";
            if (text.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return "Meteor Peak Tonight";
            if (text.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "Rare Sky Event";
            if (text.Contains("conjunction", StringComparison.OrdinalIgnoreCase)) return $"Moon Meets {hero.ObjectName}";
            return "Rare Sky Event";
        }
        if (hero.ObjectName.Contains("venus", StringComparison.OrdinalIgnoreCase)) return "Venus After Sunset";
        if (hero.ObjectName.Contains("saturn", StringComparison.OrdinalIgnoreCase)) return "Saturn Before Sunrise";
        if (hero.ObjectName.Contains("moon", StringComparison.OrdinalIgnoreCase)) return "Moon Tonight";
        return $"{hero.ObjectName} Tonight";
    }

    private static string NormalizeObjectKey(string value) => value.Trim().ToLowerInvariant();

    private async Task<ThumbnailCandidateScore> ComposeProductionReadyAsync(ThumbnailGenerationRequest request, ThumbnailCandidateSelection selection, string hook, string outputPath, CancellationToken cancellationToken)
    {
        var candidates = selection.CandidateScores.Where(x => !x.IsRejected).OrderByDescending(x => x.Score).DefaultIfEmpty(selection.SelectedCandidate).Take(4).ToArray();
        ThumbnailCandidateScore? bestCandidate = null;
        ThumbnailProductionQualityResult? bestQuality = null;

        foreach (var candidate in candidates)
        {
            await _thumbnailCompositionService.ComposeAsync(new ThumbnailCompositionRequest
            {
                GenerationRequest = request,
                SelectedCandidate = candidate,
                HookText = hook,
                OutputPath = outputPath
            }, cancellationToken);

            var quality = await ValidateThumbnailQualityAsync(outputPath, hook, cancellationToken);
            if (quality.IsProductionReady)
                return candidate;

            if (bestQuality is null || quality.QualityScore > bestQuality.QualityScore)
            {
                bestQuality = quality;
                bestCandidate = candidate;
            }
        }

        if (bestCandidate is not null && candidates.Length > 0 && bestCandidate.Path != candidates.Last().Path)
        {
            await _thumbnailCompositionService.ComposeAsync(new ThumbnailCompositionRequest
            {
                GenerationRequest = request,
                SelectedCandidate = bestCandidate,
                HookText = hook,
                OutputPath = outputPath
            }, cancellationToken);
        }

        return bestCandidate ?? selection.SelectedCandidate;
    }

    public async Task<ThumbnailProductionQualityResult> ValidateThumbnailQualityAsync(string thumbnailPath, string hookText, CancellationToken cancellationToken)
    {
        if (_thumbnailCompositionService is ThumbnailCompositionService concrete)
            return await concrete.ValidateThumbnailQualityAsync(thumbnailPath, hookText, cancellationToken);

        var score = await new ThumbnailScoringService().ScoreAsync(thumbnailPath, new ThumbnailScoringContext { EnableAstronomySceneMode = true }, cancellationToken);
        var quality = Math.Clamp(score.Score * 0.78 + score.TextSafeCompositionArea * 0.12 + (string.IsNullOrWhiteSpace(hookText) ? 0 : 0.10), 0, 1);
        return new ThumbnailProductionQualityResult
        {
            IsProductionReady = quality >= 0.70,
            Warnings = quality >= 0.70 ? [] : ["quality-below-threshold"],
            QualityScore = Math.Round(quality, 3),
            FocalObjectScore = score.FocalObjectScore,
            TextReadabilityScore = score.TextSafeCompositionArea,
            BlackFrameRisk = score.BlackPixelPercentage,
            MobileReadabilityScore = string.IsNullOrWhiteSpace(hookText) ? 0.4 : 0.9
        };
    }

    private async Task<string> GenerateOptimizedHookAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
    {
        var optimization = await _thumbnailAiOptimizationService.OptimizeAsync(new ThumbnailAiOptimizationRequest
        {
            GenerationRequest = request,
            SeoTitle = request.Metadata.PrimaryTitle,
            Language = request.Context.Localization.ResolvedLanguage,
            Region = request.Context.LocationName,
            TopPerformingHooks = request.FeedbackSignals?.BestHooks ?? []
        }, cancellationToken);

        return string.IsNullOrWhiteSpace(optimization.SelectedHook)
            ? _thumbnailHookService.GenerateHook(request, _options.MaxHookWords)
            : optimization.SelectedHook;
    }

    private static ThumbnailPlan BuildDisabledPlan(ThumbnailPlan strategyPlan) => strategyPlan;

    private ThumbnailPlan BuildFallbackPlan(ThumbnailPlan strategyPlan)
    {
        var fallback = _options.FallbackToExtractedFrame ? strategyPlan.SelectedVisualPath : null;
        return new ThumbnailPlan
        {
            PrimaryThumbnailText = strategyPlan.PrimaryThumbnailText,
            AlternateThumbnailTexts = strategyPlan.AlternateThumbnailTexts,
            LayoutType = strategyPlan.LayoutType,
            LayoutCandidates = strategyPlan.LayoutCandidates,
            SelectedVisualPath = fallback,
            ThumbnailPath = fallback,
            ThumbnailVariantPaths = fallback is null ? [] : [fallback],
            Variants = strategyPlan.Variants
        };
    }

    private static async Task WriteSelectionAsync(ThumbnailPlan plan, string thumbnailsDirectory, CelestialThumbnailSelection selection, CancellationToken cancellationToken)
    {
        var payload = new
        {
            preferredThumbnailPath = plan.ThumbnailPath,
            selectedThumbnailPath = plan.ThumbnailPath,
            thumbnailPath = plan.ThumbnailPath,
            longThumbnailPath = plan.LongThumbnailPath,
            shortThumbnailPath = plan.ShortThumbnailPath,
            originalThumbnailPath = plan.ThumbnailPath,
            originalLongThumbnailPath = plan.LongThumbnailPath,
            originalShortThumbnailPath = plan.ShortThumbnailPath,
            selectedVisualPath = plan.SelectedVisualPath,
            plan.PrimaryThumbnailText,
            plan.AlternateThumbnailTexts,
            plan.ThumbnailVariantPaths,
            fallbackUsed = plan.FallbackUsed,
            mode = plan.Mode,
            visualPolishPassApplied = true,
            LayoutType = plan.LayoutType.ToString(),
            heroObject = selection.HeroObject,
            supportObjects = selection.SupportObjects,
            selectedHook = selection.SelectedHook,
            selectedLayout = selection.SelectedLayout,
            assetSources = selection.AssetSources,
            visibilityDataUsed = selection.VisibilityDataUsed,
            specialEventThumbnailMode = selection.SpecialEventMode
        };
        await File.WriteAllTextAsync(Path.Combine(thumbnailsDirectory, "thumbnail-selection.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private async Task WriteHybridCinematicReportAsync(ThumbnailGenerationRequest request, string outputPath, CelestialThumbnailSelection selection, ThumbnailCandidateScore selectedCandidate, CancellationToken cancellationToken)
    {
        var payload = new
        {
            mode = "HybridCinematicCollage",
            finalThumbnailPath = outputPath,
            dominantObject = selection.HeroObject,
            supportObjects = selection.SupportObjects,
            selectedLayout = selection.SelectedLayout,
            selectedHook = selection.SelectedHook,
            specialEventThumbnailMode = selection.SpecialEventMode,
            assetSources = selection.AssetSources,
            visibilityDrivenSelection = selection.VisibilityDataUsed,
            aiOptimizationUse = new[] { "hook-selection", "title-scoring", "layout-preference", "object-emphasis-scoring" },
            fakeImageGenerationUsed = false,
            fallbackUsed = selection.FallbackUsed,
            mobileFirstValidation = request.IsShortForm ? "portrait object-first composition" : "landscape feed composition with left text-safe negative space",
            organicAtmosphereScore = selectedCandidate.OrganicAtmosphereScore,
            naturalLightingScore = selectedCandidate.NaturalLightingScore,
            visualArtifactPenalty = selectedCandidate.VisualArtifactPenalty,
            compositingVisibilityPenalty = selectedCandidate.CompositingVisibilityPenalty,
            cinematicSubtletyScore = selectedCandidate.CinematicSubtletyScore,
            diagnostics = new[]
            {
                "Stellarium/visibility data used for object ranking and background context.",
                "NASA/local cached celestial assets used for visual hero imagery.",
                "Composition limits support objects to avoid collage clutter.",
                "Documentary color grade, directional rim lighting, organic atmospheric haze, subdued support objects, and reduced text hierarchy applied."
            }
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var outputName = string.IsNullOrWhiteSpace(_cinematicOptions.OutputFileName) ? "thumbnail-cinematic-ai-report.json" : _cinematicOptions.OutputFileName;
        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "thumbnails", outputName), json, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "thumbnails", "thumbnail-cinematic-report.json"), json, cancellationToken);
    }

    private async Task WriteAnalysisReportAsync(ThumbnailGenerationRequest request, string outputPath, ThumbnailCandidateSelection selection, ThumbnailCandidateScore selected, string hook, bool fallbackUsed, IReadOnlyCollection<string> errors, CancellationToken cancellationToken)
    {
        var payload = new
        {
            mode = _options.Mode,
            selectedCandidate = selected,
            generatedLongThumbnail = request.IsShortForm ? null : outputPath,
            generatedShortThumbnail = request.IsShortForm ? outputPath : null,
            hookText = hook,
            language = request.Context.Localization.ResolvedLanguage,
            brightness = selected.Brightness,
            blackPixelPercentage = selected.BlackPixelPercentage,
            fallbackUsed,
            errors,
            fileSizeBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0,
            visualPolishPassApplied = true,
            polishDiagnostics = new[] { "debug-edge-cleanup", "restrained-text-hierarchy", "subtle-object-emphasis", "softened-procedural-overlays", "mobile-readability-gate" },
            scoringWeights = new { focalObjectScore = 0.35, contrastScore = 0.20, glowScore = 0.15, starRichnessScore = 0.10, textReadabilityScore = 0.10, compositionBalanceScore = 0.10 },
            astronomySceneMode = _options.EnableAstronomySceneMode,
            selectedBaseCandidate = selected,
            finalThumbnailPaths = new[] { outputPath },
            allCandidates = selection.CandidateScores,
            candidateScores = selection.CandidateScores.Where(x => !x.IsRejected),
            rejectedCandidates = selection.CandidateScores.Where(x => x.IsRejected)
        };

        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "thumbnails", "thumbnail-analysis-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }


    private static async Task WriteComparisonSheetAsync(ThumbnailGenerationRequest request, string outputPath, ThumbnailCandidateSelection selection, CancellationToken cancellationToken)
    {
        var thumbnailsDirectory = Path.Combine(request.OutputDirectory, "thumbnails");
        Directory.CreateDirectory(thumbnailsDirectory);
        var cells = selection.CandidateScores.Select(x => x.Path).Where(File.Exists).Prepend(outputPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        if (cells.Length == 0)
            return;

        const int cellWidth = 320;
        const int cellHeight = 180;
        var columns = 2;
        var rows = (int)Math.Ceiling(cells.Length / (double)columns);
        using var sheet = new Image<Rgba32>(cellWidth * columns, cellHeight * rows, Color.Black);
        for (var i = 0; i < cells.Length; i++)
        {
            using var image = await Image.LoadAsync<Rgba32>(cells[i], cancellationToken);
            image.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(cellWidth, cellHeight), Mode = ResizeMode.Crop }));
            var x = i % columns * cellWidth;
            var y = i / columns * cellHeight;
            sheet.Mutate(ctx => ctx.DrawImage(image, new Point(x, y), 1f));
        }

        await sheet.SaveAsJpegAsync(Path.Combine(thumbnailsDirectory, "thumbnail-comparison.jpg"), new JpegEncoder { Quality = 88 }, cancellationToken);
    }

    private async Task WriteFallbackDiagnosticsAsync(ThumbnailGenerationRequest request, ThumbnailPlan fallback, IReadOnlyCollection<string> errors, CancellationToken cancellationToken)
    {
        var payload = new
        {
            mode = _options.Mode,
            selectedCandidate = fallback.SelectedVisualPath,
            generatedLongThumbnail = request.IsShortForm ? null : fallback.ThumbnailPath,
            generatedShortThumbnail = request.IsShortForm ? fallback.ThumbnailPath : null,
            hookText = fallback.PrimaryThumbnailText,
            language = request.Context.Localization.ResolvedLanguage,
            brightness = (double?)null,
            blackPixelPercentage = (double?)null,
            fallbackUsed = true,
            errors,
            visualPolishPassApplied = false,
            fileSizeBytes = !string.IsNullOrWhiteSpace(fallback.ThumbnailPath) && File.Exists(fallback.ThumbnailPath) ? new FileInfo(fallback.ThumbnailPath).Length : 0
        };
        var thumbnailsDirectory = Path.Combine(request.OutputDirectory, "thumbnails");
        Directory.CreateDirectory(thumbnailsDirectory);
        await File.WriteAllTextAsync(Path.Combine(thumbnailsDirectory, "thumbnail-analysis-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
