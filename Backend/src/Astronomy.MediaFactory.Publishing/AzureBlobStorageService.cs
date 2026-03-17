using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Azure.Identity;
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
        var container = BuildContainerClient();
        if (container is null)
        {
            _logger.LogWarning("Azure blob storage is not configured. Skipping blob upload.");
            return new BlobUploadResult();
        }

        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        return new BlobUploadResult
        {
            VideoUrl = await UploadIfExistsAsync(container, request.VideoPath, request.BasePath, cancellationToken),
            AudioUrl = await UploadIfExistsAsync(container, request.AudioPath, request.BasePath, cancellationToken),
            ThumbnailUrl = await UploadIfExistsAsync(container, request.ThumbnailPath, request.BasePath, cancellationToken)
        };
    }

    private BlobContainerClient? BuildContainerClient()
    {
        if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return new BlobContainerClient(_options.ConnectionString, _options.ContainerName);
        }

        if (_options.UseManagedIdentity)
        {
            var serviceUri = _options.ServiceUri;
            if (string.IsNullOrWhiteSpace(serviceUri) && !string.IsNullOrWhiteSpace(_options.AccountName))
                serviceUri = $"https://{_options.AccountName}.blob.core.windows.net";

            if (!string.IsNullOrWhiteSpace(serviceUri) && Uri.TryCreate(serviceUri, UriKind.Absolute, out var uri))
            {
                var serviceClient = new BlobServiceClient(uri, new DefaultAzureCredential());
                return serviceClient.GetBlobContainerClient(_options.ContainerName);
            }
        }

        return null;
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
