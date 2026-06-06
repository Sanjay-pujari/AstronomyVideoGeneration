using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class NasaAssetExecutionService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<NasaAssetExecutionService> logger) : INasaAssetExecutionService
{
    private const string NasaAsset = "NasaAsset";
    private const string NasaAssetsDirectory = "nasa-assets";
    private const string GenerationSource = "Phase8E.1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<AssetExecutionResult> ExecuteOptionalAssetsAsync(AssetExecutionRequest request, CancellationToken cancellationToken)
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
        if (requestedAssetTypes is { Count: > 0 } && !requestedAssetTypes.Contains(NormalizeAssetType(NasaAsset)))
        {
            warnings.Add("No supported NasaAsset asset type was requested; nothing will be executed.");
            return new AssetExecutionResult(0, 0, 0, 0, [], warnings);
        }

        if (requestedAssetTypes is { Count: > 1 })
        {
            foreach (var assetType in requestedAssetTypes.Where(t => t != NormalizeAssetType(NasaAsset)))
                warnings.Add($"Skipped unsupported optional asset type '{assetType}'. Only NasaAsset is supported by Phase 8E.1.");
        }

        if (request.EnableExternalLookup)
            warnings.Add("External NASA lookup is disabled for Phase 8E.1; generated NASA search request metadata only.");

        var query = db.AstronomyAssetProductionJobs
            .Include(j => j.ContentGenerationPlan)
            .Where(j => j.Status == AstronomyAssetProductionJobStatuses.Pending)
            .Where(j => j.AssetType.ToLower() == NasaAsset.ToLower())
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
                warnings.Add($"Skipped duplicate NasaAsset job '{job.Id}' because an output already exists. Set overwriteExisting=true to regenerate it.");
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
                warnings.Add($"NasaAsset job '{job.Id}' failed to generate metadata JSON: {ex.Message}");
                logger.LogWarning(ex, "NasaAsset job {JobId} failed to generate metadata JSON", job.Id);
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
        var fileName = $"nasa-asset-scene-{job.SceneNumber}-{job.Id:D}.json";

        return Path.Combine(ResolveWorkingDirectoryRoot(), "assets", regionId, "events", eventIntelligenceId.ToString("D"), NasaAssetsDirectory, fileName);
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
        var searchTerms = ReadArray(metadata, "searchTerms", BuildDefaultSearchTerms(objectNames, eventType));
        var fallbackToAiImage = ReadBool(metadata, "fallbackToAiImage") ?? true;
        var assetUsagePurpose = ReadString(metadata, "assetUsagePurpose")
            ?? ReadString(metadata, "usagePurpose")
            ?? job.AssetPurpose
            ?? "Optional NASA reference imagery metadata for production enhancement.";

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
            ["assetType"] = NasaAsset,
            ["searchTerms"] = searchTerms,
            ["selectedAssets"] = new JsonArray(),
            ["fallbackToAiImage"] = fallbackToAiImage,
            ["assetUsagePurpose"] = assetUsagePurpose,
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

    private static bool? ReadBool(JsonObject metadata, string key)
    {
        if (!metadata.TryGetPropertyValue(key, out var value) || value is null)
            return null;

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<bool>(out var boolValue))
                return boolValue;
            if (jsonValue.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed))
                return parsed;
        }

        return null;
    }

    private static Guid? ReadGuid(JsonObject metadata, string key)
        => Guid.TryParse(ReadString(metadata, key), out var value) ? value : null;

    private static JsonArray ReadArray(JsonObject metadata, string key, JsonArray fallback)
    {
        if (metadata.TryGetPropertyValue(key, out var value))
        {
            if (value is JsonArray array)
                return new JsonArray(array.Where(item => !IsBlankString(item)).Select(item => item?.DeepClone()).ToArray());

            if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                return new JsonArray { text };
        }

        return new JsonArray(fallback.Select(item => item?.DeepClone()).ToArray());
    }

    private static bool IsBlankString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text);

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

    private static JsonArray BuildDefaultSearchTerms(JsonArray objectNames, string eventType)
    {
        var terms = objectNames
            .Select(n => n?.GetValue<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => (JsonNode?)JsonValue.Create(n))
            .ToArray();

        if (terms.Length > 0)
            return new JsonArray(terms);

        if (!string.IsNullOrWhiteSpace(eventType))
            return new JsonArray { eventType };

        return [];
    }

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
