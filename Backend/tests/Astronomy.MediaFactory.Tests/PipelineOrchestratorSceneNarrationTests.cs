using System.Text.Json;
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
            var stageExecutor = new PipelineStageExecutor(repository, NullLogger<PipelineStageExecutor>.Instance);
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
                maintenanceOptions: Options.Create(new MaintenanceOptions { WorkingDirectory = tempRoot }),
                pipelineStageExecutor: stageExecutor);

            await orchestrator.RunAsync(new RunPipelineRequest(
                new DateOnly(2026, 4, 30),
                ContentType.DailySkyGuide,
                "Seattle",
                "America/Los_Angeles",
                false), CancellationToken.None);

            var runDir = Directory.GetDirectories(Path.Combine(tempRoot, "DailySkyGuide", "2026-04-30"), "*").Single();
            var pipelineRun = (await repository.GetRecentAsync(1, CancellationToken.None)).Single();
            var stageNames = (await repository.GetStageExecutionsAsync(pipelineRun.Id, CancellationToken.None)).Select(stage => stage.StageName).ToArray();
            Assert.Contains(PipelineStageNames.SpeechCompleted, stageNames);
            Assert.Contains("SceneSpeechSynthesis-001", stageNames);
            Assert.Contains("SceneSpeechSynthesis-002", stageNames);
            Assert.Contains("SceneSpeechSynthesis-003", stageNames);
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


    [Fact]
    public async Task RunAsync_UsesFallbackSceneNarration_WhenScriptOmitsVisualScenes()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"pipeline-scenes-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var repository = new InMemoryPipelineRepository();
            var render = new CapturingRenderService();
            var stageExecutor = new PipelineStageExecutor(repository, NullLogger<PipelineStageExecutor>.Instance);
            var orchestrator = new PipelineOrchestrator(
                new FakeContextProvider(),
                new FakeTopicRankingService(),
                new MultiVisualProvider(),
                new MissingSceneScriptService(),
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
                maintenanceOptions: Options.Create(new MaintenanceOptions { WorkingDirectory = tempRoot }),
                pipelineStageExecutor: stageExecutor);

            await orchestrator.RunAsync(new RunPipelineRequest(
                new DateOnly(2026, 4, 30),
                ContentType.DailySkyGuide,
                "Seattle",
                "America/Los_Angeles",
                false), CancellationToken.None);

            var runDir = Directory.GetDirectories(Path.Combine(tempRoot, "DailySkyGuide", "2026-04-30"), "*").Single();
            var narrationTxt = await File.ReadAllTextAsync(Path.Combine(runDir, "narration.txt"));
            Assert.Contains("[Scene 1: Sky Overview]", narrationTxt, StringComparison.Ordinal);
            Assert.Contains("[Scene 2: Moon]", narrationTxt, StringComparison.Ordinal);
            Assert.Contains("[Scene 3: Jupiter]", narrationTxt, StringComparison.Ordinal);
            Assert.Contains("look for Jupiter", narrationTxt, StringComparison.Ordinal);
            Assert.All(Enumerable.Range(1, 3), i => Assert.True(File.Exists(Path.Combine(runDir, $"scene-audio-{i:000}.mp3"))));
            Assert.All(render.Manifest.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.AudioPath)));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FullVideoWithThreeObjects_KeepsClosingNarrationLast()
    {
        var scenes = BuildSceneOrder("Moon", "Jupiter", "Saturn");
        var result = await RunNarrationOrderingScenarioAsync(scenes, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sky-overview"] = "Overview text",
            ["object-1"] = "Moon text",
            ["object-2"] = "Jupiter text",
            ["object-3"] = "Saturn text",
            ["closing"] = "Closing text"
        });

        AssertClosingLast(result.RunDirectory, result.Render.Manifest.Scenes);
        AssertVisualAndNarrationOrderMatch(result.RunDirectory, scenes);
    }

    [Fact]
    public async Task RunAsync_FullVideoWithFiveObjects_DoesNotUseClosingForAdditionalObjects()
    {
        var scenes = BuildSceneOrder("Moon", "Jupiter", "Saturn", "Venus", "Mars");
        var result = await RunNarrationOrderingScenarioAsync(scenes, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sky-overview"] = "Overview text",
            ["object-1"] = "Moon text",
            ["object-2"] = "Jupiter text",
            ["object-3"] = "Saturn text",
            ["closing"] = "Closing text"
        });

        AssertClosingLast(result.RunDirectory, result.Render.Manifest.Scenes);
        Assert.DoesNotContain("[Scene 5: Venus focus]" + Environment.NewLine + "Closing text", await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "narration.txt")), StringComparison.Ordinal);
        AssertVisualAndNarrationOrderMatch(result.RunDirectory, scenes);
    }

    [Fact]
    public async Task RunAsync_FullVideoWithSpecialEvent_KeepsClosingNarrationLast()
    {
        var scenes = BuildSceneOrder(["Moon", "Jupiter", "Saturn"], includeSpecialEvent: true);
        var result = await RunNarrationOrderingScenarioAsync(scenes, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sky-overview"] = "Overview text",
            ["special-event-highlight"] = "Special event text",
            ["object-1"] = "Moon text",
            ["object-2"] = "Jupiter text",
            ["object-3"] = "Saturn text",
            ["closing"] = "Closing text"
        });

        AssertClosingLast(result.RunDirectory, result.Render.Manifest.Scenes);
        AssertVisualAndNarrationOrderMatch(result.RunDirectory, scenes);
    }

    [Fact]
    public async Task RunAsync_FullVideoWithStaleSceneIndex_UsesFinalVisualSceneOrderForNarration()
    {
        var scenes = new List<SceneObservationContext>
        {
            BuildScene("sky-overview", "Sky Overview", "Overview", "Sky", 1),
            BuildScene("object-1", "Jupiter focus", "Object", "Jupiter", 2),
            BuildScene("object-2", "Venus focus", "Object", "Venus", 5), // stale selected-object/ranking order would move Venus after Saturn
            BuildScene("object-3", "Neptune focus", "Object", "Neptune", 3),
            BuildScene("object-4", "Saturn focus", "Object", "Saturn", 4),
            BuildScene("object-5", "Mars focus", "Object", "Mars", 6),
            BuildScene("closing", "Closing overview", "Closing", "Sky", 7)
        };

        var result = await RunNarrationOrderingScenarioAsync(scenes, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sky-overview"] = "Overview text",
            ["object-1"] = "Jupiter text",
            ["object-2"] = "Venus text",
            ["object-3"] = "Neptune text",
            ["object-4"] = "Saturn text",
            ["object-5"] = "Mars text",
            ["closing"] = "Closing text"
        });

        var narrationText = await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "narration.txt"));
        Assert.True(narrationText.IndexOf("Jupiter text", StringComparison.Ordinal) < narrationText.IndexOf("Venus text", StringComparison.Ordinal));
        Assert.True(narrationText.IndexOf("Venus text", StringComparison.Ordinal) < narrationText.IndexOf("Neptune text", StringComparison.Ordinal));
        Assert.True(narrationText.IndexOf("Neptune text", StringComparison.Ordinal) < narrationText.IndexOf("Saturn text", StringComparison.Ordinal));
        Assert.True(narrationText.IndexOf("Saturn text", StringComparison.Ordinal) < narrationText.IndexOf("Mars text", StringComparison.Ordinal));

        AssertClosingLast(result.RunDirectory, result.Render.Manifest.Scenes);
        AssertVisualAndNarrationOrderMatch(result.RunDirectory, scenes);
        Assert.Equal(new[] { "Sky", "Jupiter", "Venus", "Neptune", "Saturn", "Mars", "Sky" }, result.Render.Manifest.Scenes.Select(scene => scene.ObjectName));
    }

    [Fact]
    public async Task RunAsync_ShortVideoSequenceInput_RemainsUnchanged()
    {
        var scenes = BuildSceneOrder("Moon", "Jupiter", "Saturn", "Venus", "Mars");
        var shorts = new CapturingShortsService();
        var result = await RunNarrationOrderingScenarioAsync(scenes, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sky-overview"] = "Overview text",
            ["object-1"] = "Moon text",
            ["object-2"] = "Jupiter text",
            ["object-3"] = "Saturn text",
            ["closing"] = "Closing text"
        }, shorts);

        Assert.Equal(scenes.Select(scene => scene.SceneId), shorts.SceneIds);
        AssertClosingLast(result.RunDirectory, result.Render.Manifest.Scenes);
    }


    private static async Task<(string RunDirectory, CapturingRenderService Render)> RunNarrationOrderingScenarioAsync(
        IReadOnlyList<SceneObservationContext> scenes,
        IReadOnlyDictionary<string, string> scriptSections,
        IShortsVideoRenderService? shortsService = null)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"pipeline-scenes-order-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var repository = new InMemoryPipelineRepository();
        var render = new CapturingRenderService();
        var stageExecutor = new PipelineStageExecutor(repository, NullLogger<PipelineStageExecutor>.Instance);
        var orchestrator = new PipelineOrchestrator(
            new StaticSceneContextProvider(scenes),
            new FakeTopicRankingService(),
            new ContextVisualProvider(),
            new DictionarySceneScriptService(scriptSections),
            new SceneSpeechService(),
            render,
            new NoOpBlobStorageService(),
            new NoOpYouTubeService(),
            shortsService ?? new NoOpShortsService(),
            new PassThroughMetadataOptimizationService(),
            new StaticThumbnailGenerationService(),
            new PassThroughSeoMetadataGeneratorService(),
            repository,
            Options.Create(new YouTubeOptions()),
            Options.Create(new RenderingOptions { WorkingDirectory = tempRoot }),
            Options.Create(new PublishingValidationOptions()),
            NullLogger<PipelineOrchestrator>.Instance,
            operationsOptions: Options.Create(new OperationsOptions()),
            maintenanceOptions: Options.Create(new MaintenanceOptions { WorkingDirectory = tempRoot }),
            pipelineStageExecutor: stageExecutor);

        await orchestrator.RunAsync(new RunPipelineRequest(
            new DateOnly(2026, 4, 30),
            ContentType.DailySkyGuide,
            "Seattle",
            "America/Los_Angeles",
            false), CancellationToken.None);

        var runDirectory = Directory.GetDirectories(Path.Combine(tempRoot, "DailySkyGuide", "2026-04-30"), "*").Single();
        return (runDirectory, render);
    }

    private static List<SceneObservationContext> BuildSceneOrder(params string[] objectNames)
        => BuildSceneOrder(objectNames, includeSpecialEvent: false);

    private static List<SceneObservationContext> BuildSceneOrder(IReadOnlyList<string> objectNames, bool includeSpecialEvent)
    {
        var scenes = new List<SceneObservationContext>
        {
            BuildScene("sky-overview", "Sky Overview", "Overview", "Sky", 1)
        };

        if (includeSpecialEvent)
        {
            scenes.Add(BuildScene("special-event-highlight", "Meteor shower highlight", "SpecialEventHighlight", "Meteor shower", scenes.Count + 1));
        }

        for (var i = 0; i < objectNames.Count; i++)
        {
            scenes.Add(BuildScene($"object-{i + 1}", $"{objectNames[i]} focus", "Object", objectNames[i], scenes.Count + 1));
        }

        scenes.Add(BuildScene("closing", "Closing overview", "Closing", "Sky", scenes.Count + 1));
        return scenes;
    }

    private static SceneObservationContext BuildScene(string sceneId, string title, string sceneType, string objectName, int sceneIndex)
        => new()
        {
            SceneId = sceneId,
            SceneTitle = title,
            SceneType = sceneType,
            SceneIndex = sceneIndex,
            ObjectName = objectName,
            ObjectType = sceneType,
            LocalObservationTime = new DateTime(2026, 4, 30, 20, 0, 0).AddMinutes(sceneIndex * 10),
            UtcObservationTime = new DateTimeOffset(new DateTime(2026, 5, 1, 3, 0, 0).AddMinutes(sceneIndex * 10), TimeSpan.Zero),
            Timezone = "America/Los_Angeles",
            DirectionLabel = "south",
            IsVisible = true,
            VisibilityReason = $"{objectName} visibility",
            RecommendedTool = "Naked eye",
            NarrationFocus = $"Observe {objectName}.",
            LocationName = "Seattle"
        };

    private static void AssertClosingLast(string runDirectory, IReadOnlyList<RenderScene> renderScenes)
    {
        var narrationText = File.ReadAllText(Path.Combine(runDirectory, "narration.txt"));
        var closingIndex = narrationText.IndexOf("Closing text", StringComparison.Ordinal);
        Assert.True(closingIndex >= 0);
        Assert.DoesNotContain("Closing text", narrationText[(closingIndex + "Closing text".Length)..], StringComparison.Ordinal);
        Assert.EndsWith("Closing text", narrationText.Trim(), StringComparison.Ordinal);
        Assert.Equal("closing", renderScenes.Last().SceneId);
        Assert.Equal("Closing text", renderScenes.Last().NarrationText);
    }

    private static void AssertVisualAndNarrationOrderMatch(string runDirectory, IReadOnlyList<SceneObservationContext> scenes)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "narration-report.json")));
        var entries = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(scenes.Count, entries.Length);
        Assert.Equal(scenes.Select(scene => scene.SceneId), entries.Select(entry => entry.GetProperty("sceneId").GetString()));
        Assert.Equal(Enumerable.Range(1, scenes.Count), entries.Select(entry => entry.GetProperty("narrationOrder").GetInt32()));
        Assert.Equal(Enumerable.Range(1, scenes.Count), entries.Select(entry => entry.GetProperty("visualOrder").GetInt32()));
        Assert.Equal(Enumerable.Range(1, scenes.Count), entries.Select(entry => entry.GetProperty("renderOrder").GetInt32()));
        Assert.All(entries, entry => Assert.Equal("FinalVisualSceneOrder", entry.GetProperty("sourceOrderUsed").GetString()));
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

    private sealed class DictionarySceneScriptService(IReadOnlyDictionary<string, string> scriptSections) : IScriptGenerationService
    {
        public Task<ScriptResult> GenerateAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ScriptResult
            {
                Title = "Sky",
                Description = "Desc",
                ScriptBody = "Body",
                Tags = ["astronomy"],
                EstimatedDurationSeconds = 90,
                SceneScriptSections = new SceneScriptSections
                {
                    SectionsBySceneId = new Dictionary<string, string>(scriptSections, StringComparer.OrdinalIgnoreCase)
                }
            });
    }

    private sealed class ContextVisualProvider : IVisualAssetProvider
    {
        public async Task<IReadOnlyCollection<string>> PrepareVisualsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            var paths = new List<string>();
            for (var i = 0; i < context.SceneObservationContexts.Count; i++)
            {
                var p = Path.Combine(outputDirectory, $"scene-{i + 1}.png");
                await File.WriteAllTextAsync(p, "img", cancellationToken);
                paths.Add(p);
            }

            return paths;
        }
    }

    private sealed class StaticSceneContextProvider(IReadOnlyList<SceneObservationContext> scenes) : IAstronomyContextProvider
    {
        public Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken)
            => Task.FromResult(new AstronomyContext
            {
                Date = date,
                LocationName = locationName,
                TimeZone = timeZone,
                SceneObservationContexts = scenes.ToList()
            });
    }

    private sealed class CapturingShortsService : IShortsVideoRenderService
    {
        public IReadOnlyList<string> SceneIds { get; private set; } = [];

        public Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken)
        {
            SceneIds = context.SceneObservationContexts.Select(scene => scene.SceneId).ToArray();
            return Task.FromResult(new ShortVideoRenderResult
            {
                Script = new ShortScriptResult(),
                AudioPath = string.Empty,
                VideoPath = string.Empty
            });
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


    private sealed class MissingSceneScriptService : IScriptGenerationService
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
                        ["overview"] = "Overview text"
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
        private readonly List<PipelineRun> _runs = [];
        private readonly List<PipelineStageExecution> _stageExecutions = [];

        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken)
        {
            _runs.Add(run);
            return Task.FromResult(run);
        }

        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_runs.FirstOrDefault(run => run.Id == id));
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>(_runs.Take(take).ToArray());
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
        public Task<IReadOnlyCollection<PipelineStageExecution>> GetStageExecutionsAsync(Guid pipelineRunId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<PipelineStageExecution>>(_stageExecutions.Where(stage => stage.PipelineRunId == pipelineRunId).ToArray());
        public Task<PipelineStageExecution?> GetLatestStageExecutionAsync(Guid pipelineRunId, string stageName, CancellationToken cancellationToken)
            => Task.FromResult(_stageExecutions.LastOrDefault(stage => stage.PipelineRunId == pipelineRunId && stage.StageName == stageName));
        public Task AddStageExecutionAsync(PipelineStageExecution stageExecution, CancellationToken cancellationToken)
        {
            _stageExecutions.Add(stageExecution);
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
