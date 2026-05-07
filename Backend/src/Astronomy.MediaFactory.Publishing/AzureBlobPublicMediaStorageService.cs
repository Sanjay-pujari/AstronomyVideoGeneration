using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class AzureBlobPublicMediaStorageService : IPublicMediaStorageService
{
    private readonly PublicMediaStorageOptions _options;
    private readonly ILogger<AzureBlobPublicMediaStorageService> _logger;
    private readonly IAzurePublicBlobClientFactory _blobClientFactory;

    public AzureBlobPublicMediaStorageService(
        IOptions<PublicMediaStorageOptions> options,
        ILogger<AzureBlobPublicMediaStorageService> logger,
        IAzurePublicBlobClientFactory? blobClientFactory = null)
    {
        _options = options.Value;
        _logger = logger;
        _blobClientFactory = blobClientFactory ?? new AzurePublicBlobClientFactory();
    }

    public async Task<PublicMediaUploadResult> UploadForInstagramAsync(string localFilePath, Guid pipelineRunId, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Failed("PublicMediaStorage is disabled.");
        }

        if (!string.Equals(_options.Provider, "AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            return Failed($"PublicMediaStorage provider '{_options.Provider}' is not supported. Configure Provider=AzureBlob.");
        }

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return Failed("PublicMediaStorage:ConnectionString is required for Azure Blob public media uploads.");
        }

        if (string.IsNullOrWhiteSpace(_options.ContainerName))
        {
            return Failed("PublicMediaStorage:ContainerName is required for Azure Blob public media uploads.");
        }

        if (!File.Exists(localFilePath))
        {
            return Failed($"Instagram Reel video is missing: {localFilePath}.");
        }

        if (new FileInfo(localFilePath).Length <= 0)
        {
            return Failed($"Instagram Reel video is empty: {localFilePath}.");
        }

        var blobName = BuildBlobName(pipelineRunId);
        try
        {
            var blob = _blobClientFactory.Create(_options.ConnectionString, _options.ContainerName, blobName);
            await blob.CreateContainerIfNotExistsAsync(cancellationToken);
            await blob.UploadAsync(localFilePath, "video/mp4", cancellationToken);

            var expiresUtc = DateTime.UtcNow.AddHours(Math.Max(1, _options.SasExpiryHours));
            var publicUrl = BuildPublicUrl(blob, blobName, expiresUtc);
            if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return Failed("Public media upload did not produce an HTTPS URL.", blobName, expiresUtc);
            }

            _logger.LogInformation("Uploaded Instagram Reel public media blob {BlobName}. PublicUrl={PublicUrlMasked} ExpiresUtc={ExpiresUtc:o}", blobName, MaskSensitiveQuery(publicUrl), expiresUtc);
            return new PublicMediaUploadResult
            {
                Success = true,
                PublicUrl = publicUrl,
                BlobName = blobName,
                ExpiresUtc = expiresUtc
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Azure.RequestFailedException)
        {
            _logger.LogWarning(ex, "Public media upload failed for Instagram Reel blob {BlobName}.", blobName);
            return Failed($"Public media upload failed: {ex.Message}", blobName);
        }
    }

    private string BuildPublicUrl(IAzurePublicBlobClient blob, string blobName, DateTime expiresUtc)
    {
        if (_options.UseSasUrl)
        {
            return blob.GenerateReadOnlySasUrl(expiresUtc);
        }

        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return _options.PublicBaseUrl.TrimEnd('/') + "/" + string.Join('/', blobName.Split('/').Select(Uri.EscapeDataString));
        }

        return blob.Uri.ToString();
    }

    private string BuildBlobName(Guid pipelineRunId)
    {
        var prefix = (_options.BlobPrefix ?? string.Empty).Trim('/');
        return string.IsNullOrWhiteSpace(prefix)
            ? $"{pipelineRunId}/short-video.mp4"
            : $"{prefix}/{pipelineRunId}/short-video.mp4";
    }

    private static PublicMediaUploadResult Failed(string error, string blobName = "", DateTime expiresUtc = default)
        => new() { Success = false, Error = error, BlobName = blobName, ExpiresUtc = expiresUtc };

    public static string MaskSensitiveQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Query))
        {
            return value ?? string.Empty;
        }

        return new UriBuilder(uri) { Query = "REDACTED" }.Uri.ToString();
    }
}

public interface IAzurePublicBlobClientFactory
{
    IAzurePublicBlobClient Create(string connectionString, string containerName, string blobName);
}

public interface IAzurePublicBlobClient
{
    Uri Uri { get; }
    Task CreateContainerIfNotExistsAsync(CancellationToken cancellationToken);
    Task UploadAsync(string localFilePath, string contentType, CancellationToken cancellationToken);
    string GenerateReadOnlySasUrl(DateTime expiresUtc);
}

public sealed class AzurePublicBlobClientFactory : IAzurePublicBlobClientFactory
{
    public IAzurePublicBlobClient Create(string connectionString, string containerName, string blobName)
    {
        var container = new BlobContainerClient(connectionString, containerName);
        return new AzurePublicBlobClient(container, blobName);
    }
}

public sealed class AzurePublicBlobClient : IAzurePublicBlobClient
{
    private readonly BlobContainerClient _container;
    private readonly BlobClient _blob;

    public AzurePublicBlobClient(BlobContainerClient container, string blobName)
    {
        _container = container;
        _blob = container.GetBlobClient(blobName);
    }

    public Uri Uri => _blob.Uri;

    public async Task CreateContainerIfNotExistsAsync(CancellationToken cancellationToken)
        => await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

    public async Task UploadAsync(string localFilePath, string contentType, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(localFilePath);
        await _blob.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
    }

    public string GenerateReadOnlySasUrl(DateTime expiresUtc)
    {
        if (!_blob.CanGenerateSasUri)
        {
            throw new InvalidOperationException("Azure Blob client cannot generate SAS URLs. Use a storage account connection string or configure PublicBaseUrl for a public container.");
        }

        var builder = new BlobSasBuilder
        {
            BlobContainerName = _blob.BlobContainerName,
            BlobName = _blob.Name,
            Resource = "b",
            ExpiresOn = new DateTimeOffset(expiresUtc, TimeSpan.Zero)
        };
        builder.SetPermissions(BlobSasPermissions.Read);
        return _blob.GenerateSasUri(builder).ToString();
    }
}
