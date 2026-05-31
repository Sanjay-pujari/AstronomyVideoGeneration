using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.EventScoring;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.SegmentClassification;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.NarrationEngine;

public interface IWeeklyNarrationEngineV2
{
    Task<WeeklyNarrationEngineV2Result> GenerateAndPersistAsync(WeeklyNarrationEngineV2Input input, CancellationToken cancellationToken);
}

public sealed record WeeklyNarrationEngineV2Input(
    Guid PipelineRunId,
    string WorkingDirectoryRoot,
    string Language,
    string RegionName,
    DateOnly WeekStartDate,
    string WeeklyEpisodePlanPath,
    string WeeklyLongformPlanPath,
    string WeeklyShortformPlanPath,
    string WeeklySegmentClassificationPlanPath,
    string WeeklyEventPriorityReportPath,
    string HeroEventSelectionPath,
    string WeeklyProductionAssetManifestPath,
    string WeeklyNarrationVisualTimelinePath,
    string WeeklyStoryBeatsPath,
    WeeklyEpisodePlan LongformPlan,
    WeeklyEpisodePlan ShortformPlan,
    WeeklySegmentClassificationPlan SegmentClassificationPlan,
    WeeklyEventPriorityReport EventPriorityReport,
    WeeklyHeroEventSelection HeroEventSelection,
    WeeklyProductionAssetManifest ProductionAssetManifest,
    WeeklyNarrationVisualTimeline NarrationVisualTimeline);

public sealed record WeeklyNarrationSegment(string SegmentId, string SegmentType, string NarrationText, int EstimatedDurationSeconds, double NarrationWeight, int PriorityScore, bool HeroEventRelated);
public sealed record WeeklyNarrationPackage(Guid PipelineRunId, DateTime GeneratedAtUtc, string Language, string Style, int TargetDurationSeconds, int TotalEstimatedDurationSeconds, IReadOnlyList<WeeklyNarrationSegment> Segments);
public sealed record NarrationAssetMapEntry(string SegmentId, string SegmentType, string EpisodeType, string NarrationText, IReadOnlyList<string> AssetIds, IReadOnlyList<string> AssetTypes);
public sealed record NarrationTimelineAssetSequenceEntry(string AssetId, string AssetType, string AssetPath, int StartSecond, int EndSecond, string Purpose);
public sealed record NarrationTimelineMapEntry(string SegmentId, string SegmentType, string EpisodeType, int NarrationStart, int NarrationEnd, IReadOnlyList<NarrationTimelineAssetSequenceEntry> AssetSequence);
public sealed record NarrationEmotionalResetMarker(int ResetSecond, string AssetId, string AssetType, string Reason);
public sealed record WeeklyNarrationReport(Guid PipelineRunId, DateTime GeneratedAtUtc, IReadOnlyList<string> InputArtifacts, bool LongformNarrationReady, bool ShortformNarrationReady, bool NarrationAssetMappingReady, bool NarrationTimelineReady, int TotalLongformNarrationSeconds, int TotalShortformNarrationSeconds, int LongformSegmentCount, int ShortformSegmentCount, int AssetMappedSegmentCount, int TimelineMappedSegmentCount, IReadOnlyList<NarrationEmotionalResetMarker> EmotionalResetMarkers, bool StellariumVarietyRuleSatisfied, IReadOnlyList<string> Warnings);
public sealed record WeeklyNarrationEngineV2Result(WeeklyNarrationPackage LongformNarration, WeeklyNarrationPackage ShortformNarration, IReadOnlyList<NarrationAssetMapEntry> NarrationAssetMap, IReadOnlyList<NarrationTimelineMapEntry> NarrationTimelineMap, WeeklyNarrationReport Report, string LongformNarrationPath, string ShortformNarrationPath, string NarrationAssetMapPath, string NarrationTimelineMapPath, string WeeklyNarrationReportPath, bool LongformNarrationReady, bool ShortformNarrationReady, bool NarrationAssetMappingReady, bool NarrationTimelineReady, int TotalLongformNarrationSeconds, int TotalShortformNarrationSeconds);

