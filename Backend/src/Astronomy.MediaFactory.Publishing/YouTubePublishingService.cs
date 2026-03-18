using System.Diagnostics;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Astronomy.MediaFactory.Publishing;

public sealed class YouTubePublishingService : IYouTubePublishingService, IYouTubeThumbnailPublisher
{
    private static readonly string[] Scopes = [YouTubeService.Scope.YoutubeUpload];

    private readonly YouTubeOptions _options;
    private readonly ILogger<YouTubePublishingService> _logger;

    public YouTubePublishingService(IOptions<YouTubeOptions> options, ILogger<YouTubePublishingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken)
    {
        var correlationId = Activity.Current?.Id ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        if (!File.Exists(videoPath))
        {
            _logger.LogWarning("Video file {VideoPath} not found. Skipping YouTube upload. CorrelationId: {CorrelationId}", videoPath, correlationId);
            return null;
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            _logger.LogWarning("YouTube client credentials are missing. Skipping upload. CorrelationId: {CorrelationId}", correlationId);
            return null;
        }

        var credential = await BuildCredentialAsync(cancellationToken);
        if (credential is null)
        {
            _logger.LogWarning("YouTube token details are missing. Configure YouTube:RefreshToken or YouTube:TokenFilePath. CorrelationId: {CorrelationId}", correlationId);
            return null;
        }

        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Astronomy update" : title.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? normalizedTitle : description.Trim();
        var normalizedTags = (tags ?? Array.Empty<string>())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToList();

        _logger.LogInformation("Starting YouTube upload for {VideoPath}. CorrelationId: {CorrelationId}", videoPath, correlationId);
        var videoId = await TransientRetryHelper.ExecuteAsync(
            ct => UploadCoreAsync(videoPath, normalizedTitle, normalizedDescription, normalizedTags, visibility, credential, ct),
            _options.UploadRetryAttempts,
            TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds),
            TimeSpan.FromSeconds(_options.MaxRetryDelaySeconds),
            _logger,
            "YouTube upload",
            Path.GetFileName(videoPath),
            cancellationToken);

        _logger.LogInformation("Finished YouTube upload for {VideoPath} with video id {VideoId}. CorrelationId: {CorrelationId}", videoPath, videoId, correlationId);
        return videoId;
    }

    public async Task<bool> UploadThumbnailAsync(string videoId, string thumbnailPath, CancellationToken cancellationToken)
    {
        var correlationId = Activity.Current?.Id ?? Activity.Current?.TraceId.ToString() ?? "n/a";
        if (string.IsNullOrWhiteSpace(videoId) || !File.Exists(thumbnailPath))
        {
            _logger.LogWarning("Skipping YouTube thumbnail upload because the video id or thumbnail file is missing. CorrelationId: {CorrelationId}", correlationId);
            return false;
        }

        var credential = await BuildCredentialAsync(cancellationToken);
        if (credential is null)
        {
            _logger.LogWarning("Skipping YouTube thumbnail upload because OAuth credentials are unavailable. CorrelationId: {CorrelationId}", correlationId);
            return false;
        }

        _logger.LogInformation("Starting YouTube thumbnail upload for {VideoId}. CorrelationId: {CorrelationId}", videoId, correlationId);
        var uploaded = await TransientRetryHelper.ExecuteAsync(
            ct => UploadThumbnailCoreAsync(videoId, thumbnailPath, credential, ct),
            _options.UploadRetryAttempts,
            TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds),
            TimeSpan.FromSeconds(_options.MaxRetryDelaySeconds),
            _logger,
            "YouTube thumbnail upload",
            videoId,
            cancellationToken);

        _logger.LogInformation("Finished YouTube thumbnail upload for {VideoId} with result {Result}. CorrelationId: {CorrelationId}", videoId, uploaded, correlationId);
        return uploaded;
    }

    private async Task<string> UploadCoreAsync(
        string videoPath,
        string title,
        string description,
        IReadOnlyCollection<string> tags,
        string visibility,
        UserCredential credential,
        CancellationToken cancellationToken)
    {
        var youtube = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName
        });

        await using var stream = File.OpenRead(videoPath);
        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = title,
                Description = description,
                Tags = tags.ToList()
            },
            Status = new VideoStatus
            {
                PrivacyStatus = string.IsNullOrWhiteSpace(visibility) ? _options.PrivacyStatus : visibility
            }
        };

        var insertRequest = youtube.Videos.Insert(video, "snippet,status", stream, "video/*");
        await insertRequest.UploadAsync(cancellationToken);

        if (insertRequest.GetProgress().Status != UploadStatus.Completed || string.IsNullOrWhiteSpace(insertRequest.ResponseBody?.Id))
        {
            throw new InvalidOperationException($"YouTube upload did not complete successfully. Status: {insertRequest.GetProgress().Status}");
        }

        return insertRequest.ResponseBody.Id;
    }

    private async Task<bool> UploadThumbnailCoreAsync(string videoId, string thumbnailPath, UserCredential credential, CancellationToken cancellationToken)
    {
        var youtube = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName
        });

        await using var stream = File.OpenRead(thumbnailPath);
        var mimeType = Path.GetExtension(thumbnailPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(thumbnailPath).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg"
            : "image/png";
        var setRequest = youtube.Thumbnails.Set(videoId, stream, mimeType);
        await setRequest.UploadAsync(cancellationToken);
        if (setRequest.GetProgress().Status != UploadStatus.Completed)
        {
            throw new InvalidOperationException($"YouTube thumbnail upload did not complete successfully. Status: {setRequest.GetProgress().Status}");
        }

        return true;
    }

    private async Task<UserCredential?> BuildCredentialAsync(CancellationToken cancellationToken)
    {
        var refreshToken = _options.RefreshToken;
        var accessToken = _options.AccessToken;

        if (string.IsNullOrWhiteSpace(refreshToken) && !string.IsNullOrWhiteSpace(_options.TokenFilePath) && File.Exists(_options.TokenFilePath))
        {
            var tokenContent = await File.ReadAllTextAsync(_options.TokenFilePath, cancellationToken);
            var token = JsonSerializer.Deserialize<TokenDocument>(tokenContent);
            refreshToken = token?.refresh_token;
            accessToken = token?.access_token;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            Scopes = Scopes,
            DataStore = new NullDataStore()
        });

        var tokenResponse = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
        {
            RefreshToken = refreshToken,
            AccessToken = accessToken
        };

        var credential = new UserCredential(flow, "astronomy-media-factory", tokenResponse);
        await TransientRetryHelper.ExecuteAsync(
            async ct =>
            {
                var refreshed = await credential.RefreshTokenAsync(ct);
                if (!refreshed)
                {
                    throw new InvalidOperationException("YouTube OAuth token refresh returned false.");
                }

                return true;
            },
            _options.UploadRetryAttempts,
            TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds),
            TimeSpan.FromSeconds(_options.MaxRetryDelaySeconds),
            _logger,
            "YouTube token refresh",
            _options.ApplicationName,
            cancellationToken);
        return credential;
    }

    private sealed class TokenDocument
    {
        public string? access_token { get; set; }
        public string? refresh_token { get; set; }
    }
}
