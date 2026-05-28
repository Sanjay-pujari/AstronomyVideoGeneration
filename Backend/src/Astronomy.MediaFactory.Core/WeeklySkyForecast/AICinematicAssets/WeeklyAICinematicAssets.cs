using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentDiversification;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;

public sealed record AICinematicAssetRequest(
    string AssetId,
    string SegmentId,
    string SegmentType,
    string EpisodeType,
    string AssetCode,
    string UsageRole,
    string EmotionalTone,
    string PacingRole,
    string StyleProfile,
    string Prompt,
    string NegativePrompt,
    int TargetWidth,
    int TargetHeight,
    string PlannedImagePath,
    bool PlaceholderAsset = false,
    int AssetPriority = 0,
    bool PacingResetCandidate = false,
    bool EmotionalResetCandidate = false,
    int SegmentOrder = int.MaxValue);

public sealed record AICinematicAssetResult(
    string AssetId,
    string SegmentId,
    string SegmentType,
    string EpisodeType,
    string AssetCode,
    string Prompt,
    string NegativePrompt,
    string StyleProfile,
    string ImagePath,
    string Source,
    string GenerationStatus,
    int Width,
    int Height,
    long FileSizeBytes,
    string ValidationStatus,
    IReadOnlyList<string> ValidationWarnings,
    string UsageRole,
    string EmotionalTone,
    string PacingRole,
    bool ProductionReady);

public sealed record AICinematicProviderResult(
    string GenerationStatus,
    string? ImagePath,
    bool ProviderConfigured,
    IReadOnlyList<string> Warnings);

public sealed record AICinematicAssetGenerationSummary(
    IReadOnlyList<AICinematicAssetRequest> Requests,
    IReadOnlyList<AICinematicAssetResult> Results,
    string PlanPath,
    string ResultsPath,
    bool GenerationReady,
    int PlannedCount,
    int GeneratedCount,
    int ProductionReadyCount,
    bool ProviderConfigured,
    int RemainingGap,
    string AzureImageDeploymentUsed,
    int DeferredCount = 0,
    bool Partial = false,
    int MaxAssetsPerRun = 0);

public interface IAICinematicImageGenerator
{
    bool IsConfigured { get; }
    string DeploymentName { get; }
    Task<AICinematicProviderResult> GenerateAsync(AICinematicAssetRequest request, CancellationToken cancellationToken);
}

public sealed class DisabledAICinematicImageGenerator : IAICinematicImageGenerator
{
    public bool IsConfigured => false;

    public string DeploymentName => string.Empty;

    public Task<AICinematicProviderResult> GenerateAsync(AICinematicAssetRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AICinematicProviderResult(
            "ProviderNotConfigured",
            null,
            ProviderConfigured: false,
            ["AI cinematic image generation provider is not configured; no production image was created."]));
}

public sealed class AICinematicStylePolicy
{
    public (string StyleProfile, string NegativePrompt) Resolve(string segmentType, string usageRole)
    {
        var style = segmentType switch
        {
            "OpeningHook" or "ShortHook" => "cinematic_wide_night_sky_reveal",
            "WeeklySummary" or "CallToAction" => "warm_cosmic_closing_backdrop",
            "AstrophotographyTip" => "observational_telescope_silhouette",
            _ when usageRole.Contains("reset", StringComparison.OrdinalIgnoreCase) => "calm_cosmic_pacing_reset",
            _ => "educational_astronomy_safe_cinematic_backdrop"
        };

        const string negative = "No exact star maps, no false astronomical labels, no fake rare conjunctions, no scientific diagrams, no misleading object alignments, no text overlays, no logos, no thumbnails, no video frames.";
        return (style, negative);
    }
}