public sealed class WeeklyNarrationEngineV2(ILogger<WeeklyNarrationEngineV2> logger) : IWeeklyNarrationEngineV2
{
    private const int LongformTargetSeconds = 380;
    private const int ShortformTargetSeconds = 50;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public async Task<WeeklyNarrationEngineV2Result> GenerateAndPersistAsync(WeeklyNarrationEngineV2Input input, CancellationToken cancellationToken)
    {
        logger.LogInformation("NARRATION_ENGINE_V2_START pipelineRunId={PipelineRunId} root={Root}", input.PipelineRunId, input.WorkingDirectoryRoot);
        var episodeDirectory = Path.Combine(input.WorkingDirectoryRoot, "episode");
        Directory.CreateDirectory(episodeDirectory);
        var eventIndex = input.EventPriorityReport.Events.ToDictionary(x => x.EventCode, StringComparer.OrdinalIgnoreCase);
        var longAssignments = input.SegmentClassificationPlan.LongformAssignments.ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        var shortAssignments = input.SegmentClassificationPlan.ShortformAssignments.ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        var longDurations = AllocateDurations(input.LongformPlan.Segments, longAssignments, eventIndex, LongformTargetSeconds);
        var shortDurations = AllocateDurations(input.ShortformPlan.Segments, shortAssignments, eventIndex, ShortformTargetSeconds);
        var longSegments = input.LongformPlan.Segments.Select(s => BuildNarrationSegment(s, GetOrDefault(longAssignments, s.SegmentId), eventIndex, input.HeroEventSelection, longDurations[s.SegmentId], input.RegionName, input.WeekStartDate)).ToList();
        var shortSegments = input.ShortformPlan.Segments.Select(s => BuildNarrationSegment(s, GetOrDefault(shortAssignments, s.SegmentId), eventIndex, input.HeroEventSelection, shortDurations[s.SegmentId], input.RegionName, input.WeekStartDate)).ToList();
        var longform = new WeeklyNarrationPackage(input.PipelineRunId, DateTime.UtcNow, input.Language, "Scientific engaging Hindi documentary; avoids astrology, speculation, and clickbait", LongformTargetSeconds, longSegments.Sum(x => x.EstimatedDurationSeconds), longSegments);
        var shortform = new WeeklyNarrationPackage(input.PipelineRunId, DateTime.UtcNow, input.Language, "Scientific engaging Hindi documentary short-form", ShortformTargetSeconds, shortSegments.Sum(x => x.EstimatedDurationSeconds), shortSegments);
        var allSegments = longSegments.Select(x => (Segment: x, EpisodeType: input.LongformPlan.EpisodeType.ToString())).Concat(shortSegments.Select(x => (Segment: x, EpisodeType: input.ShortformPlan.EpisodeType.ToString()))).ToList();
        var bundleIndex = input.ProductionAssetManifest.SegmentBundles.ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        var assetMap = allSegments.Select(x => BuildAssetMapEntry(x.Segment, x.EpisodeType, bundleIndex)).ToList();
        var timelineMap = BuildTimelineMap(input, allSegments, bundleIndex, out var resetMarkers, out var varietyOk, out var timelineWarnings);
        var reportWarnings = timelineWarnings.Concat(assetMap.Where(x => x.AssetIds.Count == 0).Select(x => $"No assets mapped for narration segment {x.SegmentId}.")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var report = new WeeklyNarrationReport(input.PipelineRunId, DateTime.UtcNow, [input.WeeklyEpisodePlanPath, input.WeeklyLongformPlanPath, input.WeeklyShortformPlanPath, input.WeeklySegmentClassificationPlanPath, input.WeeklyEventPriorityReportPath, input.HeroEventSelectionPath, input.WeeklyProductionAssetManifestPath, input.WeeklyNarrationVisualTimelinePath, input.WeeklyStoryBeatsPath], longSegments.Count == input.LongformPlan.Segments.Count && longSegments.All(x => !string.IsNullOrWhiteSpace(x.NarrationText)), shortSegments.Count == input.ShortformPlan.Segments.Count && shortSegments.All(x => !string.IsNullOrWhiteSpace(x.NarrationText)), assetMap.Count == allSegments.Count && assetMap.All(x => x.AssetIds.Count > 0), timelineMap.Count == allSegments.Count && timelineMap.All(x => x.NarrationEnd > x.NarrationStart && x.AssetSequence.Count > 0), longform.TotalEstimatedDurationSeconds, shortform.TotalEstimatedDurationSeconds, longSegments.Count, shortSegments.Count, assetMap.Count(x => x.AssetIds.Count > 0), timelineMap.Count(x => x.AssetSequence.Count > 0), resetMarkers, varietyOk, reportWarnings);
        var longformPath = Path.Combine(episodeDirectory, "longform-narration.json");
        var shortformPath = Path.Combine(episodeDirectory, "shortform-narration.json");
        var assetMapPath = Path.Combine(episodeDirectory, "narration-asset-map.json");
        var timelineMapPath = Path.Combine(episodeDirectory, "narration-timeline-map.json");
        var reportPath = Path.Combine(episodeDirectory, "weekly-narration-report.json");
        await File.WriteAllTextAsync(longformPath, JsonSerializer.Serialize(longform, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(shortformPath, JsonSerializer.Serialize(shortform, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(assetMapPath, JsonSerializer.Serialize(assetMap, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(timelineMapPath, JsonSerializer.Serialize(new { input.PipelineRunId, generatedAtUtc = DateTime.UtcNow, emotionalResetMarkers = resetMarkers, segments = timelineMap }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        logger.LogInformation("NARRATION_ENGINE_V2_COMPLETE pipelineRunId={PipelineRunId} longformSeconds={LongformSeconds} shortformSeconds={ShortformSeconds}", input.PipelineRunId, report.TotalLongformNarrationSeconds, report.TotalShortformNarrationSeconds);
        return new WeeklyNarrationEngineV2Result(longform, shortform, assetMap, timelineMap, report, longformPath, shortformPath, assetMapPath, timelineMapPath, reportPath, report.LongformNarrationReady, report.ShortformNarrationReady, report.NarrationAssetMappingReady, report.NarrationTimelineReady, report.TotalLongformNarrationSeconds, report.TotalShortformNarrationSeconds);
    }

    private static Dictionary<string, int> AllocateDurations(IReadOnlyList<WeeklyEpisodeSegment> segments, IReadOnlyDictionary<string, WeeklySegmentAssignment> assignments, IReadOnlyDictionary<string, WeeklyEventScore> events, int targetSeconds)
    {
        var raw = segments.Select(segment => { var assignment = GetOrDefault(assignments, segment.SegmentId); var weight = ResolveWeight(assignment, events, segment.SegmentType); var role = segment.SegmentType is "OpeningHook" or "WeeklySummary" or "ShortHook" or "CallToAction" ? 0.75d : 1d; return new { segment.SegmentId, Value = Math.Max(1d, segment.TargetDurationSeconds * weight * role), segment.MinDurationSeconds, segment.MaxDurationSeconds }; }).ToList();
        var total = raw.Sum(x => x.Value);
        var durations = raw.ToDictionary(x => x.SegmentId, x => Math.Clamp((int)Math.Round(x.Value / total * targetSeconds), x.MinDurationSeconds, x.MaxDurationSeconds), StringComparer.OrdinalIgnoreCase);
        var diff = targetSeconds - durations.Sum(x => x.Value);
        while (diff != 0)
        {
            var changed = false;
            foreach (var candidate in raw.OrderByDescending(x => x.Value))
            {
                var current = durations[candidate.SegmentId];
                if (diff > 0 && current < candidate.MaxDurationSeconds) { durations[candidate.SegmentId] = current + 1; diff--; changed = true; }
                else if (diff < 0 && current > candidate.MinDurationSeconds) { durations[candidate.SegmentId] = current - 1; diff++; changed = true; }
                if (diff == 0) break;
            }
            if (!changed) break;
        }
        return durations;
    }

    private static WeeklyNarrationSegment BuildNarrationSegment(WeeklyEpisodeSegment segment, WeeklySegmentAssignment? assignment, IReadOnlyDictionary<string, WeeklyEventScore> events, WeeklyHeroEventSelection hero, int duration, string regionName, DateOnly weekStartDate)
    {
        var score = ResolveEventScore(assignment, events);
        var weight = ResolveWeight(assignment, events, segment.SegmentType);
        var priority = score?.FinalScore ?? assignment?.ConfidenceScore ?? 60;
        var heroRelated = score is not null && !string.IsNullOrWhiteSpace(hero.EventCode) && score.EventCode.Equals(hero.EventCode, StringComparison.OrdinalIgnoreCase);
        return new WeeklyNarrationSegment(segment.SegmentId, segment.SegmentType, BuildHindiNarrationText(segment, assignment, score, heroRelated, regionName, weekStartDate), duration, weight, priority, heroRelated);
    }

    private static string BuildHindiNarrationText(WeeklyEpisodeSegment segment, WeeklySegmentAssignment? assignment, WeeklyEventScore? score, bool heroRelated, string regionName, DateOnly weekStartDate)
    {
        var objects = score?.ObjectCodes.Count > 0 == true ? string.Join(", ", score.ObjectCodes) : assignment?.AssignedObjects.Count > 0 == true ? string.Join(", ", assignment.AssignedObjects) : "मुख्य आकाशीय लक्ष्य";
        var date = score?.BestDateLocal?.ToString("dd MMMM") ?? assignment?.AssignedDateLocal?.ToString("dd MMMM") ?? weekStartDate.ToString("dd MMMM");
        var time = score?.BestTimeLocal?.ToString("HH:mm") ?? assignment?.AssignedBestTimeLocal?.ToString("HH:mm") ?? "सूर्यास्त के बाद";
        var direction = !string.IsNullOrWhiteSpace(score?.Direction) ? score!.Direction : "खुले क्षितिज";
        var altitude = score?.AltitudeDegrees is double alt ? $", लगभग {Math.Round(alt)} डिग्री ऊँचाई पर" : string.Empty;
        var title = score?.Title ?? assignment?.AssignedEventType ?? segment.Title;
        var reason = score?.Summary ?? assignment?.VisibilitySummary ?? segment.Purpose;
        var emphasis = heroRelated ? "यह इस सप्ताह की सबसे मज़बूत दृश्य कहानी है" : "इसे छोटी लेकिन उपयोगी निरीक्षण सूचना की तरह रखें";
        var scoreValue = score?.FinalScore ?? 0;
        var narrationWeight = score?.RecommendedNarrationWeight ?? 1d;
        return segment.SegmentType switch
        {
            "OpeningHook" => $"इस सप्ताह {regionName} के रात के आकाश में हमारी कहानी {objects} से शुरू होती है। {date} के आसपास, {time}, {direction} की ओर देखते हुए यह दृश्य सबसे साफ़ समझ आता है। {emphasis}; इसलिए आज का मार्गदर्शन सिर्फ़ सुंदर तस्वीर नहीं, बल्कि समय, दिशा और वास्तविक दृश्यता पर आधारित एक वैज्ञानिक रोडमैप है।",
            "WeeklySkyOverview" => $"सप्ताह की शुरुआत में आकाश का पैटर्न शांत है, लेकिन मुख्य संकेत स्पष्ट हैं: {objects}, चंद्रमा की रोशनी, और ग्रहों की स्थिति मिलकर देखने की योजना बनाते हैं। {reason}। अगर आप नंगी आँख से देख रहे हैं, तो पहले क्षितिज और दिशा पहचानें; अगर दूरबीन है, तो चमकीले लक्ष्यों से शुरुआत करें।",
            "HeroEvent" => $"मुख्य घटना है {title}: {objects}। सबसे बेहतर समय {date}, {time} के आसपास है, दिशा {direction}{altitude}। इस हिस्से को थोड़ा ठहरकर देखें, क्योंकि priority score {scoreValue} और narration weight {narrationWeight:0.0} बताता है कि दृश्यता, सौंदर्य और सीखने की संभावना मजबूत है। पहले चौड़ा आकाश देखें, फिर लक्ष्य को फ्रेम के केंद्र में लाएँ।",
            "MoonHighlights" => $"चंद्रमा इस सप्ताह दृश्य संतुलन तय करता है। {objects} के साथ इसका संबंध बताता है कि कब आकाश अधिक चमकीला होगा और कब हल्के लक्ष्य बेहतर दिखेंगे। {date} के आसपास चंद्रमा की दिशा और ऊँचाई देखकर योजना बनाएँ; तेज़ चांदनी में ग्रह और बड़े आकार की संरचनाएँ बेहतर रहती हैं।",
            "PlanetHighlights" => $"ग्रहों के लिए इस सप्ताह मुख्य संकेत {objects} हैं। {time} के बाद {direction} की ओर धीरे-धीरे स्कैन करें। चमकदार ग्रह स्थिर बिंदु की तरह दिखते हैं, टिमटिमाते तारों जैसे नहीं; इसलिए पहचान वैज्ञानिक तरीके से करें—दिशा, ऊँचाई, और पड़ोसी तारों की तुलना से। {reason}",
            "BestObservationWindow" => $"सबसे उपयोगी observing window {date} को {time} के आसपास बनती है। पहले {direction} क्षितिज को साफ़ रखें, फिर 10 से 15 मिनट आँखों को अंधेरे में अनुकूल होने दें। शहर की रोशनी कम हो तो वही समय फोटोग्राफी और नंगी आँख, दोनों के लिए बेहतर होगा।",
            "AstrophotographyTip" => $"फोटोग्राफी के लिए इस सप्ताह लक्ष्य को simple रखें: {objects} को wide frame में लें, horizon या foreground को reference की तरह इस्तेमाल करें। मोबाइल पर exposure थोड़ा कम रखें ताकि चमकीले ग्रह या चंद्रमा overexpose न हों। कैमरा हो तो tripod, short exposure, और कई frames लेकर बाद में best frame चुनें।",
            "WeeklySummary" => $"इस सप्ताह की checklist साफ़ है: {date}, {time}, {direction}, और मुख्य लक्ष्य {objects}। मौसम और स्थानीय प्रकाश प्रदूषण बदल सकता है, लेकिन आकाशीय geometry यही रहेगी। अगर आसमान साफ़ मिले, तो कुछ मिनट रुकिए—विज्ञान की सबसे अच्छी बात यही है कि सही समय पर देखा गया छोटा दृश्य भी ब्रह्मांड को बहुत नज़दीक महसूस करा देता है।",
            "ShortHook" => $"इस हफ्ते आसमान में {objects} पर नज़र रखें—सबसे अच्छा मौका {date}, {time} के आसपास।",
            "StrongestEvent" => $"मुख्य highlight है {title}: {objects}। {direction} की ओर देखें{altitude}; यह high-priority दृश्य है, इसलिए इसे miss न करें।",
            "WhereToLook" => $"देखने के लिए साफ़ {direction} क्षितिज चुनें, और पहले चमकीले reference points पहचानें।",
            "BestTime" => $"सबसे अच्छा समय {date} को {time} के आसपास है; कुछ मिनट पहले बाहर निकलकर आँखों को अंधेरे में adjust करें।",
            "CallToAction" => "आसमान साफ़ हो तो बाहर जाएँ, यह वैज्ञानिक sky-check करें, और अगले weekly forecast के लिए जुड़े रहें।",
            _ => $"इस खंड में {objects} पर ध्यान दें। {reason}"
        };
    }

    private static NarrationAssetMapEntry BuildAssetMapEntry(WeeklyNarrationSegment segment, string episodeType, IReadOnlyDictionary<string, SegmentProductionAssetBundle> bundles)
    {
        if (!bundles.TryGetValue(segment.SegmentId, out var bundle)) return new NarrationAssetMapEntry(segment.SegmentId, segment.SegmentType, episodeType, segment.NarrationText, [], []);
        var arranged = ArrangeAssetsWithVisualVariety(bundle.AssignedVisualAssets);
        return new NarrationAssetMapEntry(segment.SegmentId, segment.SegmentType, episodeType, segment.NarrationText, arranged.Select(x => x.AssetId).ToList(), arranged.Select(x => NormalizeAssetType(x.SourceType.ToString())).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IReadOnlyList<NarrationTimelineMapEntry> BuildTimelineMap(WeeklyNarrationEngineV2Input input, IReadOnlyList<(WeeklyNarrationSegment Segment, string EpisodeType)> segments, IReadOnlyDictionary<string, SegmentProductionAssetBundle> bundles, out IReadOnlyList<NarrationEmotionalResetMarker> resets, out bool varietyOk, out IReadOnlyList<string> warnings)
    {
        var resetList = new List<NarrationEmotionalResetMarker>();
        var warningList = new List<string>();
        var entries = new List<NarrationTimelineMapEntry>();
        var cursorByEpisode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var aiAssets = input.ProductionAssetManifest.SegmentBundles.SelectMany(x => x.AssignedVisualAssets).Where(x => x.SourceType == RealizedVisualAssetSourceType.AICinematic).DistinctBy(x => x.AssetId).ToList();
        var stellariumStreak = 0;
        foreach (var item in segments)
        {
            var start = cursorByEpisode.GetValueOrDefault(item.EpisodeType, 0);
            var end = start + item.Segment.EstimatedDurationSeconds;
            cursorByEpisode[item.EpisodeType] = end;
            bundles.TryGetValue(item.Segment.SegmentId, out var bundle);
            var assets = ArrangeAssetsWithVisualVariety(bundle?.AssignedVisualAssets ?? []);
            if (assets.Count == 0 && aiAssets.Count > 0) assets = [aiAssets[entries.Count % aiAssets.Count]];
            if (assets.Count(x => IsStellarium(x)) >= 3 && assets.All(IsStellarium) && aiAssets.Count > 0)
                assets = ArrangeAssetsWithVisualVariety(assets.Concat([aiAssets[entries.Count % aiAssets.Count]]).ToList());
            var sequence = BuildAssetSequence(assets, start, end, item.Segment.SegmentType).ToList();
            foreach (var assetType in sequence.Select(x => x.AssetType))
            {
                if (assetType.Contains("Stellarium", StringComparison.OrdinalIgnoreCase)) stellariumStreak++; else stellariumStreak = 0;
                if (stellariumStreak > 3) warningList.Add($"More than 3 consecutive Stellarium shots detected near segment {item.Segment.SegmentId}.");
            }
            entries.Add(new NarrationTimelineMapEntry(item.Segment.SegmentId, item.Segment.SegmentType, item.EpisodeType, start, end, sequence));
        }
        foreach (var episodeGroup in entries.GroupBy(x => x.EpisodeType))
        {
            var total = episodeGroup.Max(x => x.NarrationEnd);
            for (var second = 55; second < total; second += 55)
            {
                var asset = aiAssets.Count > 0 ? aiAssets[resetList.Count % aiAssets.Count] : null;
                resetList.Add(new NarrationEmotionalResetMarker(second, asset?.AssetId ?? "ai-cinematic-reset-placeholder", asset is null ? "AICinematicPlaceholder" : NormalizeAssetType(asset.SourceType.ToString()), $"Emotional reset for {episodeGroup.Key} retention cadence at approximately {second} seconds."));
            }
        }
        varietyOk = !warningList.Any(x => x.Contains("More than 3 consecutive Stellarium", StringComparison.OrdinalIgnoreCase));
        warnings = warningList;
        resets = resetList;
        return entries;
    }

    private static List<RealizedVisualAsset> ArrangeAssetsWithVisualVariety(IReadOnlyList<RealizedVisualAsset> assets)
    {
        var remaining = assets.ToList();
        var arranged = new List<RealizedVisualAsset>();
        var stellariumStreak = 0;
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(x => stellariumStreak >= 3 && !IsStellarium(x)) ?? remaining[0];
            remaining.Remove(next);
            arranged.Add(next);
            stellariumStreak = IsStellarium(next) ? stellariumStreak + 1 : 0;
        }
        return arranged;
    }

    private static IEnumerable<NarrationTimelineAssetSequenceEntry> BuildAssetSequence(IReadOnlyList<RealizedVisualAsset> assets, int start, int end, string segmentType)
    {
        if (assets.Count == 0) yield break;
        var duration = Math.Max(1, end - start);
        var shotCount = Math.Min(Math.Max(1, assets.Count), Math.Max(1, (int)Math.Ceiling(duration / 12d)));
        var baseDuration = duration / shotCount;
        var remainder = duration % shotCount;
        var cursor = start;
        for (var i = 0; i < shotCount; i++)
        {
            var asset = assets[i % assets.Count];
            var shotDuration = baseDuration + (i < remainder ? 1 : 0);
            yield return new NarrationTimelineAssetSequenceEntry(asset.AssetId, NormalizeAssetType(asset.SourceType.ToString()), asset.FilePath, cursor, cursor + shotDuration, i == 0 ? $"primary narration visual for {segmentType}" : "visual variety and pacing support");
            cursor += shotDuration;
        }
    }

    private static TValue? GetOrDefault<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary, TKey key) where TKey : notnull
    {
        return dictionary.TryGetValue(key, out var value) ? value : default;
    }

    private static WeeklyEventScore? ResolveEventScore(WeeklySegmentAssignment? assignment, IReadOnlyDictionary<string, WeeklyEventScore> events)
    {
        if (assignment is null) return null;
        return events.Values.FirstOrDefault(e => e.EventType.Equals(assignment.AssignedEventType, StringComparison.OrdinalIgnoreCase) && e.ObjectCodes.Any(o => assignment.AssignedObjects.Contains(o, StringComparer.OrdinalIgnoreCase))) ?? events.Values.FirstOrDefault(e => e.ObjectCodes.Any(o => assignment.AssignedObjects.Contains(o, StringComparer.OrdinalIgnoreCase)));
    }

    private static double ResolveWeight(WeeklySegmentAssignment? assignment, IReadOnlyDictionary<string, WeeklyEventScore> events, string segmentType)
    {
        var score = ResolveEventScore(assignment, events);
        if (score is not null) return score.RecommendedNarrationWeight;
        if (segmentType is "HeroEvent" or "StrongestEvent") return 1.8d;
        if (segmentType is "OpeningHook" or "ShortHook") return 1.3d;
        if (segmentType is "WeeklySummary" or "CallToAction") return 0.5d;
        return 1.0d;
    }

    private static bool IsStellarium(RealizedVisualAsset asset) => asset.SourceType is RealizedVisualAssetSourceType.StellariumBase or RealizedVisualAssetSourceType.StellariumExpanded;
    private static string NormalizeAssetType(string sourceType)
    {
        if (sourceType.Contains("Stellarium", StringComparison.OrdinalIgnoreCase)) return "Stellarium";
        if (sourceType.Contains("NASA", StringComparison.OrdinalIgnoreCase) || sourceType.Contains("JWST", StringComparison.OrdinalIgnoreCase)) return "NASA";
        if (sourceType.Contains("Motion", StringComparison.OrdinalIgnoreCase) || sourceType.Contains("Overlay", StringComparison.OrdinalIgnoreCase)) return "MotionGraphic";
        if (sourceType.Contains("AI", StringComparison.OrdinalIgnoreCase)) return "AICinematic";
        return sourceType;
    }
}
