namespace Astronomy.MediaFactory.Contracts;

public enum ContentType { DailySkyGuide = 1, TelescopeTargets = 2, SpaceNews = 3, AstrophotographyTips = 4 }
public enum PipelineRunStatus { Queued = 1, Running = 2, Succeeded = 3, Failed = 4 }

public sealed record RunPipelineRequest(DateOnly Date, ContentType ContentType, string LocationName, string TimeZone = "Asia/Kolkata", bool PublishToYouTube = false);
public sealed record RunPipelineResponse(Guid PipelineRunId, PipelineRunStatus Status, string Message);

public sealed class RenderingOptions
{
    public const string SectionName = "Rendering";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string WorkingDirectory { get; set; } = "./media-output";
    public int VideoWidth { get; set; } = 1920;
    public int VideoHeight { get; set; } = 1080;
    public int FrameRate { get; set; } = 30;
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

public sealed class AzureStorageOptions
{
    public const string SectionName = "AzureStorage";
    public string ConnectionString { get; set; } = "";
    public string ContainerName { get; set; } = "astronomy-media";
}

public sealed class YouTubeOptions
{
    public const string SectionName = "YouTube";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string PrivacyStatus { get; set; } = "private";
}
