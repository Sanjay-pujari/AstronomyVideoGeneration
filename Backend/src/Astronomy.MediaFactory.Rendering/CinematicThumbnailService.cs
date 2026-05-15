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

public sealed class CinematicThumbnailService : ICinematicThumbnailService
{
    private readonly IThumbnailStrategyService _thumbnailStrategyService;
    private readonly IThumbnailCandidateSelector _thumbnailCandidateSelector;
    private readonly IThumbnailCompositionService _thumbnailCompositionService;
    private readonly IThumbnailHookService _thumbnailHookService;
    private readonly IThumbnailAiOptimizationService _thumbnailAiOptimizationService;
    private readonly ThumbnailOptions _options;
    private readonly ILogger<CinematicThumbnailService> _logger;

    public CinematicThumbnailService(
        IThumbnailStrategyService thumbnailStrategyService,
        IThumbnailCandidateSelector thumbnailCandidateSelector,
        IThumbnailCompositionService thumbnailCompositionService,
        IThumbnailHookService thumbnailHookService,
        IThumbnailAiOptimizationService thumbnailAiOptimizationService,
        IOptions<ThumbnailOptions> options,
        ILogger<CinematicThumbnailService> logger)
    {
        _thumbnailStrategyService = thumbnailStrategyService;
        _thumbnailCandidateSelector = thumbnailCandidateSelector;
        _thumbnailCompositionService = thumbnailCompositionService;
        _thumbnailHookService = thumbnailHookService;
        _thumbnailAiOptimizationService = thumbnailAiOptimizationService;
        _options = options.Value;
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

            var selectedCandidate = await ComposeProductionReadyAsync(request, selection, hook, outputPath, cancellationToken);

            var plan = new ThumbnailPlan
            {
                PrimaryThumbnailText = string.IsNullOrWhiteSpace(hook) ? strategyPlan.PrimaryThumbnailText : hook,
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
                FallbackUsed = false,
                Mode = "CinematicComposed"
            };

            await WriteSelectionAsync(plan, thumbnailsDirectory, cancellationToken);
            await WriteAnalysisReportAsync(request, outputPath, selection, selectedCandidate, hook, false, errors, cancellationToken);
            if (_options.GenerateComparisonSheet)
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

    private static async Task WriteSelectionAsync(ThumbnailPlan plan, string thumbnailsDirectory, CancellationToken cancellationToken)
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
            LayoutType = plan.LayoutType.ToString()
        };
        await File.WriteAllTextAsync(Path.Combine(thumbnailsDirectory, "thumbnail-selection.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
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
