using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            var hook = _options.EnableHookText ? await GenerateOptimizedHookAsync(request, cancellationToken) : string.Empty;
            var outputPath = Path.Combine(thumbnailsDirectory, request.IsShortForm ? _options.ShortThumbnailOutputName : _options.LongThumbnailOutputName);

            await _thumbnailCompositionService.ComposeAsync(new ThumbnailCompositionRequest
            {
                GenerationRequest = request,
                SelectedCandidate = selection.SelectedCandidate,
                HookText = hook,
                OutputPath = outputPath
            }, cancellationToken);

            var plan = new ThumbnailPlan
            {
                PrimaryThumbnailText = string.IsNullOrWhiteSpace(hook) ? strategyPlan.PrimaryThumbnailText : hook,
                AlternateThumbnailTexts = strategyPlan.AlternateThumbnailTexts,
                LayoutType = strategyPlan.LayoutType,
                LayoutCandidates = strategyPlan.LayoutCandidates,
                SelectedVisualPath = selection.SelectedCandidate.Path,
                ThumbnailPath = outputPath,
                LongThumbnailPath = request.IsShortForm ? null : outputPath,
                ShortThumbnailPath = request.IsShortForm ? outputPath : null,
                ThumbnailVariantPaths = [outputPath],
                CandidateScores = selection.CandidateScores,
                Variants = strategyPlan.Variants
            };

            await WriteSelectionAsync(plan, thumbnailsDirectory, cancellationToken);
            await WriteAnalysisReportAsync(request, outputPath, selection, hook, errors, cancellationToken);
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
            plan.ThumbnailPath,
            plan.LongThumbnailPath,
            plan.ShortThumbnailPath,
            plan.SelectedVisualPath,
            plan.PrimaryThumbnailText,
            plan.AlternateThumbnailTexts,
            plan.ThumbnailVariantPaths,
            LayoutType = plan.LayoutType.ToString()
        };
        await File.WriteAllTextAsync(Path.Combine(thumbnailsDirectory, "thumbnail-selection.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private async Task WriteAnalysisReportAsync(ThumbnailGenerationRequest request, string outputPath, ThumbnailCandidateSelection selection, string hook, IReadOnlyCollection<string> errors, CancellationToken cancellationToken)
    {
        var selected = selection.SelectedCandidate;
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
            fallbackUsed = selection.FallbackUsed,
            errors,
            fileSizeBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0,
            candidateScores = selection.CandidateScores.Where(x => !x.IsRejected),
            rejectedCandidates = selection.CandidateScores.Where(x => x.IsRejected)
        };

        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "thumbnails", "thumbnail-analysis-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
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
            fileSizeBytes = !string.IsNullOrWhiteSpace(fallback.ThumbnailPath) && File.Exists(fallback.ThumbnailPath) ? new FileInfo(fallback.ThumbnailPath).Length : 0
        };
        var thumbnailsDirectory = Path.Combine(request.OutputDirectory, "thumbnails");
        Directory.CreateDirectory(thumbnailsDirectory);
        await File.WriteAllTextAsync(Path.Combine(thumbnailsDirectory, "thumbnail-analysis-report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
