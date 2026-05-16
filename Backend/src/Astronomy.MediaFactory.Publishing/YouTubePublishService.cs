using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Publishing;

public sealed class YouTubePublishService : IYouTubePublishService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IYouTubeAuthService _authService;
    private readonly IYouTubeApiClient _apiClient;
    private readonly PublishingOptions _publishingOptions;
    private readonly YouTubeOptions _youTubeOptions;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly IPipelineRepository _repository;
    private readonly ILogger<YouTubePublishService> _logger;

    public YouTubePublishService(
        IYouTubeAuthService authService,
        IYouTubeApiClient apiClient,
        IOptions<PublishingOptions> publishingOptions,
        IOptions<YouTubeOptions> youTubeOptions,
        IOptions<MaintenanceOptions> maintenanceOptions,
        IPipelineRepository repository,
        ILogger<YouTubePublishService> logger)
    {
        _authService = authService;
        _apiClient = apiClient;
        _publishingOptions = publishingOptions.Value;
        _youTubeOptions = youTubeOptions.Value;
        _maintenanceOptions = maintenanceOptions.Value;
        _repository = repository;
        _logger = logger;
    }

    public string PlatformName => "YouTube";

    public async Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken)
    {
        var mode = NormalizeMode(_publishingOptions.Mode);
        var normalizedRequest = NormalizeRequest(request, mode);
        var outputDirectory = await ResolveOutputDirectoryAsync(request.PipelineRunId, cancellationToken);
        Directory.CreateDirectory(outputDirectory);
        await WritePayloadAsync(outputDirectory, normalizedRequest, mode, cancellationToken);

        if (mode == "DryRun")
        {
            var result = new PublishResult
            {
                Success = true,
                Platform = PlatformName,
                ContentType = normalizedRequest.IsShort ? PlatformThumbnailContentTypes.ShortVideo : PlatformThumbnailContentTypes.LongVideo,
                AssetType = normalizedRequest.AssetType,
                IsShort = normalizedRequest.IsShort,
                UploadedThumbnailPath = normalizedRequest.PlatformThumbnailPath,
                ThumbnailSource = normalizedRequest.ThumbnailSource,
                ThumbnailUploadAttempted = false,
                ThumbnailUploadSuccess = false,
                Mode = mode,
                PublishedUtc = DateTime.UtcNow
            };
            await WriteResultAndPersistAsync(outputDirectory, request.PipelineRunId, normalizedRequest, result, cancellationToken);
            return result;
        }

        try
        {
            if (!File.Exists(normalizedRequest.VideoPath))
            {
                throw new FileNotFoundException("Final video file is missing.", normalizedRequest.VideoPath);
            }

            var accessToken = await _authService.GetAccessTokenAsync(cancellationToken);
            var channel = await _apiClient.GetAuthenticatedChannelAsync(accessToken, cancellationToken);
            _logger.LogInformation("Publishing to YouTube channel: {ChannelTitle} ({ChannelId})", channel.ChannelTitle, channel.ChannelId);
            await WriteJsonAsync(Path.Combine(outputDirectory, "youtube-channel-info.json"), channel, cancellationToken);

            var videoId = await _apiClient.UploadVideoAsync(normalizedRequest, accessToken, cancellationToken);
            var thumbnailWarning = await TryUploadThumbnailAsync(normalizedRequest, videoId, accessToken, outputDirectory, cancellationToken);
            var thumbnailUploadAttempted = normalizedRequest.UploadThumbnail && !string.IsNullOrWhiteSpace(normalizedRequest.PlatformThumbnailPath);
            var warnings = new List<string>();
            if (!string.IsNullOrWhiteSpace(thumbnailWarning))
            {
                warnings.Add("Video uploaded but thumbnail upload failed.");
            }
            var result = new PublishResult
            {
                Success = true,
                Platform = PlatformName,
                ContentType = normalizedRequest.IsShort ? PlatformThumbnailContentTypes.ShortVideo : PlatformThumbnailContentTypes.LongVideo,
                AssetType = normalizedRequest.AssetType,
                IsShort = normalizedRequest.IsShort,
                UploadedThumbnailPath = normalizedRequest.PlatformThumbnailPath,
                ThumbnailSource = normalizedRequest.ThumbnailSource,
                ThumbnailUploadAttempted = thumbnailUploadAttempted,
                ThumbnailUploadSuccess = thumbnailUploadAttempted && thumbnailWarning is null,
                ThumbnailWarning = thumbnailWarning,
                VideoId = videoId,
                VideoUrl = $"https://www.youtube.com/watch?v={videoId}",
                ChannelId = channel.ChannelId,
                ChannelTitle = channel.ChannelTitle,
                Error = thumbnailWarning is null ? null : $"{warnings[0]} {thumbnailWarning}",
                Warnings = warnings,
                Mode = mode,
                PublishedUtc = DateTime.UtcNow
            };
            await WriteResultAndPersistAsync(outputDirectory, request.PipelineRunId, normalizedRequest, result, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            var result = new PublishResult
            {
                Success = false,
                Platform = PlatformName,
                ContentType = normalizedRequest.IsShort ? PlatformThumbnailContentTypes.ShortVideo : PlatformThumbnailContentTypes.LongVideo,
                AssetType = normalizedRequest.AssetType,
                IsShort = normalizedRequest.IsShort,
                UploadedThumbnailPath = normalizedRequest.PlatformThumbnailPath,
                ThumbnailSource = normalizedRequest.ThumbnailSource,
                ThumbnailWarning = ex.Message,
                Error = ex.Message,
                Mode = mode,
                PublishedUtc = DateTime.UtcNow
            };
            await WriteResultAndPersistAsync(outputDirectory, request.PipelineRunId, normalizedRequest, result, cancellationToken);
            return result;
        }
    }

    private PublishRequest NormalizeRequest(PublishRequest request, string mode)
    {
        var privacyStatus = mode == "Public"
            ? "public"
            : mode == "Private"
                ? "private"
                : NormalizePrivacy(request.PrivacyStatus, _publishingOptions.DefaultPrivacyStatus, _youTubeOptions.DefaultPrivacyStatus);

        var selectedThumbnailPath = request.IsShort
            ? FirstNonBlank(request.ShortThumbnailPath, request.PlatformThumbnailPath, request.ThumbnailPath)
            : FirstNonBlank(request.LongThumbnailPath, request.PlatformThumbnailPath, request.ThumbnailPath);

        return new PublishRequest
        {
            PipelineRunId = request.PipelineRunId,
            Platform = PlatformName,
            AssetType = string.IsNullOrWhiteSpace(request.AssetType) ? "LongVideo" : request.AssetType,
            IsShort = request.IsShort,
            VideoPath = request.VideoPath,
            ThumbnailPath = selectedThumbnailPath,
            LongThumbnailPath = request.LongThumbnailPath,
            ShortThumbnailPath = request.ShortThumbnailPath,
            PlatformThumbnailPath = selectedThumbnailPath,
            ThumbnailSource = string.IsNullOrWhiteSpace(request.ThumbnailSource) ? ThumbnailSources.GeneratedThumbnail : request.ThumbnailSource,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Astronomy update" : request.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? request.Title.Trim() : request.Description.Trim(),
            Tags = request.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList(),
            PrivacyStatus = privacyStatus,
            UploadThumbnail = request.UploadThumbnail,
            YouTubeShortEligible = request.YouTubeShortEligible
        };
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string NormalizePrivacy(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.Equals(value, "public", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(value, "unlisted", StringComparison.OrdinalIgnoreCase))
            {
                return "unlisted";
            }

            if (string.Equals(value, "private", StringComparison.OrdinalIgnoreCase))
            {
                return "private";
            }
        }

        return "private";
    }

    private static string NormalizeMode(string? mode)
        => string.Equals(mode, "Public", StringComparison.Ordinal) ? "Public"
            : string.Equals(mode, "Private", StringComparison.OrdinalIgnoreCase) ? "Private"
            : string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled"
            : "DryRun";

    private async Task<string?> TryUploadThumbnailAsync(PublishRequest request, string videoId, string accessToken, string outputDirectory, CancellationToken cancellationToken)
    {
        if (!request.UploadThumbnail)
        {
            return null;
        }

        var diagnostics = new YouTubeThumbnailUploadDiagnostics
        {
            VideoId = videoId,
            ThumbnailPath = request.ThumbnailPath,
            FileExists = !string.IsNullOrWhiteSpace(request.ThumbnailPath) && File.Exists(request.ThumbnailPath),
            MimeType = GetMimeType(request.ThumbnailPath)
        };

        string? uploadPath = null;
        try
        {
            var validationError = await ValidateAndPrepareThumbnailAsync(request, diagnostics, outputDirectory, cancellationToken);
            if (validationError is not null)
            {
                diagnostics.Error = validationError;
                diagnostics.UploadStatus = "Skipped";
                await WriteThumbnailDiagnosticsAsync(outputDirectory, diagnostics, cancellationToken);
                _logger.LogWarning("Skipping YouTube thumbnail upload for {VideoId}: {Reason}", videoId, validationError);
                return validationError;
            }

            uploadPath = diagnostics.CompressedThumbnailPath ?? request.ThumbnailPath;
            diagnostics.MimeType = GetMimeType(uploadPath);
            await TransientRetryHelper.ExecuteAsync(
                async ct =>
                {
                    await _apiClient.UploadThumbnailAsync(videoId, uploadPath, accessToken, ct);
                    return true;
                },
                _youTubeOptions.UploadRetryAttempts,
                TimeSpan.FromSeconds(_youTubeOptions.RetryBaseDelaySeconds),
                TimeSpan.FromSeconds(_youTubeOptions.MaxRetryDelaySeconds),
                _logger,
                "YouTube thumbnail upload",
                videoId,
                cancellationToken);
            diagnostics.UploadStatus = "Completed";
            await WriteThumbnailDiagnosticsAsync(outputDirectory, diagnostics, cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            diagnostics.UploadStatus = GetUploadStatus(ex) ?? "Failed";
            diagnostics.Error = BuildThumbnailError(videoId, ex);
            if (YouTubeThumbnailUploadFailureClassifier.IsPermanentPermissionFailure(ex))
            {
                diagnostics.FailureCategory = YouTubeThumbnailUploadFailureClassifier.ThumbnailPermissionFailureCategory;
                diagnostics.RecommendedAction = YouTubeThumbnailUploadFailureClassifier.ThumbnailPermissionRecommendedAction;
            }

            LogThumbnailUploadDiagnostics(videoId, diagnostics.UploadStatus, ex);
            await WriteThumbnailDiagnosticsAsync(outputDirectory, diagnostics, cancellationToken);
            return $"Thumbnail upload failed: {diagnostics.Error}";
        }
    }

    private async Task<string?> ValidateAndPrepareThumbnailAsync(PublishRequest request, YouTubeThumbnailUploadDiagnostics diagnostics, string outputDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ThumbnailPath) || !File.Exists(request.ThumbnailPath))
        {
            return "Thumbnail file is missing.";
        }

        var extension = Path.GetExtension(request.ThumbnailPath);
        if (!IsSupportedThumbnailExtension(extension))
        {
            return "Thumbnail must be a PNG or JPG file.";
        }

        diagnostics.MimeType = GetMimeType(request.ThumbnailPath);
        diagnostics.FileSizeBytes = new FileInfo(request.ThumbnailPath).Length;

        try
        {
            var info = await Image.IdentifyAsync(request.ThumbnailPath, cancellationToken);
            if (info is null || info.Width <= 0 || info.Height <= 0)
            {
                return "Thumbnail image dimensions are invalid.";
            }

            diagnostics.Width = info.Width;
            diagnostics.Height = info.Height;
            LogThumbnailDimensionRecommendation(request, info.Width, info.Height);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skipping YouTube thumbnail upload because dimensions could not be read from {ThumbnailPath}.", request.ThumbnailPath);
            return "Thumbnail image dimensions are invalid.";
        }

        var maxThumbnailSizeBytes = GetMaxThumbnailSizeBytes();
        if (diagnostics.FileSizeBytes > maxThumbnailSizeBytes)
        {
            if (!_youTubeOptions.CompressThumbnailIfTooLarge)
            {
                return $"Thumbnail file is larger than {maxThumbnailSizeBytes} bytes.";
            }

            var compressedPath = await CompressThumbnailAsync(request.ThumbnailPath, outputDirectory, maxThumbnailSizeBytes, cancellationToken);
            diagnostics.CompressedThumbnailPath = compressedPath;
            diagnostics.FileSizeBytes = new FileInfo(compressedPath).Length;
            diagnostics.MimeType = GetMimeType(compressedPath);
            var compressedInfo = await Image.IdentifyAsync(compressedPath, cancellationToken);
            diagnostics.Width = compressedInfo?.Width;
            diagnostics.Height = compressedInfo?.Height;

            if (diagnostics.FileSizeBytes > maxThumbnailSizeBytes)
            {
                return $"Compressed thumbnail file is larger than {maxThumbnailSizeBytes} bytes.";
            }
        }

        return null;
    }

    private async Task<string> CompressThumbnailAsync(string thumbnailPath, string outputDirectory, long maxThumbnailSizeBytes, CancellationToken cancellationToken)
    {
        var compressedPath = Path.Combine(outputDirectory, $"youtube-thumbnail-compressed-{Path.GetFileNameWithoutExtension(thumbnailPath)}.jpg");
        var quality = Math.Clamp(_youTubeOptions.ThumbnailJpegQuality, 1, 100);

        using var image = await Image.LoadAsync(thumbnailPath, cancellationToken);
        await image.SaveAsJpegAsync(compressedPath, new JpegEncoder { Quality = quality }, cancellationToken);

        while (new FileInfo(compressedPath).Length > maxThumbnailSizeBytes && image.Width > 320 && image.Height > 180)
        {
            var nextWidth = Math.Max(320, (int)Math.Round(image.Width * 0.9));
            var nextHeight = Math.Max(180, (int)Math.Round(image.Height * 0.9));
            image.Mutate(x => x.Resize(nextWidth, nextHeight));
            await image.SaveAsJpegAsync(compressedPath, new JpegEncoder { Quality = quality }, cancellationToken);
        }

        _logger.LogInformation("Compressed YouTube thumbnail {ThumbnailPath} to {CompressedThumbnailPath} ({SizeBytes} bytes).", thumbnailPath, compressedPath, new FileInfo(compressedPath).Length);
        return compressedPath;
    }

    private void LogThumbnailDimensionRecommendation(PublishRequest request, int width, int height)
    {
        var recommendedWidth = request.IsShort ? 1080 : 1280;
        var recommendedHeight = request.IsShort ? 1920 : 720;
        if (width != recommendedWidth || height != recommendedHeight)
        {
            _logger.LogWarning(
                "YouTube {AssetType} thumbnail dimensions are {Width}x{Height}; recommended size is {RecommendedWidth}x{RecommendedHeight}.",
                request.IsShort ? "Shorts" : "long video",
                width,
                height,
                recommendedWidth,
                recommendedHeight);
        }
    }

    private void LogThumbnailUploadDiagnostics(string videoId, string? uploadStatus, Exception ex)
    {
        if (YouTubeThumbnailUploadFailureClassifier.IsPermanentPermissionFailure(ex))
        {
            _logger.LogWarning(
                "YouTube thumbnail upload skipped for video {VideoId} because the authenticated channel is not permitted to upload custom thumbnails. Status: {UploadStatus}. RecommendedAction: {RecommendedAction}",
                videoId,
                uploadStatus,
                YouTubeThumbnailUploadFailureClassifier.ThumbnailPermissionRecommendedAction);
            return;
        }

        _logger.LogWarning(ex, "YouTube thumbnail upload failed for video {VideoId}. Status: {UploadStatus}. Exception: {Exception}", videoId, uploadStatus, ex.ToString());
        if (ex is YouTubeThumbnailUploadException thumbnailException)
        {
            if (!string.IsNullOrWhiteSpace(thumbnailException.ResponseBody))
            {
                _logger.LogWarning("YouTube thumbnail upload response body for {VideoId}: {ResponseBody}", videoId, thumbnailException.ResponseBody);
            }

            if (!string.IsNullOrWhiteSpace(thumbnailException.HttpErrorDetails))
            {
                _logger.LogWarning("YouTube thumbnail upload HTTP error details for {VideoId}: {HttpErrorDetails}", videoId, thumbnailException.HttpErrorDetails);
            }

            if (thumbnailException.UploadException is not null)
            {
                _logger.LogWarning(thumbnailException.UploadException, "YouTube thumbnail upload progress exception for {VideoId}.", videoId);
            }
        }
    }

    private static string BuildThumbnailError(string videoId, Exception ex)
    {
        if (YouTubeThumbnailUploadFailureClassifier.IsPermanentPermissionFailure(ex))
        {
            return YouTubeThumbnailUploadFailureClassifier.BuildPermissionFailureMessage(videoId);
        }

        if (ex is YouTubeThumbnailUploadException thumbnailException)
        {
            var parts = new List<string> { thumbnailException.Message };
            if (thumbnailException.UploadException is not null)
            {
                parts.Add(thumbnailException.UploadException.Message);
            }
            if (!string.IsNullOrWhiteSpace(thumbnailException.HttpErrorDetails))
            {
                parts.Add(thumbnailException.HttpErrorDetails);
            }
            return string.Join(" | ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        return ex.Message;
    }

    private static string? GetUploadStatus(Exception ex)
        => ex is YouTubeThumbnailUploadException thumbnailException ? thumbnailException.UploadStatus : null;

    private long GetMaxThumbnailSizeBytes()
        => _youTubeOptions.MaxThumbnailSizeBytes > 0 ? _youTubeOptions.MaxThumbnailSizeBytes : 2 * 1024 * 1024;

    private static bool IsSupportedThumbnailExtension(string? extension)
        => extension is not null && (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));

    private static string? GetMimeType(string? path)
    {
        var extension = Path.GetExtension(path ?? string.Empty);
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return "image/jpeg";
        }

        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return "image/png";
        }

        return null;
    }

    private static Task WriteThumbnailDiagnosticsAsync(string outputDirectory, YouTubeThumbnailUploadDiagnostics diagnostics, CancellationToken cancellationToken)
        => WriteJsonAsync(Path.Combine(outputDirectory, "youtube-thumbnail-upload-diagnostics.json"), diagnostics, cancellationToken);

    private async Task<string> ResolveOutputDirectoryAsync(Guid pipelineRunId, CancellationToken cancellationToken)
    {
        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (run is null)
        {
            return _maintenanceOptions.WorkingDirectory;
        }

        if (!string.IsNullOrWhiteSpace(run.OutputFolder))
        {
            return run.OutputFolder;
        }

        var regionAwarePath = PipelineOrchestrator.BuildOutputDirectory(
            _maintenanceOptions.WorkingDirectory,
            run.ContentType,
            run.RunDate,
            run.RegionId,
            run.LocationName,
            run.Id);
        if (Directory.Exists(regionAwarePath))
        {
            return regionAwarePath;
        }

        return Path.Combine(_maintenanceOptions.WorkingDirectory, run.ContentType.ToString(), run.RunDate.ToString("yyyy-MM-dd"), run.Id.ToString("N"));
    }

    private static async Task WritePayloadAsync(string outputDirectory, PublishRequest request, string mode, CancellationToken cancellationToken)
        => await WriteJsonAsync(Path.Combine(outputDirectory, "youtube-publish-payload.json"), new
        {
            pipelineRunId = request.PipelineRunId,
            assetType = request.AssetType,
            isShort = request.IsShort,
            videoPath = request.VideoPath,
            thumbnailPath = request.ThumbnailPath,
            longThumbnailPath = request.LongThumbnailPath,
            shortThumbnailPath = request.ShortThumbnailPath,
            platformThumbnailPath = request.PlatformThumbnailPath,
            thumbnailSource = request.ThumbnailSource,
            title = request.Title,
            description = request.Description,
            tags = request.Tags,
            privacyStatus = request.PrivacyStatus,
            uploadThumbnail = request.UploadThumbnail,
            mode,
            generatedAtUtc = DateTime.UtcNow
        }, cancellationToken);

    private async Task WriteResultAndPersistAsync(string outputDirectory, Guid pipelineRunId, PublishRequest request, PublishResult result, CancellationToken cancellationToken)
    {
        var suffix = request.IsShort ? "short" : "long";
        await WriteJsonAsync(Path.Combine(outputDirectory, $"youtube-publish-result-{suffix}.json"), result, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "youtube-publish-result.json"), result, cancellationToken);
        if (!request.IsShort && result.Success && !string.Equals(result.Mode, "DryRun", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(result.VideoId))
        {
            var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
            if (run is not null)
            {
                run.YouTubeVideoId = result.VideoId;
                await _repository.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);
    }
}

internal sealed class YouTubeThumbnailUploadDiagnostics
{
    public string VideoId { get; init; } = string.Empty;
    public string ThumbnailPath { get; init; } = string.Empty;
    public bool FileExists { get; init; }
    public long? FileSizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? MimeType { get; set; }
    public string? UploadStatus { get; set; }
    public string? Error { get; set; }
    public string? CompressedThumbnailPath { get; set; }
    public string? FailureCategory { get; set; }
    public string? RecommendedAction { get; set; }
}
