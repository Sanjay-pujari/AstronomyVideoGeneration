using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class StellariumScreenshotExecutionService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<StellariumScreenshotExecutionService> logger) : IStellariumScreenshotExecutionService
{
    private const string StellariumScreenshot = "StellariumScreenshot";
    private const string StellariumScriptsDirectory = "stellarium-scripts";
    private const string GenerationSource = "Phase8D.1";

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
        if (requestedAssetTypes is { Count: > 0 } && !requestedAssetTypes.Contains(NormalizeAssetType(StellariumScreenshot)))
        {
            warnings.Add("No supported StellariumScreenshot asset type was requested; nothing will be executed.");
            return new AssetExecutionResult(0, 0, 0, 0, [], warnings);
        }

        if (requestedAssetTypes is { Count: > 1 })
        {
            foreach (var assetType in requestedAssetTypes.Where(t => t != NormalizeAssetType(StellariumScreenshot)))
                warnings.Add($"Skipped unsupported preferred asset type '{assetType}'. Only StellariumScreenshot is supported by Phase 8D.1.");
        }

        var query = db.AstronomyAssetProductionJobs
            .Include(j => j.ContentGenerationPlan)
            .Where(j => j.Status == AstronomyAssetProductionJobStatuses.Pending)
            .Where(j => j.AssetType.ToLower() == StellariumScreenshot.ToLower())
            .Where(j => j.AssetPriority == AstronomyAssetClassificationRules.Preferred)
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
            var metadataPath = BuildMetadataPath(outputPath);
            generatedFiles.Add(outputPath);
            if (!request.DryRun)
                generatedFiles.Add(metadataPath);

            if (request.DryRun)
                continue;

            if (!request.OverwriteExisting && (File.Exists(outputPath) || File.Exists(metadataPath) || !string.IsNullOrWhiteSpace(job.OutputPath)))
            {
                skippedCount++;
                warnings.Add($"Skipped duplicate StellariumScreenshot job '{job.Id}' because an SSC output already exists. Set overwriteExisting=true to regenerate it.");
                continue;
            }

            job.StartedUtc = DateTimeOffset.UtcNow;
            job.FailureReason = null;

            try
            {
                var ssc = BuildSscScript(job, outputPath, request.RegionId);
                var metadataJson = BuildMetadataJson(job, outputPath, request.RegionId);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
                await File.WriteAllTextAsync(outputPath, ssc, cancellationToken);
                await File.WriteAllTextAsync(metadataPath, metadataJson, cancellationToken);

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
                warnings.Add($"StellariumScreenshot job '{job.Id}' failed during SSC generation: {ex.Message}");
                logger.LogWarning(ex, "StellariumScreenshot SSC generation job {JobId} failed", job.Id);
            }
        }

        if (!request.DryRun)
            await db.SaveChangesAsync(cancellationToken);

        return new AssetExecutionResult(jobs.Count, completedCount, failedCount, skippedCount, generatedFiles, warnings);
    }

    private string BuildOutputPath(AstronomyAssetProductionJob job, string? requestedRegionId)
    {
        var metadata = ParseMetadata(job.MetadataJson);
        var regionId = SanitizePathSegment(ReadString(metadata, "regionId"))
            ?? SanitizePathSegment(job.ContentGenerationPlan?.RegionId)
            ?? SanitizePathSegment(requestedRegionId)
            ?? "unknown-region";
        var eventIntelligenceId = job.AstronomyEventIntelligenceId
            ?? job.ContentGenerationPlan?.AstronomyEventIntelligenceId
            ?? ReadGuid(metadata, "astronomyEventIntelligenceId")
            ?? ReadGuid(metadata, "eventIntelligenceId")
            ?? job.ContentGenerationPlanId;
        var fileName = $"scene-{job.SceneNumber}-stellarium-{job.Id:D}.ssc";

        return Path.Combine(ResolveWorkingDirectoryRoot(), "assets", regionId, "events", eventIntelligenceId.ToString("D"), StellariumScriptsDirectory, fileName);
    }

    private static string BuildMetadataPath(string outputPath)
        => Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", $"{Path.GetFileNameWithoutExtension(outputPath)}.metadata.json");

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory)
            ? "./media-output"
            : renderingOptions.Value.WorkingDirectory;

    private static string BuildSscScript(AstronomyAssetProductionJob job, string outputPath, string? requestedRegionId)
    {
        var model = BuildModel(job, outputPath, requestedRegionId);
        var script = new StringBuilder();
        script.AppendLine("// StellariumScreenshot reusable SSC script");
        script.AppendLine($"// Generated by {GenerationSource}; captureExecuted=false.");
        script.AppendLine("// This script intentionally contains no capture, shutdown, FFmpeg, AI generation, or external API commands.");
        script.AppendLine($"// Scene: {EscapeComment(job.SceneNumber.ToString(CultureInfo.InvariantCulture))} - {EscapeComment(job.SceneName)}");
        script.AppendLine($"// Event: {EscapeComment(model.EventCode)} {EscapeComment(model.EventType)}".TrimEnd());
        script.AppendLine($"// Location hint: {EscapeComment(model.LocationName)} ({EscapeComment(model.RegionId)})");
        script.AppendLine($"// Scheduled UTC: {EscapeComment(model.ScheduledUtc)}");
        script.AppendLine($"// Peak UTC: {EscapeComment(model.PeakUtc)}");
        script.AppendLine($"// Orientation hint: {EscapeComment(model.Orientation)}");
        script.AppendLine($"// Framing instructions: {EscapeComment(BuildFramingInstruction(model.ObjectNames, model.Orientation))}");
        script.AppendLine("core.clear(\"natural\");");
        script.AppendLine($"core.setDate(\"{EscapeSsc(model.CaptureUtc)}\", \"utc\");");
        script.AppendLine($"core.setObserverLocation({FormatDouble(model.Longitude)}, {FormatDouble(model.Latitude)}, 0, 0, \"{EscapeSsc(model.LocationName)}\", \"Earth\");");
        script.AppendLine("core.wait(1.0);");
        script.AppendLine($"ConstellationMgr.setFlagLines({model.RequiresConstellationLines.ToString().ToLowerInvariant()});");
        script.AppendLine($"ConstellationMgr.setFlagLabels({model.RequiresLabels.ToString().ToLowerInvariant()});");
        script.AppendLine($"SolarSystem.setFlagLabels({model.RequiresLabels.ToString().ToLowerInvariant()});");
        script.AppendLine($"NebulaMgr.setFlagHints({model.RequiresLabels.ToString().ToLowerInvariant()});");
        script.AppendLine("StelMovementMgr.setFlagTracking(true);");

        foreach (var objectName in model.ObjectNames)
        {
            script.AppendLine($"// Target object: {EscapeComment(objectName)}");
            script.AppendLine($"core.selectObjectByName(\"{EscapeSsc(objectName)}\", false);");
        }

        var primaryObject = model.ObjectNames.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(primaryObject))
        {
            script.AppendLine($"core.selectObjectByName(\"{EscapeSsc(primaryObject)}\", true);");
            script.AppendLine("core.wait(1.0);");
            script.AppendLine("core.moveToSelectedObject(2.0);");
        }

        script.AppendLine($"StelMovementMgr.zoomTo({FormatDouble(ResolveFieldOfView(model.ObjectNames.Count, model.Orientation))}, 2.0);");
        script.AppendLine("core.wait(3.0);");
        script.AppendLine("// End of reusable SSC preview. Capture is deliberately not executed in Phase 8D.1.");
        return script.ToString();
    }

    private static string BuildMetadataJson(AstronomyAssetProductionJob job, string outputPath, string? requestedRegionId)
    {
        var model = BuildModel(job, outputPath, requestedRegionId);
        var output = new JsonObject
        {
            ["assetType"] = StellariumScreenshot,
            ["eventCode"] = model.EventCode,
            ["eventType"] = model.EventType,
            ["objectNames"] = new JsonArray(model.ObjectNames.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            ["regionId"] = model.RegionId,
            ["locationName"] = model.LocationName,
            ["scheduledUtc"] = model.ScheduledUtc,
            ["peakUtc"] = model.PeakUtc,
            ["orientation"] = model.Orientation,
            ["requiresConstellationLines"] = model.RequiresConstellationLines,
            ["requiresLabels"] = model.RequiresLabels,
            ["sscFile"] = outputPath,
            ["captureExecuted"] = false,
            ["generationSource"] = GenerationSource,
            ["generatedUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };

        return JsonSerializer.Serialize(output, JsonOptions);
    }

    private static StellariumSscModel BuildModel(AstronomyAssetProductionJob job, string outputPath, string? requestedRegionId)
    {
        var metadata = ParseMetadata(job.MetadataJson);
        var objectNames = ObjectNames(job, metadata);
        var regionId = ReadString(metadata, "regionId")
            ?? job.ContentGenerationPlan?.RegionId
            ?? requestedRegionId
            ?? string.Empty;
        var locationName = ReadString(metadata, "locationName")
            ?? ReadString(metadata, "location")
            ?? regionId
            ?? "Earth";
        var scheduledUtc = ReadString(metadata, "scheduledUtc")
            ?? FormatUtc(job.ContentGenerationPlan?.ScheduledUtc)
            ?? string.Empty;
        var peakUtc = ReadString(metadata, "peakUtc")
            ?? ReadString(metadata, "eventPeakUtc")
            ?? scheduledUtc;
        var eventType = ReadString(metadata, "eventType")
            ?? ReadString(metadata, "eventTypeCode")
            ?? job.ContentGenerationPlan?.PrimaryAstronomyEventTypeCode
            ?? string.Empty;
        var eventCode = ReadString(metadata, "eventCode")
            ?? ReadString(metadata, "astronomyEventCode")
            ?? string.Empty;
        var orientation = ReadString(metadata, "suggestedOrientation")
            ?? ReadString(metadata, "orientation")
            ?? "Use horizon-aware framing for the target objects.";
        var captureUtc = NormalizeUtc(peakUtc) ?? NormalizeUtc(scheduledUtc) ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var requiresLines = ReadBool(metadata, "requiresConstellationLines") ?? true;
        var requiresLabels = ReadBool(metadata, "requiresLabels") ?? true;
        var latitude = ReadDouble(metadata, "latitude") ?? ReadDouble(metadata, "lat") ?? 0d;
        var longitude = ReadDouble(metadata, "longitude") ?? ReadDouble(metadata, "lon") ?? ReadDouble(metadata, "lng") ?? 0d;

        return new StellariumSscModel(outputPath, eventCode, eventType, objectNames, regionId, locationName, scheduledUtc, peakUtc, captureUtc, orientation, requiresLines, requiresLabels, latitude, longitude);
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
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var flag))
            return flag;
        if (value is JsonValue textValue && textValue.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed))
            return parsed;
        return null;
    }

    private static double? ReadDouble(JsonObject metadata, string key)
    {
        if (!metadata.TryGetPropertyValue(key, out var value) || value is null)
            return null;
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<double>(out var number))
            return number;
        if (value is JsonValue textValue && textValue.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static Guid? ReadGuid(JsonObject metadata, string key)
    {
        var value = ReadString(metadata, key);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static IReadOnlyList<string> ObjectNames(AstronomyAssetProductionJob job, JsonObject metadata)
    {
        var names = ReadStringArray(metadata, "objectNames")
            .Concat(ReadStringArray(metadata, "targetObjects"))
            .Concat(ReadStringArray(metadata, "objectName"))
            .Concat(ReadStringArray(metadata, "targetObject"))
            .ToList();

        if (!string.IsNullOrWhiteSpace(job.ObjectNamesJson))
        {
            try
            {
                names.AddRange(JsonSerializer.Deserialize<IReadOnlyList<string>>(job.ObjectNamesJson, JsonOptions) ?? []);
            }
            catch (JsonException)
            {
                // Metadata object names are still usable; invalid job.ObjectNamesJson should not block non-critical SSC generation.
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

    private static string BuildFramingInstruction(IReadOnlyList<string> objectNames, string orientation)
    {
        var targets = objectNames.Count > 0 ? string.Join(", ", objectNames) : "the requested sky target";
        return $"Center {targets}, preserve surrounding constellation context, then apply orientation hint: {orientation}";
    }

    private static double ResolveFieldOfView(int objectCount, string orientation)
    {
        if (orientation.Contains("wide", StringComparison.OrdinalIgnoreCase) || objectCount > 2)
            return 65d;
        if (orientation.Contains("close", StringComparison.OrdinalIgnoreCase) || objectCount <= 1)
            return 28d;
        return 45d;
    }

    private static string? NormalizeUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
            : value.Trim();
    }

    private static string? FormatUtc(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("O");

    private static string FormatDouble(double value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string EscapeSsc(string? value)
        => (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string EscapeComment(string? value)
        => (value ?? string.Empty).ReplaceLineEndings(" ").Trim();

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

    private sealed record StellariumSscModel(
        string SscFile,
        string EventCode,
        string EventType,
        IReadOnlyList<string> ObjectNames,
        string RegionId,
        string LocationName,
        string ScheduledUtc,
        string PeakUtc,
        string CaptureUtc,
        string Orientation,
        bool RequiresConstellationLines,
        bool RequiresLabels,
        double Latitude,
        double Longitude);
}
