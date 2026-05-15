using System.Diagnostics;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Azure.Core;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = Activity.Current?.Id ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        var container = BuildContainerClient();
        if (container is null)
        {
            _logger.LogWarning("Azure blob storage is not configured. Skipping blob upload for base path {BasePath}. CorrelationId: {CorrelationId}", request.BasePath, correlationId);
            return new BlobUploadResult();
        }

        _logger.LogInformation("Starting blob upload for base path {BasePath}. CorrelationId: {CorrelationId}", request.BasePath, correlationId);
        await TransientRetryHelper.ExecuteAsync(
            async ct =>
            {
                await container.CreateIfNotExistsAsync(cancellationToken: ct);
                return true;
            },
            _options.UploadRetryAttempts,
            TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds),
            TimeSpan.FromSeconds(_options.MaxRetryDelaySeconds),
            _logger,
            "blob container initialization",
            request.BasePath,
            cancellationToken);

        var videoUploadTask = UploadIfExistsAsync(container, request.VideoPath, request.BasePath, cancellationToken);
        var audioUploadTask = UploadIfExistsAsync(container, request.AudioPath, request.BasePath, cancellationToken);
        var thumbnailUploadTask = UploadIfExistsAsync(container, request.ThumbnailPath, request.BasePath, cancellationToken);

        await Task.WhenAll(videoUploadTask, audioUploadTask, thumbnailUploadTask);

        var result = new BlobUploadResult
        {
            VideoUrl = await videoUploadTask,
            AudioUrl = await audioUploadTask,
            ThumbnailUrl = await thumbnailUploadTask
        };

        _logger.LogInformation("Completed blob upload for base path {BasePath}. VideoUploaded={HasVideo} AudioUploaded={HasAudio} ThumbnailUploaded={HasThumbnail}. CorrelationId: {CorrelationId}", request.BasePath, result.VideoUrl is not null, result.AudioUrl is not null, result.ThumbnailUrl is not null, correlationId);
        return result;
    }

    private BlobContainerClient? BuildContainerClient()
    {
        if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return new BlobContainerClient(_options.ConnectionString, _options.ContainerName, CreateBlobClientOptions());
        }

        if (_options.UseManagedIdentity)
        {
            var serviceUri = _options.ServiceUri;
            if (string.IsNullOrWhiteSpace(serviceUri) && !string.IsNullOrWhiteSpace(_options.AccountName))
                serviceUri = $"https://{_options.AccountName}.blob.core.windows.net";

            if (!string.IsNullOrWhiteSpace(serviceUri) && Uri.TryCreate(serviceUri, UriKind.Absolute, out var uri))
            {
                var serviceClient = new BlobServiceClient(uri, new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = string.IsNullOrWhiteSpace(_options.ManagedIdentityClientId) ? null : _options.ManagedIdentityClientId.Trim()
                }), CreateBlobClientOptions());
                return serviceClient.GetBlobContainerClient(_options.ContainerName);
            }
        }

        return null;
    }

    private BlobClientOptions CreateBlobClientOptions()
        => new()
        {
            Retry =
            {
                Mode = RetryMode.Exponential,
                Delay = TimeSpan.FromSeconds(Math.Max(1, _options.RetryBaseDelaySeconds)),
                MaxDelay = TimeSpan.FromSeconds(Math.Max(_options.RetryBaseDelaySeconds, _options.MaxRetryDelaySeconds)),
                MaxRetries = 1,
                NetworkTimeout = TimeSpan.FromMinutes(2)
            }
        };

    private StorageTransferOptions CreateTransferOptions()
        => new()
        {
            MaximumConcurrency = Math.Clamp(_options.UploadMaximumConcurrency, 1, 16),
            InitialTransferSize = MegabytesToBytes(_options.UploadInitialTransferSizeMegabytes, 8),
            MaximumTransferSize = MegabytesToBytes(_options.UploadTransferChunkSizeMegabytes, 8)
        };

    private static long MegabytesToBytes(int megabytes, int fallbackMegabytes)
        => Math.Max(1, megabytes > 0 ? megabytes : fallbackMegabytes) * 1024L * 1024L;

    private async Task<string?> UploadIfExistsAsync(BlobContainerClient container, string? localPath, string basePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return null;
        }

        if (!File.Exists(localPath))
        {
            _logger.LogWarning("Skipping blob upload because local file {LocalPath} was not found.", localPath);
            return null;
        }

        var fileInfo = new FileInfo(localPath);
        var blobName = $"{basePath.TrimEnd('/')}/{Path.GetFileName(localPath)}".Replace("\\", "/");
        var blobClient = container.GetBlobClient(blobName);
        var transferOptions = CreateTransferOptions();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Uploading blob {BlobName} from {LocalPath}. SizeBytes={SizeBytes} MaximumConcurrency={MaximumConcurrency} InitialTransferSizeBytes={InitialTransferSizeBytes} MaximumTransferSizeBytes={MaximumTransferSizeBytes}",
            blobName,
            localPath,
            fileInfo.Length,
            transferOptions.MaximumConcurrency,
            transferOptions.InitialTransferSize,
            transferOptions.MaximumTransferSize);

        var url = await TransientRetryHelper.ExecuteAsync(
            async ct =>
            {
                await using var stream = File.OpenRead(localPath);
                await blobClient.UploadAsync(
                    stream,
                    new BlobUploadOptions
                    {
                        TransferOptions = transferOptions
                    },
                    ct);
                return blobClient.Uri.ToString();
            },
            _options.UploadRetryAttempts,
            TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds),
            TimeSpan.FromSeconds(_options.MaxRetryDelaySeconds),
            _logger,
            "blob upload",
            blobName,
            cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation(
            "Completed blob {BlobName} upload. SizeBytes={SizeBytes} DurationMs={DurationMs} ThroughputMbps={ThroughputMbps:F2}",
            blobName,
            fileInfo.Length,
            stopwatch.ElapsedMilliseconds,
            CalculateThroughputMbps(fileInfo.Length, stopwatch.Elapsed));

        return url;
    }

    private static double CalculateThroughputMbps(long bytes, TimeSpan elapsed)
        => elapsed.TotalSeconds <= 0 ? 0 : bytes * 8d / 1_000_000d / elapsed.TotalSeconds;
}
