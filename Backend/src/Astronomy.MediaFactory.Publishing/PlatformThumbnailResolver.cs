using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Publishing;

public sealed class PlatformThumbnailResolver : IPlatformThumbnailResolver
{
    private readonly ILogger<PlatformThumbnailResolver> _logger;

    public PlatformThumbnailResolver(ILogger<PlatformThumbnailResolver> logger)
    {
        _logger = logger;
    }

    public async Task<PlatformThumbnailResolution> ResolveAsync(
        string outputDirectory,
        string platform,
        string contentType,
        CancellationToken cancellationToken)
    {
        var normalizedContentType = NormalizeContentType(contentType);
        var longThumbnailPath = Path.Combine(outputDirectory, "thumbnails", "thumbnail-long.jpg");
        var shortThumbnailPath = Path.Combine(outputDirectory, "thumbnails", "thumbnail-short.jpg");
        var canonicalPath = normalizedContentType == PlatformThumbnailContentTypes.LongVideo
            ? longThumbnailPath
            : shortThumbnailPath;

        var canonicalValidation = await ValidateImageAsync(canonicalPath, platform, cancellationToken);
        if (canonicalValidation.IsValid)
        {
            return new PlatformThumbnailResolution
            {
                Platform = platform,
                ContentType = normalizedContentType,
                LongThumbnailPath = longThumbnailPath,
                ShortThumbnailPath = shortThumbnailPath,
                PlatformThumbnailPath = canonicalPath,
                ThumbnailSource = ThumbnailSources.GeneratedThumbnail,
                IsValid = true,
                Width = canonicalValidation.Width,
                Height = canonicalValidation.Height,
                FileSizeBytes = canonicalValidation.FileSizeBytes
            };
        }

        if (string.Equals(platform, "YouTube", StringComparison.OrdinalIgnoreCase)
            && normalizedContentType == PlatformThumbnailContentTypes.ShortVideo)
        {
            _logger.LogWarning(
                "Generated thumbnail {ThumbnailPath} for YouTube Shorts is missing or invalid: {Reason}. No fallback thumbnail will be used for YouTube Shorts.",
                canonicalPath,
                canonicalValidation.Warning);

            return new PlatformThumbnailResolution
            {
                Platform = platform,
                ContentType = normalizedContentType,
                LongThumbnailPath = longThumbnailPath,
                ShortThumbnailPath = shortThumbnailPath,
                PlatformThumbnailPath = canonicalPath,
                ThumbnailSource = ThumbnailSources.GeneratedThumbnail,
                IsValid = false,
                Width = canonicalValidation.Width,
                Height = canonicalValidation.Height,
                FileSizeBytes = canonicalValidation.FileSizeBytes,
                Warning = canonicalValidation.Warning
            };
        }

        _logger.LogWarning(
            "Generated thumbnail {ThumbnailPath} for {Platform} {ContentType} is missing or invalid: {Reason}. Falling back to legacy thumbnail selection.",
            canonicalPath,
            platform,
            normalizedContentType,
            canonicalValidation.Warning);

        var fallback = await ResolveFallbackAsync(outputDirectory, normalizedContentType, cancellationToken);
        return new PlatformThumbnailResolution
        {
            Platform = platform,
            ContentType = normalizedContentType,
            LongThumbnailPath = longThumbnailPath,
            ShortThumbnailPath = shortThumbnailPath,
            PlatformThumbnailPath = fallback.Path,
            ThumbnailSource = string.IsNullOrWhiteSpace(fallback.Path) ? ThumbnailSources.None : ThumbnailSources.FallbackThumbnail,
            IsValid = fallback.Validation.IsValid,
            Width = fallback.Validation.Width,
            Height = fallback.Validation.Height,
            FileSizeBytes = fallback.Validation.FileSizeBytes,
            Warning = canonicalValidation.Warning
        };
    }

    private async Task<(string Path, ThumbnailImageValidation Validation)> ResolveFallbackAsync(string outputDirectory, string contentType, CancellationToken cancellationToken)
    {
        foreach (var candidate in GetFallbackCandidates(outputDirectory, contentType).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var validation = await ValidateImageAsync(candidate, platform: null, cancellationToken);
            if (validation.IsValid)
            {
                return (candidate, validation);
            }
        }

        return (string.Empty, new ThumbnailImageValidation(false, "No valid fallback thumbnail was found.", null, null, null));
    }

