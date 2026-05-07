using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Publishing;

public sealed class InstagramReelPublishService : IInstagramReelPublishService
{
    public const string MissingInstagramBusinessAccountMessage = "Instagram Business Account ID is missing. Run /api/metaoauth/start first.";
    public const string MissingPublicVideoUrlMessage = "Instagram publishing requires a publicly accessible video_url. Upload short-video.mp4 to public storage first.";
    private const string GraphEndpoint = "https://graph.facebook.com/v23.0";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly HttpClient _httpClient;
    private readonly MetaOptions _metaOptions;
    private readonly MetaPublishingOptions _publishingOptions;
    private readonly RenderingOptions _renderingOptions;
    private readonly IPublicMediaStorageService? _publicMediaStorageService;
    private readonly ILogger<InstagramReelPublishService> _logger;

    public InstagramReelPublishService(
        HttpClient httpClient,
        IOptions<MetaOptions> metaOptions,
        IOptions<MetaPublishingOptions> publishingOptions,
        IOptions<RenderingOptions> renderingOptions,
        ILogger<InstagramReelPublishService> logger,
        IPublicMediaStorageService? publicMediaStorageService = null)
    {
        _httpClient = httpClient;
        _metaOptions = metaOptions.Value;
        _publishingOptions = publishingOptions.Value;
        _renderingOptions = renderingOptions.Value;
        _logger = logger;
        _publicMediaStorageService = publicMediaStorageService;
    }

