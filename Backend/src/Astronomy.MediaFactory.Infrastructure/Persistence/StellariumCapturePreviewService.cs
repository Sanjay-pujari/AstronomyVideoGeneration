using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class StellariumCapturePreviewService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions) : IStellariumCapturePreviewService
{
    private const string StellariumScreenshot = "StellariumScreenshot";
    private const string StellariumCapturesDirectory = "stellarium-captures";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StellariumCapturePreviewResult> PreviewCaptureAsync(StellariumCapturePreviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var maxJobs = request.MaxJobs <= 0 ? 50 : request.MaxJobs;
        var query = db.AstronomyAssetProductionJobs
            .Include(j => j.ContentGenerationPlan)
            .Where(j => j.AssetType.ToLower() == StellariumScreenshot.ToLower())
            .Where(j => j.Status == AstronomyAssetProductionJobStatuses.Completed)
            .Where(j => j.OutputPath != null && j.OutputPath.ToLower().EndsWith(".ssc"))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.RegionId))
        {
            var regionId = request.RegionId.Trim();
            query = query.Where(j => j.ContentGenerationPlan != null && j.ContentGenerationPlan.RegionId == regionId);
        }

        if (request.JobIds is { Count: > 0 })
        {
            var jobIds = request.JobIds.Where(id => id != Guid.Empty).ToHashSet();
            query = query.Where(j => jobIds.Contains(j.Id));
        }

        var jobs = await query
            .OrderBy(j => j.Priority)
            .ThenBy(j => j.SceneNumber)
            .ThenBy(j => j.Id)
            .Take(maxJobs)
            .ToListAsync(cancellationToken);

        var previews = jobs.Select(job => BuildPreview(job, request.RegionId)).ToList();
        return new StellariumCapturePreviewResult(
            previews.Count,
            previews.Count(p => p.ValidationStatus == StellariumCaptureValidationStatuses.Valid),
            previews.Count(p => p.Warnings.Count > 0),
            previews.Count(p => p.ValidationStatus == StellariumCaptureValidationStatuses.Invalid),
            previews);
    }

    private StellariumCapturePreview BuildPreview(AstronomyAssetProductionJob job, string? requestedRegionId)
    {
        var warnings = new List<string>();
        var sscFile = job.OutputPath ?? string.Empty;
        var metadataFile = ResolveMetadataPath(sscFile);
        JsonObject metadata = new();

        if (string.IsNullOrWhiteSpace(sscFile) || !File.Exists(sscFile))
        {
            warnings.Add("SSC file does not exist.");
        }
        else if (new FileInfo(sscFile).Length == 0)
        {
            warnings.Add("Empty SSC file.");
        }

        if (string.IsNullOrWhiteSpace(metadataFile) || !File.Exists(metadataFile))
        {
            warnings.Add("Metadata file does not exist.");
        }
        else
        {
            metadata = ReadMetadata(metadataFile, warnings);
        }

        if (metadata.Count == 0)
        {
            metadata = MergeMetadata(metadata, ParseMetadata(job.MetadataJson));
        }

        var targetObjects = ObjectNames(job, metadata);
        var scheduledUtc = ReadString(metadata, "scheduledUtc") ?? FormatUtc(job.ContentGenerationPlan?.ScheduledUtc);
        var peakUtc = ReadString(metadata, "peakUtc") ?? ReadString(metadata, "eventPeakUtc");
        var orientation = ReadString(metadata, "orientation") ?? ReadString(metadata, "suggestedOrientation");
        var requiresLabels = ReadBool(metadata, "requiresLabels") ?? true;
        var requiresConstellationLines = ReadBool(metadata, "requiresConstellationLines") ?? true;
        var requiresLandscape = ReadBool(metadata, "requiresLandscape") ?? IsLandscape(orientation);
        var eventIntelligenceId = job.AstronomyEventIntelligenceId
            ?? job.ContentGenerationPlan?.AstronomyEventIntelligenceId
            ?? ReadGuid(metadata, "astronomyEventIntelligenceId")
            ?? ReadGuid(metadata, "eventIntelligenceId");
        var expectedCapturePath = BuildExpectedCapturePath(job, requestedRegionId, metadata, eventIntelligenceId);
        var captureCommandPreview = BuildCaptureCommandPreview(sscFile, expectedCapturePath);

        if (targetObjects.Count == 0)
            warnings.Add("Missing target objects.");
        if (string.IsNullOrWhiteSpace(orientation))
            warnings.Add("Missing orientation.");
        if (string.IsNullOrWhiteSpace(scheduledUtc) && string.IsNullOrWhiteSpace(peakUtc))
            warnings.Add("Missing scheduledUtc or peakUtc.");
        if (!requiresLabels)
            warnings.Add("Missing labels.");
        if (!requiresConstellationLines)
            warnings.Add("Missing constellation lines.");
        if (!requiresLandscape)
            warnings.Add("Missing landscape.");

        var validationStatus = ResolveValidationStatus(sscFile, metadataFile, targetObjects, scheduledUtc, peakUtc);

        return new StellariumCapturePreview(
            job.Id,
            job.ContentGenerationPlanId,
            eventIntelligenceId,
            sscFile,
            metadataFile,
            targetObjects,
            scheduledUtc,
            peakUtc,
            orientation,
            requiresLabels,
            requiresConstellationLines,
            requiresLandscape,
            expectedCapturePath,
            captureCommandPreview,
            validationStatus,
            warnings);
    }

    private string BuildExpectedCapturePath(AstronomyAssetProductionJob job, string? requestedRegionId, JsonObject metadata, Guid? eventIntelligenceId)
    {
        var regionId = SanitizePathSegment(ReadString(metadata, "regionId"))
            ?? SanitizePathSegment(job.ContentGenerationPlan?.RegionId)
            ?? SanitizePathSegment(requestedRegionId)
            ?? "unknown-region";
        var eventId = eventIntelligenceId ?? job.ContentGenerationPlanId;
        var fileName = $"capture-scene-{job.SceneNumber}-{job.Id:D}.png";
        return Path.Combine(ResolveWorkingDirectoryRoot(), "assets", regionId, "events", eventId.ToString("D"), StellariumCapturesDirectory, fileName);
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory)
            ? "./media-output"
            : renderingOptions.Value.WorkingDirectory;

    private static string ResolveMetadataPath(string sscFile)
        => string.IsNullOrWhiteSpace(sscFile)
            ? string.Empty
            : Path.Combine(Path.GetDirectoryName(sscFile) ?? ".", $"{Path.GetFileNameWithoutExtension(sscFile)}.metadata.json");

    private static string BuildCaptureCommandPreview(string sscFile, string expectedCapturePath)
        => string.Join(Environment.NewLine,
            "Stellarium.exe",
            $"  --script {Path.GetFileName(sscFile)}",
            $"  --capture {Path.GetFileName(expectedCapturePath)}");

    private static JsonObject ReadMetadata(string metadataFile, List<string> warnings)
    {
        try
        {
            var text = File.ReadAllText(metadataFile);
            if (string.IsNullOrWhiteSpace(text))
            {
                warnings.Add("Empty metadata file.");
                return new JsonObject();
            }

            return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            warnings.Add($"Metadata file is not valid JSON: {ex.Message}");
            return new JsonObject();
        }
        catch (IOException ex)
        {
            warnings.Add($"Metadata file could not be read: {ex.Message}");
            return new JsonObject();
        }
    }

    private static string ResolveValidationStatus(string sscFile, string metadataFile, IReadOnlyList<string> targetObjects, string? scheduledUtc, string? peakUtc)
    {
        if (string.IsNullOrWhiteSpace(sscFile) || !File.Exists(sscFile))
            return StellariumCaptureValidationStatuses.Invalid;
        if (string.IsNullOrWhiteSpace(metadataFile) || !File.Exists(metadataFile))
            return StellariumCaptureValidationStatuses.Invalid;
        if (targetObjects.Count == 0)
            return StellariumCaptureValidationStatuses.Invalid;
        if (string.IsNullOrWhiteSpace(scheduledUtc) && string.IsNullOrWhiteSpace(peakUtc))
            return StellariumCaptureValidationStatuses.Invalid;
        return StellariumCaptureValidationStatuses.Valid;
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

    private static JsonObject MergeMetadata(JsonObject first, JsonObject second)
    {
        foreach (var (key, value) in second)
            first[key] = value?.DeepClone();
        return first;
    }

    private static IReadOnlyList<string> ObjectNames(AstronomyAssetProductionJob job, JsonObject metadata)
    {
        var names = ReadStringArray(metadata, "targetObjects")
            .Concat(ReadStringArray(metadata, "objectNames"))
            .Concat(ReadStringArray(metadata, "targetObject"))
            .Concat(ReadStringArray(metadata, "objectName"))
            .ToList();

        if (!string.IsNullOrWhiteSpace(job.ObjectNamesJson))
        {
            try
            {
                names.AddRange(JsonSerializer.Deserialize<IReadOnlyList<string>>(job.ObjectNamesJson, JsonOptions) ?? []);
            }
            catch (JsonException)
            {
                // The metadata JSON file remains the authoritative source for capture previews.
            }
        }

        return names.Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> ReadStringArray(JsonObject metadata, string key)
    {
        if (!metadata.TryGetPropertyValue(key, out var value) || value is null)
            return [];

        if (value is JsonArray array)
            return array.Select(item => item?.GetValue<string>()).Where(item => !string.IsNullOrWhiteSpace(item))!;

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return [];
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
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var flag))
            return flag;
        if (value is JsonValue textValue && textValue.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed))
            return parsed;
        return null;
    }

    private static Guid? ReadGuid(JsonObject metadata, string key)
    {
        var value = ReadString(metadata, key);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static string? FormatUtc(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("O");

    private static bool IsLandscape(string? orientation)
        => string.IsNullOrWhiteSpace(orientation) || orientation.Contains("landscape", StringComparison.OrdinalIgnoreCase) || orientation.Contains("horizon", StringComparison.OrdinalIgnoreCase) || orientation.Contains("wide", StringComparison.OrdinalIgnoreCase);

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static class StellariumCaptureValidationStatuses
    {
        public const string Valid = "Valid";
        public const string Invalid = "Invalid";
    }
}
