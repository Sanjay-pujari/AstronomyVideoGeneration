using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyAssetProductionJobService(
    MediaFactoryDbContext db,
    ILogger<AstronomyAssetProductionJobService> logger) : IAstronomyAssetProductionJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public async Task<AstronomyAssetProductionJobResult> CreateAssetProductionJobsAsync(AstronomyAssetProductionJobRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var planIds = request.PlanIds is { Count: > 0 } ? request.PlanIds.Where(x => x != Guid.Empty).ToHashSet() : null;
        var categories = ToSet(request.ContentCategories);
        var formats = ToSet(request.PlannedFormats);

        var query = db.ContentGenerationPlans.AsNoTracking().Where(p => p.AssetPlanJson != null);
        if (planIds is { Count: > 0 })
        {
            query = query.Where(p => planIds.Contains(p.Id));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.RegionId))
                query = query.Where(p => p.RegionId == request.RegionId);
            if (categories is { Count: > 0 })
                query = query.Where(p => categories.Contains(p.ContentCategoryCode));
            if (formats is { Count: > 0 })
                query = query.Where(p => p.PlannedFormat != null && formats.Contains(p.PlannedFormat));

            query = query.OrderByDescending(p => p.PriorityScore ?? 0m).ThenBy(p => p.ScheduledUtc ?? DateTimeOffset.MaxValue);
            if (request.MaxPlans.HasValue)
                query = query.Take(request.MaxPlans.Value);
        }

        var rows = await query.Select(p => new { p.Id, p.AssetPlanJson }).ToListAsync(cancellationToken);
        var jobs = new List<AstronomyAssetProductionJobDto>();
        var entities = new List<AstronomyAssetProductionJob>();
        var warnings = new List<string>();
        var skippedDuplicates = 0;

        var planIdSet = rows.Select(r => r.Id).ToHashSet();
        var existingKeys = request.DryRun || planIdSet.Count == 0
            ? new HashSet<JobDuplicateKey>()
            : await LoadExistingDuplicateKeysAsync(planIdSet, cancellationToken);
        var generatedKeys = new HashSet<JobDuplicateKey>();

        foreach (var row in rows)
        {
            var assetPlan = DeserializeAssetPlan(row.Id, row.AssetPlanJson, warnings);
            if (assetPlan is null)
                continue;

            var requirements = ResolveAssetRequirements(row.Id, assetPlan, warnings);

            foreach (var requirement in requirements)
            {
                if (string.IsNullOrWhiteSpace(requirement.AssetType))
                {
                    warnings.Add($"Content generation plan '{row.Id}' has an asset requirement without an asset type and the requirement was skipped.");
                    continue;
                }

                var sceneName = string.IsNullOrWhiteSpace(requirement.SceneName) ? $"Scene {requirement.SceneNumber}" : requirement.SceneName;
                var assetPurpose = string.IsNullOrWhiteSpace(requirement.AssetPurpose) ? requirement.AssetType : requirement.AssetPurpose;
                var plannedProvider = string.IsNullOrWhiteSpace(requirement.PlannedProvider) ? "Manual" : requirement.PlannedProvider;
                var promptOrInstruction = requirement.PromptOrInstruction ?? string.Empty;
                var expectedOutputType = requirement.ExpectedOutputType ?? string.Empty;
                var objectNames = requirement.ObjectNames ?? Array.Empty<string>();
                var dependsOn = requirement.DependsOn ?? Array.Empty<string>();

                var assetPriority = string.IsNullOrWhiteSpace(requirement.AssetPriority)
                    ? AstronomyAssetClassificationRules.ResolvePriority(assetPlan.ContentCategory, requirement.AssetType)
                    : requirement.AssetPriority;
                var executionGroup = string.IsNullOrWhiteSpace(requirement.AssetExecutionGroup)
                    ? AstronomyAssetClassificationRules.ResolveExecutionGroup(requirement.AssetType)
                    : requirement.AssetExecutionGroup;

                var key = new JobDuplicateKey(
                    assetPlan.ContentGenerationPlanId,
                    requirement.SceneNumber,
                    sceneName,
                    requirement.AssetType,
                    expectedOutputType);

                if (!generatedKeys.Add(key) || (!request.DryRun && existingKeys.Contains(key)))
                {
                    skippedDuplicates++;
                    continue;
                }

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
                    sceneName,
                    requirement.AssetType,
                    assetPurpose,
                    objectNames,
                    plannedProvider,
                    promptOrInstruction,
                    expectedOutputType,
                    requirement.Priority,
                    assetPriority,
                    executionGroup,
                    dependsOn,
                    requirement.MetadataJson,
                    AstronomyAssetProductionJobStatuses.Pending,
                    request.DryRun));

                if (!request.DryRun)
                {
                    entities.Add(new AstronomyAssetProductionJob
                    {
                        ContentGenerationPlanId = assetPlan.ContentGenerationPlanId,
                        AstronomyContentOpportunityId = assetPlan.AstronomyContentOpportunityId,
                        AstronomyEventIntelligenceId = assetPlan.AstronomyEventIntelligenceId,
                        SceneNumber = requirement.SceneNumber,
                        SceneName = sceneName,
                        AssetType = requirement.AssetType,
                        AssetPurpose = assetPurpose,
                        PlannedProvider = plannedProvider,
                        ObjectNamesJson = objectNames.Count > 0 ? JsonSerializer.Serialize(objectNames, JsonOptions) : null,
                        PromptOrInstruction = string.IsNullOrWhiteSpace(promptOrInstruction) ? null : promptOrInstruction,
                        ExpectedOutputType = string.IsNullOrWhiteSpace(expectedOutputType) ? null : expectedOutputType,
                        Priority = requirement.Priority,
                        AssetPriority = assetPriority,
                        AssetExecutionGroup = executionGroup,
                        Status = AstronomyAssetProductionJobStatuses.Pending,
                        MetadataJson = SerializeMetadata(requirement.MetadataJson)
                    });
                }
            }
        }

        var savedCount = 0;
        if (!request.DryRun && entities.Count > 0)
        {
            db.AstronomyAssetProductionJobs.AddRange(entities);
            savedCount = await db.SaveChangesAsync(cancellationToken);
        }

        var result = new AstronomyAssetProductionJobResult(
            jobs.Count,
            savedCount,
            skippedDuplicates,
            jobs.Count(j => j.AssetPriority.Equals(AstronomyAssetClassificationRules.Required, StringComparison.OrdinalIgnoreCase)),
            jobs.Count(j => j.AssetPriority.Equals(AstronomyAssetClassificationRules.Preferred, StringComparison.OrdinalIgnoreCase)),
            jobs.Count(j => j.AssetPriority.Equals(AstronomyAssetClassificationRules.Optional, StringComparison.OrdinalIgnoreCase)),
            jobs,
            warnings);

        logger.LogInformation("Phase 8A.2 asset production jobs created {JobCount} job DTO(s), saved {SavedCount}, skipped {SkippedDuplicates}: required={RequiredJobs} preferred={PreferredJobs} optional={OptionalJobs} warnings={WarningCount}", result.JobCount, result.SavedCount, result.SkippedDuplicates, result.RequiredJobs, result.PreferredJobs, result.OptionalJobs, result.Warnings.Count);
        return result;
    }

    private static IReadOnlyList<AstronomyAssetRequirementDto> ResolveAssetRequirements(Guid contentGenerationPlanId, AstronomyAssetPlanDto assetPlan, ICollection<string> warnings)
    {
        var requirements = new List<AstronomyAssetRequirementDto>();
        if (assetPlan.AssetRequirements is { Count: > 0 })
            requirements.AddRange(assetPlan.AssetRequirements.Where(r => r is not null));

        if (assetPlan.SceneAssetGroups is { Count: > 0 })
        {
            var sceneRequirements = assetPlan.SceneAssetGroups
                .Where(g => g?.AssetRequirements is { Count: > 0 })
                .SelectMany(g => g.AssetRequirements)
                .Where(r => r is not null)
                .ToArray();

            requirements.AddRange(sceneRequirements);
            if (assetPlan.AssetRequirements is not { Count: > 0 } && sceneRequirements.Length > 0)
                warnings.Add($"Content generation plan '{contentGenerationPlanId}' AssetPlanJson is missing top-level assetRequirements; recovered {sceneRequirements.Length} requirement(s) from sceneAssetGroups.");
        }

        if (requirements.Count > 0)
        {
            var seen = new HashSet<RequirementDuplicateKey>();
            return requirements
                .Where(r => seen.Add(new RequirementDuplicateKey(
                    contentGenerationPlanId,
                    r.SceneNumber,
                    string.IsNullOrWhiteSpace(r.SceneName) ? $"Scene {r.SceneNumber}" : r.SceneName,
                    r.AssetType ?? string.Empty,
                    r.ExpectedOutputType ?? string.Empty)))
                .ToArray();
        }

        warnings.Add($"Content generation plan '{contentGenerationPlanId}' AssetPlanJson has no asset requirements and was skipped.");
        return Array.Empty<AstronomyAssetRequirementDto>();
    }

    private async Task<HashSet<JobDuplicateKey>> LoadExistingDuplicateKeysAsync(HashSet<Guid> planIds, CancellationToken cancellationToken)
    {
        var existingRows = await db.AstronomyAssetProductionJobs
            .AsNoTracking()
            .Where(j => planIds.Contains(j.ContentGenerationPlanId))
            .Select(j => new { j.ContentGenerationPlanId, j.SceneNumber, j.SceneName, j.AssetType, j.ExpectedOutputType })
            .ToListAsync(cancellationToken);

        return existingRows
            .Select(j => new JobDuplicateKey(j.ContentGenerationPlanId, j.SceneNumber, j.SceneName, j.AssetType, j.ExpectedOutputType ?? string.Empty))
            .ToHashSet();
    }

    private static AstronomyAssetPlanDto? DeserializeAssetPlan(Guid contentGenerationPlanId, string? assetPlanJson, ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(assetPlanJson))
            return null;

        try
        {
            using var _ = JsonDocument.Parse(assetPlanJson);
            return JsonSerializer.Deserialize<AstronomyAssetPlanDto>(assetPlanJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            warnings.Add($"Content generation plan '{contentGenerationPlanId}' has invalid AssetPlanJson and was skipped: {ex.Message}");
            return null;
        }
    }

    private static string? SerializeMetadata(object? metadata)
    {
        if (metadata is null)
            return null;
        if (metadata is JsonElement element && (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined))
            return null;
        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    private static HashSet<string>? ToSet(IReadOnlyList<string>? values) => values is { Count: > 0 }
        ? values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
        : null;

    private sealed record RequirementDuplicateKey(Guid ContentGenerationPlanId, int SceneNumber, string SceneName, string AssetType, string ExpectedOutputType);

    private sealed record JobDuplicateKey(Guid ContentGenerationPlanId, int SceneNumber, string SceneName, string AssetType, string ExpectedOutputType);
}
