namespace Astronomy.MediaFactory.Contracts;

public enum ContentType { DailySkyGuide = 1, TelescopeTargets = 2, SpaceNews = 3, AstrophotographyTips = 4, SpecialEventGuide = 5 }
public enum PipelineRunStatus { Queued = 1, Running = 2, Succeeded = 3, Failed = 4, PublishFailed = 5, CompletedWithPublishErrors = 6 }
public enum PipelineJobType { GenerateMainVideo = 1, GenerateShorts = 2, PublishVideo = 3, ArchiveAssets = 4 }
public enum PipelineJobStatus { Pending = 1, Running = 2, Succeeded = 3, Failed = 4, Retrying = 5, Stale = 6 }

public sealed record RunPipelineRequest(DateOnly Date, ContentType ContentType, string LocationName, string TimeZone = "Asia/Kolkata", bool PublishToYouTube = false, bool UseTopicPlanner = false, double? Latitude = null, double? Longitude = null, string? OverrideTimezone = null, string? OverrideLocationName = null, DateOnly? TargetDate = null, string? RegionId = null, string? EventId = null, string? EventType = null, string? EventTitle = null, string? EventDescription = null, string? Language = null);
public sealed record RunPipelineResponse(Guid PipelineRunId, PipelineRunStatus Status, string Message);
public sealed record RunPipelineExecutionResponse(Guid RunId, PipelineRunStatus Status, string GenerationStatus, string PublishStatus, IReadOnlyCollection<string> FailedStages, string ResumeCommand, string RetryPublishCommand, string Message);

public sealed record EnqueuePipelineJobRequest(
    PipelineJobType JobType,
    DateOnly RunDate,
    ContentType ContentType,
    string LocationName,
    string TimeZone = "Asia/Kolkata",
    bool PublishToYouTube = false,
    bool UseTopicPlanner = false,
    DateTimeOffset? ScheduledAt = null,
    Guid? ParentPipelineRunId = null,
    string? Language = null);

public sealed class OperationsOptions
{
    public const string SectionName = "Operations";
    public int RetainDays { get; set; } = 30;
    public int SlowStageThresholdMs { get; set; } = 10000;
    public bool EnableDetailedStageMetadata { get; set; } = true;
    public bool EnforceProductionValidation { get; set; } = true;
}

public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";
    public int WorkingFileRetentionDays { get; set; } = 14;
    public int JobRetentionDays { get; set; } = 30;
    public int StageRetentionDays { get; set; } = 30;
    public int AnalyticsRetentionDays { get; set; } = 90;
    public int StaleJobThresholdMinutes { get; set; } = 60;
    public string WorkingDirectory { get; set; } = "./media-output";
}

public sealed class AlertingOptions
{
    public const string SectionName = "Alerting";
    public bool Enabled { get; set; } = false;
    public bool NotifyOnStageFailed { get; set; } = true;
    public bool NotifyOnStageSlow { get; set; } = true;
    public bool NotifyOnPublishFailed { get; set; } = true;
    public bool NotifyOnPipelineFailed { get; set; } = true;
    public bool NotifyOnQueueBacklogHigh { get; set; } = true;
    public bool NotifyOnHealthDegraded { get; set; } = true;
    public bool NotifyOnPublishSucceeded { get; set; } = false;
    public int SlowStageThresholdMs { get; set; } = 10000;
    public int QueueBacklogThreshold { get; set; } = 25;
    public int DedupWindowSeconds { get; set; } = 120;
    public string? SlackWebhookUrl { get; set; }
}

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";
    public string? ApplicationInsightsConnectionString { get; set; }
    public bool EnableStructuredScopes { get; set; } = true;
}



