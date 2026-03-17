using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PublishingFlowTests
{
    [Fact]
    public async Task AzureBlobStorageService_UploadAsync_ReturnsEmpty_WhenConnectionStringMissing()
    {
        var service = new AzureBlobStorageService(
            Options.Create(new AzureBlobOptions { ConnectionString = "", ContainerName = "astronomy-videos" }),
            NullLogger<AzureBlobStorageService>.Instance);

        var result = await service.UploadAsync(new BlobUploadRequest
        {
            BasePath = "test/path",
            VideoPath = "missing.mp4",
            AudioPath = "missing.mp3"
        }, CancellationToken.None);

        Assert.Null(result.VideoUrl);
        Assert.Null(result.AudioUrl);
        Assert.Null(result.ThumbnailUrl);
    }

    [Fact]
    public async Task YouTubePublishingService_UploadAsync_ReturnsNull_WhenCredentialsMissing()
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "video");

        var service = new YouTubePublishingService(
            Options.Create(new YouTubeOptions { ClientId = "", ClientSecret = "" }),
            NullLogger<YouTubePublishingService>.Instance);

        var videoId = await service.UploadAsync(tempFile, "title", "desc", ["tag"], "private", CancellationToken.None);
        Assert.Null(videoId);

        File.Delete(tempFile);
    }

    [Fact]
    public async Task PipelineOrchestrator_Continues_WhenBlobOrYouTubeUploadFails()
    {
        var repository = new FakePipelineRepository();
        var orchestrator = new PipelineOrchestrator(
            new FakeContextProvider(),
            new FakeTopicRankingService(),
            new FakeVisualProvider(),
            new FakeScriptService(),
            new FakeSpeechService(),
            new FakeRenderService(),
            new ThrowingBlobService(),
            new ThrowingYouTubeService(),
            repository,
            Options.Create(new YouTubeOptions { PrivacyStatus = "private" }),
            NullLogger<PipelineOrchestrator>.Instance);

        var result = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: true), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.Single(repository.PublishedVideos);
        Assert.Equal("UploadFailed", repository.PublishedVideos[0].Status);
    }

    private sealed class FakePipelineRepository : IPipelineRepository
    {
        public List<PublishedVideo> PublishedVideos { get; } = [];

        public Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken)
        {
            PublishedVideos.Add(publishedVideo);
            return Task.CompletedTask;
        }

        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken)
        {
            return Task.FromResult(run);
        }

        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineRun?>(null);
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeContextProvider : IAstronomyContextProvider
    {
        public Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken)
            => Task.FromResult(new AstronomyContext { Date = date, LocationName = locationName, TimeZone = timeZone });
    }

    private sealed class FakeTopicRankingService : ITopicRankingService
    {
        public Task<IReadOnlyCollection<RankedTopic>> RankAsync(AstronomyContext context, ContentType contentType, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<RankedTopic>>([]);
    }

    private sealed class FakeVisualProvider : IVisualAssetProvider
    {
        public async Task<IReadOnlyCollection<string>> PrepareVisualsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken)
        {
            var visualPath = Path.Combine(outputDirectory, "scene.png");
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllTextAsync(visualPath, "image", cancellationToken);
            return [visualPath];
        }
    }

    private sealed class FakeScriptService : IScriptGenerationService
    {
        public Task<ScriptResult> GenerateAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ScriptResult { Title = "Sky", Description = "Desc", ScriptBody = "Body", Tags = ["astronomy"], EstimatedDurationSeconds = 30 });
    }

    private sealed class FakeSpeechService : ISpeechSynthesisService
    {
        public async Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken)
        {
            var audioPath = Path.Combine(outputDirectory, "narration.mp3");
            await File.WriteAllTextAsync(audioPath, "audio", cancellationToken);
            return audioPath;
        }
    }

    private sealed class FakeRenderService : IVideoRenderService
    {
        public async Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(manifest.OutputPath, "video", cancellationToken);
            return manifest.OutputPath;
        }
    }

    private sealed class ThrowingBlobService : IAzureBlobStorageService
    {
        public Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("blob fail");
    }

    private sealed class ThrowingYouTubeService : IYouTubePublishingService
    {
        public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken)
            => throw new InvalidOperationException("youtube fail");
    }
}
