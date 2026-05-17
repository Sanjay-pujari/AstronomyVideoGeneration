using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class FfmpegRenderingTests
{
    [Fact]
    public void RenderManifestBuilder_BuildsConcatAndCaptionArtifacts()
    {
        var builder = new RenderManifestBuilder();
        var manifest = new RenderManifest
        {
            Title = "Sky Tonight",
            AudioPath = "/tmp/narration.mp3",
            OutputPath = "/tmp/final-video.mp4",
            IntroVisualPath = "/tmp/intro.png",
            OutroVisualPath = "/tmp/outro.png",
            Scenes =
            [
                new RenderScene { Caption = "Moon", VisualPath = "/tmp/scene-1.png", DurationSeconds = 10 },
                new RenderScene { Caption = "Mars", VisualPath = "/tmp/scene-2.png", DurationSeconds = 12 }
            ]
        };

        var plan = builder.Build(manifest);

        Assert.Equal(4, plan.Scenes.Count);
        Assert.Contains("file '/tmp/intro.png'", plan.ConcatInputContent);
        Assert.Contains("duration 10", plan.ConcatInputContent);
        Assert.Contains("Moon", plan.CaptionMetadataJson);
        Assert.Contains("00:00:00,000 --> 00:00:03,000", plan.SubtitleScaffold);
    }

    [Fact]
    public void FfmpegArgumentBuilder_BuildsExpectedArguments()
    {
        var sut = new FfmpegArgumentBuilder();
        var options = new RenderingOptions { VideoWidth = 1280, VideoHeight = 720, FrameRate = 30 };

        var args = sut.Build(options, new RenderManifest { AudioPath = "/tmp/narration.mp3", OutputPath = "/tmp/final-video.mp4" }, "/tmp/ffmpeg-input.txt", "/tmp/narration.mp3", "/tmp/final-video.mp4");

        Assert.Contains("-f concat", args);
        Assert.Contains("-i \"/tmp/ffmpeg-input.txt\"", args);
        Assert.Contains("-i \"/tmp/narration.mp3\"", args);
        Assert.Contains("scale=2560:1440", args);
        Assert.Contains("\"/tmp/final-video.mp4\"", args);
        Assert.DoesNotContain("-shortest", args, StringComparison.Ordinal);
    }



    [Fact]
    public void FfmpegArgumentBuilder_UsesVerticalCrop_ForShortsManifest()
    {
        var sut = new FfmpegArgumentBuilder();
        var options = new RenderingOptions { VideoWidth = 1280, VideoHeight = 720, FrameRate = 30 };

        var args = sut.Build(options, new RenderManifest
        {
            OutputWidth = 1080,
            OutputHeight = 1920,
            EnableVerticalCrop = true,
            AudioPath = "/tmp/narration.mp3",
            OutputPath = "/tmp/short-video.mp4"
        }, "/tmp/ffmpeg-input.txt", "/tmp/narration.mp3", "/tmp/short-video.mp4");

        Assert.Contains("scale=1080:1920:force_original_aspect_ratio=increase", args);
        Assert.Contains("crop=1080:1920", args);
        Assert.Contains("pad=1080:1920", args);
        Assert.Contains("setsar=1", args);
        Assert.Contains("-preset slow", args);
        Assert.Contains("-b:v 12M", args);
        Assert.Contains("-b:a 256k", args);
        Assert.Contains("-movflags +faststart", args);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_Throws_WhenFfmpegFails()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-tests");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var sut = CreateService(fileSystem, new ThrowingProcessRunner());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 6 }]
        }, CancellationToken.None));
        Assert.Contains(Path.Combine(tempDir.FullName, "render-manifest.json"), fileSystem.TextWrites.Keys);
        Assert.Contains(Path.Combine(tempDir.FullName, "ffmpeg-input.txt"), fileSystem.TextWrites.Keys);
        Assert.Contains(Path.Combine(tempDir.FullName, "ffmpeg-command.txt"), fileSystem.TextWrites.Keys);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_Throws_WhenAudioOrVisualMissing()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-validation");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");

        var fileSystem = new InMemoryFileSystem();
        var sut = CreateService(fileSystem, new NoopProcessRunner());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RenderAsync(new RenderManifest
        {
            Title = "Missing assets",
            AudioPath = Path.Combine(tempDir.FullName, "missing.mp3"),
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = Path.Combine(tempDir.FullName, "missing.png"), DurationSeconds = 6 }]
        }, CancellationToken.None));

        var logPath = Path.Combine(tempDir.FullName, "ffmpeg.log");
        Assert.Contains("missing", fileSystem.TextWrites[logPath], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Narration audio missing", fileSystem.TextWrites[logPath], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scene visual missing", fileSystem.TextWrites[logPath], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_WritesProcessDiagnosticsToLog()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-diagnostics");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var sut = CreateService(fileSystem, new FailingProcessRunner());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 6 }]
        }, CancellationToken.None));

        var logPath = Path.Combine(tempDir.FullName, "ffmpeg.log");
        var diagnostics = fileSystem.TextWrites[logPath];
        Assert.Contains("Command:", diagnostics, StringComparison.Ordinal);
        Assert.Contains("ExitCode: 1", diagnostics, StringComparison.Ordinal);
        Assert.Contains("--- STDERR ---", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_UsesSegmentFlow_WhenSceneAudioIsPresent()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-segmented");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        var sceneAudioPath = Path.Combine(tempDir.FullName, "scene-1.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
        await File.WriteAllBytesAsync(sceneAudioPath, [7, 8, 9]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 6d,
                [sceneAudioPath] = 6d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 6d
            }
        };
        var sut = CreateService(fileSystem, processRunner);

        await sut.RenderAsync(new RenderManifest
        {
            Title = "Segmented narration",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes =
            [
                new RenderScene
                {
                    Caption = "Scene",
                    VisualPath = scenePath,
                    AudioPath = sceneAudioPath,
                    DurationSeconds = 6
                }
            ]
        }, CancellationToken.None);

        var segmentCommand = processRunner.Commands.Single(command => command.Contains("-loop 1", StringComparison.Ordinal) && command.Contains(sceneAudioPath, StringComparison.Ordinal));
        Assert.Contains("-t 6", segmentCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("-shortest", string.Join(" ", processRunner.Commands), StringComparison.Ordinal);
    }


    [Fact]
    public async Task FfmpegVideoRenderService_MissingSegmentVisualPath_GivesSceneValidationError()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-segment-missing-visual");
        var outputPath = Path.Combine(tempDir.FullName, "short-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var sceneAudioPath = Path.Combine(tempDir.FullName, "scene-1.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(sceneAudioPath, [7, 8, 9]);

        var fileSystem = new InMemoryFileSystem();
        var sut = CreateService(fileSystem, new SegmentAwareProcessRunner());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RenderAsync(new RenderManifest
        {
            Title = "Short missing visual",
            AudioPath = audioPath,
            OutputPath = outputPath,
            OutputWidth = 1080,
            OutputHeight = 1920,
            Scenes = [new RenderScene { Caption = "Scene", SceneId = "scene-001", ObjectName = "Moon", SceneType = "main", VisualPath = Path.Combine(tempDir.FullName, "missing.png"), AudioPath = sceneAudioPath, DurationSeconds = 6 }]
        }, CancellationToken.None));

        Assert.Contains("Scene #1 validation failed", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"visual file not found: {Path.Combine(tempDir.FullName, "missing.png")}", ex.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(tempDir.FullName, "shorts-render-manifest-final.json"), fileSystem.TextWrites.Keys);
        var diagnosticJson = fileSystem.TextWrites[Path.Combine(tempDir.FullName, "render-segment-failure-scene-0.json")];
        using var diagnostic = JsonDocument.Parse(diagnosticJson);
        Assert.Equal(0, diagnostic.RootElement.GetProperty("sceneIndexZeroBased").GetInt32());
        Assert.Equal(1, diagnostic.RootElement.GetProperty("sceneIndexOneBased").GetInt32());
        Assert.Equal(1, diagnostic.RootElement.GetProperty("sceneIndex").GetInt32());
        Assert.Equal("scene-001", diagnostic.RootElement.GetProperty("sceneId").GetString());
        Assert.Contains($"visual file not found: {Path.Combine(tempDir.FullName, "missing.png")}", diagnostic.RootElement.GetProperty("validationError").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_MissingSegmentAudioPath_GivesSceneValidationError()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-segment-missing-audio");
        var outputPath = Path.Combine(tempDir.FullName, "short-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var firstVisualPath = Path.Combine(tempDir.FullName, "scene-1.png");
        var secondVisualPath = Path.Combine(tempDir.FullName, "scene-2.png");
        var firstSceneAudioPath = Path.Combine(tempDir.FullName, "scene-1.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(firstVisualPath, [4, 5, 6]);
        await File.WriteAllBytesAsync(secondVisualPath, [4, 5, 6]);
        await File.WriteAllBytesAsync(firstSceneAudioPath, [7, 8, 9]);

        var fileSystem = new InMemoryFileSystem();
        var sut = CreateService(fileSystem, new SegmentAwareProcessRunner { ProbeDurationsByPath = { [firstSceneAudioPath] = 6d } });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RenderAsync(new RenderManifest
        {
            Title = "Short missing audio",
            AudioPath = audioPath,
            OutputPath = outputPath,
            OutputWidth = 1080,
            OutputHeight = 1920,
            Scenes =
            [
                new RenderScene { Caption = "Scene 1", SceneId = "scene-001", VisualPath = firstVisualPath, AudioPath = firstSceneAudioPath, DurationSeconds = 6 },
                new RenderScene { Caption = "Scene 2", SceneId = "scene-002", VisualPath = secondVisualPath, AudioPath = Path.Combine(tempDir.FullName, "missing.mp3"), DurationSeconds = 6 }
            ]
        }, CancellationToken.None));

        Assert.Contains("Scene #2 validation failed", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"audio file not found: {Path.Combine(tempDir.FullName, "missing.mp3")}", ex.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(tempDir.FullName, "render-segment-failure-scene-1.json"), fileSystem.TextWrites.Keys);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_SegmentFailureCapturesFfmpegStderrAndCommand()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-segment-stderr");
        var outputPath = Path.Combine(tempDir.FullName, "short-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        var sceneAudioPath = Path.Combine(tempDir.FullName, "scene-1.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
        await File.WriteAllBytesAsync(sceneAudioPath, [7, 8, 9]);

        var fileSystem = new InMemoryFileSystem();
        var logger = new TestLogger<FfmpegVideoRenderService>();
        var processRunner = new FailingSegmentProcessRunner("segment encoder exploded") { ProbeDurationsByPath = { [sceneAudioPath] = 6d } };
        var sut = CreateService(fileSystem, processRunner, logger: logger);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RenderAsync(new RenderManifest
        {
            Title = "Short failing segment",
            AudioPath = audioPath,
            OutputPath = outputPath,
            OutputWidth = 1080,
            OutputHeight = 1920,
            Scenes = [new RenderScene { Caption = "Scene", SceneId = "scene-001", ObjectName = "Mars", SceneType = "main", VisualPath = scenePath, AudioPath = sceneAudioPath, DurationSeconds = 6 }]
        }, CancellationToken.None));

        Assert.Contains("FFmpeg segmented clip generation failed for scene #1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("segment encoder exploded", ex.Message, StringComparison.Ordinal);
        var diagnosticJson = fileSystem.TextWrites[Path.Combine(tempDir.FullName, "render-segment-failure-scene-0.json")];
        using var diagnostic = JsonDocument.Parse(diagnosticJson);
        Assert.Equal(1, diagnostic.RootElement.GetProperty("sceneIndex").GetInt32());
        Assert.Equal("scene-001", diagnostic.RootElement.GetProperty("sceneId").GetString());
        Assert.Equal("Mars", diagnostic.RootElement.GetProperty("objectName").GetString());
        Assert.Equal(scenePath, diagnostic.RootElement.GetProperty("visualPath").GetString());
        Assert.Equal(sceneAudioPath, diagnostic.RootElement.GetProperty("audioPath").GetString());
        Assert.Equal(6d, diagnostic.RootElement.GetProperty("duration").GetDouble());
        Assert.Equal(Path.Combine(tempDir.FullName, "segment-001.mp4"), diagnostic.RootElement.GetProperty("outputSegmentPath").GetString());
        Assert.Equal("ffmpeg stdout", diagnostic.RootElement.GetProperty("stdout").GetString());
        Assert.Equal("segment encoder exploded", diagnostic.RootElement.GetProperty("stderr").GetString());
        Assert.Equal(1, diagnostic.RootElement.GetProperty("exitCode").GetInt32());
        Assert.False(diagnostic.RootElement.GetProperty("timedOut").GetBoolean());
        Assert.Contains("ffmpeg -y -loop 1", diagnostic.RootElement.GetProperty("ffmpegCommand").GetString(), StringComparison.Ordinal);
        Assert.Contains("segment encoder exploded", string.Join(Environment.NewLine, logger.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_SceneIndexInSegmentErrorMapsToShortsManifest()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-segment-manifest-map");
        var outputPath = Path.Combine(tempDir.FullName, "short-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);

        var scenes = new List<RenderScene>();
        var processRunner = new FailingSegmentProcessRunner("scene two failed");
        for (var i = 0; i < 2; i++)
        {
            var visualPath = Path.Combine(tempDir.FullName, $"scene-{i + 1}.png");
            var sceneAudioPath = Path.Combine(tempDir.FullName, $"scene-{i + 1}.mp3");
            await File.WriteAllBytesAsync(visualPath, [4, 5, 6]);
            await File.WriteAllBytesAsync(sceneAudioPath, [7, 8, 9]);
            scenes.Add(new RenderScene { Caption = $"Scene {i + 1}", SceneId = $"scene-{i + 1:000}", ObjectName = i == 0 ? "Moon" : "Mars", VisualPath = visualPath, AudioPath = sceneAudioPath, DurationSeconds = 6 });
            processRunner.ProbeDurationsByPath[sceneAudioPath] = 6d;
        }
        processRunner.FailOnFfmpegInvocation = 2;

        var fileSystem = new InMemoryFileSystem();
        var sut = CreateService(fileSystem, processRunner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RenderAsync(new RenderManifest
        {
            Title = "Short manifest map",
            AudioPath = audioPath,
            OutputPath = outputPath,
            OutputWidth = 1080,
            OutputHeight = 1920,
            Scenes = scenes
        }, CancellationToken.None));

        using var manifestJson = JsonDocument.Parse(fileSystem.TextWrites[Path.Combine(tempDir.FullName, "shorts-render-manifest-final.json")]);
        using var diagnosticJson = JsonDocument.Parse(fileSystem.TextWrites[Path.Combine(tempDir.FullName, "render-segment-failure-scene-1.json")]);
        var manifestEntry = manifestJson.RootElement.EnumerateArray().Single(entry => entry.GetProperty("sceneIndex").GetInt32() == 1);
        Assert.Equal("scene-002", manifestEntry.GetProperty("sceneId").GetString());
        Assert.Equal("scene-002", diagnosticJson.RootElement.GetProperty("sceneId").GetString());
        Assert.Equal(manifestEntry.GetProperty("outputSegmentPath").GetString(), diagnosticJson.RootElement.GetProperty("outputSegmentPath").GetString());
    }

    [Fact]
    public async Task FfmpegVideoRenderService_LogsFfprobeTimingSeparatelyFromSegmentRenderTiming()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-probe-timing");
        var outputPath = Path.Combine(tempDir.FullName, "short-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        var sceneAudioPath = Path.Combine(tempDir.FullName, "scene-1.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
        await File.WriteAllBytesAsync(sceneAudioPath, [7, 8, 9]);

        var logger = new TestLogger<FfmpegVideoRenderService>();
        var fileSystem = new InMemoryFileSystem();
        var processRunner = new FailingSegmentProcessRunner("segment failed after probe") { ProbeDurationsByPath = { [sceneAudioPath] = 6d } };
        var sut = CreateService(fileSystem, processRunner, logger: logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RenderAsync(new RenderManifest
        {
            Title = "Short probe timing",
            AudioPath = audioPath,
            OutputPath = outputPath,
            OutputWidth = 1080,
            OutputHeight = 1920,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, AudioPath = sceneAudioPath, DurationSeconds = 6 }]
        }, CancellationToken.None));

        var messages = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains("ffprobe duration probe completed", messages, StringComparison.Ordinal);
        Assert.Contains("FFmpeg segment render failed", messages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_UsesNarrationDuration_ForSceneSegments()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-duration");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);

        var scenes = new List<RenderScene>();
        for (var i = 0; i < 5; i++)
        {
            var scenePath = Path.Combine(tempDir.FullName, $"scene-{i + 1}.png");
            await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
            scenes.Add(new RenderScene { Caption = $"Scene {i + 1}", VisualPath = scenePath, DurationSeconds = 36 });
        }

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 115d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 115d
            }
        };
        var sut = CreateService(fileSystem, processRunner);

        await sut.RenderAsync(new RenderManifest { Title = "Sky", AudioPath = audioPath, OutputPath = outputPath, Scenes = scenes }, CancellationToken.None);

        var segmentCommands = processRunner.Commands.Where(command => command.Contains("-loop 1 -i", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, segmentCommands.Count);
        Assert.All(segmentCommands, command => Assert.Contains("-frames:v 702", command, StringComparison.Ordinal));
        Assert.All(segmentCommands, command => Assert.Contains("-r 30", command, StringComparison.Ordinal));
        Assert.All(segmentCommands, command => Assert.Contains("zoompan=", command, StringComparison.Ordinal));
        Assert.All(segmentCommands, command => Assert.DoesNotContain(" -t ", command, StringComparison.Ordinal));
        var diagnostics = fileSystem.TextWrites[Path.Combine(tempDir.FullName, "ffmpeg.log")];
        Assert.Contains("narrationDurationSeconds: 115", diagnostics, StringComparison.Ordinal);
        Assert.Contains("sceneCount: 5", diagnostics, StringComparison.Ordinal);
        Assert.Contains("transitionDurationSeconds: 0.5", diagnostics, StringComparison.Ordinal);
        Assert.Contains("transitionCount: 4", diagnostics, StringComparison.Ordinal);
        Assert.Contains("totalTransitionOverlapSeconds: 2", diagnostics, StringComparison.Ordinal);
        Assert.Contains("adjustedTotalSceneDuration: 117", diagnostics, StringComparison.Ordinal);
        Assert.Contains("calculatedSceneDurationSeconds: 23.4", diagnostics, StringComparison.Ordinal);
        Assert.Contains("expectedCombinedDurationSeconds: 115", diagnostics, StringComparison.Ordinal);
        Assert.Contains("actualCombinedDurationSeconds: 115", diagnostics, StringComparison.Ordinal);
        Assert.Contains("segment-sync-report.json", string.Join("|", fileSystem.TextWrites.Keys), StringComparison.Ordinal);
    }


    [Fact]
    public async Task FfmpegVideoRenderService_DoesNotApplyAtempoCompression_ByDefault()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-no-atempo");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 12d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 12d
            }
        };
        var sut = CreateService(fileSystem, processRunner);

        await sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "One calm narration scene", VisualPath = scenePath, DurationSeconds = 6 }]
        }, CancellationToken.None);

        Assert.All(processRunner.Commands, command => Assert.DoesNotContain("atempo", command, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FfmpegVideoRenderService_WritesSpeechSpeedDiagnostics()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-speech-diagnostics");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 1d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 1d
            }
        };
        var sut = CreateService(fileSystem, processRunner, enableTransitions: false);

        await sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "This narration is deliberately much too fast", VisualPath = scenePath, DurationSeconds = 6 }]
        }, CancellationToken.None);

        var diagnosticsPath = Path.Combine(tempDir.FullName, "speech-speed-diagnostics.json");
        var diagnostics = fileSystem.TextWrites[diagnosticsPath];
        Assert.Contains("\"sceneId\": \"scene-001\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("\"ssmlProsodyRate\": \"medium\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("\"tempoApplied\": false", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Narration may be too fast.", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_SegmentedVisualDurationFollowsSceneAudioDuration()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-scene-audio-duration");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        var sceneAudioPath = Path.Combine(tempDir.FullName, "scene-1.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
        await File.WriteAllBytesAsync(sceneAudioPath, [7, 8, 9]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath = { [sceneAudioPath] = 8.25d }
        };
        var sut = CreateService(fileSystem, processRunner, useSegmentedNarration: true);

        await sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, AudioPath = sceneAudioPath, DurationSeconds = 2 }]
        }, CancellationToken.None);

        var segmentCommand = processRunner.Commands.Single(command => command.Contains("-loop 1", StringComparison.Ordinal) && command.Contains(sceneAudioPath, StringComparison.Ordinal));
        Assert.Contains("-t 8.25", segmentCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("-shortest", string.Join(" ", processRunner.Commands), StringComparison.Ordinal);
        var report = fileSystem.TextWrites[Path.Combine(tempDir.FullName, "segment-sync-report.json")];
        Assert.Contains("\"synchronizationStatus\": \"Synchronized\"", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_DisablesKenBurns_WhenConfigured()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-kenburns-disabled");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath = { [audioPath] = 10d, [Path.Combine(tempDir.FullName, "combined.mp4")] = 10d }
        };
        var sut = CreateService(fileSystem, processRunner, enableKenBurns: false);

        await sut.RenderAsync(new RenderManifest { Title = "Sky", AudioPath = audioPath, OutputPath = outputPath, Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 10 }] }, CancellationToken.None);
        var segmentCommand = processRunner.Commands.Single(command => command.Contains("-loop 1 -i", StringComparison.Ordinal));
        Assert.DoesNotContain("zoompan=", segmentCommand, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_KeepsStableZoomCentered_WhenDirectionalMotionDisabled()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-direction-disabled");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner { ProbeDurationsByPath = { [audioPath] = 10d, [Path.Combine(tempDir.FullName, "combined.mp4")] = 10d } };
        var sut = CreateService(fileSystem, processRunner, enableDirectionalMotion: false);

        await sut.RenderAsync(new RenderManifest { Title = "Sky", AudioPath = audioPath, OutputPath = outputPath, Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 10, DirectionLabel = "West" }] }, CancellationToken.None);
        var segmentCommand = processRunner.Commands.Single(command => command.Contains("-loop 1 -i", StringComparison.Ordinal));
        Assert.Contains("x='iw/2-(iw/zoom/2)+(0*", segmentCommand, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_AddsDirectionalPan_WhenDirectionalMotionEnabled()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-direction-enabled");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner { ProbeDurationsByPath = { [audioPath] = 10d, [Path.Combine(tempDir.FullName, "combined.mp4")] = 10d } };
        var sut = CreateService(fileSystem, processRunner, enableDirectionalMotion: true, directionalPanStrength: 0.04d);

        await sut.RenderAsync(new RenderManifest { Title = "Sky", AudioPath = audioPath, OutputPath = outputPath, Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 10, DirectionLabel = "West", ObjectName = "Moon", ObjectType = "Planet" }] }, CancellationToken.None);
        var segmentCommand = processRunner.Commands.Single(command => command.Contains("-loop 1 -i", StringComparison.Ordinal));
        Assert.Contains("+(1*0.04*iw*", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("directional-motion-settings.json", string.Join('\n', fileSystem.TextWrites.Keys), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_UsesExpectedOutputSize_ForShortsAndLong()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-output-size");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 10d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 10d
            }
        };
        var sut = CreateService(fileSystem, processRunner);
        await sut.RenderAsync(new RenderManifest { Title = "Long", AudioPath = audioPath, OutputPath = Path.Combine(tempDir.FullName, "final-video.mp4"), Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 10 }] }, CancellationToken.None);
        Assert.Contains(processRunner.Commands, command => command.Contains("s=1920x1080", StringComparison.Ordinal));

        var shortDir = Directory.CreateTempSubdirectory("ffmpeg-render-output-size-short");
        var shortAudio = Path.Combine(shortDir.FullName, "narration.mp3");
        var shortScene = Path.Combine(shortDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(shortAudio, [1, 2, 3]);
        await File.WriteAllBytesAsync(shortScene, [4, 5, 6]);
        var shortRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [shortAudio] = 10d,
                [Path.Combine(shortDir.FullName, "combined.mp4")] = 10d
            }
        };
        var shortSvc = CreateService(new InMemoryFileSystem(), shortRunner);
        await shortSvc.RenderAsync(new RenderManifest { Title = "Short", AudioPath = shortAudio, OutputPath = Path.Combine(shortDir.FullName, "short-video.mp4"), OutputWidth = 1080, OutputHeight = 1920, Scenes = [new RenderScene { Caption = "Scene", VisualPath = shortScene, DurationSeconds = 10 }] }, CancellationToken.None);
        Assert.Contains(shortRunner.Commands, command => command.Contains("s=1080x1920", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FfmpegVideoRenderService_AdjustsSceneDuration_ForTransitionOverlap()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-transition-adjustment");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);

        var scenes = new List<RenderScene>();
        for (var i = 0; i < 5; i++)
        {
            var scenePath = Path.Combine(tempDir.FullName, $"scene-{i + 1}.png");
            await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
            scenes.Add(new RenderScene { Caption = $"Scene {i + 1}", VisualPath = scenePath, DurationSeconds = 36 });
        }

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 81.624d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 81.624d
            }
        };
        var sut = CreateService(fileSystem, processRunner, useSegmentedNarration: true);

        await sut.RenderAsync(new RenderManifest { Title = "Sky", AudioPath = audioPath, OutputPath = outputPath, Scenes = scenes }, CancellationToken.None);

        var segmentCommands = processRunner.Commands.Where(command => command.Contains("-loop 1 -i", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, segmentCommands.Count);
        Assert.All(segmentCommands, command => Assert.Contains("-frames:v 514", command, StringComparison.Ordinal));

        var diagnostics = fileSystem.TextWrites[Path.Combine(tempDir.FullName, "ffmpeg.log")];
        Assert.Contains("narrationDurationSeconds: 81.624", diagnostics, StringComparison.Ordinal);
        Assert.Contains("transitionCount: 4", diagnostics, StringComparison.Ordinal);
        Assert.Contains("totalTransitionOverlapSeconds: 2", diagnostics, StringComparison.Ordinal);
        Assert.Contains("adjustedTotalSceneDuration: 83.624", diagnostics, StringComparison.Ordinal);
        Assert.Contains("calculatedSceneDurationSeconds: 16.7248", diagnostics, StringComparison.Ordinal);
        Assert.Contains("expectedCombinedDurationSeconds: 81.624", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_DoesNotAdjustSceneDuration_WhenTransitionsDisabled()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-transition-disabled");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);

        var scenes = new List<RenderScene>();
        for (var i = 0; i < 5; i++)
        {
            var scenePath = Path.Combine(tempDir.FullName, $"scene-{i + 1}.png");
            await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
            scenes.Add(new RenderScene { Caption = $"Scene {i + 1}", VisualPath = scenePath, DurationSeconds = 36 });
        }

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 81.624d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 81.624d
            }
        };
        var sut = CreateService(fileSystem, processRunner, useSegmentedNarration: true, enableTransitions: false);

        await sut.RenderAsync(new RenderManifest { Title = "Sky", AudioPath = audioPath, OutputPath = outputPath, Scenes = scenes }, CancellationToken.None);

        var diagnostics = fileSystem.TextWrites[Path.Combine(tempDir.FullName, "ffmpeg.log")];
        Assert.Contains("transitionDurationSeconds: 0.5", diagnostics, StringComparison.Ordinal);
        Assert.Contains("totalTransitionOverlapSeconds: 0", diagnostics, StringComparison.Ordinal);
        Assert.Contains("adjustedTotalSceneDuration: 81.624", diagnostics, StringComparison.Ordinal);
        Assert.Contains("calculatedSceneDurationSeconds: 16.3248", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_AutoDisablesTransitions_WhenSceneDurationTooShort()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-transition-short");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        var scene1 = Path.Combine(tempDir.FullName, "scene-1.png");
        var scene2 = Path.Combine(tempDir.FullName, "scene-2.png");
        await File.WriteAllBytesAsync(scene1, [4, 5, 6]);
        await File.WriteAllBytesAsync(scene2, [7, 8, 9]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 2d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 2d
            }
        };
        var sut = CreateService(fileSystem, processRunner, useSegmentedNarration: true, enableTransitions: true, transitionDurationSeconds: 0.5d);

        await sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes =
            [
                new RenderScene { Caption = "Scene 1", VisualPath = scene1, DurationSeconds = 2 },
                new RenderScene { Caption = "Scene 2", VisualPath = scene2, DurationSeconds = 2 }
            ]
        }, CancellationToken.None);

        var concatCommand = processRunner.Commands.Single(command => command.Contains("combined.mp4", StringComparison.Ordinal));
        Assert.Contains("-f concat", concatCommand, StringComparison.Ordinal);
        var diagnostics = fileSystem.TextWrites[Path.Combine(tempDir.FullName, "ffmpeg.log")];
        Assert.Contains("transitionsEnabled: False", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_Fails_WhenCombinedDurationDoesNotMatchNarration()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-duration-mismatch");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 115d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 180d
            }
        };
        var sut = CreateService(fileSystem, processRunner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 180 }]
        }, CancellationToken.None));
        Assert.Contains("differs from narration duration", ex.Message, StringComparison.OrdinalIgnoreCase);
    }



    [Fact]
    public async Task FfmpegVideoRenderService_UsesExplicitFfprobePath_WhenConfigured()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffprobe-explicit");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        var ffprobePath = Path.Combine(tempDir.FullName, "ffprobe.exe");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
        await File.WriteAllBytesAsync(ffprobePath, [7, 8, 9]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 10d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 10d
            }
        };
        var sut = CreateService(fileSystem, processRunner, useSegmentedNarration: true, ffprobePath: ffprobePath);

        await sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 10 }]
        }, CancellationToken.None);

        Assert.Contains(processRunner.FileNames, fileName => string.Equals(fileName, ffprobePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FfmpegVideoRenderService_DerivesFfprobePath_FromFfmpegPath()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffprobe-derived");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        var ffmpegPath = Path.Combine(tempDir.FullName, "ffmpeg.exe");
        var ffprobePath = Path.Combine(tempDir.FullName, "ffprobe.exe");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
        await File.WriteAllBytesAsync(ffprobePath, [7, 8, 9]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 10d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 10d
            }
        };
        var sut = CreateService(fileSystem, processRunner, useSegmentedNarration: true, ffmpegPath: ffmpegPath);

        await sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 10 }]
        }, CancellationToken.None);

        Assert.Contains(processRunner.FileNames, fileName => string.Equals(fileName, ffprobePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FfmpegVideoRenderService_FallsBackToBareFfprobe_WhenNoPathConfigured()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffprobe-fallback");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [audioPath] = 10d,
                [Path.Combine(tempDir.FullName, "combined.mp4")] = 10d
            }
        };
        var sut = CreateService(fileSystem, processRunner, useSegmentedNarration: true, ffmpegPath: "");

        await sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 10 }]
        }, CancellationToken.None);

        Assert.Contains(processRunner.FileNames, fileName => string.Equals(fileName, "ffprobe", StringComparison.Ordinal));
    }


    [Fact]
    public async Task FfmpegVideoRenderService_SegmentedKenBurnsAndFade_AreVideoOnlyEffects()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-segmented-effects");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        var sceneAudioPath = Path.Combine(tempDir.FullName, "scene-1.mp3");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
        await File.WriteAllBytesAsync(sceneAudioPath, [7, 8, 9]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner
        {
            ProbeDurationsByPath =
            {
                [sceneAudioPath] = 8d,
                [Path.Combine(tempDir.FullName, "segment-001.mp4")] = 8d
            }
        };
        var sut = CreateService(fileSystem, processRunner);

        await sut.RenderAsync(new RenderManifest
        {
            Title = "Segmented effects",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, AudioPath = sceneAudioPath, DurationSeconds = 8 }]
        }, CancellationToken.None);

        var segmentCommand = processRunner.Commands.Single(command => command.Contains(sceneAudioPath, StringComparison.Ordinal));
        Assert.Contains("zoompan=z='1 + (0.1)*pow(on/240.0,1.2)'", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("fade=t=in:st=0:d=0.75", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("fade=t=out:st=7.25:d=0.75", segmentCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("atempo", segmentCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("-shortest", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("video-effects-report.json", string.Join('|', fileSystem.TextWrites.Keys), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_EffectsDisabled_ProducesPlainScaledVideo()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-effects-disabled");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner { ProbeDurationsByPath = { [audioPath] = 6d, [Path.Combine(tempDir.FullName, "combined.mp4")] = 6d } };
        var sut = CreateService(fileSystem, processRunner, enableTransitions: false, enableKenBurns: false, enableFadeInOut: false);

        await sut.RenderAsync(new RenderManifest { Title = "Plain", AudioPath = audioPath, OutputPath = outputPath, Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 6 }] }, CancellationToken.None);

        var segmentCommand = processRunner.Commands.Single(command => command.Contains("-loop 1 -i", StringComparison.Ordinal));
        Assert.Contains("-vf \"fps=30,scale=2560:1440:flags=lanczos\"", segmentCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("zoompan", segmentCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("fade=t=", segmentCommand, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_ShortVideoUsesPortraitSafeEffects()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-short-effects");
        var outputPath = Path.Combine(tempDir.FullName, "short-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner { ProbeDurationsByPath = { [audioPath] = 5d, [Path.Combine(tempDir.FullName, "combined.mp4")] = 5d, [outputPath] = 5d } };
        var sut = CreateService(fileSystem, processRunner, enableTransitions: false);

        await sut.RenderAsync(new RenderManifest { Title = "Short", AudioPath = audioPath, OutputPath = outputPath, OutputWidth = 1080, OutputHeight = 1920, EnableVerticalCrop = true, Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 5 }] }, CancellationToken.None);

        var segmentCommand = processRunner.Commands.Single(command => command.Contains("-loop 1 -i", StringComparison.Ordinal));
        Assert.Contains("scale=1080:1920:force_original_aspect_ratio=increase", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("crop=1080:1920", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("s=1080x1920", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("(0.08)*pow", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("fade=t=in:st=0:d=0.4", segmentCommand, StringComparison.Ordinal);
    }


    [Fact]
    public async Task FfmpegVideoRenderService_LongVideoUses1440pProductionPreset_WhenEnabled()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-long-production");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner { ProbeDurationsByPath = { [audioPath] = 6d, [Path.Combine(tempDir.FullName, "combined.mp4")] = 6d, [outputPath] = 6d } };
        var sut = CreateService(fileSystem, processRunner, enableTransitions: false, enableYouTube1440pUpscale: true);

        await sut.RenderAsync(new RenderManifest { Title = "Long", AudioPath = audioPath, OutputPath = outputPath, Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 6 }] }, CancellationToken.None);

        var segmentCommand = processRunner.Commands.Single(command => command.Contains("-loop 1 -i", StringComparison.Ordinal));
        var finalCommand = processRunner.Commands.Last(command => command.Contains("-movflags +faststart", StringComparison.Ordinal) && command.Contains(outputPath, StringComparison.Ordinal));
        Assert.Contains("scale=2560:1440:flags=lanczos", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-c:v libx264", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-preset slow", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-crf 18", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-b:v 20M", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-maxrate 24M", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-pix_fmt yuv420p", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-b:a 320k", finalCommand, StringComparison.Ordinal);
        Assert.Contains("-movflags +faststart", finalCommand, StringComparison.Ordinal);
        Assert.True(new FileInfo(outputPath).Length > 0);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_WritesEncodingReport_WithMinimumBitrateAndYuv420p()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-encoding-report");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner { ProbeDurationsByPath = { [audioPath] = 6d, [Path.Combine(tempDir.FullName, "combined.mp4")] = 6d, [outputPath] = 6d } };
        var sut = CreateService(fileSystem, processRunner, enableTransitions: false, enableYouTube1440pUpscale: true);

        await sut.RenderAsync(new RenderManifest { Title = "Long", AudioPath = audioPath, OutputPath = outputPath, Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 6 }] }, CancellationToken.None);

        var reportJson = fileSystem.TextWrites[Path.Combine(tempDir.FullName, "video-encoding-report.json")];
        using var report = JsonDocument.Parse(reportJson);
        var root = report.RootElement;
        Assert.Equal("YouTubeLongProduction", root.GetProperty("presetName").GetString());
        Assert.Equal("2560x1440", root.GetProperty("resolution").GetString());
        Assert.Equal("20M", root.GetProperty("bitrate").GetString());
        Assert.Equal("libx264", root.GetProperty("codec").GetString());
        Assert.Equal(18, root.GetProperty("crf").GetInt32());
        Assert.Equal("slow", root.GetProperty("preset").GetString());
        Assert.Equal(30, root.GetProperty("fps").GetInt32());
        Assert.Equal("yuv420p", root.GetProperty("pixelFormat").GetString());
        Assert.Equal("320k", root.GetProperty("audioBitrate").GetString());
        Assert.True(root.GetProperty("faststartEnabled").GetBoolean());
        Assert.True(root.GetProperty("estimatedUploadSizeBytes").GetInt64() > 0);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_ShortsUsePortraitProductionPreset()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-short-production");
        var outputPath = Path.Combine(tempDir.FullName, "short-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var processRunner = new SegmentAwareProcessRunner { ProbeDurationsByPath = { [audioPath] = 5d, [Path.Combine(tempDir.FullName, "combined.mp4")] = 5d, [outputPath] = 5d } };
        var sut = CreateService(fileSystem, processRunner, enableTransitions: false);

        await sut.RenderAsync(new RenderManifest { Title = "Short", AudioPath = audioPath, OutputPath = outputPath, OutputWidth = 1080, OutputHeight = 1920, EnableVerticalCrop = true, Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 5 }] }, CancellationToken.None);

        var segmentCommand = processRunner.Commands.Single(command => command.Contains("-loop 1 -i", StringComparison.Ordinal));
        var finalCommand = processRunner.Commands.Last(command => command.Contains("-movflags +faststart", StringComparison.Ordinal) && command.Contains(outputPath, StringComparison.Ordinal));
        Assert.Contains("scale=1080:1920:force_original_aspect_ratio=increase", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-preset slow", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-crf 18", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-b:v 12M", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-maxrate 16M", segmentCommand, StringComparison.Ordinal);
        Assert.Contains("-pix_fmt yuv420p", finalCommand, StringComparison.Ordinal);
        Assert.Contains("-b:a 256k", finalCommand, StringComparison.Ordinal);
        Assert.Contains("-movflags +faststart", finalCommand, StringComparison.Ordinal);

        var reportJson = fileSystem.TextWrites[Path.Combine(tempDir.FullName, "video-encoding-report.json")];
        using var report = JsonDocument.Parse(reportJson);
        Assert.Equal("YouTubeShortProduction", report.RootElement.GetProperty("presetName").GetString());
        Assert.Equal("1080x1920", report.RootElement.GetProperty("resolution").GetString());
    }

    [Theory]
    [InlineData(180, 36, 360)]
    [InlineData(180, 100, 1000)]
    [InlineData(30, 10, 300)]
    [InlineData(450, 20, 450)]
    public void FfmpegVideoRenderService_CalculatesEffectiveSegmentTimeout(int configuredSeconds, double sceneDurationSeconds, int expectedSeconds)
    {
        var effective = FfmpegVideoRenderService.CalculateEffectiveSegmentTimeoutSeconds(configuredSeconds, sceneDurationSeconds);

        Assert.Equal(expectedSeconds, effective);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_FailsCleanly_WhenFfmpegTimeoutExceeded()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-timeout");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var sut = CreateService(fileSystem, new HangingProcessRunner(), ffmpegTimeoutSeconds: 1);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 6 }]
        }, CancellationToken.None));

        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ffmpeg", fileSystem.TextWrites[Path.Combine(tempDir.FullName, "ffmpeg.log")], StringComparison.OrdinalIgnoreCase);
    }

    private static FfmpegVideoRenderService CreateService(IFileSystem fileSystem, IProcessRunner processRunner, int ffmpegTimeoutSeconds = 120, bool useSegmentedNarration = false, string ffmpegPath = "ffmpeg", string? ffprobePath = null, bool enableTransitions = true, double transitionDurationSeconds = 0.5d, string transitionType = "fade", bool enableKenBurns = true, bool enableDirectionalMotion = false, double directionalPanStrength = 0.04d, bool enableFadeInOut = true, bool enableYouTube1440pUpscale = true, Microsoft.Extensions.Logging.ILogger<FfmpegVideoRenderService>? logger = null)
    {
        var options = Options.Create(new RenderingOptions
        {
            FfmpegPath = ffmpegPath,
            FfprobePath = ffprobePath,
            WorkingDirectory = "./media-output",
            VideoWidth = 1280,
            VideoHeight = 720,
            FrameRate = 30,
            EnableTransitions = enableTransitions,
            TransitionDurationSeconds = transitionDurationSeconds,
            TransitionType = transitionType,
            UseSegmentedNarration = useSegmentedNarration,
            FfmpegTimeoutSeconds = ffmpegTimeoutSeconds,
            FfmpegSegmentTimeoutSeconds = 120,
            WriteSegmentDiagnostics = true,
            KeepIntermediateFiles = true,
            EnableKenBurns = enableKenBurns,
            EnableFadeInOut = enableFadeInOut,
            FadeDurationSeconds = 0.75d,
            ShortFadeDurationSeconds = 0.4d,
            KenBurnsZoomStart = 1.0d,
            KenBurnsZoomEnd = 1.10d,
            ShortKenBurnsZoomEnd = 1.08d,
            KenBurnsFps = 30,
            KenBurnsUseEasing = true,
            EnableDirectionalMotion = enableDirectionalMotion,
            DirectionalPanStrength = directionalPanStrength,
            EnableYouTube1440pUpscale = enableYouTube1440pUpscale
        });

        return new FfmpegVideoRenderService(
            options,
            new RenderManifestBuilder(),
            new FfmpegArgumentBuilder(),
            processRunner,
            fileSystem,
            logger ?? NullLogger<FfmpegVideoRenderService>.Instance);
    }
    private sealed class HangingProcessRunner : IProcessRunner
    {
        public async Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new ProcessExecutionResult(0, "", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, fileName, arguments, string.Empty, false);
        }
    }

    private sealed class InMemoryFileSystem : IFileSystem
    {
        public HashSet<string> CreatedDirectories { get; } = [];
        public Dictionary<string, string> TextWrites { get; } = [];
        public Dictionary<string, byte[]> ByteWrites { get; } = [];

        public void CreateDirectory(string path) => CreatedDirectories.Add(path);

        public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        {
            TextWrites[path] = contents;
            return Task.CompletedTask;
        }

        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        {
            ByteWrites[path] = bytes;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken, TimeSpan? timeout = null)
            => throw new InvalidOperationException("ffmpeg missing");
    }

    private sealed class FailingProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken, TimeSpan? timeout = null)
            => Task.FromResult(new ProcessExecutionResult(
                ExitCode: 1,
                StandardOutput: "",
                StandardError: "ffmpeg failed",
                StartTimeUtc: DateTimeOffset.UtcNow,
                EndTimeUtc: DateTimeOffset.UtcNow.AddSeconds(1),
                FileName: fileName,
                Arguments: arguments,
                ExceptionText: string.Empty,
                TimedOut: false));
    }

    private sealed class NoopProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken, TimeSpan? timeout = null)
            => Task.FromResult(new ProcessExecutionResult(
                ExitCode: 0,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                StartTimeUtc: DateTimeOffset.UtcNow,
                EndTimeUtc: DateTimeOffset.UtcNow,
                FileName: fileName,
                Arguments: arguments,
                ExceptionText: string.Empty,
                TimedOut: false));
    }


    private sealed class FailingSegmentProcessRunner(string stderr) : IProcessRunner
    {
        public Dictionary<string, double> ProbeDurationsByPath { get; } = [];
        public int FailOnFfmpegInvocation { get; set; } = 1;
        private int _ffmpegInvocations;

        public Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            if (string.Equals(Path.GetFileName(fileName), "ffprobe", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(fileName), "ffprobe.exe", StringComparison.OrdinalIgnoreCase))
            {
                var probePath = ExtractLastQuotedPath(arguments);
                var duration = probePath is not null && ProbeDurationsByPath.TryGetValue(probePath, out var value) ? value : 0d;
                return Task.FromResult(new ProcessExecutionResult(duration > 0 ? 0 : 1, duration > 0 ? duration.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty, string.Empty, DateTimeOffset.UtcNow.AddMilliseconds(-120), DateTimeOffset.UtcNow, fileName, arguments, string.Empty, false));
            }

            _ffmpegInvocations++;
            if (_ffmpegInvocations == FailOnFfmpegInvocation)
            {
                return Task.FromResult(new ProcessExecutionResult(1, "ffmpeg stdout", stderr, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, fileName, arguments, string.Empty, false));
            }

            var outputPath = ExtractOutputPath(arguments);
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                File.WriteAllBytes(outputPath, [1, 2, 3]);
            }

            return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, fileName, arguments, string.Empty, false));
        }

        private static string? ExtractOutputPath(string arguments)
        {
            var parts = arguments.Split('"', StringSplitOptions.RemoveEmptyEntries);
            return parts.LastOrDefault(static value => value.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
        }

        private static string? ExtractLastQuotedPath(string arguments)
        {
            var parts = arguments.Split('"', StringSplitOptions.RemoveEmptyEntries);
            return parts.LastOrDefault(static value => value.Contains('/') || value.Contains('\\'));
        }
    }

    private sealed class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }

    private sealed class SegmentAwareProcessRunner : IProcessRunner
    {
        public List<string> Commands { get; } = [];
        public List<string> FileNames { get; } = [];
        public Dictionary<string, double> ProbeDurationsByPath { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            Commands.Add(arguments);
            FileNames.Add(fileName);
            if (string.Equals(Path.GetFileName(fileName), "ffprobe", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(fileName), "ffprobe.exe", StringComparison.OrdinalIgnoreCase))
            {
                var probePath = ExtractLastQuotedPath(arguments);
                var duration = probePath is not null && ProbeDurationsByPath.TryGetValue(probePath, out var value) ? value : 0d;
                return Task.FromResult(new ProcessExecutionResult(
                    ExitCode: duration > 0 ? 0 : 1,
                    StandardOutput: duration > 0 ? duration.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
                    StandardError: string.Empty,
                    StartTimeUtc: DateTimeOffset.UtcNow,
                    EndTimeUtc: DateTimeOffset.UtcNow,
                    FileName: fileName,
                    Arguments: arguments,
                    ExceptionText: string.Empty,
                    TimedOut: false));
            }

            var outputPath = ExtractOutputPath(arguments);
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                File.WriteAllBytes(outputPath, [1, 2, 3]);
            }

            return Task.FromResult(new ProcessExecutionResult(
                ExitCode: 0,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                StartTimeUtc: DateTimeOffset.UtcNow,
                EndTimeUtc: DateTimeOffset.UtcNow,
                FileName: fileName,
                Arguments: arguments,
                ExceptionText: string.Empty,
                TimedOut: false));
        }

        private static string? ExtractOutputPath(string arguments)
        {
            var parts = arguments.Split('"', StringSplitOptions.RemoveEmptyEntries);
            return parts.LastOrDefault(static value => value.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
        }

        private static string? ExtractLastQuotedPath(string arguments)
        {
            var parts = arguments.Split('"', StringSplitOptions.RemoveEmptyEntries);
            return parts.LastOrDefault(static value => value.Contains('/') || value.Contains('\\'));
        }
    }
}
