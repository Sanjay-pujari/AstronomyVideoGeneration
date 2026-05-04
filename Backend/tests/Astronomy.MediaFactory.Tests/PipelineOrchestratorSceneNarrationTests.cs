using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PipelineOrchestratorSceneNarrationTests
{
    [Fact]
    public async Task RunAsync_WritesUniqueSceneNarrationArtifacts_AndCombinesOutputs()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"pipeline-scenes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var repository = new InMemoryPipelineRepository();
            var render = new CapturingRenderService();
            var orchestrator = new PipelineOrchestrator(
                new FakeContextProvider(),
                new FakeTopicRankingService(),
                new MultiVisualProvider(),
                new SceneScriptService(),
                new SceneSpeechService(),
                render,
                new NoOpBlobStorageService(),
                new NoOpYouTubeService(),
                new NoOpShortsService(),
                new PassThroughMetadataOptimizationService(),
                new StaticThumbnailGenerationService(),
                new PassThroughSeoMetadataGeneratorService(),
                repository,
                Options.Create(new YouTubeOptions()),
                Options.Create(new RenderingOptions { WorkingDirectory = tempRoot }),
                Options.Create(new PublishingValidationOptions()),
                NullLogger<PipelineOrchestrator>.Instance,
                operationsOptions: Options.Create(new OperationsOptions()),
                maintenanceOptions: Options.Create(new MaintenanceOptions { WorkingDirectory = tempRoot }));

            await orchestrator.RunAsync(new RunPipelineRequest(
                new DateOnly(2026, 4, 30),
                ContentType.DailySkyGuide,
                "Seattle",
                "America/Los_Angeles",
                false), CancellationToken.None);

            var runDir = Directory.GetDirectories(Path.Combine(tempRoot, "DailySkyGuide", "2026-04-30"), "*").Single();
            var expectedTxt = Enumerable.Range(1, 3).Select(i => Path.Combine(runDir, $"scene-narration-{i:000}.txt")).ToArray();
            var expectedMp3 = Enumerable.Range(1, 3).Select(i => Path.Combine(runDir, $"scene-audio-{i:000}.mp3")).ToArray();

            Assert.Equal(expectedTxt.Length, expectedTxt.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(expectedMp3.Length, expectedMp3.Distinct(StringComparer.Ordinal).Count());
            Assert.All(expectedTxt, p => Assert.True(File.Exists(p)));
            Assert.All(expectedMp3, p => Assert.True(File.Exists(p)));

            var narrationTxt = await File.ReadAllTextAsync(Path.Combine(runDir, "narration.txt"));
            Assert.Contains("[Scene 1: Sky Overview]", narrationTxt, StringComparison.Ordinal);
            Assert.Contains("[Scene 2: Moon]", narrationTxt, StringComparison.Ordinal);
            Assert.Contains("[Scene 3: Jupiter]", narrationTxt, StringComparison.Ordinal);
            Assert.True(narrationTxt.IndexOf("[Scene 1: Sky Overview]", StringComparison.Ordinal) < narrationTxt.IndexOf("[Scene 2: Moon]", StringComparison.Ordinal));
            Assert.True(narrationTxt.IndexOf("[Scene 2: Moon]", StringComparison.Ordinal) < narrationTxt.IndexOf("[Scene 3: Jupiter]", StringComparison.Ordinal));

            var narrationMp3 = Path.Combine(runDir, "narration.mp3");
            Assert.True(File.Exists(narrationMp3));
            Assert.True(new FileInfo(narrationMp3).Length > 0);

            var concatList = await File.ReadAllLinesAsync(Path.Combine(runDir, "audio-concat-list.txt"));
            Assert.Equal(3, concatList.Length);
            Assert.Contains("scene-audio-001.mp3", concatList[0], StringComparison.Ordinal);
            Assert.Contains("scene-audio-003.mp3", concatList[2], StringComparison.Ordinal);

            var observationContext = await File.ReadAllTextAsync(Path.Combine(runDir, "scene-observation-context.json"));
            Assert.Contains("\"sceneId\": \"moon\"", observationContext, StringComparison.Ordinal);
            Assert.Contains("Waxing Gibbous Moon", observationContext, StringComparison.Ordinal);
            Assert.Contains("\"sceneId\": \"jupiter\"", observationContext, StringComparison.Ordinal);
            Assert.Contains("\"objectName\": \"Jupiter\"", observationContext, StringComparison.Ordinal);

            var selectedTimes = await File.ReadAllTextAsync(Path.Combine(runDir, "selected-observation-times.json"));
            Assert.Contains("Around 8:45 PM", selectedTimes, StringComparison.Ordinal);
            Assert.Contains("Around 9:00 PM", selectedTimes, StringComparison.Ordinal);

            Assert.DoesNotContain(render.Manifest.Scenes.Where(s => s.AudioPath is not null).Select(s => s.AudioPath), p => p!.EndsWith("narration.mp3", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class SceneSpeechService : ISpeechSynthesisService
    {
        public async Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "narration.txt"), script, cancellationToken);
            var audioPath = Path.Combine(outputDirectory, "narration.mp3");
            await File.WriteAllBytesAsync(audioPath, [1,2,3,4], cancellationToken);
            return audioPath;
        }
    }

    private sealed class SceneScriptService : IScriptGenerationService
    {
        public Task<ScriptResult> GenerateAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ScriptResult
            {
                Title = "Sky",
                Description = "Desc",
                ScriptBody = "Body",
                Tags = ["astronomy"],
                EstimatedDurationSeconds = 45,
                SceneScriptSections = new SceneScriptSections
                {
                    SectionsBySceneId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["overview"] = "Overview text",
                        ["moon"] = "Moon text",
                        ["jupiter"] = "Jupiter text",
                        ["deepsky"] = "DeepSky text",
                        ["closing"] = "Closing text"
                    }
                }
            });
    }

    private sealed class MultiVisualProvider : IVisualAssetProvider
    {
        public async Task<IReadOnlyCollection<string>> PrepareVisualsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            var paths = new List<string>();
            for (var i = 1; i <= 3; i++)
            {
                var p = Path.Combine(outputDirectory, $"scene-{i}.png");
                await File.WriteAllTextAsync(p, "img", cancellationToken);
                paths.Add(p);
            }
            return paths;
        }
    }

    private sealed class CapturingRenderService : IVideoRenderService
    {
        public RenderManifest Manifest { get; private set; } = new();
        public async Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken)
        {
            Manifest = manifest;
            await File.WriteAllTextAsync(manifest.OutputPath, "video", cancellationToken);
            return manifest.OutputPath;
        }
    }

    private sealed class FakeContextProvider : IAstronomyContextProvider
    {
        public Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken)
            => Task.FromResult(new AstronomyContext
            {
                Date = date,
                LocationName = locationName,
                TimeZone = timeZone,
                Events =
                [
                    new AstronomyEventModel { Category = "Moon", ObjectName = "Waxing Gibbous Moon", VisibilityWindow = "Around 8:45 PM", Direction = "West", ObservationTool = "Naked eye", Details = "Craters are visible near the terminator.", Score = 0.9 },
                    new AstronomyEventModel { Category = "Planet", ObjectName = "Jupiter", VisibilityWindow = "Around 9:00 PM", Direction = "South-west", ObservationTool = "Binoculars", Details = "Look for the four Galilean moons.", Score = 0.95 }
                ]
            });
    }

    private sealed class FakeTopicRankingService : ITopicRankingService
    {
        public Task<IReadOnlyCollection<RankedTopic>> RankAsync(AstronomyContext context, ContentType contentType, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<RankedTopic>>([]);
    }

    private sealed class NoOpBlobStorageService : IAzureBlobStorageService
    {
        public Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new BlobUploadResult());
    }

    private sealed class NoOpYouTubeService : IYouTubePublishingService
    {
        public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
    }

    private sealed class NoOpShortsService : IShortsVideoRenderService
    {
        public Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken)
            => Task.FromResult(new ShortVideoRenderResult
            {
                Script = new ShortScriptResult(),
                AudioPath = string.Empty,
                VideoPath = string.Empty
            });
    }

    private sealed class PassThroughMetadataOptimizationService : IMetadataOptimizationService
    {
        public Task<OptimizedVideoMetadata> OptimizeForVideoAsync(MetadataOptimizationInput input, CancellationToken cancellationToken)
            => Task.FromResult(new OptimizedVideoMetadata
            {
                PrimaryTitle = input.SourceTitle,
                OptimizedDescription = input.SourceDescription,
                Tags = input.SourceTags.ToArray(),
                Hashtags = []
            });

        public Task<OptimizedVideoMetadata> OptimizeForShortAsync(MetadataOptimizationInput input, CancellationToken cancellationToken)
            => Task.FromResult(new OptimizedVideoMetadata
            {
                PrimaryTitle = input.SourceTitle,
                OptimizedDescription = input.SourceDescription,
                Tags = input.SourceTags.ToArray(),
                Hashtags = []
            });
    }

    private sealed class StaticThumbnailGenerationService : IThumbnailGenerationService
    {
        public Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ThumbnailPlan
            {
                PrimaryThumbnailText = "SKY",
                AlternateThumbnailTexts = ["SKY"],
                SelectedVisualPath = request.AvailableVisuals.FirstOrDefault(),
                ThumbnailPath = request.AvailableVisuals.FirstOrDefault(),
                LayoutType = ThumbnailLayoutType.CenteredTitleOverlay
            });
    }

    private sealed class InMemoryPipelineRepository : IPipelineRepository
    {
        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => Task.FromResult(run);
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineRun?>(null);
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([]);
        public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
        public Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineJob>>([]);
        public Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
        public Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task AddVideoAnalyticsAsync(VideoAnalytics analytics, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ShortVideo>>([]);
        public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken) => Task.FromResult<GeneratedScript?>(null);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
