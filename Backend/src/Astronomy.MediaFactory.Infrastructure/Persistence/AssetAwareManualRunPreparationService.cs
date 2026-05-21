using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AssetAwareManualRunPreparationService(
    MediaFactoryDbContext db,
    IContentCategoryPipelineStrategyResolver strategyResolver ) : IAssetAwareManualRunPreparationService
{
    private static readonly string[] RequiredRoles = ["IntroBackground", "ThumbnailCandidate", "SupportingSkyMap", "OutroBackground"];

    public async Task<AssetAwareManualRunPackage> PrepareAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
    {
        var plan = await db.ContentGenerationPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == contentGenerationPlanId, cancellationToken)
            ?? throw new KeyNotFoundException($"Content generation plan '{contentGenerationPlanId}' was not found.");

        if (plan.Status is "Completed" or "Failed" or "Cancelled" or "Skipped")
        {
            throw new InvalidOperationException($"Asset-aware manual run package is not allowed for status '{plan.Status}'.");
        }

        if (plan.Status is not ("Planned" or "ReadyForManualRun" or "InProgress"))
        {
            throw new InvalidOperationException($"Asset-aware manual run package is only allowed for Planned, ReadyForManualRun, or InProgress plans. Current status is '{plan.Status}'.");
        }

        var strategy = strategyResolver.Resolve(plan.ContentCategoryCode);
        var warnings = new List<string>();
        object? runPipelineRequest = null;
        AssetAwareMetadata? metadata = null;
        var visualAssets = new List<VisualAssetItem>();

        if (strategy is null)
        {
            warnings.Add("No pipeline strategy implemented for this category yet.");
        }
        else
        {
            var build = await strategy.BuildAsync(plan, cancellationToken);
            runPipelineRequest = build.PipelineRequest;
            warnings.AddRange(build.Warnings);
            visualAssets = build.VisualAssets.Select(x => new VisualAssetItem(x.Role, x.Path, x.Exists)).ToList();

            if (build.AssetAwareMetadata is not null)
            {
                metadata = new AssetAwareMetadata(
                    build.AssetAwareMetadata.ContentGenerationPlanId,
                    build.AssetAwareMetadata.ContentCategoryCode,
                    build.AssetAwareMetadata.AstronomyContext,
                    build.AssetAwareMetadata.SceneCapturePlan,
                    visualAssets,
                    build.AssetAwareMetadata.ThumbnailCandidatePath,
                    build.AssetAwareMetadata.RecommendedImageSequence,
                    build.AssetAwareMetadata.Warnings);
                warnings.AddRange(build.AssetAwareMetadata.Warnings);
            }
        }

        var assetsReady = RequiredRoles.All(role => visualAssets.Any(x => x.Role == role && x.Exists));
        var canRunManually = runPipelineRequest is not null;

        return new AssetAwareManualRunPackage(
            plan.Id,
            plan.ContentCategoryCode,
            plan.Status,
            runPipelineRequest,
            metadata,
            assetsReady,
            canRunManually,
            [
                "Review pipeline request.",
                "Confirm visual assets if asset-aware mode is desired.",
                "Manually call /api/pipelines/run with runPipelineRequest.",
                "Use execution tracking APIs separately if needed."
            ],
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