public sealed class AstronomyEventsOptions
{
    private double _majorEventThreshold = 0.85;
    public const string SectionName = "AstronomyEvents";
    public bool Enabled { get; set; } = true;
    public int LookAheadDays { get; set; } = 30;
    public int RefreshEveryHours { get; set; } = 12;
    public double MediumEventThreshold { get; set; } = 0.70;
    public double MajorEventThreshold { get => _majorEventThreshold; set => _majorEventThreshold = value; }
    public double MinimumContentOpportunityScore { get; set; } = 0.65;
    public bool EnableDailyGuideEventInjection { get; set; } = true;
    public bool EnableSpecialEventVideos { get; set; } = true;
    public int MaxInjectedEventsPerDailyGuide { get; set; } = 1;
    public int MaxSpecialEventVideosPerDay { get; set; } = 2;
    public bool RunSpecialEventsBeforeDailyGuide { get; set; } = false;
    public double SpecialEventScoreThreshold { get => MajorEventThreshold; set => MajorEventThreshold = value; }
    public AstronomyEventSourceOptions Sources { get; set; } = new();
}

public sealed class AstronomyEventSourceOptions
{
    public bool MeteorShowers { get; set; } = true;
    public bool MoonPhases { get; set; } = true;
    public bool PlanetaryConjunctions { get; set; } = true;
    public bool Eclipses { get; set; } = true;
    public bool Comets { get; set; } = false;
    public bool IssPasses { get; set; } = false;
}

public sealed class TopicSelectionOptions
{
    public const string SectionName = "TopicSelection";
    public double TimelinessWeight { get; set; } = 0.25;
    public double ObservabilityWeight { get; set; } = 0.2;
    public double SignificanceWeight { get; set; } = 0.2;
    public double EducationalValueWeight { get; set; } = 0.15;
    public double GrowthPotentialWeight { get; set; } = 0.15;
    public double DiversityWeight { get; set; } = 0.05;
    public int RepetitionWindowDays { get; set; } = 5;
}




public sealed class ContentDurationOptions
{
    public const string SectionName = "ContentDuration";
    public int DailySkyGuideMinutes { get; set; } = 6;
    public int SpecialEventGuideMinutes { get; set; } = 8;
    public int YouTubeShortSeconds { get; set; } = 50;
    public int InstagramReelSeconds { get; set; } = 40;
    public int FacebookReelSeconds { get; set; } = 50;
    public bool AllowDynamicOptimization { get; set; } = true;
}

public sealed class ContentExpansionOptions
{
    public const string SectionName = "ContentExpansion";
    public bool Enabled { get; set; } = true;
    public int TargetDailyGuideMinutes { get; set; } = 6;
    public int TargetSpecialEventMinutes { get; set; } = 8;
    public int MinObjectsPerGuide { get; set; } = 3;
    public int MaxObjectsPerGuide { get; set; } = 6;
    public bool AllowMoonSegment { get; set; } = true;
    public bool AllowConstellations { get; set; } = true;
    public bool AllowBrightStars { get; set; } = true;
    public bool AllowDeepSkyObjects { get; set; } = true;
    public bool AllowObservationTips { get; set; } = true;
    public double MinimumVisibilityScore { get; set; } = 0.55;
}

public sealed class LocalizationOptions
{
    public const string SectionName = "Localization";
    public bool Enabled { get; set; } = true;
    public string DefaultLanguage { get; set; } = "en";
    public List<string> SupportedLanguages { get; set; } = ["en", "hi"];
    public string FallbackLanguage { get; set; } = "en";
}

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";
    public bool Enabled { get; set; } = false;
    public bool RunOnStartup { get; set; } = false;
    public int MaxConcurrentRuns { get; set; } = 1;
    public ContentType DefaultContentType { get; set; } = ContentType.DailySkyGuide;
    public List<SchedulerScheduleOptions> Schedules { get; set; } = [];
    public RegionSchedulingOptions Regions { get; set; } = new();
}

