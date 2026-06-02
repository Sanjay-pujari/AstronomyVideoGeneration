using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;

public sealed record WeeklyNormalizedVisualAsset(
    string AssetId,
    string Path,
    string Source,
    string SourceType,
    string SceneCode,
    string SourceSceneCode,
    string ParentSceneCode,
    string OriginalSceneCode,
    string FrameType,
    IReadOnlyList<string> TargetObjects,
    IReadOnlyList<string> RequiredLabels,
    IReadOnlyList<string> SegmentTypes,
    double DurationSeconds,
    bool IsProductionReady);

public sealed record WeeklyPostAssetDynamicSceneNormalizationReport(
    bool DynamicSceneNormalizationReady,
    int InputAssetCount,
    int NormalizedAssetCount,
    int AssetsWithDerivedSource,
    int AssetsWithDerivedParentSceneCode,
    int SplitSceneAssetsDetected,
    int NullSourceAssetsAfterNormalization,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyPostAssetDynamicSceneNormalizationResult(
    IReadOnlyList<WeeklyNormalizedVisualAsset> Assets,
    WeeklyPostAssetDynamicSceneNormalizationReport Report,
    string ReportPath);

public static class WeeklyPostAssetDynamicSceneNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<WeeklyPostAssetDynamicSceneNormalizationResult> NormalizeAndPersistAsync(
        Guid pipelineRunId,
        string rootPath,
        IEnumerable<RealizedVisualAsset>? assets,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("POST_ASSET_DYNAMIC_SCENE_NORMALIZATION_START pipelineRunId={PipelineRunId} root={Root}", pipelineRunId, rootPath);

        var safeAssets = assets?.ToList() ?? [];
        var warnings = new List<string>();
        var errors = new List<string>();
        var derivedSourceCount = 0;
        var derivedParentCount = 0;
        var splitSceneCount = 0;
        var normalized = new List<WeeklyNormalizedVisualAsset>();

        foreach (var asset in safeAssets)
        {
            var path = asset.FilePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                warnings.Add($"Asset '{asset.AssetId}' was skipped because path is missing.");
                continue;
            }

            var source = asset.SourceType.ToString();
            if (string.IsNullOrWhiteSpace(source))
            {
                source = DeriveSource(path);
                derivedSourceCount++;
            }

            var sourceSceneCode = DeriveSceneCode(path, asset.AssetCode);
            var sceneCode = string.IsNullOrWhiteSpace(sourceSceneCode) ? asset.AssetCode ?? string.Empty : sourceSceneCode;
            var parentSceneCode = InferParentSceneCode(sceneCode);
            if (!parentSceneCode.Equals(sceneCode, StringComparison.OrdinalIgnoreCase))
            {
                derivedParentCount++;
                splitSceneCount++;
            }

            normalized.Add(new WeeklyNormalizedVisualAsset(
                asset.AssetId ?? $"{source}:{System.IO.Path.GetFileNameWithoutExtension(path)}",
                path,
                source,
                source,
                sceneCode,
                sourceSceneCode,
                parentSceneCode,
                sceneCode,
                DeriveFrameType(path, asset.AssetCode),
                [],
                [],
                [],
                0,
                asset.ProductionReady));
        }

        var nullSourceCount = normalized.Count(x => string.IsNullOrWhiteSpace(x.Source));
        if (nullSourceCount > 0)
        {
            errors.Add($"{nullSourceCount} assets still have null or empty source after normalization.");
            foreach (var asset in normalized.Where(x => string.IsNullOrWhiteSpace(x.Source)))
            {
                logger.LogError(new InvalidOperationException("Null source after dynamic scene normalization."),
                    "WEEKLY_POST_ASSET_NULL_SOURCE_FAILURE stage={Stage} field={Field} sceneCode={SceneCode} assetPath={AssetPath} pipelineRunId={PipelineRunId}",
                    "POST_ASSET_DYNAMIC_SCENE_NORMALIZATION", "source", asset.SceneCode, asset.Path, pipelineRunId);
            }
        }

        var report = new WeeklyPostAssetDynamicSceneNormalizationReport(
            DynamicSceneNormalizationReady: nullSourceCount == 0,
            InputAssetCount: safeAssets.Count,
            NormalizedAssetCount: normalized.Count,
            AssetsWithDerivedSource: derivedSourceCount,
            AssetsWithDerivedParentSceneCode: derivedParentCount,
            SplitSceneAssetsDetected: splitSceneCount,
            NullSourceAssetsAfterNormalization: nullSourceCount,
            Warnings: warnings,
            Errors: errors);

        var renderDirectory = System.IO.Path.Combine(rootPath, "render");
        Directory.CreateDirectory(renderDirectory);
        var reportPath = System.IO.Path.Combine(renderDirectory, "post-asset-dynamic-scene-normalization-report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);

        logger.LogInformation("POST_ASSET_DYNAMIC_SCENE_NORMALIZATION_COMPLETE pipelineRunId={PipelineRunId} inputAssetCount={InputAssetCount} normalizedAssetCount={NormalizedAssetCount} nullSourceAssetsAfterNormalization={NullSourceAssetsAfterNormalization} reportPath={ReportPath}",
            pipelineRunId, report.InputAssetCount, report.NormalizedAssetCount, report.NullSourceAssetsAfterNormalization, reportPath);

        return new WeeklyPostAssetDynamicSceneNormalizationResult(normalized, report, reportPath);
    }

    public static string DeriveSource(string? path)
    {
        var value = (path ?? string.Empty).Replace('\\', '/');
        if (value.Contains("/stellarium/scenes/", StringComparison.OrdinalIgnoreCase)) return "frameScreenshots";
        if (value.Contains("/ai-cinematic/", StringComparison.OrdinalIgnoreCase) || value.Contains("/cinematic/", StringComparison.OrdinalIgnoreCase)) return "AICinematic";
        if (value.Contains("/nasa/", StringComparison.OrdinalIgnoreCase)) return "NASA";
        if (value.Contains("/jwst/", StringComparison.OrdinalIgnoreCase)) return "JWST";
        if (value.Contains("/motion/", StringComparison.OrdinalIgnoreCase) || value.Contains("/cosmic-graphics/", StringComparison.OrdinalIgnoreCase) || value.Contains("/motion-graphics/", StringComparison.OrdinalIgnoreCase)) return "MotionGraphics";
        return "Unknown";
    }

    public static string DeriveSceneCode(string? path, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var directory = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
            if (!string.IsNullOrWhiteSpace(directory) && !directory.Equals("scenes", StringComparison.OrdinalIgnoreCase))
                return directory;
        }

        return fallback ?? string.Empty;
    }

    public static string InferParentSceneCode(string? sceneCode)
    {
        var value = sceneCode ?? string.Empty;
        foreach (var suffix in new[] { "_saturn", "_venus", "_mars", "_jupiter", "_mercury", "_moon" })
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return value[..^suffix.Length];
        }

        return value;
    }

    public static string DeriveFrameType(string? path, string? fallback = null)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path ?? string.Empty);
        if (fileName.Contains("01_", StringComparison.OrdinalIgnoreCase)) return "EstablishingWide";
        if (fileName.Contains("02_", StringComparison.OrdinalIgnoreCase)) return "BalancedStoryFrame";
        if (fileName.Contains("03_", StringComparison.OrdinalIgnoreCase)) return "HeroCloseup";
        if (fileName.Contains("04_", StringComparison.OrdinalIgnoreCase)) return "NegativeSpaceFrame";
        if (fileName.Contains("05_", StringComparison.OrdinalIgnoreCase)) return "EducationalContext";
        return fallback ?? fileName;
    }
}
