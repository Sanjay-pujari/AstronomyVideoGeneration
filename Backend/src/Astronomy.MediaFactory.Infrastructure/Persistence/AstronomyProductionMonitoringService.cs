using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyProductionMonitoringService(
    MediaFactoryDbContext db,
    IEnumerable<IAstronomyAssetProducer> producers) : IAstronomyProductionMonitoringService
{
    private const int TopPendingAssetCount = 10;

    public async Task<AstronomyProductionSummary> GetProductionSummaryAsync(AstronomyProductionMonitoringRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StartUtc.HasValue && request.EndUtc.HasValue && request.StartUtc > request.EndUtc)
            throw new ArgumentException("startUtc must be earlier than or equal to endUtc.", nameof(request));

        var warnings = new List<string>();
        var normalizedRegionId = string.IsNullOrWhiteSpace(request.RegionId) ? null : request.RegionId.Trim();

        var eventQuery = ApplyEventFilters(db.AstronomyEventIntelligences.AsNoTracking(), normalizedRegionId, request.StartUtc, request.EndUtc);
        var eventIdsQuery = eventQuery.Select(e => e.Id);

        var opportunityQuery = db.AstronomyContentOpportunities.AsNoTracking().AsQueryable();
        if (HasIntelligenceFilter(normalizedRegionId, request.StartUtc, request.EndUtc))
            opportunityQuery = opportunityQuery.Where(o => eventIdsQuery.Contains(o.AstronomyEventIntelligenceId));

        var planQuery = ApplyPlanFilters(db.ContentGenerationPlans.AsNoTracking(), normalizedRegionId, request.StartUtc, request.EndUtc);
        var planIdsQuery = planQuery.Select(p => p.Id);

        var jobQuery = db.AstronomyAssetProductionJobs.AsNoTracking().AsQueryable();
        if (HasPlanFilter(normalizedRegionId, request.StartUtc, request.EndUtc))
            jobQuery = jobQuery.Where(j => planIdsQuery.Contains(j.ContentGenerationPlanId));

        var eventStatusCounts = await CountByStatusAsync(eventQuery.Select(e => e.Status), cancellationToken);
        var opportunityStatusCounts = await CountByStatusAsync(opportunityQuery.Select(o => o.Status), cancellationToken);
        var planStatusCounts = await CountByStatusAsync(planQuery.Select(p => p.Status), cancellationToken);
        var jobStatusCounts = await CountByStatusAsync(jobQuery.Select(j => j.Status), cancellationToken);

        var eventCounts = new ProductionEventCounts(
            eventStatusCounts.Total,
            eventStatusCounts.Count("Candidate"),
            eventStatusCounts.Count("Planned"),
            eventStatusCounts.Count("Completed"));

        var opportunityCounts = new ProductionOpportunityCounts(
            opportunityStatusCounts.Total,
            opportunityStatusCounts.Count("Proposed"),
            opportunityStatusCounts.Count("Planned"),
            opportunityStatusCounts.Count("Completed"));

        var videoPlanCounts = new ProductionVideoPlanCounts(
            planStatusCounts.Total,
            planStatusCounts.Count("Planned"),
            planStatusCounts.Count("Completed"),
            planStatusCounts.Count("Failed"));

        var pendingJobCount = jobStatusCounts.Count(AstronomyAssetProductionJobStatuses.Pending);
        var completedJobCount = jobStatusCounts.Count(AstronomyAssetProductionJobStatuses.Completed);
        var assetJobCounts = new ProductionAssetJobCounts(
            jobStatusCounts.Total,
            pendingJobCount,
            completedJobCount,
            jobStatusCounts.Count(AstronomyAssetProductionJobStatuses.Failed),
            jobStatusCounts.Count(AstronomyAssetProductionJobStatuses.InProgress));

        var assetTypeBreakdown = await BuildAssetTypeBreakdownAsync(jobQuery, cancellationToken);
        var priorityBreakdown = await BuildPriorityBreakdownAsync(jobQuery, cancellationToken);
        var producerCoverage = await BuildProducerCoverageAsync(jobQuery, cancellationToken);
        var topPendingAssets = await BuildTopPendingAssetsAsync(jobQuery, cancellationToken);
        var completionPercent = assetJobCounts.Total == 0
            ? 0m
            : Math.Round(assetJobCounts.Completed * 100m / assetJobCounts.Total, 2, MidpointRounding.AwayFromZero);

        if (assetJobCounts.Failed > 0)
            warnings.Add($"{assetJobCounts.Failed} asset production job(s) are failed.");
        var uncoveredTypes = producerCoverage.Where(c => !c.Covered).Select(c => c.AssetType).ToArray();
        if (uncoveredTypes.Length > 0)
            warnings.Add($"No asset producer coverage for: {string.Join(", ", uncoveredTypes)}.");
        if (assetJobCounts.Total == 0)
            warnings.Add("No asset production jobs found for the selected filters.");

        return new AstronomyProductionSummary(
            DateTimeOffset.UtcNow,
            normalizedRegionId,
            eventCounts,
            opportunityCounts,
            videoPlanCounts,
            assetJobCounts,
            assetTypeBreakdown,
            priorityBreakdown,
            producerCoverage,
            topPendingAssets,
            completionPercent,
            warnings);
    }

    private static IQueryable<AstronomyEventIntelligence> ApplyEventFilters(
        IQueryable<AstronomyEventIntelligence> query,
        string? regionId,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtc)
    {
        if (!string.IsNullOrWhiteSpace(regionId))
            query = query.Where(e => e.RegionId == regionId);
        if (startUtc.HasValue)
            query = query.Where(e => e.StartUtc >= startUtc.Value);
        if (endUtc.HasValue)
            query = query.Where(e => e.StartUtc <= endUtc.Value);
        return query;
    }

    private static IQueryable<ContentGenerationPlan> ApplyPlanFilters(
        IQueryable<ContentGenerationPlan> query,
        string? regionId,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtc)
    {
        if (!string.IsNullOrWhiteSpace(regionId))
            query = query.Where(p => p.RegionId == regionId);
        if (startUtc.HasValue)
            query = query.Where(p => p.ScheduledUtc.HasValue && p.ScheduledUtc.Value >= startUtc.Value);
        if (endUtc.HasValue)
            query = query.Where(p => p.ScheduledUtc.HasValue && p.ScheduledUtc.Value <= endUtc.Value);
        return query;
    }

    private static bool HasIntelligenceFilter(string? regionId, DateTimeOffset? startUtc, DateTimeOffset? endUtc)
        => !string.IsNullOrWhiteSpace(regionId) || startUtc.HasValue || endUtc.HasValue;

    private static bool HasPlanFilter(string? regionId, DateTimeOffset? startUtc, DateTimeOffset? endUtc)
        => !string.IsNullOrWhiteSpace(regionId) || startUtc.HasValue || endUtc.HasValue;

    private async Task<IReadOnlyList<ProductionAssetTypeBreakdownItem>> BuildAssetTypeBreakdownAsync(IQueryable<AstronomyAssetProductionJob> jobQuery, CancellationToken cancellationToken)
    {
        var groups = await jobQuery
            .GroupBy(j => j.AssetType)
            .Select(g => new
            {
                AssetType = g.Key,
                Total = g.Count(),
                Completed = g.Count(j => j.Status == AstronomyAssetProductionJobStatuses.Completed),
                Pending = g.Count(j => j.Status == AstronomyAssetProductionJobStatuses.Pending)
            })
            .ToListAsync(cancellationToken);

        return groups
            .OrderBy(g => g.AssetType, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProductionAssetTypeBreakdownItem(g.AssetType, g.Total, g.Completed, g.Pending))
            .ToList();
    }

    private async Task<ProductionPriorityBreakdown> BuildPriorityBreakdownAsync(IQueryable<AstronomyAssetProductionJob> jobQuery, CancellationToken cancellationToken)
    {
        var counts = await CountByStatusAsync(jobQuery.Select(j => j.AssetPriority), cancellationToken);
        return new ProductionPriorityBreakdown(
            counts.Count(AstronomyAssetClassificationRules.Required),
            counts.Count(AstronomyAssetClassificationRules.Preferred),
            counts.Count(AstronomyAssetClassificationRules.Optional));
    }

    private async Task<IReadOnlyList<ProductionProducerCoverageItem>> BuildProducerCoverageAsync(IQueryable<AstronomyAssetProductionJob> jobQuery, CancellationToken cancellationToken)
    {
        var assetTypes = await jobQuery
            .Select(j => j.AssetType)
            .Distinct()
            .ToListAsync(cancellationToken);

        return assetTypes
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(assetType =>
            {
                var sample = new AstronomyAssetProductionJob { AssetType = assetType };
                var producer = producers.FirstOrDefault(p => p.CanHandle(sample));
                return new ProductionProducerCoverageItem(assetType, producer?.ProducerName ?? "Uncovered", producer is not null);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<ProductionPendingAssetItem>> BuildTopPendingAssetsAsync(IQueryable<AstronomyAssetProductionJob> jobQuery, CancellationToken cancellationToken)
    {
        var pendingJobs = await jobQuery
            .Where(j => j.Status == AstronomyAssetProductionJobStatuses.Pending)
            .Select(j => new
            {
                j.AssetType,
                j.AssetPriority,
                j.Priority,
                j.CreatedUtc,
                j.ContentGenerationPlanId,
                ContentCategory = j.ContentGenerationPlan == null ? string.Empty : j.ContentGenerationPlan.ContentCategoryCode
            })
            .ToListAsync(cancellationToken);

        return pendingJobs
            .OrderBy(j => PriorityRank(j.AssetPriority))
            .ThenBy(j => j.Priority)
            .ThenBy(j => j.CreatedUtc)
            .Take(TopPendingAssetCount)
            .Select(j => new ProductionPendingAssetItem(j.AssetType, j.AssetPriority, j.ContentCategory, j.ContentGenerationPlanId))
            .ToList();
    }

    private static async Task<StatusCounts> CountByStatusAsync(IQueryable<string> statuses, CancellationToken cancellationToken)
    {
        var groups = await statuses
            .GroupBy(s => s)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new StatusCounts(groups.ToDictionary(g => Normalize(g.Status), g => g.Count, StringComparer.Ordinal));
    }

    private static int PriorityRank(string? priority)
    {
        var normalized = Normalize(priority);
        if (normalized == Normalize(AstronomyAssetClassificationRules.Required)) return 0;
        if (normalized == Normalize(AstronomyAssetClassificationRules.Preferred)) return 1;
        return 2;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private sealed class StatusCounts(IReadOnlyDictionary<string, int> counts)
    {
        public int Total => counts.Values.Sum();
        public int Count(string status) => counts.TryGetValue(Normalize(status), out var count) ? count : 0;
    }
}
