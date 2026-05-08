using System.Diagnostics;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class AzureBlobPublicMediaStorageService : IPublicMediaStorageService
{
    private const int FourMegabytes = 4 * 1024 * 1024;
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(15);

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

        var fileInfo = new FileInfo(localFilePath);
        if (fileInfo.Length <= 0)
        {
            return Failed($"Instagram Reel video is empty: {localFilePath}.");
        }

        var blobName = BuildBlobName(pipelineRunId);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var blob = _blobClientFactory.Create(_options.ConnectionString, _options.ContainerName, blobName);
            await blob.CreateContainerIfNotExistsAsync(cancellationToken);

            var transferOptions = CreateTransferOptions();
            using var uploadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            uploadTimeout.CancelAfter(UploadTimeout);

            try
            {
                await blob.UploadAsync(localFilePath, "video/mp4", transferOptions, uploadTimeout.Token);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && uploadTimeout.IsCancellationRequested)
            {
                stopwatch.Stop();
                const string timeoutError = "Azure Blob upload timed out.";
                _logger.LogWarning(ex, "Public media upload timed out for Instagram Reel blob {BlobName} after {UploadDurationMs} ms.", blobName, stopwatch.ElapsedMilliseconds);
                await WriteDiagnosticsAsync(localFilePath, fileInfo.Length, blobName, stopwatch.ElapsedMilliseconds, success: false, timeoutError, ex, cancellationToken);
                return Failed(timeoutError, blobName);
            }

            var expiresUtc = DateTime.UtcNow.AddHours(Math.Max(1, _options.SasExpiryHours));
            var publicUrl = BuildPublicUrl(blob, blobName, expiresUtc);
            if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                stopwatch.Stop();
                const string invalidUrlError = "Public media upload did not produce an HTTPS URL.";
                await WriteDiagnosticsAsync(localFilePath, fileInfo.Length, blobName, stopwatch.ElapsedMilliseconds, success: false, invalidUrlError, exception: null, cancellationToken);
                return Failed(invalidUrlError, blobName, expiresUtc);
            }

            stopwatch.Stop();
            await WriteDiagnosticsAsync(localFilePath, fileInfo.Length, blobName, stopwatch.ElapsedMilliseconds, success: true, failure: null, exception: null, cancellationToken);
            _logger.LogInformation("Uploaded Instagram Reel public media blob {BlobName}. PublicUrl={PublicUrlMasked} ExpiresUtc={ExpiresUtc:o} UploadDurationMs={UploadDurationMs}", blobName, MaskSensitiveQuery(publicUrl), expiresUtc, stopwatch.ElapsedMilliseconds);
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
            stopwatch.Stop();
            _logger.LogWarning(ex, "Public media upload failed for Instagram Reel blob {BlobName} after {UploadDurationMs} ms.", blobName, stopwatch.ElapsedMilliseconds);
            await WriteDiagnosticsAsync(localFilePath, fileInfo.Length, blobName, stopwatch.ElapsedMilliseconds, success: false, $"Public media upload failed: {ex.Message}", ex, cancellationToken);
            return Failed($"Public media upload failed: {ex.Message}", blobName);
        }
    }

    private static StorageTransferOptions CreateTransferOptions()
        => new()
        {
            MaximumConcurrency = 2,
            InitialTransferSize = FourMegabytes,
            MaximumTransferSize = FourMegabytes
        };

    private async Task WriteDiagnosticsAsync(
        string localFilePath,
        long fileSize,
        string blobName,
        long uploadDurationMs,
        bool success,
        string? failure,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(localFilePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Directory.GetCurrentDirectory();
            }

            Directory.CreateDirectory(directory);
            var diagnostics = new PublicMediaUploadDiagnostics
            {
                LocalFilePath = localFilePath,
                FileSize = fileSize,
                BlobName = blobName,
                UploadDurationMs = uploadDurationMs,
                RetryCount = null,
                Success = success,
                Failure = failure,
                ExceptionType = exception?.GetType().FullName,
                GeneratedUtc = DateTime.UtcNow
            };

            var diagnosticsPath = Path.Combine(directory, "public-media-upload-result.json");
            await using var stream = new FileStream(diagnosticsPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 16 * 1024, useAsync: true);
            await JsonSerializer.SerializeAsync(stream, diagnostics, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to write public media upload diagnostics for blob {BlobName}.", blobName);
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
    Task UploadAsync(string localFilePath, string contentType, StorageTransferOptions transferOptions, CancellationToken cancellationToken);
    string GenerateReadOnlySasUrl(DateTime expiresUtc);
}

public sealed class AzurePublicBlobClientFactory : IAzurePublicBlobClientFactory
{
    public IAzurePublicBlobClient Create(string connectionString, string containerName, string blobName)
    {
        var container = new BlobContainerClient(connectionString, containerName, CreateClientOptions());
        return new AzurePublicBlobClient(container, blobName);
    }

    private static BlobClientOptions CreateClientOptions()
        => new()
        {
            Retry =
            {
                Mode = RetryMode.Exponential,
                Delay = TimeSpan.FromSeconds(2),
                MaxDelay = TimeSpan.FromSeconds(20),
                MaxRetries = 8,
                NetworkTimeout = TimeSpan.FromMinutes(10)
            }
        };
}

public sealed class AzurePublicBlobClient : IAzurePublicBlobClient
{
    private const int FourMegabytes = 4 * 1024 * 1024;

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

    public async Task UploadAsync(string localFilePath, string contentType, StorageTransferOptions transferOptions, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            localFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: FourMegabytes,
            useAsync: true);

        await _blob.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType
                },
                TransferOptions = transferOptions
            },
            cancellationToken);
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

public sealed class PublicMediaUploadDiagnostics
{
    public string LocalFilePath { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string BlobName { get; init; } = string.Empty;
    public long UploadDurationMs { get; init; }
    public int? RetryCount { get; init; }
    public bool Success { get; init; }
    public string? Failure { get; init; }
    public string? ExceptionType { get; init; }
    public DateTime GeneratedUtc { get; init; }
}
