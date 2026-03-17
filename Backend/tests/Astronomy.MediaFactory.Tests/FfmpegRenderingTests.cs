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

        var args = sut.Build(options, "/tmp/ffmpeg-input.txt", "/tmp/narration.mp3", "/tmp/final-video.mp4");

        Assert.Contains("-f concat", args);
        Assert.Contains("-i \"/tmp/ffmpeg-input.txt\"", args);
        Assert.Contains("-i \"/tmp/narration.mp3\"", args);
        Assert.Contains("scale=1280:720", args);
        Assert.Contains("\"/tmp/final-video.mp4\"", args);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_CreatesPlaceholder_WhenFfmpegFails()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-tests");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");
        var audioPath = Path.Combine(tempDir.FullName, "narration.mp3");
        var scenePath = Path.Combine(tempDir.FullName, "scene-1.png");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);

        var fileSystem = new InMemoryFileSystem();
        var sut = CreateService(fileSystem, new ThrowingProcessRunner());

        var result = await sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 6 }]
        }, CancellationToken.None);

        Assert.Equal(outputPath, result);
        Assert.Contains(Path.Combine(tempDir.FullName, "render-manifest.json"), fileSystem.TextWrites.Keys);
        Assert.Contains(Path.Combine(tempDir.FullName, "ffmpeg-input.txt"), fileSystem.TextWrites.Keys);
        Assert.Contains(Path.Combine(tempDir.FullName, "ffmpeg-command.txt"), fileSystem.TextWrites.Keys);
        Assert.Empty(fileSystem.ByteWrites[outputPath]);
    }

    [Fact]
    public async Task FfmpegVideoRenderService_CreatesPlaceholder_WhenAudioOrVisualMissing()
    {
        var tempDir = Directory.CreateTempSubdirectory("ffmpeg-render-validation");
        var outputPath = Path.Combine(tempDir.FullName, "final-video.mp4");

        var fileSystem = new InMemoryFileSystem();
        var sut = CreateService(fileSystem, new NoopProcessRunner());

        await sut.RenderAsync(new RenderManifest
        {
            Title = "Missing assets",
            AudioPath = Path.Combine(tempDir.FullName, "missing.mp3"),
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = Path.Combine(tempDir.FullName, "missing.png"), DurationSeconds = 6 }]
        }, CancellationToken.None);

        Assert.Empty(fileSystem.ByteWrites[outputPath]);
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

        await sut.RenderAsync(new RenderManifest
        {
            Title = "Sky",
            AudioPath = audioPath,
            OutputPath = outputPath,
            Scenes = [new RenderScene { Caption = "Scene", VisualPath = scenePath, DurationSeconds = 6 }]
        }, CancellationToken.None);

        var logPath = Path.Combine(tempDir.FullName, "ffmpeg.log");
        var diagnostics = fileSystem.TextWrites[logPath];
        Assert.Contains("Command:", diagnostics, StringComparison.Ordinal);
        Assert.Contains("ExitCode: 1", diagnostics, StringComparison.Ordinal);
        Assert.Contains("--- STDERR ---", diagnostics, StringComparison.Ordinal);
    }

    private static FfmpegVideoRenderService CreateService(IFileSystem fileSystem, IProcessRunner processRunner)
    {
        var options = Options.Create(new RenderingOptions
        {
            FfmpegPath = "ffmpeg",
            WorkingDirectory = "./media-output",
            VideoWidth = 1280,
            VideoHeight = 720,
            FrameRate = 30,
            ImageTransitionSeconds = 1
        });

        return new FfmpegVideoRenderService(
            options,
            new RenderManifestBuilder(),
            new FfmpegArgumentBuilder(),
            processRunner,
            fileSystem,
            NullLogger<FfmpegVideoRenderService>.Instance);
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
}
