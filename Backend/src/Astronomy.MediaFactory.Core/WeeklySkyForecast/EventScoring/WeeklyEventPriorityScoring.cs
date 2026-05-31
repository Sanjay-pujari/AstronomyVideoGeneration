using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.EventScoring;

public interface IWeeklyEventPriorityScoringEngine
{
    Task<WeeklyEventPriorityScoringResult> ScoreAndPersistAsync(WeeklyEventPriorityScoringInput input, CancellationToken cancellationToken);
}

public sealed record WeeklyEventPriorityScoringInput(
    Guid PipelineRunId,
    string WorkingDirectoryRoot,
    string SkyfieldWeeklyResponsePath,
    string WeeklyStoryBeatsPath,
    string WeeklyScenesManifestPath,
    string WeeklySegmentClassificationPlanPath,
    string WeeklyVisualAssetPlanPath,
    string WeeklyProductionAssetManifestPath,
    string WeeklyNarrationVisualTimelinePath,
    WeeklyAstronomyEventExtractionResult EventExtractionResult);

public sealed record EventPriorityWeights(
    int VisibilityMax = 25,
    int BrightnessMax = 15,
    int RarityMax = 20,
    int EducationalMax = 10,
    int VisualBeautyMax = 15,
    int ViralPotentialMax = 15);

public sealed record WeeklyEventScore(
    string EventCode,
    string EventType,
    string Title,
    string Summary,
    IReadOnlyList<string> ObjectCodes,
    string? PrimaryObject,
    DateOnly? BestDateLocal,
    TimeOnly? BestTimeLocal,
    string? Direction,
    double? AltitudeDegrees,
    double? Magnitude,
    double? AngularSeparationDegrees,
    int VisibilityScore,
    int BrightnessScore,
    int RarityScore,
    int EducationalScore,
    int VisualBeautyScore,
    int ViralPotentialScore,
    int FinalScore,
    string Classification,
    double RecommendedNarrationWeight,
    double RecommendedTimelineWeight,
    bool RecommendedThumbnailCandidate,
    bool RecommendedOpeningHook,
    bool RecommendedHeroEvent,
    string Reason,
    IReadOnlyList<string> ScoringSignals);

public sealed record WeeklyHeroEventSelection(
    string EventCode,
    int FinalScore,
    string Classification,
    string Reason,
    bool SelectedAsHeroEvent);

public sealed record WeeklyEventCandidateReport(
    DateTime GeneratedAtUtc,
    string CandidateType,
    int CandidateCount,
    IReadOnlyList<WeeklyEventScore> Candidates);

public sealed record WeeklyEventPriorityReport(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    string ScoringModelVersion,
    EventPriorityWeights Weights,
    IReadOnlyList<string> InputArtifacts,
    int TotalEventCount,
    WeeklyHeroEventSelection? HeroEventSelection,
    IReadOnlyList<string> TopThreeEventCodes,
    IReadOnlyList<WeeklyEventScore> Events);

public sealed record WeeklyEventPriorityScoringResult(
    WeeklyEventPriorityReport Report,
    WeeklyHeroEventSelection HeroEventSelection,
    WeeklyEventCandidateReport ThumbnailCandidateReport,
    WeeklyEventCandidateReport OpeningHookCandidateReport,
    string WeeklyEventPriorityReportPath,
    string HeroEventSelectionPath,
    string ThumbnailCandidateReportPath,
    string OpeningHookCandidateReportPath,
    string? HighestPriorityEventCode,
    int HighestPriorityEventScore,
    string? HeroEventClassification,
    IReadOnlyList<string> TopThreeEventCodes,
    bool EventPriorityScoringReady);

