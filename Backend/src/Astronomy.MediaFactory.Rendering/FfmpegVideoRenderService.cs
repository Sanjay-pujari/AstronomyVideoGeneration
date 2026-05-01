using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;

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

            var (command, finalResult) = await RenderFromImageSegmentsAsync(plan, manifest.AudioPath, outputPath, outputDirectory, segmentConcatPath, commandPath, linkedCts.Token);
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
            var segmentArguments = $"-y -loop 1 -i \"{scene.VisualPath}\" -i \"{sceneAudioPath}\" -shortest -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac \"{segmentOutputPath}\"";
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
        string commandPath,
        CancellationToken cancellationToken)
    {
        var narrationDurationSeconds = await ProbeMediaDurationSecondsAsync(narrationAudioPath, cancellationToken);
        if (narrationDurationSeconds <= 0)
        {
            throw new InvalidOperationException($"Could not determine narration duration from '{narrationAudioPath}'.");
        }

        var segmentPaths = new List<string>();
        var segmentDurationsSeconds = new List<double>();
        var segmentDiagnostics = new List<string>();
        var sceneCount = plan.Scenes.Count;
        var transitionDurationSeconds = GetTransitionDurationSeconds();
        var transitionsEnabled = IsXfadeEnabled(sceneCount, transitionDurationSeconds);
        var transitionCount = Math.Max(sceneCount - 1, 0);
        var totalTransitionOverlapSeconds = transitionsEnabled ? transitionDurationSeconds * transitionCount : 0d;
        var adjustedTotalSceneDuration = transitionsEnabled
            ? narrationDurationSeconds + totalTransitionOverlapSeconds
            : narrationDurationSeconds;
        var durationPerScene = sceneCount == 0 ? 1d : adjustedTotalSceneDuration / sceneCount;
        var expectedCombinedDurationSeconds = transitionsEnabled
            ? Math.Max(0d, adjustedTotalSceneDuration - totalTransitionOverlapSeconds)
            : durationPerScene * sceneCount;
        if (transitionsEnabled && durationPerScene <= transitionDurationSeconds + 2d)
        {
            transitionsEnabled = false;
            totalTransitionOverlapSeconds = 0d;
            adjustedTotalSceneDuration = narrationDurationSeconds;
            durationPerScene = sceneCount == 0 ? 1d : adjustedTotalSceneDuration / sceneCount;
            expectedCombinedDurationSeconds = durationPerScene * sceneCount;
            _logger.LogWarning(
                "Transitions were enabled but auto-disabled because sceneDurationSeconds ({SceneDurationSeconds:F3}) must be greater than transitionDurationSeconds + 2 ({Threshold:F3}).",
                durationPerScene,
                transitionDurationSeconds + 2d);
        }
        segmentDiagnostics.Add(string.Join(Environment.NewLine, new[]
        {
            $"enableTransitions: {_options.EnableTransitions}",
            $"transitionsEnabled: {transitionsEnabled}",
            $"narrationDurationSeconds: {narrationDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"sceneCount: {sceneCount}",
            $"transitionType: {_options.TransitionType}",
            $"transitionDurationSeconds: {transitionDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"transitionCount: {transitionCount}",
            $"totalTransitionOverlapSeconds: {totalTransitionOverlapSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"adjustedTotalSceneDuration: {adjustedTotalSceneDuration.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"calculatedSceneDurationSeconds: {durationPerScene.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"expectedCombinedDurationSeconds: {expectedCombinedDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        }));
        for (var i = 0; i < plan.Scenes.Count; i++)
        {
            var scene = plan.Scenes[i];
            var duration = durationPerScene > 0 ? durationPerScene : 1d;
            var frameCount = Math.Max(1, (int)Math.Round(duration * 30d, MidpointRounding.AwayFromZero));
            var segmentPath = Path.Combine(outputDirectory, $"segment-{i + 1:000}.mp4");
            if (!File.Exists(scene.VisualPath))
            {
                throw new FileNotFoundException($"Scene image not found for segment {i + 1}.", scene.VisualPath);
            }

            var zoomPanFilter = BuildKenBurnsFilter(i, frameCount);
            var segmentFilter = $"{zoomPanFilter},fade=t=in:st=0:d=0.5,fade=t=out:st=duration-0.5:d=0.5";
            var segmentArguments =
                $"-y -nostdin -loop 1 -i \"{NormalizePath(scene.VisualPath)}\" -vf \"{segmentFilter}\" -frames:v {frameCount} -c:v libx264 -preset ultrafast -pix_fmt yuv420p -r 30 \"{NormalizePath(segmentPath)}\"";
            var segmentCommand = $"{_options.FfmpegPath} {segmentArguments}";
            await _fileSystem.WriteAllTextAsync(commandPath, segmentCommand, cancellationToken);
            var segmentCommandPath = Path.Combine(outputDirectory, $"ffmpeg-segment-{i + 1:000}-command.txt");

            var effectiveSegmentTimeoutSeconds = CalculateEffectiveSegmentTimeoutSeconds(_options.FfmpegSegmentTimeoutSeconds, duration);
            var segmentDurationSeconds = (int)Math.Ceiling(duration);
            var segmentDiagnosticsEntry = string.Join(Environment.NewLine, new[]
            {
                $"Segment #{i + 1} duration: {segmentDurationSeconds} seconds",
                $"Segment #{i + 1} frameCount: {frameCount}",
                $"Configured segment timeout: {_options.FfmpegSegmentTimeoutSeconds} seconds",
                $"Effective segment timeout: {effectiveSegmentTimeoutSeconds} seconds",
                $"Command: {segmentCommand}"
            });
            segmentDiagnostics.Add(segmentDiagnosticsEntry);
            await _fileSystem.WriteAllTextAsync(segmentCommandPath, segmentDiagnosticsEntry, cancellationToken);
            _logger.LogInformation(
                "Segment #{SegmentIndex} duration: {SegmentDurationSeconds} seconds. Configured segment timeout: {ConfiguredTimeoutSeconds} seconds. Effective segment timeout: {EffectiveTimeoutSeconds} seconds",
                i + 1,
                segmentDurationSeconds,
                _options.FfmpegSegmentTimeoutSeconds,
                effectiveSegmentTimeoutSeconds);

            using var segmentTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(effectiveSegmentTimeoutSeconds));
            using var segmentLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, segmentTimeoutCts.Token);
            var segmentResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, segmentArguments, segmentLinkedCts.Token);
            var segmentExists = File.Exists(segmentPath);
            var segmentSize = segmentExists ? new FileInfo(segmentPath).Length : 0L;
            if (segmentResult.ExitCode != 0 || !segmentExists || segmentSize <= 0)
            {
                var timedOut = segmentResult.ExceptionText?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true
                    || (segmentTimeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested);
                var timeoutFailureMessage = $"FFmpeg segment #{i + 1} timed out after {effectiveSegmentTimeoutSeconds} seconds";
                throw new InvalidOperationException(
                    $"{(timedOut ? timeoutFailureMessage : $"FFmpeg segment creation failed for scene #{i + 1}.")}{Environment.NewLine}" +
                    $"Command: {segmentCommand}{Environment.NewLine}" +
                    $"ExitCode: {segmentResult.ExitCode}{Environment.NewLine}" +
                    $"TimedOut: {timedOut}{Environment.NewLine}" +
                    $"Stdout: {segmentResult.StandardOutput}{Environment.NewLine}" +
                    $"Stderr: {segmentResult.StandardError}");
            }

            segmentPaths.Add(segmentPath);
            segmentDurationsSeconds.Add(frameCount / 30d);
        }

        var combinedPath = Path.Combine(outputDirectory, "combined.mp4");
        var concatBody = string.Join(Environment.NewLine, segmentPaths.Select(path => $"file '{NormalizePath(path).Replace("'", "'\\''")}'"));
        await _fileSystem.WriteAllTextAsync(segmentConcatPath, concatBody, cancellationToken);
        var concatArguments = BuildSegmentTransitionArguments(segmentPaths, segmentDurationsSeconds, segmentConcatPath, combinedPath, transitionsEnabled, transitionDurationSeconds);
        await _fileSystem.WriteAllTextAsync(commandPath, $"{_options.FfmpegPath} {concatArguments}", cancellationToken);
        segmentDiagnostics.Add($"xfadeCommand: {_options.FfmpegPath} {concatArguments}");
        _logger.LogInformation("Concatenating FFmpeg segments: {Command}", $"{_options.FfmpegPath} {concatArguments}");
        var concatResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, concatArguments, cancellationToken);
        if (concatResult.ExitCode != 0 || !File.Exists(combinedPath))
        {
            throw new InvalidOperationException("FFmpeg concat of scene segments failed.");
        }
        var combinedDurationSeconds = await ProbeMediaDurationSecondsAsync(combinedPath, cancellationToken);
        var combinedDurationDelta = Math.Abs(combinedDurationSeconds - narrationDurationSeconds);
        segmentDiagnostics.Add($"actualCombinedDurationSeconds: {combinedDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (combinedDurationDelta > 1.5d)
        {
            var warningMessage = $"Combined scene duration ({combinedDurationSeconds:F3}s) differs from narration duration ({narrationDurationSeconds:F3}s) by {combinedDurationDelta:F3}s.";
            _logger.LogWarning("{Warning}", warningMessage);
            throw new InvalidOperationException($"{warningMessage} Refusing final mux to avoid trimmed/missing scenes.");
        }

        var finalArguments =
            $"-y -i \"{NormalizePath(combinedPath)}\" -i \"{NormalizePath(narrationAudioPath)}\" -shortest -c:v copy -c:a aac \"{NormalizePath(outputPath)}\"";
        var finalCommand = $"{_options.FfmpegPath} {finalArguments}";
        await _fileSystem.WriteAllTextAsync(commandPath, finalCommand, cancellationToken);
        await _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "ffmpeg.log"), string.Join($"{Environment.NewLine}{Environment.NewLine}", segmentDiagnostics), cancellationToken);
        _logger.LogInformation("Rendering final FFmpeg output with narration: {Command}", finalCommand);

        var finalResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, finalArguments, cancellationToken);
        if (finalResult.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException("FFmpeg final render with narration failed.");
        }

        return (finalCommand, finalResult);
    }

    private static string BuildKenBurnsFilter(int sceneIndex, int frameCount)
    {
        var effect = sceneIndex % 5;
        return effect switch
        {
            0 => $"zoompan=z='min(zoom+0.0015,1.3)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d={frameCount}:s=1280x720:fps=30",
            1 => $"zoompan=z='min(zoom+0.0015,1.3)':x='iw/2-(iw/zoom/2)-iw*0.08':y='ih/2-(ih/zoom/2)':d={frameCount}:s=1280x720:fps=30",
            2 => $"zoompan=z='min(zoom+0.0015,1.3)':x='iw/2-(iw/zoom/2)+iw*0.08':y='ih/2-(ih/zoom/2)':d={frameCount}:s=1280x720:fps=30",
            3 => $"zoompan=z='if(eq(on,1),1.3,max(zoom-0.0015,1.0))':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d={frameCount}:s=1280x720:fps=30",
            _ => $"zoompan=z='1.12':x='if(gte(x,iw-iw/zoom),0,x+1)':y='ih/2-(ih/zoom/2)':d={frameCount}:s=1280x720:fps=30"
        };
    }

    private async Task<double> ProbeMediaDurationSecondsAsync(string mediaPath, CancellationToken cancellationToken)
    {
        var ffprobePath = ResolveFfprobePath();
        _logger.LogInformation("Using ffprobe path: {FfprobePath}", ffprobePath);

        if (Path.IsPathRooted(ffprobePath) && !File.Exists(ffprobePath))
        {
            _logger.LogWarning("Configured ffprobe path does not exist: {FfprobePath}", ffprobePath);
            return 0d;
        }

        var probeArguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{NormalizePath(mediaPath)}\"";
        var probeResult = await _processRunner.ExecuteAsync(ffprobePath, probeArguments, cancellationToken);
        if (probeResult.ExitCode != 0)
        {
            return 0d;
        }

        return double.TryParse(
            probeResult.StandardOutput.Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var durationSeconds)
            ? Math.Max(0d, durationSeconds)
            : 0d;
    }

    private string ResolveFfprobePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.FfprobePath))
        {
            return _options.FfprobePath;
        }

        if (!string.IsNullOrWhiteSpace(_options.FfmpegPath))
        {
            try
            {
                var ffmpegFileName = Path.GetFileName(_options.FfmpegPath);
                if (string.Equals(ffmpegFileName, "ffmpeg", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ffmpegFileName, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var ffmpegDirectory = Path.GetDirectoryName(_options.FfmpegPath);
                    if (!string.IsNullOrWhiteSpace(ffmpegDirectory))
                    {
                        return Path.Combine(ffmpegDirectory, "ffprobe.exe");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to derive ffprobe path from ffmpeg path '{FfmpegPath}'.", _options.FfmpegPath);
            }
        }

        return "ffprobe";
    }

    public static int CalculateEffectiveSegmentTimeoutSeconds(int configuredSegmentTimeoutSeconds, double sceneDurationSeconds)
    {
        var effectiveSegmentTimeoutSeconds = Math.Max(configuredSegmentTimeoutSeconds, (int)Math.Ceiling(sceneDurationSeconds * 10d));
        effectiveSegmentTimeoutSeconds = Math.Max(effectiveSegmentTimeoutSeconds, 300);
        return effectiveSegmentTimeoutSeconds;
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');

    private string BuildSegmentTransitionArguments(IReadOnlyList<string> segmentPaths, IReadOnlyList<double> segmentDurationsSeconds, string segmentConcatPath, string combinedPath, bool transitionsEnabled, double transitionDurationSeconds)
    {
        if (!transitionsEnabled)
        {
            return $"-y -f concat -safe 0 -i \"{NormalizePath(segmentConcatPath)}\" -c copy \"{NormalizePath(combinedPath)}\"";
        }

        var inputArguments = string.Join(" ", segmentPaths.Select(path => $"-i \"{NormalizePath(path)}\""));
        var filterParts = new List<string>();
        var cumulativeDurationSeconds = segmentDurationsSeconds[0];
        var previousLabel = "[0:v]";
        var transitionType = GetTransitionType();
        for (var i = 1; i < segmentPaths.Count; i++)
        {
            var outputLabel = i == segmentPaths.Count - 1 ? "[vout]" : $"[v{i}]";
            var offset = Math.Max(0d, cumulativeDurationSeconds - transitionDurationSeconds);
            filterParts.Add($"{previousLabel}[{i}:v]xfade=transition={transitionType}:duration={transitionDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}:offset={offset.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}{outputLabel}");
            previousLabel = outputLabel;
            cumulativeDurationSeconds += segmentDurationsSeconds[i] - transitionDurationSeconds;
        }

        var filterComplex = string.Join(";", filterParts);
        return $"-y {inputArguments} -filter_complex \"{filterComplex}\" -map \"[vout]\" -pix_fmt yuv420p -c:v libx264 -preset ultrafast \"{NormalizePath(combinedPath)}\"";
    }

    private bool IsXfadeEnabled(int sceneCount, double transitionDurationSeconds)
        => sceneCount > 1 && _options.EnableTransitions && transitionDurationSeconds > 0d;

    private double GetTransitionDurationSeconds()
        => _options.TransitionDurationSeconds > 0d
            ? _options.TransitionDurationSeconds
            : 0d;

    private string GetTransitionType()
        => string.IsNullOrWhiteSpace(_options.TransitionType) ? "fade" : _options.TransitionType.Trim();
    private static List<string> FindMissingAssets(RenderManifest manifest, RenderPlan plan)
    {
        var missingAssets = new List<string>();

        var hasSceneAudio = plan.Scenes.Any(scene => !string.IsNullOrWhiteSpace(scene.AudioPath) && File.Exists(scene.AudioPath));
        if (!hasSceneAudio && (string.IsNullOrWhiteSpace(manifest.AudioPath) || !File.Exists(manifest.AudioPath)))
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
            $"TimedOut: {result.TimedOut}",
            string.IsNullOrWhiteSpace(result.ExceptionText) ? string.Empty : $"Exception: {result.ExceptionText}",
            "--- STDERR ---",
            result.StandardError,
            "--- STDOUT ---",
            result.StandardOutput
        }.Where(static line => !string.IsNullOrEmpty(line)));
    }
}
