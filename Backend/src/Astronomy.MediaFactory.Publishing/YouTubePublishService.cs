using System.Text.Json;
using Google;
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
                ContentKind = normalizedRequest.IsShort ? PlatformThumbnailContentTypes.ShortVideo : PlatformThumbnailContentTypes.LongVideo,
                AssetType = normalizedRequest.AssetType,
                IsShort = normalizedRequest.IsShort,
                UploadedThumbnailPath = normalizedRequest.PlatformThumbnailPath,
                ThumbnailSource = normalizedRequest.ThumbnailSource,
                VideoPathUsed = normalizedRequest.VideoPath,
                ThumbnailPathUsed = normalizedRequest.PlatformThumbnailPath,
                ThumbnailStrategy = normalizedRequest.ThumbnailSource,
                ThumbnailUploadAttempted = false,
                ThumbnailUploadSuccess = false,
                Mode = mode,
                PublishedUtc = DateTime.UtcNow
            };
            await WriteResultAndPersistAsync(outputDirectory, request.PipelineRunId, normalizedRequest, result, cancellationToken);
            await ThumbnailPublishDiagnosticsWriter.WriteFromPublishResultAsync(outputDirectory, result, cancellationToken);
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
            var thumbnailOutcome = await TryUploadThumbnailAsync(normalizedRequest, videoId, accessToken, outputDirectory, cancellationToken);
            var thumbnailUploadAttempted = thumbnailOutcome.UploadAttempted;
            var warnings = new List<string>();
            if (!string.IsNullOrWhiteSpace(thumbnailOutcome.FailureWarning))
            {
                warnings.Add("Video uploaded but thumbnail upload failed.");
            }
            if (!string.IsNullOrWhiteSpace(thumbnailOutcome.NonFailureWarning))
            {
                warnings.Add(thumbnailOutcome.NonFailureWarning);
            }
            var result = new PublishResult
            {
                Success = true,
                Platform = PlatformName,
                ContentType = normalizedRequest.IsShort ? PlatformThumbnailContentTypes.ShortVideo : PlatformThumbnailContentTypes.LongVideo,
                ContentKind = normalizedRequest.IsShort ? PlatformThumbnailContentTypes.ShortVideo : PlatformThumbnailContentTypes.LongVideo,
                AssetType = normalizedRequest.AssetType,
                IsShort = normalizedRequest.IsShort,
                UploadedThumbnailPath = normalizedRequest.PlatformThumbnailPath,
                ThumbnailSource = normalizedRequest.ThumbnailSource,
                VideoPathUsed = normalizedRequest.VideoPath,
                ThumbnailPathUsed = normalizedRequest.PlatformThumbnailPath,
                ThumbnailStrategy = normalizedRequest.ThumbnailSource,
                ThumbnailUploadAttempted = thumbnailUploadAttempted,
                ThumbnailUploadSuccess = thumbnailOutcome.UploadSuccess,
                ThumbnailWarning = thumbnailOutcome.Warning,
                VideoId = videoId,
                VideoUrl = $"https://www.youtube.com/watch?v={videoId}",
                Url = $"https://www.youtube.com/watch?v={videoId}",
                ChannelId = channel.ChannelId,
                ChannelTitle = channel.ChannelTitle,
                Error = thumbnailOutcome.FailureWarning,
                Warning = thumbnailOutcome.Warning,
                Warnings = warnings,
                Mode = mode,
                PublishedUtc = DateTime.UtcNow
            };
            await WriteResultAndPersistAsync(outputDirectory, request.PipelineRunId, normalizedRequest, result, cancellationToken);
            await ThumbnailPublishDiagnosticsWriter.WriteFromPublishResultAsync(outputDirectory, result, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            var result = new PublishResult
            {
                Success = false,
                Platform = PlatformName,
                ContentType = normalizedRequest.IsShort ? PlatformThumbnailContentTypes.ShortVideo : PlatformThumbnailContentTypes.LongVideo,
                ContentKind = normalizedRequest.IsShort ? PlatformThumbnailContentTypes.ShortVideo : PlatformThumbnailContentTypes.LongVideo,
                AssetType = normalizedRequest.AssetType,
                IsShort = normalizedRequest.IsShort,
                UploadedThumbnailPath = normalizedRequest.PlatformThumbnailPath,
                ThumbnailSource = normalizedRequest.ThumbnailSource,
                VideoPathUsed = normalizedRequest.VideoPath,
                ThumbnailPathUsed = normalizedRequest.PlatformThumbnailPath,
                ThumbnailStrategy = normalizedRequest.ThumbnailSource,
                Warning = ex.Message,
                ThumbnailWarning = ex.Message,
                Error = ex.Message,
                Mode = mode,
                PublishedUtc = DateTime.UtcNow
            };
            await WriteResultAndPersistAsync(outputDirectory, request.PipelineRunId, normalizedRequest, result, cancellationToken);
            await ThumbnailPublishDiagnosticsWriter.WriteFromPublishResultAsync(outputDirectory, result, cancellationToken);
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
            ? FirstNonBlank(request.PlatformThumbnailPath, request.ShortThumbnailPath, request.ThumbnailPath)
            : FirstNonBlank(request.PlatformThumbnailPath, request.LongThumbnailPath, request.ThumbnailPath);

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

    private async Task<ThumbnailUploadOutcome> TryUploadThumbnailAsync(PublishRequest request, string videoId, string accessToken, string outputDirectory, CancellationToken cancellationToken)
    {
        var diagnostics = new YouTubeThumbnailUploadDiagnostics
        {
            VideoId = videoId,
            IsShortVideo = request.IsShort,
            VideoPath = request.VideoPath,
            ContentKind = request.IsShort ? PlatformThumbnailContentTypes.ShortVideo : PlatformThumbnailContentTypes.LongVideo,
            UploadedThumbnailPath = request.PlatformThumbnailPath,
            ThumbnailSource = request.ThumbnailSource,
            ThumbnailPath = request.ThumbnailPath,
            FileExists = !string.IsNullOrWhiteSpace(request.ThumbnailPath) && File.Exists(request.ThumbnailPath),
            MimeType = GetMimeType(request.ThumbnailPath)
        };

        if (!request.UploadThumbnail)
        {
            diagnostics.UploadAttempted = false;
            diagnostics.UploadSuccess = false;
            diagnostics.UploadStatus = "Skipped";
            if (request.IsShort && !_youTubeOptions.UploadCustomThumbnailForShorts)
            {
                diagnostics.Reason = "YouTube Shorts custom thumbnail not uploaded by API/path missing/platform limitation";
                diagnostics.Warning = $"{diagnostics.Reason} (YouTube:UploadCustomThumbnailForShorts=false.)";
                await WriteThumbnailDiagnosticsAsync(outputDirectory, diagnostics, cancellationToken);
                _logger.LogWarning("{Warning}", diagnostics.Warning);
                return ThumbnailUploadOutcome.Skipped(diagnostics.Warning);
            }

            await WriteThumbnailDiagnosticsAsync(outputDirectory, diagnostics, cancellationToken);
            return ThumbnailUploadOutcome.Skipped(null);
        }

        string? uploadPath = null;
        try
        {
            var validationError = await ValidateAndPrepareThumbnailAsync(request, diagnostics, outputDirectory, cancellationToken);
            if (validationError is not null)
            {
                diagnostics.Error = validationError;
                if (request.IsShort)
                {
                    diagnostics.Reason = "YouTube Shorts custom thumbnail not uploaded by API/path missing/platform limitation";
                    diagnostics.Warning = diagnostics.Reason;
                }
                diagnostics.UploadAttempted = false;
                diagnostics.UploadSuccess = false;
                diagnostics.Warning = request.IsShort ? $"{diagnostics.Reason}: {validationError}" : validationError;
                diagnostics.UploadStatus = "Skipped";
                await WriteThumbnailDiagnosticsAsync(outputDirectory, diagnostics, cancellationToken);
                _logger.LogWarning("Skipping YouTube thumbnail upload for {VideoId}: {Reason}", videoId, validationError);
                return ThumbnailUploadOutcome.Failed(false, validationError);
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
            diagnostics.UploadAttempted = true;
            diagnostics.UploadSuccess = true;
            diagnostics.UploadStatus = "Completed";

            if (request.IsShort)
            {
                await PopulatePostUploadStatusAsync(videoId, accessToken, diagnostics, cancellationToken);
                diagnostics.ThumbnailUploadSuccess = true;
                diagnostics.ThumbnailWarning = "YouTube accepted custom thumbnail, but Shorts feed may display auto-selected frame depending on YouTube processing/UI behavior.";
            }

            await WriteThumbnailDiagnosticsAsync(outputDirectory, diagnostics, cancellationToken);
            return request.IsShort
                ? ThumbnailUploadOutcome.Succeeded(diagnostics.ThumbnailWarning)
                : ThumbnailUploadOutcome.Succeeded(null);
        }
        catch (Exception ex)
        {
            diagnostics.UploadStatus = GetUploadStatus(ex) ?? "Failed";
            diagnostics.Error = BuildThumbnailError(videoId, ex);
            diagnostics.GoogleError = diagnostics.Error;
            diagnostics.GoogleStatusCode = GetGoogleStatusCode(ex);
            diagnostics.GoogleErrorReason = GetGoogleErrorReason(ex);
            diagnostics.GoogleErrorMessage = GetGoogleErrorMessage(ex) ?? diagnostics.Error;
            diagnostics.UploadAttempted = true;
            diagnostics.UploadSuccess = false;
            if (YouTubeThumbnailUploadFailureClassifier.IsPermanentPermissionFailure(ex))
            {
                diagnostics.FailureCategory = YouTubeThumbnailUploadFailureClassifier.ThumbnailPermissionFailureCategory;
                diagnostics.RecommendedAction = YouTubeThumbnailUploadFailureClassifier.ThumbnailPermissionRecommendedAction;
                diagnostics.Warning = $"YouTube custom thumbnail upload failed: {diagnostics.Error}";
            }

            LogThumbnailUploadDiagnostics(videoId, diagnostics.UploadStatus, ex);
            await WriteThumbnailDiagnosticsAsync(outputDirectory, diagnostics, cancellationToken);
            return ThumbnailUploadOutcome.Failed(true, diagnostics.Warning ?? $"Thumbnail upload failed: {diagnostics.Error}");
        }
    }

    private async Task PopulatePostUploadStatusAsync(string videoId, string accessToken, YouTubeThumbnailUploadDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        try
        {
            var status = await _apiClient.GetVideoPostUploadStatusAsync(videoId, accessToken, cancellationToken);
            diagnostics.SnippetThumbnailsAfterUpload = status is null
                ? null
                : new
                {
                    Default = status.SnippetThumbnailDefault,
                    Medium = status.SnippetThumbnailMedium,
                    High = status.SnippetThumbnailHigh
                };
            diagnostics.SnippetThumbnailDefault = status?.SnippetThumbnailDefault;
            diagnostics.SnippetThumbnailMedium = status?.SnippetThumbnailMedium;
            diagnostics.SnippetThumbnailHigh = status?.SnippetThumbnailHigh;
            diagnostics.StatusUploadStatus = status?.UploadStatus;
            diagnostics.StatusPrivacyStatus = status?.PrivacyStatus;

            _logger.LogInformation(
                "YouTube post-thumbnail videos.list for {VideoId}: default={DefaultThumbnail}, medium={MediumThumbnail}, high={HighThumbnail}, uploadStatus={UploadStatus}, privacyStatus={PrivacyStatus}",
                videoId,
                status?.SnippetThumbnailDefault,
                status?.SnippetThumbnailMedium,
                status?.SnippetThumbnailHigh,
                status?.UploadStatus,
                status?.PrivacyStatus);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YouTube videos.list after thumbnail upload failed for {VideoId}.", videoId);
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

    private static int? GetGoogleStatusCode(Exception ex)
        => FindGoogleApiException(ex) is { } googleApiException ? (int)googleApiException.HttpStatusCode : null;

    private static string? GetGoogleErrorReason(Exception ex)
        => FindGoogleApiException(ex)?.Error?.Errors?.FirstOrDefault()?.Reason;

    private static string? GetGoogleErrorMessage(Exception ex)
        => FindGoogleApiException(ex)?.Error?.Message ?? FindGoogleApiException(ex)?.Message;

    private static GoogleApiException? FindGoogleApiException(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is GoogleApiException googleApiException)
            {
                return googleApiException;
            }

            if (current is YouTubeThumbnailUploadException thumbnailException && thumbnailException.UploadException is not null)
            {
                var uploadException = FindGoogleApiException(thumbnailException.UploadException);
                if (uploadException is not null)
                {
                    return uploadException;
                }
            }
        }

        return null;
    }

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

    private static async Task WriteThumbnailDiagnosticsAsync(string outputDirectory, YouTubeThumbnailUploadDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        var fileName = string.Equals(diagnostics.ContentKind, PlatformThumbnailContentTypes.ShortVideo, StringComparison.OrdinalIgnoreCase)
            ? "youtube-shorts-thumbnail-diagnostics.json"
            : "youtube-long-video-thumbnail-diagnostics.json";
        await WriteJsonAsync(Path.Combine(outputDirectory, fileName), diagnostics, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "youtube-thumbnail-upload-diagnostics.json"), diagnostics, cancellationToken);
        if (string.Equals(diagnostics.ContentKind, PlatformThumbnailContentTypes.ShortVideo, StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(Path.Combine(outputDirectory, "youtube-short-thumbnail-upload-diagnostics.json"), diagnostics, cancellationToken);
        }
    }

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

internal sealed record ThumbnailUploadOutcome(bool UploadAttempted, bool UploadSuccess, string? Warning, string? FailureWarning, string? NonFailureWarning)
{
    public static ThumbnailUploadOutcome Succeeded(string? warning) => new(true, true, warning, null, warning);
    public static ThumbnailUploadOutcome Failed(bool attempted, string warning) => new(attempted, false, warning, warning, null);
    public static ThumbnailUploadOutcome Skipped(string? warning) => new(false, false, warning, null, warning);
}

internal sealed class YouTubeThumbnailUploadDiagnostics
{
    public string VideoId { get; init; } = string.Empty;
    public bool IsShortVideo { get; init; }
    public string VideoPath { get; init; } = string.Empty;
    public string ContentKind { get; init; } = string.Empty;
    public string UploadedThumbnailPath { get; init; } = string.Empty;
    public string ThumbnailSource { get; init; } = ThumbnailSources.None;
    public string ThumbnailPath { get; init; } = string.Empty;
    public bool FileExists { get; init; }
    public long? FileSizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? MimeType { get; set; }
    public bool UploadAttempted { get; set; }
    public bool UploadSuccess { get; set; }
    public string? UploadStatus { get; set; }
    public string? GoogleError { get; set; }
    public int? GoogleStatusCode { get; set; }
    public string? GoogleErrorReason { get; set; }
    public string? GoogleErrorMessage { get; set; }
    public string? Warning { get; set; }
    public string? Error { get; set; }
    public string? CompressedThumbnailPath { get; set; }
    public string? FailureCategory { get; set; }
    public object? SnippetThumbnailsAfterUpload { get; set; }
    public object? SnippetThumbnailDefault { get; set; }
    public object? SnippetThumbnailMedium { get; set; }
    public object? SnippetThumbnailHigh { get; set; }
    public string? StatusUploadStatus { get; set; }
    public string? StatusPrivacyStatus { get; set; }
    public bool? ThumbnailUploadSuccess { get; set; }
    public string? ThumbnailWarning { get; set; }
    public string? RecommendedAction { get; set; }
    public string? Reason { get; set; }
}
