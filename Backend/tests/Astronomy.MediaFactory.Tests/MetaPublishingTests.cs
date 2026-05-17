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

public sealed class MetaPublishingTests
{
    [Fact]
    public async Task DryRun_WritesFacebookPayload_AndDoesNotCallApi()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal("DryRun", result.Mode);
        Assert.Empty(handler.Requests);
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-publish-payload.json")));
    }


    [Fact]
    public async Task PosterFrameFallback_GeneratesMetaVideo_WhenMetaCoverUnsupported()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var posterService = new FakePosterFrameFallbackService();
        var service = MetaPublishingTestFactory.CreateMetaService(
            workspace,
            repository,
            new TrackingMetaHandler(),
            new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true, UsePosterFrameFallbackForReels = true, PosterFrameDurationSeconds = 0.75d },
            posterFrameFallbackService: posterService);

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();
        var output = workspace.OutputDirectory(run);

        Assert.True(result.Success);
        Assert.True(result.PosterFrameApplied);
        Assert.EndsWith(Path.Combine("shorts", "short-video-meta.mp4"), result.PosterFrameVideoPath);
        Assert.True(File.Exists(Path.Combine(output, "shorts", "short-video-meta.mp4")));
        var reportJson = await File.ReadAllTextAsync(Path.Combine(output, "meta-poster-frame-report.json"));
        Assert.Contains("\"posterFrameApplied\": true", reportJson);
        Assert.Contains("Custom cover unsupported/rejected by Meta endpoint", reportJson);
        Assert.Equal(Path.Combine(output, "shorts", "short-video.mp4"), posterService.InputShortVideoPath);
        Assert.Equal(Path.Combine(output, "thumbnails", "thumbnail-short.jpg"), posterService.PosterFrameImagePath);
        Assert.Contains("Meta custom cover was not accepted. Poster-frame fallback video was uploaded.", result.Warnings);
    }

    [Fact]
    public async Task MetaPublish_UsesMetaVideo_ForFacebookAndInstagramReels()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var storage = new RecordingPublicMediaStorageService("https://cdn.example.test/short-video.mp4");
        var service = MetaPublishingTestFactory.CreateMetaService(
            workspace,
            repository,
            new TrackingMetaHandler { BaseAddress = "https://upload.example.test" },
            new MetaPublishingOptions
            {
                Enabled = true,
                Mode = "DryRun",
                PublishFacebookReel = true,
                PublishInstagramReel = true,
                PublicMediaBaseUrl = "https://cdn.example.test/short-video.mp4",
                UsePosterFrameFallbackForReels = true
            },
            publicMediaStorageService: storage,
            posterFrameFallbackService: new FakePosterFrameFallbackService());

        var results = await service.PublishForPipelineRunAsync(run.Id, "all", CancellationToken.None);
        var output = workspace.OutputDirectory(run);
        var metaVideoPath = Path.Combine(output, "shorts", "short-video-meta.mp4");

        Assert.All(results, result => Assert.Equal(metaVideoPath, result.PosterFrameVideoPath));
        Assert.Contains("short-video-meta.mp4", await File.ReadAllTextAsync(Path.Combine(output, "facebook-reel-publish-payload.json")));
        Assert.Contains("short-video-meta.mp4", await File.ReadAllTextAsync(Path.Combine(output, "instagram-reel-publish-payload.json")));
    }

    [Fact]
    public async Task MissingMetaTokenFile_FailsClearly()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: false);
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, new TrackingMetaHandler(), new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Equal(FacebookReelPublishService.IncompleteTokenMessage, result.Error);
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-publish-result.json")));
    }

    [Fact]
    public async Task MissingShortVideo_FailsClearly_InRealMode()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: false, createToken: true);
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, new TrackingMetaHandler(), new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Contains("shorts/short-video.mp4", result.Error);
    }

    [Fact]
    public async Task Caption_IncludesHookLocationObjectsAndHashtags()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, new TrackingMetaHandler(), new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true, CaptionHashtagSuffix = "#Astronomy #NightSky #Stargazing" });

        _ = await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None);
        var caption = await File.ReadAllTextAsync(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-caption.txt"));

        Assert.Contains("Amazing Saturn Tonight", caption);
        Assert.Contains("Location: Denver", caption);
        Assert.Contains("Saturn", caption);
        Assert.Contains("Moon", caption);
        Assert.Contains("#Astronomy #NightSky #Stargazing", caption);
    }

    [Fact]
    public async Task PublicMode_CallsStartUploadFinishInOrder()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { BaseAddress = "https://upload.example.test" };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal("video-123", result.VideoId);
        Assert.Equal("post-456", result.PostId);
        Assert.Equal(new[] { "start", "upload", "finish" }, handler.Requests.Take(3).Select(x => x.Phase).ToArray());
        Assert.Contains("Authorization", handler.Requests[1].Headers.Keys);
        Assert.Equal("0", handler.Requests[1].Headers["offset"]);
        Assert.Equal(new FileInfo(Path.Combine(workspace.OutputDirectory(run), "shorts", "short-video.mp4")).Length.ToString(System.Globalization.CultureInfo.InvariantCulture), handler.Requests[1].Headers["file_size"]);
        Assert.Equal("application/octet-stream", handler.Requests[1].ContentHeaders["Content-Type"]);
    }


    [Fact]
    public async Task FinishRequest_IncludesPublishedStateAndTitle()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { VerificationStatuses = new Queue<string>(new[] { "PUBLISHED" }) };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true, FacebookReelProcessingPollSeconds = 0, FacebookReelProcessingMaxAttempts = 1 });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.True(result.PublishedVerified);
        var finish = handler.Requests.Single(x => x.Phase == "finish");
        Assert.Contains("upload_phase=finish", finish.Body);
        Assert.Contains("video_id=video-123", finish.Body);
        Assert.Contains("video_state=PUBLISHED", finish.Body);
        Assert.Contains("description=", finish.Body);
        Assert.Contains("title=Amazing+Saturn+Tonight", finish.Body);
    }

    [Fact]
    public async Task FinishRequest_MissingPublishedStateFailsTestGuard()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { VerificationStatuses = new Queue<string>(new[] { "PUBLISHED" }) };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true, FacebookReelProcessingPollSeconds = 0, FacebookReelProcessingMaxAttempts = 1 });

        _ = await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None);

        var finish = handler.Requests.Single(x => x.Phase == "finish");
        Assert.DoesNotContain("video_state=", finish.Body.Replace("video_state=PUBLISHED", string.Empty, StringComparison.Ordinal));
        Assert.Contains("video_state=PUBLISHED", finish.Body);
    }

    [Fact]
    public async Task VerificationPolling_RetriesUntilStatusAvailable()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { VerificationStatuses = new Queue<string>(new[] { "PROCESSING", "PUBLISHED" }) };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true, FacebookReelProcessingPollSeconds = 0, FacebookReelProcessingMaxAttempts = 3 });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.True(result.PublishedVerified);
        Assert.True(handler.Requests.Count(x => x.Phase == "verify-video") >= 2);
    }

    [Fact]
    public async Task PublishDiagnostics_AreWrittenWithoutAccessToken()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { VerificationStatuses = new Queue<string>(new[] { "PUBLISHED" }) };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true, FacebookReelProcessingPollSeconds = 0, FacebookReelProcessingMaxAttempts = 1 });

        _ = await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None);

        var resultJson = await File.ReadAllTextAsync(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-publish-result.json"));
        Assert.Contains("startResponse", resultJson);
        Assert.Contains("uploadResponseStatus", resultJson);
        Assert.Contains("finishResponse", resultJson);
        Assert.Contains("publishedVerified", resultJson);
        Assert.DoesNotContain("page-token-secret", resultJson);
    }

    [Fact]
    public async Task AppModeWarning_IsIncludedWhenVisibilityIsNotVerified()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { VerificationStatuses = new Queue<string>(new[] { "PROCESSING" }) };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true, FacebookReelProcessingPollSeconds = 0, FacebookReelProcessingMaxAttempts = 1 });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.False(result.PublishedVerified);
        Assert.Contains(FacebookReelPublishService.MetaAppDevelopmentModeWarning, result.Warnings);
    }


    [Fact]
    public async Task FacebookProcessingUntilTimeout_ReturnsSuccessWithWarningAndDiagnostics()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { VerificationStatuses = new Queue<string>(new[] { "PROCESSING", "PROCESSING", "PROCESSING", "PROCESSING" }) };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true, FacebookVerificationPollDelaySeconds = 0, FacebookVerificationPollAttempts = 2 });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal("Facebook", result.Platform);
        Assert.Equal("video-123", result.VideoId);
        Assert.Equal("https://facebook.example.test/reel/video-123", result.Url);
        Assert.False(result.PublishedVerified);
        Assert.Equal("Facebook Reel uploaded but public visibility could not be verified before processing timeout.", result.Warning);

        var resultJson = await File.ReadAllTextAsync(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-publish-result.json"));
        Assert.Contains("facebookUploadStatus", resultJson);
        Assert.Contains("facebookVideoId", resultJson);
        Assert.Contains("facebookPermalink", resultJson);
        Assert.Contains("verificationTimedOut", resultJson);
        Assert.Contains("processingPollAttempts", resultJson);
        Assert.Contains("publishedVerified", resultJson);
    }

    [Fact]
    public async Task FacebookVerificationTaskCanceled_ReturnsSuccessWithWarning()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { ThrowTaskCanceledDuringVerification = true };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true, FacebookVerificationPollDelaySeconds = 0, FacebookVerificationPollAttempts = 1 });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal("video-123", result.VideoId);
        Assert.False(result.PublishedVerified);
        Assert.Equal("Facebook Reel uploaded but public visibility could not be verified before processing timeout.", result.Warning);
    }

    [Fact]
    public async Task FacebookStartMissingVideoId_FailsBeforeUpload()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { StartMissingVideoId = true };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Contains("did not return video_id and upload_url", result.Error);
        Assert.DoesNotContain(handler.Requests, request => request.Phase == "upload");
    }

    [Fact]
    public async Task UploadFailure_IncludesResponseBody()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { FailUpload = true };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Contains("Facebook Reel binary upload failed with status 400", result.Error);
        Assert.Contains("missing offset", result.Error);
    }

    [Fact]
    public async Task ResultJson_IsWritten()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, new TrackingMetaHandler(), new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true });

        _ = await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-publish-result.json")));
    }

    [Fact]
    public async Task UnsupportedAsset_DoesNotInvokeFacebookOrInstagram()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true, PublishInstagramReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "invalid", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Contains("Supported assets are facebook-reel, instagram-reel, and all", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task InstagramDryRun_WritesPayload_AndDoesNotCallApi()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = false, PublishInstagramReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal("Instagram", result.Platform);
        Assert.Equal("DryRun", result.Mode);
        Assert.Empty(handler.Requests);
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "instagram-reel-publish-payload.json")));
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "instagram-reel-caption.txt")));
    }

    [Fact]
    public async Task InstagramMissingBusinessAccountId_FailsClearly()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        File.WriteAllText(workspace.TokenPath, JsonSerializer.Serialize(new MetaOAuthTokenFile("113", "AstroPulse", "page-token-secret", null, "astro", "user-token-secret", DateTimeOffset.UtcNow)));
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, new TrackingMetaHandler(), new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = false, PublishInstagramReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Equal(InstagramReelPublishService.MissingInstagramBusinessAccountMessage, result.Error);
    }

    [Fact]
    public async Task InstagramRealMode_MissingStorageService_FailsBeforeApi()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = false, PublishInstagramReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Equal("PublicMediaStorage is not configured for Instagram Reel publishing.", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task InstagramContainerCreation_UsesReelsMediaType()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = false, PublishInstagramReel = true, PublicMediaBaseUrl = "https://cdn.example.test/short-video.mp4", InstagramContainerPollSeconds = 0, InstagramContainerMaxAttempts = 1 });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        var container = handler.Requests.Single(x => x.Phase == "ig-container");
        Assert.Contains("media_type=REELS", container.Body);
        Assert.Contains("video_url=https%3A%2F%2Fcdn.example.test%2Fshort-video.mp4", container.Body);
    }

    [Fact]
    public async Task InstagramPolling_WaitsUntilFinished()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { InstagramContainerStatuses = new Queue<string>(new[] { "IN_PROGRESS", "FINISHED" }) };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = false, PublishInstagramReel = true, PublicMediaBaseUrl = "https://cdn.example.test/short-video.mp4", InstagramContainerPollSeconds = 0, InstagramContainerMaxAttempts = 3 });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal(2, handler.Requests.Count(x => x.Phase == "ig-poll"));
    }

    [Fact]
    public async Task InstagramMediaPublish_IsCalledAfterFinished()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { InstagramContainerStatuses = new Queue<string>(new[] { "FINISHED" }) };
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = false, PublishInstagramReel = true, PublicMediaBaseUrl = "https://cdn.example.test/short-video.mp4", InstagramContainerPollSeconds = 0, InstagramContainerMaxAttempts = 1 });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal(new[] { "head-public-url", "ig-container", "ig-poll", "ig-publish", "ig-details" }, handler.Requests.Select(x => x.Phase).ToArray());
        Assert.Equal("ig-media-456", result.VideoId);
    }

    [Fact]
    public async Task AssetInstagramReel_PublishesOnlyInstagram()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true, PublishInstagramReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.Equal("Instagram", result.Platform);
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "instagram-reel-publish-payload.json")));
        Assert.False(File.Exists(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-publish-payload.json")));
    }

    [Fact]
    public async Task AssetAll_PublishesFacebookAndInstagramReelsOnly()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true, PublishInstagramReel = true, PublishFacebookFullVideo = true, PublishInstagramFullVideo = true });

        var results = await service.PublishForPipelineRunAsync(run.Id, "all", CancellationToken.None);

        Assert.Equal(new[] { "Facebook", "Instagram" }, results.Select(x => x.Platform).ToArray());
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-publish-payload.json")));
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "instagram-reel-publish-payload.json")));
        var facebookPayload = await File.ReadAllTextAsync(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-publish-payload.json"));
        var instagramPayload = await File.ReadAllTextAsync(Path.Combine(workspace.OutputDirectory(run), "instagram-reel-publish-payload.json"));
        Assert.Contains("shorts", facebookPayload);
        Assert.Contains("short-video.mp4", facebookPayload);
        Assert.DoesNotContain("final-video.mp4", facebookPayload);
        Assert.Contains("shorts", instagramPayload);
        Assert.Contains("short-video.mp4", instagramPayload);
        Assert.DoesNotContain("final-video.mp4", instagramPayload);
    }


    [Fact]
    public async Task InstagramPublicMediaUpload_WritesConfiguredBlobPath()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var storage = new RecordingPublicMediaStorageService("https://storage.example.test/meta-media/astronomy/reels/" + run.Id + "/short-video.mp4?sig=secret");
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = false, PublishInstagramReel = true, InstagramContainerPollSeconds = 0, InstagramContainerMaxAttempts = 1 }, storage);

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal(Path.Combine(workspace.OutputDirectory(run), "shorts", "short-video.mp4"), storage.LocalFilePath);
        Assert.Equal(run.Id, storage.PipelineRunId);
        var diagnostics = await File.ReadAllTextAsync(Path.Combine(workspace.OutputDirectory(run), "public-media-upload-result.json"));
        Assert.Contains($"astronomy/reels/{run.Id}/short-video.mp4", diagnostics);
        Assert.DoesNotContain("sig=secret", diagnostics);
    }

    [Fact]
    public async Task InstagramMediaCall_ReceivesUploadedVideoUrl()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var storage = new RecordingPublicMediaStorageService("https://storage.example.test/meta-media/astronomy/reels/" + run.Id + "/short-video.mp4?sig=secret");
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = false, PublishInstagramReel = true, InstagramContainerPollSeconds = 0, InstagramContainerMaxAttempts = 1 }, storage);

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        var container = handler.Requests.Single(x => x.Phase == "ig-container");
        Assert.Contains("video_url=https%3A%2F%2Fstorage.example.test%2Fmeta-media%2Fastronomy%2Freels%2F", container.Body);
        Assert.Contains("%2Fshort-video.mp4%3Fsig%3Dsecret", container.Body);
    }


    [Fact]
    public async Task InstagramMediaCall_ReceivesPublicCoverUrl()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var storage = new RecordingPublicMediaStorageService("https://storage.example.test/meta-media/astronomy/reels/" + run.Id + "/short-video.mp4?sig=secret");
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = false, PublishInstagramReel = true, InstagramContainerPollSeconds = 0, InstagramContainerMaxAttempts = 1 }, storage);

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal(Path.Combine(workspace.OutputDirectory(run), "thumbnails", "thumbnail-short.jpg"), storage.ThumbnailLocalFilePath);
        var container = handler.Requests.Single(x => x.Phase == "ig-container");
        Assert.Contains("cover_url=https%3A%2F%2Fstorage.example.test%2Fmeta-media%2Fastronomy%2Freels%2F", container.Body);
        Assert.Contains("%2Fthumbnail-short.jpg%3Fsig%3Dsecret", container.Body);
        Assert.DoesNotContain("thumbnail-short.jpg&", container.Body);
    }

    [Fact]
    public async Task InstagramRealMode_MissingStorageConfig_FailsClearly()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var storage = new RecordingPublicMediaStorageService("", success: false, error: "PublicMediaStorage:ConnectionString is required for Azure Blob public media uploads.");
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = false, PublishInstagramReel = true }, storage);

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Equal("PublicMediaStorage:ConnectionString is required for Azure Blob public media uploads.", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FacebookPublishing_DoesNotUsePublicMediaStorage()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { VerificationStatuses = new Queue<string>(new[] { "PUBLISHED" }) };
        var storage = new RecordingPublicMediaStorageService("https://storage.example.test/unused.mp4?sig=secret");
        var service = MetaPublishingTestFactory.CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true, PublishInstagramReel = false, FacebookReelProcessingPollSeconds = 0, FacebookReelProcessingMaxAttempts = 1 }, storage);

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.False(storage.Called);
        Assert.Equal(new[] { "start", "upload", "finish", "verify-video" }, handler.Requests.Select(x => x.Phase).ToArray());
    }

    [Fact]
    public async Task YouTubePublishing_RemainsUnaffected()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var youtube = new TrackingYouTubePublishService();
        var content = new ContentPublishService(
            repository,
            youtube,
            new FixedTokenHealthService(),
            Options.Create(new PublishingOptions { Enabled = true, Mode = "DryRun" }),
            Options.Create(new YouTubeOptions()),
            Options.Create(new TokenHealthOptions { Enabled = false }),
            Options.Create(new MaintenanceOptions { WorkingDirectory = workspace.Root }),
            NullLogger<ContentPublishService>.Instance);

        var results = await content.PublishForPipelineRunAsync(run.Id, "short", CancellationToken.None);

        Assert.All(results, result => Assert.Equal("YouTube", result.Platform));
        Assert.True(youtube.Called);
    }

}

