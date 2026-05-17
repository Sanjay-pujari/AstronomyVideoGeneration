using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Publishing;

internal static class ThumbnailPublishDiagnosticsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task WriteAsync(string outputDirectory, ThumbnailPublishDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var fileName = BuildFileName(diagnostics.Platform, diagnostics.ContentType);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
    }

    public static async Task WriteFromPublishResultAsync(string outputDirectory, PublishResult result, CancellationToken cancellationToken)
    {
        await WriteAsync(outputDirectory, new ThumbnailPublishDiagnostics
        {
            Platform = result.Platform,
            ContentType = string.IsNullOrWhiteSpace(result.ContentKind) ? result.ContentType : result.ContentKind!,
            VideoPath = result.VideoPathUsed,
            ThumbnailPath = result.ThumbnailPathUsed ?? result.UploadedThumbnailPath,
            ThumbnailSource = result.ThumbnailSource ?? result.ThumbnailStrategy ?? ThumbnailSources.None,
            UploadAttempted = result.ThumbnailUploadAttempted,
            UploadSuccess = result.ThumbnailUploadSuccess,
            UploadedVideoId = result.VideoId,
            PostId = result.PostId,
            Warning = result.ThumbnailWarning ?? result.Warning,
            Error = result.Error
        }, cancellationToken);
    }

    public static async Task WriteFromMetaResultAsync(string outputDirectory, MetaPublishResult result, CancellationToken cancellationToken)
    {
        await WriteAsync(outputDirectory, new ThumbnailPublishDiagnostics
        {
            Platform = result.Platform,
            ContentType = string.IsNullOrWhiteSpace(result.ContentKind) ? result.ContentType : result.ContentKind!,
            VideoPath = result.VideoPathUsed,
            ThumbnailPath = result.ThumbnailPathUsed ?? result.UploadedThumbnailPath,
            ThumbnailSource = result.ThumbnailSource ?? result.ThumbnailStrategy ?? ThumbnailSources.None,
            UploadAttempted = result.ThumbnailUploadAttempted,
            UploadSuccess = result.ThumbnailUploadSuccess,
            UploadedVideoId = result.VideoId,
            PostId = result.PostId,
            Warning = result.ThumbnailWarning ?? result.Warning,
            Error = result.Error
        }, cancellationToken);
    }

    private static string BuildFileName(string platform, string contentType)
    {
        var normalizedPlatform = Normalize(platform);
        var normalizedContentType = Normalize(contentType);
        return $"{normalizedPlatform}-{normalizedContentType}-thumbnail-diagnostics.json";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value.Trim()
            .Replace("YouTube", "youtube", StringComparison.OrdinalIgnoreCase)
            .Replace("Facebook", "facebook", StringComparison.OrdinalIgnoreCase)
            .Replace("Instagram", "instagram", StringComparison.OrdinalIgnoreCase)
            .Replace("LongVideo", "long-video", StringComparison.OrdinalIgnoreCase)
            .Replace("ShortVideo", "short", StringComparison.OrdinalIgnoreCase)
            .Replace("Reel", "reel", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "-", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
    }
}
