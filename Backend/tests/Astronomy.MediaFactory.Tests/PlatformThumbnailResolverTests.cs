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
    public async Task YouTube_long_resolves_thumbnail_long()
    {
        var root = CreateTempRoot();
        try
        {
            var expected = await WriteImageAsync(root, "thumbnails/thumbnail-long.jpg", 1280, 720);
            await WriteImageAsync(root, "thumbnails/thumbnail-short.jpg", 1080, 1920);

            var result = await ResolveAsync(root, "YouTube", PlatformThumbnailContentTypes.LongVideo);

            Assert.Equal(expected, result.PlatformThumbnailPath);
            Assert.Equal(ThumbnailSources.GeneratedThumbnail, result.ThumbnailSource);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("YouTube", PlatformThumbnailContentTypes.ShortVideo)]
    [InlineData("Facebook", PlatformThumbnailContentTypes.Reel)]
    [InlineData("Instagram", PlatformThumbnailContentTypes.Reel)]
    public async Task Short_and_reel_platforms_resolve_thumbnail_short(string platform, string contentKind)
    {
        var root = CreateTempRoot();
        try
        {
            await WriteImageAsync(root, "thumbnails/thumbnail-long.jpg", 1280, 720);
            var expected = await WriteImageAsync(root, "thumbnails/thumbnail-short.jpg", 1080, 1920);

            var result = await ResolveAsync(root, platform, contentKind);

            Assert.Equal(expected, result.PlatformThumbnailPath);
            Assert.Equal(ThumbnailSources.GeneratedThumbnail, result.ThumbnailSource);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Generated_thumbnail_wins_over_existing_fallback_screenshot()
    {
        var root = CreateTempRoot();
        try
        {
            var generated = await WriteImageAsync(root, "thumbnails/thumbnail-short.jpg", 1080, 1920);
            await WriteImageAsync(root, "shorts/thumbnail-1.png", 1080, 1920);

            var result = await ResolveAsync(root, "Instagram", PlatformThumbnailContentTypes.Reel);

            Assert.Equal(generated, result.PlatformThumbnailPath);
            Assert.Equal(ThumbnailSources.GeneratedThumbnail, result.ThumbnailSource);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Missing_generated_thumbnail_falls_back_safely()
    {
        var root = CreateTempRoot();
        try
        {
            var fallback = await WriteImageAsync(root, "shorts/thumbnail-1.png", 1080, 1920);

            var result = await ResolveAsync(root, "Facebook", PlatformThumbnailContentTypes.Reel);

            Assert.Equal(fallback, result.PlatformThumbnailPath);
            Assert.Equal(ThumbnailSources.FallbackThumbnail, result.ThumbnailSource);
            Assert.True(result.IsValid);
        }
        finally { Directory.Delete(root, true); }
    }

    private static Task<PlatformThumbnailResolution> ResolveAsync(string root, string platform, string contentKind)
        => new PlatformThumbnailResolver(NullLogger<PlatformThumbnailResolver>.Instance).ResolveAsync(root, platform, contentKind, CancellationToken.None);

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "platform-thumbnail-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<string> WriteImageAsync(string root, string relativePath, int width, int height)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Image<Rgba32>(width, height, new Rgba32(20, 30, 40));
        await image.SaveAsync(path);
        return path;
    }
}