internal static class MetaPublishingTestFactory
{
    public static MetaPublishService CreateMetaService(TempMetaWorkspace workspace, IPipelineRepository repository, TrackingMetaHandler handler, MetaPublishingOptions options, IPublicMediaStorageService? publicMediaStorageService = null, IMetaPosterFrameFallbackService? posterFrameFallbackService = null)
    {
        publicMediaStorageService ??= string.IsNullOrWhiteSpace(options.PublicMediaBaseUrl) ? null : new FixedPublicMediaStorageService(options.PublicMediaBaseUrl);
        var facebook = new FacebookReelPublishService(
            new HttpClient(handler),
            Options.Create(new MetaOptions { TokenFilePath = workspace.TokenPath }),
            Options.Create(options),
            Options.Create(new RenderingOptions { FfprobePath = "missing-ffprobe-for-tests" }),
            NullLogger<FacebookReelPublishService>.Instance);

        var instagram = new InstagramReelPublishService(
            new HttpClient(handler),
            Options.Create(new MetaOptions { TokenFilePath = workspace.TokenPath }),
            Options.Create(options),
            Options.Create(new RenderingOptions { FfprobePath = "missing-ffprobe-for-tests" }),
            NullLogger<InstagramReelPublishService>.Instance,
            publicMediaStorageService,
            publicMediaStorageService is null ? null : new MetaThumbnailAssetPublisher(publicMediaStorageService));

        return new MetaPublishService(
            repository,
            facebook,
            instagram,
            new FixedTokenHealthService(),
            Options.Create(options),
            Options.Create(new TokenHealthOptions { Enabled = false }),
            Options.Create(new MaintenanceOptions { WorkingDirectory = workspace.Root }),
            NullLogger<MetaPublishService>.Instance,
            posterFrameFallbackService: posterFrameFallbackService);
    }
}

