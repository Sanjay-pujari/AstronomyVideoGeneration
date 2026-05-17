using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class FacebookVideoPublishService : IFacebookVideoPublishService
{
    private const string GraphEndpoint = "https://graph.facebook.com/v23.0";
    private const string CustomThumbnailUnsupportedWarning = "Facebook full video endpoint did not accept custom thumbnail; platform may auto-select frame.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly HttpClient _httpClient;
    private readonly MetaOptions _metaOptions;
    private readonly MetaPublishingOptions _publishingOptions;
    private readonly ILogger<FacebookVideoPublishService> _logger;

    public FacebookVideoPublishService(HttpClient httpClient, IOptions<MetaOptions> metaOptions, IOptions<MetaPublishingOptions> publishingOptions, ILogger<FacebookVideoPublishService> logger)
    {
        _httpClient = httpClient;
        _metaOptions = metaOptions.Value;
        _publishingOptions = publishingOptions.Value;
        _logger = logger;
    }

    public async Task<MetaPublishResult> PublishVideoAsync(MetaPublishRequest request, CancellationToken cancellationToken)
    {
        var mode = NormalizeMode(_publishingOptions.Mode);
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(request.VideoPath)) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        try
        {
            var token = await LoadTokenAsync(cancellationToken);
            var pageId = string.IsNullOrWhiteSpace(_publishingOptions.FacebookPageId) ? token.FacebookPageId : _publishingOptions.FacebookPageId.Trim();
            await WritePayloadAsync(outputDirectory, pageId, token.FacebookPageName, request, mode, cancellationToken);

            if (mode == "Disabled")
            {
                return await WriteResultAsync(outputDirectory, Failed(mode, "Facebook full video publishing is disabled.", request), cancellationToken);
            }

            if (!File.Exists(request.VideoPath))
            {
                return await WriteResultAsync(outputDirectory, Failed(mode, $"Facebook full video is missing: {request.VideoPath}", request), cancellationToken);
            }

            var thumbnailExists = !string.IsNullOrWhiteSpace(request.PlatformThumbnailPath) && File.Exists(request.PlatformThumbnailPath);
            if (mode == "DryRun")
            {
                return await WriteResultAsync(outputDirectory, new MetaPublishResult
                {
                    Success = true,
                    Platform = "Facebook",
                    ContentType = PlatformThumbnailContentTypes.LongVideo,
                    ContentKind = PlatformThumbnailContentTypes.LongVideo,
                    Mode = mode,
                    UploadedThumbnailPath = request.PlatformThumbnailPath,
                    ThumbnailSource = request.ThumbnailSource,
                    ThumbnailUploadAttempted = thumbnailExists,
                    ThumbnailUploadSuccess = thumbnailExists,
                    ThumbnailWarning = thumbnailExists ? null : "Facebook full video thumbnail file is missing; platform may auto-select frame.",
                    Warning = thumbnailExists ? null : "Facebook full video thumbnail file is missing; platform may auto-select frame.",
                    Warnings = thumbnailExists ? [] : ["Facebook full video thumbnail file is missing; platform may auto-select frame."],
                    VideoPathUsed = request.VideoPath,
                    ThumbnailPathUsed = request.PlatformThumbnailPath,
                    ThumbnailStrategy = request.ThumbnailSource,
                    PublishedUtc = DateTime.UtcNow
                }, cancellationToken);
            }

            var result = await PublishRealAsync(pageId, token.FacebookPageName, token.FacebookPageAccessToken, request, thumbnailExists, mode, cancellationToken);
            return await WriteResultAsync(outputDirectory, result, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or JsonException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Facebook full video publish failed for pipeline run {PipelineRunId}.", request.PipelineRunId);
            return await WriteResultAsync(outputDirectory, Failed(mode, ex.Message, request), cancellationToken);
        }
    }

    private async Task<MetaPublishResult> PublishRealAsync(string pageId, string pageName, string pageAccessToken, MetaPublishRequest request, bool thumbnailExists, string mode, CancellationToken cancellationToken)
    {
        await using var video = File.OpenRead(request.VideoPath);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(pageAccessToken), "access_token" },
            { new StringContent(request.Caption), "description" },
            { new StringContent(string.IsNullOrWhiteSpace(request.ShortTitle) ? "Astronomy video" : request.ShortTitle), "title" }
        };
        var videoContent = new StreamContent(video);
        videoContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(videoContent, "source", Path.GetFileName(request.VideoPath));

        if (thumbnailExists)
        {
            await using var thumb = File.OpenRead(request.PlatformThumbnailPath);
            var thumbContent = new StreamContent(thumb);
            thumbContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(thumbContent, "thumb", Path.GetFileName(request.PlatformThumbnailPath));
            using var response = await _httpClient.PostAsync($"{GraphEndpoint}/{Uri.EscapeDataString(pageId)}/videos", form, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<FacebookVideoResponse>(JsonOptions, cancellationToken) ?? new FacebookVideoResponse();
                return Success(mode, body.Id, pageId, pageName, request, thumbnailAttempted: true, thumbnailSuccess: true, warning: null);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Facebook full video upload with custom thumbnail failed with status {StatusCode}. Retrying without thumbnail. Response: {Response}", (int)response.StatusCode, error);
        }

        video.Position = 0;
        using var retryForm = new MultipartFormDataContent
        {
            { new StringContent(pageAccessToken), "access_token" },
            { new StringContent(request.Caption), "description" },
            { new StringContent(string.IsNullOrWhiteSpace(request.ShortTitle) ? "Astronomy video" : request.ShortTitle), "title" }
        };
        var retryVideoContent = new StreamContent(video);
        retryVideoContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        retryForm.Add(retryVideoContent, "source", Path.GetFileName(request.VideoPath));
        using var retryResponse = await _httpClient.PostAsync($"{GraphEndpoint}/{Uri.EscapeDataString(pageId)}/videos", retryForm, cancellationToken);
        if (!retryResponse.IsSuccessStatusCode)
        {
            var error = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Facebook full video upload failed with status {(int)retryResponse.StatusCode}. {error}");
        }

        var retryBody = await retryResponse.Content.ReadFromJsonAsync<FacebookVideoResponse>(JsonOptions, cancellationToken) ?? new FacebookVideoResponse();
        return Success(mode, retryBody.Id, pageId, pageName, request, thumbnailExists, false, thumbnailExists ? CustomThumbnailUnsupportedWarning : null);
    }

    private static MetaPublishResult Success(string mode, string? videoId, string pageId, string pageName, MetaPublishRequest request, bool thumbnailAttempted, bool thumbnailSuccess, string? warning)
        => new()
        {
            Success = true,
            Platform = "Facebook",
            ContentType = PlatformThumbnailContentTypes.LongVideo,
            ContentKind = PlatformThumbnailContentTypes.LongVideo,
            Mode = mode,
            UploadedThumbnailPath = request.PlatformThumbnailPath,
            ThumbnailSource = request.ThumbnailSource,
            ThumbnailUploadAttempted = thumbnailAttempted,
            ThumbnailUploadSuccess = thumbnailSuccess,
            ThumbnailWarning = warning,
            VideoId = videoId,
            PostId = videoId,
            Url = string.IsNullOrWhiteSpace(videoId) ? null : $"https://www.facebook.com/{pageId}/videos/{videoId}/",
            Warning = warning,
            Warnings = string.IsNullOrWhiteSpace(warning) ? [] : [warning],
            VideoPathUsed = request.VideoPath,
            ThumbnailPathUsed = request.PlatformThumbnailPath,
            ThumbnailStrategy = request.ThumbnailSource,
            PublishedUtc = DateTime.UtcNow
        };

    private static MetaPublishResult Failed(string mode, string error, MetaPublishRequest request)
        => new()
        {
            Success = false,
            Platform = "Facebook",
            ContentType = PlatformThumbnailContentTypes.LongVideo,
            ContentKind = PlatformThumbnailContentTypes.LongVideo,
            Mode = mode,
            Error = error,
            ThumbnailWarning = error,
            Warning = error,
            UploadedThumbnailPath = request.PlatformThumbnailPath,
            ThumbnailSource = request.ThumbnailSource,
            VideoPathUsed = request.VideoPath,
            ThumbnailPathUsed = request.PlatformThumbnailPath,
            ThumbnailStrategy = request.ThumbnailSource,
            PublishedUtc = DateTime.UtcNow
        };

    private async Task<MetaOAuthTokenFile> LoadTokenAsync(CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(_metaOptions.TokenFilePath) ? Path.Combine(AppContext.BaseDirectory, "meta-oauth-token.json") : Path.GetFullPath(_metaOptions.TokenFilePath);
        if (!File.Exists(path)) throw new InvalidOperationException(FacebookReelPublishService.IncompleteTokenMessage);
        var token = JsonSerializer.Deserialize<MetaOAuthTokenFile>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
        if (token is null || string.IsNullOrWhiteSpace(token.FacebookPageId) || string.IsNullOrWhiteSpace(token.FacebookPageAccessToken) || string.IsNullOrWhiteSpace(token.FacebookPageName))
        {
            throw new InvalidOperationException(FacebookReelPublishService.IncompleteTokenMessage);
        }
        return token;
    }

    private static Task WritePayloadAsync(string outputDirectory, string pageId, string pageName, MetaPublishRequest request, string mode, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(outputDirectory, "facebook-long-publish-payload.json"), JsonSerializer.Serialize(new { pageId, pageName, videoPath = request.VideoPath, thumbnailPath = request.PlatformThumbnailPath, thumbnailSource = request.ThumbnailSource, caption = request.Caption, title = request.ShortTitle, mode, generatedAtUtc = DateTime.UtcNow }, JsonOptions), cancellationToken);

    private static async Task<MetaPublishResult> WriteResultAsync(string outputDirectory, MetaPublishResult result, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "facebook-long-publish-result.json"), JsonSerializer.Serialize(result, JsonOptions), cancellationToken);
        return result;
    }

    private static string NormalizeMode(string? mode)
        => string.Equals(mode, "Public", StringComparison.OrdinalIgnoreCase) ? "Public"
            : string.Equals(mode, "Private", StringComparison.OrdinalIgnoreCase) ? "Private"
            : string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled"
            : "DryRun";

    private sealed class FacebookVideoResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }
}
