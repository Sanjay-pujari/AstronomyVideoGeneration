using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RealizedVisualAssetSourceType
{
    StellariumBase,
    StellariumExpanded,
    AICinematic,
    NASA,
    JWST,
    MotionGraphics,
    EducationalOverlay
}

public sealed record RealizedVisualAsset(
    string AssetId,
    RealizedVisualAssetSourceType SourceType,
    string AssetCode,
    string FilePath,
    bool Exists,
    long FileSizeBytes,
    int Width,
    int Height,
    string SegmentUsageRole,
    bool Reusable,
    bool ProductionReady);

public sealed record SegmentProductionAssetBundle(
    string SegmentId,
    string EpisodeType,
    string SegmentType,
    int TargetDurationSeconds,
    string NarrationStatus,
    string NarrationTextPath,
    int NarrationEstimatedWords,
    IReadOnlyList<RealizedVisualAsset> AssignedVisualAssets,
    IReadOnlyList<string> MissingVisualAssetTypes,
    bool ProductionReady,
    string ReadinessReason,
    IReadOnlyList<string> Warnings,
    bool ProductionReadyForTest,
    bool ProductionReadyForFinalVideo);

public sealed record WeeklyProductionAssetManifest(
    Guid PipelineRunId,
    string RegionId,
    string Language,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    int LongformTargetDurationSeconds,
    int ShortformTargetDurationSeconds,
    int TotalProductionImageAssetCount,
    int StellariumBaseAssetCount,
    int ExpandedStellariumAssetCount,
    int AICinematicAssetCount,
    int NASAAssetCount,
    int JWSTAssetCount,
    int MotionGraphicsAssetCount,
    int EducationalOverlayAssetCount,
    IReadOnlyList<SegmentProductionAssetBundle> SegmentBundles);