internal sealed class FixedPublicMediaStorageService : IPublicMediaStorageService
{
    private readonly string _publicUrl;

    public FixedPublicMediaStorageService(string publicUrl) => _publicUrl = publicUrl;

    public Task<PublicMediaUploadResult> UploadForInstagramAsync(string localFilePath, Guid pipelineRunId, CancellationToken cancellationToken)
        => UploadPublicAssetAsync(localFilePath, pipelineRunId, "short-video.mp4", "video/mp4", cancellationToken);

    public Task<PublicMediaUploadResult> UploadPublicAssetAsync(string localFilePath, Guid pipelineRunId, string assetFileName, string contentType, CancellationToken cancellationToken)
        => Task.FromResult(new PublicMediaUploadResult { Success = true, PublicUrl = _publicUrl.Replace("short-video.mp4", assetFileName), BlobName = $"astronomy/reels/{pipelineRunId}/{assetFileName}", ExpiresUtc = DateTime.UtcNow.AddHours(24) });
}

internal sealed class RecordingPublicMediaStorageService : IPublicMediaStorageService
{
    private readonly string _publicUrl;
    private readonly bool _success;
    private readonly string _error;

    public bool Called { get; private set; }
    public string? LocalFilePath { get; private set; }
    public string? ThumbnailLocalFilePath { get; private set; }
    public Guid PipelineRunId { get; private set; }