public sealed class SchedulerScheduleOptions
{
    public string? RegionId { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string LocationName { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Timezone { get; set; } = "Asia/Kolkata";
    public string LocalRunTime { get; set; } = "18:00";
    public bool PublishEnabled { get; set; } = true;
    public string Language { get; set; } = "en";
}


public sealed class RegionSchedulingOptions
{
    public bool Enabled { get; set; } = true;
    public List<string> DefaultPublishPlatforms { get; set; } = ["YouTube", "Facebook", "Instagram"];
    public List<RegionScheduleOptions> Items { get; set; } =
    [
        new RegionScheduleOptions
        {
            RegionId = "india-udaipur",
            DisplayName = "Udaipur, India",
            Latitude = 24.5854,
            Longitude = 73.7125,
            Timezone = "Asia/Kolkata",
            Language = "en",
            LocalRunTime = "18:00",
            Enabled = true
        },
        new RegionScheduleOptions
        {
            RegionId = "usa-new-york",
            DisplayName = "New York, USA",
            Latitude = 40.7128,
            Longitude = -74.0060,
            Timezone = "America/New_York",
            Language = "en",
            LocalRunTime = "18:00",
            Enabled = false
        },
        new RegionScheduleOptions
        {
            RegionId = "australia-sydney",
            DisplayName = "Sydney, Australia",
            Latitude = -33.8688,
            Longitude = 151.2093,
            Timezone = "Australia/Sydney",
            Language = "en",
            LocalRunTime = "18:00",
            Enabled = false
        }
    ];
}

public sealed class RegionScheduleOptions
{
    public string RegionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Timezone { get; set; } = "Asia/Kolkata";
    public string Language { get; set; } = "en";
    public string LocalRunTime { get; set; } = "18:00";
    public bool Enabled { get; set; } = true;
}

public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";
    public string DailySkyGuideCron { get; set; } = "0 0 18 * * ?";
    public string TelescopeTargetsCron { get; set; } = "0 0 19 * * ?";
    public string SpaceNewsCron { get; set; } = "0 0 20 * * ?";
    public string AstrophotographyTipsCron { get; set; } = "0 0 21 * * ?";
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryBackoffSeconds { get; set; } = 60;
    public int QueuePollIntervalSeconds { get; set; } = 10;
}


public sealed class OptimizationOptions
{
    public const string SectionName = "Optimization";
    public bool Enabled { get; set; } = true;
    public OptimizationMode Mode { get; set; } = OptimizationMode.RecommendOnly;
    public int MinimumDataPoints { get; set; } = 10;
    public bool ApplyToSchedulerRunsOnly { get; set; } = true;
    public bool AllowTitleOptimization { get; set; } = true;
    public bool AllowThumbnailOptimization { get; set; } = true;
    public bool AllowDurationOptimization { get; set; } = true;
    public bool AllowPublishTimeOptimization { get; set; } = true;
    public bool AllowObjectRankingOptimization { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = 0.6;
}

public enum OptimizationMode
{
    Disabled = 0,
    RecommendOnly = 1,
    ApplySafeRules = 2,
    ApplySafeRecommendations = 3
}


public sealed class AIOptimizationOptions
{
    public const string SectionName = "AIOptimization";
    public bool Enabled { get; set; } = true;
    public OptimizationMode Mode { get; set; } = OptimizationMode.RecommendOnly;
    public bool UseAzureOpenAI { get; set; } = true;
    public int MinimumAnalyticsRows { get; set; } = 20;
    public bool RequireHumanApproval { get; set; } = true;
    public double MinimumConfidenceToApply { get; set; } = 0.75;
    public IReadOnlyCollection<string> AllowedApplyFields { get; set; } =
    [
        "recommendedHooks",
        "recommendedThumbnailText",
        "recommendedHashtagSets",
        "recommendedPublishTimes",
        "recommendedObjectsToBoost"
    ];
    public string OutputFileName { get; set; } = "ai-optimization-recommendations.json";
}

public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";
    public bool Enabled { get; set; } = true;
    public int CollectEveryMinutes { get; set; } = 60;
    public int CollectForRecentDays { get; set; } = 14;
    public int FetchIntervalMinutes { get; set; } = 1440;
    public int TopN { get; set; } = 10;
    public double PerformanceScoreViewsWeight { get; set; } = 0.35;
    public double PerformanceScoreEngagementWeight { get; set; } = 0.25;
    public double PerformanceScoreWatchTimeWeight { get; set; } = 0.20;
    public double PerformanceScoreSharesWeight { get; set; } = 0.10;
    public double PerformanceScoreRetentionWeight { get; set; } = 0.10;
}


public enum VideoRenderProfileKind
{
    Auto = 0,
    IntermediateSegment = 1,
    YouTubeLongFinal = 2,
    ShortsFinal = 3,
    MetaReelFinal = 4
}

public sealed class RenderingOptions
{
    public const string SectionName = "Rendering";
    public const string VideoRenderSectionName = "VideoRender";
    public const string VideoEncodingSectionName = "VideoEncoding";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string? FfprobePath { get; set; }
    public string WorkingDirectory { get; set; } = "./media-output";
    public int VideoWidth { get; set; } = 1280;
    public int VideoHeight { get; set; } = 720;
    public int FrameRate { get; set; } = 30;
    public double ImageTransitionSeconds { get; set; } = 1;
    public string? BackgroundMusicPath { get; set; }
    public bool UseSegmentedNarration { get; set; } = false;
    public bool EnableTransitions { get; set; } = true;
    public bool EnableFadeInOut { get; set; } = true;
    public double FadeDurationSeconds { get; set; } = 0.75d;
    public double ShortFadeDurationSeconds { get; set; } = 0.4d;
    public double TransitionDurationSeconds { get; set; } = 0.5;
    public string TransitionType { get; set; } = "fade";
    public int FfmpegTimeoutSeconds { get; set; } = 600;
    public int FfmpegSegmentTimeoutSeconds { get; set; } = 120;
    public bool WriteSegmentDiagnostics { get; set; } = true;
    public bool KeepIntermediateFiles { get; set; } = true;
    public bool EnableKenBurns { get; set; } = true;
    public double KenBurnsZoomStart { get; set; } = 1.0d;
    public double KenBurnsZoomEnd { get; set; } = 1.10d;
    public double ShortKenBurnsZoomEnd { get; set; } = 1.08d;
    public int KenBurnsFps { get; set; } = 30;
    public bool KenBurnsUseEasing { get; set; } = true;
    public bool EnableDirectionalMotion { get; set; } = false;
    public double DirectionalPanStrength { get; set; } = 0.04d;
    public bool EnableYouTube1440pUpscale { get; set; } = true;
    public string IntermediatePreset { get; set; } = "fast";
    public int IntermediateCrf { get; set; } = 21;
    public string IntermediateScaleFlags { get; set; } = "bicubic";
    public string YouTubeLongPreset { get; set; } = "medium";
    public int YouTubeLongCrf { get; set; } = 18;
    public int YouTubeLongWidth { get; set; } = 2560;
    public int YouTubeLongHeight { get; set; } = 1440;
    public string ShortsPreset { get; set; } = "medium";
    public int ShortsCrf { get; set; } = 20;
    public string ShortsMaxRate { get; set; } = "12M";
    public string MetaReelPreset { get; set; } = "medium";
    public int MetaReelCrf { get; set; } = 21;
    public string MetaReelMaxRate { get; set; } = "10M";
    public OutputCleanupOptions OutputCleanup { get; set; } = new();
}

public sealed record VideoEncodingPreset(
    string Name,
    int Width,
    int Height,
    string Codec,
    string Preset,
    int Crf,
    string PixelFormat,
    string VideoBitrate,
    string MaxVideoBitrate,
    string BufferSize,
    string AudioBitrate,
    string ScaleFlags)
{
    public static VideoEncodingPreset IntermediateSegment(RenderingOptions options, int width, int height) => new(
        Name: "IntermediateSegment",
        Width: width,
        Height: height,
        Codec: "libx264",
        Preset: NormalizePreset(options.IntermediatePreset, "fast"),
        Crf: Math.Clamp(options.IntermediateCrf, 20, 22),
        PixelFormat: "yuv420p",
        VideoBitrate: string.Empty,
        MaxVideoBitrate: string.Empty,
        BufferSize: string.Empty,
        AudioBitrate: "192k",
        ScaleFlags: NormalizeScaleFlags(options.IntermediateScaleFlags, "bicubic"));

    public static VideoEncodingPreset YouTubeLongFinal(RenderingOptions options)
    {
        var enable1440pUpscale = options.EnableYouTube1440pUpscale;
        return new(
            Name: "YouTubeLongFinal",
            Width: enable1440pUpscale ? Math.Max(1, options.YouTubeLongWidth) : 1920,
            Height: enable1440pUpscale ? Math.Max(1, options.YouTubeLongHeight) : 1080,
            Codec: "libx264",
            Preset: NormalizePreset(options.YouTubeLongPreset, "medium"),
            Crf: options.YouTubeLongCrf > 0 ? options.YouTubeLongCrf : 18,
            PixelFormat: "yuv420p",
            VideoBitrate: enable1440pUpscale ? "20M" : "16M",
            MaxVideoBitrate: enable1440pUpscale ? "24M" : "20M",
            BufferSize: enable1440pUpscale ? "48M" : "40M",
            AudioBitrate: "320k",
            ScaleFlags: "lanczos");
    }

    public static VideoEncodingPreset ShortsFinal(RenderingOptions options) => new(
        Name: "ShortsFinal",
        Width: 1080,
        Height: 1920,
        Codec: "libx264",
        Preset: NormalizePreset(options.ShortsPreset, "medium"),
        Crf: options.ShortsCrf > 0 ? options.ShortsCrf : 20,
        PixelFormat: "yuv420p",
        VideoBitrate: "10M",
        MaxVideoBitrate: string.IsNullOrWhiteSpace(options.ShortsMaxRate) ? "12M" : options.ShortsMaxRate,
        BufferSize: "24M",
        AudioBitrate: "256k",
        ScaleFlags: "bicubic");

    public static VideoEncodingPreset MetaReelFinal(RenderingOptions options) => new(
        Name: "MetaReelFinal",
        Width: 1080,
        Height: 1920,
        Codec: "libx264",
        Preset: NormalizePreset(options.MetaReelPreset, "medium"),
        Crf: options.MetaReelCrf > 0 ? options.MetaReelCrf : 21,
        PixelFormat: "yuv420p",
        VideoBitrate: "8M",
        MaxVideoBitrate: string.IsNullOrWhiteSpace(options.MetaReelMaxRate) ? "10M" : options.MetaReelMaxRate,
        BufferSize: "20M",
        AudioBitrate: "256k",
        ScaleFlags: "bicubic");

    public static VideoEncodingPreset YouTubeLongProduction(bool enable1440pUpscale)
    {
        var options = new RenderingOptions { EnableYouTube1440pUpscale = enable1440pUpscale, YouTubeLongPreset = "slow" };
        return YouTubeLongFinal(options) with { Name = "YouTubeLongProduction" };
    }

    public static VideoEncodingPreset YouTubeShortProduction()
    {
        var options = new RenderingOptions { ShortsPreset = "slow", ShortsCrf = 18, ShortsMaxRate = "16M" };
        return ShortsFinal(options) with { Name = "YouTubeShortProduction", VideoBitrate = "12M", BufferSize = "32M" };
    }

    private static string NormalizePreset(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeScaleFlags(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public sealed class OutputCleanupOptions
{
    public bool CreateLegacySegmentFolders { get; set; } = false;
    public bool KeepDiagnostics { get; set; } = true;
}


public sealed class PublishingValidationOptions
{
    public const string SectionName = "PublishingValidation";
    public bool Enabled { get; set; } = true;
    public int MinimumLongVideoDurationSeconds { get; set; } = 60;
    public int MinimumShortVideoDurationSeconds { get; set; } = 15;
    public bool BlockPublishOnWarning { get; set; } = false;
}

public sealed class AstronomyApiOptions
{
    public const string SectionName = "AstronomyApis";
    public string NasaApiKey { get; set; } = "DEMO_KEY";
    public string NasaBaseUrl { get; set; } = "https://api.nasa.gov";
}

public sealed class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAI";
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChatDeployment { get; set; } = "";
    public bool UseManagedIdentity { get; set; }
    public string? ManagedIdentityClientId { get; set; }
}
