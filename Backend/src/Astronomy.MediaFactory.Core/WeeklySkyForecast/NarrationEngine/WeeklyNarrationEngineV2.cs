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

public sealed record WeeklyNarrationSegment(string SegmentId, string SegmentType, string NarrationText, int EstimatedDurationSeconds, [property: JsonIgnore] double NarrationWeight, [property: JsonIgnore] int PriorityScore, bool HeroEventRelated);
public sealed record WeeklyNarrationPackage(Guid PipelineRunId, DateTime GeneratedAtUtc, string Language, string Style, int TargetDurationSeconds, int TotalEstimatedDurationSeconds, IReadOnlyList<WeeklyNarrationSegment> Segments);
public sealed record NarrationAssetMapEntry(string SegmentId, string SegmentType, string EpisodeType, string NarrationText, IReadOnlyList<string> AssetIds, IReadOnlyList<string> AssetTypes);
public sealed record NarrationTimelineAssetSequenceEntry(string AssetId, string AssetType, string AssetPath, int StartSecond, int EndSecond, string Purpose);
public sealed record NarrationTimelineMapEntry(string SegmentId, string SegmentType, string EpisodeType, int NarrationStart, int NarrationEnd, IReadOnlyList<NarrationTimelineAssetSequenceEntry> AssetSequence);
public sealed record NarrationEmotionalResetMarker(int ResetSecond, string AssetId, string AssetType, string Reason);
public sealed record WeeklyNarrationReport(Guid PipelineRunId, DateTime GeneratedAtUtc, IReadOnlyList<string> InputArtifacts, bool LongformNarrationReady, bool ShortformNarrationReady, bool NarrationAssetMappingReady, bool NarrationTimelineReady, int TotalLongformNarrationSeconds, int TotalShortformNarrationSeconds, int LongformSegmentCount, int ShortformSegmentCount, int AssetMappedSegmentCount, int TimelineMappedSegmentCount, IReadOnlyList<NarrationEmotionalResetMarker> EmotionalResetMarkers, bool StellariumVarietyRuleSatisfied, IReadOnlyList<string> Warnings);
public sealed record WeeklyNarrationEditorialReviewReport(Guid PipelineRunId, DateTime GeneratedAtUtc, bool NarrationEditorialRefinementReady, bool DocumentaryNarrationReady, bool VisualVarietyPassed, int RepeatedAssetSequenceCount, int InternalMetadataLeakCount, bool HeroEventLongerThanMoonHighlights, bool MoonHighlightsLongerThanBackground, IReadOnlyList<string> ForbiddenTerms, IReadOnlyDictionary<string, int> LongformSegmentDurations, IReadOnlyDictionary<string, IReadOnlyList<string>> SegmentAssetTypes, IReadOnlyList<NarrationEmotionalResetMarker> EmotionalResetMarkers, IReadOnlyList<string> Warnings);
public sealed record WeeklyNarrationEngineV2Result(WeeklyNarrationPackage LongformNarration, WeeklyNarrationPackage ShortformNarration, IReadOnlyList<NarrationAssetMapEntry> NarrationAssetMap, IReadOnlyList<NarrationTimelineMapEntry> NarrationTimelineMap, WeeklyNarrationReport Report, WeeklyNarrationEditorialReviewReport EditorialReviewReport, string LongformNarrationPath, string ShortformNarrationPath, string NarrationAssetMapPath, string NarrationTimelineMapPath, string WeeklyNarrationReportPath, string EditorialReviewReportPath, bool LongformNarrationReady, bool ShortformNarrationReady, bool NarrationAssetMappingReady, bool NarrationTimelineReady, int TotalLongformNarrationSeconds, int TotalShortformNarrationSeconds, bool NarrationEditorialRefinementReady, bool DocumentaryNarrationReady, bool VisualVarietyPassed, int RepeatedAssetSequenceCount, int InternalMetadataLeakCount);

public sealed class WeeklyNarrationEngineV2(ILogger<WeeklyNarrationEngineV2> logger) : IWeeklyNarrationEngineV2
{
    private const int LongformTargetSeconds = 380;
    private const int ShortformTargetSeconds = 50;
    private static readonly string[] ForbiddenNarrationTerms = ["priority score", "timeline weight", "narration weight", "classification", "hero event score", "event priority", "score", "weight"];
    private static readonly string[] RetentionAssetCodes = ["fast_cinematic_sky_hook", "cinematic_weekly_sky_reveal", "cosmic_retention_reset", "cosmic_closing_background", "shortform_call_to_action_background"];
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

