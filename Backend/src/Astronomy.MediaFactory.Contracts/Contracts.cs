namespace Astronomy.MediaFactory.Contracts;

public enum ContentType { DailySkyGuide = 1, TelescopeTargets = 2, SpaceNews = 3, AstrophotographyTips = 4 }
public enum PipelineRunStatus { Queued = 1, Running = 2, Succeeded = 3, Failed = 4 }
public enum PipelineJobType { GenerateMainVideo = 1, GenerateShorts = 2, PublishVideo = 3, ArchiveAssets = 4 }
public enum PipelineJobStatus { Pending = 1, Running = 2, Succeeded = 3, Failed = 4, Retrying = 5 }

public sealed record RunPipelineRequest(DateOnly Date, ContentType ContentType, string LocationName, string TimeZone = "Asia/Kolkata", bool PublishToYouTube = false);
public sealed record RunPipelineResponse(Guid PipelineRunId, PipelineRunStatus Status, string Message);

public sealed record EnqueuePipelineJobRequest(
    PipelineJobType JobType,
    DateOnly RunDate,
    ContentType ContentType,
    string LocationName,
    string TimeZone = "Asia/Kolkata",
    bool PublishToYouTube = false,
    DateTimeOffset? ScheduledAt = null,
    Guid? ParentPipelineRunId = null);

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
}
