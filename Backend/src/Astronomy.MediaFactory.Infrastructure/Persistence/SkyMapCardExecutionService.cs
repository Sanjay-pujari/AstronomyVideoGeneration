using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class SkyMapCardExecutionService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<SkyMapCardExecutionService> logger) : ISkyMapCardExecutionService
{
    private const string SkyMapCard = "SkyMapCard";
    private const string SkyMapCardsDirectory = "sky-map-cards";
    private const string GenerationSource = "Phase8C.2A";

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
        if (requestedAssetTypes is { Count: > 0 } && !requestedAssetTypes.Contains(NormalizeAssetType(SkyMapCard)))
        {
            warnings.Add("No supported SkyMapCard asset type was requested; nothing will be executed.");
            return new AssetExecutionResult(0, 0, 0, 0, [], warnings);
        }

        if (requestedAssetTypes is { Count: > 1 })
        {
            foreach (var assetType in requestedAssetTypes.Where(t => t != NormalizeAssetType(SkyMapCard)))
                warnings.Add($"Skipped unsupported preferred asset type '{assetType}'. Only SkyMapCard is supported by Phase 8C.2A.");
        }

        var query = db.AstronomyAssetProductionJobs
            .Include(j => j.ContentGenerationPlan)
            .Where(j => j.Status == AstronomyAssetProductionJobStatuses.Pending)
            .Where(j => j.AssetType.ToLower() == SkyMapCard.ToLower())
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
                warnings.Add($"Skipped duplicate SkyMapCard job '{job.Id}' because an output already exists. Set overwriteExisting=true to regenerate it.");
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
                warnings.Add($"SkyMapCard job '{job.Id}' failed: {ex.Message}");
                logger.LogWarning(ex, "SkyMapCard job {JobId} failed", job.Id);
            }
        }

        if (!request.DryRun)
            await db.SaveChangesAsync(cancellationToken);

        return new AssetExecutionResult(jobs.Count, completedCount, failedCount, skippedCount, generatedFiles, warnings);
    }

    private string BuildOutputPath(AstronomyAssetProductionJob job, string? requestedRegionId)
    {
        var regionId = SanitizePathSegment(requestedRegionId)
            ?? SanitizePathSegment(job.ContentGenerationPlan?.RegionId)
            ?? SanitizePathSegment(ReadString(ParseMetadata(job.MetadataJson), "regionId"))
            ?? "unknown-region";
        var planId = job.ContentGenerationPlanId.ToString("D");
        var fileName = $"scene-{job.SceneNumber}-skymap-{job.Id:D}.json";

        return Path.Combine(ResolveWorkingDirectoryRoot(), "assets", regionId, planId, SkyMapCardsDirectory, fileName);
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
            ?? FormatUtc(job.ContentGenerationPlan?.ScheduledUtc);
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
        var viewingInstructions = ReadArray(metadata, "viewingInstructions", BuildViewingInstructions(objectNames, eventType));
        var dataPoints = ReadArray(metadata, "dataPoints", BuildDataPoints(objectNames, scheduledUtc, peakUtc, locationName));
        var observationSummary = ReadString(metadata, "observationSummary")
            ?? BuildObservationSummary(objectNames, locationName, scheduledUtc, peakUtc);

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
            ["cardType"] = SkyMapCard,
            ["viewingInstructions"] = viewingInstructions,
            ["observationSummary"] = observationSummary,
            ["dataPoints"] = dataPoints,
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

    private static JsonArray ReadArray(JsonObject metadata, string key, JsonArray fallback)
    {
        if (metadata.TryGetPropertyValue(key, out var value) && value is JsonArray array)
            return new JsonArray(array.Select(item => item?.DeepClone()).ToArray());

        return new JsonArray(fallback.Select(item => item?.DeepClone()).ToArray());
    }

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

    private static JsonArray BuildViewingInstructions(JsonArray objectNames, string eventType)
    {
        var primaryObject = FirstObjectName(objectNames) ?? "the highlighted object";
        var instructions = new List<string>
        {
            "Look toward the clearest visible horizon during the scheduled viewing window.",
            $"Identify {primaryObject} first, then compare nearby bright points against the sky map card.",
            "Use binoculars if local conditions are hazy or city lights reduce contrast."
        };

        if (eventType.Contains("sunset", StringComparison.OrdinalIgnoreCase) || eventType.Contains("conjunction", StringComparison.OrdinalIgnoreCase))
            instructions[0] = "Look toward the western horizon after sunset.";

        return new JsonArray(instructions.Select(instruction => (JsonNode?)JsonValue.Create(instruction)).ToArray());
    }

    private static JsonArray BuildDataPoints(JsonArray objectNames, string? scheduledUtc, string? peakUtc, string locationName)
    {
        var points = new List<string>();
        if (objectNames.Count > 0)
            points.Add($"Objects: {string.Join(", ", objectNames.Select(n => n?.GetValue<string>()).Where(n => !string.IsNullOrWhiteSpace(n)))}");
        if (!string.IsNullOrWhiteSpace(locationName))
            points.Add($"Location: {locationName}");
        if (!string.IsNullOrWhiteSpace(scheduledUtc))
            points.Add($"Scheduled UTC: {scheduledUtc}");
        if (!string.IsNullOrWhiteSpace(peakUtc))
            points.Add($"Peak UTC: {peakUtc}");

        return new JsonArray(points.Select(point => (JsonNode?)JsonValue.Create(point)).ToArray());
    }

    private static string BuildObservationSummary(JsonArray objectNames, string locationName, string? scheduledUtc, string? peakUtc)
    {
        var objects = objectNames.Count > 0
            ? string.Join(", ", objectNames.Select(n => n?.GetValue<string>()).Where(n => !string.IsNullOrWhiteSpace(n)))
            : "the highlighted sky target";
        var timePhrase = !string.IsNullOrWhiteSpace(peakUtc)
            ? $"peaking around {peakUtc}"
            : !string.IsNullOrWhiteSpace(scheduledUtc)
                ? $"scheduled for {scheduledUtc}"
                : "during the planned viewing window";

        return $"Sky map card for {objects} near {locationName}, {timePhrase}.";
    }

    private static string? FirstObjectName(JsonArray objectNames)
        => objectNames.Select(n => n?.GetValue<string>()).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

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
