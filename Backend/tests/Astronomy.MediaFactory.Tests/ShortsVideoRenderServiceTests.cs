using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ShortsVideoRenderServiceTests
{
    [Fact]
    public async Task RenderAsync_UsesSingleNarrationAndDistributesSceneDurations()
    {
        var renderService = new CapturingRenderService();
        var speech = new TrackingSpeechService();
        var sut = new ShortsVideoRenderService(
            new FakeShortScriptService(),
            speech,
            new FixedVisualProvider(),
            renderService,
            new NoopBlobService(),
            new NoopYouTubeService(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            Options.Create(new YouTubeOptions()),
            NullLogger<ShortsVideoRenderService>.Instance);

        var outputDir = Directory.CreateTempSubdirectory("shorts-single-audio").FullName;
        var result = await sut.RenderAsync(ContentType.SpaceNews, new AstronomyContext { Date = DateOnly.FromDateTime(DateTime.UtcNow) }, [], outputDir, false, CancellationToken.None);

        Assert.Equal(1, speech.Calls);
        Assert.NotNull(renderService.LastManifest);
        Assert.All(renderService.LastManifest!.Scenes, scene => Assert.True(string.IsNullOrWhiteSpace(scene.AudioPath)));
        Assert.Equal(5, renderService.LastManifest.Scenes.Count);
        Assert.All(renderService.LastManifest.Scenes, scene => Assert.Equal(6, scene.DurationSeconds));
        Assert.Equal(30, renderService.LastManifest.Scenes.Sum(scene => scene.DurationSeconds));
        Assert.Equal(result.AudioPath, renderService.LastManifest.AudioPath);
    }

    private sealed class FakeShortScriptService : IShortsScriptGenerationService
    {
        public Task<ShortScriptResult> GenerateShortAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ShortScriptResult { Hook = "Hook", ShortScript = "Script", Title = "Title", EstimatedDurationSeconds = 45 });
    }

    private sealed class TrackingSpeechService : ISpeechSynthesisService
    {
        public int Calls { get; private set; }
        public Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, "narration.mp3");
            File.WriteAllBytes(path, [1, 2, 3]);
            return Task.FromResult(path);
        }
    }

    private sealed class FixedVisualProvider : IVisualAssetProvider
    {
        public Task<IReadOnlyCollection<string>> PrepareVisualsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            var scenes = Enumerable.Range(1, 5).Select(i => Path.Combine(outputDirectory, $"scene-{i}.png")).ToArray();
            foreach (var scene in scenes) File.WriteAllBytes(scene, [1, 2, 3]);
            return Task.FromResult<IReadOnlyCollection<string>>(scenes);
        }
    }

    private sealed class CapturingRenderService : IVideoRenderService
    {
        public RenderManifest? LastManifest { get; private set; }
        public Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken)
        {
            LastManifest = manifest;
            File.WriteAllBytes(manifest.OutputPath, [1, 2, 3]);
            return Task.FromResult(manifest.OutputPath);
        }
    }

    private sealed class NoopBlobService : IAzureBlobStorageService
    {
        public Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken) => Task.FromResult(new BlobUploadResult());
    }

    private sealed class NoopYouTubeService : IYouTubePublishingService
    {
        public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string privacyStatus, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
