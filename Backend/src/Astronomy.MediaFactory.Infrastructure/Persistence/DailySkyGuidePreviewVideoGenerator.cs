using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DailySkyGuidePreviewVideoGenerator(
    MediaFactoryDbContext db,
    IDailySkyGuideAssetAwareCompositionPlanner planner,
    IAssetAwarePreviewVideoComposer composer,
    IOptions<StellariumOptions> stellariumOptions) : IDailySkyGuidePreviewVideoGenerator
{
    private readonly StellariumOptions _stellariumOptions = stellariumOptions.Value;

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

        Directory.CreateDirectory(Path.GetDirectoryName(preview.OutputVideoPath)!);
        var plan = await planner.BuildAsync(contentGenerationPlanId, cancellationToken);
        var composed = await composer.ComposeAsync(plan, request, preview.OutputVideoPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(composed) || !File.Exists(composed))
        {
            return preview with { Success = false, ErrorMessage = "Preview composition failed.", OutputVideoPath = composed ?? preview.OutputVideoPath };
        }

        return preview with { Success = true, OutputVideoPath = composed };
    }

    public Task<AssetAwarePreviewVideoResponse> GetPreviewInfoAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
        => BuildPreviewInfoInternalAsync(contentGenerationPlanId, cancellationToken);

    private async Task<AssetAwarePreviewVideoResponse> BuildPreviewInfoInternalAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
    {
        var entity = await db.ContentGenerationPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == contentGenerationPlanId, cancellationToken);
        if (entity is null)
        {
            return new(contentGenerationPlanId, false, null, null, 0, 0, [], ["Plan not found."], "Plan not found.");
        }

        if (!string.Equals(entity.ContentCategoryCode, "DailySkyGuide", StringComparison.OrdinalIgnoreCase))
        {
            return new(contentGenerationPlanId, false, null, null, 0, 0, [], ["Plan category is not DailySkyGuide."], "Invalid category.");
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
            return new(contentGenerationPlanId, false, outputVideoPath, thumbnailPath, 0, 0, segments, ["No valid image assets found."], "No valid image assets found.");
        }

        var warnings = new List<string>(plan.Warnings);
        if (!File.Exists(outputVideoPath)) warnings.Add("Preview video does not exist yet.");
        return new(contentGenerationPlanId, true, outputVideoPath, thumbnailPath, included.Count, included.Sum(x => x.DurationSeconds), segments, warnings, null);
    }

    private string ResolveOutputDirectory(Guid planId)
    {
        if (!string.IsNullOrWhiteSpace(_stellariumOptions.OutputRoot))
            return Path.Combine(_stellariumOptions.OutputRoot, planId.ToString("D"), "preview-videos");
        return Path.Combine(_stellariumOptions.CaptureDirectory, "content-plans", planId.ToString("D"), "preview-videos");
    }
}
