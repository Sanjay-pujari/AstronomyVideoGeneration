using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Astronomy.MediaFactory.Publishing;

public sealed class YouTubeAnalyticsService : IYouTubeAnalyticsService
{
    private static readonly string[] Scopes = [YouTubeService.Scope.YoutubeReadonly, "https://www.googleapis.com/auth/yt-analytics.readonly"];

    private readonly YouTubeOptions _options;
    private readonly ILogger<YouTubeAnalyticsService> _logger;

    public YouTubeAnalyticsService(IOptions<YouTubeOptions> options, ILogger<YouTubeAnalyticsService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<YouTubeVideoAnalyticsSnapshot?> GetVideoAnalyticsAsync(string videoId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            return null;

        var credential = await BuildCredentialAsync(cancellationToken);
        if (credential is null)
            return null;

        var youtube = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName
        });

        var request = youtube.Videos.List("statistics,contentDetails");
        request.Id = videoId;
        var response = await request.ExecuteAsync(cancellationToken);
        var item = response.Items?.FirstOrDefault();
        return item is null ? null : YouTubeAnalyticsParser.Parse(item);
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
        {
            _logger.LogWarning("YouTube token details are missing for analytics fetch.");
            return null;
        }

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = _options.ClientId, ClientSecret = _options.ClientSecret },
            Scopes = Scopes,
            DataStore = new NullDataStore()
        });

        var tokenResponse = new Google.Apis.Auth.OAuth2.Responses.TokenResponse { RefreshToken = refreshToken, AccessToken = accessToken };
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

public static class YouTubeAnalyticsParser
{
    public static YouTubeVideoAnalyticsSnapshot Parse(Video video)
    {
        return new YouTubeVideoAnalyticsSnapshot
        {
            VideoId = video.Id ?? string.Empty,
            Views = ToInt64(video.Statistics?.ViewCount),
            Likes = ToInt64(video.Statistics?.LikeCount),
            Comments = ToInt64(video.Statistics?.CommentCount),
            DurationSeconds = ParseIsoDuration(video.ContentDetails?.Duration)
        };
    }

    private static long ToInt64(ulong? value)
    {
        if (!value.HasValue)
            return 0;

        return value.Value > long.MaxValue ? long.MaxValue : (long)value.Value;
    }

    internal static int ParseIsoDuration(string? duration)
        => string.IsNullOrWhiteSpace(duration) ? 0 : (int)System.Xml.XmlConvert.ToTimeSpan(duration).TotalSeconds;
}