public sealed class WeeklyEventPriorityScoringEngine(ILogger<WeeklyEventPriorityScoringEngine> logger) : IWeeklyEventPriorityScoringEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<WeeklyEventPriorityScoringResult> ScoreAndPersistAsync(WeeklyEventPriorityScoringInput input, CancellationToken cancellationToken)
    {
        logger.LogInformation("EVENT_PRIORITY_SCORING_START pipelineRunId={PipelineRunId} root={Root}", input.PipelineRunId, input.WorkingDirectoryRoot);

        var episodeDirectory = Path.Combine(input.WorkingDirectoryRoot, "episode");
        Directory.CreateDirectory(episodeDirectory);

        var artifactText = await ReadArtifactTextAsync(input, cancellationToken);
        var rankedEvents = input.EventExtractionResult.ExtractedEvents
            .Select(ev => ScoreEvent(ev, artifactText))
            .OrderByDescending(x => x.FinalScore)
            .ThenByDescending(x => x.VisibilityScore)
            .ThenByDescending(x => x.RarityScore)
            .ThenBy(x => x.BestDateLocal)
            .ThenBy(x => x.EventCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var topThree = rankedEvents.Take(3).Select(x => x.EventCode).ToList();
        var hero = rankedEvents.FirstOrDefault();
        var heroSelection = hero is null
            ? new WeeklyHeroEventSelection(string.Empty, 0, "Background", "No astronomy events were available for priority scoring.", false)
            : new WeeklyHeroEventSelection(
                hero.EventCode,
                hero.FinalScore,
                hero.Classification,
                $"Selected as the highest scoring event because it combines {hero.VisibilityScore}/25 visibility, {hero.RarityScore}/20 rarity, {hero.VisualBeautyScore}/15 visual beauty, and {hero.ViralPotentialScore}/15 viral potential.",
                true);

        rankedEvents = rankedEvents
            .Select(e => e with
            {
                RecommendedHeroEvent = hero is not null && e.EventCode.Equals(hero.EventCode, StringComparison.OrdinalIgnoreCase),
                RecommendedThumbnailCandidate = topThree.Contains(e.EventCode, StringComparer.OrdinalIgnoreCase),
                RecommendedOpeningHook = topThree.Contains(e.EventCode, StringComparer.OrdinalIgnoreCase)
            })
            .ToList();

        foreach (var score in rankedEvents)
        {
            logger.LogInformation("EVENT_SCORE_CALCULATED eventCode={EventCode} eventType={EventType} finalScore={FinalScore} classification={Classification}", score.EventCode, score.EventType, score.FinalScore, score.Classification);
        }
        logger.LogInformation("EVENT_PRIORITY_RANKING_COMPLETE eventCount={EventCount} topThree={TopThree}", rankedEvents.Count, string.Join(",", topThree));
        logger.LogInformation("HERO_EVENT_SELECTED eventCode={EventCode} finalScore={FinalScore} classification={Classification}", heroSelection.EventCode, heroSelection.FinalScore, heroSelection.Classification);

        var thumbnailReport = new WeeklyEventCandidateReport(DateTime.UtcNow, "Thumbnail", rankedEvents.Take(3).ToList().Count, rankedEvents.Take(3).ToList());
        logger.LogInformation("THUMBNAIL_CANDIDATES_SELECTED candidateCount={CandidateCount} eventCodes={EventCodes}", thumbnailReport.CandidateCount, string.Join(",", thumbnailReport.Candidates.Select(x => x.EventCode)));

        var openingHookReport = new WeeklyEventCandidateReport(DateTime.UtcNow, "OpeningHook", rankedEvents.Take(3).ToList().Count, rankedEvents.Take(3).ToList());
        logger.LogInformation("OPENING_HOOK_CANDIDATES_SELECTED candidateCount={CandidateCount} eventCodes={EventCodes}", openingHookReport.CandidateCount, string.Join(",", openingHookReport.Candidates.Select(x => x.EventCode)));

        var report = new WeeklyEventPriorityReport(
            input.PipelineRunId,
            DateTime.UtcNow,
            "event-priority-scoring-v2",
            new EventPriorityWeights(),
            BuildInputArtifacts(input),
            rankedEvents.Count,
            heroSelection,
            topThree,
            rankedEvents);

        var reportPath = Path.Combine(episodeDirectory, "weekly-event-priority-report.json");
        var heroPath = Path.Combine(episodeDirectory, "heroEventSelection.json");
        var thumbnailPath = Path.Combine(episodeDirectory, "thumbnail-candidate-report.json");
        var openingHookPath = Path.Combine(episodeDirectory, "opening-hook-candidate-report.json");

        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(heroPath, JsonSerializer.Serialize(heroSelection, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(thumbnailPath, JsonSerializer.Serialize(thumbnailReport, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(openingHookPath, JsonSerializer.Serialize(openingHookReport, JsonOptions), cancellationToken);

        logger.LogInformation("EVENT_PRIORITY_SCORING_COMPLETE reportPath={ReportPath} heroPath={HeroPath} thumbnailPath={ThumbnailPath} openingHookPath={OpeningHookPath}", reportPath, heroPath, thumbnailPath, openingHookPath);

        return new WeeklyEventPriorityScoringResult(
            report,
            heroSelection,
            thumbnailReport,
            openingHookReport,
            reportPath,
            heroPath,
            thumbnailPath,
            openingHookPath,
            hero?.EventCode,
            hero?.FinalScore ?? 0,
            hero?.Classification,
            topThree,
            rankedEvents.Count > 0 && hero is not null && File.Exists(reportPath) && File.Exists(heroPath) && File.Exists(thumbnailPath) && File.Exists(openingHookPath));
    }

    private static WeeklyEventScore ScoreEvent(WeeklyAstronomyEvent ev, string artifactText)
    {
        var signals = new List<string>();
        var eventCode = ResolveEventCode(ev);
        var objectCodes = ev.Objects.Select(o => o.ObjectCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var visibility = ScoreVisibility(ev, signals);
        var brightness = ScoreBrightness(ev, objectCodes, signals);
        var rarity = ScoreRarity(ev, signals);
        var artifactMentions = CountArtifactMentions(ev, objectCodes, artifactText);
        var educational = ScoreEducationalValue(ev, artifactMentions, signals);
        var beauty = ScoreVisualBeauty(ev, objectCodes, artifactMentions, signals);
        var viral = ScoreViralPotential(ev, objectCodes, rarity, beauty, artifactMentions, signals);
        var final = Clamp(visibility + brightness + rarity + educational + beauty + viral, 0, 100);
        var classification = Classify(final);

        return new WeeklyEventScore(
            eventCode,
            ev.EventType.ToString(),
            ev.Title,
            ev.Summary,
            objectCodes,
            ev.PrimaryObject,
            ev.BestDateLocal,
            ev.BestTimeLocal,
            ev.Direction,
            ev.AltitudeDegrees,
            ev.Magnitude,
            ev.AngularSeparationDegrees,
            visibility,
            brightness,
            rarity,
            educational,
            beauty,
            viral,
            final,
            classification,
            NarrationWeight(classification),
            TimelineWeight(classification),
            false,
            false,
            false,
            BuildReason(ev, final, classification),
            signals);
    }

    private static int ScoreVisibility(WeeklyAstronomyEvent ev, List<string> signals)
    {
        var altitude = ev.AltitudeDegrees ?? ev.Objects.Select(o => o.AltitudeDegrees ?? 0).DefaultIfEmpty(0).Max();
        var altitudeScore = altitude switch
        {
            >= 60 => 10,
            >= 40 => 9,
            >= 25 => 7,
            >= 15 => 5,
            >= 8 => 3,
            _ => 1
        };
        signals.Add($"Altitude contribution {altitudeScore}/10 from {altitude:0.#} degrees.");

        var rawVisibility = ev.VisibilityScore <= 1 ? ev.VisibilityScore * 100 : ev.VisibilityScore;
        var visibilityWindowScore = Clamp((int)Math.Round(rawVisibility / 100d * 7), 0, 7);
        signals.Add($"Visibility-window contribution {visibilityWindowScore}/7 from normalized visibility {rawVisibility:0.#}.");

        var darknessScore = ev.BestTimeLocal is null ? 4 : IsDarkTime(ev.BestTimeLocal.Value) ? 5 : 3;
        signals.Add($"Darkness contribution {darknessScore}/5 from best local time {ev.BestTimeLocal?.ToString() ?? "unknown"}.");

        var moonInterferenceScore = EstimateMoonInterferenceScore(ev);
        signals.Add($"Moon-interference contribution {moonInterferenceScore}/3.");

        return Clamp(altitudeScore + visibilityWindowScore + darknessScore + moonInterferenceScore, 0, 25);
    }

    private static int ScoreBrightness(WeeklyAstronomyEvent ev, IReadOnlyList<string> objectCodes, List<string> signals)
    {
        if (ev.Magnitude.HasValue)
        {
            var magnitudeScore = ev.Magnitude.Value switch
            {
                <= -3 => 15,
                <= -1 => 13,
                <= 1 => 11,
                <= 3 => 8,
                <= 5 => 5,
                _ => 2
            };
            signals.Add($"Brightness contribution {magnitudeScore}/15 from magnitude {ev.Magnitude:0.#}.");
            return magnitudeScore;
        }

        var brightObjects = objectCodes.Count(IsPublicInterestObject);
        var score = ev.EventType switch
        {
            WeeklyAstronomyEventType.Conjunction => 10 + Math.Min(3, brightObjects),
            WeeklyAstronomyEventType.Grouping => 11 + Math.Min(4, brightObjects),
            WeeklyAstronomyEventType.HeroObject => IsPublicInterestObject(ev.PrimaryObject ?? string.Empty) ? 13 : 9,
            WeeklyAstronomyEventType.BestViewingWindow => 9,
            _ => 7 + Math.Min(4, brightObjects)
        };
        signals.Add($"Brightness contribution {Clamp(score, 0, 15)}/15 from {brightObjects} prominent public-interest object(s).");
        return Clamp(score, 0, 15);
    }

    private static int ScoreRarity(WeeklyAstronomyEvent ev, List<string> signals)
    {
        var score = ev.EventType switch
        {
            WeeklyAstronomyEventType.RareEvent => 20,
            WeeklyAstronomyEventType.Conjunction => 16,
            WeeklyAstronomyEventType.Grouping => ev.ObjectCount >= 3 ? 18 : 15,
            WeeklyAstronomyEventType.TelescopeOpportunity => 12,
            WeeklyAstronomyEventType.DeepSkyHighlight => 13,
            WeeklyAstronomyEventType.BestViewingWindow => 8,
            WeeklyAstronomyEventType.DirectionalObservation => 6,
            _ => 9
        };
        if (ev.AngularSeparationDegrees is <= 3) score += 2;
        if (ev.RarityScore >= 70) score += 2;
        signals.Add($"Rarity contribution {Clamp(score, 0, 20)}/20 from {ev.EventType}, separation {ev.AngularSeparationDegrees?.ToString("0.#") ?? "n/a"}, extractor rarity {ev.RarityScore:0.#}.");
        return Clamp(score, 0, 20);
    }

    private static int ScoreEducationalValue(WeeklyAstronomyEvent ev, int artifactMentions, List<string> signals)
    {
        var score = ev.EventType switch
        {
            WeeklyAstronomyEventType.DirectionalObservation => 10,
            WeeklyAstronomyEventType.BestViewingWindow => 9,
            WeeklyAstronomyEventType.Conjunction or WeeklyAstronomyEventType.Grouping => 9,
            WeeklyAstronomyEventType.TelescopeOpportunity => 8,
            WeeklyAstronomyEventType.DeepSkyHighlight => 8,
            _ => 7
        };
        if (!string.IsNullOrWhiteSpace(ev.RecommendedNarrationAngle)) score += 1;
        if (artifactMentions >= 2) score += 1;
        signals.Add($"Educational contribution {Clamp(score, 0, 10)}/10 from teaching value of {ev.EventType} and {artifactMentions} cross-artifact mention(s).");
        return Clamp(score, 0, 10);
    }

    private static int ScoreVisualBeauty(WeeklyAstronomyEvent ev, IReadOnlyList<string> objectCodes, int artifactMentions, List<string> signals)
    {
        var score = ev.EventType switch
        {
            WeeklyAstronomyEventType.Grouping => 14,
            WeeklyAstronomyEventType.Conjunction => 13,
            WeeklyAstronomyEventType.HeroObject => 11,
            WeeklyAstronomyEventType.DeepSkyHighlight => 13,
            WeeklyAstronomyEventType.BestViewingWindow => 10,
            _ => 9
        };
        if (objectCodes.Contains("MOON", StringComparer.OrdinalIgnoreCase)) score += 1;
        if (ev.RecommendedSceneType?.Contains("wide", StringComparison.OrdinalIgnoreCase) == true || ev.RecommendedSceneType?.Contains("group", StringComparison.OrdinalIgnoreCase) == true) score += 1;
        if (artifactMentions >= 3) score += 1;
        signals.Add($"Visual-beauty contribution {Clamp(score, 0, 15)}/15 from composition potential, scene type {ev.RecommendedSceneType ?? "unknown"}, and cross-artifact support.");
        return Clamp(score, 0, 15);
    }

    private static int ScoreViralPotential(WeeklyAstronomyEvent ev, IReadOnlyList<string> objectCodes, int rarityScore, int beautyScore, int artifactMentions, List<string> signals)
    {
        var publicInterest = objectCodes.Count(IsPublicInterestObject);
        var score = 4 + publicInterest * 2;
        if (ev.EventType is WeeklyAstronomyEventType.Conjunction or WeeklyAstronomyEventType.Grouping or WeeklyAstronomyEventType.RareEvent) score += 4;
        if (rarityScore >= 16) score += 2;
        if (beautyScore >= 13) score += 2;
        if (ev.Title.Contains("moon", StringComparison.OrdinalIgnoreCase) || ev.Title.Contains("venus", StringComparison.OrdinalIgnoreCase)) score += 1;
        if (artifactMentions >= 4) score += 1;
        signals.Add($"Viral-potential contribution {Clamp(score, 0, 15)}/15 from public interest, thumbnail attractiveness, audience appeal, and editorial artifact support.");
        return Clamp(score, 0, 15);
    }


    private static async Task<string> ReadArtifactTextAsync(WeeklyEventPriorityScoringInput input, CancellationToken cancellationToken)
    {
        var chunks = new List<string>();
        foreach (var path in BuildInputArtifacts(input).Where(File.Exists))
        {
            var info = new FileInfo(path);
            if (info.Length > 1_500_000) continue;
            chunks.Add(await File.ReadAllTextAsync(path, cancellationToken));
        }
        return string.Join("\n", chunks);
    }

    private static int CountArtifactMentions(WeeklyAstronomyEvent ev, IReadOnlyList<string> objectCodes, string artifactText)
    {
        if (string.IsNullOrWhiteSpace(artifactText)) return 0;
        var mentions = 0;
        if (!string.IsNullOrWhiteSpace(ev.Title) && artifactText.Contains(ev.Title, StringComparison.OrdinalIgnoreCase)) mentions++;
        if (!string.IsNullOrWhiteSpace(ev.PrimaryObject) && artifactText.Contains(ev.PrimaryObject, StringComparison.OrdinalIgnoreCase)) mentions++;
        mentions += objectCodes.Count(code => artifactText.Contains(code, StringComparison.OrdinalIgnoreCase));
        return mentions;
    }

    private static string ResolveEventCode(WeeklyAstronomyEvent ev)
    {
        if (!string.IsNullOrWhiteSpace(ev.EventId)) return ev.EventId;
        var objectPart = string.Join("-", ev.Objects.Select(x => x.ObjectCode).Where(x => !string.IsNullOrWhiteSpace(x)).Take(3));
        return $"{ev.EventType}-{ev.BestDateLocal:yyyyMMdd}-{objectPart}".Trim('-');
    }

    private static string BuildReason(WeeklyAstronomyEvent ev, int finalScore, string classification) =>
        $"{ev.Title} scored {finalScore}/100 as {classification} based on deterministic visibility, brightness, rarity, educational, visual beauty, and viral-potential signals.";

    private static IReadOnlyList<string> BuildInputArtifacts(WeeklyEventPriorityScoringInput input) =>
    [
        input.SkyfieldWeeklyResponsePath,
        input.WeeklyStoryBeatsPath,
        input.WeeklyScenesManifestPath,
        input.WeeklySegmentClassificationPlanPath,
        input.WeeklyVisualAssetPlanPath,
        input.WeeklyProductionAssetManifestPath,
        input.WeeklyNarrationVisualTimelinePath
    ];

    private static bool IsDarkTime(TimeOnly time) => time.Hour >= 18 || time.Hour <= 5;

    private static int EstimateMoonInterferenceScore(WeeklyAstronomyEvent ev)
    {
        var hasMoon = ev.Objects.Any(o => o.ObjectCode.Equals("MOON", StringComparison.OrdinalIgnoreCase));
        if (hasMoon && ev.EventType is WeeklyAstronomyEventType.DeepSkyHighlight or WeeklyAstronomyEventType.TelescopeOpportunity) return 1;
        if (hasMoon) return 3;
        return 2;
    }

    private static bool IsPublicInterestObject(string objectCode) => objectCode.ToUpperInvariant() is "MOON" or "VENUS" or "JUPITER" or "SATURN" or "MARS" or "MERCURY";

    private static string Classify(int finalScore) => finalScore switch
    {
        >= 90 => "Legendary",
        >= 75 => "Hero",
        >= 60 => "Major",
        >= 40 => "Supporting",
        _ => "Background"
    };

    private static double NarrationWeight(string classification) => classification switch
    {
        "Legendary" => 2.0,
        "Hero" => 1.8,
        "Major" => 1.3,
        "Supporting" => 1.0,
        _ => 0.5
    };

    private static double TimelineWeight(string classification) => classification switch
    {
        "Legendary" => 2.0,
        "Hero" => 1.7,
        "Major" => 1.3,
        "Supporting" => 1.0,
        _ => 0.5
    };

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
}
