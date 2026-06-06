using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ConstellationGuideExecutionService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<ConstellationGuideExecutionService> logger) : IConstellationGuideExecutionService
{
    private const string ConstellationGuide = "ConstellationGuide";
    private const string ConstellationGuidesDirectory = "constellation-guides";
    private const string GenerationSource = "Phase8C.2B";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<AssetExecutionResult> ExecutePreferredAssetsAsync(AssetExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var maxJobs = request.MaxJobs <= 0 ? 50 : request.MaxJobs;
        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var completedCount = 0;
        var failedCount = 0;
        var skippedCount = 0;

        var requestedAssetTypes = ToSet(request.AssetTypes);
        if (requestedAssetTypes is { Count: > 0 } && !requestedAssetTypes.Contains(NormalizeAssetType(ConstellationGuide)))
        {
            warnings.Add("No supported ConstellationGuide asset type was requested; nothing will be executed.");
            return new AssetExecutionResult(0, 0, 0, 0, [], warnings);
        }

        if (requestedAssetTypes is { Count: > 1 })
        {
            foreach (var assetType in requestedAssetTypes.Where(t => t != NormalizeAssetType(ConstellationGuide)))
                warnings.Add($"Skipped unsupported preferred asset type '{assetType}'. Only ConstellationGuide is supported by Phase 8C.2B.");
        }

        var query = db.AstronomyAssetProductionJobs
            .Include(j => j.ContentGenerationPlan)
            .Where(j => j.Status == AstronomyAssetProductionJobStatuses.Pending)
            .Where(j => j.AssetType.ToLower() == ConstellationGuide.ToLower())
            .AsQueryable();

        if (request.JobIds is { Count: > 0 })
        {
            var jobIds = request.JobIds.Where(id => id != Guid.Empty).ToHashSet();
            query = query.Where(j => jobIds.Contains(j.Id));
        }

        if (!string.IsNullOrWhiteSpace(request.RegionId))
        {
            var regionId = request.RegionId.Trim();
            query = query.Where(j => j.ContentGenerationPlan != null && j.ContentGenerationPlan.RegionId == regionId);
        }

        var jobs = await query
            .OrderBy(j => j.Priority)
            .ThenBy(j => j.SceneNumber)
            .ThenBy(j => j.Id)
            .Take(maxJobs)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputPath = BuildOutputPath(job, request.RegionId);
            generatedFiles.Add(outputPath);

            if (request.DryRun)
                continue;

            if (!request.OverwriteExisting && (File.Exists(outputPath) || !string.IsNullOrWhiteSpace(job.OutputPath)))
            {
                skippedCount++;
                warnings.Add($"Skipped duplicate ConstellationGuide job '{job.Id}' because an output already exists. Set overwriteExisting=true to regenerate it.");
                continue;
            }

            job.StartedUtc = DateTimeOffset.UtcNow;
            job.FailureReason = null;

            try
            {
                var assetJson = BuildAssetJson(job, request.RegionId);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
                await File.WriteAllTextAsync(outputPath, assetJson, cancellationToken);

                job.OutputPath = outputPath;
                job.Status = AstronomyAssetProductionJobStatuses.Completed;
                job.CompletedUtc = DateTimeOffset.UtcNow;
                completedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                job.Status = AstronomyAssetProductionJobStatuses.Failed;
                job.FailureReason = ex.Message;
                job.CompletedUtc = DateTimeOffset.UtcNow;
                failedCount++;
                warnings.Add($"ConstellationGuide job '{job.Id}' failed: {ex.Message}");
                logger.LogWarning(ex, "ConstellationGuide job {JobId} failed", job.Id);
            }
        }

        if (!request.DryRun)
            await db.SaveChangesAsync(cancellationToken);

        return new AssetExecutionResult(jobs.Count, completedCount, failedCount, skippedCount, generatedFiles, warnings);
    }

    private string BuildOutputPath(AstronomyAssetProductionJob job, string? requestedRegionId)
    {
        var metadata = ParseMetadata(job.MetadataJson);
        var regionId = SanitizePathSegment(job.ContentGenerationPlan?.RegionId)
            ?? SanitizePathSegment(ReadString(metadata, "regionId"))
            ?? SanitizePathSegment(requestedRegionId)
            ?? "unknown-region";
        var eventIntelligenceId = job.AstronomyEventIntelligenceId
            ?? job.ContentGenerationPlan?.AstronomyEventIntelligenceId
            ?? ReadGuid(metadata, "astronomyEventIntelligenceId")
            ?? ReadGuid(metadata, "eventIntelligenceId")
            ?? job.ContentGenerationPlanId;
        var fileName = $"constellation-guide-scene-{job.SceneNumber}-{job.Id:D}.json";

        return Path.Combine(ResolveWorkingDirectoryRoot(), "assets", regionId, "events", eventIntelligenceId.ToString("D"), ConstellationGuidesDirectory, fileName);
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory)
            ? "./media-output"
            : renderingOptions.Value.WorkingDirectory;

    private static string BuildAssetJson(AstronomyAssetProductionJob job, string? requestedRegionId)
    {
        var metadata = ParseMetadata(job.MetadataJson);
        var objectNames = ObjectNames(job);
        var regionId = ReadString(metadata, "regionId")
            ?? job.ContentGenerationPlan?.RegionId
            ?? requestedRegionId
            ?? "";
        var locationName = ReadString(metadata, "locationName")
            ?? ReadString(metadata, "location")
            ?? regionId;
        var scheduledUtc = ReadString(metadata, "scheduledUtc")
            ?? FormatUtc(job.ContentGenerationPlan?.ScheduledUtc)
            ?? "";
        var peakUtc = ReadString(metadata, "peakUtc")
            ?? ReadString(metadata, "eventPeakUtc")
            ?? scheduledUtc;
        var eventType = ReadString(metadata, "eventType")
            ?? ReadString(metadata, "eventTypeCode")
            ?? job.ContentGenerationPlan?.PrimaryAstronomyEventTypeCode
            ?? "";
        var eventCode = ReadString(metadata, "eventCode")
            ?? ReadString(metadata, "astronomyEventCode")
            ?? "";
        var viewingDirection = ReadString(metadata, "viewingDirection")
            ?? ReadString(metadata, "direction")
            ?? BuildDefaultViewingDirection(eventType);
        var recommendedObservationTime = ReadString(metadata, "recommendedObservationTime")
            ?? peakUtc
            ?? scheduledUtc;

        var output = new JsonObject
        {
            ["sceneNumber"] = job.SceneNumber,
            ["sceneName"] = job.SceneName,
            ["eventCode"] = eventCode,
            ["eventType"] = eventType,
            ["regionId"] = regionId,
            ["locationName"] = locationName,
            ["scheduledUtc"] = scheduledUtc,
            ["peakUtc"] = peakUtc,
            ["objectNames"] = objectNames,
            ["guideType"] = ConstellationGuide,
            ["viewingDirection"] = viewingDirection,
            ["recommendedObservationTime"] = recommendedObservationTime,
            ["constellationHints"] = BuildConstellationHints(),
            ["starHopInstructions"] = BuildStarHopInstructions(objectNames),
            ["orientationTips"] = BuildOrientationTips(viewingDirection),
            ["labelingRequirements"] = BuildLabelingRequirements(),
            ["generationSource"] = GenerationSource,
            ["generatedUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };

        return JsonSerializer.Serialize(output, JsonOptions);
    }

    private static JsonObject ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(metadataJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static string? ReadString(JsonObject metadata, string key)
    {
        if (!metadata.TryGetPropertyValue(key, out var value) || value is null)
            return null;

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
            return string.IsNullOrWhiteSpace(text) ? null : text;

        return value.ToJsonString(JsonOptions);
    }

    private static Guid? ReadGuid(JsonObject metadata, string key)
        => Guid.TryParse(ReadString(metadata, key), out var value) ? value : null;

    private static JsonArray ObjectNames(AstronomyAssetProductionJob job)
    {
        if (string.IsNullOrWhiteSpace(job.ObjectNamesJson))
            return [];

        try
        {
            var names = JsonSerializer.Deserialize<IReadOnlyList<string>>(job.ObjectNamesJson, JsonOptions) ?? [];
            return new JsonArray(names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string BuildDefaultViewingDirection(string eventType)
        => IsPlanetConjunctionOrGrouping(eventType)
            ? "Western sky after sunset"
            : "Clearest horizon during the scheduled viewing window";

    private static bool IsPlanetConjunctionOrGrouping(string eventType)
        => eventType.Contains("conjunction", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("grouping", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("planet", StringComparison.OrdinalIgnoreCase);

    private static JsonArray BuildConstellationHints()
        => new()
        {
            "Use nearby bright planets as anchors.",
            "Enable constellation lines and labels in the final Stellarium view."
        };

    private static JsonArray BuildStarHopInstructions(JsonArray objectNames)
    {
        var names = objectNames.Select(n => n?.GetValue<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (names.Any(name => string.Equals(name, "Jupiter", StringComparison.OrdinalIgnoreCase))
            && names.Any(name => string.Equals(name, "Venus", StringComparison.OrdinalIgnoreCase)))
        {
            return new JsonArray { "Locate Venus first, then look nearby for Jupiter." };
        }

        if (names.Count > 1)
            return new JsonArray { "Locate the brightest planet first, then scan along the ecliptic." };

        return new JsonArray { "Locate the highlighted object first, then compare surrounding bright stars against the constellation labels." };
    }

    private static JsonArray BuildOrientationTips(string viewingDirection)
        => new()
        {
            $"Start by facing {viewingDirection} and keep the horizon visible for orientation.",
            "Use constellation labels and horizon markers together so viewers can align the final scene with the real sky.",
            "Binoculars are optional for confirming faint nearby stars, but the guide should remain useful to unaided-eye viewers."
        };

    private static JsonObject BuildLabelingRequirements()
        => new()
        {
            ["showConstellationLines"] = true,
            ["showConstellationLabels"] = true,
            ["showObjectLabels"] = true,
            ["showHorizon"] = true
        };

    private static string? FormatUtc(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("O");

    private static HashSet<string>? ToSet(IReadOnlyList<string>? values)
        => values is { Count: > 0 }
            ? values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(NormalizeAssetType).ToHashSet(StringComparer.Ordinal)
            : null;

    private static string NormalizeAssetType(string? assetType)
        => (assetType ?? string.Empty).Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }
}
