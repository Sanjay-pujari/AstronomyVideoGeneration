using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
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
    private const string PosterFrameFallbackWarning = "Meta custom cover was not accepted. Poster-frame fallback video was uploaded.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly HttpClient _httpClient;
    private readonly MetaOptions _metaOptions;
    private readonly MetaPublishingOptions _publishingOptions;
    private readonly RenderingOptions _renderingOptions;
    private readonly IPublicMediaStorageService? _publicMediaStorageService;
    private readonly IMetaThumbnailAssetPublisher? _thumbnailAssetPublisher;
    private readonly ILogger<InstagramReelPublishService> _logger;
    private int _graphRetryAttempts;
    private int _graphTransientFailureCount;
    private string? _lastGraphError;
    private int? _lastGraphHttpStatusCode;

    public InstagramReelPublishService(
        HttpClient httpClient,
        IOptions<MetaOptions> metaOptions,
        IOptions<MetaPublishingOptions> publishingOptions,
        IOptions<RenderingOptions> renderingOptions,
        ILogger<InstagramReelPublishService> logger,
        IPublicMediaStorageService? publicMediaStorageService = null,
        IMetaThumbnailAssetPublisher? thumbnailAssetPublisher = null)
    {
        _httpClient = httpClient;
        _metaOptions = metaOptions.Value;
        _publishingOptions = publishingOptions.Value;
        _renderingOptions = renderingOptions.Value;
        _logger = logger;
        _publicMediaStorageService = publicMediaStorageService;
        _thumbnailAssetPublisher = thumbnailAssetPublisher;
    }

    public async Task<MetaPublishResult> PublishReelAsync(MetaPublishRequest request, CancellationToken cancellationToken)
    {
        var mode = NormalizeMode(_publishingOptions.Mode);
        var outputDirectory = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(request.VideoPath))!) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        MetaPublishResult result;
        InstagramContainerDiagnostics? containerDiagnostics = null;
        InstagramPublishDiagnostics? publishDiagnostics = null;
        InstagramPollingDiagnostics? pollingDiagnostics = null;
        PublicMediaUploadResult? publicMediaUpload = null;
        PublicMediaUploadResult? thumbnailMediaUpload = null;
        string? coverUrl = null;
        string? graphError = null;
        try
        {
            var token = await LoadTokenAsync(cancellationToken);
            var accessToken = string.IsNullOrWhiteSpace(token.FacebookPageAccessToken) ? token.LongLivedUserAccessToken : token.FacebookPageAccessToken;
            var publicVideo = await ResolvePublicVideoUrlAsync(request, mode, cancellationToken);
            publicMediaUpload = publicVideo.UploadResult;
            var publicVideoUrl = publicVideo.PublicUrl;
            var safePublicVideoUrl = RedactSensitiveQuery(publicVideoUrl);
            await WritePublicMediaUploadResultAsync(outputDirectory, request.VideoPath, publicMediaUpload, publicVideoUrl, cancellationToken);
            var publicCover = await ResolvePublicCoverUrlAsync(request, mode, cancellationToken);
            thumbnailMediaUpload = publicCover.UploadResult;
            coverUrl = publicCover.PublicUrl;
            await WriteInstagramThumbnailDiagnosticsAsync(outputDirectory, null, null, coverUrl, request, thumbnailMediaUpload, null, null, cancellationToken);
            await WritePayloadAsync(outputDirectory, token, request, mode, safePublicVideoUrl, publicMediaUpload, cancellationToken);

            if (mode == "Disabled")
            {
                result = Failed(mode, "Instagram Reel publishing is disabled.");
            }
            else if (mode == "DryRun")
            {
                var thumbnailWarning = BuildUnsupportedThumbnailWarning(request);
                var warnings = BuildWarnings(thumbnailWarning, request.PosterFrameApplied);
                result = new MetaPublishResult
                {
                    Success = true,
                    Platform = "Instagram",
                    ContentType = PlatformThumbnailContentTypes.Reel,
                    ContentKind = PlatformThumbnailContentTypes.Reel,
                    Mode = mode,
                    UploadedThumbnailPath = request.PlatformThumbnailPath,
                    ThumbnailSource = request.ThumbnailSource,
                    VideoPathUsed = request.VideoPath,
                    ThumbnailPathUsed = request.PlatformThumbnailPath,
                    ThumbnailStrategy = request.ThumbnailSource,
                    ThumbnailUploadAttempted = false,
                    ThumbnailUploadSuccess = false,
                    ThumbnailWarning = thumbnailWarning,
                    Warning = warnings.FirstOrDefault(),
                    Warnings = warnings,
                    PosterFrameApplied = request.PosterFrameApplied,
                    PosterFrameVideoPath = request.PosterFrameApplied ? request.VideoPath : null,
                    PublishedUtc = DateTime.UtcNow
                };
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
                    var publishResult = await PublishRealAsync(token.InstagramBusinessAccountId!, token.InstagramUsername!, accessToken, publicVideoUrl!, coverUrl, request, mode, cancellationToken);
                    result = publishResult.Result;
                    containerDiagnostics = publishResult.ContainerDiagnostics with { VideoUrlUsedForInstagram = safePublicVideoUrl, CoverUrlUsedForInstagram = RedactSensitiveQuery(coverUrl) };
                    publishDiagnostics = publishResult.PublishDiagnostics;
                    pollingDiagnostics = publishResult.PollingDiagnostics;
                    graphError = publishResult.GraphError;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or JsonException or HttpRequestException)
        {
            _logger.LogWarning("Instagram Reel publish failed for pipeline run {PipelineRunId}. Error={Error}", request.PipelineRunId, RedactAccessToken(ex.Message));
            result = Failed(mode, RedactAccessToken(ex.Message) ?? ex.Message);
        }

        await WritePublicMediaUploadResultAsync(outputDirectory, request.VideoPath, publicMediaUpload, null, cancellationToken);
        await WriteInstagramThumbnailDiagnosticsAsync(outputDirectory, containerDiagnostics?.CreationId, publishDiagnostics?.MediaId, coverUrl, request, thumbnailMediaUpload, graphError, result.ThumbnailWarning, cancellationToken);
        await WriteContainerResultAsync(outputDirectory, containerDiagnostics, result, cancellationToken);
        await WritePublishResultAsync(outputDirectory, publishDiagnostics, result, cancellationToken);
        await WritePollingDiagnosticsAsync(outputDirectory, pollingDiagnostics, result, cancellationToken);
        await ThumbnailPublishDiagnosticsWriter.WriteFromMetaResultAsync(outputDirectory, result, cancellationToken);
        return result;
    }

    private async Task<(MetaPublishResult Result, InstagramContainerDiagnostics ContainerDiagnostics, InstagramPublishDiagnostics PublishDiagnostics, InstagramPollingDiagnostics PollingDiagnostics, string? GraphError)> PublishRealAsync(
        string instagramBusinessAccountId,
        string instagramUsername,
        string accessToken,
        string videoUrl,
        string? coverUrl,
        MetaPublishRequest request,
        string mode,
        CancellationToken cancellationToken)
    {
        var thumbnailWarning = string.Empty;
        var containerForm = new Dictionary<string, string>
        {
            ["media_type"] = "REELS",
            ["video_url"] = videoUrl,
            ["caption"] = request.Caption,
            ["access_token"] = accessToken
        };
        if (IsPublicHttpsUrl(coverUrl))
        {
            containerForm["cover_url"] = coverUrl!;
        }

        CreateContainerResponse container;
        string? graphError = null;
        try
        {
            container = await PostGraphAsync<CreateContainerResponse>(
                $"{GraphEndpoint}/{Uri.EscapeDataString(instagramBusinessAccountId)}/media",
                containerForm,
                "Instagram Reel media container creation",
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (containerForm.ContainsKey("cover_url"))
        {
            graphError = ex.Message;
            thumbnailWarning = "Instagram cover_url was not accepted; platform may choose video frame.";
            _logger.LogWarning(ex, thumbnailWarning);
            containerForm.Remove("cover_url");
            container = await PostGraphAsync<CreateContainerResponse>(
                $"{GraphEndpoint}/{Uri.EscapeDataString(instagramBusinessAccountId)}/media",
                containerForm,
                "Instagram Reel media container creation",
                cancellationToken);
        }

        var containerId = container.ContainerId;
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Instagram Reel media container creation did not return id.");
        }

        var poll = await PollContainerAsync(containerId, accessToken, cancellationToken);
        var containerDiagnostics = new InstagramContainerDiagnostics
        {
            CreationId = containerId,
            StatusCode = poll.StatusCode,
            Status = poll.Status,
            Attempts = poll.Attempts,
            Finished = poll.Finished
        };
        var pollingDiagnostics = new InstagramPollingDiagnostics
        {
            CreationId = containerId,
            PollingAttempts = poll.Attempts,
            LastKnownStatus = FirstNonBlank(poll.StatusCode, poll.Status),
            TimedOut = poll.TimedOut,
            PublishedVerified = false,
            LastGraphError = RedactAccessToken(poll.LastGraphError),
            TransientFailureCount = poll.TransientFailureCount,
            GraphRetryAttempts = poll.GraphRetryAttempts,
            LastHttpStatusCode = poll.LastHttpStatusCode
        };

        if (!poll.Finished)
        {
            if (poll.TimedOut && _publishingOptions.InstagramTreatProcessingTimeoutAsSuccess && !string.IsNullOrWhiteSpace(containerId))
            {
                const string warning = "Instagram Reel container still processing when verification timeout occurred.";
                var warnings = BuildWarnings(thumbnailWarning, request.PosterFrameApplied);
                warnings.Add(warning);
                pollingDiagnostics = pollingDiagnostics with { Warning = warning };
                _logger.LogWarning("Instagram Reel container {CreationId} is still processing after {Attempts} verification poll attempts; treating as upload success.", containerId, poll.Attempts);
                return (new MetaPublishResult
                {
                    Success = true,
                    Platform = "Instagram",
                    ContentType = PlatformThumbnailContentTypes.Reel,
                    ContentKind = PlatformThumbnailContentTypes.Reel,
                    Mode = mode,
                    UploadedThumbnailPath = request.PlatformThumbnailPath,
                    UploadedThumbnailUrl = coverUrl,
                    ThumbnailSource = request.ThumbnailSource,
                    VideoPathUsed = request.VideoPath,
                    ThumbnailPathUsed = request.PlatformThumbnailPath,
                    ThumbnailStrategy = request.ThumbnailSource,
                    ThumbnailUploadAttempted = IsPublicHttpsUrl(coverUrl),
                    ThumbnailUploadSuccess = false,
                    ThumbnailWarning = thumbnailWarning,
                    Warning = warning,
                    Warnings = warnings,
                    PosterFrameApplied = request.PosterFrameApplied,
                    PosterFrameVideoPath = request.PosterFrameApplied ? request.VideoPath : null,
                    PostId = containerId,
                    VideoId = containerId,
                    PublishedVerified = false,
                    PublishedUtc = DateTime.UtcNow
                }, containerDiagnostics, new InstagramPublishDiagnostics { CreationId = containerId, InstagramBusinessAccountId = instagramBusinessAccountId, InstagramUsername = instagramUsername }, pollingDiagnostics, graphError);
            }

            var error = string.Equals(poll.StatusCode, "ERROR", StringComparison.OrdinalIgnoreCase)
                ? $"Instagram Reel media container failed: {poll.Status ?? poll.StatusCode}."
                : $"Instagram Reel media container did not finish after {poll.Attempts} attempts.";
            return (new MetaPublishResult
            {
                Success = false,
                Platform = "Instagram",
                ContentType = PlatformThumbnailContentTypes.Reel,
                Mode = mode,
                Error = error,
                UploadedThumbnailPath = request.PlatformThumbnailPath,
                UploadedThumbnailUrl = coverUrl,
                ThumbnailSource = request.ThumbnailSource,
                ThumbnailUploadAttempted = IsPublicHttpsUrl(coverUrl),
                ThumbnailUploadSuccess = false,
                ThumbnailWarning = thumbnailWarning,
                Warning = BuildWarnings(thumbnailWarning, request.PosterFrameApplied).FirstOrDefault(),
                Warnings = BuildWarnings(thumbnailWarning, request.PosterFrameApplied),
                PosterFrameApplied = request.PosterFrameApplied,
                PosterFrameVideoPath = request.PosterFrameApplied ? request.VideoPath : null,
                PublishedUtc = DateTime.UtcNow
            }, containerDiagnostics, new InstagramPublishDiagnostics { CreationId = containerId, InstagramBusinessAccountId = instagramBusinessAccountId, InstagramUsername = instagramUsername }, pollingDiagnostics, graphError);
        }

        var publish = await PostGraphAsync<PublishContainerResponse>(
            $"{GraphEndpoint}/{Uri.EscapeDataString(instagramBusinessAccountId)}/media_publish",
            new Dictionary<string, string>
            {
                ["creation_id"] = containerId,
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
            CreationId = containerId,
            MediaId = publish.Id,
            Permalink = details?.Permalink,
            MediaType = details?.MediaType,
            Timestamp = details?.Timestamp,
            InstagramBusinessAccountId = instagramBusinessAccountId,
            InstagramUsername = instagramUsername
        };
        pollingDiagnostics = pollingDiagnostics with
        {
            MediaId = publish.Id,
            Permalink = details?.Permalink,
            PublishedVerified = !string.IsNullOrWhiteSpace(details?.Permalink)
        };

        return (new MetaPublishResult
        {
            Success = true,
            Platform = "Instagram",
            ContentType = PlatformThumbnailContentTypes.Reel,
            ContentKind = PlatformThumbnailContentTypes.Reel,
            Mode = mode,
            UploadedThumbnailPath = request.PlatformThumbnailPath,
            UploadedThumbnailUrl = coverUrl,
            ThumbnailSource = request.ThumbnailSource,
            VideoPathUsed = request.VideoPath,
            ThumbnailPathUsed = request.PlatformThumbnailPath,
            ThumbnailStrategy = request.ThumbnailSource,
            ThumbnailUploadAttempted = IsPublicHttpsUrl(coverUrl),
            ThumbnailUploadSuccess = IsPublicHttpsUrl(coverUrl) && string.IsNullOrWhiteSpace(thumbnailWarning),
            ThumbnailWarning = thumbnailWarning,
            Warning = BuildWarnings(thumbnailWarning, request.PosterFrameApplied).FirstOrDefault(),
            Warnings = BuildWarnings(thumbnailWarning, request.PosterFrameApplied),
            PosterFrameApplied = request.PosterFrameApplied,
            PosterFrameVideoPath = request.PosterFrameApplied ? request.VideoPath : null,
            PostId = publish.Id,
            VideoId = publish.Id,
            Url = details?.Permalink,
            PublishedVerified = !string.IsNullOrWhiteSpace(details?.Permalink),
            PublishedUtc = DateTime.UtcNow
        }, containerDiagnostics, diagnostics, pollingDiagnostics, graphError);
    }

    private async Task<InstagramContainerPollResult> PollContainerAsync(string creationId, string accessToken, CancellationToken externalCancellationToken)
    {
        var useLegacyPollingSettings = _publishingOptions.InstagramContainerMaxAttempts != 18 || _publishingOptions.InstagramContainerPollSeconds != 10;
        var configuredAttempts = useLegacyPollingSettings ? _publishingOptions.InstagramContainerMaxAttempts : _publishingOptions.InstagramPollAttempts;
        var configuredDelaySeconds = useLegacyPollingSettings ? _publishingOptions.InstagramContainerPollSeconds : _publishingOptions.InstagramPollDelaySeconds;
        var maxAttempts = Math.Max(1, configuredAttempts);
        var pollDelay = TimeSpan.FromSeconds(Math.Max(0, configuredDelaySeconds));
        InstagramContainerStatus? latest = null;
        var attempts = 0;
        var startingGraphRetryAttempts = _graphRetryAttempts;
        var startingTransientFailureCount = _graphTransientFailureCount;
        string? lastTransientError = null;
        int? lastTransientStatusCode = null;

        try
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                externalCancellationToken.ThrowIfCancellationRequested();
                if (attempt > 1 && pollDelay > TimeSpan.Zero)
                {
                    await Task.Delay(pollDelay, externalCancellationToken);
                }

                attempts = attempt;
                try
                {
                    latest = await GetGraphAsync<InstagramContainerStatus>($"{GraphEndpoint}/{Uri.EscapeDataString(creationId)}?fields=status_code,status&access_token={Uri.EscapeDataString(accessToken)}", "Instagram Reel container status", externalCancellationToken);
                    if (_graphTransientFailureCount > startingTransientFailureCount)
                    {
                        lastTransientError = _lastGraphError;
                        lastTransientStatusCode = _lastGraphHttpStatusCode;
                    }

                    _logger.LogInformation("Instagram Reel container poll {Attempt}/{MaxAttempts} for creation {CreationId}: status_code={StatusCode}.", attempt, maxAttempts, creationId, latest?.StatusCode ?? "unknown");
                }
                catch (HttpRequestException ex) when (!externalCancellationToken.IsCancellationRequested)
                {
                    lastTransientError = ex.Message;
                    lastTransientStatusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : _lastGraphHttpStatusCode;
                    _logger.LogWarning("Transient Instagram Reel container poll {Attempt}/{MaxAttempts} failed for creation {CreationId}; continuing to next poll attempt. Error={Error}", attempt, maxAttempts, creationId, RedactAccessToken(ex.Message));
                    continue;
                }

                if (string.Equals(latest?.StatusCode, "FINISHED", StringComparison.OrdinalIgnoreCase))
                {
                    return new InstagramContainerPollResult(true, latest.StatusCode, latest.Status, attempt, false, lastTransientError, _graphTransientFailureCount - startingTransientFailureCount, _graphRetryAttempts - startingGraphRetryAttempts, lastTransientStatusCode);
                }

                if (string.Equals(latest?.StatusCode, "ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    return new InstagramContainerPollResult(false, latest.StatusCode, latest.Status, attempt, false, lastTransientError, _graphTransientFailureCount - startingTransientFailureCount, _graphRetryAttempts - startingGraphRetryAttempts, lastTransientStatusCode);
                }
            }
        }
        catch (OperationCanceledException) when (!externalCancellationToken.IsCancellationRequested && IsProcessingStatus(latest))
        {
            _logger.LogWarning("Instagram Reel container polling timed out while creation {CreationId} was still processing.", creationId);
            return new InstagramContainerPollResult(false, latest?.StatusCode, latest?.Status, Math.Max(1, attempts), true, lastTransientError, _graphTransientFailureCount - startingTransientFailureCount, _graphRetryAttempts - startingGraphRetryAttempts, lastTransientStatusCode);
        }

        var timedOut = IsProcessingStatus(latest) || (latest is null && lastTransientError is not null);
        var status = latest?.Status ?? lastTransientError;
        return new InstagramContainerPollResult(false, latest?.StatusCode, status, Math.Max(1, attempts == 0 ? maxAttempts : attempts), timedOut, lastTransientError, _graphTransientFailureCount - startingTransientFailureCount, _graphRetryAttempts - startingGraphRetryAttempts, lastTransientStatusCode);
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


    private async Task<(string? PublicUrl, PublicMediaUploadResult? UploadResult)> ResolvePublicCoverUrlAsync(MetaPublishRequest request, string mode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlatformThumbnailPath) || !File.Exists(request.PlatformThumbnailPath))
        {
            return (null, null);
        }

        if (mode == "DryRun" || mode == "Disabled")
        {
            return (null, null);
        }

        if (_thumbnailAssetPublisher is null)
        {
            return (null, new PublicMediaUploadResult { Success = false, Error = "PublicMediaStorage is not configured for Instagram thumbnail cover_url publishing." });
        }

        var upload = await _thumbnailAssetPublisher.UploadThumbnailAsync(request.PlatformThumbnailPath, request.PipelineRunId, cancellationToken);
        return upload.Success ? (upload.PublicUrl, upload) : (null, upload);
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

    private Task<T> PostGraphAsync<T>(string url, Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
        => ExecuteGraphWithRetryAsync<T>(
            () => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) },
            operation,
            url,
            cancellationToken);

    private Task<T> GetGraphAsync<T>(string url, string operation, CancellationToken cancellationToken)
        => ExecuteGraphWithRetryAsync<T>(() => new HttpRequestMessage(HttpMethod.Get, url), operation, url, cancellationToken);

    private async Task<T> ExecuteGraphWithRetryAsync<T>(Func<HttpRequestMessage> requestFactory, string operation, string url, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _publishingOptions.GraphRetryMaxAttempts);
        Exception? lastException = null;
        HttpStatusCode? lastStatusCode = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 1)
            {
                await DelayGraphRetryAsync(attempt, cancellationToken);
            }

            _graphRetryAttempts++;
            try
            {
                using var request = requestFactory();
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                        ?? throw new InvalidOperationException($"{operation} returned an empty response.");
                }

                lastStatusCode = response.StatusCode;
                _lastGraphHttpStatusCode = (int)response.StatusCode;
                if (!IsTransientStatusCode(response.StatusCode))
                {
                    throw new InvalidOperationException($"{operation} failed with status {(int)response.StatusCode}.");
                }

                var statusMessage = $"{operation} failed with transient status {(int)response.StatusCode}.";
                RecordTransientGraphFailure(statusMessage, response.StatusCode);
                if (attempt == maxAttempts)
                {
                    lastException = new HttpRequestException(statusMessage, null, response.StatusCode);
                    break;
                }

                _logger.LogWarning("Transient Meta Graph API HTTP status {StatusCode} for {Operation} on attempt {Attempt}/{MaxAttempts}; retrying. Url={Url}", (int)response.StatusCode, operation, attempt, maxAttempts, RedactAccessToken(url));
            }
            catch (Exception ex) when (IsTransientGraphException(ex, cancellationToken))
            {
                lastException = ex;
                RecordTransientGraphFailure(ex.Message, lastStatusCode);
                if (attempt == maxAttempts)
                {
                    throw new HttpRequestException($"{operation} failed after {attempt} transient Meta Graph API attempts: {RedactAccessToken(ex.Message)}", ex, lastStatusCode);
                }

                _logger.LogWarning("Transient Meta Graph API exception for {Operation} on attempt {Attempt}/{MaxAttempts}; retrying. Url={Url}; Error={Error}", operation, attempt, maxAttempts, RedactAccessToken(url), RedactAccessToken(ex.Message));
            }
        }

        throw new HttpRequestException($"{operation} failed after {maxAttempts} transient Meta Graph API attempts: {RedactAccessToken(lastException?.Message ?? _lastGraphError)}", lastException, lastStatusCode);
    }

    private async Task DelayGraphRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = GetGraphRetryDelay(attempt, Math.Max(0, _publishingOptions.GraphRetryBaseDelaySeconds));
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static TimeSpan GetGraphRetryDelay(int attempt, int baseDelaySeconds)
    {
        if (baseDelaySeconds <= 0)
        {
            return TimeSpan.Zero;
        }

        var multiplier = attempt switch
        {
            2 => 1.0d,
            3 => 2.5d,
            _ => 5.0d
        };
        var jitter = Random.Shared.NextDouble() * 0.25d;
        return TimeSpan.FromSeconds((baseDelaySeconds * multiplier) + jitter);
    }

    private void RecordTransientGraphFailure(string message, HttpStatusCode? statusCode = null)
    {
        _graphTransientFailureCount++;
        _lastGraphError = RedactAccessToken(message);
        if (statusCode.HasValue)
        {
            _lastGraphHttpStatusCode = (int)statusCode.Value;
        }
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            || (int)statusCode == 425;

    private static bool IsTransientGraphException(Exception ex, CancellationToken cancellationToken)
        => ex is HttpRequestException
            || ex is IOException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            || ContainsConnectionReset(ex);

    private static bool ContainsConnectionReset(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException socketException && socketException.SocketErrorCode == SocketError.ConnectionReset)
            {
                return true;
            }
        }

        return false;
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
            _logger.LogWarning("Instagram Reel media details GET failed. Error={Error}", RedactAccessToken(ex.Message));
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


    private static async Task WriteInstagramThumbnailDiagnosticsAsync(string outputDirectory, string? mediaContainerId, string? mediaId, string? coverUrl, MetaPublishRequest request, PublicMediaUploadResult? upload, string? graphError, string? warning, CancellationToken cancellationToken)
    {
        var payload = new
        {
            mediaContainerId,
            mediaId,
            coverUrl = RedactSensitiveQuery(coverUrl),
            thumbnailSource = request.ThumbnailSource,
            coverUploadAttempted = !string.IsNullOrWhiteSpace(request.PlatformThumbnailPath),
            coverUploadSuccess = upload?.Success == true && IsPublicHttpsUrl(coverUrl),
            graphError,
            warning
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "instagram-thumbnail-upload-diagnostics.json"), JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
    }

    private static async Task WritePayloadAsync(string outputDirectory, MetaOAuthTokenFile token, MetaPublishRequest request, string mode, string? publicVideoUrl, PublicMediaUploadResult? upload, CancellationToken cancellationToken)
    {
        var payload = new
        {
            instagramBusinessAccountId = token.InstagramBusinessAccountId,
            instagramUsername = token.InstagramUsername,
            localVideoPath = request.VideoPath,
            localFilePath = request.VideoPath,
            longThumbnailPath = request.LongThumbnailPath,
            shortThumbnailPath = request.ShortThumbnailPath,
            platformThumbnailPath = request.PlatformThumbnailPath,
            thumbnailSource = request.ThumbnailSource,
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

    private static async Task WritePollingDiagnosticsAsync(string outputDirectory, InstagramPollingDiagnostics? diagnostics, MetaPublishResult result, CancellationToken cancellationToken)
    {
        var output = diagnostics ?? new InstagramPollingDiagnostics
        {
            MediaId = result.VideoId,
            Permalink = result.Url,
            PublishedVerified = result.PublishedVerified,
            Warning = result.Warning
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "instagram-reel-polling-diagnostics.json"), JsonSerializer.Serialize(output, JsonOptions), cancellationToken);
    }

    private static bool IsPublicHttpsUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string? RedactSensitiveQuery(string? value) => RedactAccessToken(value);

    private static string? RedactAccessToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return System.Text.RegularExpressions.Regex.Replace(value, "(?i)(access_token=)[^&\s]+", "$1REDACTED");
    }


    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static bool IsProcessingStatus(InstagramContainerStatus? status)
        => IsProcessingStatus(status?.StatusCode) || IsProcessingStatus(status?.Status);

    private static bool IsProcessingStatus(string? status)
        => string.Equals(status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "PROCESSING", StringComparison.OrdinalIgnoreCase);

    private static MetaPublishResult Failed(string mode, string error)
        => new() { Success = false, Platform = "Instagram", ContentType = PlatformThumbnailContentTypes.Reel, ContentKind = PlatformThumbnailContentTypes.Reel, Mode = mode, Error = error, ThumbnailWarning = error, Warning = error, PublishedUtc = DateTime.UtcNow };

    private static List<string> BuildWarnings(string? thumbnailWarning, bool posterFrameApplied)
    {
        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(thumbnailWarning))
        {
            warnings.Add(thumbnailWarning);
        }

        if (posterFrameApplied)
        {
            warnings.Add(PosterFrameFallbackWarning);
        }

        return warnings;
    }

    private string? BuildUnsupportedThumbnailWarning(MetaPublishRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlatformThumbnailPath))
        {
            return null;
        }

        const string warning = "Instagram cover_url was not accepted; platform may choose video frame.";
        _logger.LogWarning(warning);
        return warning;
    }

    private static string NormalizeMode(string? mode)
        => string.Equals(mode, "Public", StringComparison.OrdinalIgnoreCase) ? "Public"
            : string.Equals(mode, "Private", StringComparison.OrdinalIgnoreCase) ? "Private"
            : string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled"
            : "DryRun";

    private sealed record InstagramContainerPollResult(bool Finished, string? StatusCode, string? Status, int Attempts, bool TimedOut, string? LastGraphError = null, int TransientFailureCount = 0, int GraphRetryAttempts = 0, int? LastHttpStatusCode = null);

    private sealed record CreateContainerResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("creation_id")] string? CreationId)
    {
        public string? ContainerId => string.IsNullOrWhiteSpace(Id) ? CreationId : Id;
    }
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
        public string? CoverUrlUsedForInstagram { get; init; }
    }


    private sealed record InstagramPollingDiagnostics
    {
        public string? CreationId { get; init; }
        public string? MediaId { get; init; }
        public int PollingAttempts { get; init; }
        public string? LastKnownStatus { get; init; }
        public bool TimedOut { get; init; }
        public bool PublishedVerified { get; init; }
        public string? Warning { get; init; }
        public string? Permalink { get; init; }
        public string? LastGraphError { get; init; }
        public int TransientFailureCount { get; init; }
        public int GraphRetryAttempts { get; init; }
        public int? LastHttpStatusCode { get; init; }
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
