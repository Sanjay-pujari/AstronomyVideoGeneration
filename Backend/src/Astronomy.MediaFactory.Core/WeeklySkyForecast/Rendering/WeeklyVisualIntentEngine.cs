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
    Observation,
    DirectionGuidance,
    BestTime,
    ScientificContext,
    EducationalExplanation,
    AstrophotographyTip,
    Summary,
    CallToAction
}

public sealed record WeeklyVisualIntentBuildResponse(
    Guid PipelineRunId,
    bool VisualIntentReady,
    string ResolvedPipelineRunRoot,
    string VisualIntentPlanPath,
    string VisualIntentShotPlanPath,
    string VisualIntentValidationReportPath,
    int TotalBeats,
    int MatchedBeatCount,
    int UnmatchedBeatCount,
    int NarrationVisualMismatchCount,
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
    IReadOnlyList<string> MentionedObjects,
    IReadOnlyList<string> EditorialRulesApplied,
    WeeklyVisualIntentAssetSelection PrimaryVisual,
    WeeklyVisualIntentAssetSelection? SecondaryVisual,
    IReadOnlyList<WeeklyVisualIntentAssetSelection> Overlays,
    bool MatchedToNarration,
    IReadOnlyList<string> Warnings);

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
    int FullscreenMotionGraphicOveruseCount,
    int FullscreenEducationalOverlayCount,
    int SameFamilyConsecutiveMax,
    bool ShortformHookStrongVisualPassed,
    bool SaturnNarrationMatchedToSaturnVisual,
    bool VenusNarrationMatchedToVenusVisual,
    bool MoonNarrationMatchedToMoonVisual,
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
        var narrationTimelineMap = await ReadJsonAsync<IReadOnlyList<NarrationTimelineMapEntry>>(Path.Combine(episodeDirectory, "narration-timeline-map.json"), cancellationToken) ?? [];
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
        foreach (var segment in timeline!.Longform.Segments.Concat(timeline.Shortform.Segments))
        {
            var narrationText = ResolveNarrationText(segment, narrationBySegment);
            var intent = ClassifyIntent(segment.SegmentType, narrationText, segment.EpisodeType, segment.StartSecond);
            var mentionedObjects = DetectMentionedObjects(narrationText, segment.SegmentType);
            var candidate = SelectPrimaryVisual(segment, intent, mentionedObjects, catalog, previousFamilies, segment.EpisodeType, warnings);
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
                mentionedObjects,
                rules,
                candidate,
                secondary,
                overlays,
                MatchesNarration(candidate, mentionedObjects, intent),
                beatWarnings);
            beats.Add(beat);
            TrackFamily(previousFamilies, candidate.VisualFamily);
        }

        EnsureShortformStrongVisual(beats, catalog, warnings);
        var validation = BuildValidation(beats, warnings, errors);
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

        var planPath = Path.Combine(renderDirectory, "visual-intent-plan.json");
        var visualShotPlanPath = Path.Combine(renderDirectory, "visual-intent-shot-plan.json");
        var validationPath = Path.Combine(renderDirectory, "visual-intent-validation-report.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(visualShotPlanPath, JsonSerializer.Serialize(visualShotPlan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);

        logger.LogInformation("WEEKLY_VISUAL_INTENT_COMPLETE pipelineRunId={PipelineRunId} ready={Ready} totalBeats={TotalBeats} mismatches={MismatchCount}", pipelineRunId, validation.VisualIntentReady, validation.TotalBeats, validation.NarrationVisualMismatchCount);
        return ToResponse(pipelineRunId, root, planPath, visualShotPlanPath, validationPath, validation);
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
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
                DetectObjects($"{asset.AssetId} {asset.AssetCode} {asset.FilePath} {bundle.SegmentId} {bundle.SegmentType}"))))
            .ToList();

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
                DetectObjects($"{shot.AssetId} {shot.AssetType} {shot.AssetPath} {segment.SegmentId} {segment.SegmentType}")))))
            .Concat(timeline.Longform.Segments.Concat(timeline.Shortform.Segments).SelectMany(segment => segment.Shots.Select(shot => new AssetCandidate(
                shot.AssetId,
                shot.AssetId,
                shot.AssetType,
                NormalizeFamily(shot.AssetType, shot.AssetPath, shot.AssetId),
                shot.AssetPath,
                segment.SegmentId,
                segment.EpisodeType,
                segment.SegmentType,
                string.IsNullOrWhiteSpace(shot.AssetPath) || File.Exists(shot.AssetPath) || !Path.IsPathRooted(shot.AssetPath),
                DetectObjects($"{shot.AssetId} {shot.AssetType} {shot.AssetPath} {segment.SegmentId} {segment.SegmentType}")))))
            .ToList();

        return manifestAssets.Concat(renderedAssets)
            .Where(x => !string.IsNullOrWhiteSpace(x.AssetId) || !string.IsNullOrWhiteSpace(x.AssetPath))
            .GroupBy(x => $"{x.AssetId}|{x.AssetPath}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private static WeeklyVisualIntentType ClassifyIntent(string segmentType, string narration, string episodeType, double startSecond)
    {
        var text = $"{segmentType} {narration}".ToLowerInvariant();
        if (segmentType.Contains("CallToAction", StringComparison.OrdinalIgnoreCase) || text.Contains("subscribe") || text.Contains("follow")) return WeeklyVisualIntentType.CallToAction;
        if (segmentType.Contains("Summary", StringComparison.OrdinalIgnoreCase) || text.Contains("recap") || text.Contains("in summary")) return WeeklyVisualIntentType.Summary;
        if (segmentType.Contains("Hook", StringComparison.OrdinalIgnoreCase) || startSecond <= 1) return WeeklyVisualIntentType.Hook;
        if (text.Contains("camera") || text.Contains("photo") || text.Contains("astrophotography") || text.Contains("exposure") || text.Contains("tripod")) return WeeklyVisualIntentType.AstrophotographyTip;
        if (text.Contains("why") || text.Contains("rings") || text.Contains("phase") || text.Contains("detail") || text.Contains("science")) return WeeklyVisualIntentType.ScientificContext;
        if (text.Contains("look") || text.Contains("direction") || text.Contains("east") || text.Contains("west") || text.Contains("north") || text.Contains("south") || text.Contains("horizon") || text.Contains("ऊपर") || text.Contains("क्षितिज")) return WeeklyVisualIntentType.DirectionGuidance;
        if (text.Contains("best time") || text.Contains("time") || text.Contains("after sunset") || text.Contains("before sunrise") || text.Contains("minutes") || text.Contains(" बजे")) return WeeklyVisualIntentType.BestTime;
        if (text.Contains("learn") || text.Contains("explain") || text.Contains("checklist") || text.Contains("समझ")) return WeeklyVisualIntentType.EducationalExplanation;
        return WeeklyVisualIntentType.Observation;
    }

    private static IReadOnlyList<string> DetectMentionedObjects(string narration, string segmentType)
        => DetectObjects($"{narration} {segmentType}");

    private static IReadOnlyList<string> DetectObjects(string text)
    {
        var value = text.ToLowerInvariant();
        var objects = new List<string>();
        if (value.Contains("saturn") || value.Contains("शनि")) objects.Add("Saturn");
        if (value.Contains("venus") || value.Contains("शुक्र")) objects.Add("Venus");
        if (value.Contains("moon") || value.Contains("lunar") || value.Contains("चंद्र") || value.Contains("चाँद")) objects.Add("Moon");
        if (value.Contains("jupiter") || value.Contains("बृहस्पति")) objects.Add("Jupiter");
        if (value.Contains("mars") || value.Contains("मंगल")) objects.Add("Mars");
        if (value.Contains("mercury") || value.Contains("बुध")) objects.Add("Mercury");
        return objects.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static WeeklyVisualIntentAssetSelection? SelectPrimaryVisual(FinalRenderSegment segment, WeeklyVisualIntentType intent, IReadOnlyList<string> mentionedObjects, IReadOnlyList<AssetCandidate> catalog, Queue<string> previousFamilies, string episodeType, List<string> warnings)
    {
        var preferredFamilies = PreferredPrimaryFamilies(intent, segment.NarrationText);
        if (mentionedObjects.Count > 0)
        {
            if (segment.NarrationText.Contains("rings", StringComparison.OrdinalIgnoreCase) || segment.NarrationText.Contains("detail", StringComparison.OrdinalIgnoreCase))
                preferredFamilies = new[] { "CelestialReference" }.Concat(preferredFamilies.Where(x => !x.Equals("CelestialReference", StringComparison.OrdinalIgnoreCase))).ToArray();
        }

        var sameSegment = catalog.Where(x => x.SegmentId.Equals(segment.SegmentId, StringComparison.OrdinalIgnoreCase)).ToList();
        var candidates = sameSegment.Concat(catalog).Where(x => x.ProductionReady).DistinctBy(x => $"{x.AssetId}|{x.AssetPath}").ToList();
        var objectCandidates = mentionedObjects.Count == 0 ? candidates : candidates.Where(x => mentionedObjects.Any(o => x.MatchedObjects.Contains(o, StringComparer.OrdinalIgnoreCase))).ToList();
        var search = objectCandidates.Count > 0 ? objectCandidates : candidates;

        foreach (var family in preferredFamilies)
        {
            var selected = Pick(search, family, previousFamilies);
            if (selected is null && objectCandidates.Count == 0)
                selected = Pick(candidates, family, previousFamilies);
            if (selected is not null) return ToSelection(selected, "primary", false, segment.StartSecond, segment.EndSecond);
        }

        var fallback = Pick(search.Where(x => x.VisualFamily is not "MotionGraphics" and not "EducationalOverlay"), null, previousFamilies)
            ?? Pick(candidates.Where(x => x.VisualFamily is not "MotionGraphics" and not "EducationalOverlay"), null, previousFamilies);
        if (fallback is not null)
        {
            warnings.Add($"Primary visual fallback used for segment {segment.SegmentId}; no preferred-family asset matched {intent}.");
            return ToSelection(fallback, "primary_editorial_fallback", false, segment.StartSecond, segment.EndSecond);
        }

        return null;
    }

    private static WeeklyVisualIntentAssetSelection? SelectSecondaryVisual(FinalRenderSegment segment, WeeklyVisualIntentType intent, IReadOnlyList<string> mentionedObjects, IReadOnlyList<AssetCandidate> catalog, string primaryFamily)
    {
        var secondaryFamily = intent switch
        {
            WeeklyVisualIntentType.Hook => "Stellarium",
            WeeklyVisualIntentType.Observation => "CelestialReference",
            WeeklyVisualIntentType.ScientificContext => "Stellarium",
            WeeklyVisualIntentType.Summary => "MotionGraphics",
            _ => null
        };
        if (secondaryFamily is null) return null;
        var selected = catalog
            .Where(x => x.VisualFamily.Equals(secondaryFamily, StringComparison.OrdinalIgnoreCase) && !x.VisualFamily.Equals(primaryFamily, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.SegmentId.Equals(segment.SegmentId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => mentionedObjects.Any(o => x.MatchedObjects.Contains(o, StringComparer.OrdinalIgnoreCase)))
            .FirstOrDefault();
        return selected is null ? null : ToSelection(selected, "secondary_support", false, segment.StartSecond, segment.EndSecond);
    }

    private static IReadOnlyList<WeeklyVisualIntentAssetSelection> SelectOverlays(FinalRenderSegment segment, WeeklyVisualIntentType intent, IReadOnlyList<AssetCandidate> catalog)
    {
        var overlayFamily = intent switch
        {
            WeeklyVisualIntentType.DirectionGuidance or WeeklyVisualIntentType.BestTime or WeeklyVisualIntentType.Summary => "MotionGraphics",
            WeeklyVisualIntentType.EducationalExplanation => "EducationalOverlay",
            WeeklyVisualIntentType.AstrophotographyTip => "EducationalOverlay",
            WeeklyVisualIntentType.CallToAction => "MotionGraphics",
            _ => null
        };
        if (overlayFamily is null) return [];
        var selected = catalog
            .Where(x => x.VisualFamily.Equals(overlayFamily, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.SegmentId.Equals(segment.SegmentId, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (selected is null) return [];
        var duration = Math.Min(segment.DurationSeconds, overlayFamily.Equals("MotionGraphics", StringComparison.OrdinalIgnoreCase) ? 3 : Math.Max(3, Math.Min(6, segment.DurationSeconds)));
        return [ToSelection(selected, overlayFamily.Equals("MotionGraphics", StringComparison.OrdinalIgnoreCase) ? "lower_third_overlay" : "educational_overlay", true, segment.StartSecond, segment.StartSecond + duration)];
    }

    private static string[] PreferredPrimaryFamilies(WeeklyVisualIntentType intent, string narration)
        => intent switch
        {
            WeeklyVisualIntentType.Hook => ["AICinematic", "Stellarium"],
            WeeklyVisualIntentType.Observation => ["Stellarium", "CelestialReference"],
            WeeklyVisualIntentType.DirectionGuidance => ["Stellarium"],
            WeeklyVisualIntentType.BestTime => ["Stellarium", "AICinematic"],
            WeeklyVisualIntentType.ScientificContext => ["CelestialReference", "Stellarium"],
            WeeklyVisualIntentType.EducationalExplanation => ["Stellarium", "CelestialReference"],
            WeeklyVisualIntentType.AstrophotographyTip => ["Stellarium", "CelestialReference"],
            WeeklyVisualIntentType.Summary => ["AICinematic", "Stellarium"],
            WeeklyVisualIntentType.CallToAction => ["AICinematic", "Stellarium"],
            _ => ["Stellarium"]
        };

    private static AssetCandidate? Pick(IEnumerable<AssetCandidate> candidates, string? family, Queue<string> previousFamilies)
    {
        var list = candidates.Where(x => family is null || x.VisualFamily.Equals(family, StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0) return null;
        var wouldBeThird = previousFamilies.Count >= 2 && previousFamilies.All(x => x.Equals(list[0].VisualFamily, StringComparison.OrdinalIgnoreCase));
        if (wouldBeThird)
        {
            var alternate = list.FirstOrDefault(x => !x.VisualFamily.Equals(previousFamilies.Peek(), StringComparison.OrdinalIgnoreCase));
            if (alternate is not null) return alternate;
            return null;
        }
        return list.First();
    }

    private static WeeklyVisualIntentAssetSelection ToSelection(AssetCandidate asset, string usage, bool isOverlay, double startSecond, double endSecond)
        => new(asset.AssetId, asset.AssetType, asset.VisualFamily, asset.AssetPath, usage, isOverlay, startSecond, endSecond, Math.Max(0, endSecond - startSecond), asset.MatchedObjects, asset.ProductionReady);

    private static WeeklyVisualIntentAssetSelection BuildUnavailableSelection(FinalRenderSegment segment, WeeklyVisualIntentType intent, IReadOnlyList<string> mentionedObjects, List<WeeklyInternalCelestialAssetRequest> internalRequests, string reason)
    {
        var objectCode = mentionedObjects.FirstOrDefault() ?? "DeepSkyObject";
        if (intent is WeeklyVisualIntentType.ScientificContext or WeeklyVisualIntentType.EducationalExplanation or WeeklyVisualIntentType.AstrophotographyTip || mentionedObjects.Count > 0)
            internalRequests.Add(new WeeklyInternalCelestialAssetRequest(objectCode, segment.SegmentId, segment.EpisodeType, reason, "requestedButUnavailable"));
        return new WeeklyVisualIntentAssetSelection($"internal-celestial-{objectCode.ToLowerInvariant()}-requested", "InternalCelestial", "CelestialReference", string.Empty, "primary_internal_celestial_fallback_request", false, segment.StartSecond, segment.EndSecond, segment.DurationSeconds, mentionedObjects, false, true, "InternalCelestial");
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
        if (mentionedObjects.Count > 0 && !mentionedObjects.Any(o => selection.MatchedObjects.Contains(o, StringComparer.OrdinalIgnoreCase))) return false;
        if ((selection.VisualFamily is "MotionGraphics" or "EducationalOverlay") && !selection.IsOverlay) return false;
        return true;
    }

    private static void EnsureShortformStrongVisual(List<WeeklyVisualIntentBeat> beats, IReadOnlyList<AssetCandidate> catalog, List<string> warnings)
    {
        var firstShort = beats.Where(x => x.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.StartSecond).FirstOrDefault();
        if (firstShort is null || firstShort.StartSecond > 3) return;
        if (firstShort.PrimaryVisual.VisualFamily is not ("MotionGraphics" or "EducationalOverlay") && !firstShort.PrimaryVisual.RequestedButUnavailable) return;
        var strong = catalog.FirstOrDefault(x => x.VisualFamily is "AICinematic" or "Stellarium" or "CelestialReference" && x.ProductionReady);
        if (strong is null) return;
        var replacement = ToSelection(strong, "shortform_strong_hook_primary", false, firstShort.StartSecond, firstShort.EndSecond);
        var index = beats.IndexOf(firstShort);
        beats[index] = firstShort with { PrimaryVisual = replacement, MatchedToNarration = true, Warnings = firstShort.Warnings.Append("Shortform hook primary visual upgraded to strongest available non-card visual.").ToList() };
        warnings.Add("Shortform hook visual was upgraded to avoid weak full-screen card usage in the first 3 seconds.");
    }

    private static WeeklyVisualIntentValidationReport BuildValidation(IReadOnlyList<WeeklyVisualIntentBeat> beats, IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
    {
        var fullscreenMotion = beats.Count(x => x.PrimaryVisual.VisualFamily.Equals("MotionGraphics", StringComparison.OrdinalIgnoreCase) && !x.PrimaryVisual.IsOverlay && x.PrimaryVisual.DurationSeconds > 3);
        var fullscreenEdu = beats.Count(x => x.PrimaryVisual.VisualFamily.Equals("EducationalOverlay", StringComparison.OrdinalIgnoreCase) && !x.PrimaryVisual.IsOverlay);
        var mismatches = beats.Count(x => !x.MatchedToNarration);
        var matched = beats.Count - mismatches;
        var sameFamilyMax = MaxConsecutive(beats.OrderBy(x => x.EpisodeType).ThenBy(x => x.StartSecond).Select(x => x.PrimaryVisual.VisualFamily));
        var shortHook = beats.Where(x => x.EpisodeType.Equals("shortform", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.StartSecond).FirstOrDefault();
        var shortHookPassed = shortHook is null || (shortHook.StartSecond <= 3 && shortHook.PrimaryVisual.VisualFamily is not ("MotionGraphics" or "EducationalOverlay") && !shortHook.PrimaryVisual.RequestedButUnavailable);
        var saturn = ObjectMatchPassed(beats, "Saturn");
        var venus = ObjectMatchPassed(beats, "Venus");
        var moon = ObjectMatchPassed(beats, "Moon");
        var ready = errors.Count == 0 && beats.Count > 0 && fullscreenMotion == 0 && fullscreenEdu == 0 && shortHookPassed && saturn && venus && moon;
        return new WeeklyVisualIntentValidationReport(ready, beats.Count, matched, mismatches, mismatches, fullscreenMotion, fullscreenEdu, sameFamilyMax, shortHookPassed, saturn, venus, moon, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static bool ObjectMatchPassed(IReadOnlyList<WeeklyVisualIntentBeat> beats, string objectName)
    {
        var relevant = beats.Where(x => x.MentionedObjects.Contains(objectName, StringComparer.OrdinalIgnoreCase)).ToList();
        return relevant.Count == 0 || relevant.All(x => x.PrimaryVisual.MatchedObjects.Contains(objectName, StringComparer.OrdinalIgnoreCase) || x.SecondaryVisual?.MatchedObjects.Contains(objectName, StringComparer.OrdinalIgnoreCase) == true);
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
        return new WeeklyVisualIntentAssetMix(P("Stellarium"), P("AICinematic"), P("CelestialReference"), P("MotionGraphics"), P("EducationalOverlay"));
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

    private static async Task<WeeklyVisualIntentBuildResponse> PersistFailureAsync(Guid pipelineRunId, string root, string renderDirectory, IReadOnlyList<string> inputPaths, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(renderDirectory);
        var validation = new WeeklyVisualIntentValidationReport(false, 0, 0, 0, 0, 0, 0, 0, true, true, true, true, warnings, errors);
        var planPath = Path.Combine(renderDirectory, "visual-intent-plan.json");
        var shotPlanPath = Path.Combine(renderDirectory, "visual-intent-shot-plan.json");
        var validationPath = Path.Combine(renderDirectory, "visual-intent-validation-report.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(new WeeklyVisualIntentPlan(pipelineRunId, DateTime.UtcNow, "weekly-visual-intent-v1", inputPaths, [], new WeeklyVisualIntentAssetMix(40, 15, 20, 12, 8), new WeeklyVisualIntentAssetMix(0, 0, 0, 0, 0), new WeeklyVisualIntentAssetMix(45, 30, 10, 8, 4), new WeeklyVisualIntentAssetMix(0, 0, 0, 0, 0), [], warnings), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(shotPlanPath, JsonSerializer.Serialize(new WeeklyVisualIntentShotPlan(pipelineRunId, DateTime.UtcNow, []), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(validation, JsonOptions), cancellationToken);
        return ToResponse(pipelineRunId, root, planPath, shotPlanPath, validationPath, validation);
    }

    private static WeeklyVisualIntentBuildResponse ToResponse(Guid pipelineRunId, string root, string planPath, string shotPlanPath, string validationPath, WeeklyVisualIntentValidationReport validation)
        => new(pipelineRunId, validation.VisualIntentReady, root, planPath, shotPlanPath, validationPath, validation.TotalBeats, validation.MatchedBeatCount, validation.UnmatchedBeatCount, validation.NarrationVisualMismatchCount, validation.FullscreenMotionGraphicOveruseCount, validation.FullscreenEducationalOverlayCount, validation.SameFamilyConsecutiveMax, validation.ShortformHookStrongVisualPassed, validation.SaturnNarrationMatchedToSaturnVisual, validation.VenusNarrationMatchedToVenusVisual, validation.MoonNarrationMatchedToMoonVisual, validation.Warnings, validation.Errors);

    private static string ResolveNarrationText(FinalRenderSegment segment, IReadOnlyDictionary<string, string> narrationBySegment)
        => !string.IsNullOrWhiteSpace(segment.NarrationText) ? segment.NarrationText : narrationBySegment.GetValueOrDefault(segment.SegmentId, string.Empty);

    private static string NormalizeFamily(string assetType, string assetPath, string assetCode)
    {
        var value = $"{assetType} {assetPath} {assetCode}".ToLowerInvariant();
        if (value.Contains("motion")) return "MotionGraphics";
        if (value.Contains("educational") || value.Contains("overlay")) return "EducationalOverlay";
        if (value.Contains("ai") || value.Contains("cinematic")) return "AICinematic";
        if (value.Contains("nasa") || value.Contains("jwst") || value.Contains("internalcelestial") || value.Contains("celestial")) return "CelestialReference";
        if (value.Contains("stellarium") || value.Contains("scene") || value.Contains("frame")) return "Stellarium";
        return "Stellarium";
    }

    private static void TrackFamily(Queue<string> previousFamilies, string family)
    {
        previousFamilies.Enqueue(family);
        while (previousFamilies.Count > 2) previousFamilies.Dequeue();
    }

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
