namespace Astronomy.MediaFactory.Contracts;

public enum ContentType { DailySkyGuide = 1, TelescopeTargets = 2, SpaceNews = 3, AstrophotographyTips = 4 }
public enum PipelineRunStatus { Queued = 1, Running = 2, Succeeded = 3, Failed = 4 }
public enum PipelineJobType { GenerateMainVideo = 1, GenerateShorts = 2, PublishVideo = 3, ArchiveAssets = 4 }
public enum PipelineJobStatus { Pending = 1, Running = 2, Succeeded = 3, Failed = 4, Retrying = 5 }

public sealed record RunPipelineRequest(DateOnly Date, ContentType ContentType, string LocationName, string TimeZone = "Asia/Kolkata", bool PublishToYouTube = false, bool UseTopicPlanner = false);
public sealed record RunPipelineResponse(Guid PipelineRunId, PipelineRunStatus Status, string Message);

public sealed record EnqueuePipelineJobRequest(
    PipelineJobType JobType,
    DateOnly RunDate,
    ContentType ContentType,
    string LocationName,
    string TimeZone = "Asia/Kolkata",
    bool PublishToYouTube = false,
    bool UseTopicPlanner = false,
    DateTimeOffset? ScheduledAt = null,
    Guid? ParentPipelineRunId = null);

public sealed class OperationsOptions
{
    public const string SectionName = "Operations";
    public int RetainDays { get; set; } = 30;
    public int SlowStageThresholdMs { get; set; } = 10000;
    public bool EnableDetailedStageMetadata { get; set; } = true;
    public bool EnforceProductionValidation { get; set; } = true;
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

public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";
    public int FetchIntervalMinutes { get; set; } = 1440;
    public int TopN { get; set; } = 10;
}

public sealed class RenderingOptions
{
    public const string SectionName = "Rendering";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string WorkingDirectory { get; set; } = "./media-output";
    public int VideoWidth { get; set; } = 1280;
    public int VideoHeight { get; set; } = 720;
    public int FrameRate { get; set; } = 30;
    public double ImageTransitionSeconds { get; set; } = 1;
    public string? BackgroundMusicPath { get; set; }
}

public sealed class AstronomyApiOptions
{
    public const string SectionName = "AstronomyApis";
    public string NasaApiKey { get; set; } = "DEMO_KEY";
    public string NasaBaseUrl { get; set; } = "https://api.nasa.gov";
    public string MpcBaseUrl { get; set; } = "https://www.minorplanetcenter.net";
    public string SkyfieldServiceUrl { get; set; } = "http://localhost:8099";
    public string StellariumScriptsDirectory { get; set; } = "./stellarium-scripts";
}

public sealed class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAI";
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChatDeployment { get; set; } = "";
    public bool UseManagedIdentity { get; set; }
}