    public RecordingPublicMediaStorageService(string publicUrl, bool success = true, string error = "")
    {
        _publicUrl = publicUrl;
        _success = success;
        _error = error;
    }

    public Task<PublicMediaUploadResult> UploadForInstagramAsync(string localFilePath, Guid pipelineRunId, CancellationToken cancellationToken)
        => UploadPublicAssetAsync(localFilePath, pipelineRunId, "short-video.mp4", "video/mp4", cancellationToken);

    public Task<PublicMediaUploadResult> UploadPublicAssetAsync(string localFilePath, Guid pipelineRunId, string assetFileName, string contentType, CancellationToken cancellationToken)
    {
        Called = true;
        if (string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            ThumbnailLocalFilePath = localFilePath;
        }
        else
        {
            LocalFilePath = localFilePath;
        }
        PipelineRunId = pipelineRunId;
        return Task.FromResult(new PublicMediaUploadResult
        {
            Success = _success,
            PublicUrl = _publicUrl.Replace("short-video.mp4", assetFileName),
            BlobName = $"astronomy/reels/{pipelineRunId}/{assetFileName}",
            Error = _error,
            ExpiresUtc = DateTime.UtcNow.AddHours(24)
        });
    }
}


internal sealed class FakePosterFrameFallbackService : IMetaPosterFrameFallbackService
{
    public string? InputShortVideoPath { get; private set; }
    public string? PosterFrameImagePath { get; private set; }

