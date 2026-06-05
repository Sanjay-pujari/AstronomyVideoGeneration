using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyAssetProductionJobService(
    MediaFactoryDbContext db,
    ILogger<AstronomyAssetProductionJobService> logger) : IAstronomyAssetProductionJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AstronomyAssetProductionJobResult> CreateAssetProductionJobsAsync(AstronomyAssetProductionJobRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.DryRun)
            throw new ArgumentException("Asset production job creation currently supports dryRun=true only.");

        var planIds = request.PlanIds is { Count: > 0 } ? request.PlanIds.Where(x => x != Guid.Empty).ToHashSet() : null;
        var categories = ToSet(request.ContentCategories);
        var formats = ToSet(request.PlannedFormats);

        var query = db.ContentGenerationPlans.AsNoTracking().Where(p => p.AssetPlanJson != null && p.AssetPlanJson != "");
        if (!string.IsNullOrWhiteSpace(request.RegionId))
            query = query.Where(p => p.RegionId == request.RegionId);
        if (planIds is { Count: > 0 })
            query = query.Where(p => planIds.Contains(p.Id));
        if (categories is { Count: > 0 })
            query = query.Where(p => categories.Contains(p.ContentCategoryCode));
        if (formats is { Count: > 0 })
            query = query.Where(p => p.PlannedFormat != null && formats.Contains(p.PlannedFormat));

        query = query.OrderByDescending(p => p.PriorityScore ?? 0m).ThenBy(p => p.ScheduledUtc ?? DateTimeOffset.MaxValue);
        if (request.MaxPlans.HasValue)
            query = query.Take(request.MaxPlans.Value);

        var rows = await query.Select(p => new { p.Id, p.AssetPlanJson }).ToListAsync(cancellationToken);
        var jobs = new List<AstronomyAssetProductionJobDto>();

        foreach (var row in rows)
        {
            var assetPlan = DeserializeAssetPlan(row.Id, row.AssetPlanJson);
            if (assetPlan is null)
                continue;

            foreach (var requirement in assetPlan.AssetRequirements)
            {
                var assetPriority = string.IsNullOrWhiteSpace(requirement.AssetPriority)
                    ? AstronomyAssetClassificationRules.ResolvePriority(assetPlan.ContentCategory, requirement.AssetType)
                    : requirement.AssetPriority;
                var executionGroup = string.IsNullOrWhiteSpace(requirement.AssetExecutionGroup)
                    ? AstronomyAssetClassificationRules.ResolveExecutionGroup(requirement.AssetType)
                    : requirement.AssetExecutionGroup;

                jobs.Add(new AstronomyAssetProductionJobDto(
                    assetPlan.ContentGenerationPlanId,
                    assetPlan.AstronomyContentOpportunityId,
                    assetPlan.AstronomyEventIntelligenceId,
                    assetPlan.ContentCategory,
                    assetPlan.PlannedFormat,
                    assetPlan.RegionId,
                    assetPlan.LocationName,
                    assetPlan.ScheduledUtc,
                    assetPlan.PeakUtc,
                    requirement.SceneNumber,
                    requirement.SceneName,
                    requirement.AssetType,
                    requirement.AssetPurpose,
                    requirement.ObjectNames,
                    requirement.PlannedProvider,
                    requirement.PromptOrInstruction,
                    requirement.ExpectedOutputType,
                    requirement.Priority,
                    assetPriority,
                    executionGroup,
                    requirement.DependsOn,
                    requirement.MetadataJson,
                    DryRun: true));
            }
        }

        var result = new AstronomyAssetProductionJobResult(
            jobs.Count,
            jobs.Count(j => j.AssetPriority.Equals(AstronomyAssetClassificationRules.Required, StringComparison.OrdinalIgnoreCase)),
            jobs.Count(j => j.AssetPriority.Equals(AstronomyAssetClassificationRules.Preferred, StringComparison.OrdinalIgnoreCase)),
            jobs.Count(j => j.AssetPriority.Equals(AstronomyAssetClassificationRules.Optional, StringComparison.OrdinalIgnoreCase)),
            jobs);

        logger.LogInformation("Phase 8A.1 asset production job DTO dry run created {JobCount} job(s): required={RequiredJobs} preferred={PreferredJobs} optional={OptionalJobs}", result.JobCount, result.RequiredJobs, result.PreferredJobs, result.OptionalJobs);
        return result;
    }

    private static AstronomyAssetPlanDto? DeserializeAssetPlan(Guid contentGenerationPlanId, string? assetPlanJson)
    {
        if (string.IsNullOrWhiteSpace(assetPlanJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AstronomyAssetPlanDto>(assetPlanJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Content generation plan '{contentGenerationPlanId}' has invalid AssetPlanJson and cannot be converted into production job DTOs.", ex);
        }
    }

    private static HashSet<string>? ToSet(IReadOnlyList<string>? values) => values is { Count: > 0 }
        ? values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
        : null;
}
