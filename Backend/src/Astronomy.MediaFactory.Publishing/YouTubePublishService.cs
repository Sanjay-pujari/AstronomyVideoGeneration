using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class YouTubePublishService : IYouTubePublishService
{
    private const long MaxThumbnailBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
                Mode = mode,
                PublishedUtc = DateTime.UtcNow
            };
            await WriteResultAndPersistAsync(outputDirectory, request.PipelineRunId, result, cancellationToken);
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
            var error = await TryUploadThumbnailAsync(normalizedRequest, videoId, accessToken, cancellationToken);
            var result = new PublishResult
            {
                Success = true,
                Platform = PlatformName,
                VideoId = videoId,
                VideoUrl = $"https://www.youtube.com/watch?v={videoId}",
                ChannelId = channel.ChannelId,
                ChannelTitle = channel.ChannelTitle,
                Error = error,
                Mode = mode,
                PublishedUtc = DateTime.UtcNow
            };
            await WriteResultAndPersistAsync(outputDirectory, request.PipelineRunId, result, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            var result = new PublishResult
            {
                Success = false,
                Platform = PlatformName,
                Error = ex.Message,
                Mode = mode,
                PublishedUtc = DateTime.UtcNow
            };
            await WriteResultAndPersistAsync(outputDirectory, request.PipelineRunId, result, cancellationToken);
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

        return new PublishRequest
        {
            PipelineRunId = request.PipelineRunId,
            Platform = PlatformName,
            VideoPath = request.VideoPath,
            ThumbnailPath = request.ThumbnailPath,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Astronomy update" : request.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? request.Title.Trim() : request.Description.Trim(),
            Tags = request.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList(),
            PrivacyStatus = privacyStatus,
            UploadThumbnail = request.UploadThumbnail
        };
    }

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

    private async Task<string?> TryUploadThumbnailAsync(PublishRequest request, string videoId, string accessToken, CancellationToken cancellationToken)
    {
        if (!request.UploadThumbnail)
        {
            return null;
        }

        var validationError = ValidateThumbnail(request.ThumbnailPath);
        if (validationError is not null)
        {
            _logger.LogWarning("Skipping YouTube thumbnail upload for {VideoId}: {Reason}", videoId, validationError);
            return validationError;
        }

        try
        {
            await TransientRetryHelper.ExecuteAsync(
                async ct =>
                {
                    await _apiClient.UploadThumbnailAsync(videoId, request.ThumbnailPath, accessToken, ct);
                    return true;
                },
                _youTubeOptions.UploadRetryAttempts,
                TimeSpan.FromSeconds(_youTubeOptions.RetryBaseDelaySeconds),
                TimeSpan.FromSeconds(_youTubeOptions.MaxRetryDelaySeconds),
                _logger,
                "YouTube thumbnail upload",
                videoId,
                cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YouTube thumbnail upload failed for video {VideoId}.", videoId);
            return $"Thumbnail upload failed: {ex.Message}";
        }
    }

    private static string? ValidateThumbnail(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return "Thumbnail file is missing.";
        }

        var extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return "Thumbnail must be a PNG or JPG file.";
        }

        var length = new FileInfo(path).Length;
        return length > MaxThumbnailBytes ? "Thumbnail file is larger than 2MB." : null;
    }

    private async Task<string> ResolveOutputDirectoryAsync(Guid pipelineRunId, CancellationToken cancellationToken)
    {
        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (run is null)
        {
            return _maintenanceOptions.WorkingDirectory;
        }

        return Path.Combine(_maintenanceOptions.WorkingDirectory, run.ContentType.ToString(), run.RunDate.ToString("yyyy-MM-dd"), run.Id.ToString("N"));
    }

    private static async Task WritePayloadAsync(string outputDirectory, PublishRequest request, string mode, CancellationToken cancellationToken)
        => await WriteJsonAsync(Path.Combine(outputDirectory, "youtube-publish-payload.json"), new
        {
            pipelineRunId = request.PipelineRunId,
            videoPath = request.VideoPath,
            thumbnailPath = request.ThumbnailPath,
            title = request.Title,
            description = request.Description,
            tags = request.Tags,
            privacyStatus = request.PrivacyStatus,
            uploadThumbnail = request.UploadThumbnail,
            mode,
            generatedAtUtc = DateTime.UtcNow
        }, cancellationToken);

    private async Task WriteResultAndPersistAsync(string outputDirectory, Guid pipelineRunId, PublishResult result, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(Path.Combine(outputDirectory, "youtube-publish-result.json"), result, cancellationToken);
        if (result.Success && !string.Equals(result.Mode, "DryRun", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(result.VideoId))
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
