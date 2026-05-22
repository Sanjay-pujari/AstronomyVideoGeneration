using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DailySkyGuidePreviewVideoGenerator(
    MediaFactoryDbContext db,
    IDailySkyGuideAssetAwareCompositionPlanner planner,
    IAssetAwarePreviewVideoComposer composer,
    IOptions<RenderingOptions> renderingOptions) : IDailySkyGuidePreviewVideoGenerator
{
    private readonly RenderingOptions _renderingOptions = renderingOptions.Value;

    public async Task<AssetAwarePreviewVideoResponse> GenerateAsync(Guid contentGenerationPlanId, AssetAwarePreviewVideoRequest request, CancellationToken cancellationToken)
    {
        var preview = await BuildPreviewInfoInternalAsync(contentGenerationPlanId, cancellationToken);
        if (!preview.Success || string.IsNullOrWhiteSpace(preview.OutputVideoPath))
            return preview;

        if (File.Exists(preview.OutputVideoPath) && !request.OverwriteExisting)
        {
            preview.Warnings.Add("Preview video already exists. Set overwriteExisting=true to regenerate.");
            return preview;
        }

        var plan = await planner.BuildAsync(contentGenerationPlanId, cancellationToken);
        var composed = await composer.ComposeAsync(plan, request, preview.OutputVideoPath, cancellationToken);
        var outputPath = composed.OutputVideoPath ?? preview.OutputVideoPath;
        var outputExists = !string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        var thumbnailExists = !string.IsNullOrWhiteSpace(composed.ThumbnailPath) && File.Exists(composed.ThumbnailPath) && new FileInfo(composed.ThumbnailPath).Length > 0;

        if (!outputExists)
        {
            var ffmpegErrorSummary = string.IsNullOrWhiteSpace(composed.FfmpegStandardError)
                ? "Preview composition failed."
                : $"Preview composition failed: {composed.FfmpegStandardError.Trim()}";
            return preview with
            {
                Success = false,
                ErrorMessage = ffmpegErrorSummary,
                OutputVideoPath = outputPath,
                ThumbnailPath = composed.ThumbnailPath ?? preview.ThumbnailPath,
                FfmpegCommandLine = request.Diagnostics ? composed.FfmpegCommandLine : null,
                FfmpegExitCode = request.Diagnostics ? composed.FfmpegExitCode : null,
                FfmpegStandardError = request.Diagnostics ? composed.FfmpegStandardError : null,
                FfmpegStandardOutput = request.Diagnostics ? composed.FfmpegStandardOutput : null,
                ResolvedFfmpegPath = request.Diagnostics ? composed.ResolvedFfmpegPath : null
            };
        }

        return preview with
        {
            Success = true,
            OutputVideoPath = outputPath,
            ThumbnailPath = thumbnailExists ? composed.ThumbnailPath : preview.ThumbnailPath,
            Warnings = thumbnailExists ? preview.Warnings : [.. preview.Warnings, "Preview thumbnail does not exist or is empty."],
            FfmpegCommandLine = request.Diagnostics ? composed.FfmpegCommandLine : null,
            FfmpegExitCode = request.Diagnostics ? composed.FfmpegExitCode : null,
            FfmpegStandardError = request.Diagnostics ? composed.FfmpegStandardError : null,
            FfmpegStandardOutput = request.Diagnostics ? composed.FfmpegStandardOutput : null,
            ResolvedFfmpegPath = request.Diagnostics ? composed.ResolvedFfmpegPath : null
        };
    }

    public Task<AssetAwarePreviewVideoResponse> GetPreviewInfoAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
        => BuildPreviewInfoInternalAsync(contentGenerationPlanId, cancellationToken);

    private async Task<AssetAwarePreviewVideoResponse> BuildPreviewInfoInternalAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
    {
        var entity = await db.ContentGenerationPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == contentGenerationPlanId, cancellationToken);
        if (entity is null)
        {
            return new(contentGenerationPlanId, false, null, null, 0, 0, [], ["Plan not found."], "Plan not found.", OutputFolder: ResolveOutputDirectory(contentGenerationPlanId));
        }

        if (!string.Equals(entity.ContentCategoryCode, "DailySkyGuide", StringComparison.OrdinalIgnoreCase))
        {
            return new(contentGenerationPlanId, false, null, null, 0, 0, [], ["Plan category is not DailySkyGuide."], "Invalid category.", OutputFolder: ResolveOutputDirectory(contentGenerationPlanId));
        }

        var plan = await planner.BuildAsync(contentGenerationPlanId, cancellationToken);
        var outputDirectory = ResolveOutputDirectory(contentGenerationPlanId);
        var outputVideoPath = Path.Combine(outputDirectory, "daily-skyguide-preview.mp4");
        var thumbnailPath = Path.Combine(outputDirectory, "daily-skyguide-preview-thumbnail.png");

        var segments = plan.Segments.Select(x => new AssetAwarePreviewSegmentResult(
            x.SortOrder, x.SegmentCode, x.SegmentType, x.ImagePath, x.ImageExists, x.SuggestedDurationSeconds,
            x.ImageExists, x.ImageExists ? "zoompan+fade" : null, x.ImageExists ? null : "Image missing")).ToList();

        var included = segments.Where(x => x.IncludedInVideo).ToList();
        if (included.Count == 0)
        {
            return new(contentGenerationPlanId, false, outputVideoPath, thumbnailPath, 0, 0, segments, ["No valid image assets found."], "No valid image assets found.", OutputFolder: outputDirectory);
        }

        var warnings = new List<string>(plan.Warnings);
        if (!File.Exists(outputVideoPath)) warnings.Add("Preview video does not exist yet.");
        return new(contentGenerationPlanId, true, outputVideoPath, thumbnailPath, included.Count, included.Sum(x => x.DurationSeconds), segments, warnings, null, OutputFolder: outputDirectory);
    }

    private string ResolveOutputDirectory(Guid planId)
    {
        var renderingRoot = string.IsNullOrWhiteSpace(_renderingOptions.WorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : _renderingOptions.WorkingDirectory;
        return Path.Combine(renderingRoot, "content-plans", planId.ToString("D"), "preview-videos");
    }
}
