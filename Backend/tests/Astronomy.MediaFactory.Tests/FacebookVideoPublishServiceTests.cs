using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class FacebookVideoPublishServiceTests
{
    [Fact]
    public async Task FileBelowThreshold_UsesSimpleUpload()
    {
        using var workspace = CreateWorkspace(out var request, videoSizeBytes: 10);
        var handler = new FacebookLongVideoHandler();
        var service = CreateService(workspace, handler, new MetaPublishingOptions { Mode = "Public", FacebookSimpleUploadMaxBytes = 100 });

        var result = await service.PublishVideoAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Simple", result.UploadMode);
        Assert.Equal(new[] { "simple" }, handler.Requests.Select(x => x.Phase).ToArray());
        var diagnostics = await ReadDiagnosticsAsync(request.VideoPath);
        Assert.Equal("Simple", diagnostics.RootElement.GetProperty("uploadMode").GetString());
    }

    [Fact]
    public async Task FileAboveThreshold_UsesResumableUpload()
    {
        using var workspace = CreateWorkspace(out var request, videoSizeBytes: 25);
        var handler = new FacebookLongVideoHandler();
        var service = CreateService(workspace, handler, new MetaPublishingOptions { Mode = "Public", FacebookSimpleUploadMaxBytes = 10, FacebookUploadChunkSizeBytes = 8 });

        var result = await service.PublishVideoAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Resumable", result.UploadMode);
        Assert.Equal("long-video-123", result.VideoId);
        Assert.Equal(new[] { "resumable-start", "resumable-transfer", "resumable-transfer", "resumable-transfer", "resumable-transfer", "resumable-finish", "thumbnail-update" }, handler.Requests.Select(x => x.Phase).ToArray());
    }

    [Fact]
    public async Task SimpleUpload413_RetriesWithResumableUpload()
    {
        using var workspace = CreateWorkspace(out var request, videoSizeBytes: 25);
        var handler = new FacebookLongVideoHandler { SimpleUploadStatusCode = HttpStatusCode.RequestEntityTooLarge };
        var service = CreateService(workspace, handler, new MetaPublishingOptions { Mode = "Public", FacebookSimpleUploadMaxBytes = 100, FacebookUploadChunkSizeBytes = 25 });

        var result = await service.PublishVideoAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Resumable", result.UploadMode);
        Assert.Equal(new[] { "simple", "resumable-start", "resumable-transfer", "resumable-finish", "thumbnail-update" }, handler.Requests.Select(x => x.Phase).ToArray());
        var diagnostics = await ReadDiagnosticsAsync(request.VideoPath);
        Assert.Contains("413 Payload Too Large", diagnostics.RootElement.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public async Task ChunkTransfer_LoopsUntilOffsetsComplete()
    {
        using var workspace = CreateWorkspace(out var request, videoSizeBytes: 21);
        var handler = new FacebookLongVideoHandler();
        var service = CreateService(workspace, handler, new MetaPublishingOptions { Mode = "Public", FacebookSimpleUploadMaxBytes = 1, FacebookUploadChunkSizeBytes = 5 });

        var result = await service.PublishVideoAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(5, handler.Requests.Count(x => x.Phase == "resumable-transfer"));
        var diagnostics = await ReadDiagnosticsAsync(request.VideoPath);
        Assert.Equal(5, diagnostics.RootElement.GetProperty("chunksUploaded").GetInt32());
        Assert.Equal("21", diagnostics.RootElement.GetProperty("progress").EnumerateArray().Last().GetProperty("startOffset").GetString());
        Assert.Equal("21", diagnostics.RootElement.GetProperty("progress").EnumerateArray().Last().GetProperty("endOffset").GetString());
    }

    [Fact]
    public async Task FinishPublishesVideo()
    {
        using var workspace = CreateWorkspace(out var request, videoSizeBytes: 12);
        var handler = new FacebookLongVideoHandler();
        var service = CreateService(workspace, handler, new MetaPublishingOptions { Mode = "Public", FacebookSimpleUploadMaxBytes = 1, FacebookUploadChunkSizeBytes = 12 });

        var result = await service.PublishVideoAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        var finish = Assert.Single(handler.Requests.Where(x => x.Phase == "resumable-finish"));
        Assert.Contains("upload_phase=finish", finish.Body);
        Assert.Contains("published=true", finish.Body);
        Assert.Contains("title=Amazing+Saturn+Tonight", finish.Body);
        Assert.Contains("description=Look+up", finish.Body);
        var diagnostics = await ReadDiagnosticsAsync(request.VideoPath);
        Assert.True(diagnostics.RootElement.GetProperty("finishSuccess").GetBoolean());
    }

    [Fact]
    public async Task ThumbnailFailure_DoesNotFailCompletedResumableUpload()
    {
        using var workspace = CreateWorkspace(out var request, videoSizeBytes: 12);
        var handler = new FacebookLongVideoHandler { ThumbnailUpdateStatusCode = HttpStatusCode.NotFound };
        var service = CreateService(workspace, handler, new MetaPublishingOptions { Mode = "Public", FacebookSimpleUploadMaxBytes = 1, FacebookUploadChunkSizeBytes = 12 });

        var result = await service.PublishVideoAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.ThumbnailUploadAttempted);
        Assert.False(result.ThumbnailUploadSuccess);
        Assert.Contains("custom thumbnail", result.Warning, StringComparison.OrdinalIgnoreCase);
        var diagnostics = await ReadDiagnosticsAsync(request.VideoPath);
        Assert.True(diagnostics.RootElement.GetProperty("thumbnailAttempted").GetBoolean());
        Assert.False(diagnostics.RootElement.GetProperty("thumbnailSuccess").GetBoolean());
    }

    private static TempMetaWorkspace CreateWorkspace(out MetaPublishRequest request, int videoSizeBytes)
    {
        var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        _ = repository;
        var output = workspace.OutputDirectory(run);
        var videoPath = Path.Combine(output, "final-video.mp4");
        File.WriteAllBytes(videoPath, Enumerable.Range(0, videoSizeBytes).Select(x => (byte)(x % 255)).ToArray());
        request = new MetaPublishRequest
        {
            PipelineRunId = run.Id,
            Platform = "Facebook",
            VideoPath = videoPath,
            PlatformThumbnailPath = Path.Combine(output, "thumbnails", "thumbnail-long.jpg"),
            ThumbnailSource = ThumbnailSources.GeneratedThumbnail,
            Caption = "Look up for a fast tour of tonight's best targets.",
            ShortTitle = "Amazing Saturn Tonight",
            IsReel = false
        };
        return workspace;
    }

    private static FacebookVideoPublishService CreateService(TempMetaWorkspace workspace, FacebookLongVideoHandler handler, MetaPublishingOptions options)
        => new(
            new HttpClient(handler),
            Options.Create(new MetaOptions { TokenFilePath = workspace.TokenPath }),
            Options.Create(options),
            NullLogger<FacebookVideoPublishService>.Instance);

    private static Task<JsonDocument> ReadDiagnosticsAsync(string videoPath)
        => JsonDocument.ParseAsync(File.OpenRead(Path.Combine(Path.GetDirectoryName(videoPath)!, "facebook-full-video-upload-diagnostics.json"))).AsTask();
}

internal sealed class FacebookLongVideoHandler : HttpMessageHandler
{
    public HttpStatusCode SimpleUploadStatusCode { get; set; } = HttpStatusCode.OK;
    public HttpStatusCode ThumbnailUpdateStatusCode { get; set; } = HttpStatusCode.OK;
    public List<(string Phase, string Body)> Requests { get; } = [];
    private long _fileSize;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        if (request.RequestUri!.AbsolutePath.EndsWith("/thumbnails", StringComparison.OrdinalIgnoreCase))
        {
            Requests.Add(("thumbnail-update", body));
            return ThumbnailUpdateStatusCode == HttpStatusCode.OK
                ? JsonResponse(new { success = true })
                : new HttpResponseMessage(ThumbnailUpdateStatusCode) { Content = JsonContent.Create(new { error = new { message = "unsupported thumbnail update" } }) };
        }

        if (body.Contains("upload_phase=start", StringComparison.Ordinal))
        {
            Requests.Add(("resumable-start", body));
            _fileSize = ExtractLong(body, "file_size");
            return JsonResponse(new { upload_session_id = "session-123", video_id = "long-video-123", start_offset = "0", end_offset = _fileSize.ToString() });
        }

        if (body.Contains("upload_phase=transfer", StringComparison.Ordinal))
        {
            Requests.Add(("resumable-transfer", body));
            var start = ExtractLong(body, "start_offset");
            // Multipart framing makes Content-Length unsuitable for chunk length, so infer progress from the chunk part body.
            var next = Math.Min(_fileSize, start + InferChunkPayloadBytes(body));
            return JsonResponse(new { start_offset = next.ToString(), end_offset = _fileSize.ToString() });
        }

        if (body.Contains("upload_phase=finish", StringComparison.Ordinal))
        {
            Requests.Add(("resumable-finish", body));
            return JsonResponse(new { success = true });
        }

        Requests.Add(("simple", body));
        return SimpleUploadStatusCode == HttpStatusCode.OK
            ? JsonResponse(new { id = "simple-video-123" })
            : new HttpResponseMessage(SimpleUploadStatusCode) { Content = JsonContent.Create(new { error = new { message = "payload too large" } }) };
    }

    private static long ExtractLong(string body, string name)
    {
        var marker = $"name=\"{name}\"";
        var index = body.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            marker = name + "=";
            index = body.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return 0;
            index += marker.Length;
            var end = body.IndexOf('&', index);
            var value = end < 0 ? body[index..] : body[index..end];
            return long.Parse(Uri.UnescapeDataString(value));
        }

        var valueStart = body.IndexOf("\r\n\r\n", index, StringComparison.Ordinal) + 4;
        var valueEnd = body.IndexOf("\r\n", valueStart, StringComparison.Ordinal);
        return long.Parse(body[valueStart..valueEnd]);
    }

    private static int InferChunkPayloadBytes(string body)
    {
        var marker = "name=\"video_file_chunk\"";
        var index = body.IndexOf(marker, StringComparison.Ordinal);
        var valueStart = body.IndexOf("\r\n\r\n", index, StringComparison.Ordinal) + 4;
        var valueEnd = body.IndexOf("\r\n--", valueStart, StringComparison.Ordinal);
        return valueEnd - valueStart;
    }

    private static HttpResponseMessage JsonResponse(object payload) => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
}