        var longSegments = input.LongformPlan.Segments.Select((s, index) => BuildNarrationSegment(s, GetOrDefault(longAssignments, s.SegmentId), eventIndex, input.HeroEventSelection, longDurations[s.SegmentId], input.RegionName, input.WeekStartDate, index, input.LongformPlan.Segments.Count, false)).ToList();
        var shortSegments = input.ShortformPlan.Segments.Select((s, index) => BuildNarrationSegment(s, GetOrDefault(shortAssignments, s.SegmentId), eventIndex, input.HeroEventSelection, shortDurations[s.SegmentId], input.RegionName, input.WeekStartDate, index, input.ShortformPlan.Segments.Count, true)).ToList();

        var longform = new WeeklyNarrationPackage(input.PipelineRunId, DateTime.UtcNow, input.Language, "Scientific documentary narration with connected transitions, practical observation guidance, and no internal production metadata", LongformTargetSeconds, longSegments.Sum(x => x.EstimatedDurationSeconds), longSegments);
        var shortform = new WeeklyNarrationPackage(input.PipelineRunId, DateTime.UtcNow, input.Language, "Fast, memorable scientific documentary short-form narration with no filler", ShortformTargetSeconds, shortSegments.Sum(x => x.EstimatedDurationSeconds), shortSegments);
        var allSegments = longSegments.Select(x => (Segment: x, EpisodeType: input.LongformPlan.EpisodeType.ToString())).Concat(shortSegments.Select(x => (Segment: x, EpisodeType: input.ShortformPlan.EpisodeType.ToString()))).ToList();
        var bundleIndex = input.ProductionAssetManifest.SegmentBundles.ToDictionary(x => x.SegmentId, StringComparer.OrdinalIgnoreCase);
        var allAssets = input.ProductionAssetManifest.SegmentBundles.SelectMany(x => x.AssignedVisualAssets).DistinctBy(x => x.AssetId).ToList();

        var assetMap = allSegments.Select(x => BuildAssetMapEntry(x.Segment, x.EpisodeType, bundleIndex, allAssets)).ToList();
        var timelineMap = BuildTimelineMap(input, allSegments, bundleIndex, allAssets, out var resetMarkers, out var visualVarietyPassed, out var repeatedAssetSequenceCount, out var timelineWarnings);
        var internalMetadataLeakCount = CountInternalMetadataLeaks(longSegments.Concat(shortSegments).Select(x => x.NarrationText));
        var reportWarnings = timelineWarnings.Concat(assetMap.Where(x => x.AssetIds.Count == 0).Select(x => $"No assets mapped for narration segment {x.SegmentId}."));

        var heroLongerThanMoon = SegmentDuration(longSegments, "HeroEvent", "StrongestEvent") > SegmentDuration(longSegments, "MoonHighlights");
        var moonLongerThanBackground = SegmentDuration(longSegments, "MoonHighlights") > SegmentDuration(longSegments, "Background");
        var editorialWarnings = reportWarnings.ToList();
        if (!heroLongerThanMoon) editorialWarnings.Add("HeroEvent narration should be longer than MoonHighlights narration.");
        if (!moonLongerThanBackground) editorialWarnings.Add("MoonHighlights narration should be longer than background or overview narration.");
        if (internalMetadataLeakCount > 0) editorialWarnings.Add("Viewer-facing narration contains internal metadata terms.");
        if (!visualVarietyPassed) editorialWarnings.Add("More than two consecutive narration segments use the same lead asset type.");

