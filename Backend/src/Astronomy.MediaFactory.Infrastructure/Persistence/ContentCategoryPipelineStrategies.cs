using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DailySkyGuidePipelineStrategy : IContentCategoryPipelineStrategy
{
    public string CategoryCode => "DailySkyGuide";

    public Task<PipelineBuildResult> BuildAsync(ContentGenerationPlan plan, CancellationToken cancellationToken)
    {
        if (!string.Equals(plan.ContentCategoryCode, CategoryCode, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new PipelineBuildResult(
                false,
                plan.ContentCategoryCode,
                plan.Id,
                null,
                [],
                $"Strategy '{CategoryCode}' cannot build category '{plan.ContentCategoryCode}'."));
        }

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

        return Task.FromResult(new PipelineBuildResult(true, plan.ContentCategoryCode, plan.Id, pipelineRequest, [], null));
    }
}

public sealed class ContentCategoryPipelineStrategyResolver(IEnumerable<IContentCategoryPipelineStrategy> strategies) : IContentCategoryPipelineStrategyResolver
{
    public IContentCategoryPipelineStrategy? Resolve(string contentCategoryCode)
        => strategies.FirstOrDefault(x => x.CategoryCode.Equals(contentCategoryCode, StringComparison.OrdinalIgnoreCase));
}
