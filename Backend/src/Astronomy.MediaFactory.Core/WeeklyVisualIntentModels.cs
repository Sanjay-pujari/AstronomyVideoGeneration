using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

public interface IWeeklyVisualIntentEngine
{
    Task<WeeklyVisualIntentBuildResult> BuildAsync(Guid pipelineRunId, CancellationToken cancellationToken);
}

public sealed record WeeklyVisualIntentBuildResult(
    Guid PipelineRunId,
    string OutputDirectory,
    string VisualIntentPlanPath,
    string VisualIntentShotPlanPath,
    string ValidationReportPath,
    WeeklyVisualIntentValidationReport ValidationReport);

public sealed record WeeklyVisualIntentPlan
{
    public Guid PipelineRunId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string EngineVersion { get; init; } = "phase-6.6a";
    public IReadOnlyCollection<string> Inputs { get; init; } = [];
    public IReadOnlyCollection<WeeklyVisualIntentBeat> Beats { get; init; } = [];
    public WeeklyVisualIntentAssetMix TargetLongformMix { get; init; } = WeeklyVisualIntentAssetMix.LongformTarget;
    public WeeklyVisualIntentShortformRules ShortformRules { get; init; } = new();
}

public sealed record WeeklyVisualIntentShotPlan
{
    public Guid PipelineRunId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string EngineVersion { get; init; } = "phase-6.6a";
    public IReadOnlyCollection<WeeklyVisualIntentShot> Shots { get; init; } = [];
}

public sealed record WeeklyVisualIntentBeat
{
    public string BeatId { get; init; } = "";
    public string Form { get; init; } = "longform";
    public int Sequence { get; init; }
    public double? StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
    public double? DurationSeconds { get; init; }
    public string NarrationText { get; init; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WeeklyVisualIntentType VisualIntent { get; init; }
    public IReadOnlyCollection<string> MentionedObjects { get; init; } = [];
    public WeeklyVisualAssetUse Primary { get; init; } = new();
    public WeeklyVisualAssetUse? Secondary { get; init; }
    public WeeklyVisualAssetUse? Overlay { get; init; }
    public string EditorialRationale { get; init; } = "";
    public IReadOnlyCollection<WeeklyInternalCelestialAssetRequest> InternalCelestialRequests { get; init; } = [];
    public IReadOnlyCollection<string> Warnings { get; init; } = [];
}

public sealed record WeeklyVisualIntentShot
{
    public string ShotId { get; init; } = "";
    public string BeatId { get; init; } = "";
    public string Form { get; init; } = "longform";
    public int Sequence { get; init; }
    public double? StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
    public string VisualIntent { get; init; } = "";
    public WeeklyVisualAssetUse Primary { get; init; } = new();
    public WeeklyVisualAssetUse? Overlay { get; init; }
    public bool RendererShouldTreatMotionGraphicAsOverlay { get; init; }
    public bool RendererShouldTreatEducationalGraphicAsOverlay { get; init; }
    public string Notes { get; init; } = "";
}

public sealed record WeeklyVisualAssetUse
{
    public string AssetFamily { get; init; } = "requestedButUnavailable";
    public string? AssetSource { get; init; }
    public string? AssetId { get; init; }
    public string? Path { get; init; }
    public string? MatchedObject { get; init; }
    public string Role { get; init; } = "primary";
    public string? Placement { get; init; }
    public bool Fullscreen { get; init; }
    public double? MaxFullscreenSeconds { get; init; }
    public string Availability { get; init; } = "available";
}

public sealed record WeeklyInternalCelestialAssetRequest
{
    public string ObjectKey { get; init; } = "";
    public string Source { get; init; } = "InternalCelestial";
    public string Status { get; init; } = "requestedButUnavailable";
    public string Reason { get; init; } = "NASA/JWST/internal detail visual was missing or weak for the narration beat.";
}

public sealed record WeeklyVisualIntentValidationReport
{
    public bool VisualIntentReady { get; init; }
    public int TotalBeats { get; init; }
    public int MatchedBeatCount { get; init; }
    public int UnmatchedBeatCount { get; init; }
    public int NarrationVisualMismatchCount { get; init; }
    public int FullscreenMotionGraphicOveruseCount { get; init; }
    public int FullscreenEducationalOverlayCount { get; init; }
    public int SameFamilyConsecutiveMax { get; init; }
    public bool ShortformHookStrongVisualPassed { get; init; }
    public bool SaturnNarrationMatchedToSaturnVisual { get; init; }
    public bool VenusNarrationMatchedToVenusVisual { get; init; }
    public bool MoonNarrationMatchedToMoonVisual { get; init; }
    public IReadOnlyCollection<string> Warnings { get; init; } = [];
    public IReadOnlyCollection<string> Errors { get; init; } = [];
}

public sealed record WeeklyVisualIntentAssetMix
{
    public string Stellarium { get; init; } = "35-45%";
    public string AiCinematic { get; init; } = "10-20%";
    public string NasaJwstInternalCelestial { get; init; } = "15-25%";
    public string MotionGraphics { get; init; } = "10-15%";
    public string EducationalOverlay { get; init; } = "5-10%";

    public static WeeklyVisualIntentAssetMix LongformTarget { get; } = new();
}

public sealed record WeeklyVisualIntentShortformRules
{
    public string FirstThreeSeconds { get; init; } = "Use strongest object visual first.";
    public string DominantFamilies { get; init; } = "Stellarium/AI Cinematic";
    public string MotionGraphics { get; init; } = "Overlay only.";
    public string Cta { get; init; } = "End only, small CTA text overlay.";
}

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
