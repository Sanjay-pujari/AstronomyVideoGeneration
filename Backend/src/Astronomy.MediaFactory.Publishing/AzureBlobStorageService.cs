using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class AzureBlobStorageService : IAzureBlobStorageService
{
    private readonly AzureBlobOptions _options;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(IOptions<AzureBlobOptions> options, ILogger<AzureBlobStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            _logger.LogWarning("AzureBlob:ConnectionString is not configured. Skipping blob upload.");
            return new BlobUploadResult();
        }

        var container = new BlobContainerClient(_options.ConnectionString, _options.ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        return new BlobUploadResult
        {
            VideoUrl = await UploadIfExistsAsync(container, request.VideoPath, request.BasePath, cancellationToken),
            AudioUrl = await UploadIfExistsAsync(container, request.AudioPath, request.BasePath, cancellationToken),
            ThumbnailUrl = await UploadIfExistsAsync(container, request.ThumbnailPath, request.BasePath, cancellationToken)
        };
    }

    private async Task<string?> UploadIfExistsAsync(BlobContainerClient container, string? localPath, string basePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            return null;

        var blobName = $"{basePath.TrimEnd('/')}/{Path.GetFileName(localPath)}".Replace("\\", "/");
        var blobClient = container.GetBlobClient(blobName);

        await using var stream = File.OpenRead(localPath);
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken);
        return blobClient.Uri.ToString();
    }
}
