using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Rendering;

public sealed class FfmpegVideoRenderService : IVideoRenderService
{
    private readonly RenderingOptions _options;
    private readonly RenderManifestBuilder _manifestBuilder;
    private readonly FfmpegArgumentBuilder _argumentBuilder;
    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<FfmpegVideoRenderService> _logger;

    public FfmpegVideoRenderService(
        IOptions<RenderingOptions> options,
        RenderManifestBuilder manifestBuilder,
        FfmpegArgumentBuilder argumentBuilder,
        IProcessRunner processRunner,
        IFileSystem fileSystem,
        ILogger<FfmpegVideoRenderService> logger)
    {
        _options = options.Value;
        _manifestBuilder = manifestBuilder;
        _argumentBuilder = argumentBuilder;
        _processRunner = processRunner;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken)
    {
        var outputPath = manifest.OutputPath;
        var outputDirectory = Path.GetDirectoryName(outputPath) ?? _options.WorkingDirectory;
        _fileSystem.CreateDirectory(outputDirectory);

        var plan = _manifestBuilder.Build(manifest);
        var manifestPath = Path.Combine(outputDirectory, "render-manifest.json");
        var concatPath = Path.Combine(outputDirectory, "ffmpeg-input.txt");
        var segmentConcatPath = Path.Combine(outputDirectory, "ffmpeg-segments.txt");
        var captionMetadataPath = Path.Combine(outputDirectory, "caption-metadata.json");
        var subtitleScaffoldPath = Path.Combine(outputDirectory, "subtitles.scaffold.srt");
        var commandPath = Path.Combine(outputDirectory, "ffmpeg-command.txt");
        var ffmpegLogPath = Path.Combine(outputDirectory, "ffmpeg.log");

        await _fileSystem.WriteAllTextAsync(manifestPath, plan.ManifestJson, cancellationToken);
        await _fileSystem.WriteAllTextAsync(concatPath, plan.ConcatInputContent, cancellationToken);
        await _fileSystem.WriteAllTextAsync(captionMetadataPath, plan.CaptionMetadataJson, cancellationToken);
        await _fileSystem.WriteAllTextAsync(subtitleScaffoldPath, plan.SubtitleScaffold, cancellationToken);

        var missingAssets = FindMissingAssets(manifest, plan);
        if (missingAssets.Count > 0)
        {
            var validationError = BuildMissingAssetMessage(missingAssets);
            _logger.LogWarning("Skipping FFmpeg render because input validation failed: {Reason}", validationError);
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, validationError, cancellationToken);
            throw new InvalidOperationException($"Video render input validation failed: {validationError}");
        }

        var hasSegmentedAudio = plan.Scenes.Any(s => !string.IsNullOrWhiteSpace(s.AudioPath) && File.Exists(s.AudioPath));
        if (_options.UseSegmentedNarration && hasSegmentedAudio)
        {
            await RenderFromSegmentsAsync(manifest, plan, outputDirectory, segmentConcatPath, cancellationToken);
            return outputPath;
        }

        if (!_options.UseSegmentedNarration && hasSegmentedAudio)
        {
            _logger.LogInformation("Bypassing segmented narration render flow because {Option}=false.", nameof(_options.UseSegmentedNarration));
        }

