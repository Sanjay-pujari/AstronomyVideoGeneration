using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.Contains("scale=1280:720", args);
        Assert.Contains("\"/tmp/final-video.mp4\"", args);
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

        Assert.Contains("crop=1080:1920", args);
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
    public async Task FfmpegVideoRenderService_BypassesSegmentFlow_WhenUseSegmentedNarrationDisabled()
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
        var processRunner = new SegmentAwareProcessRunner();
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

        var concatCommand = Assert.Single(processRunner.Commands.Where(command => command.Contains("-f concat", StringComparison.Ordinal)));
        Assert.Contains($"-i \"{audioPath}\"", concatCommand, StringComparison.Ordinal);
        Assert.Contains("-shortest", concatCommand, StringComparison.Ordinal);
    }


    [Theory]
    [InlineData(180, 36, 180)]
    [InlineData(180, 40, 200)]
    [InlineData(30, 10, 180)]
    [InlineData(300, 20, 300)]
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

    private static FfmpegVideoRenderService CreateService(IFileSystem fileSystem, IProcessRunner processRunner, int ffmpegTimeoutSeconds = 120)
    {
        var options = Options.Create(new RenderingOptions
        {
            FfmpegPath = "ffmpeg",
            WorkingDirectory = "./media-output",
            VideoWidth = 1280,
            VideoHeight = 720,
            FrameRate = 30,
            ImageTransitionSeconds = 1,
            UseSegmentedNarration = false,
            FfmpegTimeoutSeconds = ffmpegTimeoutSeconds,
            FfmpegSegmentTimeoutSeconds = 180,
            KeepIntermediateFiles = true
        });

        return new FfmpegVideoRenderService(
            options,
            new RenderManifestBuilder(),
            new FfmpegArgumentBuilder(),
            processRunner,
            fileSystem,
            NullLogger<FfmpegVideoRenderService>.Instance);
    }
    private sealed class HangingProcessRunner : IProcessRunner
    {
        public async Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new ProcessExecutionResult(0, "", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, fileName, arguments, string.Empty);
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
        public Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken)
            => throw new InvalidOperationException("ffmpeg missing");
    }

    private sealed class FailingProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ProcessExecutionResult(
                ExitCode: 1,
                StandardOutput: "",
                StandardError: "ffmpeg failed",
                StartTimeUtc: DateTimeOffset.UtcNow,
                EndTimeUtc: DateTimeOffset.UtcNow.AddSeconds(1),
                FileName: fileName,
                Arguments: arguments,
                ExceptionText: string.Empty));
    }

    private sealed class NoopProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ProcessExecutionResult(
                ExitCode: 0,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                StartTimeUtc: DateTimeOffset.UtcNow,
                EndTimeUtc: DateTimeOffset.UtcNow,
                FileName: fileName,
                Arguments: arguments,
                ExceptionText: string.Empty));
    }

    private sealed class SegmentAwareProcessRunner : IProcessRunner
    {
        public List<string> Commands { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            Commands.Add(arguments);

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
                ExceptionText: string.Empty));
        }

        private static string? ExtractOutputPath(string arguments)
        {
            var parts = arguments.Split('"', StringSplitOptions.RemoveEmptyEntries);
            return parts.LastOrDefault(static value => value.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
        }
    }
}