        var editorialReport = new WeeklyNarrationEditorialReviewReport(
            input.PipelineRunId,
            DateTime.UtcNow,
            internalMetadataLeakCount == 0 && visualVarietyPassed && repeatedAssetSequenceCount <= 1 && heroLongerThanMoon && moonLongerThanBackground,
            internalMetadataLeakCount == 0 && longSegments.Count > 0 && shortSegments.Count > 0,
            visualVarietyPassed,
            repeatedAssetSequenceCount,
            internalMetadataLeakCount,
            heroLongerThanMoon,
            moonLongerThanBackground,
            ForbiddenNarrationTerms,
            longSegments.GroupBy(x => x.SegmentType, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Sum(segment => segment.EstimatedDurationSeconds), StringComparer.OrdinalIgnoreCase),
            assetMap.ToDictionary(x => $"{x.EpisodeType}:{x.SegmentId}", x => x.AssetTypes, StringComparer.OrdinalIgnoreCase),
            resetMarkers,
            editorialWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        var report = new WeeklyNarrationReport(input.PipelineRunId, DateTime.UtcNow, [input.WeeklyEpisodePlanPath, input.WeeklyLongformPlanPath, input.WeeklyShortformPlanPath, input.WeeklySegmentClassificationPlanPath, input.WeeklyEventPriorityReportPath, input.HeroEventSelectionPath, input.WeeklyProductionAssetManifestPath, input.WeeklyNarrationVisualTimelinePath, input.WeeklyStoryBeatsPath], longSegments.Count == input.LongformPlan.Segments.Count && longSegments.All(x => !string.IsNullOrWhiteSpace(x.NarrationText)), shortSegments.Count == input.ShortformPlan.Segments.Count && shortSegments.All(x => !string.IsNullOrWhiteSpace(x.NarrationText)), assetMap.Count == allSegments.Count && assetMap.All(x => x.AssetIds.Count > 0), timelineMap.Count == allSegments.Count && timelineMap.All(x => x.NarrationEnd > x.NarrationStart && x.AssetSequence.Count > 0), longform.TotalEstimatedDurationSeconds, shortform.TotalEstimatedDurationSeconds, longSegments.Count, shortSegments.Count, assetMap.Count(x => x.AssetIds.Count > 0), timelineMap.Count(x => x.AssetSequence.Count > 0), resetMarkers, visualVarietyPassed, editorialWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        var longformPath = Path.Combine(episodeDirectory, "longform-narration.json");
        var shortformPath = Path.Combine(episodeDirectory, "shortform-narration.json");
        var assetMapPath = Path.Combine(episodeDirectory, "narration-asset-map.json");
        var timelineMapPath = Path.Combine(episodeDirectory, "narration-timeline-map.json");
        var reportPath = Path.Combine(episodeDirectory, "weekly-narration-report.json");
        var editorialReviewReportPath = Path.Combine(episodeDirectory, "narration-editorial-review-report.json");
        await File.WriteAllTextAsync(longformPath, JsonSerializer.Serialize(longform, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(shortformPath, JsonSerializer.Serialize(shortform, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(assetMapPath, JsonSerializer.Serialize(assetMap, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(timelineMapPath, JsonSerializer.Serialize(new { input.PipelineRunId, generatedAtUtc = DateTime.UtcNow, emotionalResetMarkers = resetMarkers, segments = timelineMap }, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(editorialReviewReportPath, JsonSerializer.Serialize(editorialReport, JsonOptions), cancellationToken);

        logger.LogInformation("NARRATION_ENGINE_V2_COMPLETE pipelineRunId={PipelineRunId} longformSeconds={LongformSeconds} shortformSeconds={ShortformSeconds} editorialReady={EditorialReady}", input.PipelineRunId, report.TotalLongformNarrationSeconds, report.TotalShortformNarrationSeconds, editorialReport.NarrationEditorialRefinementReady);
        return new WeeklyNarrationEngineV2Result(longform, shortform, assetMap, timelineMap, report, editorialReport, longformPath, shortformPath, assetMapPath, timelineMapPath, reportPath, editorialReviewReportPath, report.LongformNarrationReady, report.ShortformNarrationReady, report.NarrationAssetMappingReady, report.NarrationTimelineReady, report.TotalLongformNarrationSeconds, report.TotalShortformNarrationSeconds, editorialReport.NarrationEditorialRefinementReady, editorialReport.DocumentaryNarrationReady, editorialReport.VisualVarietyPassed, editorialReport.RepeatedAssetSequenceCount, editorialReport.InternalMetadataLeakCount);
    }

    private static Dictionary<string, int> AllocateDurations(IReadOnlyList<WeeklyEpisodeSegment> segments, IReadOnlyDictionary<string, WeeklySegmentAssignment> assignments, IReadOnlyDictionary<string, WeeklyEventScore> events, int targetSeconds)
    {
        var raw = segments.Select(segment => { var assignment = GetOrDefault(assignments, segment.SegmentId); var weight = ResolveWeight(assignment, events, segment.SegmentType); var role = segment.SegmentType is "OpeningHook" or "WeeklySummary" or "ShortHook" or "CallToAction" ? 0.75d : 1d; if (segment.SegmentType is "HeroEvent" or "StrongestEvent") role = 1.35d; if (segment.SegmentType is "MoonHighlights") role = 1.1d; return new { segment.SegmentId, Value = Math.Max(1d, segment.TargetDurationSeconds * weight * role), segment.MinDurationSeconds, segment.MaxDurationSeconds, segment.SegmentType }; }).ToList();
        var total = raw.Sum(x => x.Value);
        var durations = raw.ToDictionary(x => x.SegmentId, x => Math.Clamp((int)Math.Round(x.Value / total * targetSeconds), x.MinDurationSeconds, x.MaxDurationSeconds), StringComparer.OrdinalIgnoreCase);
        var hero = raw.FirstOrDefault(x => x.SegmentType is "HeroEvent" or "StrongestEvent");
        var moon = raw.FirstOrDefault(x => x.SegmentType is "MoonHighlights");
        if (hero is not null && moon is not null && durations[hero.SegmentId] <= durations[moon.SegmentId] && durations[hero.SegmentId] < hero.MaxDurationSeconds)
        {
            durations[hero.SegmentId]++;
            if (durations[moon.SegmentId] > moon.MinDurationSeconds) durations[moon.SegmentId]--;
        }
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

    private static WeeklyNarrationSegment BuildNarrationSegment(WeeklyEpisodeSegment segment, WeeklySegmentAssignment? assignment, IReadOnlyDictionary<string, WeeklyEventScore> events, WeeklyHeroEventSelection hero, int duration, string regionName, DateOnly weekStartDate, int segmentIndex, int segmentCount, bool isShortform)
    {
        var score = ResolveEventScore(assignment, events);
        var weight = ResolveWeight(assignment, events, segment.SegmentType);
        var priority = score?.FinalScore ?? assignment?.ConfidenceScore ?? 60;
        var heroRelated = score is not null && !string.IsNullOrWhiteSpace(hero.EventCode) && score.EventCode.Equals(hero.EventCode, StringComparison.OrdinalIgnoreCase);
        return new WeeklyNarrationSegment(segment.SegmentId, segment.SegmentType, BuildHindiNarrationText(segment, assignment, score, heroRelated, regionName, weekStartDate, segmentIndex, segmentCount, isShortform), duration, weight, priority, heroRelated);
    }

    private static string BuildHindiNarrationText(WeeklyEpisodeSegment segment, WeeklySegmentAssignment? assignment, WeeklyEventScore? score, bool heroRelated, string regionName, DateOnly weekStartDate, int segmentIndex, int segmentCount, bool isShortform)
    {
        IReadOnlyList<string> objectCodes = score?.ObjectCodes.Count > 0 == true
            ? score.ObjectCodes
            : assignment?.AssignedObjects.Count > 0 == true
                ? assignment.AssignedObjects
                : ["मुख्य आकाशीय लक्ष्य"];
        var objects = FormatObjects(objectCodes);
        var date = score?.BestDateLocal?.ToString("dd MMMM") ?? assignment?.AssignedDateLocal?.ToString("dd MMMM") ?? weekStartDate.ToString("dd MMMM");
        var time = score?.BestTimeLocal?.ToString("HH:mm") ?? assignment?.AssignedBestTimeLocal?.ToString("HH:mm") ?? "सूर्यास्त के बाद";
        var direction = !string.IsNullOrWhiteSpace(score?.Direction) ? score!.Direction : "खुले क्षितिज";
        var altitude = score?.AltitudeDegrees is double alt ? $", लगभग {Math.Round(alt)} डिग्री ऊँचाई पर" : string.Empty;
        var title = score?.Title ?? assignment?.AssignedEventType ?? segment.Title;
        var reason = CleanViewerText(score?.Summary ?? assignment?.VisibilitySummary ?? segment.Purpose);
        var transition = segmentIndex switch
        {
            0 => $"इस सप्ताह {regionName} में रात का आकाश एक शांत शुरुआत से खुलता है।",
            1 => "अब जब दिशा और समय का ढाँचा सामने है, कहानी अपने मुख्य दृश्य की ओर बढ़ती है।",
            _ when segmentIndex == segmentCount - 1 => "अंत में, पूरी सप्ताहिक योजना को एक सरल निरीक्षण क्रम में बाँध लेते हैं।",
            _ => "इसके बाद ध्यान उसी आकाश में बदलती रोशनी और गहराई पर जाता है।"
        };

        if (isShortform)
        {
            return segment.SegmentType switch
            {
                "ShortHook" => $"इस हफ्ते {objects} आसमान का सबसे याद रखने लायक संकेत है। {date}, {time}—साफ़ {direction} चुनिए और जल्दी देखिए।",
                "StrongestEvent" => $"मुख्य दृश्य: {title}। {direction} की ओर{altitude} देखें; यह वही क्षण है जहाँ ग्रह, चंद्र रोशनी और रात की पारदर्शिता एक साथ काम करते हैं।",
                "WhereToLook" => $"फोन नीचे रखें, क्षितिज पहचानें, फिर चमकीले स्थिर बिंदुओं से रास्ता बनाइए। यही सबसे तेज़ तरीका है लक्ष्य तक पहुँचने का।",
                "BestTime" => $"सबसे अच्छा समय {date} को {time} के आसपास है। पाँच मिनट पहले बाहर निकलें, आँखों को अंधेरे में ढलने दें।",
                "CallToAction" => "आसमान साफ़ हो तो बाहर जाइए—यह छोटा sky-check आपको पूरे सप्ताह की सबसे अच्छी खिड़की दे सकता है।",
                _ => $"{objects} को {date}, {time} के आसपास देखें। साफ़ दिशा, कम रोशनी, और थोड़ी धैर्य—यही पूरी तैयारी है।"
            };
        }

        return segment.SegmentType switch
        {
            "OpeningHook" => $"{transition} पहली नज़र में यह सिर्फ़ एक और सप्ताह लग सकता है, लेकिन {date} के आसपास {time} पर {direction} की ओर देखने से आकाश की व्यवस्था बदलती हुई दिखाई देती है। {objects} हमें याद दिलाते हैं कि खगोलीय घटनाएँ अचानक नहीं होतीं; वे धीमे, मापने योग्य पैटर्न से बनती हैं। आज की यात्रा उसी पैटर्न को पढ़ने की कोशिश है—कब बाहर जाना है, कहाँ देखना है, और किस दृश्य को सबसे अधिक ध्यान देना है।",
            "WeeklySkyOverview" => $"सप्ताह की बड़ी तस्वीर यह है: चंद्र रोशनी, ग्रहों की स्थिति, और स्थानीय क्षितिज मिलकर observation plan बनाते हैं। पहले खुले आसमान की दिशा पहचानिए, फिर चमकीले संदर्भ बिंदुओं से रास्ता बनाइए। {reason}। इस overview का मकसद याद रखने लायक तैयारी देना है—बैटरी चार्ज, लाल रोशनी, और ऐसा स्थान जहाँ आसमान जितना हो सके खुला मिले।",
            "HeroEvent" => $"{transition} मुख्य दृश्य है {title}: {objects}। सबसे बेहतर अवसर {date}, {time} के आसपास बनता है; {direction} की ओर देखें{altitude}। इसकी अहमियत केवल सुंदरता में नहीं है। ऐसे alignment हमें सिखाते हैं कि आकाश में दूरी, कोण और चमक कैसे मिलकर एक पठनीय दृश्य बनाते हैं। पहले चौड़े फ्रेम में पूरे क्षेत्र को देखिए, फिर धीरे-धीरे लक्ष्य को केंद्र में लाइए। यदि दूरबीन है, तो उसे तुरंत नहीं लगाइए—नंगी आँख से pattern पहचानना इस घटना को समझने का पहला वैज्ञानिक कदम है।",
            "MoonHighlights" => $"जब मुख्य दृश्य तय हो जाए, तब चंद्रमा सप्ताह का प्रकाश-संतुलन निर्धारित करता है। {date} के आसपास उसकी उपस्थिति यह बताएगी कि हल्के तारे दबेंगे या ग्रह और बड़े आकार की संरचनाएँ अधिक साफ़ लगेंगी। तेज़ चांदनी में detail के बजाय आकार, दिशा और separation पर ध्यान दें। यदि चंद्रमा क्षितिज के पास हो, तो foreground के साथ उसका पैमाना समझना आसान होता है; ऊपर चढ़ने पर वही रोशनी आकाश को अधिक उजला बना देती है।",
            "PlanetHighlights" => $"बाद में सप्ताह में, ग्रहों की पहचान observation को शिक्षा में बदल देती है। {objects} को खोजते समय याद रखें कि ग्रह अक्सर स्थिर, साफ़ बिंदु की तरह दिखते हैं, जबकि आसपास के तारे अधिक टिमटिमा सकते हैं। {time} के बाद {direction} की ओर धीरे-धीरे scan करें। दिशा, ऊँचाई और पड़ोसी तारों से तुलना करके पहचान पक्की करें; यही तरीका अनुमान को वैज्ञानिक निरीक्षण में बदलता है।",
            "BestObservationWindow" => $"एक बार लक्ष्य तय हो जाए, सबसे उपयोगी observing window {date} को {time} के आसपास खुलती है। बाहर निकलने से पहले बादल, धुंध और स्थानीय रोशनी देख लें। स्थान पर पहुँचकर 10 से 15 मिनट आँखों को अंधेरे में अनुकूल होने दें; इसी छोटे इंतज़ार के बाद faint details उभरने लगती हैं। यदि horizon साफ़ है, तो पहले wide view लें, फिर ऊँचाई और दिशा के आधार पर आगे बढ़ें।",
            "AstrophotographyTip" => $"After observing the grouping, photography के लिए frame को simple रखें। {objects} को wide composition में रखें और horizon, पेड़ या इमारत को scale reference की तरह इस्तेमाल करें। मोबाइल पर exposure थोड़ा घटाएँ ताकि चमकीले लक्ष्य जलकर सफेद न हो जाएँ। कैमरा है तो tripod, short exposure, और कई frames लें; बाद में सबसे steady frame चुनना एक लंबी exposure से बेहतर परिणाम दे सकता है।",
            "WeeklySummary" => $"{transition} इस सप्ताह की checklist सरल है: {date}, {time}, {direction}, और मुख्य लक्ष्य {objects}। मौसम बदल सकता है, शहर की रोशनी बाधा बन सकती है, लेकिन celestial geometry अपने क्रम में चलती रहती है। अगर आसमान साफ़ मिले, तो कुछ मिनट रुकिए। अक्सर astronomy की सबसे गहरी अनुभूति बड़े telescope से नहीं, बल्कि सही समय पर खुले आकाश को ध्यान से पढ़ने से आती है।",
            _ => $"{transition} इस खंड में {objects} पर ध्यान दें। {reason}"
        };
    }

    private static NarrationAssetMapEntry BuildAssetMapEntry(WeeklyNarrationSegment segment, string episodeType, IReadOnlyDictionary<string, SegmentProductionAssetBundle> bundles, IReadOnlyList<RealizedVisualAsset> allAssets)
    {
        bundles.TryGetValue(segment.SegmentId, out var bundle);
        var arranged = ResolveEditorialAssets(segment.SegmentType, episodeType, bundle?.AssignedVisualAssets ?? [], allAssets);
        return new NarrationAssetMapEntry(segment.SegmentId, segment.SegmentType, episodeType, segment.NarrationText, arranged.Select(x => x.AssetId).ToList(), arranged.Select(x => NormalizeAssetType(x.SourceType.ToString())).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IReadOnlyList<NarrationTimelineMapEntry> BuildTimelineMap(WeeklyNarrationEngineV2Input input, IReadOnlyList<(WeeklyNarrationSegment Segment, string EpisodeType)> segments, IReadOnlyDictionary<string, SegmentProductionAssetBundle> bundles, IReadOnlyList<RealizedVisualAsset> allAssets, out IReadOnlyList<NarrationEmotionalResetMarker> resets, out bool visualVarietyPassed, out int repeatedAssetSequenceCount, out IReadOnlyList<string> warnings)
    {
        var resetList = new List<NarrationEmotionalResetMarker>();
        var warningList = new List<string>();
        var entries = new List<NarrationTimelineMapEntry>();
        var cursorByEpisode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var previousLeadTypeByEpisode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var streakByEpisode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        repeatedAssetSequenceCount = 0;

        foreach (var item in segments)
        {
            var start = cursorByEpisode.GetValueOrDefault(item.EpisodeType, 0);
            var end = start + item.Segment.EstimatedDurationSeconds;
            cursorByEpisode[item.EpisodeType] = end;
            bundles.TryGetValue(item.Segment.SegmentId, out var bundle);
            var assets = ResolveEditorialAssets(item.Segment.SegmentType, item.EpisodeType, bundle?.AssignedVisualAssets ?? [], allAssets);
            assets = AvoidLeadTypeStreak(assets, previousLeadTypeByEpisode.GetValueOrDefault(item.EpisodeType), streakByEpisode.GetValueOrDefault(item.EpisodeType));
            var sequence = BuildAssetSequence(assets, start, end, item.Segment.SegmentType).ToList();
            InjectRetentionResetAssets(sequence, start, end, item.EpisodeType, allAssets, resetList);

            var leadType = sequence.FirstOrDefault()?.AssetType ?? "Missing";
            var previous = previousLeadTypeByEpisode.GetValueOrDefault(item.EpisodeType);
            var streak = string.Equals(previous, leadType, StringComparison.OrdinalIgnoreCase) ? streakByEpisode.GetValueOrDefault(item.EpisodeType) + 1 : 1;
            if (streak > 2)
            {
                repeatedAssetSequenceCount++;
                warningList.Add($"More than two consecutive narration segments use {leadType} as the lead visual near segment {item.Segment.SegmentId}.");
            }
            previousLeadTypeByEpisode[item.EpisodeType] = leadType;
            streakByEpisode[item.EpisodeType] = streak;
            entries.Add(new NarrationTimelineMapEntry(item.Segment.SegmentId, item.Segment.SegmentType, item.EpisodeType, start, end, sequence));
        }

        visualVarietyPassed = repeatedAssetSequenceCount <= 1;
        warnings = warningList;
        resets = resetList;
        return entries;
    }

    private static List<RealizedVisualAsset> ResolveEditorialAssets(string segmentType, string episodeType, IReadOnlyList<RealizedVisualAsset> localAssets, IReadOnlyList<RealizedVisualAsset> allAssets)
    {
        var preferredTypes = PreferredAssetTypes(segmentType, episodeType);
        var selected = new List<RealizedVisualAsset>();
        foreach (var preferredType in preferredTypes)
        {
            var match = localAssets.Concat(allAssets).FirstOrDefault(asset => AssetMatches(asset, preferredType) && selected.All(x => !x.AssetId.Equals(asset.AssetId, StringComparison.OrdinalIgnoreCase)));
            if (match is not null) selected.Add(match);
        }
        foreach (var asset in localAssets.Concat(allAssets).Where(asset => selected.All(x => !x.AssetId.Equals(asset.AssetId, StringComparison.OrdinalIgnoreCase))))
        {
            selected.Add(asset);
            if (selected.Count >= Math.Max(3, preferredTypes.Count)) break;
        }
        return ArrangeAssetsWithVisualVariety(selected);
    }

    private static IReadOnlyList<string> PreferredAssetTypes(string segmentType, string episodeType) => segmentType switch
    {
        "OpeningHook" => ["AICinematic", "Stellarium", "MotionGraphic"],
        "WeeklySkyOverview" => ["MotionGraphic", "Stellarium", "AICinematic"],
        "HeroEvent" or "StrongestEvent" => ["Stellarium", "NASA", "MotionGraphic"],
        "MoonHighlights" => ["Stellarium", "NASA"],
        "PlanetHighlights" => ["Stellarium", "JWST", "NASA"],
        "BestObservationWindow" or "BestTime" or "WhereToLook" => ["MotionGraphic", "WhereToLook"],
        "AstrophotographyTip" => ["ExpandedStellarium", "AICinematic"],
        "WeeklySummary" or "CallToAction" => ["AICinematic", "MotionGraphic"],
        "ShortHook" => ["AICinematic", "MotionGraphic"],
        _ => episodeType.Contains("Short", StringComparison.OrdinalIgnoreCase) ? ["AICinematic", "MotionGraphic", "Stellarium"] : ["Stellarium", "NASA", "MotionGraphic"]
    };

    private static List<RealizedVisualAsset> ArrangeAssetsWithVisualVariety(IReadOnlyList<RealizedVisualAsset> assets)
    {
        var remaining = assets.ToList();
        var arranged = new List<RealizedVisualAsset>();
        while (remaining.Count > 0)
        {
            var previousType = arranged.Count == 0 ? null : NormalizeAssetType(arranged[^1].SourceType.ToString());
            var next = remaining.FirstOrDefault(x => !string.Equals(NormalizeAssetType(x.SourceType.ToString()), previousType, StringComparison.OrdinalIgnoreCase)) ?? remaining[0];
            remaining.Remove(next);
            arranged.Add(next);
        }
        return arranged;
    }

    private static List<RealizedVisualAsset> AvoidLeadTypeStreak(List<RealizedVisualAsset> assets, string? previousLeadType, int previousStreak)
    {
        if (assets.Count <= 1 || previousStreak < 2) return assets;
        var alternative = assets.Skip(1).FirstOrDefault(x => !string.Equals(NormalizeAssetType(x.SourceType.ToString()), previousLeadType, StringComparison.OrdinalIgnoreCase));
        if (alternative is null) return assets;
        assets.Remove(alternative);
        assets.Insert(0, alternative);
        return assets;
    }

    private static IEnumerable<NarrationTimelineAssetSequenceEntry> BuildAssetSequence(IReadOnlyList<RealizedVisualAsset> assets, int start, int end, string segmentType)
    {
        if (assets.Count == 0) yield break;
        var duration = Math.Max(1, end - start);
        var shotCount = Math.Min(Math.Max(1, assets.Count), Math.Max(1, (int)Math.Ceiling(duration / 10d)));
        var baseDuration = duration / shotCount;
        var remainder = duration % shotCount;
        var cursor = start;
        for (var i = 0; i < shotCount; i++)
        {
            var asset = assets[i % assets.Count];
            var shotDuration = baseDuration + (i < remainder ? 1 : 0);
            yield return new NarrationTimelineAssetSequenceEntry(asset.AssetId, NormalizeAssetType(asset.SourceType.ToString()), asset.FilePath, cursor, cursor + shotDuration, i == 0 ? $"primary documentary visual for {segmentType}" : "supporting visual variety for narration pacing");
            cursor += shotDuration;
        }
    }

    private static void InjectRetentionResetAssets(List<NarrationTimelineAssetSequenceEntry> sequence, int start, int end, string episodeType, IReadOnlyList<RealizedVisualAsset> allAssets, List<NarrationEmotionalResetMarker> resetList)
    {
        var firstReset = ((start / 55) + 1) * 55;
        for (var second = firstReset; second < end; second += 55)
        {
            var resetAsset = PickRetentionAsset(allAssets, resetList.Count);
            var assetId = resetAsset?.AssetId ?? $"retention-reset-placeholder-{resetList.Count + 1}";
            var assetType = resetAsset is null ? "AICinematicPlaceholder" : NormalizeAssetType(resetAsset.SourceType.ToString());
            var assetPath = resetAsset?.FilePath ?? string.Empty;
            resetList.Add(new NarrationEmotionalResetMarker(second, assetId, assetType, $"Editorial emotional reset for {episodeType} retention cadence at approximately {second} seconds."));
            sequence.Add(new NarrationTimelineAssetSequenceEntry(assetId, assetType, assetPath, Math.Max(start, second - 2), Math.Min(end, second + 4), "emotional reset wonder image for retention pacing"));
        }
        sequence.Sort((a, b) => a.StartSecond.CompareTo(b.StartSecond));
    }

    private static RealizedVisualAsset? PickRetentionAsset(IReadOnlyList<RealizedVisualAsset> allAssets, int index)
    {
        var code = RetentionAssetCodes[index % RetentionAssetCodes.Length];
        return allAssets.FirstOrDefault(x => x.AssetCode.Equals(code, StringComparison.OrdinalIgnoreCase))
            ?? allAssets.Where(x => x.SourceType is RealizedVisualAssetSourceType.AICinematic or RealizedVisualAssetSourceType.NASA or RealizedVisualAssetSourceType.JWST).Skip(index % Math.Max(1, allAssets.Count(x => x.SourceType is RealizedVisualAssetSourceType.AICinematic or RealizedVisualAssetSourceType.NASA or RealizedVisualAssetSourceType.JWST))).FirstOrDefault()
            ?? allAssets.FirstOrDefault();
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

    private static int SegmentDuration(IReadOnlyList<WeeklyNarrationSegment> segments, params string[] segmentTypes) => segments.Where(x => segmentTypes.Contains(x.SegmentType, StringComparer.OrdinalIgnoreCase)).Sum(x => x.EstimatedDurationSeconds);

    private static int CountInternalMetadataLeaks(IEnumerable<string> narrationTexts) => narrationTexts.Sum(text => ForbiddenNarrationTerms.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)));

    private static string FormatObjects(IReadOnlyList<string> objects)
    {
        var clean = objects.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        if (clean.Count == 0) return "मुख्य आकाशीय लक्ष्य";
        if (clean.Count == 1) return clean[0];
        return string.Join(", ", clean.Take(clean.Count - 1)) + " और " + clean[^1];
    }

    private static string CleanViewerText(string value)
    {
        var clean = value;
        foreach (var term in ForbiddenNarrationTerms) clean = clean.Replace(term, "महत्वपूर्ण संकेत", StringComparison.OrdinalIgnoreCase);
        return clean;
    }

    private static bool AssetMatches(RealizedVisualAsset asset, string preferredType)
    {
        var normalized = NormalizeAssetType(asset.SourceType.ToString());
        if (normalized.Equals(preferredType, StringComparison.OrdinalIgnoreCase)) return true;
        return preferredType switch
        {
            "NASA" => asset.SourceType is RealizedVisualAssetSourceType.NASA,
            "JWST" => asset.SourceType is RealizedVisualAssetSourceType.JWST,
            "MotionGraphic" => asset.SourceType is RealizedVisualAssetSourceType.MotionGraphics,
            "WhereToLook" => asset.SourceType is RealizedVisualAssetSourceType.EducationalOverlay || asset.AssetCode.Contains("where", StringComparison.OrdinalIgnoreCase),
            "ExpandedStellarium" => asset.SourceType is RealizedVisualAssetSourceType.StellariumExpanded,
            "Stellarium" => asset.SourceType is RealizedVisualAssetSourceType.StellariumBase,
            "AICinematic" => asset.SourceType is RealizedVisualAssetSourceType.AICinematic,
            _ => false
        };
    }

    private static string NormalizeAssetType(string sourceType)
    {
        if (sourceType.Contains("Expanded", StringComparison.OrdinalIgnoreCase)) return "ExpandedStellarium";
        if (sourceType.Contains("Stellarium", StringComparison.OrdinalIgnoreCase)) return "Stellarium";
        if (sourceType.Contains("JWST", StringComparison.OrdinalIgnoreCase)) return "JWST";
        if (sourceType.Contains("NASA", StringComparison.OrdinalIgnoreCase)) return "NASA";
        if (sourceType.Contains("Overlay", StringComparison.OrdinalIgnoreCase)) return "WhereToLook";
        if (sourceType.Contains("Motion", StringComparison.OrdinalIgnoreCase)) return "MotionGraphic";
        if (sourceType.Contains("AI", StringComparison.OrdinalIgnoreCase)) return "AICinematic";
        return sourceType;
    }
}
