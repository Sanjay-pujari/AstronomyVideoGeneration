using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DailySkyGuideAssetAwareContextService(
    MediaFactoryDbContext db,
    IContentPlanningService planning,
    IDailySkyGuideVisualAssetProvider assetProvider) : IDailySkyGuideAssetAwareContextService
{
    private static readonly string[] RecommendedSequence = ["IntroBackground", "ThumbnailCandidate", "SupportingSkyMap", "OutroBackground"];

    public async Task<DailySkyGuideAssetAwareExecutionContext> BuildAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
    {
        var plan = await db.ContentGenerationPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == contentGenerationPlanId, cancellationToken)
            ?? throw new KeyNotFoundException($"Content generation plan '{contentGenerationPlanId}' was not found.");

        if (!string.Equals(plan.ContentCategoryCode, "DailySkyGuide", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Plan '{contentGenerationPlanId}' is '{plan.ContentCategoryCode}', expected 'DailySkyGuide'.");
        }

        var warnings = new List<string>();
        var assets = (await assetProvider.GetAssetsAsync(contentGenerationPlanId, cancellationToken)).ToList();
        var scenePlan = await planning.BuildStellariumScenePlanPreviewAsync(contentGenerationPlanId, cancellationToken);

        var sceneLookup = scenePlan.Scenes
            .Where(x => !string.IsNullOrWhiteSpace(x.OutputImageRole))
            .GroupBy(x => x.OutputImageRole, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).First(), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < assets.Count; i++)
        {
            if (sceneLookup.TryGetValue(assets[i].Role, out var scene))
            {
                assets[i] = assets[i] with { SceneCode = scene.SceneCode, SceneType = scene.SceneType, TargetObjectCode = scene.TargetObjectCode };
            }
        }

        warnings.AddRange(scenePlan.Warnings);
        var missing = assets.Where(x => !x.Exists).Select(x => x.Role).ToArray();
        if (missing.Length > 0)
        {
            warnings.Add($"Missing expected assets: {string.Join(", ", missing)}.");
        }

        var context = await planning.BuildDailySkyGuideContextPreviewAsync(contentGenerationPlanId, cancellationToken);
        var thumbnailPath = assets.FirstOrDefault(x => x.Role.Equals("ThumbnailCandidate", StringComparison.OrdinalIgnoreCase))?.Path;

        return new DailySkyGuideAssetAwareExecutionContext(
            plan.Id,
            plan.ContentCategoryCode,
            plan.RegionId,
            context.LocationName,
            context.TargetDate,
            plan.Language,
            plan.Title,
            plan.PrimaryCelestialObjectCode,
            thumbnailPath,
            assets,
            RecommendedSequence.ToList(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
}
