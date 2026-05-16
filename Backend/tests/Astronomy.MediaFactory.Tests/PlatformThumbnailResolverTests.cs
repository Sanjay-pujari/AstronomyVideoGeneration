using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PlatformThumbnailResolverTests
{
    [Fact]
    public async Task YouTubeLong_ResolvesThumbnailLong()
    {
        var outputDir = CreateTempDirectory();
        var expected = Path.Combine(outputDir, "thumbnails", "thumbnail-long.jpg");
        await WriteImageAsync(expected, 1280, 720);
        await WriteImageAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-short.jpg"), 1080, 1920);

        var result = await CreateResolver().ResolveAsync(outputDir, "YouTube", PlatformThumbnailContentTypes.LongVideo, CancellationToken.None);

        Assert.Equal(expected, result.PlatformThumbnailPath);
        Assert.Equal(ThumbnailSources.GeneratedThumbnail, result.ThumbnailSource);
    }

    [Fact]
    public async Task YouTubeShort_ResolvesThumbnailShort()
    {
        var outputDir = CreateTempDirectory();
        await WriteImageAsync(Path.Combine(outputDir, "thumbnails", "thumbnail-long.jpg"), 1280, 720);
        var expected = Path.Combine(outputDir, "thumbnails", "thumbnail-short.jpg");
        await WriteImageAsync(expected, 1080, 1920);

        var result = await CreateResolver().ResolveAsync(outputDir, "YouTube", PlatformThumbnailContentTypes.ShortVideo, CancellationToken.None);

        Assert.Equal(expected, result.PlatformThumbnailPath);
        Assert.Equal(ThumbnailSources.GeneratedThumbnail, result.ThumbnailSource);
    }

    [Fact]
    public async Task FacebookReel_ResolvesThumbnailShort()
    {
        var outputDir = CreateTempDirectory();
        var expected = Path.Combine(outputDir, "thumbnails", "thumbnail-short.jpg");
        await WriteImageAsync(expected, 1080, 1920);

        var result = await CreateResolver().ResolveAsync(outputDir, "Facebook", PlatformThumbnailContentTypes.Reel, CancellationToken.None);

        Assert.Equal(expected, result.PlatformThumbnailPath);
        Assert.Equal(ThumbnailSources.GeneratedThumbnail, result.ThumbnailSource);
    }

    [Fact]
    public async Task InstagramReel_ResolvesThumbnailShort()
    {
        var outputDir = CreateTempDirectory();
        var expected = Path.Combine(outputDir, "thumbnails", "thumbnail-short.jpg");
        await WriteImageAsync(expected, 1080, 1920);

        var result = await CreateResolver().ResolveAsync(outputDir, "Instagram", PlatformThumbnailContentTypes.Reel, CancellationToken.None);

        Assert.Equal(expected, result.PlatformThumbnailPath);
        Assert.Equal(ThumbnailSources.GeneratedThumbnail, result.ThumbnailSource);
    }

    [Fact]
    public async Task FallbackScreenshot_IsNotUsed_WhenGeneratedThumbnailExists()
    {
        var outputDir = CreateTempDirectory();
        var generated = Path.Combine(outputDir, "thumbnails", "thumbnail-long.jpg");
        var fallback = Path.Combine(outputDir, "thumbnail-1.png");
        await WriteImageAsync(generated, 1280, 720);
        await WriteImageAsync(fallback, 1280, 720);

        var result = await CreateResolver().ResolveAsync(outputDir, "YouTube", PlatformThumbnailContentTypes.LongVideo, CancellationToken.None);

        Assert.Equal(generated, result.PlatformThumbnailPath);
        Assert.NotEqual(fallback, result.PlatformThumbnailPath);
        Assert.Equal(ThumbnailSources.GeneratedThumbnail, result.ThumbnailSource);
    }

    [Fact]
    public async Task MissingThumbnail_FallsBackSafely()
    {
        var outputDir = CreateTempDirectory();
        var fallback = Path.Combine(outputDir, "thumbnail-1.png");
        await WriteImageAsync(fallback, 1280, 720);

        var result = await CreateResolver().ResolveAsync(outputDir, "YouTube", PlatformThumbnailContentTypes.LongVideo, CancellationToken.None);

        Assert.Equal(fallback, result.PlatformThumbnailPath);
        Assert.Equal(ThumbnailSources.FallbackThumbnail, result.ThumbnailSource);
        Assert.True(result.IsValid);
    }

    private static PlatformThumbnailResolver CreateResolver()
        => new(NullLogger<PlatformThumbnailResolver>.Instance);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "astro-thumb-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WriteImageAsync(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Image<Rgba32>(width, height, Color.MidnightBlue);
        await image.SaveAsync(path);
    }
}
