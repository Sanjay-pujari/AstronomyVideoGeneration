using System.Net;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class CelestialAssetIngestionServiceTests
{
    [Fact]
    public async Task EmptyFolderGetsPopulatedAndMetadataWritten()
    {
        var root = CreateTempDirectory();
        var imageBytes = await CreateImageBytesAsync();
        var handler = new StubHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/search")
                return JsonResponse(SearchPayload("NASA Jupiter", "JUPITER_1", "https://cdn.test/jupiter.jpg"));
            if (request.RequestUri!.Host == "cdn.test")
                return BytesResponse(imageBytes, "image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(root, handler, requiredObjects: ["jupiter"]);

        var result = await service.RefreshObjectAsync("jupiter", CancellationToken.None);

        Assert.Equal(1, result.ImagesFound);
        Assert.Equal(1, result.ImagesDownloaded);
        Assert.True(File.Exists(result.SelectedPrimaryAsset));
        Assert.True(File.Exists(Path.Combine(root, "jupiter", "asset-metadata.json")));
        Assert.True(File.Exists(Path.Combine(root, "asset-ingestion-report.json")));
    }

    [Fact]
    public async Task CachePreventsRedownload()
    {
        var root = CreateTempDirectory();
        var imageBytes = await CreateImageBytesAsync();
        var calls = 0;
        var handler = new StubHttpHandler(request =>
        {
            calls++;
            if (request.RequestUri!.AbsolutePath == "/search")
                return JsonResponse(SearchPayload("NASA Mars", "MARS_1", "https://cdn.test/mars.jpg"));
            if (request.RequestUri!.Host == "cdn.test")
                return BytesResponse(imageBytes, "image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(root, handler, requiredObjects: ["mars"]);

        await service.RefreshObjectAsync("mars", CancellationToken.None);
        calls = 0;
        var cached = await service.RefreshObjectAsync("mars", CancellationToken.None);

        Assert.True(cached.SkippedBecauseCached);
        Assert.Equal(0, cached.ImagesDownloaded);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task MissingNasaResultFallsBackSafely()
    {
        var root = CreateTempDirectory();
        var handler = new StubHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/search")
                return JsonResponse("""{"collection":{"items":[]}}""");
            if (request.RequestUri!.AbsolutePath == "/planetary/apod")
                return JsonResponse("""{"media_type":"video"}""");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(root, handler, requiredObjects: ["neptune"]);

        var result = await service.RefreshObjectAsync("neptune", CancellationToken.None);
        var status = await service.GetObjectAsync("neptune", CancellationToken.None);

        Assert.Equal(0, result.ImagesDownloaded);
        Assert.NotNull(status);
        Assert.False(status!.IsSatisfied);
        Assert.Empty(status.Images);
    }

    [Fact]
    public async Task ThumbnailProviderUsesLocalCachedAssetBeforeNasaDownload()
    {
        var root = CreateTempDirectory();
        var directory = Path.Combine(root, "saturn");
        Directory.CreateDirectory(directory);
        var localPath = Path.Combine(directory, "curated.jpg");
        await File.WriteAllBytesAsync(localPath, await CreateImageBytesAsync());
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("NASA should not be called for cached assets."));
        var ingestion = CreateService(root, handler, requiredObjects: ["saturn"]);
        var provider = new CelestialAssetProvider(
            ingestion,
            Options.Create(new CelestialAssetsOptions { RootPath = root, RequiredObjects = ["saturn"], MaxImagesPerObject = 1 }),
            NullLogger<CelestialAssetProvider>.Instance);

        var asset = await provider.GetAssetAsync(new CelestialAssetRequest { ObjectName = "Saturn", ObjectType = "Planet" }, CancellationToken.None);

        Assert.Equal(localPath, asset.LocalPath);
        Assert.Equal("LocalCache", asset.Source);
        Assert.False(asset.FallbackUsed);
    }

    private static CelestialAssetIngestionService CreateService(string root, HttpMessageHandler handler, IReadOnlyCollection<string> requiredObjects)
        => new(
            new HttpClient(handler),
            Options.Create(new CelestialAssetsOptions
            {
                RootPath = root,
                RequiredObjects = requiredObjects.ToList(),
                MaxImagesPerObject = 1,
                DownloadIfMissing = true,
                PreferLocalCache = true,
                RefreshExistingAssets = false,
                AllowedExtensions = [".jpg", ".jpeg", ".png"]
            }),
            Options.Create(new AstronomyApiOptions { NasaApiKey = "DEMO_KEY", NasaBaseUrl = "https://api.nasa.gov" }),
            Options.Create(new NasaImagesOptions { SearchBaseUrl = "https://images-api.nasa.gov", SearchEndpoint = "/search", AssetEndpoint = "/asset/{nasaId}" }),
            NullLogger<CelestialAssetIngestionService>.Instance);

    private static string SearchPayload(string title, string nasaId, string href)
        => JsonSerializer.Serialize(new
        {
            collection = new
            {
                items = new[]
                {
                    new
                    {
                        data = new[] { new { title, nasa_id = nasaId, description = "A NASA astronomy image." } },
                        links = new[] { new { href } }
                    }
                }
            }
        });

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage BytesResponse(byte[] bytes, string mediaType)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType) } } };

    private static async Task<byte[]> CreateImageBytesAsync()
    {
        using var image = new Image<Rgba32>(64, 64, Color.Navy);
        await using var stream = new MemoryStream();
        await image.SaveAsJpegAsync(stream);
        return stream.ToArray();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "celestial-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
