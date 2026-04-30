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

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, _options.FfmpegTimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var (command, finalResult) = await RenderFromImageSegmentsAsync(plan, manifest.AudioPath, outputPath, outputDirectory, segmentConcatPath, linkedCts.Token);
            await _fileSystem.WriteAllTextAsync(commandPath, command, cancellationToken);
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, BuildProcessDiagnostics(finalResult), cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var timeoutMessage = $"FFmpeg timed out after {_options.FfmpegTimeoutSeconds} seconds while rendering image segments.";
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


    private async Task<(string Command, ProcessExecutionResult FinalResult)> RenderFromImageSegmentsAsync(
        RenderPlan plan,
        string narrationAudioPath,
        string outputPath,
        string outputDirectory,
        string segmentConcatPath,
        CancellationToken cancellationToken)
    {
        var segmentPaths = new List<string>();
        for (var i = 0; i < plan.Scenes.Count; i++)
        {
            var scene = plan.Scenes[i];
            var duration = scene.DurationSeconds > 0 ? scene.DurationSeconds : 3;
            var segmentPath = Path.Combine(outputDirectory, $"segment-{i + 1:000}.mp4");
            var segmentArguments =
                $"-y -loop 1 -t {duration.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{NormalizePath(scene.VisualPath)}\" -c:v libx264 -pix_fmt yuv420p \"{NormalizePath(segmentPath)}\"";

            _logger.LogInformation("Creating FFmpeg segment {SegmentIndex}/{SegmentCount}: {Command}", i + 1, plan.Scenes.Count, $"{_options.FfmpegPath} {segmentArguments}");
            var segmentResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, segmentArguments, cancellationToken);
            if (segmentResult.ExitCode != 0 || !File.Exists(segmentPath))
            {
                throw new InvalidOperationException($"FFmpeg segment creation failed for scene #{i + 1}.");
            }

            segmentPaths.Add(segmentPath);
        }

        var concatBody = string.Join(Environment.NewLine, segmentPaths.Select(path => $"file '{NormalizePath(path).Replace("'", "'\\''")}'"));
        await _fileSystem.WriteAllTextAsync(segmentConcatPath, concatBody, cancellationToken);

        var combinedPath = Path.Combine(outputDirectory, "combined.mp4");
        var concatArguments = $"-y -f concat -safe 0 -i \"{NormalizePath(segmentConcatPath)}\" -c copy \"{NormalizePath(combinedPath)}\"";
        _logger.LogInformation("Concatenating FFmpeg segments: {Command}", $"{_options.FfmpegPath} {concatArguments}");
        var concatResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, concatArguments, cancellationToken);
        if (concatResult.ExitCode != 0 || !File.Exists(combinedPath))
        {
            throw new InvalidOperationException("FFmpeg concat of scene segments failed.");
        }

        var finalArguments =
            $"-y -i \"{NormalizePath(combinedPath)}\" -i \"{NormalizePath(narrationAudioPath)}\" -shortest -c:v copy -c:a aac \"{NormalizePath(outputPath)}\"";
        var finalCommand = $"{_options.FfmpegPath} {finalArguments}";
        _logger.LogInformation("Rendering final FFmpeg output with narration: {Command}", finalCommand);

        var finalResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, finalArguments, cancellationToken);
        if (finalResult.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException("FFmpeg final render with narration failed.");
        }

        foreach (var segmentPath in segmentPaths)
        {
            if (File.Exists(segmentPath))
            {
                File.Delete(segmentPath);
            }
        }

        if (File.Exists(combinedPath))
        {
            File.Delete(combinedPath);
        }

        return (finalCommand, finalResult);
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
