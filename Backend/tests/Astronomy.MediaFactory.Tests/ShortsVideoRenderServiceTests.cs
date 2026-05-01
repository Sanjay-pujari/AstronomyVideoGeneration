using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ShortsVideoRenderServiceTests
{
    [Fact]
    public async Task RenderAsync_UsesSceneBasedNarrationSegments_WhenSegmentSynthesisSucceeds()
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
            Options.Create(new RenderingOptions()),
            NullLogger<ShortsVideoRenderService>.Instance);

        var outputDir = Directory.CreateTempSubdirectory("shorts-single-audio").FullName;
        var result = await sut.RenderAsync(ContentType.SpaceNews, new AstronomyContext { Date = DateOnly.FromDateTime(DateTime.UtcNow) }, [], outputDir, false, CancellationToken.None);

        Assert.Equal(6, speech.Calls);
        Assert.NotNull(renderService.LastManifest);
        Assert.All(renderService.LastManifest!.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.AudioPath)));
        Assert.Equal(5, renderService.LastManifest.Scenes.Count);
        Assert.All(renderService.LastManifest.Scenes, scene => Assert.Equal(30, scene.DurationSeconds));
        Assert.Equal(result.AudioPath, renderService.LastManifest.AudioPath);
        Assert.Equal(Path.Combine(outputDir, "narration.mp3"), result.AudioPath);
        Assert.All(Enumerable.Range(1, 5), i => Assert.True(File.Exists(Path.Combine(outputDir, $"scene-audio-{i:000}.mp3"))));
        var concatListPath = Path.Combine(outputDir, "audio-concat-list.txt");
        Assert.True(File.Exists(concatListPath));
        var concatLines = await File.ReadAllLinesAsync(concatListPath);
        Assert.Equal(5, concatLines.Length);
        Assert.Contains("scene-audio-001.mp3", concatLines[0], StringComparison.Ordinal);
        Assert.Contains("scene-audio-005.mp3", concatLines[4], StringComparison.Ordinal);
        Assert.Contains("moon facts", speech.Scripts[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("jupiter storms", speech.Scripts[3], StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeShortScriptService : IShortsScriptGenerationService
    {
        public Task<ShortScriptResult> GenerateShortAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ShortScriptResult
            {
                Hook = "Hook",
                ShortScript = "Script",
                Title = "Title",
                EstimatedDurationSeconds = 45,
                SceneNarrationSegments =
                [
                    new SceneNarrationSegment{ SceneId = "sky-overview", SceneTitle = "Sky overview", VisualTarget = "wide sky", NarrationText = "overview of tonight's sky."},
                    new SceneNarrationSegment{ SceneId = "moon", SceneTitle = "Moon focus", VisualTarget = "moon", NarrationText = "moon facts and crater highlights."},
                    new SceneNarrationSegment{ SceneId = "jupiter", SceneTitle = "Jupiter focus", VisualTarget = "jupiter", NarrationText = "jupiter storms and bright disk."},
                    new SceneNarrationSegment{ SceneId = "planet-secondary", SceneTitle = "Secondary planet", VisualTarget = "mars", NarrationText = "mars color contrast tonight."},
                    new SceneNarrationSegment{ SceneId = "constellation", SceneTitle = "Constellation", VisualTarget = "orion", NarrationText = "constellation orientation tips."}
                ]
            });
    }

    private sealed class TrackingSpeechService : ISpeechSynthesisService
    {
        public int Calls { get; private set; }
        public List<string> Scripts { get; } = [];
        public Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            Scripts.Add(script);
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