public sealed class AICinematicPromptBuilder(AICinematicStylePolicy stylePolicy, ILogger<AICinematicPromptBuilder> logger)
{
    public AICinematicAssetRequest Build(
        SegmentVisualAssetPlan segment,
        string episodeType,
        string assetCode,
        string usageRole,
        string regionId,
        string language,
        DateOnly weekStartDate,
        int segmentOrder)
    {
        var (style, negativePrompt) = stylePolicy.Resolve(segment.SegmentType, usageRole);
        var emotion = ResolveEmotion(segment);
        var pacing = ResolvePacing(segment);
        var prompt = string.Join(' ', new[]
        {
            "Create a production-ready cinematic astronomy still image, 16:9 landscape.",
            $"Segment type: {segment.SegmentType}; usage role: {usageRole}; emotional tone: {emotion}; pacing role: {pacing}.",
            $"Region context: {regionId}; language context: {language}; forecast week starts {weekStartDate:yyyy-MM-dd}.",
            ResolveSceneDescription(assetCode, segment.SegmentType),
            "Use astronomy-safe atmosphere only: cinematic night sky, Milky Way atmosphere, cosmic wonder, atmospheric horizon, gentle starlight, telescope silhouette, or abstract educational space backdrop.",
            "Do not claim exact real alignments, do not render a sky map, do not add object labels, and do not imply a rare event unless supplied by verified astronomy data."
        });

        logger.LogInformation("AI_CINEMATIC_PROMPT_BUILT segmentId={SegmentId} assetCode={AssetCode} styleProfile={StyleProfile}", segment.SegmentId, assetCode, style);
        return new AICinematicAssetRequest(
            AssetId: $"{episodeType}_{segment.SegmentId}_{assetCode}".ToLowerInvariant(),
            segment.SegmentId,
            segment.SegmentType,
            episodeType,
            assetCode,
            usageRole,
            emotion,
            pacing,
            style,
            prompt,
            negativePrompt,
            TargetWidth: 1920,
            TargetHeight: 1080,
            PlannedImagePath: string.Empty,
            AssetPriority: ResolveAssetPriority(segment, assetCode),
            PacingResetCandidate: segment.RetentionMetadata.PacingResetCandidate,
            EmotionalResetCandidate: segment.RetentionMetadata.EmotionalResetCandidate,
            SegmentOrder: segmentOrder);
    }

    private static int ResolveAssetPriority(SegmentVisualAssetPlan segment, string assetCode)
    {
        var priority = segment.AssetPriority;
        if (IsInitialHotfixAsset(segment.SegmentType, assetCode)) priority += 10_000;
        return priority;
    }

    private static bool IsInitialHotfixAsset(string segmentType, string assetCode) =>
        (segmentType.Equals("OpeningHook", StringComparison.OrdinalIgnoreCase) && assetCode.Equals("cinematic_weekly_sky_reveal", StringComparison.OrdinalIgnoreCase))
        || (segmentType.Equals("WeeklySummary", StringComparison.OrdinalIgnoreCase) && assetCode.Equals("cosmic_closing_background", StringComparison.OrdinalIgnoreCase))
        || (segmentType.Equals("ShortHook", StringComparison.OrdinalIgnoreCase) && assetCode.Equals("fast_cinematic_sky_hook", StringComparison.OrdinalIgnoreCase));

    private static string ResolveEmotion(SegmentVisualAssetPlan segment) =>
        segment.SourcePlans.FirstOrDefault(x => x.SourceType == VisualAssetSourceType.AICinematic)?.EmotionalTone
        ?? (segment.SegmentType is "OpeningHook" or "ShortHook" ? "awe and anticipation" : segment.SegmentType is "WeeklySummary" or "CallToAction" ? "hopeful wonder" : "calm cosmic wonder");

    private static string ResolvePacing(SegmentVisualAssetPlan segment) =>
        segment.RetentionMetadata.PacingResetCandidate ? "pacing_reset" : segment.RetentionMetadata.EmotionalResetCandidate ? "emotional_reset" : "cinematic_support";

    private static string ResolveSceneDescription(string assetCode, string segmentType) => assetCode switch
    {
        "cinematic_weekly_sky_reveal" => "A sweeping wide horizon under a deepening night sky with subtle Milky Way atmosphere and a sense of weekly discovery.",
        "atmospheric_horizon_wonder" => "A quiet observing landscape with atmospheric horizon glow, gentle stars, and room for narration-safe composition.",
        "cosmic_closing_background" => "A warm cosmic closing background with peaceful starlight and subtle depth, designed for recap narration.",
        "weekly_recap_cinematic_sky" => "A calm cinematic sky backdrop that feels reflective and educational without specific event claims.",
        "fast_cinematic_sky_hook" => "A high-impact night-sky hook image with dynamic sky contrast and no labels or fake sky-map elements.",
        "subscribe_observe_sky_background" => "An inviting telescope-silhouette night landscape for a call-to-action background, clean and non-thumbnail-like.",
        "cosmic_breathing_space" => "A serene cosmic breathing-space backdrop with soft star fields and broad negative space for visual pacing.",
        "deep_space_scale_transition" => "An abstract deep-space scale transition showing depth and wonder without identifying exact objects or alignments.",
        _ => $"A cinematic astronomy-safe still image for {segmentType}, focused on mood, observation, and cosmic wonder without false specificity."
    };
}

