using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
namespace Astronomy.MediaFactory.Publishing;
public sealed class AzureBlobArchivalService : IArchivalService
{
    private readonly AzureStorageOptions _options;
    public AzureBlobArchivalService(IOptions<AzureStorageOptions> options) { _options = options.Value; }
    public async Task<string?> ArchiveAsync(string localPath, string blobPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString) || !File.Exists(localPath)) return null;
        var container = new BlobContainerClient(_options.ConnectionString, _options.ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var blob = container.GetBlobClient(blobPath.Replace("\\", "/"));
        await using var stream = File.OpenRead(localPath);
        await blob.UploadAsync(stream, overwrite: true, cancellationToken);
        return blob.Uri.ToString();
    }
}
