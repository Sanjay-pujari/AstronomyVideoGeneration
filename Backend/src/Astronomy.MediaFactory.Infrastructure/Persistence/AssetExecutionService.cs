using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AssetExecutionService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<AssetExecutionService> logger) : IAssetExecutionService
{
    private const string TextOverlayCard = "TextOverlayCard";
    private const string ThumbnailConcept = "ThumbnailConcept";
    private const string TextCardsDirectory = "text-cards";
    private const string ThumbnailConceptsDirectory = "thumbnail-concepts";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> SupportedAssetTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        TextOverlayCard,
        ThumbnailConcept
    };

    public async Task<AssetExecutionResult> ExecuteRequiredAssetsAsync(AssetExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var maxJobs = request.MaxJobs <= 0 ? 50 : request.MaxJobs;
        var warnings = new List<string>();
        var generatedFiles = new List<string>();
        var completedCount = 0;
        var failedCount = 0;
        var skippedCount = 0;

        var query = db.AstronomyAssetProductionJobs
            .Include(j => j.ContentGenerationPlan)
            .Where(j => j.Status == AstronomyAssetProductionJobStatuses.Pending)
            .Where(j => j.AssetPriority == AstronomyAssetClassificationRules.Required)
            .AsQueryable();

        if (request.JobIds is { Count: > 0 })
        {
            var jobIds = request.JobIds.ToHashSet();
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

            if (!SupportedAssetTypes.Contains(job.AssetType))
            {
                skippedCount++;
                warnings.Add($"Skipped unsupported required asset job '{job.Id}' with asset type '{job.AssetType}'.");
                continue;
            }

            var outputPath = BuildOutputPath(job, request.RegionId);
            generatedFiles.Add(outputPath);

            if (request.DryRun)
                continue;

            if (!request.OverwriteExisting && (File.Exists(outputPath) || !string.IsNullOrWhiteSpace(job.OutputPath)))
            {
                skippedCount++;
                warnings.Add($"Skipped duplicate required asset job '{job.Id}' because an output already exists. Set overwriteExisting=true to regenerate it.");
                continue;
            }

            job.StartedUtc = DateTimeOffset.UtcNow;
            job.FailureReason = null;

            try
            {
                var assetJson = BuildAssetJson(job);
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
                warnings.Add($"Required asset job '{job.Id}' failed: {ex.Message}");
                logger.LogWarning(ex, "Required asset job {JobId} failed", job.Id);
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
            ?? "unknown-region";
        var planId = job.ContentGenerationPlanId.ToString("D");
        var directoryName = NormalizeAssetType(job.AssetType) == NormalizeAssetType(TextOverlayCard)
            ? TextCardsDirectory
            : ThumbnailConceptsDirectory;
        var fileName = $"scene-{job.SceneNumber:000}-{NormalizeAssetType(job.AssetType)}-{job.Id:N}.json";

        return Path.Combine(ResolveWorkingDirectoryRoot(), "assets", regionId, planId, directoryName, fileName);
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory)
            ? "./media-output"
            : renderingOptions.Value.WorkingDirectory;

    private static string BuildAssetJson(AstronomyAssetProductionJob job)
    {
        var metadata = ParseMetadata(job.MetadataJson);
        var output = NormalizeAssetType(job.AssetType) switch
        {
            "textoverlaycard" => BuildTextOverlayCard(metadata, job),
            "thumbnailconcept" => BuildThumbnailConcept(metadata, job),
            _ => throw new InvalidOperationException($"Unsupported asset type '{job.AssetType}'.")
        };

        return JsonSerializer.Serialize(output, JsonOptions);
    }

    private static JsonObject BuildTextOverlayCard(JsonObject metadata, AstronomyAssetProductionJob job) => new()
    {
        ["titleText"] = ReadString(metadata, "titleText") ?? job.SceneName,
        ["subtitleText"] = ReadString(metadata, "subtitleText") ?? job.AssetPurpose,
        ["dataPoints"] = ReadArray(metadata, "dataPoints", ObjectNames(job))
    };

    private static JsonObject BuildThumbnailConcept(JsonObject metadata, AstronomyAssetProductionJob job) => new()
    {
        ["thumbnailText"] = ReadString(metadata, "thumbnailText") ?? job.SceneName,
        ["emotion"] = ReadString(metadata, "emotion") ?? "curiosity",
        ["composition"] = ReadString(metadata, "composition") ?? job.PromptOrInstruction ?? job.AssetPurpose,
        ["keyObjects"] = ReadArray(metadata, "keyObjects", ObjectNames(job))
    };

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
            return new JsonArray(names.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeAssetType(string? assetType)
        => (assetType ?? string.Empty).Replace("_", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }
}
