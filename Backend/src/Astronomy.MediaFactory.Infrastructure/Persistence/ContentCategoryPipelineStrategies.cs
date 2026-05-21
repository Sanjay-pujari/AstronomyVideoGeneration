using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DailySkyGuidePipelineStrategy(IDailySkyGuideVisualAssetPackager visualAssetPackager, IDailySkyGuideContextBuilder contextBuilder) : IContentCategoryPipelineStrategy
{
    public string CategoryCode => "DailySkyGuide";

    public async Task<PipelineBuildResult> BuildAsync(ContentGenerationPlan plan, CancellationToken cancellationToken)
    {
        if (!string.Equals(plan.ContentCategoryCode, CategoryCode, StringComparison.OrdinalIgnoreCase))
        {
            return new PipelineBuildResult(
                false,
                plan.ContentCategoryCode,
                plan.Id,
                null,
                [],
                [],
                $"Strategy '{CategoryCode}' cannot build category '{plan.ContentCategoryCode}'."));
        }

        var warnings = new List<string>();
        // Bridge step for preview only: ensure DailySkyGuide context can still be built without executing pipeline.
        _ = await contextBuilder.BuildAsync(plan, cancellationToken);
        var visualAssetPackage = await visualAssetPackager.BuildPackageAsync(plan.Id, cancellationToken);
        warnings.AddRange(visualAssetPackage.Warnings);

        var scheduledUtc = plan.ScheduledUtc?.UtcDateTime ?? DateTime.UtcNow;
        var request = new RunPipelineRequest(
            DateOnly.FromDateTime(scheduledUtc),
            ContentType.DailySkyGuide,
            plan.RegionId,
            RegionId: plan.RegionId,
            Language: plan.Language);

        var pipelineRequest = new Dictionary<string, object?>
        {
            ["runPipelineRequest"] = request,
            ["title"] = plan.Title,
            ["scheduledUtc"] = plan.ScheduledUtc,
            ["primaryCelestialObjectCode"] = plan.PrimaryCelestialObjectCode,
            ["hookStyleCode"] = plan.HookStyleCode,
            ["narrationStyleCode"] = plan.NarrationStyleCode,
            ["thumbnailStyleCode"] = plan.ThumbnailStyleCode
        };

        return new PipelineBuildResult(true, plan.ContentCategoryCode, plan.Id, pipelineRequest, visualAssetPackage.Assets.ToList(), warnings, null);
    }
}

public sealed class ContentCategoryPipelineStrategyResolver(IEnumerable<IContentCategoryPipelineStrategy> strategies) : IContentCategoryPipelineStrategyResolver
{
    public IContentCategoryPipelineStrategy? Resolve(string contentCategoryCode)
        => strategies.FirstOrDefault(x => x.CategoryCode.Equals(contentCategoryCode, StringComparison.OrdinalIgnoreCase));
}