public sealed class AICinematicAssetPersister
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };

    public string ResolveImagePath(string workingDirectoryRoot, AICinematicAssetRequest request)
    {
        var safeEpisodeType = SanitizePathPart(request.EpisodeType);
        var safeSegmentType = SanitizePathPart(ToSnakeCase(request.SegmentType));
        var safeAssetCode = SanitizePathPart(request.AssetCode);
        return Path.Combine(workingDirectoryRoot, "ai-cinematic", safeEpisodeType, safeSegmentType, safeAssetCode + ".png");
    }

    public async Task<(string PlanPath, string ResultsPath)> WriteAsync(string workingDirectoryRoot, IReadOnlyList<AICinematicAssetRequest> requests, IReadOnlyList<AICinematicAssetResult> results, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(workingDirectoryRoot, "ai-cinematic", "longform"));
        Directory.CreateDirectory(Path.Combine(workingDirectoryRoot, "ai-cinematic", "shortform"));
        Directory.CreateDirectory(Path.Combine(workingDirectoryRoot, "ai-cinematic", "common"));
        var episodeDirectory = Path.Combine(workingDirectoryRoot, "episode");
        Directory.CreateDirectory(episodeDirectory);
        var planPath = Path.Combine(episodeDirectory, "ai-cinematic-asset-plan.json");
        var resultsPath = Path.Combine(episodeDirectory, "ai-cinematic-asset-results.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(requests, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(resultsPath, JsonSerializer.Serialize(results, JsonOptions), cancellationToken);
        return (planPath, resultsPath);
    }

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "asset" : cleaned.Trim().ToLowerInvariant();
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "segment";
        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch) && i > 0) chars.Add('_');
            chars.Add(char.ToLowerInvariant(ch));
        }
        return new string(chars.ToArray());
    }
}

public sealed class AICinematicAssetValidator
{
    public AICinematicAssetResult Validate(AICinematicAssetRequest request, AICinematicProviderResult providerResult)
    {
        var warnings = new List<string>(providerResult.Warnings ?? []);
        var imagePath = providerResult.ImagePath ?? request.PlannedImagePath;
        var width = 0;
        var height = 0;
        var fileSizeBytes = 0L;
        var validationStatus = "Failed";

        if (string.IsNullOrWhiteSpace(providerResult.ImagePath) || !File.Exists(providerResult.ImagePath))
        {
            warnings.Add("Generated file does not exist because no provider output was returned.");
        }
        else
        {
            var info = new FileInfo(providerResult.ImagePath);
            fileSizeBytes = info.Length;
            if (fileSizeBytes <= 0) warnings.Add("Generated file is empty.");
            if (fileSizeBytes <= 50 * 1024) warnings.Add("Generated file is below the 50 KB production threshold.");

            if (!TryReadImageDimensions(providerResult.ImagePath, out width, out height, out var format))
            {
                warnings.Add("Generated file is not a readable PNG or JPEG image.");
            }
            else
            {
                if (format is not ("PNG" or "JPEG")) warnings.Add("Generated file format is not PNG or JPEG.");
                if (width < 1024) warnings.Add("Generated image width is below 1024 pixels.");
                if (height < 720) warnings.Add("Generated image height is below 720 pixels.");
            }
        }

        if (request.PlaceholderAsset)
            warnings.Add("Placeholder assets cannot be marked production-ready.");

        if (!string.IsNullOrWhiteSpace(providerResult.ImagePath))
        {
            var extension = Path.GetExtension(providerResult.ImagePath);
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".PNG" or ".JPG" or ".JPEG"))
                warnings.Add("Generated file extension is not PNG or JPEG.");
        }

        var productionReady = providerResult.ProviderConfigured
            && providerResult.GenerationStatus.Equals("Generated", StringComparison.OrdinalIgnoreCase)
            && !request.PlaceholderAsset
            && fileSizeBytes > 50 * 1024
            && width >= 1024
            && height >= 720
            && warnings.Count == 0;

        if (productionReady) validationStatus = "Passed";
        else if (!providerResult.ProviderConfigured) validationStatus = "ProviderNotConfigured";

        var generationStatus = providerResult.GenerationStatus;
        if (providerResult.GenerationStatus.Equals("Generated", StringComparison.OrdinalIgnoreCase) && !productionReady)
        {
            generationStatus = "GeneratedButInvalid";
        }

        return new AICinematicAssetResult(
            request.AssetId,
            request.SegmentId,
            request.SegmentType,
            request.EpisodeType,
            request.AssetCode,
            request.Prompt,
            request.NegativePrompt,
            request.StyleProfile,
            imagePath,
            "AICinematic",
            generationStatus,
            width,
            height,
            fileSizeBytes,
            validationStatus,
            warnings,
            request.UsageRole,
            request.EmotionalTone,
            request.PacingRole,
            productionReady);
    }

    private static bool TryReadImageDimensions(string path, out int width, out int height, out string format)
    {
        width = 0;
        height = 0;
        format = string.Empty;
        Span<byte> header = stackalloc byte[32];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header);
        if (read >= 24 && header[0] == 0x89 && header[1] == (byte)'P' && header[2] == (byte)'N' && header[3] == (byte)'G')
        {
            width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));
            format = "PNG";
            return width > 0 && height > 0;
        }

        if (read >= 2 && header[0] == 0xFF && header[1] == 0xD8)
        {
            stream.Position = 2;
            while (stream.Position < stream.Length)
            {
                if (stream.ReadByte() != 0xFF) continue;
                int marker;
                do { marker = stream.ReadByte(); } while (marker == 0xFF);
                if (marker < 0) break;
                Span<byte> lengthBytes = stackalloc byte[2];
                if (stream.Read(lengthBytes) != 2) break;
                var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
                if (length < 2) break;
                if (marker is >= 0xC0 and <= 0xC3)
                {
                    _ = stream.ReadByte();
                    Span<byte> sizeBytes = stackalloc byte[4];
                    if (stream.Read(sizeBytes) != 4) break;
                    height = BinaryPrimitives.ReadUInt16BigEndian(sizeBytes.Slice(0, 2));
                    width = BinaryPrimitives.ReadUInt16BigEndian(sizeBytes.Slice(2, 2));
                    format = "JPEG";
                    return width > 0 && height > 0;
                }
                stream.Position += length - 2;
            }
        }

        return false;
    }
}

