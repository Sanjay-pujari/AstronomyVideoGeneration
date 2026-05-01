using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
                repository,
                Options.Create(new YouTubeOptions()),
                NullLogger<PipelineOrchestrator>.Instance,
                operationsOptions: Options.Create(new OperationsOptions()),
                maintenanceOptions: Options.Create(new MaintenanceOptions { WorkingDirectory = tempRoot }));

            await orchestrator.RunAsync(new RunPipelineRequest
            {
                Date = new DateOnly(2026, 4, 30),
                ContentType = ContentType.Daily,
                LocationName = "Seattle",
                TimeZone = "America/Los_Angeles",
                PublishToYouTube = false
            }, CancellationToken.None);

            var runDir = Directory.GetDirectories(Path.Combine(tempRoot, "Daily", "2026-04-30"), "*").Single();
            var expectedTxt = Enumerable.Range(1, 3).Select(i => Path.Combine(runDir, $"scene-narration-{i:000}.txt")).ToArray();
            var expectedMp3 = Enumerable.Range(1, 3).Select(i => Path.Combine(runDir, $"scene-audio-{i:000}.mp3")).ToArray();

            Assert.Equal(expectedTxt.Length, expectedTxt.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(expectedMp3.Length, expectedMp3.Distinct(StringComparer.Ordinal).Count());
            Assert.All(expectedTxt, File.Exists);
            Assert.All(expectedMp3, File.Exists);

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
                    Overview = "Overview text",
                    Moon = "Moon text",
                    Jupiter = "Jupiter text",
                    DeepSky = "DeepSky text",
                    Closing = "Closing text"
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
}