    public async Task<MetaPosterFrameFallbackResult> ApplyAsync(string outputDirectory, string inputShortVideoPath, string posterFrameImagePath, double durationSeconds, CancellationToken cancellationToken)
    {
        InputShortVideoPath = inputShortVideoPath;
        PosterFrameImagePath = posterFrameImagePath;
        var outputMetaVideoPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputShortVideoPath))!, "short-video-meta.mp4");
        File.Copy(inputShortVideoPath, outputMetaVideoPath, overwrite: true);
        var result = new MetaPosterFrameFallbackResult(
            true,
            posterFrameImagePath,
            Math.Clamp(durationSeconds, 0.5d, 1.0d),
            inputShortVideoPath,
            outputMetaVideoPath,
            MetaPosterFrameFallbackService.Reason);
        await MetaPosterFrameFallbackService.WriteReportAsync(outputDirectory, result, cancellationToken);
        return result;
    }
}

public sealed class TempMetaWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "meta-publish-tests", Guid.NewGuid().ToString("N"));
    public string TokenPath => Path.Combine(Root, "meta-oauth-token.json");

    private static readonly byte[] TinyPngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    public TempMetaWorkspace() => Directory.CreateDirectory(Root);

    public InMemoryPipelineRepository CreateRepositoryWithRun(out PipelineRun run, bool createVideo, bool createToken)
    {
        run = new PipelineRun { RunDate = new DateOnly(2026, 5, 7), ContentType = ContentType.DailySkyGuide, LocationName = "Denver", TimeZone = "America/Denver", Status = PipelineRunStatus.Succeeded };
        var output = OutputDirectory(run);
        Directory.CreateDirectory(Path.Combine(output, "shorts"));
        File.WriteAllText(Path.Combine(output, "seo-metadata.json"), JsonSerializer.Serialize(new SeoMetadataResult { Title = "Root Title", Description = "Root description", TagsCsv = "astronomy" }));
        File.WriteAllText(Path.Combine(output, "shorts", "seo-metadata.json"), JsonSerializer.Serialize(new SeoMetadataResult { Title = "Amazing Saturn Tonight", Description = "Look up for a fast tour of tonight's best targets.", TagsCsv = "saturn,moon" }));
        File.WriteAllText(Path.Combine(output, "selected-visible-objects.json"), "[{\"objectName\":\"Saturn\"},{\"objectName\":\"Moon\"}]");
        File.WriteAllText(Path.Combine(output, "pre-publish-validation-report.json"), "{\"passed\":true,\"errors\":[],\"warnings\":[]}");
        Directory.CreateDirectory(Path.Combine(output, "thumbnails"));
        File.WriteAllBytes(Path.Combine(output, "thumbnails", "thumbnail-long.jpg"), TinyPngBytes);
        File.WriteAllBytes(Path.Combine(output, "thumbnails", "thumbnail-short.jpg"), TinyPngBytes);
        File.WriteAllText(Path.Combine(output, "thumbnail-1.png"), "thumb");
        File.WriteAllText(Path.Combine(output, "final-video.mp4"), "video");
        if (createVideo)
        {
            File.WriteAllText(Path.Combine(output, "shorts", "short-video.mp4"), "not-empty-mp4");
        }

        if (createToken)
        {
            File.WriteAllText(TokenPath, JsonSerializer.Serialize(new MetaOAuthTokenFile("1135323479659435", "AstroPulse", "page-token-secret", "17841433998640998", "astro", "user-token-secret", DateTimeOffset.UtcNow)));
        }

        return new InMemoryPipelineRepository(run);
    }

    public string OutputDirectory(PipelineRun run) => Path.Combine(Root, run.ContentType.ToString(), run.RunDate.ToString("yyyy-MM-dd"), run.Id.ToString("N"));
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
}

