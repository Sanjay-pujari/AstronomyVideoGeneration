using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Astronomy.MediaFactory.Publishing;

public sealed class YouTubePublishingService : IYouTubePublishingService
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
        if (!File.Exists(videoPath))
        {
            _logger.LogWarning("Video file {VideoPath} not found. Skipping YouTube upload.", videoPath);
            return null;
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            _logger.LogWarning("YouTube client credentials are missing. Skipping upload.");
            return null;
        }

        var credential = await BuildCredentialAsync(cancellationToken);
        if (credential is null)
        {
            _logger.LogWarning("YouTube token details are missing. Configure YouTube:RefreshToken or YouTube:TokenFilePath.");
            return null;
        }

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

        if (insertRequest.GetProgress().Status != UploadStatus.Completed || insertRequest.ResponseBody?.Id is null)
        {
            _logger.LogError("YouTube upload did not complete successfully. Status: {Status}", insertRequest.GetProgress().Status);
            return null;
        }

        return insertRequest.ResponseBody.Id;
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
        await credential.RefreshTokenAsync(cancellationToken);
        return credential;
    }

    private sealed class TokenDocument
    {
        public string? access_token { get; set; }
        public string? refresh_token { get; set; }
    }
}
