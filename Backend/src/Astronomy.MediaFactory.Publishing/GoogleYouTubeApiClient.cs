using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Google;

namespace Astronomy.MediaFactory.Publishing;

public sealed class GoogleYouTubeApiClient : IYouTubeApiClient
{
    private readonly YouTubeOptions _options;

    public GoogleYouTubeApiClient(IOptions<YouTubeOptions> options)
    {
        _options = options.Value;
    }

    public async Task<YouTubeChannelInfo> GetAuthenticatedChannelAsync(string accessToken, CancellationToken cancellationToken)
    {
        var youtube = CreateService(accessToken);
        var request = youtube.Channels.List("snippet");
        request.Mine = true;
        var response = await request.ExecuteAsync(cancellationToken);
        var channel = response.Items?.FirstOrDefault();
        if (channel is null || string.IsNullOrWhiteSpace(channel.Id))
        {
            throw new InvalidOperationException("No YouTube channel found for authenticated account.");
        }

        return new YouTubeChannelInfo
        {
            ChannelId = channel.Id,
            ChannelTitle = channel.Snippet?.Title ?? string.Empty
        };
    }

    public async Task<string> UploadVideoAsync(PublishRequest request, string accessToken, CancellationToken cancellationToken)
    {
        var youtube = CreateService(accessToken);
        await using var stream = File.OpenRead(request.VideoPath);
        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = request.Title,
                Description = request.Description,
                Tags = request.Tags,
                CategoryId = string.IsNullOrWhiteSpace(_options.CategoryId) ? "28" : _options.CategoryId
            },
            Status = new VideoStatus
            {
                PrivacyStatus = string.IsNullOrWhiteSpace(request.PrivacyStatus) ? "private" : request.PrivacyStatus,
                SelfDeclaredMadeForKids = false
            }
        };

        var insert = youtube.Videos.Insert(video, "snippet,status", stream, "video/*");
        await insert.UploadAsync(cancellationToken);
        if (insert.GetProgress().Status != UploadStatus.Completed || string.IsNullOrWhiteSpace(insert.ResponseBody?.Id))
        {
            throw new InvalidOperationException($"YouTube upload did not complete successfully. Status: {insert.GetProgress().Status}");
        }

        return insert.ResponseBody.Id;
    }

    public async Task UploadThumbnailAsync(string videoId, string thumbnailPath, string accessToken, CancellationToken cancellationToken)
    {
        var youtube = CreateService(accessToken);
        await using var stream = File.OpenRead(thumbnailPath);
        var mimeType = Path.GetExtension(thumbnailPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(thumbnailPath).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
                : "image/png";
        var upload = youtube.Thumbnails.Set(videoId, stream, mimeType);
        try
        {
            await upload.UploadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ThrowThumbnailUploadException(upload, ex);
        }

        var progress = upload.GetProgress();
        if (progress.Status != UploadStatus.Completed)
        {
            ThrowThumbnailUploadException(upload, progress.Exception);
        }
    }


    public async Task<YouTubeVideoPostUploadStatus?> GetVideoPostUploadStatusAsync(string videoId, string accessToken, CancellationToken cancellationToken)
    {
        var youtube = CreateService(accessToken);
        var request = youtube.Videos.List("snippet,status");
        request.Id = videoId;
        var response = await request.ExecuteAsync(cancellationToken);
        var video = response.Items?.FirstOrDefault();
        if (video is null)
        {
            return null;
        }

        return new YouTubeVideoPostUploadStatus
        {
            SnippetThumbnailDefault = video.Snippet?.Thumbnails?.Default__,
            SnippetThumbnailMedium = video.Snippet?.Thumbnails?.Medium,
            SnippetThumbnailHigh = video.Snippet?.Thumbnails?.High,
            UploadStatus = video.Status?.UploadStatus,
            PrivacyStatus = video.Status?.PrivacyStatus
        };
    }

    private static void ThrowThumbnailUploadException(ThumbnailsResource.SetMediaUpload upload, Exception? exception)
    {
        var progress = upload.GetProgress();
        var responseBody = upload.ResponseBody is null ? null : JsonSerializer.Serialize(upload.ResponseBody);
        var httpErrorDetails = BuildHttpErrorDetails(exception ?? progress.Exception);
        throw new YouTubeThumbnailUploadException(
            $"YouTube thumbnail upload did not complete successfully. Status: {progress.Status}",
            progress.Status.ToString(),
            exception ?? progress.Exception,
            responseBody,
            httpErrorDetails);
    }

    private static string? BuildHttpErrorDetails(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        if (exception is GoogleApiException googleApiException)
        {
            var error = googleApiException.Error is null ? null : JsonSerializer.Serialize(googleApiException.Error);
            return $"StatusCode={(int)googleApiException.HttpStatusCode} ({googleApiException.HttpStatusCode}); Error={error}; Message={googleApiException.Message}";
        }

        return exception.Message;
    }

    private YouTubeService CreateService(string accessToken)
        => new(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
            ApplicationName = _options.ApplicationName
        });
}
