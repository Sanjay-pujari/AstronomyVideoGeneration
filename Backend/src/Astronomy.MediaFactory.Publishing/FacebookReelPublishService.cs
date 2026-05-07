using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class FacebookReelPublishService : IFacebookReelPublishService
{
    public const string IncompleteTokenMessage = "Meta OAuth token file is missing or incomplete. Run /api/metaoauth/start first.";
    public const string MetaAppDevelopmentModeWarning = "Meta app may be in Development mode. Content created by apps in development may not be publicly visible to users outside app roles. Move app to Live mode and complete App Review for required permissions.";
    private const string GraphEndpoint = "https://graph.facebook.com/v23.0";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly HttpClient _httpClient;
    private readonly MetaOptions _metaOptions;
    private readonly MetaPublishingOptions _publishingOptions;
    private readonly RenderingOptions _renderingOptions;
    private readonly ILogger<FacebookReelPublishService> _logger;

    public FacebookReelPublishService(
        HttpClient httpClient,
        IOptions<MetaOptions> metaOptions,
        IOptions<MetaPublishingOptions> publishingOptions,
        IOptions<RenderingOptions> renderingOptions,
        ILogger<FacebookReelPublishService> logger)
    {
        _httpClient = httpClient;
        _metaOptions = metaOptions.Value;
        _publishingOptions = publishingOptions.Value;
        _renderingOptions = renderingOptions.Value;
        _logger = logger;
    }

    public async Task<MetaPublishResult> PublishReelAsync(MetaPublishRequest request, CancellationToken cancellationToken)
    {
        var mode = NormalizeMode(_publishingOptions.Mode);
        var outputDirectory = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(request.VideoPath))!) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        MetaPublishResult result;
        FacebookReelPublishDiagnostics? diagnostics = null;
        try
        {
            var token = await LoadTokenAsync(cancellationToken);
            var pageId = string.IsNullOrWhiteSpace(_publishingOptions.FacebookPageId) ? token.FacebookPageId : _publishingOptions.FacebookPageId.Trim();
            await WritePayloadAsync(outputDirectory, pageId, token.FacebookPageName, request, mode, cancellationToken);

            if (mode == "Disabled")
            {
                result = Failed(mode, "Facebook Reel publishing is disabled.");
            }
            else if (mode == "DryRun")
            {
                result = new MetaPublishResult { Success = true, Platform = "Facebook", Mode = mode, PublishedUtc = DateTime.UtcNow };
            }
            else
            {
                var validationError = await ValidateVideoAsync(request.VideoPath, cancellationToken);
                if (validationError is not null)
                {
                    result = Failed(mode, validationError);
                }
                else
                {
                    var publishResult = await PublishRealAsync(pageId, token.FacebookPageName, token.FacebookPageAccessToken, request, mode, cancellationToken);
                    result = publishResult.Result;
                    diagnostics = publishResult.Diagnostics;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or JsonException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Facebook Reel publish failed for pipeline run {PipelineRunId}.", request.PipelineRunId);
            result = Failed(mode, ex.Message);
        }

        await WriteResultAsync(outputDirectory, diagnostics, result, cancellationToken);
        return result;
    }

    private async Task<(MetaPublishResult Result, FacebookReelPublishDiagnostics Diagnostics)> PublishRealAsync(string pageId, string pageName, string pageAccessToken, MetaPublishRequest request, string mode, CancellationToken cancellationToken)
    {
        var start = await PostGraphAsync<StartUploadResponse>(
            $"{GraphEndpoint}/{Uri.EscapeDataString(pageId)}/video_reels",
            new Dictionary<string, string>
            {
                ["upload_phase"] = "start",
                ["access_token"] = pageAccessToken
            },
            "Facebook Reel upload session start",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(start.VideoId) || string.IsNullOrWhiteSpace(start.UploadUrl))
        {
            throw new InvalidOperationException("Facebook Reel upload session did not return video_id and upload_url.");
        }

        var uploadStatus = await UploadBinaryAsync(start.UploadUrl, request.VideoPath, pageAccessToken, cancellationToken);

        var finish = await PostGraphAsync<FinishUploadResponse>(
            $"{GraphEndpoint}/{Uri.EscapeDataString(pageId)}/video_reels",
            new Dictionary<string, string>
            {
                ["upload_phase"] = "finish",
                ["video_id"] = start.VideoId,
                ["video_state"] = "PUBLISHED",
                ["description"] = request.Caption,
                ["title"] = string.IsNullOrWhiteSpace(request.ShortTitle) ? BuildFallbackTitle(request.Caption) : request.ShortTitle,
                ["access_token"] = pageAccessToken
            },
            "Facebook Reel publish finish",
            cancellationToken);

        var warnings = new List<string>();
        var verification = await VerifyPublishedAsync(pageId, pageAccessToken, start.VideoId, cancellationToken);
        if (!verification.PublishedVerified)
        {
            warnings.Add("Facebook Reel upload finished, but public Page visibility could not be verified before the processing timeout.");
            warnings.Add(MetaAppDevelopmentModeWarning);
        }

        var postId = FirstNonBlank(finish.PostId, finish.Id);
        var permalinkUrl = FirstNonBlank(verification.PermalinkUrl, string.IsNullOrWhiteSpace(postId) ? null : $"https://www.facebook.com/{postId}");
        var diagnostics = new FacebookReelPublishDiagnostics
        {
            StartResponse = start,
            UploadResponseStatus = uploadStatus,
            FinishResponse = finish,
            VideoId = start.VideoId,
            PostId = postId,
            PermalinkUrl = permalinkUrl,
            ProcessingStatus = verification.ProcessingStatus,
            PageId = pageId,
            PageName = pageName,
            PublishedVerified = verification.PublishedVerified,
            Warnings = warnings
        };

        return (new MetaPublishResult
        {
            Success = true,
            Platform = "Facebook",
            Mode = mode,
            VideoId = start.VideoId,
            PostId = postId,
            Url = permalinkUrl,
            PublishedVerified = verification.PublishedVerified,
            Warning = warnings.FirstOrDefault(),
            Warnings = warnings,
            PublishedUtc = DateTime.UtcNow
        }, diagnostics);
    }

    private async Task<int> UploadBinaryAsync(string uploadUrl, string videoPath, string pageAccessToken, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(videoPath);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var message = new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = content };
        message.Headers.TryAddWithoutValidation("Authorization", $"OAuth {pageAccessToken}");
        message.Headers.TryAddWithoutValidation("offset", "0");
        message.Headers.TryAddWithoutValidation("file_size", stream.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var suffix = string.IsNullOrWhiteSpace(errorBody) ? string.Empty : $" Response: {errorBody}";
            throw new InvalidOperationException($"Facebook Reel binary upload failed with status {(int)response.StatusCode}.{suffix}");
        }

        return (int)response.StatusCode;
    }

    private async Task<FacebookReelVerificationResult> VerifyPublishedAsync(string pageId, string pageAccessToken, string videoId, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _publishingOptions.FacebookReelProcessingMaxAttempts);
        var pollDelay = TimeSpan.FromSeconds(Math.Max(0, _publishingOptions.FacebookReelProcessingPollSeconds));
        JsonObject? latestVideo = null;
        JsonObject? latestReels = null;
        string? latestStatus = null;
        string? permalink = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (attempt > 1 && pollDelay > TimeSpan.Zero)
            {
                await Task.Delay(pollDelay, cancellationToken);
            }

            latestVideo = await TryGetGraphObjectAsync($"{GraphEndpoint}/{Uri.EscapeDataString(videoId)}?fields=id,permalink_url,created_time,description,status&access_token={Uri.EscapeDataString(pageAccessToken)}", cancellationToken);
            latestReels = await TryGetGraphObjectAsync($"{GraphEndpoint}/{Uri.EscapeDataString(pageId)}/video_reels?fields=id,permalink_url,created_time,description,status&limit=10&access_token={Uri.EscapeDataString(pageAccessToken)}", cancellationToken);
            latestStatus = FirstNonBlank(ExtractStatusText(latestVideo), ExtractStatusText(latestReels));
            permalink = FirstNonBlank(ExtractString(latestVideo, "permalink_url"), ExtractReelPermalink(latestReels, videoId));

            _logger.LogInformation("Facebook Reel processing poll {Attempt}/{MaxAttempts} for video {VideoId}: status={Status}, permalink={Permalink}.", attempt, maxAttempts, videoId, latestStatus ?? "unknown", permalink ?? "none");

            if (IsPublished(latestStatus) || (!_publishingOptions.RequirePublishedState && !string.IsNullOrWhiteSpace(permalink)))
            {
                return new FacebookReelVerificationResult(true, latestStatus, permalink);
            }
        }

        return new FacebookReelVerificationResult(false, latestStatus ?? ExtractStatusText(latestVideo) ?? ExtractStatusText(latestReels), permalink);
    }

    private async Task<JsonObject?> TryGetGraphObjectAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Facebook Reel verification GET failed with status {StatusCode} for {Url}.", (int)response.StatusCode, RedactAccessToken(url));
                return null;
            }

            return await response.Content.ReadFromJsonAsync<JsonObject>(JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Facebook Reel verification GET failed for {Url}.", RedactAccessToken(url));
            return null;
        }
    }

    private async Task<T> PostGraphAsync<T>(string url, Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{operation} failed with status {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException($"{operation} returned an empty response.");
    }

    private async Task<string?> ValidateVideoAsync(string videoPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(videoPath))
        {
            return "Facebook Reel video is missing: shorts/short-video.mp4.";
        }

        var fileInfo = new FileInfo(videoPath);
        if (fileInfo.Length <= 0)
        {
            return "Facebook Reel video is empty: shorts/short-video.mp4.";
        }

        var validation = await YouTubeShortsValidation.ValidateBeforeUploadAsync(videoPath, _renderingOptions.FfprobePath, cancellationToken);
        if (validation.Warnings.Count > 0)
        {
            _logger.LogWarning("Facebook Reel preferred-format validation warnings for {VideoPath}: {Warnings}", videoPath, string.Join("; ", validation.Warnings));
        }

        return null;
    }

    private async Task<MetaOAuthTokenFile> LoadTokenAsync(CancellationToken cancellationToken)
    {
        var path = ResolveTokenFilePath();
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(IncompleteTokenMessage);
        }

        var token = JsonSerializer.Deserialize<MetaOAuthTokenFile>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
        if (token is null
            || string.IsNullOrWhiteSpace(token.FacebookPageId)
            || string.IsNullOrWhiteSpace(token.FacebookPageAccessToken)
            || string.IsNullOrWhiteSpace(token.FacebookPageName))
        {
            throw new InvalidOperationException(IncompleteTokenMessage);
        }

        return token;
    }

    private string ResolveTokenFilePath()
        => string.IsNullOrWhiteSpace(_metaOptions.TokenFilePath)
            ? Path.Combine(AppContext.BaseDirectory, "meta-oauth-token.json")
            : Path.GetFullPath(_metaOptions.TokenFilePath);

    private static async Task WritePayloadAsync(string outputDirectory, string pageId, string pageName, MetaPublishRequest request, string mode, CancellationToken cancellationToken)
    {
        var payload = new
        {
            pageId,
            pageName,
            videoPath = request.VideoPath,
            caption = request.Caption,
            shortTitle = request.ShortTitle,
            mode,
            generatedAtUtc = DateTime.UtcNow
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "facebook-reel-publish-payload.json"), JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
    }

    private static async Task WriteResultAsync(string outputDirectory, FacebookReelPublishDiagnostics? diagnostics, MetaPublishResult result, CancellationToken cancellationToken)
        => await File.WriteAllTextAsync(Path.Combine(outputDirectory, "facebook-reel-publish-result.json"), JsonSerializer.Serialize(diagnostics ?? result, JsonOptions), cancellationToken);

    private static MetaPublishResult Failed(string mode, string error)
        => new() { Success = false, Platform = "Facebook", Mode = mode, Error = error, PublishedUtc = DateTime.UtcNow };

    private static string NormalizeMode(string? mode)
        => string.Equals(mode, "Public", StringComparison.OrdinalIgnoreCase) ? "Public"
            : string.Equals(mode, "Private", StringComparison.OrdinalIgnoreCase) ? "Private"
            : string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled"
            : "DryRun";

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string BuildFallbackTitle(string caption)
    {
        var firstLine = caption.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return "Astronomy Reel";
        }

        return firstLine.Length <= 120 ? firstLine : firstLine[..120].TrimEnd();
    }

    private static string RedactAccessToken(string url)
    {
        var tokenIndex = url.IndexOf("access_token=", StringComparison.OrdinalIgnoreCase);
        return tokenIndex < 0 ? url : url[..tokenIndex] + "access_token=REDACTED";
    }

    private static string? ExtractString(JsonObject? node, string propertyName)
        => node is not null && node.TryGetPropertyValue(propertyName, out var value) ? value?.GetValue<string>() : null;

    private static string? ExtractStatusText(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            return value.TryGetValue<string>(out var text) ? text : null;
        }

        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                if (property.Key.Contains("status", StringComparison.OrdinalIgnoreCase) || property.Key.Contains("state", StringComparison.OrdinalIgnoreCase))
                {
                    var text = ExtractStatusText(property.Value);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            foreach (var property in obj)
            {
                var text = ExtractStatusText(property.Value);
                if (IsPublished(text))
                {
                    return text;
                }
            }
        }

        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var text = ExtractStatusText(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string? ExtractReelPermalink(JsonObject? reels, string videoId)
    {
        if (reels is null || !reels.TryGetPropertyValue("data", out var data) || data is not JsonArray array)
        {
            return null;
        }

        foreach (var item in array.OfType<JsonObject>())
        {
            if (string.Equals(ExtractString(item, "id"), videoId, StringComparison.OrdinalIgnoreCase))
            {
                return ExtractString(item, "permalink_url");
            }
        }

        return array.OfType<JsonObject>().Select(x => ExtractString(x, "permalink_url")).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    private static bool IsPublished(string? status)
        => !string.IsNullOrWhiteSpace(status) && status.Contains("PUBLISHED", StringComparison.OrdinalIgnoreCase);

    private sealed record FacebookReelVerificationResult(bool PublishedVerified, string? ProcessingStatus, string? PermalinkUrl);

    private sealed class FacebookReelPublishDiagnostics
    {
        public StartUploadResponse? StartResponse { get; init; }
        public int UploadResponseStatus { get; init; }
        public FinishUploadResponse? FinishResponse { get; init; }
        public string? VideoId { get; init; }
        public string? PostId { get; init; }
        public string? PermalinkUrl { get; init; }
        public string? ProcessingStatus { get; init; }
        public string PageId { get; init; } = string.Empty;
        public string PageName { get; init; } = string.Empty;
        public bool PublishedVerified { get; init; }
        public List<string> Warnings { get; init; } = [];
    }

    private sealed class StartUploadResponse
    {
        [JsonPropertyName("video_id")]
        public string? VideoId { get; init; }

        [JsonPropertyName("upload_url")]
        public string? UploadUrl { get; init; }
    }

    private sealed class FinishUploadResponse
    {
        [JsonPropertyName("post_id")]
        public string? PostId { get; init; }

        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }
}