        var arguments = BuildSimpleArguments(plan, manifest.AudioPath, outputPath);
        var command = $"{_options.FfmpegPath} {arguments}";
        _logger.LogInformation("FFmpeg Command: {cmd}", command);
        await _fileSystem.WriteAllTextAsync(commandPath, command, cancellationToken);

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, _options.FfmpegTimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var result = await _processRunner.ExecuteAsync(_options.FfmpegPath, arguments, linkedCts.Token);
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, BuildProcessDiagnostics(result), cancellationToken);

            if (result.ExitCode != 0 || !File.Exists(outputPath))
            {
                _logger.LogError("FFmpeg failed with exit code {ExitCode}. STDERR: {Stderr}", result.ExitCode, result.StandardError);
                throw new InvalidOperationException($"FFmpeg failed with exit code {result.ExitCode}. STDERR: {result.StandardError}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var timeoutMessage = $"FFmpeg timed out after {_options.FfmpegTimeoutSeconds} seconds. Command: {_options.FfmpegPath} {arguments}";
            _logger.LogWarning(timeoutMessage);
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, timeoutMessage, cancellationToken);
            throw new InvalidOperationException(timeoutMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FFmpeg execution failed.");
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, ex.ToString(), cancellationToken);
            throw new InvalidOperationException($"FFmpeg execution failed. See {ffmpegLogPath} for details.", ex);
        }

        return outputPath;
    }

    private async Task RenderFromSegmentsAsync(RenderManifest manifest, RenderPlan plan, string outputDirectory, string segmentConcatPath, CancellationToken cancellationToken)
    {
        var segmentClipPaths = new List<string>();
        for (var i = 0; i < plan.Scenes.Count; i++)
        {
            var scene = plan.Scenes[i];
            var sceneAudioPath = scene.AudioPath;
            if (string.IsNullOrWhiteSpace(sceneAudioPath) || !File.Exists(sceneAudioPath))
            {
                continue;
            }

            var segmentOutputPath = Path.Combine(outputDirectory, $"segment-{i + 1:000}.mp4");
            var segmentArguments = $"-y -loop 1 -t {scene.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{scene.VisualPath}\" -i \"{sceneAudioPath}\" -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest \"{segmentOutputPath}\"";
            var segmentResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, segmentArguments, cancellationToken);
            if (segmentResult.ExitCode != 0 || !File.Exists(segmentOutputPath))
            {
                throw new InvalidOperationException($"FFmpeg segmented clip generation failed for scene #{i + 1}.");
            }

            segmentClipPaths.Add(segmentOutputPath);
        }

        if (segmentClipPaths.Count == 0)
        {
            throw new InvalidOperationException("Segmented narration flow was requested but no segment clips were produced.");
        }

        var concatBody = string.Join(Environment.NewLine, segmentClipPaths.Select(path => $"file '{path.Replace("'", "'\\''")}'"));
        await _fileSystem.WriteAllTextAsync(segmentConcatPath, concatBody, cancellationToken);

        var concatArguments = $"-y -f concat -safe 0 -i \"{segmentConcatPath}\" -c copy \"{manifest.OutputPath}\"";
        var concatResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, concatArguments, cancellationToken);
        if (concatResult.ExitCode != 0 || !File.Exists(manifest.OutputPath))
        {
            throw new InvalidOperationException("FFmpeg concat of segmented clips failed.");
        }
    }


    private string BuildSimpleArguments(RenderPlan plan, string narrationAudioPath, string outputPath)
    {
        var normalizedAudioPath = NormalizePath(narrationAudioPath);
        var normalizedOutputPath = NormalizePath(outputPath);
        var inputArgs = new List<string>();

        for (var i = 0; i < plan.Scenes.Count; i++)
        {
            var scene = plan.Scenes[i];
            var duration = scene.DurationSeconds > 0 ? scene.DurationSeconds : 3;
            var normalizedVisualPath = NormalizePath(scene.VisualPath);
            inputArgs.Add($"-loop 1 -t {duration.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{normalizedVisualPath}\"");
        }

        return string.Join(' ',
            "-y",
            string.Join(' ', inputArgs),
            $"-i \"{normalizedAudioPath}\"",
            $"-r {_options.FrameRate}",
            "-c:v libx264",
            "-pix_fmt yuv420p",
            "-c:a aac",
            "-shortest",
            $"\"{normalizedOutputPath}\"");
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');
    private static List<string> FindMissingAssets(RenderManifest manifest, RenderPlan plan)
    {
        var missingAssets = new List<string>();

        if (string.IsNullOrWhiteSpace(manifest.AudioPath) || !File.Exists(manifest.AudioPath))
        {
            missingAssets.Add($"Narration audio missing: '{manifest.AudioPath}'");
        }

        if (plan.Scenes.Count == 0)
        {
            missingAssets.Add("No scene visuals were provided.");
        }

        foreach (var scene in plan.Scenes.Where(scene => string.IsNullOrWhiteSpace(scene.VisualPath) || !File.Exists(scene.VisualPath)))
        {
            missingAssets.Add($"{scene.Segment} visual missing: '{scene.VisualPath}' (caption: '{scene.Caption}')");
        }

        return missingAssets;
    }

    private static string BuildMissingAssetMessage(IReadOnlyCollection<string> missingAssets)
        => string.Join(Environment.NewLine, missingAssets);

    private static string BuildProcessDiagnostics(ProcessExecutionResult result)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"Command: {result.FileName} {result.Arguments}".TrimEnd(),
            $"ExitCode: {result.ExitCode}",
            $"StartedUtc: {result.StartTimeUtc:O}",
            $"EndedUtc: {result.EndTimeUtc:O}",
            $"DurationMs: {result.Duration.TotalMilliseconds:F0}",
            string.IsNullOrWhiteSpace(result.ExceptionText) ? string.Empty : $"Exception: {result.ExceptionText}",
            "--- STDERR ---",
            result.StandardError,
            "--- STDOUT ---",
            result.StandardOutput
        }.Where(static line => !string.IsNullOrEmpty(line)));
    }
}
