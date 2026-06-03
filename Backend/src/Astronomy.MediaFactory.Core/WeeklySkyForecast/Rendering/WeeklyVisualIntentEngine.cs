using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.NarrationEngine;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.TimelineComposition;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.Rendering;

public interface IWeeklyVisualIntentEngine
{
    Task<WeeklyVisualIntentBuildResponse> BuildAsync(Guid pipelineRunId, CancellationToken cancellationToken);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeeklyVisualIntentType
{
    Hook,
    HeroEvent,
    StrongestEvent,
    MoonHighlights,
    PlanetHighlights,
    Observation,
    ObservationWindow,
    DirectionGuidance,
    BestTime,
    ScientificContext,
    EducationalExplanation,
    AstrophotographyTip,
    Summary,
    RetentionReset,
    CallToAction
}

public sealed record WeeklyVisualIntentBuildResponse(
    Guid PipelineRunId,
    bool VisualIntentReady,
    bool VisualStorytellingReady,
    string ResolvedPipelineRunRoot,
    string VisualIntentPlanPath,
    string VisualIntentShotPlanPath,
    string VisualIntentValidationReportPath,
    int TotalBeats,
    int MatchedBeatCount,
    int UnmatchedBeatCount,
    int NarrationVisualMismatchCount,
    int FallbackVisualCount,
    int MotionGraphicOverlayUsageCount,
    int EducationalOverlayUsageCount,
    int FullscreenMotionGraphicCount,
    int FullscreenMotionGraphicOveruseCount,
    int FullscreenEducationalOverlayCount,
    int SameFamilyConsecutiveMax,
    bool ShortformHookStrongVisualPassed,
    bool SaturnNarrationMatchedToSaturnVisual,
    bool VenusNarrationMatchedToVenusVisual,
    bool MoonNarrationMatchedToMoonVisual,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyVisualIntentPlan(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    string PlanVersion,
    IReadOnlyList<string> InputArtifacts,
    IReadOnlyList<WeeklyVisualIntentBeat> Beats,
    WeeklyVisualIntentAssetMix LongformTargetMix,
    WeeklyVisualIntentAssetMix ActualLongformMix,
    WeeklyVisualIntentAssetMix ShortformTargetMix,
    WeeklyVisualIntentAssetMix ActualShortformMix,
    IReadOnlyList<WeeklyInternalCelestialAssetRequest> InternalCelestialFallbackRequests,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyVisualIntentBeat(
    string BeatId,
    string EpisodeType,
    string SegmentId,
    string SegmentType,
    WeeklyVisualIntentType VisualIntent,
    double StartSecond,
    double EndSecond,
    double DurationSeconds,
    string NarrationText,
    string NarrationSubject,
    IReadOnlyList<string> MentionedObjects,
    IReadOnlyList<string> EditorialRulesApplied,
    WeeklyVisualIntentAssetSelection PrimaryVisual,
    WeeklyVisualIntentAssetSelection? SecondaryVisual,
    IReadOnlyList<WeeklyVisualIntentAssetSelection> Overlays,
    bool MatchedToNarration,
    IReadOnlyList<WeeklyVisualIntentAssetCandidate> PrimaryVisualCandidatePool,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyVisualIntentAssetCandidate(
    string AssetId,
    string Path,
    string Family,
    string SourceType,
    string SceneCode,
    IReadOnlyList<string> TargetObjects,
    IReadOnlyList<string> MatchedSubjects,
    int Score,
    bool IsEligibleAsPrimary,
    bool IsEligibleAsOverlay,
    string Reason);

public sealed record WeeklyVisualIntentAssetSelection(
    string AssetId,
    string AssetType,
    string VisualFamily,
    string AssetPath,
    string Usage,
    bool IsOverlay,
    double StartSecond,
    double EndSecond,
    double DurationSeconds,
    IReadOnlyList<string> MatchedObjects,
    bool ProductionReady,
    bool RequestedButUnavailable = false,
    string? RequestSource = null);

public sealed record WeeklyVisualIntentStoryboard(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    string StoryboardVersion,
    IReadOnlyList<WeeklyVisualIntentStoryboardBeat> Beats);

public sealed record WeeklyVisualIntentStoryboardBeat(
    string NarrationBeatId,
    string Subject,
    WeeklyVisualIntentType Intent,
    WeeklyVisualIntentAssetSelection PrimaryVisual,
    WeeklyVisualIntentAssetSelection? SecondaryVisual,
    WeeklyVisualIntentAssetSelection? OverlayVisual,
    double DurationSeconds);

public sealed record WeeklyVisualStorytellingReport(
    bool VisualStorytellingReady,
    int SameFamilyConsecutiveMax,
    int MotionGraphicOverlayUsageCount,
    int EducationalOverlayUsageCount,
    int FullscreenMotionGraphicCount,
    int FullscreenEducationalOverlayCount,
    bool SaturnNarrationMatchedToSaturnVisual,
    bool VenusNarrationMatchedToVenusVisual,
    bool MoonNarrationMatchedToMoonVisual,
    int FallbackVisualCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyVisualIntentShotPlan(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<WeeklyVisualIntentEpisodeShotPlan> Episodes);

public sealed record WeeklyVisualIntentEpisodeShotPlan(
    string EpisodeType,
    double ActualDurationSeconds,
    IReadOnlyList<WeeklyVisualIntentSegmentShotPlan> Segments);

public sealed record WeeklyVisualIntentSegmentShotPlan(
    string SegmentId,
    string SegmentType,
    WeeklyVisualIntentType VisualIntent,
    double StartSecond,
    double EndSecond,
    double DurationSeconds,
    string NarrationText,
    IReadOnlyList<WeeklyVisualIntentShotPlanEntry> Shots,
    IReadOnlyList<WeeklyVisualIntentShotPlanEntry> Overlays);

public sealed record WeeklyVisualIntentShotPlanEntry(
    int ShotNumber,
    string AssetId,
    string AssetType,
    string VisualFamily,
    string AssetPath,
    double StartSecond,
    double EndSecond,
    double DurationSeconds,
    string Usage,
    bool IsOverlay,
    IReadOnlyList<string> MatchedObjects,
    bool ProductionReady,
    bool RequestedButUnavailable = false,
    string? RequestSource = null);

public sealed record WeeklyVisualIntentAssetMix(
    double StellariumPercent,
    double AICinematicPercent,
    double CelestialReferencePercent,
    double MotionGraphicsPercent,
    double EducationalOverlayPercent);

public sealed record WeeklyInternalCelestialAssetRequest(
    string ObjectCode,
    string SegmentId,
    string EpisodeType,
    string Reason,
    string Status);

public sealed record WeeklyVisualIntentValidationReport(
    bool VisualIntentReady,
    int TotalBeats,
    int MatchedBeatCount,
    int UnmatchedBeatCount,
    int NarrationVisualMismatchCount,
    int FallbackVisualCount,
    int MotionGraphicOverlayUsageCount,
    int EducationalOverlayUsageCount,
    int FullscreenMotionGraphicCount,
    int FullscreenMotionGraphicOveruseCount,
    int FullscreenEducationalOverlayCount,
    int SameFamilyConsecutiveMax,
    bool ShortformHookStrongVisualPassed,
    bool SaturnNarrationMatchedToSaturnVisual,
    bool VenusNarrationMatchedToVenusVisual,
    bool MoonNarrationMatchedToMoonVisual,
    bool FamilyRotationApplied,
    int FamilyRotationSwapCount,
    IReadOnlyDictionary<string, int> PrimaryFamilyCounts,
    IReadOnlyDictionary<string, bool> ObjectCoverage,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WeeklyVisualFamilyDistributionReport(
    bool VisualFamilyDistributionReady,
    IReadOnlyDictionary<string, int> PrimaryFamilyCounts,
    int SameFamilyConsecutiveMax,
    bool RotationApplied,
    int RotationSwapCount,
    int CandidatePoolBuiltForBeatCount,
    double AverageCandidatesPerBeat,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed class WeeklyVisualIntentEngine(
    IWeeklyPipelineRunDirectoryResolver pipelineRunDirectoryResolver,
    ILogger<WeeklyVisualIntentEngine> logger) : IWeeklyVisualIntentEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] InputFileNames =
    [
        "episode/longform-narration.json",
        "episode/shortform-narration.json",
        "render/audio-driven-final-render-timeline.json",
        "render/audio-driven-resolved-render-shot-plan.json",
        "episode/weekly-production-asset-manifest.json",
        "episode/narration-asset-map.json",
        "episode/narration-timeline-map.json"
    ];

    public async Task<WeeklyVisualIntentBuildResponse> BuildAsync(Guid pipelineRunId, CancellationToken cancellationToken)
    {
        var root = await pipelineRunDirectoryResolver.ResolveRunDirectoryAsync(pipelineRunId);
        logger.LogInformation("WEEKLY_VISUAL_INTENT_START pipelineRunId={PipelineRunId} root={Root}", pipelineRunId, root);

        var episodeDirectory = Path.Combine(root, "episode");
        var renderDirectory = Path.Combine(root, "render");
        Directory.CreateDirectory(renderDirectory);

        var warnings = new List<string>();
        var errors = new List<string>();
        var inputPaths = InputFileNames.Select(x => Path.Combine(root, x)).ToList();
        foreach (var missing in inputPaths.Where(path => !File.Exists(path)))
        {
            errors.Add($"Required visual intent input missing: {missing}");
        }

        if (errors.Count > 0)
        {
            return await PersistFailureAsync(pipelineRunId, root, renderDirectory, inputPaths, warnings, errors, cancellationToken);
        }

        var timeline = await ReadJsonAsync<FinalRenderTimeline>(Path.Combine(renderDirectory, "audio-driven-final-render-timeline.json"), cancellationToken);
        var shotPlan = await ReadJsonAsync<ResolvedRenderShotPlan>(Path.Combine(renderDirectory, "audio-driven-resolved-render-shot-plan.json"), cancellationToken);
        var manifest = await ReadJsonAsync<WeeklyProductionAssetManifest>(Path.Combine(episodeDirectory, "weekly-production-asset-manifest.json"), cancellationToken);
        var narrationAssetMap = await ReadJsonAsync<IReadOnlyList<NarrationAssetMapEntry>>(Path.Combine(episodeDirectory, "narration-asset-map.json"), cancellationToken) ?? [];
        var narrationTimelineMap = await ReadNarrationTimelineMapAsync(Path.Combine(episodeDirectory, "narration-timeline-map.json"), cancellationToken);
        var longformNarration = await ReadJsonAsync<WeeklyNarrationPackage>(Path.Combine(episodeDirectory, "longform-narration.json"), cancellationToken);
        var shortformNarration = await ReadJsonAsync<WeeklyNarrationPackage>(Path.Combine(episodeDirectory, "shortform-narration.json"), cancellationToken);

        if (timeline is null)
            errors.Add("audio-driven-final-render-timeline.json could not be parsed.");
        if (shotPlan is null)
            errors.Add("audio-driven-resolved-render-shot-plan.json could not be parsed.");
        if (manifest is null)
            errors.Add("weekly-production-asset-manifest.json could not be parsed.");
        if (errors.Count > 0)
            return await PersistFailureAsync(pipelineRunId, root, renderDirectory, inputPaths, warnings, errors, cancellationToken);

        manifest = EnrichProductionAssetManifest(manifest!);
        await File.WriteAllTextAsync(Path.Combine(episodeDirectory, "weekly-production-asset-manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);

        var catalog = BuildAssetCatalog(manifest!, shotPlan!, timeline!);
        var narrationBySegment = longformNarration?.Segments.Concat(shortformNarration?.Segments ?? []).ToDictionary(x => x.SegmentId, x => x.NarrationText, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in narrationAssetMap)
        {
            if (!narrationBySegment.ContainsKey(entry.SegmentId) && !string.IsNullOrWhiteSpace(entry.NarrationText))
                narrationBySegment[entry.SegmentId] = entry.NarrationText;
        }
        if (narrationTimelineMap.Count == 0)
            warnings.Add("narration-timeline-map.json was present but contained no beat-to-asset timing entries.");

        var beats = new List<WeeklyVisualIntentBeat>();
        var internalRequests = new List<WeeklyInternalCelestialAssetRequest>();
        var previousFamilies = new Queue<string>();
        foreach (var (episodeType, sourceSegment) in EnumerateTimelineSegments(timeline!))
        {
            var segment = sourceSegment;
            if (!string.Equals(sourceSegment.EpisodeType, episodeType, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Timeline segment {sourceSegment.SegmentId} carried episode type '{sourceSegment.EpisodeType}' inside the {episodeType} episode; visual intent normalized it to '{episodeType}'.");
                segment = sourceSegment with { EpisodeType = episodeType };
            }

            var narrationText = ResolveNarrationText(segment, narrationBySegment);
            var intent = ClassifyIntent(segment.SegmentType, narrationText, episodeType, segment.StartSecond);
            var mentionedObjects = DetectMentionedObjects(narrationText, segment.SegmentType);
            var narrationSubject = ResolveNarrationSubject(mentionedObjects, narrationText, segment.SegmentType);
            var candidatePool = BuildPrimaryVisualCandidatePool(segment, intent, narrationSubject, mentionedObjects, catalog);
            var candidate = SelectPrimaryVisual(segment, candidatePool, previousFamilies, warnings);
            if (candidate is null)
            {
                candidate = BuildUnavailableSelection(segment, intent, mentionedObjects, internalRequests, "No suitable production visual was found for the beat's editorial intent.");
            }

            var overlays = SelectOverlays(segment, intent, catalog);
            var secondary = SelectSecondaryVisual(segment, intent, mentionedObjects, catalog, candidate.VisualFamily);
            var rules = ResolveRulesApplied(intent, mentionedObjects, narrationText).ToList();
            var beatWarnings = new List<string>();
            if (candidate.RequestedButUnavailable) beatWarnings.Add("Primary visual requested through InternalCelestial fallback but unavailable in this phase.");
            if (!MatchesNarration(candidate, mentionedObjects, intent)) beatWarnings.Add("Primary visual is editorial fallback rather than direct narration/object match.");
            if (overlays.Any(x => !x.IsOverlay)) beatWarnings.Add("Overlay asset was not marked overlay-safe.");

            var beat = new WeeklyVisualIntentBeat(
                $"{segment.EpisodeType}-{segment.SegmentId}",
                segment.EpisodeType,
                segment.SegmentId,
                segment.SegmentType,
                intent,
                segment.StartSecond,
                segment.EndSecond,
                segment.DurationSeconds,
                narrationText,
                narrationSubject,
                mentionedObjects,
                rules,
                candidate,
                secondary,
                overlays,
                MatchesNarration(candidate, mentionedObjects, intent),
                candidatePool,
                beatWarnings);
            beats.Add(beat);
            TrackFamily(previousFamilies, candidate.VisualFamily);
        }

        EnsureShortformStrongVisual(beats, catalog, warnings);
        EnsureObjectCoverage(beats, catalog, warnings);
        var rotationResult = ApplyVisualFamilyRotation(beats, warnings);
        var validation = BuildValidation(beats, warnings, errors, rotationResult.Applied, rotationResult.SwapCount);
        var familyDistributionReport = BuildVisualFamilyDistributionReport(beats, validation, rotationResult, warnings, errors);
        var plan = new WeeklyVisualIntentPlan(
            pipelineRunId,
            DateTime.UtcNow,
            "weekly-visual-intent-v1",
            inputPaths,
            beats,
            new WeeklyVisualIntentAssetMix(40, 15, 20, 12, 8),
            BuildMix(beats.Where(x => x.EpisodeType.Equals("longform", StringComparison.OrdinalIgnoreCase))),
            new WeeklyVisualIntentAssetMix(45, 30, 10, 8, 4),
            BuildMix(beats.Where(x => x.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase))),
            internalRequests.DistinctBy(x => $"{x.EpisodeType}:{x.SegmentId}:{x.ObjectCode}:{x.Reason}").ToList(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        var visualShotPlan = BuildShotPlan(pipelineRunId, timeline, beats);
        var storyboard = BuildStoryboard(pipelineRunId, beats);
        var storytellingReport = BuildStorytellingReport(beats, validation);
        var rebuiltTimeline = RebuildTimelineFromStoryboard(timeline!, storyboard);

        var planPath = Path.Combine(renderDirectory, "visual-intent-plan.json");
        var storyboardPath = Path.Combine(renderDirectory, "visual-intent-storyboard.json");
        var storytellingReportPath = Path.Combine(renderDirectory, "visual-storytelling-report.json");
        var visualShotPlanPath = Path.Combine(renderDirectory, "visual-intent-shot-plan.json");
        var validationPath = Path.Combine(renderDirectory, "visual-intent-validation-report.json");
        var familyDistributionReportPath = Path.Combine(renderDirectory, "visual-family-distribution-report.json");
        var audioDrivenTimelinePath = Path.Combine(renderDirectory, "audio-driven-final-render-timeline.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(storyboardPath, JsonSerializer.Serialize(storyboard, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(storytellingReportPath, JsonSerializer.Serialize(storytellingReport, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(visualShotPlanPath, JsonSerializer.Serialize(visualShotPlan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(familyDistributionReportPath, JsonSerializer.Serialize(familyDistributionReport, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(audioDrivenTimelinePath, JsonSerializer.Serialize(rebuiltTimeline, JsonOptions), cancellationToken);

        logger.LogInformation("WEEKLY_VISUAL_INTENT_COMPLETE pipelineRunId={PipelineRunId} ready={Ready} totalBeats={TotalBeats} mismatches={MismatchCount}", pipelineRunId, validation.VisualIntentReady, validation.TotalBeats, validation.NarrationVisualMismatchCount);
        return ToResponse(pipelineRunId, root, planPath, visualShotPlanPath, validationPath, validation);
    }

    private static async Task<IReadOnlyList<NarrationTimelineMapEntry>> ReadNarrationTimelineMapAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var segments = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("segments", out var segmentElement)
                ? segmentElement
                : default;

        if (segments.ValueKind is not JsonValueKind.Array)
            throw new JsonException("narration-timeline-map.json must be either a timeline entry array or an object with a segments array.");

        return segments.Deserialize<IReadOnlyList<NarrationTimelineMapEntry>>(JsonOptions) ?? [];
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static WeeklyProductionAssetManifest EnrichProductionAssetManifest(WeeklyProductionAssetManifest manifest)
    {
        var bundles = (manifest.SegmentBundles ?? [])
            .Select(bundle => bundle with
            {
                AssignedVisualAssets = (bundle.AssignedVisualAssets ?? [])
                    .Select(asset =>
                    {
                        var family = NormalizeFamily(asset.SourceType.ToString(), asset.FilePath, asset.AssetCode);
                        var supportedObjects = DetectObjects(BuildSearchText(asset.AssetId, asset.AssetCode, asset.FilePath, bundle.SegmentId, bundle.SegmentType, asset.SegmentUsageRole));
                        var intentTags = ResolveAssetIntentTags(asset.AssetId, asset.AssetCode, asset.FilePath, family, bundle.SegmentType, asset.SegmentUsageRole, supportedObjects);
                        var supportedSegments = intentTags.Concat([bundle.SegmentType, bundle.SegmentId]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        return asset with
                        {
                            Family = family,
                            IntentTags = intentTags,
                            SupportedObjects = supportedObjects,
                            SupportedSegments = supportedSegments
                        };
                    })
                    .ToList()
            })
            .ToList();
        return manifest with { SegmentBundles = bundles };
    }

    private static IReadOnlyList<string> ResolveAssetIntentTags(string assetId, string assetCode, string assetPath, string family, string segmentType, string usageRole, IReadOnlyList<string> supportedObjects)
    {
        var text = BuildSearchText(assetId, assetCode, assetPath, segmentType, usageRole).ToLowerInvariant();
        var tags = new List<string>();
        void Add(params string[] values) => tags.AddRange(values);

        if (family.Equals("MotionGraphic", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Contains("weekly-overview-timeline")) Add("WeeklyOverview", "WeeklySummary");
            if (text.Contains("best-observation-window-card")) Add("DirectionGuidance", "ObservationWindow", "BestTime");
            if (text.Contains("visibility-calendar")) Add("ObservationWindow", "BestTime");
            if (text.Contains("where-to-look-card")) Add("DirectionGuidance");
            if (text.Contains("best-time-card")) Add("BestTime");
            if (text.Contains("call-to-action-card")) Add("CTA");
            if (text.Contains("hero-event-card")) Add("HeroEvent");
        }
        else if (family.Equals("EducationalOverlay", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Contains("planet-visibility-explainer")) Add("EducationalExplanation", "ObservationGuidance");
            else Add("EducationalExplanation");
        }
        else if (family.Equals("AICinematic", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Contains("opening_hook")) Add("Hook");
            if (text.Contains("retention_reset")) Add("RetentionReset");
            if (text.Contains("weekly_summary")) Add("Summary");
            if (text.Contains("call_to_action")) Add("CTA");
            if (tags.Count == 0) Add("Hook", "Summary");
        }
        else if (family.Equals("Stellarium", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Contains("moon_hero_scene")) Add("Observation", "Moon");
            if (text.Contains("western_planet_grouping_scene_venus")) Add("Observation", "Venus");
            if (text.Contains("western_planet_grouping_scene_saturn")) Add("Observation", "Saturn");
            if (text.Contains("expanded") || text.Contains("astrophotography")) Add("AstrophotographyTip");
            if (tags.Count == 0) Add("Observation");
        }
        else if (family is "NASA" or "JWST" or "InternalCelestial" or "CelestialReference")
        {
            Add("ScientificContext");
        }

        foreach (var supportedObject in supportedObjects)
            Add(supportedObject);

        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<AssetCandidate> BuildAssetCatalog(WeeklyProductionAssetManifest manifest, ResolvedRenderShotPlan shotPlan, FinalRenderTimeline timeline)
    {
        var manifestAssets = (manifest.SegmentBundles ?? [])
            .SelectMany(bundle => (bundle.AssignedVisualAssets ?? []).Select(asset => new AssetCandidate(
                asset.AssetId,
                asset.AssetCode,
                asset.SourceType.ToString(),
                NormalizeFamily(asset.SourceType.ToString(), asset.FilePath, asset.AssetCode),
                asset.FilePath,
                bundle.SegmentId,
                bundle.EpisodeType,
                bundle.SegmentType,
                asset.Exists && asset.ProductionReady,
                DetectObjects(BuildSearchText(asset.AssetId, asset.AssetCode, asset.FilePath, bundle.SegmentId, bundle.SegmentType, asset.SourceType.ToString(), asset.SegmentUsageRole))))
            .ToList());

        var renderedAssets = shotPlan.Episodes.SelectMany(episode => episode.Segments.SelectMany(segment => segment.Shots.Select(shot => new AssetCandidate(
                shot.AssetId,
                shot.AssetId,
                shot.AssetType,
                NormalizeFamily(shot.AssetType, shot.AssetPath, shot.AssetId),
                shot.AssetPath,
                segment.SegmentId,
                episode.EpisodeType,
                segment.SegmentType,
                string.IsNullOrWhiteSpace(shot.AssetPath) || File.Exists(shot.AssetPath) || !Path.IsPathRooted(shot.AssetPath),
                DetectObjects(BuildSearchText(shot.AssetId, shot.AssetType, shot.AssetPath, segment.SegmentId, segment.SegmentType, shot.Purpose, shot.LayoutMode)))))
            .Concat(EnumerateTimelineSegments(timeline).SelectMany(row => row.Segment.Shots.Select(shot => new AssetCandidate(
                shot.AssetId,
                shot.AssetId,
                shot.AssetType,
                NormalizeFamily(shot.AssetType, shot.AssetPath, shot.AssetId),
                shot.AssetPath,
                row.Segment.SegmentId,
                row.EpisodeType,
                row.Segment.SegmentType,
                string.IsNullOrWhiteSpace(shot.AssetPath) || File.Exists(shot.AssetPath) || !Path.IsPathRooted(shot.AssetPath),
                DetectObjects(BuildSearchText(shot.AssetId, shot.AssetType, shot.AssetPath, row.Segment.SegmentId, row.Segment.SegmentType, shot.Purpose)))))
            .ToList()));

        return manifestAssets.Concat(renderedAssets)
            .Where(x => !string.IsNullOrWhiteSpace(x.AssetId) || !string.IsNullOrWhiteSpace(x.AssetPath))
            .GroupBy(x => $"{x.AssetId}|{x.AssetPath}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private static IEnumerable<(string EpisodeType, FinalRenderSegment Segment)> EnumerateTimelineSegments(FinalRenderTimeline timeline)
    {
        foreach (var segment in timeline.Longform.Segments)
            yield return ("longform", segment);
        foreach (var segment in timeline.Shortform.Segments)
            yield return ("shortform", segment);
    }

    private static WeeklyVisualIntentType ClassifyIntent(string segmentType, string narration, string episodeType, double startSecond)
    {
        var text = $"{segmentType} {narration}".ToLowerInvariant();
        if (segmentType.Contains("CallToAction", StringComparison.OrdinalIgnoreCase) || text.Contains("subscribe") || text.Contains("follow")) return WeeklyVisualIntentType.CallToAction;
        if (segmentType.Contains("Retention", StringComparison.OrdinalIgnoreCase) || text.Contains("don't miss") || text.Contains("stay with")) return WeeklyVisualIntentType.RetentionReset;
        if (segmentType.Contains("Summary", StringComparison.OrdinalIgnoreCase) || text.Contains("recap") || text.Contains("in summary")) return WeeklyVisualIntentType.Summary;
        if (segmentType.Contains("Hook", StringComparison.OrdinalIgnoreCase) || startSecond <= 1) return WeeklyVisualIntentType.Hook;
        if (segmentType.Contains("HeroEvent", StringComparison.OrdinalIgnoreCase)) return WeeklyVisualIntentType.HeroEvent;
        if (segmentType.Contains("StrongestEvent", StringComparison.OrdinalIgnoreCase)) return WeeklyVisualIntentType.StrongestEvent;
        if (segmentType.Contains("MoonHighlight", StringComparison.OrdinalIgnoreCase) || text.Contains("moon") || text.Contains("lunar") || text.Contains("chandra") || text.Contains("चंद्र") || text.Contains("चाँद")) return WeeklyVisualIntentType.MoonHighlights;
        if (segmentType.Contains("PlanetHighlight", StringComparison.OrdinalIgnoreCase) || text.Contains("saturn") || text.Contains("venus") || text.Contains("shani") || text.Contains("shukra") || text.Contains("शनि") || text.Contains("शुक्र")) return WeeklyVisualIntentType.PlanetHighlights;
        if (text.Contains("camera") || text.Contains("photo") || text.Contains("astrophotography") || text.Contains("exposure") || text.Contains("tripod")) return WeeklyVisualIntentType.AstrophotographyTip;
        if (text.Contains("why") || text.Contains("rings") || text.Contains("phase") || text.Contains("detail") || text.Contains("science")) return WeeklyVisualIntentType.ScientificContext;
        if (text.Contains("visibility") || text.Contains("calendar") || text.Contains("window")) return WeeklyVisualIntentType.ObservationWindow;
        if (text.Contains("look") || text.Contains("direction") || text.Contains("east") || text.Contains("west") || text.Contains("north") || text.Contains("south") || text.Contains("horizon") || text.Contains("ऊपर") || text.Contains("क्षितिज")) return WeeklyVisualIntentType.DirectionGuidance;
        if (text.Contains("best time") || text.Contains("time") || text.Contains("after sunset") || text.Contains("before sunrise") || text.Contains("minutes") || text.Contains(" बजे")) return WeeklyVisualIntentType.BestTime;
        if (text.Contains("learn") || text.Contains("explain") || text.Contains("checklist") || text.Contains("समझ")) return WeeklyVisualIntentType.EducationalExplanation;
        return WeeklyVisualIntentType.Observation;
    }

    private static string BuildSearchText(params string?[] values)
        => string.Join(' ', values.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static IReadOnlyList<string> DetectMentionedObjects(string narration, string segmentType)
        => DetectObjects($"{narration} {segmentType}");

    private static string ResolveNarrationSubject(IReadOnlyList<string> mentionedObjects, string narration, string segmentType)
    {
        var primary = mentionedObjects.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(primary)) return primary;
        var text = $"{narration} {segmentType}";
        if (text.Contains("sky", StringComparison.OrdinalIgnoreCase) || text.Contains("night", StringComparison.OrdinalIgnoreCase)) return "Sky";
        return "Astronomy";
    }

    private static IReadOnlyList<string> DetectObjects(string text)
    {
        var value = text.ToLowerInvariant();
        var objects = new List<string>();
        if (value.Contains("saturn") || value.Contains("shani") || value.Contains("शनि")) objects.Add("Saturn");
        if (value.Contains("venus") || value.Contains("shukra") || value.Contains("शुक्र")) objects.Add("Venus");
        if (value.Contains("moon") || value.Contains("lunar") || value.Contains("chandra") || value.Contains("चंद्र") || value.Contains("चाँद")) objects.Add("Moon");
        if (value.Contains("jupiter") || value.Contains("बृहस्पति")) objects.Add("Jupiter");
        if (value.Contains("mars") || value.Contains("मंगल")) objects.Add("Mars");
        if (value.Contains("mercury") || value.Contains("बुध")) objects.Add("Mercury");
        return objects.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<WeeklyVisualIntentAssetCandidate> BuildPrimaryVisualCandidatePool(FinalRenderSegment segment, WeeklyVisualIntentType intent, string narrationSubject, IReadOnlyList<string> mentionedObjects, IReadOnlyList<AssetCandidate> catalog)
    {
        var sameSegment = catalog.Where(x => x.SegmentId.Equals(segment.SegmentId, StringComparison.OrdinalIgnoreCase)).ToList();
        var uniqueAssets = sameSegment.Concat(catalog)
            .Where(x => x.ProductionReady)
            .DistinctBy(x => $"{x.AssetId}|{x.AssetPath}")
            .ToList();
        var astronomyAssetExists = uniqueAssets.Any(x => IsAstronomyPrimaryFamily(x.VisualFamily)
            && (mentionedObjects.Count == 0 || mentionedObjects.Any(o => AssetMatchesObject(x, o))));

        return uniqueAssets
            .Select(asset => ToVisualIntentCandidate(asset, segment, intent, narrationSubject, mentionedObjects, astronomyAssetExists))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.IsEligibleAsPrimary)
            .ThenBy(x => x.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static WeeklyVisualIntentAssetSelection? SelectPrimaryVisual(FinalRenderSegment segment, IReadOnlyList<WeeklyVisualIntentAssetCandidate> candidatePool, Queue<string> previousFamilies, List<string> warnings)
    {
        var candidates = candidatePool
            .Where(x => x.IsEligibleAsPrimary && x.Score > 0)
            .ToList();

        var selected = Pick(candidates, previousFamilies);
        if (selected is not null) return ToSelection(selected, "primary", false, segment.StartSecond, segment.EndSecond);

        warnings.Add($"Primary visual fallback required for segment {segment.SegmentId}; no eligible production candidate scored above zero.");
        return null;
    }

    private static WeeklyVisualIntentAssetCandidate ToVisualIntentCandidate(AssetCandidate asset, FinalRenderSegment segment, WeeklyVisualIntentType intent, string narrationSubject, IReadOnlyList<string> mentionedObjects, bool astronomyAssetExists)
    {
        var matchedSubjects = mentionedObjects.Where(o => AssetMatchesObject(asset, o)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var score = ScoreVisualCandidate(asset, segment, intent, narrationSubject, mentionedObjects, matchedSubjects, astronomyAssetExists);
        var eligiblePrimary = IsEligibleAsPrimary(asset, segment, intent, mentionedObjects, astronomyAssetExists);
        var eligibleOverlay = IsEligibleAsOverlay(asset, intent);
        var reason = ResolveCandidateReason(asset, intent, matchedSubjects, eligiblePrimary, eligibleOverlay, astronomyAssetExists);
        return new WeeklyVisualIntentAssetCandidate(asset.AssetId, asset.AssetPath, asset.VisualFamily, asset.AssetType, asset.AssetCode, asset.MatchedObjects, matchedSubjects, score, eligiblePrimary, eligibleOverlay, reason);
    }

    private static AssetCandidate ToAssetCandidate(WeeklyVisualIntentAssetCandidate candidate, string segmentId, string episodeType, string segmentType)
        => new(candidate.AssetId, candidate.SceneCode, candidate.SourceType, candidate.Family, candidate.Path, segmentId, episodeType, segmentType, true, candidate.TargetObjects);

    private static string ResolveCandidateReason(AssetCandidate asset, WeeklyVisualIntentType intent, IReadOnlyList<string> matchedSubjects, bool eligiblePrimary, bool eligibleOverlay, bool astronomyAssetExists)
    {
        var parts = new List<string>();
        if (matchedSubjects.Count > 0) parts.Add($"matched {string.Join(", ", matchedSubjects)}");
        parts.Add($"family {asset.VisualFamily} scored for {intent}");
        parts.Add(eligiblePrimary ? "eligible primary" : "not primary eligible");
        if (eligibleOverlay) parts.Add("overlay eligible");
        if (asset.VisualFamily.Equals("AICinematic", StringComparison.OrdinalIgnoreCase) && astronomyAssetExists && !eligiblePrimary) parts.Add("AI cinematic restricted because astronomy/celestial asset exists");
        return string.Join("; ", parts);
    }

    private static WeeklyVisualIntentAssetSelection? SelectSecondaryVisual(FinalRenderSegment segment, WeeklyVisualIntentType intent, IReadOnlyList<string> mentionedObjects, IReadOnlyList<AssetCandidate> catalog, string primaryFamily)
    {
        var secondaryFamily = intent switch
        {
            WeeklyVisualIntentType.Hook => "Stellarium",
            WeeklyVisualIntentType.Observation => "CelestialReference",
            WeeklyVisualIntentType.ScientificContext => "Stellarium",
            WeeklyVisualIntentType.Summary => "MotionGraphic",
            _ => null
        };
        if (secondaryFamily is null) return null;
        var selected = catalog
            .Where(x => x.VisualFamily.Equals(secondaryFamily, StringComparison.OrdinalIgnoreCase) && !x.VisualFamily.Equals(primaryFamily, StringComparison.OrdinalIgnoreCase))
            .Where(x => mentionedObjects.Count == 0 || mentionedObjects.Any(o => x.MatchedObjects.Contains(o, StringComparer.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.SegmentId.Equals(segment.SegmentId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => mentionedObjects.Any(o => x.MatchedObjects.Contains(o, StringComparer.OrdinalIgnoreCase)))
            .FirstOrDefault();
        return selected is null ? null : ToSelection(selected, "secondary_support", false, segment.StartSecond, segment.EndSecond);
    }

    private static IReadOnlyList<WeeklyVisualIntentAssetSelection> SelectOverlays(FinalRenderSegment segment, WeeklyVisualIntentType intent, IReadOnlyList<AssetCandidate> catalog)
    {
        var overlayFamily = intent switch
        {
            WeeklyVisualIntentType.DirectionGuidance or WeeklyVisualIntentType.BestTime or WeeklyVisualIntentType.ObservationWindow or WeeklyVisualIntentType.Summary => "MotionGraphic",
            WeeklyVisualIntentType.EducationalExplanation => "EducationalOverlay",
            WeeklyVisualIntentType.AstrophotographyTip => "EducationalOverlay",
            WeeklyVisualIntentType.CallToAction => "MotionGraphic",
            _ => null
        };
        if (overlayFamily is null) return [];
        var selected = catalog
            .Where(x => x.VisualFamily.Equals(overlayFamily, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.SegmentId.Equals(segment.SegmentId, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (selected is null) return [];
        var duration = Math.Min(segment.DurationSeconds, overlayFamily.Equals("MotionGraphic", StringComparison.OrdinalIgnoreCase) ? 3 : Math.Max(3, Math.Min(6, segment.DurationSeconds)));
        return [ToSelection(selected, overlayFamily.Equals("MotionGraphic", StringComparison.OrdinalIgnoreCase) ? "lower_third_overlay" : "educational_overlay", true, segment.StartSecond, segment.StartSecond + duration)];
    }

    private static string[] PreferredPrimaryFamilies(WeeklyVisualIntentType intent, string narration, string segmentType, IReadOnlyList<string> mentionedObjects)
    {
        var text = $"{segmentType} {narration}";
        if (intent is WeeklyVisualIntentType.CallToAction) return ["AICinematic"];
        if (intent is WeeklyVisualIntentType.Summary) return ["AICinematic", "MotionGraphic", "Stellarium", "CelestialReference"];
        if (intent is WeeklyVisualIntentType.MoonHighlights || IsMoonHighlight(text, mentionedObjects)) return ["Stellarium", "CelestialReference", "NASA", "AICinematic"];
        if (intent is WeeklyVisualIntentType.PlanetHighlights or WeeklyVisualIntentType.HeroEvent or WeeklyVisualIntentType.StrongestEvent || IsPlanetHighlight(text, mentionedObjects) || IsHeroOrStrongestEvent(segmentType)) return ["Stellarium", "CelestialReference", "NASA", "JWST", "AICinematic"];
        return intent switch
        {
            WeeklyVisualIntentType.Hook or WeeklyVisualIntentType.RetentionReset => ["AICinematic", "Stellarium"],
            WeeklyVisualIntentType.Observation => ["Stellarium", "CelestialReference", "AICinematic"],
            WeeklyVisualIntentType.DirectionGuidance => ["Stellarium", "MotionGraphic", "AICinematic"],
            WeeklyVisualIntentType.BestTime or WeeklyVisualIntentType.ObservationWindow => ["Stellarium", "MotionGraphic", "AICinematic"],
            WeeklyVisualIntentType.ScientificContext => ["CelestialReference", "NASA", "JWST", "Stellarium", "AICinematic"],
            WeeklyVisualIntentType.EducationalExplanation => ["Stellarium", "CelestialReference", "AICinematic"],
            WeeklyVisualIntentType.AstrophotographyTip => ["Stellarium", "CelestialReference", "NASA", "AICinematic"],
            _ => ["Stellarium", "CelestialReference", "AICinematic"]
        };
    }

    private static bool ShouldRestrictAICinematicPrimary(FinalRenderSegment segment, WeeklyVisualIntentType intent, IReadOnlyList<string> mentionedObjects)
    {
        if (IsHeroOrStrongestEvent(segment.SegmentType)) return true;
        if (segment.SegmentType.Contains("MoonHighlight", StringComparison.OrdinalIgnoreCase)) return true;
        if (segment.SegmentType.Contains("PlanetHighlight", StringComparison.OrdinalIgnoreCase)) return true;
        if (intent is WeeklyVisualIntentType.Hook or WeeklyVisualIntentType.Summary or WeeklyVisualIntentType.CallToAction) return false;
        if (IsMoonHighlight($"{segment.SegmentType} {segment.NarrationText}", mentionedObjects)) return true;
        if (IsPlanetHighlight($"{segment.SegmentType} {segment.NarrationText}", mentionedObjects)) return true;
        return false;
    }

    private static bool HasAstronomyPrimaryAlternative(FinalRenderSegment segment, WeeklyVisualIntentType intent, string narrationSubject, IReadOnlyList<string> mentionedObjects, IReadOnlyList<string> preferredFamilies, IReadOnlyList<AssetCandidate> catalog)
        => catalog.Any(x => x.ProductionReady
            && IsAstronomyPrimaryFamily(x.VisualFamily)
            && ScoreVisualMatch(x, segment, intent, narrationSubject, mentionedObjects, preferredFamilies) > 0);

    private static bool IsAstronomyPrimaryFamily(string family)
        => family is "Stellarium" or "NASA" or "JWST" or "InternalCelestial" or "CelestialReference";

    private static bool IsHeroOrStrongestEvent(string segmentType)
        => segmentType.Contains("HeroEvent", StringComparison.OrdinalIgnoreCase)
            || segmentType.Contains("StrongestEvent", StringComparison.OrdinalIgnoreCase);

    private static bool IsMoonHighlight(string text, IReadOnlyList<string> mentionedObjects)
        => text.Contains("MoonHighlight", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Moon", StringComparison.OrdinalIgnoreCase)
            || mentionedObjects.Contains("Moon", StringComparer.OrdinalIgnoreCase);

    private static bool IsPlanetHighlight(string text, IReadOnlyList<string> mentionedObjects)
        => text.Contains("PlanetHighlight", StringComparison.OrdinalIgnoreCase)
            || mentionedObjects.Contains("Saturn", StringComparer.OrdinalIgnoreCase)
            || mentionedObjects.Contains("Venus", StringComparer.OrdinalIgnoreCase);

    private static int PrimaryFamilyRank(string family, IReadOnlyList<string> preferredFamilies)
    {
        var index = preferredFamilies.ToList().FindIndex(x => x.Equals(family, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : preferredFamilies.Count + 1;
    }

    private static int ScoreVisualMatch(AssetCandidate asset, FinalRenderSegment segment, WeeklyVisualIntentType intent, string narrationSubject, IReadOnlyList<string> mentionedObjects, IReadOnlyList<string> preferredFamilies)
    {
        var score = 0;
        var hasAstronomicalSubject = mentionedObjects.Count > 0;
        if (hasAstronomicalSubject)
        {
            if (!AssetMatchesObject(asset, narrationSubject)) return 0;
            score += 100; // exact narration subject match
            if (asset.MatchedObjects.Contains(narrationSubject, StringComparer.OrdinalIgnoreCase)) score += 80; // target object/object labels contain subject
            if (ContainsObjectName(asset.AssetPath, narrationSubject)) score += 70;
            if (ContainsObjectName(asset.AssetCode, narrationSubject) || ContainsObjectName(asset.AssetId, narrationSubject)) score += 60;
            if (IsGroupingAsset(asset) && asset.MatchedObjects.Contains(narrationSubject, StringComparer.OrdinalIgnoreCase)) score += 30;
        }
        else if (asset.MatchedObjects.Count > 0)
        {
            score += 10;
        }

        var familyRank = PrimaryFamilyRank(asset.VisualFamily, preferredFamilies);
        if (familyRank <= preferredFamilies.Count) score += (preferredFamilies.Count - familyRank) * 1_000;
        if (intent is WeeklyVisualIntentType.AstrophotographyTip && asset.AssetType.Contains("StellariumExpanded", StringComparison.OrdinalIgnoreCase)) score += 125;
        if (asset.SegmentType.Equals(segment.SegmentType, StringComparison.OrdinalIgnoreCase)) score += 25;
        if (asset.SegmentId.Equals(segment.SegmentId, StringComparison.OrdinalIgnoreCase)) score += 15;
        return score;
    }

    private static int ScoreVisualCandidate(AssetCandidate asset, FinalRenderSegment segment, WeeklyVisualIntentType intent, string narrationSubject, IReadOnlyList<string> mentionedObjects, IReadOnlyList<string> matchedSubjects, bool astronomyAssetExists)
    {
        var score = 0;
        if (mentionedObjects.Count > 0)
        {
            if (matchedSubjects.Count == 0) return 0;
            if (AssetMatchesObject(asset, narrationSubject)) score += 100;
            foreach (var subject in matchedSubjects)
            {
                if (asset.MatchedObjects.Contains(subject, StringComparer.OrdinalIgnoreCase)) score += 80;
                if (ContainsObjectName(asset.AssetPath, subject)) score += 70;
                if (ContainsObjectName(asset.AssetCode, subject) || ContainsObjectName(asset.AssetId, subject)) score += 60;
            }
        }
        else if (asset.MatchedObjects.Count > 0)
        {
            score += 20;
        }

        if (IsFamilySuitableForIntent(asset.VisualFamily, intent, segment.SegmentType, mentionedObjects)) score += 40;
        score += FamilyIntentBonus(asset, intent, segment, mentionedObjects, astronomyAssetExists);
        if (asset.SegmentType.Equals(segment.SegmentType, StringComparison.OrdinalIgnoreCase)) score += 25;
        if (asset.SegmentId.Equals(segment.SegmentId, StringComparison.OrdinalIgnoreCase)) score += 15;
        return score;
    }

    private static int FamilyIntentBonus(AssetCandidate asset, WeeklyVisualIntentType intent, FinalRenderSegment segment, IReadOnlyList<string> mentionedObjects, bool astronomyAssetExists)
    {
        var family = asset.VisualFamily;
        var text = $"{segment.SegmentType} {segment.NarrationText}";
        if (intent is WeeklyVisualIntentType.Hook or WeeklyVisualIntentType.RetentionReset)
            return family switch { "AICinematic" => 100, "Stellarium" => 40, _ => 0 };
        if (intent is WeeklyVisualIntentType.HeroEvent or WeeklyVisualIntentType.StrongestEvent || IsHeroOrStrongestEvent(segment.SegmentType))
            return family switch { "Stellarium" => 100, "NASA" or "JWST" or "InternalCelestial" or "CelestialReference" => 80, "AICinematic" => astronomyAssetExists ? -100 : 0, _ => 0 };
        if (intent is WeeklyVisualIntentType.MoonHighlights || IsMoonHighlight(text, mentionedObjects))
            return family switch { "Stellarium" => 100, "NASA" or "InternalCelestial" or "CelestialReference" => 90, "AICinematic" => -100, _ => 0 };
        if (intent is WeeklyVisualIntentType.PlanetHighlights || IsPlanetHighlight(text, mentionedObjects))
            return family switch { "Stellarium" => 100, "NASA" or "JWST" or "InternalCelestial" or "CelestialReference" => 90, "AICinematic" => -100, _ => 0 };
        return intent switch
        {
            WeeklyVisualIntentType.ScientificContext => family switch { "NASA" or "JWST" or "InternalCelestial" or "CelestialReference" => 100, "Stellarium" => 50, "AICinematic" => -50, _ => 0 },
            WeeklyVisualIntentType.DirectionGuidance => family switch { "Stellarium" => 100, "MotionGraphic" => 80, "AICinematic" => -50, _ => 0 },
            WeeklyVisualIntentType.BestTime or WeeklyVisualIntentType.ObservationWindow => family switch { "Stellarium" => 100, "MotionGraphic" => 60, "AICinematic" => -30, _ => 0 },
            WeeklyVisualIntentType.Summary => family switch { "AICinematic" => 90, "MotionGraphic" => 60, _ => 0 },
            WeeklyVisualIntentType.CallToAction => family switch { "AICinematic" => 100, "MotionGraphic" => 70, _ => 0 },
            _ => 0
        };
    }

    private static bool IsFamilySuitableForIntent(string family, WeeklyVisualIntentType intent, string segmentType, IReadOnlyList<string> mentionedObjects)
    {
        if (IsHeroOrStrongestEvent(segmentType) || IsMoonHighlight(segmentType, mentionedObjects) || IsPlanetHighlight(segmentType, mentionedObjects))
            return family is "Stellarium" or "NASA" or "JWST" or "InternalCelestial" or "CelestialReference";
        return intent switch
        {
            WeeklyVisualIntentType.Hook or WeeklyVisualIntentType.RetentionReset or WeeklyVisualIntentType.Summary or WeeklyVisualIntentType.CallToAction => family is "AICinematic" or "Stellarium",
            WeeklyVisualIntentType.ScientificContext => family is "NASA" or "JWST" or "InternalCelestial" or "CelestialReference" or "Stellarium",
            WeeklyVisualIntentType.DirectionGuidance or WeeklyVisualIntentType.BestTime or WeeklyVisualIntentType.ObservationWindow => family is "Stellarium" or "MotionGraphic",
            _ => family is "Stellarium" or "NASA" or "JWST" or "InternalCelestial" or "CelestialReference"
        };
    }

    private static bool IsEligibleAsPrimary(AssetCandidate asset, FinalRenderSegment segment, WeeklyVisualIntentType intent, IReadOnlyList<string> mentionedObjects, bool astronomyAssetExists)
    {
        if (!asset.ProductionReady) return false;
        if (asset.VisualFamily.Equals("EducationalOverlay", StringComparison.OrdinalIgnoreCase)) return false;
        if (asset.VisualFamily.Equals("MotionGraphic", StringComparison.OrdinalIgnoreCase)) return false;
        if (asset.VisualFamily.Equals("AICinematic", StringComparison.OrdinalIgnoreCase))
        {
            if (intent is WeeklyVisualIntentType.Hook or WeeklyVisualIntentType.RetentionReset or WeeklyVisualIntentType.Summary or WeeklyVisualIntentType.CallToAction) return true;
            return !astronomyAssetExists;
        }
        return true;
    }

    private static bool IsEligibleAsOverlay(AssetCandidate asset, WeeklyVisualIntentType intent)
        => asset.VisualFamily is "EducationalOverlay"
            || (asset.VisualFamily is "MotionGraphic"
                && intent is WeeklyVisualIntentType.DirectionGuidance or WeeklyVisualIntentType.BestTime or WeeklyVisualIntentType.ObservationWindow or WeeklyVisualIntentType.Summary or WeeklyVisualIntentType.CallToAction);

    private static WeeklyVisualIntentAssetCandidate? Pick(IReadOnlyList<WeeklyVisualIntentAssetCandidate> candidates, Queue<string> previousFamilies)
    {
        if (candidates.Count == 0) return null;
        if (previousFamilies.Count < 2) return candidates[0];

        var lastTwo = previousFamilies.ToList();
        if (!lastTwo[0].Equals(lastTwo[1], StringComparison.OrdinalIgnoreCase)) return candidates[0];

        return candidates.FirstOrDefault(x => !x.Family.Equals(lastTwo[0], StringComparison.OrdinalIgnoreCase)) ?? candidates[0];
    }

    private static ScoredAssetCandidate? Pick(IReadOnlyList<ScoredAssetCandidate> candidates, Queue<string> previousFamilies)
    {
        if (candidates.Count == 0) return null;
        if (previousFamilies.Count < 2) return candidates[0];

        var lastTwo = previousFamilies.ToList();
        if (!lastTwo[0].Equals(lastTwo[1], StringComparison.OrdinalIgnoreCase)) return candidates[0];

        return candidates.FirstOrDefault(x => !x.Asset.VisualFamily.Equals(lastTwo[0], StringComparison.OrdinalIgnoreCase)) ?? candidates[0];
    }

    private static bool AssetMatchesObject(AssetCandidate asset, string objectName)
        => asset.MatchedObjects.Contains(objectName, StringComparer.OrdinalIgnoreCase)
           || ContainsObjectName(asset.AssetId, objectName)
           || ContainsObjectName(asset.AssetCode, objectName)
           || ContainsObjectName(asset.AssetPath, objectName);

    private static bool SelectionMatchesObject(WeeklyVisualIntentAssetSelection selection, string objectName)
        => selection.MatchedObjects.Contains(objectName, StringComparer.OrdinalIgnoreCase)
           || ContainsObjectName(selection.AssetId, objectName)
           || ContainsObjectName(selection.AssetType, objectName)
           || ContainsObjectName(selection.AssetPath, objectName);

    private static bool ContainsObjectName(string? value, string objectName)
        => !string.IsNullOrWhiteSpace(value) && value.Contains(objectName, StringComparison.OrdinalIgnoreCase);

    private static bool IsGroupingAsset(AssetCandidate asset)
        => BuildSearchText(asset.AssetId, asset.AssetCode, asset.AssetPath, asset.SegmentId, asset.SegmentType).Contains("grouping", StringComparison.OrdinalIgnoreCase);

    private static WeeklyVisualIntentAssetSelection ToSelection(AssetCandidate asset, string usage, bool isOverlay, double startSecond, double endSecond)
        => new(asset.AssetId, asset.AssetType, asset.VisualFamily, asset.AssetPath, usage, isOverlay, startSecond, endSecond, Math.Max(0, endSecond - startSecond), asset.MatchedObjects, asset.ProductionReady);

    private static WeeklyVisualIntentAssetSelection ToSelection(WeeklyVisualIntentAssetCandidate candidate, string usage, bool isOverlay, double startSecond, double endSecond)
        => new(candidate.AssetId, candidate.SourceType, candidate.Family, candidate.Path, usage, isOverlay, startSecond, endSecond, Math.Max(0, endSecond - startSecond), candidate.TargetObjects, true);

    private static WeeklyVisualIntentAssetSelection BuildUnavailableSelection(FinalRenderSegment segment, WeeklyVisualIntentType intent, IReadOnlyList<string> mentionedObjects, List<WeeklyInternalCelestialAssetRequest> internalRequests, string reason)
    {
        var objectCode = mentionedObjects.FirstOrDefault() ?? "DeepSkyObject";
        if (intent is WeeklyVisualIntentType.ScientificContext or WeeklyVisualIntentType.EducationalExplanation or WeeklyVisualIntentType.AstrophotographyTip || mentionedObjects.Count > 0)
            internalRequests.Add(new WeeklyInternalCelestialAssetRequest(objectCode, segment.SegmentId, segment.EpisodeType, reason, "requestedButUnavailable"));
        return new WeeklyVisualIntentAssetSelection($"internal-celestial-{objectCode.ToLowerInvariant()}-requested", "InternalCelestial", "InternalCelestial", string.Empty, "primary_internal_celestial_fallback_request", false, segment.StartSecond, segment.EndSecond, segment.DurationSeconds, mentionedObjects, false, true, "InternalCelestial");
    }

    private static IEnumerable<string> ResolveRulesApplied(WeeklyVisualIntentType intent, IReadOnlyList<string> mentionedObjects, string narration)
    {
        yield return $"Intent classified as {intent}.";
        if (mentionedObjects.Contains("Saturn", StringComparer.OrdinalIgnoreCase)) yield return "Saturn narration prefers Saturn visual.";
        if (mentionedObjects.Contains("Venus", StringComparer.OrdinalIgnoreCase)) yield return "Venus narration prefers Venus visual.";
        if (mentionedObjects.Contains("Moon", StringComparer.OrdinalIgnoreCase)) yield return "Moon narration prefers Moon visual.";
        if (narration.Contains("rings", StringComparison.OrdinalIgnoreCase) || narration.Contains("detail", StringComparison.OrdinalIgnoreCase)) yield return "Rings/detail narration prefers NASA/JWST/InternalCelestial.";
        if (intent is WeeklyVisualIntentType.DirectionGuidance or WeeklyVisualIntentType.BestTime) yield return "Direction/time narration uses Stellarium primary plus overlay.";
        if (intent is WeeklyVisualIntentType.EducationalExplanation) yield return "Educational overlay must sit over a real sky/celestial visual.";
    }

    private static bool MatchesNarration(WeeklyVisualIntentAssetSelection selection, IReadOnlyList<string> mentionedObjects, WeeklyVisualIntentType intent)
    {
        if (selection.RequestedButUnavailable) return false;
        if (mentionedObjects.Count > 0 && !mentionedObjects.Any(o => SelectionMatchesObject(selection, o))) return false;
        if ((selection.VisualFamily is "MotionGraphic" or "EducationalOverlay") && !selection.IsOverlay) return false;
        return true;
    }

    private static void EnsureShortformStrongVisual(List<WeeklyVisualIntentBeat> beats, IReadOnlyList<AssetCandidate> catalog, List<string> warnings)
    {
        var firstShort = beats.Where(x => x.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.StartSecond).FirstOrDefault();
        if (firstShort is null || firstShort.StartSecond > 3) return;
        if (firstShort.PrimaryVisual.VisualFamily is not ("MotionGraphic" or "EducationalOverlay") && !firstShort.PrimaryVisual.RequestedButUnavailable) return;
        var strong = catalog.FirstOrDefault(x => x.VisualFamily is "AICinematic" or "Stellarium" or "NASA" or "JWST" or "InternalCelestial" or "CelestialReference" && x.ProductionReady);
        if (strong is null) return;
        var replacement = ToSelection(strong, "shortform_strong_hook_primary", false, firstShort.StartSecond, firstShort.EndSecond);
        var index = beats.IndexOf(firstShort);
        beats[index] = firstShort with { PrimaryVisual = replacement, MatchedToNarration = true, Warnings = firstShort.Warnings.Append("Shortform hook primary visual upgraded to strongest available non-card visual.").ToList() };
        warnings.Add("Shortform hook visual was upgraded to avoid weak full-screen card usage in the first 3 seconds.");
    }


    private static VisualFamilyRotationResult ApplyVisualFamilyRotation(List<WeeklyVisualIntentBeat> beats, List<string> warnings)
    {
        var ordered = beats.Select((Beat, Index) => new { Beat, Index })
            .OrderBy(x => x.Beat.EpisodeType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Beat.StartSecond)
            .ToList();
        var swaps = 0;
        var runFamily = string.Empty;
        var runLength = 0;
        foreach (var row in ordered)
        {
            var beat = beats[row.Index];
            if (beat.PrimaryVisual.VisualFamily.Equals(runFamily, StringComparison.OrdinalIgnoreCase))
            {
                runLength++;
            }
            else
            {
                runFamily = beat.PrimaryVisual.VisualFamily;
                runLength = 1;
            }

            if (runLength < 3) continue;

            var replacement = FindRotationReplacement(beat, runFamily);
            if (replacement is null)
            {
                warnings.Add($"Visual family rotation could not find a compatible different-family primary for segment {beat.SegmentId}; keeping {runFamily}.");
                continue;
            }

            var selection = ToSelection(replacement, "primary_family_rotation", false, beat.StartSecond, beat.EndSecond);
            beats[row.Index] = beat with
            {
                PrimaryVisual = selection,
                SecondaryVisual = beat.SecondaryVisual?.VisualFamily.Equals(selection.VisualFamily, StringComparison.OrdinalIgnoreCase) == true ? null : beat.SecondaryVisual,
                MatchedToNarration = MatchesNarration(selection, beat.MentionedObjects, beat.VisualIntent),
                Warnings = beat.Warnings.Append($"Visual family rotation replaced third consecutive {runFamily} primary with {selection.VisualFamily} from candidate pool.").ToList()
            };
            swaps++;
            runFamily = selection.VisualFamily;
            runLength = 1;
        }

        return new VisualFamilyRotationResult(true, swaps);
    }

    private static WeeklyVisualIntentAssetCandidate? FindRotationReplacement(WeeklyVisualIntentBeat beat, string currentFamily)
        => beat.PrimaryVisualCandidatePool
            .Where(x => x.IsEligibleAsPrimary && x.Score > 0)
            .Where(x => !x.Family.Equals(currentFamily, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static bool FamiliesAreCompatible(string fromFamily, string toFamily)
    {
        if (fromFamily.Equals(toFamily, StringComparison.OrdinalIgnoreCase)) return false;
        return IsRotatablePrimaryFamily(fromFamily) && IsRotatablePrimaryFamily(toFamily);
    }

    private static bool IsRotatablePrimaryFamily(string family)
    {
        return family is "Stellarium" or "NASA" or "JWST" or "InternalCelestial" or "CelestialReference" or "AICinematic";
    }

    private static int EnsureObjectCoverage(List<WeeklyVisualIntentBeat> beats, IReadOnlyList<AssetCandidate> catalog, List<string> warnings)
    {
        var fixes = 0;
        foreach (var objectName in new[] { "Moon", "Venus", "Saturn" })
        {
            if (ObjectMatchPassed(beats, objectName)) continue;
            var row = beats.Select((Beat, Index) => new { Beat, Index })
                .Where(x => x.Beat.MentionedObjects.Contains(objectName, StringComparer.OrdinalIgnoreCase) || x.Beat.NarrationSubject.Equals(objectName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Beat.EpisodeType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Beat.StartSecond)
                .FirstOrDefault();
            if (row is null) continue;

            var replacement = catalog
                .Where(x => x.ProductionReady && x.VisualFamily is not "MotionGraphic" and not "EducationalOverlay")
                .Where(x => AssetMatchesObject(x, objectName))
                .DistinctBy(x => $"{x.AssetId}|{x.AssetPath}")
                .OrderByDescending(x => x.SegmentId.Equals(row.Beat.SegmentId, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.VisualFamily.Equals(row.Beat.PrimaryVisual.VisualFamily, StringComparison.OrdinalIgnoreCase) || FamiliesAreCompatible(row.Beat.PrimaryVisual.VisualFamily, x.VisualFamily))
                .ThenBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (replacement is null) continue;

            var selection = ToSelection(replacement, "primary_object_coverage", false, row.Beat.StartSecond, row.Beat.EndSecond);
            beats[row.Index] = row.Beat with
            {
                PrimaryVisual = selection,
                MatchedToNarration = MatchesNarration(selection, row.Beat.MentionedObjects, row.Beat.VisualIntent),
                Warnings = row.Beat.Warnings.Append($"Object coverage pass selected {objectName} primary visual.").ToList()
            };
            warnings.Add($"Object coverage pass ensured {objectName} visual coverage on segment {row.Beat.SegmentId}.");
            fixes++;
        }
        return fixes;
    }

    private static WeeklyVisualIntentValidationReport BuildValidation(IReadOnlyList<WeeklyVisualIntentBeat> beats, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, bool familyRotationApplied, int familyRotationSwapCount)
    {
        var fullscreenMotion = beats.Count(x => x.PrimaryVisual.VisualFamily.Equals("MotionGraphic", StringComparison.OrdinalIgnoreCase) && !x.PrimaryVisual.IsOverlay && x.PrimaryVisual.DurationSeconds > 3);
        var fullscreenEdu = beats.Count(x => x.PrimaryVisual.VisualFamily.Equals("EducationalOverlay", StringComparison.OrdinalIgnoreCase) && !x.PrimaryVisual.IsOverlay);
        var motionOverlayUsage = beats.SelectMany(x => x.Overlays).Count(x => x.VisualFamily.Equals("MotionGraphic", StringComparison.OrdinalIgnoreCase) && x.IsOverlay);
        var educationalOverlayUsage = beats.SelectMany(x => x.Overlays).Count(x => x.VisualFamily.Equals("EducationalOverlay", StringComparison.OrdinalIgnoreCase) && x.IsOverlay);
        var fallbackVisualCount = beats.Count(x => x.PrimaryVisual.RequestedButUnavailable || x.PrimaryVisual.Usage.Contains("fallback", StringComparison.OrdinalIgnoreCase));
        var mismatches = beats.Count(x => !x.MatchedToNarration);
        var matched = beats.Count - mismatches;
        var sameFamilyMax = MaxConsecutive(beats.OrderBy(x => x.EpisodeType).ThenBy(x => x.StartSecond).Select(x => x.PrimaryVisual.VisualFamily));
        var shortHook = beats.Where(x => x.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.StartSecond).FirstOrDefault();
        var shortHookPassed = shortHook is null || (shortHook.StartSecond <= 3 && shortHook.PrimaryVisual.VisualFamily is not ("MotionGraphic" or "EducationalOverlay") && !shortHook.PrimaryVisual.RequestedButUnavailable);
        var objectCoverage = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["MOON"] = ObjectMatchPassed(beats, "Moon"),
            ["VENUS"] = ObjectMatchPassed(beats, "Venus"),
            ["SATURN"] = ObjectMatchPassed(beats, "Saturn")
        };
        var saturn = objectCoverage["SATURN"];
        var venus = objectCoverage["VENUS"];
        var moon = objectCoverage["MOON"];
        var everyBeatHasSubject = beats.All(x => !string.IsNullOrWhiteSpace(x.NarrationSubject));
        var ready = errors.Count == 0 && beats.Count > 0 && mismatches == 0 && fullscreenMotion == 0 && fullscreenEdu == 0 && sameFamilyMax <= 2 && shortHookPassed && everyBeatHasSubject && saturn && venus && moon;
        return new WeeklyVisualIntentValidationReport(ready, beats.Count, matched, mismatches, mismatches, fallbackVisualCount, motionOverlayUsage, educationalOverlayUsage, fullscreenMotion, fullscreenMotion, fullscreenEdu, sameFamilyMax, shortHookPassed, saturn, venus, moon, familyRotationApplied, familyRotationSwapCount, BuildPrimaryFamilyCounts(beats), objectCoverage, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static WeeklyVisualFamilyDistributionReport BuildVisualFamilyDistributionReport(IReadOnlyList<WeeklyVisualIntentBeat> beats, WeeklyVisualIntentValidationReport validation, VisualFamilyRotationResult rotationResult, IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
    {
        var averageCandidates = beats.Count == 0 ? 0 : Math.Round(beats.Average(x => x.PrimaryVisualCandidatePool.Count), 2);
        return new WeeklyVisualFamilyDistributionReport(
            true,
            validation.PrimaryFamilyCounts,
            validation.SameFamilyConsecutiveMax,
            rotationResult.Applied,
            rotationResult.SwapCount,
            beats.Count(x => x.PrimaryVisualCandidatePool.Count > 0),
            averageCandidates,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IReadOnlyDictionary<string, int> BuildPrimaryFamilyCounts(IReadOnlyList<WeeklyVisualIntentBeat> beats)
    {
        var counts = EmptyFamilyCounts().ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var family in beats.Select(x => CanonicalReportFamily(x.PrimaryVisual.VisualFamily)))
            counts[family] = counts.GetValueOrDefault(family) + 1;
        return counts;
    }

    private static IReadOnlyDictionary<string, int> EmptyFamilyCounts()
        => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Stellarium"] = 0,
            ["NASA"] = 0,
            ["JWST"] = 0,
            ["InternalCelestial"] = 0,
            ["AICinematic"] = 0,
            ["MotionGraphic"] = 0,
            ["EducationalOverlay"] = 0
        };

    private static string CanonicalReportFamily(string family)
        => family.Equals("MotionGraphics", StringComparison.OrdinalIgnoreCase) ? "MotionGraphic"
            : family.Equals("CelestialReference", StringComparison.OrdinalIgnoreCase) ? "InternalCelestial"
            : family;

    private static bool ObjectMatchPassed(IReadOnlyList<WeeklyVisualIntentBeat> beats, string objectName)
    {
        var relevant = beats.Where(x => x.MentionedObjects.Contains(objectName, StringComparer.OrdinalIgnoreCase)
            || x.NarrationSubject.Equals(objectName, StringComparison.OrdinalIgnoreCase)
            || SelectionMatchesObject(x.PrimaryVisual, objectName)
            || x.SecondaryVisual is not null && SelectionMatchesObject(x.SecondaryVisual, objectName)
            || x.Overlays.Any(o => SelectionMatchesObject(o, objectName))).ToList();
        return relevant.Any(x => SelectionMatchesObject(x.PrimaryVisual, objectName) || x.SecondaryVisual is not null && SelectionMatchesObject(x.SecondaryVisual, objectName));
    }

    private static int MaxConsecutive(IEnumerable<string> families)
    {
        var max = 0;
        var current = 0;
        string? previous = null;
        foreach (var family in families)
        {
            current = previous is not null && previous.Equals(family, StringComparison.OrdinalIgnoreCase) ? current + 1 : 1;
            max = Math.Max(max, current);
            previous = family;
        }
        return max;
    }

    private static WeeklyVisualIntentAssetMix BuildMix(IEnumerable<WeeklyVisualIntentBeat> beats)
    {
        var list = beats.ToList();
        var primaryDuration = list.Sum(x => x.DurationSeconds);
        var overlayDuration = list.SelectMany(x => x.Overlays).Sum(x => x.DurationSeconds);
        var total = Math.Max(1, primaryDuration + overlayDuration);
        double P(string family)
        {
            var primary = list.Where(x => x.PrimaryVisual.VisualFamily.Equals(family, StringComparison.OrdinalIgnoreCase)).Sum(x => x.DurationSeconds);
            var overlays = list.SelectMany(x => x.Overlays).Where(x => x.VisualFamily.Equals(family, StringComparison.OrdinalIgnoreCase)).Sum(x => x.DurationSeconds);
            return Math.Round((primary + overlays) / total * 100, 2);
        }
        return new WeeklyVisualIntentAssetMix(P("Stellarium"), P("AICinematic"), P("CelestialReference"), P("MotionGraphic"), P("EducationalOverlay"));
    }

    private static WeeklyVisualIntentShotPlan BuildShotPlan(Guid pipelineRunId, FinalRenderTimeline timeline, IReadOnlyList<WeeklyVisualIntentBeat> beats)
    {
        WeeklyVisualIntentEpisodeShotPlan BuildEpisode(string episodeType, FinalRenderEpisodeTimeline episode)
        {
            var segments = episode.Segments.Select(segment =>
            {
                var beat = beats.First(x => x.EpisodeType.Equals(episodeType, StringComparison.OrdinalIgnoreCase) && x.SegmentId.Equals(segment.SegmentId, StringComparison.OrdinalIgnoreCase));
                var shots = new[] { beat.PrimaryVisual }.Concat(beat.SecondaryVisual is null ? [] : new[] { beat.SecondaryVisual })
                    .Select((selection, index) => ToShotEntry(index + 1, selection))
                    .ToList();
                return new WeeklyVisualIntentSegmentShotPlan(segment.SegmentId, segment.SegmentType, beat.VisualIntent, segment.StartSecond, segment.EndSecond, segment.DurationSeconds, beat.NarrationText, shots, beat.Overlays.Select((selection, index) => ToShotEntry(index + 1, selection)).ToList());
            }).ToList();
            return new WeeklyVisualIntentEpisodeShotPlan(episodeType, episode.ActualDurationSeconds, segments);
        }
        return new WeeklyVisualIntentShotPlan(pipelineRunId, DateTime.UtcNow, new[] { BuildEpisode("longform", timeline.Longform), BuildEpisode("shortform", timeline.Shortform) });
    }

    private static WeeklyVisualIntentShotPlanEntry ToShotEntry(int shotNumber, WeeklyVisualIntentAssetSelection selection)
        => new(shotNumber, selection.AssetId, selection.AssetType, selection.VisualFamily, selection.AssetPath, selection.StartSecond, selection.EndSecond, selection.DurationSeconds, selection.Usage, selection.IsOverlay, selection.MatchedObjects, selection.ProductionReady, selection.RequestedButUnavailable, selection.RequestSource);

    private static WeeklyVisualIntentStoryboard BuildStoryboard(Guid pipelineRunId, IReadOnlyList<WeeklyVisualIntentBeat> beats)
        => new(
            pipelineRunId,
            DateTime.UtcNow,
            "weekly-semantic-visual-storyboard-v1",
            beats.OrderBy(x => x.EpisodeType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.StartSecond)
                .Select(beat => new WeeklyVisualIntentStoryboardBeat(
                    beat.BeatId,
                    beat.NarrationSubject,
                    beat.VisualIntent,
                    beat.PrimaryVisual,
                    beat.SecondaryVisual,
                    beat.Overlays.FirstOrDefault(),
                    beat.DurationSeconds))
                .ToList());

    private static WeeklyVisualStorytellingReport BuildStorytellingReport(IReadOnlyList<WeeklyVisualIntentBeat> beats, WeeklyVisualIntentValidationReport validation)
        => new(
            validation.VisualIntentReady,
            validation.SameFamilyConsecutiveMax,
            validation.MotionGraphicOverlayUsageCount,
            validation.EducationalOverlayUsageCount,
            validation.FullscreenMotionGraphicCount,
            validation.FullscreenEducationalOverlayCount,
            validation.SaturnNarrationMatchedToSaturnVisual,
            validation.VenusNarrationMatchedToVenusVisual,
            validation.MoonNarrationMatchedToMoonVisual,
            validation.FallbackVisualCount,
            validation.Warnings,
            validation.Errors);

    private static FinalRenderTimeline RebuildTimelineFromStoryboard(FinalRenderTimeline timeline, WeeklyVisualIntentStoryboard storyboard)
    {
        var storyboardByBeat = storyboard.Beats.ToDictionary(x => x.NarrationBeatId, StringComparer.OrdinalIgnoreCase);
        FinalRenderEpisodeTimeline RebuildEpisode(string episodeType, FinalRenderEpisodeTimeline episode)
        {
            var segments = episode.Segments.Select(segment =>
            {
                var beatId = $"{episodeType}-{segment.SegmentId}";
                if (!storyboardByBeat.TryGetValue(beatId, out var beat)) return segment;
                var shots = BuildTimelineShotsFromStoryboardBeat(segment, beat);
                return segment with { Shots = shots };
            }).ToList();
            return episode with { Segments = segments };
        }

        return timeline with
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Longform = RebuildEpisode("longform", timeline.Longform),
            Shortform = RebuildEpisode("shortform", timeline.Shortform)
        };
    }

    private static IReadOnlyList<FinalRenderShot> BuildTimelineShotsFromStoryboardBeat(FinalRenderSegment segment, WeeklyVisualIntentStoryboardBeat beat)
    {
        var shots = new List<FinalRenderShot>();
        var hasSecondary = beat.SecondaryVisual is not null && beat.SecondaryVisual.ProductionReady && !beat.SecondaryVisual.RequestedButUnavailable;
        var primaryEnd = hasSecondary ? segment.StartSecond + segment.DurationSeconds * 0.62 : segment.EndSecond;
        shots.Add(ToFinalRenderShot(1, beat.PrimaryVisual, segment, "semantic_primary_visual", segment.StartSecond, primaryEnd));
        if (hasSecondary)
            shots.Add(ToFinalRenderShot(shots.Count + 1, beat.SecondaryVisual!, segment, "semantic_secondary_visual", primaryEnd, segment.EndSecond));
        if (beat.OverlayVisual is not null && beat.OverlayVisual.ProductionReady && !beat.OverlayVisual.RequestedButUnavailable)
            shots.Add(ToFinalRenderShot(shots.Count + 1, beat.OverlayVisual, segment, "semantic_overlay_visual", beat.OverlayVisual.StartSecond, beat.OverlayVisual.EndSecond));
        return shots;
    }

    private static FinalRenderShot ToFinalRenderShot(int shotNumber, WeeklyVisualIntentAssetSelection selection, FinalRenderSegment segment, string purposePrefix, double startSecond, double endSecond)
    {
        var durationSeconds = Math.Max(0, endSecond - startSecond);
        var overlayStart = selection.IsOverlay ? (int?)Math.Max(0, Math.Floor(startSecond - segment.StartSecond)) : null;
        var overlayEnd = selection.IsOverlay ? (int?)Math.Max(overlayStart ?? 0, Math.Ceiling(endSecond - segment.StartSecond)) : null;
        return new FinalRenderShot(
            shotNumber,
            selection.AssetId,
            selection.AssetType,
            selection.AssetPath,
            startSecond,
            endSecond,
            durationSeconds,
            shotNumber == 1 ? "fade" : "cut",
            "fade",
            selection.IsOverlay ? "overlay_pin_10_20_percent" : ResolveMotionEffect(selection.VisualFamily, shotNumber),
            $"{purposePrefix}: {selection.Usage}",
            selection.IsOverlay,
            overlayStart,
            overlayEnd);
    }

    private static string ResolveMotionEffect(string visualFamily, int shotNumber)
        => visualFamily switch
        {
            "Stellarium" => "slow_sky_pan",
            "NASA" or "JWST" or "InternalCelestial" or "CelestialReference" => "slow_push_in",
            "AICinematic" => "cinematic_drift",
            _ => shotNumber == 1 ? "gentle_push" : "supporting_cutaway"
        };

    private static async Task<WeeklyVisualIntentBuildResponse> PersistFailureAsync(Guid pipelineRunId, string root, string renderDirectory, IReadOnlyList<string> inputPaths, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(renderDirectory);
        var validation = new WeeklyVisualIntentValidationReport(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, true, true, true, true, false, 0, EmptyFamilyCounts(), new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase), warnings, errors);
        var planPath = Path.Combine(renderDirectory, "visual-intent-plan.json");
        var shotPlanPath = Path.Combine(renderDirectory, "visual-intent-shot-plan.json");
        var validationPath = Path.Combine(renderDirectory, "visual-intent-validation-report.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(new WeeklyVisualIntentPlan(pipelineRunId, DateTime.UtcNow, "weekly-visual-intent-v1", inputPaths, [], new WeeklyVisualIntentAssetMix(40, 15, 20, 12, 8), new WeeklyVisualIntentAssetMix(0, 0, 0, 0, 0), new WeeklyVisualIntentAssetMix(45, 30, 10, 8, 4), new WeeklyVisualIntentAssetMix(0, 0, 0, 0, 0), [], warnings), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(shotPlanPath, JsonSerializer.Serialize(new WeeklyVisualIntentShotPlan(pipelineRunId, DateTime.UtcNow, []), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        return ToResponse(pipelineRunId, root, planPath, shotPlanPath, validationPath, validation);
    }

    private static WeeklyVisualIntentBuildResponse ToResponse(Guid pipelineRunId, string root, string planPath, string shotPlanPath, string validationPath, WeeklyVisualIntentValidationReport validation)
        => new(pipelineRunId, validation.VisualIntentReady, validation.VisualIntentReady, root, planPath, shotPlanPath, validationPath, validation.TotalBeats, validation.MatchedBeatCount, validation.UnmatchedBeatCount, validation.NarrationVisualMismatchCount, validation.FallbackVisualCount, validation.MotionGraphicOverlayUsageCount, validation.EducationalOverlayUsageCount, validation.FullscreenMotionGraphicCount, validation.FullscreenMotionGraphicOveruseCount, validation.FullscreenEducationalOverlayCount, validation.SameFamilyConsecutiveMax, validation.ShortformHookStrongVisualPassed, validation.SaturnNarrationMatchedToSaturnVisual, validation.VenusNarrationMatchedToVenusVisual, validation.MoonNarrationMatchedToMoonVisual, validation.Warnings, validation.Errors);

    private static string ResolveNarrationText(FinalRenderSegment segment, IReadOnlyDictionary<string, string> narrationBySegment)
        => !string.IsNullOrWhiteSpace(segment.NarrationText) ? segment.NarrationText : narrationBySegment.GetValueOrDefault(segment.SegmentId, string.Empty);

    private static string NormalizeFamily(string assetType, string assetPath, string assetCode)
    {
        var value = $"{assetType} {assetPath} {assetCode}".ToLowerInvariant();
        if (value.Contains("motion")) return "MotionGraphic";
        if (value.Contains("educational") || value.Contains("overlay")) return "EducationalOverlay";
        if (value.Contains("ai") || value.Contains("cinematic")) return "AICinematic";
        if (value.Contains("jwst")) return "JWST";
        if (value.Contains("nasa")) return "NASA";
        if (value.Contains("internalcelestial") || value.Contains("internal-celestial") || value.Contains("celestial")) return "InternalCelestial";
        if (value.Contains("stellarium") || value.Contains("scene") || value.Contains("frame")) return "Stellarium";
        return "Stellarium";
    }

    private static void TrackFamily(Queue<string> previousFamilies, string family)
    {
        previousFamilies.Enqueue(family);
        while (previousFamilies.Count > 2) previousFamilies.Dequeue();
    }

    private sealed record VisualFamilyRotationResult(bool Applied, int SwapCount);

    private sealed record ScoredAssetCandidate(AssetCandidate Asset, int Score);

    private sealed record AssetCandidate(
        string AssetId,
        string AssetCode,
        string AssetType,
        string VisualFamily,
        string AssetPath,
        string SegmentId,
        string EpisodeType,
        string SegmentType,
        bool ProductionReady,
        IReadOnlyList<string> MatchedObjects);
}