public sealed class WeeklyAICinematicAssetGenerationService(
    AICinematicPromptBuilder promptBuilder,
    AICinematicAssetPersister persister,
    AICinematicAssetValidator validator,
    IAICinematicImageGenerator generator,
    IOptions<WeeklySkyForecastAICinematicAssetsOptions> options,
    ILogger<WeeklyAICinematicAssetGenerationService> logger)
{
    public async Task<AICinematicAssetGenerationSummary> GenerateAndPersistAsync(
        WeeklyVisualAssetPlan visualAssetPlan,
        WeeklyVisualBalanceReport visualBalanceReport,
        WeeklySegmentDiversificationPlan diversificationPlan,
        WeeklyEpisodeArchitectureResult episodeArchitecture,
        object? weeklyContext,
        string workingDirectoryRoot,
        CancellationToken cancellationToken,
        bool continueOnFailure = true)
    {
        logger.LogInformation("AI_CINEMATIC_ASSET_GENERATION_START pipelineRunId={PipelineRunId}", visualAssetPlan.PipelineRunId);
        _ = diversificationPlan;
        _ = episodeArchitecture;
        _ = weeklyContext;

        var aiOptions = options.Value;
        var continueAfterFailure = continueOnFailure && aiOptions.ContinueOnFailure;
        var maxAssetsPerRun = Math.Max(0, aiOptions.MaxAssetsPerRun);
        var requests = BuildRequests(visualAssetPlan)
            .OrderByDescending(request => request.AssetPriority)
            .ThenByDescending(request => request.PacingResetCandidate)
            .ThenByDescending(request => request.EmotionalResetCandidate)
            .ThenBy(request => request.SegmentOrder)
            .ToList();
        logger.LogInformation(
            "AI_CINEMATIC_PROVIDER_STATUS configured={ProviderConfigured} plannedCount={PlannedCount} maxAssetsPerRun={MaxAssetsPerRun}",
            generator.IsConfigured,
            requests.Count,
            maxAssetsPerRun);

        var materializedRequests = requests
            .Select(request => request with { PlannedImagePath = persister.ResolveImagePath(workingDirectoryRoot, request) })
            .ToList();

        var activeRequests = aiOptions.Enabled
            ? materializedRequests.Take(maxAssetsPerRun).ToList()
            : new List<AICinematicAssetRequest>();
        var deferredRequests = materializedRequests.Skip(activeRequests.Count).ToList();
        if (!aiOptions.Enabled)
        {
            logger.LogInformation("AI_CINEMATIC_ASSET_GENERATION_DISABLED plannedCount={PlannedCount}", materializedRequests.Count);
        }

        var results = new List<AICinematicAssetResult>();
        var generationPartial = deferredRequests.Count > 0 || !aiOptions.Enabled;
        AICinematicAssetRequest? timedOutRequest = null;
        foreach (var request in activeRequests)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                logger.LogInformation("AI_CINEMATIC_ASSET_REQUEST_CREATED assetId={AssetId} segmentId={SegmentId} assetCode={AssetCode} assetPriority={AssetPriority} segmentOrder={SegmentOrder}", request.AssetId, request.SegmentId, request.AssetCode, request.AssetPriority, request.SegmentOrder);
                var targetDirectory = Path.GetDirectoryName(request.PlannedImagePath);
                if (!string.IsNullOrWhiteSpace(targetDirectory)) Directory.CreateDirectory(targetDirectory);
                AICinematicProviderResult providerResult;
                try
                {
                    providerResult = await generator.GenerateAsync(request, cancellationToken);
                }
                catch (OperationCanceledException) when (continueAfterFailure)
                {
                    timedOutRequest = request;
                    generationPartial = true;
                    logger.LogWarning("AI_CINEMATIC_ASSET_GENERATION_TIMED_OUT_CONTINUING assetId={AssetId} segmentType={SegmentType} assetCode={AssetCode}", request.AssetId, request.SegmentType, request.AssetCode);
                    break;
                }
                catch (Exception ex) when (continueAfterFailure)
                {
                    logger.LogError(ex, "AI_CINEMATIC_ASSET_GENERATION_FAILED_CONTINUING assetId={AssetId} segmentType={SegmentType} assetCode={AssetCode}", request.AssetId, request.SegmentType, request.AssetCode);
                    providerResult = new AICinematicProviderResult(
                        "Failed",
                        null,
                        generator.IsConfigured,
                        [$"Unexpected AI cinematic image generation failure: {ex.Message}"]);
                }

                logger.LogInformation("AI_CINEMATIC_ASSET_GENERATED assetId={AssetId} status={GenerationStatus}", request.AssetId, providerResult.GenerationStatus);
                var result = validator.Validate(request, providerResult);
                LogValidation(request, result);
                results.Add(result);
            }
            catch (OperationCanceledException) when (continueAfterFailure)
            {
                timedOutRequest = request;
                generationPartial = true;
                logger.LogWarning("AI_CINEMATIC_ASSET_STAGE_CANCELLED_PARTIAL_RESULTS assetId={AssetId} segmentType={SegmentType} assetCode={AssetCode}", request.AssetId, request.SegmentType, request.AssetCode);
                break;
            }
        }

        if (timedOutRequest is not null)
        {
            var alreadyResultAssetIds = results.Select(x => x.AssetId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            deferredRequests = activeRequests
                .SkipWhile(request => !ReferenceEquals(request, timedOutRequest))
                .Concat(deferredRequests)
                .Where(request => !alreadyResultAssetIds.Contains(request.AssetId))
                .ToList();
        }

        results.AddRange(deferredRequests.Select(request => CreateDeferredResult(
            request,
            timedOutRequest is not null
                ? "Deferred because AI cinematic stage timeout was reached before this asset could be generated."
                : aiOptions.Enabled
                    ? $"Deferred by WeeklySkyForecast:AICinematicAssets:MaxAssetsPerRun={maxAssetsPerRun}."
                    : "Deferred because WeeklySkyForecast:AICinematicAssets:Enabled=false.")));

        var writeToken = cancellationToken.IsCancellationRequested && continueAfterFailure ? CancellationToken.None : cancellationToken;
        var (planPath, resultsPath) = await persister.WriteAsync(workingDirectoryRoot, materializedRequests, results, writeToken);
        logger.LogInformation("AI_CINEMATIC_ASSET_PLAN_WRITTEN path={PlanPath}", planPath);
        logger.LogInformation("AI_CINEMATIC_ASSET_RESULTS_WRITTEN path={ResultsPath}", resultsPath);
        var generatedCount = results.Count(IsGeneratedStatus);
        var productionReadyCount = results.Count(x => x.ProductionReady);
        var deferredCount = results.Count(IsDeferredStatus);
        await UpdateVisualBalanceReportAsync(visualAssetPlan, visualBalanceReport, workingDirectoryRoot, materializedRequests.Count, generatedCount, productionReadyCount, writeToken);

        var summary = new AICinematicAssetGenerationSummary(
            materializedRequests,
            results,
            planPath,
            resultsPath,
            GenerationReady: true,
            PlannedCount: materializedRequests.Count,
            GeneratedCount: generatedCount,
            ProductionReadyCount: productionReadyCount,
            ProviderConfigured: generator.IsConfigured,
            RemainingGap: materializedRequests.Count - productionReadyCount,
            AzureImageDeploymentUsed: generator.DeploymentName,
            DeferredCount: deferredCount,
            Partial: generationPartial || deferredCount > 0,
            MaxAssetsPerRun: maxAssetsPerRun);
        logger.LogInformation("AI_CINEMATIC_ASSET_GENERATION_COMPLETE planned={PlannedCount} generated={GeneratedCount} deferred={DeferredCount} productionReady={ProductionReadyCount} partial={Partial}", summary.PlannedCount, summary.GeneratedCount, summary.DeferredCount, summary.ProductionReadyCount, summary.Partial);
        return summary;
    }

    private IEnumerable<AICinematicAssetRequest> BuildRequests(WeeklyVisualAssetPlan plan)
    {
        var segmentOrder = 0;
        foreach (var segment in plan.LongformSegmentVisualPlans)
        {
            foreach (var item in BuildSegmentRequests(segment, "longform", plan, segmentOrder))
                yield return item;
            segmentOrder++;
        }

        foreach (var segment in plan.ShortformSegmentVisualPlans)
        {
            foreach (var item in BuildSegmentRequests(segment, "shortform", plan, segmentOrder))
                yield return item;
            segmentOrder++;
        }
    }

    private IEnumerable<AICinematicAssetRequest> BuildSegmentRequests(SegmentVisualAssetPlan segment, string episodeType, WeeklyVisualAssetPlan plan, int segmentOrder)
    {
        if (!ShouldGenerateForSegment(segment)) yield break;

        var assets = ResolveAssetCodes(segment).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var assetCode in assets)
        {
            yield return promptBuilder.Build(segment, episodeType, assetCode, ResolveUsageRole(segment, assetCode), plan.RegionId, plan.Language, plan.WeekStartDate, segmentOrder);
        }
    }


    private void LogValidation(AICinematicAssetRequest request, AICinematicAssetResult result)
    {
        if (result.ValidationStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "AI_IMAGE_VALIDATION_PASSED deployment={Deployment} assetCode={AssetCode} segmentType={SegmentType} plannedImagePath={PlannedImagePath} generationStatus={GenerationStatus} validationStatus={ValidationStatus}",
                generator.DeploymentName,
                request.AssetCode,
                request.SegmentType,
                request.PlannedImagePath,
                result.GenerationStatus,
                result.ValidationStatus);
        }
        else
        {
            logger.LogWarning(
                "AI_IMAGE_VALIDATION_FAILED deployment={Deployment} assetCode={AssetCode} segmentType={SegmentType} plannedImagePath={PlannedImagePath} generationStatus={GenerationStatus} validationStatus={ValidationStatus} warnings={Warnings}",
                generator.DeploymentName,
                request.AssetCode,
                request.SegmentType,
                request.PlannedImagePath,
                result.GenerationStatus,
                result.ValidationStatus,
                string.Join(" | ", result.ValidationWarnings));
        }

        logger.LogInformation("AI_CINEMATIC_ASSET_VALIDATED assetId={AssetId} validationStatus={ValidationStatus} productionReady={ProductionReady}", result.AssetId, result.ValidationStatus, result.ProductionReady);
    }

    private static AICinematicAssetResult CreateDeferredResult(AICinematicAssetRequest request, string reason) => new(
        request.AssetId,
        request.SegmentId,
        request.SegmentType,
        request.EpisodeType,
        request.AssetCode,
        request.Prompt,
        request.NegativePrompt,
        request.StyleProfile,
        request.PlannedImagePath,
        "AzureOpenAI",
        "Deferred",
        0,
        0,
        0,
        "Deferred",
        [reason],
        request.UsageRole,
        request.EmotionalTone,
        request.PacingRole,
        ProductionReady: false);

    private static bool ShouldGenerateForSegment(SegmentVisualAssetPlan segment) =>
        segment.PrimaryVisualSource == VisualAssetSourceType.AICinematic
        || segment.SecondaryVisualSource == VisualAssetSourceType.AICinematic
        || segment.RequiredVisualAssets.Any(x => x.PreferredSource == VisualAssetSourceType.AICinematic || x.AssetCategory.Contains("cinematic", StringComparison.OrdinalIgnoreCase) || x.RequirementId.Contains("cinematic", StringComparison.OrdinalIgnoreCase))
        || segment.RetentionMetadata.PacingResetCandidate
        || segment.RetentionMetadata.EmotionalResetCandidate;

    private static IEnumerable<string> ResolveAssetCodes(SegmentVisualAssetPlan segment)
    {
        foreach (var code in DefaultAssetCodes(segment.SegmentType)) yield return code;
        foreach (var requirement in segment.RequiredVisualAssets.Where(x => x.PreferredSource == VisualAssetSourceType.AICinematic))
            yield return NormalizeAssetCode(requirement.AssetCategory);
        if (segment.RetentionMetadata.PacingResetCandidate) yield return "cosmic_breathing_space";
        if (segment.RetentionMetadata.EmotionalResetCandidate) yield return "deep_space_scale_transition";
    }

    private static IReadOnlyList<string> DefaultAssetCodes(string segmentType) => segmentType switch
    {
        "OpeningHook" => ["cinematic_weekly_sky_reveal", "atmospheric_horizon_wonder"],
        "WeeklySummary" => ["cosmic_closing_background", "weekly_recap_cinematic_sky"],
        "ShortHook" => ["fast_cinematic_sky_hook"],
        "CallToAction" => ["subscribe_observe_sky_background"],
        _ => []
    };

    private static string ResolveUsageRole(SegmentVisualAssetPlan segment, string assetCode)
    {
        if (assetCode.Contains("breathing", StringComparison.OrdinalIgnoreCase) || assetCode.Contains("transition", StringComparison.OrdinalIgnoreCase)) return "pacing_reset";
        return segment.RequiredVisualAssets.FirstOrDefault(x => x.PreferredSource == VisualAssetSourceType.AICinematic)?.UsageRole
            ?? segment.SourcePlans.FirstOrDefault(x => x.SourceType == VisualAssetSourceType.AICinematic)?.UsageRole
            ?? "cinematic_segment_support";
    }

    private static string NormalizeAssetCode(string value)
    {
        var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray();
        var normalized = string.Join('_', new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "ai_cinematic_asset" : normalized;
    }

    private static bool IsGeneratedStatus(AICinematicAssetResult result) =>
        result.GenerationStatus.Equals("Generated", StringComparison.OrdinalIgnoreCase)
        || result.GenerationStatus.Equals("GeneratedButInvalid", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeferredStatus(AICinematicAssetResult result) =>
        result.GenerationStatus.Equals("Deferred", StringComparison.OrdinalIgnoreCase);

    private static async Task UpdateVisualBalanceReportAsync(WeeklyVisualAssetPlan plan, WeeklyVisualBalanceReport originalBalanceReport, string workingDirectoryRoot, int planned, int generated, int productionReady, CancellationToken cancellationToken)
    {
        var balancePath = Path.Combine(workingDirectoryRoot, "episode", "weekly-visual-balance-report.json");
        if (!File.Exists(balancePath)) return;

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(balancePath, cancellationToken));
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            data[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText());
        }

        data["aiCinematicAssetsPlanned"] = planned;
        data["aiCinematicAssetsGenerated"] = generated;
        data["aiCinematicProductionReadyCount"] = productionReady;
        data["remainingAICinematicGap"] = Math.Max(0, planned - productionReady);
        var allPlannedAssetsProductionReady = planned == 0 || productionReady >= planned;
        data["visualBalanceAfterAICinematicAssets"] = productionReady > 0 && plan.PlannedAICinematicCount > 0 ? "ImprovedWithProductionReadyAssets" : "PlanningOnlyProviderNotConfigured";
        data["visualBalanceHealthy"] = originalBalanceReport.VisualBalanceHealthy && allPlannedAssetsProductionReady && generated >= planned;
        await File.WriteAllTextAsync(balancePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
