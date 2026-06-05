using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyAssetProducerPreviewService(
    MediaFactoryDbContext db,
    IEnumerable<IAstronomyAssetProducer> producers,
    ILogger<AstronomyAssetProducerPreviewService> logger) : IAstronomyAssetProducerPreviewService
{
    public async Task<AstronomyAssetProducerPreviewResult> PreviewAssetProductionAsync(AstronomyAssetProducerPreviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var warnings = new List<string>();
        var query = db.AstronomyAssetProductionJobs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.RegionId))
            query = query.Where(j => j.ContentGenerationPlan != null && j.ContentGenerationPlan.RegionId == request.RegionId);

        var jobIds = ToSet(request.JobIds);
        if (jobIds is { Count: > 0 })
            query = query.Where(j => jobIds.Contains(j.Id));

        var assetTypes = ToSet(request.AssetTypes);
        if (assetTypes is { Count: > 0 })
            query = query.Where(j => assetTypes.Contains(j.AssetType.ToLower()));

        var priorities = ToSet(request.AssetPriorities);
        if (priorities is { Count: > 0 })
            query = query.Where(j => priorities.Contains(j.AssetPriority.ToLower()));

        var providers = ToSet(request.Providers);
        if (providers is { Count: > 0 })
            query = query.Where(j => providers.Contains(j.PlannedProvider.ToLower()));

        var statuses = ToSet(request.Status) ?? new HashSet<string>([AstronomyAssetProductionJobStatuses.Pending.ToLowerInvariant()], StringComparer.Ordinal);
        if (statuses.Count > 0)
            query = query.Where(j => statuses.Contains(j.Status.ToLower()));

        var maxJobs = Math.Clamp(request.MaxJobs ?? 20, 1, 500);
        var jobs = await query
            .OrderBy(j => j.Priority)
            .ThenBy(j => j.CreatedUtc)
            .Take(maxJobs)
            .ToListAsync(cancellationToken);

        var previews = new List<AstronomyAssetProductionJobPreview>();
        foreach (var job in jobs)
        {
            var producer = producers.FirstOrDefault(p => p.CanHandle(job));
            if (producer is null)
            {
                warnings.Add($"No asset producer registered for asset type '{job.AssetType}' (job {job.Id}).");
                previews.Add(new AstronomyAssetProductionJobPreview(
                    job.Id,
                    job.ContentGenerationPlanId,
                    job.AssetType,
                    job.AssetPriority,
                    job.AssetExecutionGroup,
                    job.PlannedProvider,
                    null,
                    false,
                    "NoProducer",
                    [$"No asset producer registered for asset type '{job.AssetType}'."],
                    null,
                    null,
                    null,
                    null,
                    false));
                continue;
            }

            var validation = await producer.ValidateAsync(job, cancellationToken);
            var estimate = await producer.EstimateAsync(job, cancellationToken);
            var productionRequest = await producer.CreateProductionRequestAsync(job, cancellationToken);
            previews.Add(new AstronomyAssetProductionJobPreview(
                job.Id,
                job.ContentGenerationPlanId,
                job.AssetType,
                job.AssetPriority,
                job.AssetExecutionGroup,
                job.PlannedProvider,
                producer.ProducerName,
                validation.IsValid,
                validation.Status,
                validation.Messages,
                estimate.EstimatedDurationSeconds,
                estimate.EstimatedCostCategory,
                estimate.EstimatedComplexity,
                productionRequest,
                false));
        }

        var coverage = jobs
            .GroupBy(j => j.AssetType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var sample = g.First();
                var producer = producers.FirstOrDefault(p => p.CanHandle(sample));
                return new AssetProducerCoverageResult(g.Key, producer?.ProducerName ?? "Uncovered", g.Count(), producer is not null);
            })
            .ToList();

        var result = new AstronomyAssetProducerPreviewResult(
            previews.Count,
            previews.Count(p => p.CanProduce),
            previews.Count(p => !p.CanProduce),
            coverage,
            previews,
            warnings);

        logger.LogInformation("Phase 8B asset production preview returned {JobCount} jobs ({ValidJobs} valid, {InvalidJobs} invalid) without execution or database mutation.", result.JobCount, result.ValidJobs, result.InvalidJobs);
        return result;
    }

    private static HashSet<Guid>? ToSet(IReadOnlyList<Guid>? values)
        => values is { Count: > 0 } ? values.Where(v => v != Guid.Empty).ToHashSet() : null;

    private static HashSet<string>? ToSet(IReadOnlyList<string>? values)
        => values is { Count: > 0 }
            ? values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal)
            : null;
}
