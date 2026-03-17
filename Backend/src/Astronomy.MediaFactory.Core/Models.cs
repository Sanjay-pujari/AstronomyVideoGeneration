using Astronomy.MediaFactory.Contracts;
namespace Astronomy.MediaFactory.Core;

public sealed class AstronomyContext
{
    public DateOnly Date { get; init; }
    public string LocationName { get; init; } = "";
    public string TimeZone { get; init; } = "Asia/Kolkata";
    public List<AstronomyEventModel> Events { get; init; } = new();
    public List<NewsItemModel> NewsItems { get; init; } = new();
    public List<VisualIdeaModel> VisualIdeas { get; init; } = new();
}
public sealed class AstronomyEventModel
{
    public string Category { get; init; } = "";
    public string ObjectName { get; init; } = "";
    public string VisibilityWindow { get; init; } = "";
    public string Direction { get; init; } = "";
    public string ObservationTool { get; init; } = "";
    public string Details { get; init; } = "";
    public double Score { get; init; }
}
public sealed class NewsItemModel
{
    public string Headline { get; init; } = "";
    public string Summary { get; init; } = "";
    public string SourceName { get; init; } = "";
    public DateOnly PublishedDate { get; init; }
    public string? SourceUrl { get; init; }
}
public sealed class VisualIdeaModel
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? SourcePathOrUrl { get; init; }
}
public sealed class ScriptResult
{
    public string Prompt { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string ScriptBody { get; init; } = "";
    public string[] Tags { get; init; } = Array.Empty<string>();
    public int EstimatedDurationSeconds { get; init; }
}
public sealed class RenderManifest
{
    public string Title { get; set; } = "";
    public string AudioPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string? IntroVisualPath { get; set; }
    public string? OutroVisualPath { get; set; }
    public List<RenderScene> Scenes { get; set; } = new();
}
public sealed class RenderScene
{
    public string Caption { get; set; } = "";
    public string VisualPath { get; set; } = "";
    public int DurationSeconds { get; set; }
}
public sealed class RankedTopic
{
    public ContentType ContentType { get; init; }
    public string TopicTitle { get; init; } = "";
    public string Summary { get; init; } = "";
    public double Score { get; init; }
}

public sealed class BlobUploadRequest
{
    public required string BasePath { get; init; }
    public required string VideoPath { get; init; }
    public required string AudioPath { get; init; }
    public string? ThumbnailPath { get; init; }
}

public sealed class BlobUploadResult
{
    public string? VideoUrl { get; init; }
    public string? AudioUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
}
