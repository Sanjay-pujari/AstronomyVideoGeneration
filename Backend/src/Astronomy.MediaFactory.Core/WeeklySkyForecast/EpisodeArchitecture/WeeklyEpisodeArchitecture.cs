using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.EpisodeArchitecture;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeeklyEpisodeType
{
    LongFormWeeklyForecast,
    ShortFormWeeklyHighlight
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeeklyEpisodeVisualSource
{
    Stellarium,
    AICinematic,
    NASA,
    JWST,
    MotionGraphics,
    Hybrid
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeeklyEpisodeNarrationStyle
{
    Documentary,
    ObservationGuide,
    Educational,
    EmotionalWonder,
    ShortHook,
    CallToAction
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeeklyEpisodePacingRole
{
    Hook,
    BuildContext,
    HeroMoment,
    EducationalBreath,
    PracticalGuidance,
    EmotionalReset,
    Closing
}

public sealed record WeeklyEpisodeSegment(
    string SegmentId,
    string SegmentType,
    string Title,
    string Purpose,
    int Priority,
    int TargetDurationSeconds,
    int MinDurationSeconds,
    int MaxDurationSeconds,
    WeeklyEpisodeVisualSource VisualSourcePreference,
    WeeklyEpisodeNarrationStyle NarrationStyle,
    string EmotionalTone,
    WeeklyEpisodePacingRole PacingRole,
    IReadOnlyList<string> RequiredAssets,
    string ProductionStatus);

public sealed record WeeklyEpisodeVisualSourceRatioTarget(
    string VisualSource,
    int MinPercent,
    int MaxPercent);

public sealed record WeeklyEpisodePlan(
    Guid PipelineRunId,
    string RegionId,
    string Language,
    DateOnly WeekStartDate,
    WeeklyEpisodeType EpisodeType,
    int TotalTargetDurationSeconds,
    IReadOnlyList<WeeklyEpisodeSegment> Segments,
    IReadOnlyList<WeeklyEpisodeVisualSourceRatioTarget> VisualSourceRatioTargets,
    string ProductionReadinessStatus);

public sealed record WeeklyEpisodeArchitectureResult(
    WeeklyEpisodePlan MainProductionPlan,
    WeeklyEpisodePlan LongFormPlan,
    WeeklyEpisodePlan ShortFormPlan,
    string WeeklyEpisodePlanPath,
    string WeeklyLongformPlanPath,
    string WeeklyShortformPlanPath,
    bool EpisodeArchitectureReady);

public sealed class WeeklyEpisodeStructurePolicy
{
    public IReadOnlyList<string> GetSegmentTypes(WeeklyEpisodeType episodeType) => episodeType switch
    {
        WeeklyEpisodeType.LongFormWeeklyForecast =>
        [
            "OpeningHook",
            "WeeklySkyOverview",
            "HeroEvent",
            "MoonHighlights",
            "PlanetHighlights",
            "BestObservationWindow",
            "AstrophotographyTip",
            "WeeklySummary"
        ],
        WeeklyEpisodeType.ShortFormWeeklyHighlight =>
        [
            "ShortHook",
            "StrongestEvent",
            "WhereToLook",
            "BestTime",
            "CallToAction"
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(episodeType), episodeType, null)
    };

    public (string Title, string Purpose, string EmotionalTone, IReadOnlyList<string> RequiredAssets) GetSegmentIntent(string segmentType) => segmentType switch
    {
        "OpeningHook" => ("This Week's Sky in One Promise", "Open with a cinematic promise that frames why this week is worth looking up.", "Wonder and anticipation", ["hero sky concept", "week theme"]),
        "WeeklySkyOverview" => ("The Week at a Glance", "Establish the overall observing storyline, weather-independent sky rhythm, and major celestial themes.", "Calm orientation", ["weekly sky map", "constellation context"]),
        "HeroEvent" => ("The Main Event", "Give the strongest weekly event the most detailed observing and visual treatment.", "Awe and focus", ["primary event target", "best viewing time", "stellarium framing"]),
        "MoonHighlights" => ("Moon Highlights", "Explain the Moon's phase, best nights, and its role in the week's skywatching story.", "Reflective clarity", ["moon phase", "best moon night"]),
        "PlanetHighlights" => ("Planet Highlights", "Guide viewers through the most visible planets and their best observing windows.", "Discovery and confidence", ["visible planets", "planet labels"]),
        "BestObservationWindow" => ("Best Observation Window", "Translate the week into practical when-to-watch guidance.", "Useful confidence", ["date window", "time window", "viewing conditions"]),
        "AstrophotographyTip" => ("Astrophotography Tip", "Offer one actionable imaging idea tied to the week's sky conditions.", "Encouraging expertise", ["camera tip", "target suggestion"]),
        "WeeklySummary" => ("Your Weekly Sky Checklist", "Close with a concise recap and an emotionally satisfying reason to observe.", "Warm closure", ["summary checklist", "closing visual"]),
        "ShortHook" => ("Don't Miss This Sky Moment", "Capture attention immediately with the strongest weekly reason to watch.", "Immediate intrigue", ["strongest visual hook"]),
        "StrongestEvent" => ("The Highlight", "Present the single strongest event in a fast, clear format.", "Focused excitement", ["primary event target", "stellarium framing"]),
        "WhereToLook" => ("Where to Look", "Give directional guidance that can be understood quickly.", "Practical clarity", ["direction graphic", "horizon cue"]),
        "BestTime" => ("Best Time", "Name the best observing window without expanding into full narration.", "Urgent usefulness", ["time card", "date card"]),
        "CallToAction" => ("Look Up This Week", "End with a brief reminder to observe and follow for the next forecast.", "Upbeat close", ["closing title card"]),
        _ => throw new ArgumentOutOfRangeException(nameof(segmentType), segmentType, null)
    };
}

public sealed class WeeklyEpisodeDurationPolicy
{
    public int GetTargetDurationSeconds(string segmentType) => segmentType switch
    {
        "OpeningHook" => 20,
        "WeeklySkyOverview" => 40,
        "HeroEvent" => 70,
        "MoonHighlights" => 70,
        "PlanetHighlights" => 70,
        "BestObservationWindow" => 40,
        "AstrophotographyTip" => 40,
        "WeeklySummary" => 30,
        "ShortHook" => 5,
        "StrongestEvent" => 20,
        "WhereToLook" => 10,
        "BestTime" => 10,
        "CallToAction" => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(segmentType), segmentType, null)
    };

    public (int MinDurationSeconds, int MaxDurationSeconds) GetDurationBounds(int targetDurationSeconds)
    {
        var min = Math.Max(1, (int)Math.Floor(targetDurationSeconds * 0.8));
        var max = Math.Max(targetDurationSeconds, (int)Math.Ceiling(targetDurationSeconds * 1.2));
        return (min, max);
    }
}

public sealed class WeeklyEpisodeVisualPolicy
{
    public WeeklyEpisodeVisualSource GetVisualSourcePreference(string segmentType) => segmentType switch
    {
        "OpeningHook" => WeeklyEpisodeVisualSource.AICinematic,
        "WeeklySkyOverview" => WeeklyEpisodeVisualSource.Hybrid,
        "HeroEvent" => WeeklyEpisodeVisualSource.Stellarium,
        "MoonHighlights" => WeeklyEpisodeVisualSource.Stellarium,
        "PlanetHighlights" => WeeklyEpisodeVisualSource.Stellarium,
        "BestObservationWindow" => WeeklyEpisodeVisualSource.MotionGraphics,
        "AstrophotographyTip" => WeeklyEpisodeVisualSource.Hybrid,
        "WeeklySummary" => WeeklyEpisodeVisualSource.AICinematic,
        "ShortHook" => WeeklyEpisodeVisualSource.Stellarium,
        "StrongestEvent" => WeeklyEpisodeVisualSource.Stellarium,
        "WhereToLook" => WeeklyEpisodeVisualSource.MotionGraphics,
        "BestTime" => WeeklyEpisodeVisualSource.MotionGraphics,
        "CallToAction" => WeeklyEpisodeVisualSource.AICinematic,
        _ => throw new ArgumentOutOfRangeException(nameof(segmentType), segmentType, null)
    };

    public IReadOnlyList<WeeklyEpisodeVisualSourceRatioTarget> GetRatioTargets(WeeklyEpisodeType episodeType) => episodeType switch
    {
        WeeklyEpisodeType.LongFormWeeklyForecast =>
        [
            new("Stellarium", 45, 60),
            new("AICinematic", 15, 25),
            new("NASA/JWST", 10, 15),
            new("MotionGraphics", 10, 15)
        ],
        WeeklyEpisodeType.ShortFormWeeklyHighlight =>
        [
            new("Stellarium", 40, 60),
            new("MotionGraphics", 20, 40),
            new("AICinematic", 10, 25)
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(episodeType), episodeType, null)
    };
}

public sealed class WeeklyEpisodeNarrationPolicy
{
    public WeeklyEpisodeNarrationStyle GetNarrationStyle(string segmentType) => segmentType switch
    {
        "OpeningHook" => WeeklyEpisodeNarrationStyle.EmotionalWonder,
        "WeeklySkyOverview" => WeeklyEpisodeNarrationStyle.Documentary,
        "HeroEvent" => WeeklyEpisodeNarrationStyle.ObservationGuide,
        "MoonHighlights" => WeeklyEpisodeNarrationStyle.Educational,
        "PlanetHighlights" => WeeklyEpisodeNarrationStyle.ObservationGuide,
        "BestObservationWindow" => WeeklyEpisodeNarrationStyle.ObservationGuide,
        "AstrophotographyTip" => WeeklyEpisodeNarrationStyle.Educational,
        "WeeklySummary" => WeeklyEpisodeNarrationStyle.EmotionalWonder,
        "ShortHook" => WeeklyEpisodeNarrationStyle.ShortHook,
        "StrongestEvent" => WeeklyEpisodeNarrationStyle.ObservationGuide,
        "WhereToLook" => WeeklyEpisodeNarrationStyle.ObservationGuide,
        "BestTime" => WeeklyEpisodeNarrationStyle.ObservationGuide,
        "CallToAction" => WeeklyEpisodeNarrationStyle.CallToAction,
        _ => throw new ArgumentOutOfRangeException(nameof(segmentType), segmentType, null)
    };

    public WeeklyEpisodePacingRole GetPacingRole(string segmentType) => segmentType switch
    {
        "OpeningHook" => WeeklyEpisodePacingRole.Hook,
        "WeeklySkyOverview" => WeeklyEpisodePacingRole.BuildContext,
        "HeroEvent" => WeeklyEpisodePacingRole.HeroMoment,
        "MoonHighlights" => WeeklyEpisodePacingRole.EducationalBreath,
        "PlanetHighlights" => WeeklyEpisodePacingRole.BuildContext,
        "BestObservationWindow" => WeeklyEpisodePacingRole.PracticalGuidance,
        "AstrophotographyTip" => WeeklyEpisodePacingRole.EducationalBreath,
        "WeeklySummary" => WeeklyEpisodePacingRole.Closing,
        "ShortHook" => WeeklyEpisodePacingRole.Hook,
        "StrongestEvent" => WeeklyEpisodePacingRole.HeroMoment,
        "WhereToLook" => WeeklyEpisodePacingRole.PracticalGuidance,
        "BestTime" => WeeklyEpisodePacingRole.PracticalGuidance,
        "CallToAction" => WeeklyEpisodePacingRole.Closing,
        _ => throw new ArgumentOutOfRangeException(nameof(segmentType), segmentType, null)
    };
}

public sealed class WeeklyEpisodePlanPersister
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<string> WriteAsync(WeeklyEpisodePlan plan, string outputPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        return outputPath;
    }
}

public sealed class WeeklyEpisodeArchitectureService(
    WeeklyEpisodeStructurePolicy structurePolicy,
    WeeklyEpisodeDurationPolicy durationPolicy,
    WeeklyEpisodeVisualPolicy visualPolicy,
    WeeklyEpisodeNarrationPolicy narrationPolicy,
    WeeklyEpisodePlanPersister persister,
    ILogger<WeeklyEpisodeArchitectureService> logger)
{
    public async Task<WeeklyEpisodeArchitectureResult> BuildAndPersistAsync(
        Guid pipelineRunId,
        string regionId,
        string language,
        DateOnly weekStartDate,
        string workingDirectoryRoot,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "WEEKLY_EPISODE_ARCHITECTURE_START pipelineRunId={PipelineRunId} regionId={RegionId} language={Language} weekStartDate={WeekStartDate}",
            pipelineRunId,
            regionId,
            language,
            weekStartDate);

        var longFormPlan = BuildPlan(pipelineRunId, regionId, language, weekStartDate, WeeklyEpisodeType.LongFormWeeklyForecast);
        var shortFormPlan = BuildPlan(pipelineRunId, regionId, language, weekStartDate, WeeklyEpisodeType.ShortFormWeeklyHighlight);

        ValidatePlan(longFormPlan, expectedSegments: 8, expectedDurationSeconds: 380);
        ValidatePlan(shortFormPlan, expectedSegments: 5, expectedDurationSeconds: 50);

        var episodeDirectory = Path.Combine(workingDirectoryRoot, "episode");
        var weeklyEpisodePlanPath = Path.Combine(episodeDirectory, "weekly-episode-plan.json");
        var weeklyLongformPlanPath = Path.Combine(episodeDirectory, "weekly-longform-plan.json");
        var weeklyShortformPlanPath = Path.Combine(episodeDirectory, "weekly-shortform-plan.json");

        await persister.WriteAsync(longFormPlan, weeklyEpisodePlanPath, cancellationToken);
        logger.LogInformation("WEEKLY_EPISODE_PLAN_WRITTEN episodeType={EpisodeType} path={Path}", longFormPlan.EpisodeType, weeklyEpisodePlanPath);
        await persister.WriteAsync(longFormPlan, weeklyLongformPlanPath, cancellationToken);
        logger.LogInformation("WEEKLY_EPISODE_PLAN_WRITTEN episodeType={EpisodeType} path={Path}", longFormPlan.EpisodeType, weeklyLongformPlanPath);
        await persister.WriteAsync(shortFormPlan, weeklyShortformPlanPath, cancellationToken);
        logger.LogInformation("WEEKLY_EPISODE_PLAN_WRITTEN episodeType={EpisodeType} path={Path}", shortFormPlan.EpisodeType, weeklyShortformPlanPath);

        logger.LogInformation(
            "WEEKLY_EPISODE_ARCHITECTURE_COMPLETE pipelineRunId={PipelineRunId} longformTargetDurationSeconds={LongformTargetDurationSeconds} shortformTargetDurationSeconds={ShortformTargetDurationSeconds}",
            pipelineRunId,
            longFormPlan.TotalTargetDurationSeconds,
            shortFormPlan.TotalTargetDurationSeconds);

        return new WeeklyEpisodeArchitectureResult(
            longFormPlan,
            longFormPlan,
            shortFormPlan,
            weeklyEpisodePlanPath,
            weeklyLongformPlanPath,
            weeklyShortformPlanPath,
            EpisodeArchitectureReady: true);
    }

    private WeeklyEpisodePlan BuildPlan(Guid pipelineRunId, string regionId, string language, DateOnly weekStartDate, WeeklyEpisodeType episodeType)
    {
        var segments = structurePolicy.GetSegmentTypes(episodeType)
            .Select((segmentType, index) => BuildSegment(episodeType, segmentType, index + 1))
            .ToList();

        return new WeeklyEpisodePlan(
            pipelineRunId,
            regionId,
            language,
            weekStartDate,
            episodeType,
            segments.Sum(segment => segment.TargetDurationSeconds),
            segments,
            visualPolicy.GetRatioTargets(episodeType),
            ProductionReadinessStatus: "EpisodeArchitectureReady");
    }

    private WeeklyEpisodeSegment BuildSegment(WeeklyEpisodeType episodeType, string segmentType, int priority)
    {
        var targetDuration = durationPolicy.GetTargetDurationSeconds(segmentType);
        var durationBounds = durationPolicy.GetDurationBounds(targetDuration);
        var visualSourcePreference = visualPolicy.GetVisualSourcePreference(segmentType);
        var narrationStyle = narrationPolicy.GetNarrationStyle(segmentType);
        var pacingRole = narrationPolicy.GetPacingRole(segmentType);
        var intent = structurePolicy.GetSegmentIntent(segmentType);
        var segment = new WeeklyEpisodeSegment(
            SegmentId: $"{episodeType.ToString().ToLowerInvariant()}-{priority:00}-{ToKebabCase(segmentType)}",
            SegmentType: segmentType,
            Title: intent.Title,
            Purpose: intent.Purpose,
            Priority: priority,
            TargetDurationSeconds: targetDuration,
            MinDurationSeconds: durationBounds.MinDurationSeconds,
            MaxDurationSeconds: durationBounds.MaxDurationSeconds,
            VisualSourcePreference: visualSourcePreference,
            NarrationStyle: narrationStyle,
            EmotionalTone: intent.EmotionalTone,
            PacingRole: pacingRole,
            RequiredAssets: intent.RequiredAssets,
            ProductionStatus: "Planned");

        logger.LogInformation(
            "WEEKLY_EPISODE_SEGMENT_CREATED episodeType={EpisodeType} segmentType={SegmentType} targetDurationSeconds={TargetDurationSeconds} visualSourcePreference={VisualSourcePreference} narrationStyle={NarrationStyle} pacingRole={PacingRole}",
            episodeType,
            segment.SegmentType,
            segment.TargetDurationSeconds,
            segment.VisualSourcePreference,
            segment.NarrationStyle,
            segment.PacingRole);

        return segment;
    }

    private static void ValidatePlan(WeeklyEpisodePlan plan, int expectedSegments, int expectedDurationSeconds)
    {
        if (plan.Segments.Count != expectedSegments)
            throw new InvalidOperationException($"{plan.EpisodeType} episode architecture validation failed: expected {expectedSegments} segments but found {plan.Segments.Count}.");
        if (plan.TotalTargetDurationSeconds != expectedDurationSeconds)
            throw new InvalidOperationException($"{plan.EpisodeType} episode architecture validation failed: expected {expectedDurationSeconds} seconds but found {plan.TotalTargetDurationSeconds} seconds.");
        if (plan.Segments.Any(segment => segment.VisualSourcePreference.ToString().Length == 0))
            throw new InvalidOperationException($"{plan.EpisodeType} episode architecture validation failed: visual source preferences are not fully assigned.");
        if (plan.Segments.Any(segment => segment.NarrationStyle.ToString().Length == 0))
            throw new InvalidOperationException($"{plan.EpisodeType} episode architecture validation failed: narration styles are not fully assigned.");
        if (plan.Segments.Any(segment => segment.PacingRole.ToString().Length == 0))
            throw new InvalidOperationException($"{plan.EpisodeType} episode architecture validation failed: pacing roles are not fully assigned.");
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var characters = new List<char>(value.Length * 2);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current) && i > 0)
                characters.Add('-');
            characters.Add(char.ToLowerInvariant(current));
        }

        return new string(characters.ToArray());
    }
}