    private static IEnumerable<string> GetFallbackCandidates(string outputDirectory, string contentType)
    {
        var shortsDirectory = Path.Combine(outputDirectory, "shorts");

        if (contentType == PlatformThumbnailContentTypes.LongVideo)
        {
            yield return Path.Combine(outputDirectory, "thumbnail-long.jpg");
            yield return Path.Combine(outputDirectory, "thumbnail-1.png");
            yield return Path.Combine(outputDirectory, "thumbnail-1.jpg");
            yield return Path.Combine(outputDirectory, "thumbnail-1.jpeg");
            yield return Path.Combine(outputDirectory, "thumbnails", "thumbnail-1.png");
            yield return Path.Combine(outputDirectory, "thumbnails", "thumbnail-1.jpg");
            yield return Path.Combine(outputDirectory, "thumbnails", "thumbnail-1.jpeg");
            yield break;
        }

        yield return Path.Combine(shortsDirectory, "thumbnail-short.jpg");
        yield return Path.Combine(shortsDirectory, "thumbnails", "thumbnail-short.jpg");
        yield return Path.Combine(shortsDirectory, "thumbnail-1.png");
        yield return Path.Combine(shortsDirectory, "thumbnail-1.jpg");
        yield return Path.Combine(shortsDirectory, "thumbnail-1.jpeg");
        yield return Path.Combine(shortsDirectory, "short-cover-1.png");
        yield return Path.Combine(shortsDirectory, "short-cover-1.jpg");
        yield return Path.Combine(shortsDirectory, "thumbnails", "thumbnail-1.png");
        yield return Path.Combine(shortsDirectory, "thumbnails", "thumbnail-1.jpg");
        yield return Path.Combine(shortsDirectory, "thumbnails", "thumbnail-1.jpeg");
    }

    private static async Task<ThumbnailImageValidation> ValidateImageAsync(string path, string? platform, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new ThumbnailImageValidation(false, "Thumbnail file is missing.", null, null, null);
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length <= 0)
        {
            return new ThumbnailImageValidation(false, "Thumbnail file is empty.", 0, null, null);
        }

        if (!IsSupportedImageExtension(Path.GetExtension(path)))
        {
            return new ThumbnailImageValidation(false, "Thumbnail must be a JPG or PNG file.", fileInfo.Length, null, null);
        }

        if (string.Equals(platform, "YouTube", StringComparison.OrdinalIgnoreCase) && fileInfo.Length > 2 * 1024 * 1024)
        {
            return new ThumbnailImageValidation(false, "YouTube thumbnail must be 2MB or smaller.", fileInfo.Length, null, null);
        }

        try
        {
            var imageInfo = await Image.IdentifyAsync(path, cancellationToken);
            if (imageInfo is null || imageInfo.Width <= 0 || imageInfo.Height <= 0)
            {
                return new ThumbnailImageValidation(false, "Thumbnail image dimensions are invalid.", fileInfo.Length, imageInfo?.Width, imageInfo?.Height);
            }

            return new ThumbnailImageValidation(true, null, fileInfo.Length, imageInfo.Width, imageInfo.Height);
        }
        catch
        {
            return new ThumbnailImageValidation(false, "Thumbnail file is not a valid JPG or PNG image.", fileInfo.Length, null, null);
        }
    }

    private static string NormalizeContentType(string? contentType)
        => string.Equals(contentType, PlatformThumbnailContentTypes.LongVideo, StringComparison.OrdinalIgnoreCase)
            ? PlatformThumbnailContentTypes.LongVideo
            : string.Equals(contentType, PlatformThumbnailContentTypes.Reel, StringComparison.OrdinalIgnoreCase)
                ? PlatformThumbnailContentTypes.Reel
                : PlatformThumbnailContentTypes.ShortVideo;

    private static bool IsSupportedImageExtension(string extension)
        => extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase);

    private sealed record ThumbnailImageValidation(bool IsValid, string? Warning, long? FileSizeBytes, int? Width, int? Height);
}