public sealed record SegmentAssetCoverageResult(
    string SegmentId,
    string EpisodeType,
    string SegmentType,
    int AssignedVisualAssetCount,
    IReadOnlyList<string> SatisfiedAssetTypesForTest,
    IReadOnlyList<string> MissingAssetTypesForFinal,
    bool FallbackUsed,
    bool ProductionReadyForTest,
    bool ProductionReadyForFinalVideo,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyAssetCoverageAuditReport(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    int PlannedVisualAssetCount,
    int RealizedVisualAssetCount,
    int ProductionReadyVisualAssetCount,
    int MissingVisualAssetCount,
    IReadOnlyDictionary<string, int> RealizedBySource,
    IReadOnlyDictionary<string, int> MissingBySource,
    IReadOnlyList<SegmentAssetCoverageResult> SegmentCoverage,
    double CoveragePercentage,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyVideoReadinessReport(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    bool TestVideoPipelineReady,
    bool FinalVideoPipelineReady,
    bool LongformTestReady,
    bool ShortformTestReady,
    bool LongformFinalReady,
    bool ShortformFinalReady,
    int ReadySegmentCountForTest,
    int ReadySegmentCountForFinal,
    IReadOnlyList<string> NotReadySegments,
    IReadOnlyList<string> MissingAssetCategories,
    IReadOnlyList<string> MissingNarrationCategories,
    IReadOnlyList<string> RecommendedNextActions);

public sealed record WeeklyAssetRealizationResult(
    WeeklyProductionAssetManifest Manifest,
    WeeklyAssetCoverageAuditReport RealizationReport,
    WeeklyVideoReadinessReport VideoReadinessReport,
    string WeeklyProductionAssetManifestPath,
    string WeeklyAssetRealizationReportPath,
    string WeeklyVideoReadinessReportPath,
    bool AssetRealizationReady,
    string NasaAssetPlanPath,
    string NasaAssetResultsPath,
    int PlannedNASAAssetCount,
    int GeneratedNASAAssetCount,
    int ProductionReadyNASAAssetCount,
    IReadOnlyList<string> NasaImagePaths,
    bool NasaProviderConfigured);

public sealed record WeeklyAssetRealizationInput(
    Guid PipelineRunId,
    string RegionId,
    string Language,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    string RootPath,
    string StoryBeatsPath,
    string NarrationTextPath,
    WeeklyEpisodePlan LongformPlan,
    WeeklyEpisodePlan ShortformPlan,
    WeeklySegmentClassificationPlan SegmentClassificationPlan,
    WeeklyVisualAssetPlan VisualAssetPlan,
    string WeeklyVisualAssetPlanPath,
    IReadOnlyList<string> FrameScreenshots,
    IReadOnlyList<string> ExpandedFrameScreenshots,
    IReadOnlyList<string> AICinematicImagePaths,
    IReadOnlyList<string> AllProductionImageAssets);

public sealed class WeeklyAssetRealizationService(
    WeeklyAssetRealizationPersister persister,
    WeeklyAssetRealizationValidator validator,
    INasaImageAssetProvider nasaImageAssetProvider,
    ILogger<WeeklyAssetRealizationService> logger)
{
    public async Task<WeeklyAssetRealizationResult> RealizeAndPersistAsync(WeeklyAssetRealizationInput input, CancellationToken cancellationToken)
    {
        logger.LogInformation("ASSET_REALIZATION_START pipelineRunId={PipelineRunId} root={Root}", input.PipelineRunId, input.RootPath);
        var assets = RegisterAssets(input);
        var bundles = BuildSegmentBundles(input, assets);
        var manifest = BuildManifest(input, assets, bundles);
        var report = BuildCoverageReport(input, assets, bundles);
        var readiness = validator.BuildVideoReadinessReport(input, manifest, report);
        var paths = await persister.PersistAsync(input.RootPath, manifest, report, readiness, cancellationToken);

        var nasaAssets = await nasaImageAssetProvider.RealizeAsync(input.RootPath, input.WeeklyVisualAssetPlanPath, paths.ManifestPath, continueOnFailure: true, cancellationToken);
        if (nasaAssets.Results.NasaImagePaths.Count > 0)
        {
            var enrichedInput = input with { AllProductionImageAssets = input.AllProductionImageAssets.Concat(nasaAssets.Results.NasaImagePaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
            assets = RegisterAssets(enrichedInput);
            bundles = BuildSegmentBundles(enrichedInput, assets);
            manifest = BuildManifest(enrichedInput, assets, bundles);
            report = BuildCoverageReport(enrichedInput, assets, bundles);
            readiness = validator.BuildVideoReadinessReport(enrichedInput, manifest, report);
            paths = await persister.PersistAsync(enrichedInput.RootPath, manifest, report, readiness, cancellationToken);
        }

        logger.LogInformation("ASSET_REALIZATION_COMPLETE pipelineRunId={PipelineRunId} testReady={TestReady} finalReady={FinalReady} segmentCount={SegmentCount} nasaGenerated={NasaGenerated} nasaProductionReady={NasaProductionReady}", input.PipelineRunId, readiness.TestVideoPipelineReady, readiness.FinalVideoPipelineReady, bundles.Count, nasaAssets.Results.GeneratedNASAAssetCount, nasaAssets.Results.ProductionReadyNASAAssetCount);
        return new WeeklyAssetRealizationResult(manifest, report, readiness, paths.ManifestPath, paths.RealizationReportPath, paths.VideoReadinessReportPath, readiness.TestVideoPipelineReady, nasaAssets.PlanPath, nasaAssets.ResultsPath, nasaAssets.Results.PlannedNASAAssetCount, nasaAssets.Results.GeneratedNASAAssetCount, nasaAssets.Results.ProductionReadyNASAAssetCount, nasaAssets.Results.NasaImagePaths, nasaAssets.Results.ProviderConfigured);
    }

    private static WeeklyProductionAssetManifest BuildManifest(WeeklyAssetRealizationInput input, IReadOnlyList<RealizedVisualAsset> assets, IReadOnlyList<SegmentProductionAssetBundle> bundles) => new(
        input.PipelineRunId,
        input.RegionId,
        input.Language,
        input.WeekStartDate,
        input.WeekEndDate,
        input.LongformPlan.TotalTargetDurationSeconds,
        input.ShortformPlan.TotalTargetDurationSeconds,
        assets.Count,
        Count(assets, RealizedVisualAssetSourceType.StellariumBase),
        Count(assets, RealizedVisualAssetSourceType.StellariumExpanded),
        Count(assets, RealizedVisualAssetSourceType.AICinematic),
        Count(assets, RealizedVisualAssetSourceType.NASA),
        Count(assets, RealizedVisualAssetSourceType.JWST),
        Count(assets, RealizedVisualAssetSourceType.MotionGraphics),
        Count(assets, RealizedVisualAssetSourceType.EducationalOverlay),
        bundles);

    private List<RealizedVisualAsset> RegisterAssets(WeeklyAssetRealizationInput input)
    {
        var registrations = new List<(string Path, RealizedVisualAssetSourceType Source)>();
        registrations.AddRange(input.FrameScreenshots.Select(path => (path, RealizedVisualAssetSourceType.StellariumBase)));
        registrations.AddRange(input.ExpandedFrameScreenshots.Select(path => (path, RealizedVisualAssetSourceType.StellariumExpanded)));
        registrations.AddRange(input.AICinematicImagePaths.Select(path => (path, RealizedVisualAssetSourceType.AICinematic)));

        var known = new HashSet<string>(registrations.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var path in input.AllProductionImageAssets.Where(path => !string.IsNullOrWhiteSpace(path) && known.Add(path)))
        {
            registrations.Add((path, InferSourceType(path)));
        }

        return registrations
            .Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateAsset(group.Key, group.First().Source))
            .ToList();
    }

    private RealizedVisualAsset CreateAsset(string path, RealizedVisualAssetSourceType sourceType)
    {
        var exists = File.Exists(path);
        var fileInfo = exists ? new FileInfo(path) : null;
        var (width, height) = exists ? ImageDimensionReader.Read(path) : (0, 0);
        var assetCode = Path.GetFileNameWithoutExtension(path);
        var asset = new RealizedVisualAsset(
            $"{sourceType}:{assetCode}",
            sourceType,
            assetCode,
            path,
            exists,
            fileInfo?.Length ?? 0,
            width,
            height,
            "ReusableProductionVisual",
            true,
            exists && fileInfo?.Length > 0 && width > 0 && height > 0);
        logger.LogInformation("PRODUCTION_ASSET_REGISTERED assetId={AssetId} sourceType={SourceType} path={Path} exists={Exists} size={Size} width={Width} height={Height}", asset.AssetId, asset.SourceType, asset.FilePath, asset.Exists, asset.FileSizeBytes, asset.Width, asset.Height);
        return asset;
    }

    private List<SegmentProductionAssetBundle> BuildSegmentBundles(WeeklyAssetRealizationInput input, IReadOnlyList<RealizedVisualAsset> assets)
    {
        var bundles = new List<SegmentProductionAssetBundle>();
        var allSegments = input.LongformPlan.Segments.Select(s => (EpisodeType: WeeklyEpisodeType.LongFormWeeklyForecast.ToString(), Segment: s))
            .Concat(input.ShortformPlan.Segments.Select(s => (EpisodeType: WeeklyEpisodeType.ShortFormWeeklyHighlight.ToString(), Segment: s)));
        var narrationExists = File.Exists(input.StoryBeatsPath);
        var narrationTextExists = File.Exists(input.NarrationTextPath);
        var narrationWordCount = narrationTextExists ? CountWords(File.ReadAllText(input.NarrationTextPath)) : 0;

        foreach (var item in allSegments)
        {
            var (assigned, missing, finalReady, warnings) = AssignAssets(item.EpisodeType, item.Segment.SegmentType, assets);
            var filesReady = assigned.Count > 0 && assigned.All(x => x.Exists && x.ProductionReady);
            var testReady = filesReady && narrationExists;
            var finalSegmentReady = testReady && finalReady;
            if (assigned.Count > 0 && warnings.Any(x => x.Contains("fallback", StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogInformation("SEGMENT_ASSET_FALLBACK_ASSIGNED segmentId={SegmentId} segmentType={SegmentType} assets={Assets} warnings={Warnings}", item.Segment.SegmentId, item.Segment.SegmentType, string.Join(',', assigned.Select(x => x.AssetId)), string.Join(" | ", warnings));
            }
            var reason = testReady
                ? finalSegmentReady ? "Segment has final-ready visual and narration coverage." : "Segment is ready for test using available realized visual assets; final requirements remain open."
                : assigned.Count == 0 ? "No realized visual asset could be assigned." : "Assigned visual files or story beats are missing.";
            var bundle = new SegmentProductionAssetBundle(
                item.Segment.SegmentId,
                item.EpisodeType,
                item.Segment.SegmentType,
                item.Segment.TargetDurationSeconds,
                narrationExists ? "StoryBeatsAvailable" : "StoryBeatsMissing",
                input.NarrationTextPath,
                narrationWordCount,
                assigned,
                missing,
                testReady,
                reason,
                warnings,
                testReady,
                finalSegmentReady);
            logger.LogInformation("SEGMENT_ASSET_BUNDLE_CREATED segmentId={SegmentId} episodeType={EpisodeType} segmentType={SegmentType} assignedAssets={AssetCount} productionReadyForTest={TestReady} productionReadyForFinalVideo={FinalReady}", bundle.SegmentId, bundle.EpisodeType, bundle.SegmentType, bundle.AssignedVisualAssets.Count, bundle.ProductionReadyForTest, bundle.ProductionReadyForFinalVideo);
            bundles.Add(bundle);
        }

        return bundles;
    }

    private (IReadOnlyList<RealizedVisualAsset> Assigned, IReadOnlyList<string> Missing, bool FinalReady, IReadOnlyList<string> Warnings) AssignAssets(string episodeType, string segmentType, IReadOnlyList<RealizedVisualAsset> assets)
    {
        var warnings = new List<string>();
        var missing = new List<string>();
        var assigned = new List<RealizedVisualAsset>();
        var baseStellarium = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.StellariumBase).ToList();
        var expanded = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.StellariumExpanded).ToList();
        var stellarium = baseStellarium.Concat(expanded).ToList();
        var ai = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.AICinematic).ToList();
        var motion = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.MotionGraphics).ToList();
        var educational = assets.Where(a => a.SourceType == RealizedVisualAssetSourceType.EducationalOverlay).ToList();
        var finalReady = true;

        void Use(IEnumerable<RealizedVisualAsset> candidates, string role)
        {
            var asset = SelectBest(candidates, segmentType);
            if (asset is not null && assigned.All(x => !x.AssetId.Equals(asset.AssetId, StringComparison.OrdinalIgnoreCase)))
            {
                assigned.Add(asset with { SegmentUsageRole = role, Reusable = true });
            }
        }

        void Fallback(IEnumerable<RealizedVisualAsset> candidates, string ideal, string reason)
        {
            finalReady = false;
            missing.Add(ideal);
            Use(candidates, "fallback");
            warnings.Add(reason);
        }

        switch (segmentType)
        {
            case "OpeningHook":
                if (ai.Count > 0) Use(ai, "preferred_cinematic_hook");
                else if (stellarium.Count > 0) Fallback(stellarium, "AICinematic", "AICinematic opening hook missing; assigned Stellarium fallback for test readiness.");
                else missing.Add("AICinematicOrStellarium");
                break;
            case "WeeklySkyOverview":
                if (motion.Count > 0) Use(motion, "preferred_motion_overview");
                else Fallback(stellarium.Concat(ai), "MotionGraphics", "WeeklySkyOverview missing MotionGraphics; assigned widest Stellarium/AI fallback for test readiness.");
                break;
            case "HeroEvent":
            case "MoonHighlights":
            case "PlanetHighlights":
            case "StrongestEvent":
                Use(stellarium, "required_stellarium_visual");
                if (assigned.Count == 0) missing.Add("StellariumBaseOrStellariumExpanded");
                break;
            case "BestObservationWindow":
                if (motion.Count > 0) Use(motion, "preferred_motion_window");
                else Fallback(expanded.Concat(stellarium).Concat(ai), "MotionGraphics", "BestObservationWindow missing motion graphic; assigned expanded/Stellarium fallback for test readiness.");
                break;
            case "AstrophotographyTip":
                Use(expanded.Concat(educational).Concat(ai), "astrophotography_visual");
                if (assigned.Count == 0) missing.Add("StellariumExpandedOrEducationalOverlayOrAICinematic");
                if (educational.Count == 0)
                {
                    finalReady = false;
                    missing.Add("EducationalOverlay");
                    warnings.Add("EducationalOverlay is not realized; expanded/AI asset can satisfy test coverage only.");
                }
                break;
            case "WeeklySummary":
                if (ai.Count > 0) Use(ai, "closing_cinematic_or_montage");
                else Fallback(stellarium, "AICinematicOrMontage", "WeeklySummary missing recap montage/AI; assigned Stellarium fallback for test readiness.");
                if (motion.Count == 0)
                {
                    finalReady = false;
                    missing.Add("MotionGraphics");
                    warnings.Add("WeeklySummary recap motion graphics are not realized for final video readiness.");
                }
                break;
            case "ShortHook":
                if (ai.Count > 0) Use(ai, "short_hook_cinematic");
                else if (stellarium.Count > 0) Use(stellarium, "short_hook_stellarium");
                else missing.Add("AICinematicOrStellarium");
                break;
            case "WhereToLook":
                if (stellarium.Count > 0) Use(stellarium, "where_to_look_visual");
                else if (motion.Count > 0) Use(motion, "where_to_look_motion_graphic");
                else missing.Add("StellariumOrMotionGraphics");
                if (motion.Count == 0)
                {
                    finalReady = false;
                    missing.Add("MotionGraphics");
                    warnings.Add("WhereToLook direction motion graphic is not realized for final video readiness.");
                }
                break;
            case "BestTime":
                if (motion.Count > 0) Use(motion, "best_time_card");
                else Fallback(stellarium.Concat(ai), "MotionGraphics", "BestTime missing time-card motion graphic; assigned available visual fallback for test readiness.");
                break;
            case "CallToAction":
                if (ai.Count > 0) Use(ai, "cta_cinematic_background");
                else Fallback(stellarium, "AICinematicOrGenericClosingVisual", "CallToAction missing AI/generic closing visual; assigned Stellarium fallback for test readiness.");
                if (motion.Count == 0)
                {
                    finalReady = false;
                    missing.Add("MotionGraphics");
                }
                break;
            default:
                Use(assets, "generic_visual");
                break;
        }

        if (assigned.Count == 0) finalReady = false;
        logger.LogInformation("SEGMENT_ASSET_COVERAGE_CALCULATED episodeType={EpisodeType} segmentType={SegmentType} assignedAssets={AssignedAssets} missing={Missing} finalReady={FinalReady}", episodeType, segmentType, assigned.Count, string.Join(',', missing.Distinct(StringComparer.OrdinalIgnoreCase)), finalReady);
        return (assigned, missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), finalReady && missing.Count == 0, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private WeeklyAssetCoverageAuditReport BuildCoverageReport(WeeklyAssetRealizationInput input, IReadOnlyList<RealizedVisualAsset> assets, IReadOnlyList<SegmentProductionAssetBundle> bundles)
    {
        var plannedBySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["MotionGraphics"] = input.VisualAssetPlan.PlannedMotionGraphicsCount,
            ["EducationalOverlay"] = input.VisualAssetPlan.PlannedEducationalOverlayCount,
            ["AICinematic"] = input.VisualAssetPlan.PlannedAICinematicCount,
            ["NASA"] = input.VisualAssetPlan.PlannedNASAAssetCount,
            ["JWST"] = input.VisualAssetPlan.PlannedJWSTAssetCount
        };
        var realizedBySource = Enum.GetValues<RealizedVisualAssetSourceType>()
            .ToDictionary(x => x.ToString(), x => Count(assets, x), StringComparer.OrdinalIgnoreCase);
        realizedBySource["Stellarium"] = Count(assets, RealizedVisualAssetSourceType.StellariumBase) + Count(assets, RealizedVisualAssetSourceType.StellariumExpanded);
        var missingBySource = plannedBySource.ToDictionary(x => x.Key, x => Math.Max(0, x.Value - realizedBySource.GetValueOrDefault(x.Key)), StringComparer.OrdinalIgnoreCase);
        var planned = Math.Max(input.VisualAssetPlan.PlannedVisualAssetCount, assets.Count + missingBySource.Values.Sum());
        var readyAssets = assets.Count(x => x.ProductionReady);
        var segmentCoverage = bundles.Select(bundle => new SegmentAssetCoverageResult(
            bundle.SegmentId,
            bundle.EpisodeType,
            bundle.SegmentType,
            bundle.AssignedVisualAssets.Count,
            bundle.AssignedVisualAssets.Select(x => x.SourceType.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            bundle.MissingVisualAssetTypes,
            bundle.Warnings.Any(w => w.Contains("fallback", StringComparison.OrdinalIgnoreCase)),
            bundle.ProductionReadyForTest,
            bundle.ProductionReadyForFinalVideo,
            bundle.Warnings)).ToList();
        var blockers = bundles.Where(x => !x.ProductionReadyForTest).Select(x => $"{x.SegmentId} has no test-ready visual/narration coverage.").ToList();
        var warnings = bundles.SelectMany(x => x.Warnings)
            .Concat(missingBySource.Where(x => x.Value > 0).Select(x => $"{x.Key} has {x.Value} planned assets not realized."))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new WeeklyAssetCoverageAuditReport(
            input.PipelineRunId,
            DateTime.UtcNow,
            planned,
            assets.Count,
            readyAssets,
            Math.Max(0, planned - assets.Count),
            realizedBySource,
            missingBySource,
            segmentCoverage,
            planned <= 0 ? 100 : Math.Round((double)assets.Count / planned * 100, 2),
            blockers,
            warnings);
    }

    private static RealizedVisualAsset? SelectBest(IEnumerable<RealizedVisualAsset> candidates, string segmentType)
    {
        return candidates
            .Where(x => x.ProductionReady)
            .OrderByDescending(x => ScoreAssetForSegment(x, segmentType))
            .ThenByDescending(x => x.Width * x.Height)
            .FirstOrDefault();
    }

    private static int ScoreAssetForSegment(RealizedVisualAsset asset, string segmentType)
    {
        var code = asset.AssetCode;
        if (segmentType == "AstrophotographyTip" && code.Contains("astro", StringComparison.OrdinalIgnoreCase)) return 100;
        if ((segmentType == "BestObservationWindow" || segmentType == "WhereToLook") && (code.Contains("where", StringComparison.OrdinalIgnoreCase) || code.Contains("guidance", StringComparison.OrdinalIgnoreCase) || code.Contains("wide", StringComparison.OrdinalIgnoreCase))) return 95;
        if ((segmentType == "MoonHighlights" || segmentType == "OpeningHook") && code.Contains("moon", StringComparison.OrdinalIgnoreCase)) return 90;
        if (segmentType == "PlanetHighlights" && (code.Contains("planet", StringComparison.OrdinalIgnoreCase) || code.Contains("western", StringComparison.OrdinalIgnoreCase))) return 90;
        if ((segmentType == "HeroEvent" || segmentType == "StrongestEvent") && code.Contains("hero", StringComparison.OrdinalIgnoreCase)) return 90;
        return asset.Width;
    }

    private static RealizedVisualAssetSourceType InferSourceType(string path)
    {
        var value = path.ToLowerInvariant();
        if (value.Contains("jwst")) return RealizedVisualAssetSourceType.JWST;
        if (value.Contains("nasa")) return RealizedVisualAssetSourceType.NASA;
        if (value.Contains("motion")) return RealizedVisualAssetSourceType.MotionGraphics;
        if (value.Contains("overlay") || value.Contains("educational")) return RealizedVisualAssetSourceType.EducationalOverlay;
        if (value.Contains("ai") || value.Contains("cinematic")) return RealizedVisualAssetSourceType.AICinematic;
        return RealizedVisualAssetSourceType.StellariumBase;
    }

    private static int Count(IReadOnlyList<RealizedVisualAsset> assets, RealizedVisualAssetSourceType sourceType) => assets.Count(x => x.SourceType == sourceType);
    private static int CountWords(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

public sealed class WeeklyAssetRealizationPersister(ILogger<WeeklyAssetRealizationPersister> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<(string ManifestPath, string RealizationReportPath, string VideoReadinessReportPath)> PersistAsync(
        string root,
        WeeklyProductionAssetManifest manifest,
        WeeklyAssetCoverageAuditReport realizationReport,
        WeeklyVideoReadinessReport readinessReport,
        CancellationToken cancellationToken)
    {
        var episodeDirectory = Path.Combine(root, "episode");
        Directory.CreateDirectory(episodeDirectory);
        var manifestPath = Path.Combine(episodeDirectory, "weekly-production-asset-manifest.json");
        var realizationReportPath = Path.Combine(episodeDirectory, "weekly-asset-realization-report.json");
        var readinessReportPath = Path.Combine(episodeDirectory, "weekly-video-readiness-report.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(realizationReportPath, JsonSerializer.Serialize(realizationReport, JsonOptions), cancellationToken);
        logger.LogInformation("ASSET_REALIZATION_REPORT_WRITTEN manifestPath={ManifestPath} realizationReportPath={RealizationReportPath}", manifestPath, realizationReportPath);
        await File.WriteAllTextAsync(readinessReportPath, JsonSerializer.Serialize(readinessReport, JsonOptions), cancellationToken);
        logger.LogInformation("VIDEO_READINESS_REPORT_WRITTEN path={Path}", readinessReportPath);
        return (manifestPath, realizationReportPath, readinessReportPath);
    }
}

public sealed class WeeklyAssetRealizationValidator
{
    public WeeklyVideoReadinessReport BuildVideoReadinessReport(WeeklyAssetRealizationInput input, WeeklyProductionAssetManifest manifest, WeeklyAssetCoverageAuditReport report)
    {
        var longform = manifest.SegmentBundles.Where(x => x.EpisodeType == WeeklyEpisodeType.LongFormWeeklyForecast.ToString()).ToList();
        var shortform = manifest.SegmentBundles.Where(x => x.EpisodeType == WeeklyEpisodeType.ShortFormWeeklyHighlight.ToString()).ToList();
        var expectedSegmentCount = input.LongformPlan.Segments.Count + input.ShortformPlan.Segments.Count;
        var storyBeatsExist = File.Exists(input.StoryBeatsPath);
        var allAssignedFilesExist = manifest.SegmentBundles.SelectMany(x => x.AssignedVisualAssets).All(x => x.Exists && x.ProductionReady);
        var allSegmentsHaveVisuals = manifest.SegmentBundles.Count == expectedSegmentCount && manifest.SegmentBundles.All(x => x.AssignedVisualAssets.Count > 0);
        var testReady = allSegmentsHaveVisuals && storyBeatsExist && allAssignedFilesExist;
        var longformTestReady = longform.Count == input.LongformPlan.Segments.Count && longform.All(x => x.ProductionReadyForTest);
        var shortformTestReady = shortform.Count == input.ShortformPlan.Segments.Count && shortform.All(x => x.ProductionReadyForTest);
        var longformFinalReady = longform.Count > 0 && longform.All(x => x.ProductionReadyForFinalVideo);
        var shortformFinalReady = shortform.Count > 0 && shortform.All(x => x.ProductionReadyForFinalVideo);
        var missingAssets = report.MissingBySource.Where(x => x.Value > 0).Select(x => x.Key).Concat(manifest.SegmentBundles.SelectMany(x => x.MissingVisualAssetTypes)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var missingNarration = new List<string>();
        if (!storyBeatsExist) missingNarration.Add("weekly-story-beats");
        if (!File.Exists(input.NarrationTextPath)) missingNarration.Add("weekly-narration-text");
        var notReady = manifest.SegmentBundles
            .Where(x => !x.ProductionReadyForTest || !x.ProductionReadyForFinalVideo)
            .Select(x => $"{x.SegmentId}:{x.SegmentType}:test={x.ProductionReadyForTest}:final={x.ProductionReadyForFinalVideo}:missing={string.Join('|', x.MissingVisualAssetTypes)}")
            .ToList();
        var finalReady = longformFinalReady && shortformFinalReady && missingAssets.Count == 0 && missingNarration.Count == 0;
        var next = new List<string>();
        if (missingAssets.Contains("MotionGraphics", StringComparer.OrdinalIgnoreCase)) next.Add("Realize planned MotionGraphics assets for overview, best-time, where-to-look, summary, and CTA segments.");
        if (missingAssets.Contains("EducationalOverlay", StringComparer.OrdinalIgnoreCase)) next.Add("Generate educational overlay cards for the astrophotography and checklist segments.");
        if (missingAssets.Contains("NASA", StringComparer.OrdinalIgnoreCase) || missingAssets.Contains("JWST", StringComparer.OrdinalIgnoreCase)) next.Add("Resolve NASA/JWST context imagery where planned by the visual asset plan.");
        if (missingNarration.Count > 0) next.Add("Generate final narration artifacts before final video rendering.");
        if (next.Count == 0 && !finalReady) next.Add("Remove fallback visual assignments by realizing each segment's preferred source-specific assets.");
        return new WeeklyVideoReadinessReport(
            input.PipelineRunId,
            DateTime.UtcNow,
            testReady,
            finalReady,
            longformTestReady,
            shortformTestReady,
            longformFinalReady,
            shortformFinalReady,
            manifest.SegmentBundles.Count(x => x.ProductionReadyForTest),
            manifest.SegmentBundles.Count(x => x.ProductionReadyForFinalVideo),
            notReady,
            missingAssets,
            missingNarration,
            next.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
}

internal static class ImageDimensionReader
{
    public static (int Width, int Height) Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24];
            if (stream.Read(header) < 24) return (0, 0);
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                return (BinaryPrimitives.ReadInt32BigEndian(header[16..20]), BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
            }
            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                return ReadJpegDimensions(stream);
            }
        }
        catch
        {
            return (0, 0);
        }
        return (0, 0);
    }

    private static (int Width, int Height) ReadJpegDimensions(Stream stream)
    {
        stream.Position = 2;
        while (stream.Position < stream.Length)
        {
            if (stream.ReadByte() != 0xFF) continue;
            var marker = stream.ReadByte();
            if (marker < 0) break;
            var length = ReadBigEndianUInt16(stream);
            if (length < 2) break;
            if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
            {
                stream.ReadByte();
                var height = ReadBigEndianUInt16(stream);
                var width = ReadBigEndianUInt16(stream);
                return (width, height);
            }
            stream.Seek(length - 2, SeekOrigin.Current);
        }
        return (0, 0);
    }

    private static int ReadBigEndianUInt16(Stream stream)
    {
        var hi = stream.ReadByte();
        var lo = stream.ReadByte();
        return hi < 0 || lo < 0 ? 0 : (hi << 8) + lo;
    }
}
