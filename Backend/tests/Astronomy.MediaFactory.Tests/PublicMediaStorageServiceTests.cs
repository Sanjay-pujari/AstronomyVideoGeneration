using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PublicMediaStorageServiceTests
{
    [Fact]
    public async Task UploadForInstagram_UploadsShortVideoToConfiguredBlobPath()
    {
        using var temp = new TempFile();
        var runId = Guid.NewGuid();
        var client = new FakeAzurePublicBlobClient("https://account.blob.core.windows.net/meta-media/astronomy/reels/short-video.mp4?sig=secret");
        var service = CreateService(client);

        var result = await service.UploadForInstagramAsync(temp.Path, runId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal($"astronomy/reels/{runId}/short-video.mp4", result.BlobName);
        Assert.Equal($"astronomy/reels/{runId}/short-video.mp4", client.BlobName);
        Assert.Equal(temp.Path, client.UploadedLocalFilePath);
        Assert.Equal("video/mp4", client.ContentType);
    }

    [Fact]
    public async Task UploadForInstagram_GeneratedSasUrlStartsWithHttps()
    {
        using var temp = new TempFile();
        var client = new FakeAzurePublicBlobClient("https://account.blob.core.windows.net/meta-media/blob.mp4?sv=2026&sig=secret");
        var service = CreateService(client);

        var result = await service.UploadForInstagramAsync(temp.Path, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.StartsWith("https://", result.PublicUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadForInstagram_SasExpiryIsSet()
    {
        using var temp = new TempFile();
        var client = new FakeAzurePublicBlobClient("https://account.blob.core.windows.net/meta-media/blob.mp4?sig=secret");
        var service = CreateService(client, sasExpiryHours: 6);
        var before = DateTime.UtcNow.AddHours(5).AddMinutes(55);

        var result = await service.UploadForInstagramAsync(temp.Path, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.ExpiresUtc > before);
        Assert.Equal(result.ExpiresUtc, client.ExpiresUtc);
    }

    private static AzureBlobPublicMediaStorageService CreateService(FakeAzurePublicBlobClient client, int sasExpiryHours = 24)
        => new(
            Options.Create(new PublicMediaStorageOptions
            {
                Enabled = true,
                Provider = "AzureBlob",
                ConnectionString = "UseDevelopmentStorage=true",
                ContainerName = "meta-media",
                BlobPrefix = "astronomy/reels",
                UseSasUrl = true,
                SasExpiryHours = sasExpiryHours
            }),
            NullLogger<AzureBlobPublicMediaStorageService>.Instance,
            new FakeAzurePublicBlobClientFactory(client));

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "public-media-tests", Guid.NewGuid().ToString("N"), "short-video.mp4");

        public TempFile()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, "mp4");
        }

        public void Dispose()
        {
            var root = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FakeAzurePublicBlobClientFactory : IAzurePublicBlobClientFactory
    {
        private readonly FakeAzurePublicBlobClient _client;

        public FakeAzurePublicBlobClientFactory(FakeAzurePublicBlobClient client) => _client = client;

        public IAzurePublicBlobClient Create(string connectionString, string containerName, string blobName)
        {
            _client.BlobName = blobName;
            return _client;
        }
    }

    private sealed class FakeAzurePublicBlobClient : IAzurePublicBlobClient
    {
        private readonly string _sasUrl;

        public FakeAzurePublicBlobClient(string sasUrl)
        {
            _sasUrl = sasUrl;
            Uri = new Uri("https://account.blob.core.windows.net/meta-media/blob.mp4");
        }

        public Uri Uri { get; }
        public string? BlobName { get; set; }
        public string? UploadedLocalFilePath { get; private set; }
        public string? ContentType { get; private set; }
        public DateTime ExpiresUtc { get; private set; }

        public Task CreateContainerIfNotExistsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UploadAsync(string localFilePath, string contentType, CancellationToken cancellationToken)
        {
            UploadedLocalFilePath = localFilePath;
            ContentType = contentType;
            return Task.CompletedTask;
        }

        public string GenerateReadOnlySasUrl(DateTime expiresUtc)
        {
            ExpiresUtc = expiresUtc;
            return _sasUrl;
        }
    }
}
