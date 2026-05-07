using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class FacebookReelPublishService : IFacebookReelPublishService
{
    public const string IncompleteTokenMessage = "Meta OAuth token file is missing or incomplete. Run /api/metaoauth/start first.";
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
                    result = await PublishRealAsync(pageId, token.FacebookPageAccessToken, request, mode, cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or JsonException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Facebook Reel publish failed for pipeline run {PipelineRunId}.", request.PipelineRunId);
            result = Failed(mode, ex.Message);
        }

        await WriteResultAsync(outputDirectory, result, cancellationToken);
        return result;
    }

    private async Task<MetaPublishResult> PublishRealAsync(string pageId, string pageAccessToken, MetaPublishRequest request, string mode, CancellationToken cancellationToken)
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
            return Failed(mode, "Facebook Reel upload session did not return video_id and upload_url.");
        }

        await UploadBinaryAsync(start.UploadUrl, request.VideoPath, pageAccessToken, cancellationToken);

        var finish = await PostGraphAsync<FinishUploadResponse>(
            $"{GraphEndpoint}/{Uri.EscapeDataString(pageId)}/video_reels",
            new Dictionary<string, string>
            {
                ["upload_phase"] = "finish",
                ["video_id"] = start.VideoId,
                ["description"] = request.Caption,
                ["access_token"] = pageAccessToken
            },
            "Facebook Reel publish finish",
            cancellationToken);

        var postId = FirstNonBlank(finish.PostId, finish.Id);
        return new MetaPublishResult
        {
            Success = true,
            Platform = "Facebook",
            Mode = mode,
            VideoId = start.VideoId,
            PostId = postId,
            Url = string.IsNullOrWhiteSpace(postId) ? null : $"https://www.facebook.com/{postId}",
            PublishedUtc = DateTime.UtcNow
        };
    }

    private async Task UploadBinaryAsync(string uploadUrl, string videoPath, string pageAccessToken, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(videoPath);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        using var message = new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = content };
        message.Headers.TryAddWithoutValidation("Authorization", $"OAuth {pageAccessToken}");
        message.Headers.TryAddWithoutValidation("file_size", stream.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Facebook Reel binary upload failed with status {(int)response.StatusCode}.");
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
            mode,
            generatedAtUtc = DateTime.UtcNow
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "facebook-reel-publish-payload.json"), JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
    }

    private static async Task WriteResultAsync(string outputDirectory, MetaPublishResult result, CancellationToken cancellationToken)
        => await File.WriteAllTextAsync(Path.Combine(outputDirectory, "facebook-reel-publish-result.json"), JsonSerializer.Serialize(result, JsonOptions), cancellationToken);

    private static MetaPublishResult Failed(string mode, string error)
        => new() { Success = false, Platform = "Facebook", Mode = mode, Error = error, PublishedUtc = DateTime.UtcNow };

    private static string NormalizeMode(string? mode)
        => string.Equals(mode, "Public", StringComparison.OrdinalIgnoreCase) ? "Public"
            : string.Equals(mode, "Private", StringComparison.OrdinalIgnoreCase) ? "Private"
            : string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled"
            : "DryRun";

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

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
