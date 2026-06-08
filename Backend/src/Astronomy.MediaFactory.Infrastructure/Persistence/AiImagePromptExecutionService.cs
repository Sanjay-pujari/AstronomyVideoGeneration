using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AiImagePromptExecutionService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<AiImagePromptExecutionService> logger) : IAiImagePromptExecutionService
{
    private const string AiHeroImage = "AiHeroImage";
    private const string AiCinematicImage = "AiCinematicImage";
    private const string PromptDirectory = "ai-image-prompts";
    private const string GenerationSource = "Phase8F.1";

    private static readonly string[] SupportedAssetTypes = [AiHeroImage, AiCinematicImage];

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

        var supported = SupportedAssetTypes.Select(NormalizeAssetType).ToHashSet(StringComparer.Ordinal);
        var requestedAssetTypes = ToSet(request.AssetTypes);
        var selectedAssetTypes = requestedAssetTypes is { Count: > 0 }
            ? requestedAssetTypes.Where(supported.Contains).ToHashSet(StringComparer.Ordinal)
            : supported;

        if (requestedAssetTypes is { Count: > 0 })
        {
            foreach (var assetType in requestedAssetTypes.Where(t => !supported.Contains(t)))
                warnings.Add($"Skipped unsupported AI image prompt asset type '{assetType}'. Phase 8F.1 supports AiHeroImage and AiCinematicImage only.");
        }

        if (selectedAssetTypes.Count == 0)
        {
            warnings.Add("No supported AI image prompt asset type was requested; supported types are AiHeroImage and AiCinematicImage.");
            return new AssetExecutionResult(0, 0, 0, 0, [], warnings);
        }

        if (request.EnableExternalGeneration)
            warnings.Add("External AI image generation is disabled for Phase 8F.1; prompt JSON packages only were prepared.");

        var query = db.AstronomyAssetProductionJobs
            .Include(j => j.ContentGenerationPlan)
            .Where(j => j.Status == AstronomyAssetProductionJobStatuses.Pending)
            .Where(j => selectedAssetTypes.Contains(j.AssetType.ToLower()))
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
                warnings.Add($"Skipped duplicate {job.AssetType} job '{job.Id}' because an output already exists. Set overwriteExisting=true to regenerate it.");
                continue;
            }

            job.StartedUtc = DateTimeOffset.UtcNow;
            job.FailureReason = null;

            try
            {
                var promptJson = BuildPromptJson(job, request.RegionId);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ResolveWorkingDirectoryRoot());
                await File.WriteAllTextAsync(outputPath, promptJson, cancellationToken);

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
                warnings.Add($"{job.AssetType} job '{job.Id}' failed to generate prompt JSON: {ex.Message}");
                logger.LogWarning(ex, "AI image prompt job {JobId} failed to generate prompt JSON", job.Id);
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
        var fileName = $"ai-image-prompt-scene-{job.SceneNumber}-{SanitizeFileName(job.AssetType)}-{job.Id:D}.json";

        return Path.Combine(ResolveWorkingDirectoryRoot(), "assets", regionId, "events", eventIntelligenceId.ToString("D"), PromptDirectory, fileName);
    }

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory)
            ? "./media-output"
            : renderingOptions.Value.WorkingDirectory;

    private static string BuildPromptJson(AstronomyAssetProductionJob job, string? requestedRegionId)
    {
        var metadata = ParseMetadata(job.MetadataJson);
        var objectNames = ObjectNames(job);
        var objectNameList = ObjectNameList(objectNames);
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
        var aspectRatio = ResolveAspectRatio(job, metadata);
        var style = ReadString(metadata, "style") ?? DefaultStyle(job.AssetType);
        var basePrompt = ReadString(metadata, "imagePrompt")
            ?? ReadString(metadata, "prompt")
            ?? job.PromptOrInstruction
            ?? BuildDefaultBasePrompt(job, objectNameList, eventType, locationName);

        var professionalPrompt = BuildProfessionalPrompt(job, objectNameList, eventType, eventCode, locationName, aspectRatio, style, basePrompt);

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
            ["assetType"] = job.AssetType,
            ["objectNames"] = objectNames,
            ["aspectRatio"] = aspectRatio,
            ["style"] = style,
            ["basePrompt"] = basePrompt,
            ["professionalPrompt"] = professionalPrompt,
            ["negativePrompt"] = "blurry, low resolution, cartoonish, overexposed, distorted planets, fake text, watermark, extra moons, wrong number of planets, cluttered composition, visible horizontal banding, layered strip gradients, stacked rectangular gradient regions",
            ["compositionGuide"] = BuildCompositionGuide(job.AssetType, aspectRatio),
            ["lightingGuide"] = BuildLightingGuide(job.AssetType, objectNameList),
            ["qualityChecklist"] = BuildQualityChecklist(job.AssetType, aspectRatio),
            ["safetyNotes"] = BuildSafetyNotes(job.AssetType),
            ["generationSource"] = GenerationSource,
            ["generatedUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };

        return JsonSerializer.Serialize(output, JsonOptions);
    }

    private static string BuildProfessionalPrompt(AstronomyAssetProductionJob job, IReadOnlyList<string> objectNames, string eventType, string eventCode, string locationName, string aspectRatio, string style, string basePrompt)
    {
        var objects = objectNames.Count > 0 ? string.Join(" and ", objectNames) : "the featured celestial event";
        var assetIntent = job.AssetType.Equals(AiHeroImage, StringComparison.OrdinalIgnoreCase)
            ? "a high-retention opening visual with a dramatic, clean focal point optimized for a thumbnail or vertical short hook"
            : "an educational cinematic illustration for an explanation or transition scene, calmer than a hero image and documentary-grade";
        var safeZones = aspectRatio == "9:16"
            ? "Maintain clean upper and lower caption-safe zones with negative space for short-form text overlays."
            : "Use cinematic wide composition with clean negative space for title or explanatory text overlays.";

        return string.Join(" ",
            $"Create {assetIntent} in a professional documentary astronomy style.",
            $"Subject: {objects}; event context: {NonBlank(eventType, "astronomical event")} {NonBlank(eventCode, string.Empty)} near {NonBlank(locationName, "the observing region")}.",
            $"Base creative direction: {basePrompt}",
            $"Aspect ratio {aspectRatio}; style: {style}.",
            "Compose with clear foreground, midground, and background layers, a strong visual hierarchy, and premium cinematic depth rather than a screenshot or slideshow frame.",
            "Keep celestial bodies at realistic apparent scale; if enlarged for storytelling, make it a cinematic artistic interpretation that remains scientifically honest and not misleading.",
            safeZones,
            "Use a smooth continuous sky gradient with atmospheric scattering, subtle twilight haze, and natural sky blending.",
            "Do not create visible horizontal bands, layered strip gradients, or stacked rectangular gradient regions.",
            "Do not include fake UI, labels, watermarks, logos, or unreadable text inside the image.").Trim();
    }

    private static string BuildCompositionGuide(string assetType, string aspectRatio)
    {
        var opening = assetType.Equals(AiHeroImage, StringComparison.OrdinalIgnoreCase)
            ? "Use a bold single focal point with high contrast and immediate emotional readability."
            : "Use a calmer educational focal point with room for explanatory pacing.";
        var frame = aspectRatio == "9:16"
            ? "Foreground silhouette or landscape anchor in the lower third, celestial subject in the middle third, deep sky background above; reserve top and bottom safe zones for captions."
            : "Wide foreground horizon or observing context, midground atmosphere, and background sky with the main celestial relationship placed on a cinematic third; preserve negative space for text.";
        return $"{opening} {frame} Avoid clutter and avoid flat planet lineup slideshow styling.";
    }

    private static string BuildLightingGuide(string assetType, IReadOnlyList<string> objectNames)
    {
        var mood = assetType.Equals(AiHeroImage, StringComparison.OrdinalIgnoreCase)
            ? "dramatic twilight or predawn rim light, premium contrast, controlled glow"
            : "soft documentary twilight, natural atmospheric gradients, restrained glow";
        var objects = objectNames.Count > 0 ? string.Join(", ", objectNames) : "the celestial objects";
        return $"Use {mood}; keep {objects} legible without overexposure, with realistic atmospheric scattering, subtle twilight haze, smooth continuous gradients, and a natural night-sky color palette; visibleHorizontalBanding=false and naturalSkyGradient=true.";
    }

    private static JsonArray BuildQualityChecklist(string assetType, string aspectRatio) => new()
    {
        "Cinematic and premium, not a simple screenshot or slideshow card.",
        "Foreground, midground, and background are clearly composed.",
        "Realistic celestial scale is preserved unless intentionally symbolic/cinematic.",
        "Visual hierarchy reads instantly on mobile and thumbnail surfaces.",
        aspectRatio == "9:16" ? "Upper and lower safe zones are clean for captions." : "Wide 16:9 frame includes clean negative space for text.",
        assetType.Equals(AiHeroImage, StringComparison.OrdinalIgnoreCase) ? "Hero image has a strong high-retention focal point." : "Cinematic image is calm, educational, and documentary-grade.",
        "No fake UI, no fake text, no watermark, and no unreadable lettering.",
        "Sky rendering validation: visibleHorizontalBanding=false and naturalSkyGradient=true."
    };

    private static JsonArray BuildSafetyNotes(string assetType) => new()
    {
        "Phase 8F.1 prepares prompt JSON only; no AI image provider is called.",
        "Do not imply scientifically impossible planet sizes unless marked as symbolic or cinematic artistic interpretation.",
        "Avoid misleading astronomy visuals such as extra moons, wrong planet counts, or distorted planets.",
        assetType.Equals(AiHeroImage, StringComparison.OrdinalIgnoreCase)
            ? "Dramatic hook visuals must remain clean and astronomically responsible."
            : "Educational transition visuals must prioritize clarity and documentary authenticity."
    };

    private static string ResolveAspectRatio(AstronomyAssetProductionJob job, JsonObject metadata)
    {
        var aspectRatio = ReadString(metadata, "aspectRatio");
        if (aspectRatio is "9:16" or "16:9")
            return aspectRatio;

        return job.ContentGenerationPlan?.PlannedFormat?.Contains("Short", StringComparison.OrdinalIgnoreCase) == true ? "9:16" : "16:9";
    }

    private static string DefaultStyle(string assetType)
        => assetType.Equals(AiHeroImage, StringComparison.OrdinalIgnoreCase)
            ? "high-retention cinematic astronomy hero image"
            : "cinematic educational astronomy illustration";

    private static string BuildDefaultBasePrompt(AstronomyAssetProductionJob job, IReadOnlyList<string> objectNames, string eventType, string locationName)
    {
        var objects = objectNames.Count > 0 ? string.Join(", ", objectNames) : NonBlank(eventType, "night sky event");
        return $"Cinematic artistic interpretation of {objects} over {NonBlank(locationName, "the observing location")} for scene '{job.SceneName}'.";
    }

    private static string NonBlank(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

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

    private static IReadOnlyList<string> ObjectNameList(JsonArray objectNames)
        => objectNames
            .Select(node => node?.GetValue<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

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

    private static string SanitizeFileName(string value)
        => SanitizePathSegment(value) ?? "AiImage";
}