public sealed class TrackingMetaHandler : HttpMessageHandler
{
    public string BaseAddress { get; set; } = "https://upload.example.test";
    public bool FailUpload { get; set; }
    public bool StartMissingVideoId { get; set; }
    public bool ThrowTaskCanceledDuringVerification { get; set; }
    public Queue<string> VerificationStatuses { get; set; } = new Queue<string>(new[] { "PUBLISHED" });
    public Queue<string> InstagramContainerStatuses { get; set; } = new Queue<string>(new[] { "FINISHED" });
    public List<(string Phase, Dictionary<string, string> Headers, Dictionary<string, string> ContentHeaders, string Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        if (request.Method == HttpMethod.Head && request.RequestUri!.Scheme == Uri.UriSchemeHttps)
        {
            Requests.Add(("head-public-url", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request), body));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
            return response;
        }

        if (request.RequestUri!.AbsolutePath.EndsWith("/media", StringComparison.OrdinalIgnoreCase) && body.Contains("media_type=REELS", StringComparison.Ordinal))
        {
            Requests.Add(("ig-container", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request), body));
            return JsonResponse(new { id = "creation-123" });
        }

        if (request.RequestUri!.AbsolutePath.EndsWith("/media_publish", StringComparison.OrdinalIgnoreCase))
        {
            Requests.Add(("ig-publish", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request), body));
            return JsonResponse(new { id = "ig-media-456" });
        }

        if (request.RequestUri!.AbsolutePath.EndsWith("/creation-123", StringComparison.OrdinalIgnoreCase))
        {
            Requests.Add(("ig-poll", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request), body));
            var status = InstagramContainerStatuses.Count > 0 ? InstagramContainerStatuses.Dequeue() : "IN_PROGRESS";
            return JsonResponse(new { status_code = status, status });
        }

        if (request.RequestUri!.AbsolutePath.EndsWith("/ig-media-456", StringComparison.OrdinalIgnoreCase))
        {
            Requests.Add(("ig-details", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request), body));
            return JsonResponse(new { id = "ig-media-456", permalink = "https://instagram.example.test/reel/ig-media-456", media_type = "VIDEO", timestamp = "2026-05-07T00:00:00+0000" });
        }

        if (request.RequestUri!.AbsolutePath.EndsWith("/video_reels", StringComparison.OrdinalIgnoreCase) && body.Contains("upload_phase=start", StringComparison.Ordinal))
        {
            Requests.Add(("start", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request), body));
            return StartMissingVideoId
                ? JsonResponse(new { upload_url = BaseAddress + "/upload/video-123" })
                : JsonResponse(new { video_id = "video-123", upload_url = BaseAddress + "/upload/video-123" });
        }

        if (request.RequestUri!.Host == "upload.example.test")
        {
            Requests.Add(("upload", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request), body));
            return FailUpload
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = new { message = "missing offset" } }) }
                : JsonResponse(new { success = true });
        }

        if (request.RequestUri!.AbsolutePath.EndsWith("/video_reels", StringComparison.OrdinalIgnoreCase) && body.Contains("upload_phase=finish", StringComparison.Ordinal))
        {
            Requests.Add(("finish", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request), body));
            return JsonResponse(new { post_id = "post-456", id = "post-456" });
        }

        if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/video-123", StringComparison.OrdinalIgnoreCase))
        {
            Requests.Add(("verify-video", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request), body));
            if (ThrowTaskCanceledDuringVerification)
            {
                throw new TaskCanceledException("verification timeout");
            }

            var status = VerificationStatuses.Count > 0 ? VerificationStatuses.Dequeue() : "PROCESSING";
            return JsonResponse(new { id = "video-123", permalink_url = "https://facebook.example.test/reel/video-123", status });
        }

        if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/video_reels", StringComparison.OrdinalIgnoreCase))
        {
            Requests.Add(("verify-reels", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request), body));
            var status = VerificationStatuses.Count > 0 ? VerificationStatuses.Peek() : "PROCESSING";
            return JsonResponse(new { data = new[] { new { id = "video-123", permalink_url = "https://facebook.example.test/reel/video-123", status } } });
        }

        return new HttpResponseMessage(HttpStatusCode.BadRequest);
    }

    private static HttpResponseMessage JsonResponse(object payload) => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    private static Dictionary<string, string> ContentHeaders(HttpRequestMessage request)
        => request.Content?.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)) ?? new Dictionary<string, string>();
}