    public async Task<MetaPublishResult> PublishReelAsync(MetaPublishRequest request, CancellationToken cancellationToken)
    {
        var mode = NormalizeMode(_publishingOptions.Mode);
        var outputDirectory = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(request.VideoPath))!) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        MetaPublishResult result;
        InstagramContainerDiagnostics? containerDiagnostics = null;
        InstagramPublishDiagnostics? publishDiagnostics = null;
        PublicMediaUploadResult? publicMediaUpload = null;
        try
        {
            var token = await LoadTokenAsync(cancellationToken);
            var accessToken = string.IsNullOrWhiteSpace(token.FacebookPageAccessToken) ? token.LongLivedUserAccessToken : token.FacebookPageAccessToken;
            var publicVideo = await ResolvePublicVideoUrlAsync(request, mode, cancellationToken);
            publicMediaUpload = publicVideo.UploadResult;
            var publicVideoUrl = publicVideo.PublicUrl;
            var safePublicVideoUrl = RedactSensitiveQuery(publicVideoUrl);
            await WritePublicMediaUploadResultAsync(outputDirectory, request.VideoPath, publicMediaUpload, publicVideoUrl, cancellationToken);
            await WritePayloadAsync(outputDirectory, token, request, mode, safePublicVideoUrl, publicMediaUpload, cancellationToken);

            if (mode == "Disabled")
            {
                result = Failed(mode, "Instagram Reel publishing is disabled.");
            }
            else if (mode == "DryRun")
            {
                result = new MetaPublishResult { Success = true, Platform = "Instagram", Mode = mode, PublishedUtc = DateTime.UtcNow };
            }
            else
            {
                var validationError = publicMediaUpload is { Success: false }
                    ? publicMediaUpload.Error
                    : await ValidateBeforePublishAsync(request.VideoPath, publicVideoUrl, cancellationToken);
                if (validationError is not null)
                {
                    result = Failed(mode, validationError);
                }
                else
                {
                    var publishResult = await PublishRealAsync(token.InstagramBusinessAccountId!, token.InstagramUsername!, accessToken, publicVideoUrl!, request.Caption, mode, cancellationToken);
                    result = publishResult.Result;
                    containerDiagnostics = publishResult.ContainerDiagnostics with { VideoUrlUsedForInstagram = safePublicVideoUrl };
                    publishDiagnostics = publishResult.PublishDiagnostics;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or JsonException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Instagram Reel publish failed for pipeline run {PipelineRunId}.", request.PipelineRunId);
            result = Failed(mode, ex.Message);
        }

        await WritePublicMediaUploadResultAsync(outputDirectory, request.VideoPath, publicMediaUpload, null, cancellationToken);
        await WriteContainerResultAsync(outputDirectory, containerDiagnostics, result, cancellationToken);
        await WritePublishResultAsync(outputDirectory, publishDiagnostics, result, cancellationToken);
        return result;
    }

    private async Task<(MetaPublishResult Result, InstagramContainerDiagnostics ContainerDiagnostics, InstagramPublishDiagnostics PublishDiagnostics)> PublishRealAsync(
        string instagramBusinessAccountId,
        string instagramUsername,
        string accessToken,
        string videoUrl,
        string caption,
        string mode,
        CancellationToken cancellationToken)
    {
        var container = await PostGraphAsync<CreateContainerResponse>(
            $"{GraphEndpoint}/{Uri.EscapeDataString(instagramBusinessAccountId)}/media",
            new Dictionary<string, string>
            {
                ["media_type"] = "REELS",
                ["video_url"] = videoUrl,
                ["caption"] = caption,
                ["access_token"] = accessToken
            },
            "Instagram Reel media container creation",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(container.CreationId))
        {
            throw new InvalidOperationException("Instagram Reel media container creation did not return creation_id.");
        }

        var poll = await PollContainerAsync(container.CreationId, accessToken, cancellationToken);
        var containerDiagnostics = new InstagramContainerDiagnostics
        {
            CreationId = container.CreationId,
            StatusCode = poll.StatusCode,
            Status = poll.Status,
            Attempts = poll.Attempts,
            Finished = poll.Finished
        };

        if (!poll.Finished)
        {
            var error = string.Equals(poll.StatusCode, "ERROR", StringComparison.OrdinalIgnoreCase)
                ? $"Instagram Reel media container failed: {poll.Status ?? poll.StatusCode}."
                : $"Instagram Reel media container did not finish after {poll.Attempts} attempts.";
            return (Failed(mode, error), containerDiagnostics, new InstagramPublishDiagnostics { CreationId = container.CreationId, InstagramBusinessAccountId = instagramBusinessAccountId, InstagramUsername = instagramUsername });
        }

        var publish = await PostGraphAsync<PublishContainerResponse>(
            $"{GraphEndpoint}/{Uri.EscapeDataString(instagramBusinessAccountId)}/media_publish",
            new Dictionary<string, string>
            {
                ["creation_id"] = container.CreationId,
                ["access_token"] = accessToken
            },
            "Instagram Reel media publish",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(publish.Id))
        {
            throw new InvalidOperationException("Instagram Reel media_publish did not return id.");
        }

        var details = await TryGetGraphObjectAsync<InstagramMediaDetails>($"{GraphEndpoint}/{Uri.EscapeDataString(publish.Id)}?fields=id,permalink,media_type,timestamp&access_token={Uri.EscapeDataString(accessToken)}", cancellationToken);
        var diagnostics = new InstagramPublishDiagnostics
        {
            CreationId = container.CreationId,
            MediaId = publish.Id,
            Permalink = details?.Permalink,
            MediaType = details?.MediaType,
            Timestamp = details?.Timestamp,
            InstagramBusinessAccountId = instagramBusinessAccountId,
            InstagramUsername = instagramUsername
        };

        return (new MetaPublishResult
        {
            Success = true,
            Platform = "Instagram",
            Mode = mode,
            PostId = publish.Id,
            VideoId = publish.Id,
            Url = details?.Permalink,
            PublishedVerified = !string.IsNullOrWhiteSpace(details?.Permalink),
            PublishedUtc = DateTime.UtcNow
        }, containerDiagnostics, diagnostics);
    }

    private async Task<InstagramContainerPollResult> PollContainerAsync(string creationId, string accessToken, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _publishingOptions.InstagramContainerMaxAttempts);
        var pollDelay = TimeSpan.FromSeconds(Math.Max(0, _publishingOptions.InstagramContainerPollSeconds));
        InstagramContainerStatus? latest = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (attempt > 1 && pollDelay > TimeSpan.Zero)
            {
                await Task.Delay(pollDelay, cancellationToken);
            }

            latest = await GetGraphAsync<InstagramContainerStatus>($"{GraphEndpoint}/{Uri.EscapeDataString(creationId)}?fields=status_code,status&access_token={Uri.EscapeDataString(accessToken)}", "Instagram Reel container status", cancellationToken);
            _logger.LogInformation("Instagram Reel container poll {Attempt}/{MaxAttempts} for creation {CreationId}: status_code={StatusCode}.", attempt, maxAttempts, creationId, latest?.StatusCode ?? "unknown");

            if (string.Equals(latest?.StatusCode, "FINISHED", StringComparison.OrdinalIgnoreCase))
            {
                return new InstagramContainerPollResult(true, latest.StatusCode, latest.Status, attempt);
            }

            if (string.Equals(latest?.StatusCode, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                return new InstagramContainerPollResult(false, latest.StatusCode, latest.Status, attempt);
            }
        }

        return new InstagramContainerPollResult(false, latest?.StatusCode, latest?.Status, maxAttempts);
    }

    private async Task<(string? PublicUrl, PublicMediaUploadResult? UploadResult)> ResolvePublicVideoUrlAsync(MetaPublishRequest request, string mode, CancellationToken cancellationToken)
    {
        if (mode != "DryRun" && mode != "Disabled")
        {
            if (!File.Exists(request.VideoPath))
            {
                throw new FileNotFoundException("Instagram Reel video is missing: shorts/short-video.mp4.", request.VideoPath);
            }

            if (_publicMediaStorageService is null)
            {
                return (null, new PublicMediaUploadResult { Success = false, Error = "PublicMediaStorage is not configured for Instagram Reel publishing." });
            }

            var upload = await _publicMediaStorageService.UploadForInstagramAsync(request.VideoPath, request.PipelineRunId, cancellationToken);
            if (!upload.Success)
            {
                return (null, upload);
            }

            return (upload.PublicUrl, upload);
        }

        var configured = _publishingOptions.PublicMediaBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return (null, null);
        }

        if (Uri.TryCreate(configured, UriKind.Absolute, out var absolute) && absolute.Scheme == Uri.UriSchemeHttps && absolute.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return (absolute.ToString(), null);
        }

        if (Uri.TryCreate(configured.TrimEnd('/') + "/" + Uri.EscapeDataString(Path.GetFileName(request.VideoPath)), UriKind.Absolute, out var combined) && combined.Scheme == Uri.UriSchemeHttps)
        {
            return (combined.ToString(), null);
        }

        return (null, null);
    }

    private async Task<string?> ValidateBeforePublishAsync(string videoPath, string? publicVideoUrl, CancellationToken cancellationToken)
    {
        if (!File.Exists(videoPath))
        {
            return "Instagram Reel video is missing: shorts/short-video.mp4.";
        }

        if (new FileInfo(videoPath).Length <= 0)
        {
            return "Instagram Reel video is empty: shorts/short-video.mp4.";
        }

        if (!IsPublicHttpsUrl(publicVideoUrl))
        {
            return MissingPublicVideoUrlMessage;
        }

        var reachabilityError = await ValidatePublicUrlReachableAsync(publicVideoUrl!, cancellationToken);
        if (reachabilityError is not null)
        {
            return reachabilityError;
        }

        var validation = await YouTubeShortsValidation.ValidateBeforeUploadAsync(videoPath, _renderingOptions.FfprobePath, cancellationToken);
        var warnings = validation.Warnings.Where(w => !w.Contains("60", StringComparison.Ordinal)).ToList();
        if (validation.Diagnostics.DurationSeconds > 90d)
        {
            return $"Instagram Reel duration is {validation.Diagnostics.DurationSeconds:F2}s; expected <= 90s.";
        }

        if (warnings.Count > 0)
        {
            _logger.LogWarning("Instagram Reel preferred-format validation warnings for {VideoPath}: {Warnings}", videoPath, string.Join("; ", warnings));
        }

        return null;
    }

    private async Task<MetaOAuthTokenFile> LoadTokenAsync(CancellationToken cancellationToken)
    {
        var path = ResolveTokenFilePath();
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(MissingInstagramBusinessAccountMessage);
        }

        var token = JsonSerializer.Deserialize<MetaOAuthTokenFile>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
        if (token is null || string.IsNullOrWhiteSpace(token.InstagramBusinessAccountId))
        {
            throw new InvalidOperationException(MissingInstagramBusinessAccountMessage);
        }

        if (string.IsNullOrWhiteSpace(token.InstagramUsername)
            || (string.IsNullOrWhiteSpace(token.FacebookPageAccessToken) && string.IsNullOrWhiteSpace(token.LongLivedUserAccessToken)))
        {
            throw new InvalidOperationException("Meta OAuth token file is missing Instagram publishing token details. Run /api/metaoauth/start first.");
        }

        return token;
    }

    private string ResolveTokenFilePath()
        => string.IsNullOrWhiteSpace(_metaOptions.TokenFilePath)
            ? Path.Combine(AppContext.BaseDirectory, "meta-oauth-token.json")
            : Path.GetFullPath(_metaOptions.TokenFilePath);

    private async Task<T> PostGraphAsync<T>(string url, Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{operation} failed with status {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException($"{operation} returned an empty response.");
    }

    private async Task<T> GetGraphAsync<T>(string url, string operation, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{operation} failed with status {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException($"{operation} returned an empty response.");
    }

    private async Task<T?> TryGetGraphObjectAsync<T>(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken) : default;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Instagram Reel media details GET failed.");
            return default;
        }
    }

    private async Task<string?> ValidatePublicUrlReachableAsync(string publicVideoUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, publicVideoUrl);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return $"Instagram Reel public video_url is not reachable by HEAD request. Status={(int)response.StatusCode}.";
        }

        if (response.Content.Headers.ContentType?.MediaType is { } mediaType && !string.Equals(mediaType, "video/mp4", StringComparison.OrdinalIgnoreCase))
        {
            return $"Instagram Reel public video_url content type is '{mediaType}'; expected video/mp4.";
        }

        return null;
    }

    private static async Task WritePayloadAsync(string outputDirectory, MetaOAuthTokenFile token, MetaPublishRequest request, string mode, string? publicVideoUrl, PublicMediaUploadResult? upload, CancellationToken cancellationToken)
    {
        var payload = new
        {
            instagramBusinessAccountId = token.InstagramBusinessAccountId,
            instagramUsername = token.InstagramUsername,
            localVideoPath = request.VideoPath,
            localFilePath = request.VideoPath,
            blobName = upload?.BlobName,
            publicUrlMasked = publicVideoUrl,
            expiresUtc = upload?.ExpiresUtc,
            instagramVideoUrlUsedMasked = publicVideoUrl,
            caption = request.Caption,
            mode,
            generatedAtUtc = DateTime.UtcNow
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "instagram-reel-publish-payload.json"), JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
    }

    private static async Task WritePublicMediaUploadResultAsync(string outputDirectory, string localFilePath, PublicMediaUploadResult? upload, string? instagramVideoUrlUsed, CancellationToken cancellationToken)
    {
        var publicUrl = upload?.PublicUrl ?? instagramVideoUrlUsed;
        var payload = new
        {
            localFilePath,
            blobName = upload?.BlobName,
            publicUrlMasked = RedactSensitiveQuery(publicUrl),
            expiresUtc = upload?.ExpiresUtc,
            instagramVideoUrlUsedMasked = RedactSensitiveQuery(instagramVideoUrlUsed ?? upload?.PublicUrl),
            success = upload?.Success,
            error = upload?.Error
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "public-media-upload-result.json"), JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
    }

    private static async Task WriteContainerResultAsync(string outputDirectory, InstagramContainerDiagnostics? diagnostics, MetaPublishResult result, CancellationToken cancellationToken)
    {
        var output = diagnostics is null ? (object)result : diagnostics;
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "instagram-reel-container-result.json"), JsonSerializer.Serialize(output, JsonOptions), cancellationToken);
    }

    private static async Task WritePublishResultAsync(string outputDirectory, InstagramPublishDiagnostics? diagnostics, MetaPublishResult result, CancellationToken cancellationToken)
    {
        var output = diagnostics is null ? (object)result : diagnostics;
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "instagram-reel-publish-result.json"), JsonSerializer.Serialize(output, JsonOptions), cancellationToken);
    }

    private static bool IsPublicHttpsUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string? RedactSensitiveQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Query))
        {
            return value;
        }

        var builder = new UriBuilder(uri) { Query = "REDACTED" };
        return builder.Uri.ToString();
    }

    private static MetaPublishResult Failed(string mode, string error)
        => new() { Success = false, Platform = "Instagram", Mode = mode, Error = error, PublishedUtc = DateTime.UtcNow };

    private static string NormalizeMode(string? mode)
        => string.Equals(mode, "Public", StringComparison.OrdinalIgnoreCase) ? "Public"
            : string.Equals(mode, "Private", StringComparison.OrdinalIgnoreCase) ? "Private"
            : string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled"
            : "DryRun";

    private sealed record InstagramContainerPollResult(bool Finished, string? StatusCode, string? Status, int Attempts);

    private sealed record CreateContainerResponse([property: JsonPropertyName("creation_id")] string? CreationId);
    private sealed record InstagramContainerStatus([property: JsonPropertyName("status_code")] string? StatusCode, [property: JsonPropertyName("status")] string? Status);
    private sealed record PublishContainerResponse([property: JsonPropertyName("id")] string? Id);
    private sealed record InstagramMediaDetails([property: JsonPropertyName("id")] string? Id, [property: JsonPropertyName("permalink")] string? Permalink, [property: JsonPropertyName("media_type")] string? MediaType, [property: JsonPropertyName("timestamp")] string? Timestamp);

    private sealed record InstagramContainerDiagnostics
    {
        public string? CreationId { get; init; }
        public string? StatusCode { get; init; }
        public string? Status { get; init; }
        public int Attempts { get; init; }
        public bool Finished { get; init; }
        public string? VideoUrlUsedForInstagram { get; init; }
    }

    private sealed record InstagramPublishDiagnostics
    {
        public string? CreationId { get; init; }
        public string? MediaId { get; init; }
        public string? Permalink { get; init; }
        public string? MediaType { get; init; }
        public string? Timestamp { get; init; }
        public string InstagramBusinessAccountId { get; init; } = string.Empty;
        public string InstagramUsername { get; init; } = string.Empty;
    }
}
