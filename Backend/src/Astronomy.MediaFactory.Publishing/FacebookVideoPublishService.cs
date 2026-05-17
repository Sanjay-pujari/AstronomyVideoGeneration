using System.Globalization;
using System.Net;
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
                    UploadMode = "Simple",
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

            var result = await PublishRealAsync(outputDirectory, pageId, token.FacebookPageName, token.FacebookPageAccessToken, request, thumbnailExists, mode, cancellationToken);
            return await WriteResultAsync(outputDirectory, result, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or JsonException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Facebook full video publish failed for pipeline run {PipelineRunId}.", request.PipelineRunId);
            return await WriteResultAsync(outputDirectory, Failed(mode, ex.Message, request), cancellationToken);
        }
    }

    private async Task<MetaPublishResult> PublishRealAsync(string outputDirectory, string pageId, string pageName, string pageAccessToken, MetaPublishRequest request, bool thumbnailExists, string mode, CancellationToken cancellationToken)
    {
        var fileSizeBytes = new FileInfo(request.VideoPath).Length;
        var diagnostics = new FacebookFullVideoUploadDiagnostics
        {
            FileSizeBytes = fileSizeBytes,
            UploadMode = ShouldUseResumableUpload(fileSizeBytes) ? "Resumable" : "Simple",
            ChunkSizeBytes = ResolveChunkSizeBytes()
        };

        try
        {
            MetaPublishResult result;
            if (diagnostics.UploadMode == "Resumable")
            {
                result = await PublishResumableAsync(pageId, pageName, pageAccessToken, request, thumbnailExists, mode, diagnostics, cancellationToken);
            }
            else
            {
                var simpleResult = await TryPublishSimpleAsync(pageId, pageName, pageAccessToken, request, thumbnailExists, mode, diagnostics, cancellationToken);
                if (!simpleResult.ShouldRetryResumable)
                {
                    result = simpleResult.Result ?? throw new InvalidOperationException("Facebook simple upload did not return a result.");
                }
                else
                {
                    diagnostics.UploadMode = "Resumable";
                    diagnostics.Warnings.Add("Facebook simple upload returned 413 Payload Too Large; retrying with resumable upload.");
                    result = await PublishResumableAsync(pageId, pageName, pageAccessToken, request, thumbnailExists, mode, diagnostics, cancellationToken);
                }
            }

            await WriteDiagnosticsAsync(outputDirectory, diagnostics, cancellationToken);
            return result;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException)
        {
            diagnostics.Errors.Add(ex.Message);
            await WriteDiagnosticsAsync(outputDirectory, diagnostics, cancellationToken);
            return Failed(mode, ex.Message, request, diagnostics.UploadMode);
        }
    }

    private async Task<SimpleUploadAttempt> TryPublishSimpleAsync(string pageId, string pageName, string pageAccessToken, MetaPublishRequest request, bool thumbnailExists, string mode, FacebookFullVideoUploadDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        var title = ResolveTitle(request);
        if (thumbnailExists)
        {
            using var form = CreateSimpleUploadForm(pageAccessToken, request.Caption, title, request.VideoPath);
            await using var thumb = File.OpenRead(request.PlatformThumbnailPath);
            var thumbContent = new StreamContent(thumb);
            thumbContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(thumbContent, "thumb", Path.GetFileName(request.PlatformThumbnailPath));

            using var response = await _httpClient.PostAsync(PageVideosUrl(pageId), form, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<FacebookVideoResponse>(JsonOptions, cancellationToken) ?? new FacebookVideoResponse();
                diagnostics.VideoId = body.Id;
                diagnostics.FinishSuccess = true;
                diagnostics.ThumbnailAttempted = true;
                diagnostics.ThumbnailSuccess = true;
                return new SimpleUploadAttempt(Success(mode, "Simple", body.Id, pageId, pageName, request, thumbnailAttempted: true, thumbnailSuccess: true, warning: null), false);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            diagnostics.Errors.Add($"Simple upload with thumbnail failed with status {(int)response.StatusCode}. {error}");
            if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                return new SimpleUploadAttempt(null, true);
            }

            _logger.LogWarning("Facebook full video upload with custom thumbnail failed with status {StatusCode}. Retrying without thumbnail. Response: {Response}", (int)response.StatusCode, error);
        }

        using var retryForm = CreateSimpleUploadForm(pageAccessToken, request.Caption, title, request.VideoPath);
        using var retryResponse = await _httpClient.PostAsync(PageVideosUrl(pageId), retryForm, cancellationToken);
        if (!retryResponse.IsSuccessStatusCode)
        {
            var error = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
            diagnostics.Errors.Add($"Simple upload without thumbnail failed with status {(int)retryResponse.StatusCode}. {error}");
            if (retryResponse.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                return new SimpleUploadAttempt(null, true);
            }

            throw new InvalidOperationException($"Facebook full video upload failed with status {(int)retryResponse.StatusCode}. {error}");
        }

        var retryBody = await retryResponse.Content.ReadFromJsonAsync<FacebookVideoResponse>(JsonOptions, cancellationToken) ?? new FacebookVideoResponse();
        diagnostics.VideoId = retryBody.Id;
        diagnostics.FinishSuccess = true;
        diagnostics.ThumbnailAttempted = thumbnailExists;
        diagnostics.ThumbnailSuccess = false;
        var warning = thumbnailExists ? CustomThumbnailUnsupportedWarning : null;
        if (!string.IsNullOrWhiteSpace(warning)) diagnostics.Warnings.Add(warning);
        return new SimpleUploadAttempt(Success(mode, "Simple", retryBody.Id, pageId, pageName, request, thumbnailExists, false, warning), false);
    }

    private async Task<MetaPublishResult> PublishResumableAsync(string pageId, string pageName, string pageAccessToken, MetaPublishRequest request, bool thumbnailExists, string mode, FacebookFullVideoUploadDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        var fileSizeBytes = diagnostics.FileSizeBytes;
        var title = ResolveTitle(request);

        var start = await StartResumableUploadAsync(pageId, pageAccessToken, fileSizeBytes, cancellationToken);
        diagnostics.UploadSessionId = start.UploadSessionId;
        diagnostics.VideoId = start.VideoId;
        diagnostics.Progress.Add(new FacebookUploadProgress(start.StartOffset, start.EndOffset));

        var startOffset = ParseOffset(start.StartOffset, nameof(start.StartOffset));
        var endOffset = ParseOffset(start.EndOffset, nameof(start.EndOffset));
        await using var video = File.OpenRead(request.VideoPath);
        while (startOffset != endOffset)
        {
            var chunkLength = checked((int)Math.Min(Math.Min(diagnostics.ChunkSizeBytes, endOffset - startOffset), fileSizeBytes - startOffset));
            if (chunkLength <= 0)
            {
                throw new InvalidOperationException($"Facebook resumable upload returned invalid offsets: start_offset={startOffset}, end_offset={endOffset}.");
            }

            var next = await TransferResumableChunkAsync(pageId, pageAccessToken, start.UploadSessionId, video, startOffset, chunkLength, cancellationToken);
            diagnostics.ChunksUploaded++;
            diagnostics.Progress.Add(new FacebookUploadProgress(next.StartOffset, next.EndOffset));
            startOffset = ParseOffset(next.StartOffset, nameof(next.StartOffset));
            endOffset = ParseOffset(next.EndOffset, nameof(next.EndOffset));
        }

        await FinishResumableUploadAsync(pageId, pageAccessToken, start.UploadSessionId, title, request.Caption, cancellationToken);
        diagnostics.FinishSuccess = true;

        var thumbnailAttempted = false;
        var thumbnailSuccess = false;
        string? warning = null;
        if (thumbnailExists)
        {
            thumbnailAttempted = true;
            diagnostics.ThumbnailAttempted = true;
            var thumbnailResult = await TryUpdateThumbnailAsync(start.VideoId, pageAccessToken, request.PlatformThumbnailPath, cancellationToken);
            thumbnailSuccess = thumbnailResult.Success;
            diagnostics.ThumbnailSuccess = thumbnailResult.Success;
            if (!thumbnailResult.Success)
            {
                warning = thumbnailResult.Warning;
                if (!string.IsNullOrWhiteSpace(warning)) diagnostics.Warnings.Add(warning);
            }
        }

        return Success(mode, "Resumable", start.VideoId, pageId, pageName, request, thumbnailAttempted, thumbnailSuccess, warning);
    }

    private async Task<FacebookResumableStartResponse> StartResumableUploadAsync(string pageId, string pageAccessToken, long fileSizeBytes, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["upload_phase"] = "start",
            ["file_size"] = fileSizeBytes.ToString(CultureInfo.InvariantCulture),
            ["access_token"] = pageAccessToken
        });
        using var response = await _httpClient.PostAsync(PageVideosUrl(pageId), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Facebook resumable upload start failed with status {(int)response.StatusCode}. {body}");
        }

        var parsed = JsonSerializer.Deserialize<FacebookResumableStartResponse>(body, JsonOptions) ?? new FacebookResumableStartResponse();
        if (string.IsNullOrWhiteSpace(parsed.UploadSessionId) || string.IsNullOrWhiteSpace(parsed.VideoId))
        {
            throw new InvalidOperationException($"Facebook resumable upload start response was missing upload_session_id or video_id. {body}");
        }

        return parsed;
    }

    private async Task<FacebookResumableTransferResponse> TransferResumableChunkAsync(string pageId, string pageAccessToken, string uploadSessionId, FileStream video, long startOffset, int chunkLength, CancellationToken cancellationToken)
    {
        var chunk = new byte[chunkLength];
        video.Position = startOffset;
        var read = 0;
        while (read < chunkLength)
        {
            var current = await video.ReadAsync(chunk.AsMemory(read, chunkLength - read), cancellationToken);
            if (current == 0) break;
            read += current;
        }

        if (read != chunkLength)
        {
            throw new InvalidOperationException($"Unable to read Facebook upload chunk at offset {startOffset}; expected {chunkLength} bytes and read {read} bytes.");
        }

        using var form = new MultipartFormDataContent
        {
            { new StringContent("transfer"), "upload_phase" },
            { new StringContent(uploadSessionId), "upload_session_id" },
            { new StringContent(startOffset.ToString(CultureInfo.InvariantCulture)), "start_offset" },
            { new StringContent(pageAccessToken), "access_token" }
        };
        var chunkContent = new ByteArrayContent(chunk);
        chunkContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(chunkContent, "video_file_chunk", "chunk.mp4");

        using var response = await _httpClient.PostAsync(PageVideosUrl(pageId), form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Facebook resumable upload transfer failed with status {(int)response.StatusCode}. {body}");
        }

        return JsonSerializer.Deserialize<FacebookResumableTransferResponse>(body, JsonOptions) ?? new FacebookResumableTransferResponse();
    }

    private async Task FinishResumableUploadAsync(string pageId, string pageAccessToken, string uploadSessionId, string title, string description, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["upload_phase"] = "finish",
            ["upload_session_id"] = uploadSessionId,
            ["title"] = title,
            ["description"] = description,
            ["published"] = "true",
            ["access_token"] = pageAccessToken
        });
        using var response = await _httpClient.PostAsync(PageVideosUrl(pageId), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Facebook resumable upload finish failed with status {(int)response.StatusCode}. {body}");
        }
    }

    private async Task<ThumbnailUpdateResult> TryUpdateThumbnailAsync(string? videoId, string pageAccessToken, string thumbnailPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return new ThumbnailUpdateResult(false, "Facebook full video thumbnail update skipped because the resumable upload response did not include a video id.");
        }

        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(pageAccessToken), "access_token" },
                { new StringContent("true"), "is_preferred" }
            };
            await using var thumb = File.OpenRead(thumbnailPath);
            var thumbContent = new StreamContent(thumb);
            thumbContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(thumbContent, "source", Path.GetFileName(thumbnailPath));

            using var response = await _httpClient.PostAsync($"{GraphEndpoint}/{Uri.EscapeDataString(videoId)}/thumbnails", form, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new ThumbnailUpdateResult(true, null);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            var warning = $"{CustomThumbnailUnsupportedWarning} Thumbnail update failed with status {(int)response.StatusCode}. {error}";
            _logger.LogWarning("{Warning}", warning);
            return new ThumbnailUpdateResult(false, warning);
        }
        catch (HttpRequestException ex)
        {
            var warning = $"{CustomThumbnailUnsupportedWarning} Thumbnail update failed: {ex.Message}";
            _logger.LogWarning(ex, "Facebook full video thumbnail update failed.");
            return new ThumbnailUpdateResult(false, warning);
        }
    }

    private MultipartFormDataContent CreateSimpleUploadForm(string pageAccessToken, string description, string title, string videoPath)
    {
        var video = File.OpenRead(videoPath);
        var form = new MultipartFormDataContent
        {
            { new StringContent(pageAccessToken), "access_token" },
            { new StringContent(description), "description" },
            { new StringContent(title), "title" }
        };
        var videoContent = new StreamContent(video);
        videoContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(videoContent, "source", Path.GetFileName(videoPath));
        return form;
    }

    private bool ShouldUseResumableUpload(long fileSizeBytes)
        => _publishingOptions.FacebookFullVideoUseResumableUpload && fileSizeBytes > Math.Max(0, _publishingOptions.FacebookSimpleUploadMaxBytes);

    private int ResolveChunkSizeBytes() => Math.Max(1, _publishingOptions.FacebookUploadChunkSizeBytes);

    private static long ParseOffset(string? offset, string name)
        => long.TryParse(offset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Facebook resumable upload response included invalid {name}: {offset}");

    private static string ResolveTitle(MetaPublishRequest request)
        => string.IsNullOrWhiteSpace(request.ShortTitle) ? "Astronomy video" : request.ShortTitle;

    private static string PageVideosUrl(string pageId) => $"{GraphEndpoint}/{Uri.EscapeDataString(pageId)}/videos";

    private static MetaPublishResult Success(string mode, string uploadMode, string? videoId, string pageId, string pageName, MetaPublishRequest request, bool thumbnailAttempted, bool thumbnailSuccess, string? warning)
        => new()
        {
            Success = true,
            Platform = "Facebook",
            ContentType = PlatformThumbnailContentTypes.LongVideo,
            ContentKind = PlatformThumbnailContentTypes.LongVideo,
            Mode = mode,
            UploadMode = uploadMode,
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

    private static MetaPublishResult Failed(string mode, string error, MetaPublishRequest request, string? uploadMode = null)
        => new()
        {
            Success = false,
            Platform = "Facebook",
            ContentType = PlatformThumbnailContentTypes.LongVideo,
            ContentKind = PlatformThumbnailContentTypes.LongVideo,
            Mode = mode,
            UploadMode = uploadMode,
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
        await ThumbnailPublishDiagnosticsWriter.WriteFromMetaResultAsync(outputDirectory, result, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "facebook-video-thumbnail-diagnostics.json"), JsonSerializer.Serialize(new
        {
            thumbnailPath = result.ThumbnailPathUsed ?? result.UploadedThumbnailPath,
            exists = !string.IsNullOrWhiteSpace(result.ThumbnailPathUsed ?? result.UploadedThumbnailPath) && File.Exists(result.ThumbnailPathUsed ?? result.UploadedThumbnailPath!),
            fileSizeBytes = !string.IsNullOrWhiteSpace(result.ThumbnailPathUsed ?? result.UploadedThumbnailPath) && File.Exists(result.ThumbnailPathUsed ?? result.UploadedThumbnailPath!) ? new FileInfo(result.ThumbnailPathUsed ?? result.UploadedThumbnailPath!).Length : 0,
            uploadAttempted = result.ThumbnailUploadAttempted,
            uploadSuccess = result.ThumbnailUploadSuccess,
            graphResponse = result.PostId ?? result.VideoId,
            error = result.Error ?? result.ThumbnailWarning ?? result.Warning
        }, JsonOptions), cancellationToken);
        return result;
    }

    private static Task WriteDiagnosticsAsync(string outputDirectory, FacebookFullVideoUploadDiagnostics diagnostics, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(outputDirectory, "facebook-full-video-upload-diagnostics.json"), JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);

    private static string NormalizeMode(string? mode)
        => string.Equals(mode, "Public", StringComparison.OrdinalIgnoreCase) ? "Public"
            : string.Equals(mode, "Private", StringComparison.OrdinalIgnoreCase) ? "Private"
            : string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled"
            : "DryRun";

    private sealed record SimpleUploadAttempt(MetaPublishResult? Result, bool ShouldRetryResumable);
    private sealed record ThumbnailUpdateResult(bool Success, string? Warning);

    private sealed class FacebookVideoResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }

    private sealed class FacebookResumableStartResponse
    {
        [JsonPropertyName("upload_session_id")]
        public string UploadSessionId { get; init; } = string.Empty;

        [JsonPropertyName("video_id")]
        public string VideoId { get; init; } = string.Empty;

        [JsonPropertyName("start_offset")]
        public string StartOffset { get; init; } = "0";

        [JsonPropertyName("end_offset")]
        public string EndOffset { get; init; } = "0";
    }

    private sealed class FacebookResumableTransferResponse
    {
        [JsonPropertyName("start_offset")]
        public string StartOffset { get; init; } = "0";

        [JsonPropertyName("end_offset")]
        public string EndOffset { get; init; } = "0";
    }

    private sealed class FacebookFullVideoUploadDiagnostics
    {
        public long FileSizeBytes { get; init; }
        public string UploadMode { get; set; } = "Simple";
        public int ChunkSizeBytes { get; init; }
        public string? UploadSessionId { get; set; }
        public string? VideoId { get; set; }
        public int ChunksUploaded { get; set; }
        public List<FacebookUploadProgress> Progress { get; } = [];
        public bool FinishSuccess { get; set; }
        public bool ThumbnailAttempted { get; set; }
        public bool ThumbnailSuccess { get; set; }
        public List<string> Warnings { get; } = [];
        public List<string> Errors { get; } = [];
    }

    private sealed record FacebookUploadProgress(string? StartOffset, string? EndOffset);
}