internal sealed class TrackingYouTubePublishService : IYouTubePublishService
{
    public string PlatformName => "YouTube";
    public bool Called { get; private set; }
    public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken)
    {
        Called = true;
        return Task.FromResult(new PublishResult { Success = true, Platform = "YouTube", Mode = "DryRun", AssetType = request.AssetType, IsShort = request.IsShort });
    }
}

public sealed class InMemoryPipelineRepository : IPipelineRepository
{
    private readonly PipelineRun _run;
    public InMemoryPipelineRepository(PipelineRun run) => _run = run;
    public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => Task.FromResult(run);
    public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == _run.Id ? _run : null);
    public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>(new[] { _run });
    public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>(Array.Empty<GeneratedScript>());
    public Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
    public Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineJob>>(Array.Empty<PipelineJob>());
    public Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
    public Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>(Array.Empty<PublishedVideo>());
    public Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>(Array.Empty<GeneratedScript>());
    public Task AddVideoAnalyticsAsync(VideoAnalytics analytics, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Array.Empty<VideoAnalytics>());
    public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Array.Empty<VideoAnalytics>());
    public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Array.Empty<VideoAnalytics>());
    public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Array.Empty<VideoAnalytics>());
    public Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Array.Empty<VideoAnalytics>());
    public Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>(Array.Empty<PublishedVideo>());
    public Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ShortVideo>>(Array.Empty<ShortVideo>());
    public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken) => Task.FromResult<GeneratedScript?>(null);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
