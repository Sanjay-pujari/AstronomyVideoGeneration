using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;
using System.Text.Json;

namespace Astronomy.MediaFactory.Rendering;

public sealed class FfmpegVideoRenderService : IVideoRenderService
{
    private readonly RenderingOptions _options;
    private readonly RenderManifestBuilder _manifestBuilder;
    private readonly FfmpegArgumentBuilder _argumentBuilder;
    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<FfmpegVideoRenderService> _logger;
    private const int LongOutputWidth = 1920;
    private const int LongOutputHeight = 1080;
    private const int ShortOutputWidth = 1080;
    private const int ShortOutputHeight = 1920;

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
        if (hasSegmentedAudio)
        {
            await RenderFromSegmentsAsync(manifest, plan, outputDirectory, segmentConcatPath, cancellationToken);
            await WriteShortDiagnosticsIfNeededAsync(manifest, outputPath, outputDirectory, cancellationToken);
            return outputPath;
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, _options.FfmpegTimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var (command, finalResult) = await RenderFromImageSegmentsAsync(manifest, plan, manifest.AudioPath, outputPath, outputDirectory, segmentConcatPath, commandPath, linkedCts.Token);
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

        await WriteShortDiagnosticsIfNeededAsync(manifest, outputPath, outputDirectory, cancellationToken);
        return outputPath;
    }

    private async Task RenderFromSegmentsAsync(RenderManifest manifest, RenderPlan plan, string outputDirectory, string segmentConcatPath, CancellationToken cancellationToken)
    {
        var segmentClipPaths = new List<string>();
        var speechDiagnostics = new List<SpeechSpeedDiagnostic>();
        var syncReports = new List<SegmentSyncReportEntry>();
        for (var i = 0; i < plan.Scenes.Count; i++)
        {
            var scene = plan.Scenes[i];
            var sceneAudioPath = scene.AudioPath;
            if (string.IsNullOrWhiteSpace(sceneAudioPath) || !File.Exists(sceneAudioPath))
            {
                throw new InvalidOperationException($"Segmented narration requires audio for every scene; missing audio for scene #{i + 1} ({scene.SceneId ?? scene.Caption}).");
            }

            var segmentOutputPath = Path.Combine(outputDirectory, $"segment-{i + 1:000}.mp4");
            var audioDurationSeconds = await ProbeMediaDurationSecondsAsync(sceneAudioPath, cancellationToken);
            if (audioDurationSeconds <= 0)
            {
                audioDurationSeconds = Math.Max(1d, scene.DurationSeconds);
                _logger.LogWarning("Could not determine audio duration for scene #{SceneIndex}; using manifest duration {DurationSeconds:F3}s.", i + 1, audioDurationSeconds);
            }

            var (outputWidth, outputHeight) = GetOutputSize(manifest);
            var fps = IsShortManifest(manifest) ? GetShortSafeFps() : Math.Max(1, _options.FrameRate);
            var segmentFilter = IsShortManifest(manifest) ? BuildExactOutputFilter(outputWidth, outputHeight) : $"scale={outputWidth}:{outputHeight}";
            var lockedDurationSeconds = audioDurationSeconds;
            var durationArgument = lockedDurationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var segmentArguments = $"-y -loop 1 -i \"{NormalizePath(scene.VisualPath)}\" -i \"{NormalizePath(sceneAudioPath)}\" -vf \"{segmentFilter}\" -t {durationArgument} -c:v libx264 -preset ultrafast -pix_fmt yuv420p -r {fps} -c:a aac -f mp4 \"{NormalizePath(segmentOutputPath)}\"";
            speechDiagnostics.Add(CreateSpeechSpeedDiagnostic(scene, i, audioDurationSeconds, lockedDurationSeconds));
            var segmentResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, segmentArguments, cancellationToken);
            if (segmentResult.ExitCode != 0 || !File.Exists(segmentOutputPath))
            {
                throw new InvalidOperationException($"FFmpeg segmented clip generation failed for scene #{i + 1}.");
            }

            var visualDurationSeconds = await ProbeMediaDurationSecondsAsync(segmentOutputPath, cancellationToken);
            if (visualDurationSeconds <= 0)
            {
                visualDurationSeconds = lockedDurationSeconds;
            }

            ValidateSegmentSynchronization(scene, i, audioDurationSeconds, visualDurationSeconds);
            syncReports.Add(CreateSegmentSyncReportEntry(scene, i, audioDurationSeconds, visualDurationSeconds));
            segmentClipPaths.Add(segmentOutputPath);
        }

        if (segmentClipPaths.Count == 0)
        {
            throw new InvalidOperationException("Segmented narration flow was requested but no segment clips were produced.");
        }

        var concatBody = string.Join(Environment.NewLine, segmentClipPaths.Select(path => $"file '{path.Replace("'", "'\\''")}'"));
        await _fileSystem.WriteAllTextAsync(segmentConcatPath, concatBody, cancellationToken);
        await WriteSpeechSpeedDiagnosticsAsync(outputDirectory, speechDiagnostics, cancellationToken);
        await WriteSegmentSyncReportAsync(outputDirectory, syncReports, cancellationToken);

        var concatArguments = IsShortManifest(manifest)
            ? $"-y -f concat -safe 0 -i \"{NormalizePath(segmentConcatPath)}\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -r {GetShortSafeFps()} -c:a aac -f mp4 \"{NormalizePath(manifest.OutputPath)}\""
            : $"-y -f concat -safe 0 -i \"{NormalizePath(segmentConcatPath)}\" -c copy \"{NormalizePath(manifest.OutputPath)}\"";
        var concatResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, concatArguments, cancellationToken);
        if (concatResult.ExitCode != 0 || !File.Exists(manifest.OutputPath))
        {
            throw new InvalidOperationException("FFmpeg concat of segmented clips failed.");
        }
    }


    private async Task<(string Command, ProcessExecutionResult FinalResult)> RenderFromImageSegmentsAsync(
        RenderManifest manifest,
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
        var motionDiagnostics = new List<string>();
        var speechDiagnostics = new List<SpeechSpeedDiagnostic>();
        var syncReports = new List<SegmentSyncReportEntry>();
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
            var fps = IsShortManifest(manifest) ? GetShortSafeFps() : Math.Max(1, _options.KenBurnsFps > 0 ? _options.KenBurnsFps : _options.FrameRate);
            var frameCount = Math.Max(1, (int)Math.Round(duration * fps, MidpointRounding.AwayFromZero));
            var segmentPath = Path.Combine(outputDirectory, $"segment-{i + 1:000}.mp4");
            if (!File.Exists(scene.VisualPath))
            {
                throw new FileNotFoundException($"Scene image not found for segment {i + 1}.", scene.VisualPath);
            }

            var (outputWidth, outputHeight) = GetOutputSize(manifest);
            var motionProfile = ResolveMotionProfile(scene);
            var zoomPanFilter = BuildKenBurnsFilter(duration, fps, outputWidth, outputHeight, motionProfile);
            const double fadeDurationSeconds = 0.5d;
            var fadeOutStartSeconds = Math.Max(0d, duration - fadeDurationSeconds);
            var roundedFadeOutStartSeconds = Math.Round(fadeOutStartSeconds, 3, MidpointRounding.AwayFromZero);
            var formattedFadeOutStartSeconds = roundedFadeOutStartSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var formattedFadeDurationSeconds = fadeDurationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var exactOutputFilter = IsShortManifest(manifest) ? BuildExactOutputFilter(outputWidth, outputHeight) : $"scale={outputWidth}:{outputHeight}:flags=lanczos";
            var segmentFilter =
                _options.EnableKenBurns
                    ? $"fps={fps},{exactOutputFilter},{zoomPanFilter},fade=t=in:st=0:d={formattedFadeDurationSeconds},fade=t=out:st={formattedFadeOutStartSeconds}:d={formattedFadeDurationSeconds}"
                    : $"{exactOutputFilter},fade=t=in:st=0:d={formattedFadeDurationSeconds},fade=t=out:st={formattedFadeOutStartSeconds}:d={formattedFadeDurationSeconds}";
            var segmentArguments =
                $"-y -nostdin -loop 1 -i \"{NormalizePath(scene.VisualPath)}\" -vf \"{segmentFilter}\" -frames:v {frameCount} -c:v libx264 -preset ultrafast -pix_fmt yuv420p -r {fps} -f mp4 \"{NormalizePath(segmentPath)}\"";
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
            motionDiagnostics.Add(string.Join(Environment.NewLine, new[]
            {
                "{",
                $"  \"sceneId\": \"scene-{i + 1:000}\",",
                $"  \"imagePath\": \"{EscapeForJson(scene.VisualPath)}\",",
                $"  \"durationSeconds\": {duration.ToString(System.Globalization.CultureInfo.InvariantCulture)},",
                $"  \"fps\": {fps},",
                $"  \"totalFrames\": {frameCount},",
                $"  \"zoomStart\": {_options.KenBurnsZoomStart.ToString(System.Globalization.CultureInfo.InvariantCulture)},",
                $"  \"zoomEnd\": {_options.KenBurnsZoomEnd.ToString(System.Globalization.CultureInfo.InvariantCulture)},",
                $"  \"enableDirectionalMotion\": {(_options.EnableDirectionalMotion ? "true" : "false")},",
                $"  \"panStrength\": {motionProfile.PanStrength.ToString(System.Globalization.CultureInfo.InvariantCulture)},",
                $"  \"panDirection\": \"{motionProfile.PanDirection}\",",
                $"  \"outputResolution\": \"{outputWidth}x{outputHeight}\",",
                $"  \"isShort\": {(outputHeight > outputWidth ? "true" : "false")}",
                "}"
            }));
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

            var finalSegmentDurationSeconds = frameCount / (double)fps;
            segmentPaths.Add(segmentPath);
            segmentDurationsSeconds.Add(finalSegmentDurationSeconds);
            speechDiagnostics.Add(CreateSpeechSpeedDiagnostic(scene, i, duration, finalSegmentDurationSeconds));
            ValidateSegmentSynchronization(scene, i, duration, finalSegmentDurationSeconds);
            syncReports.Add(CreateSegmentSyncReportEntry(scene, i, duration, finalSegmentDurationSeconds));
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

        var finalArguments = IsShortManifest(manifest)
            ? $"-y -i \"{NormalizePath(combinedPath)}\" -i \"{NormalizePath(narrationAudioPath)}\" -map 0:v:0 -map 1:a:0 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -r {GetShortSafeFps()} -c:a aac -f mp4 \"{NormalizePath(outputPath)}\""
            : $"-y -i \"{NormalizePath(combinedPath)}\" -i \"{NormalizePath(narrationAudioPath)}\" -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac \"{NormalizePath(outputPath)}\"";
        var finalCommand = $"{_options.FfmpegPath} {finalArguments}";
        await _fileSystem.WriteAllTextAsync(commandPath, finalCommand, cancellationToken);
        await _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "ffmpeg.log"), string.Join($"{Environment.NewLine}{Environment.NewLine}", segmentDiagnostics), cancellationToken);
        await WriteSpeechSpeedDiagnosticsAsync(outputDirectory, speechDiagnostics, cancellationToken);
        await WriteSegmentSyncReportAsync(outputDirectory, syncReports, cancellationToken);
        await _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "video-motion-settings.json"), $"[{Environment.NewLine}{string.Join($",{Environment.NewLine}", motionDiagnostics)}{Environment.NewLine}]", cancellationToken);
        await _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-motion-settings.json"), $"[{Environment.NewLine}{string.Join($",{Environment.NewLine}", motionDiagnostics)}{Environment.NewLine}]", cancellationToken);
        _logger.LogInformation("Rendering final FFmpeg output with narration: {Command}", finalCommand);

        var finalResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, finalArguments, cancellationToken);
        if (finalResult.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException("FFmpeg final render with narration failed.");
        }

        return (finalCommand, finalResult);
    }


    private Task WriteSegmentSyncReportAsync(string outputDirectory, IReadOnlyCollection<SegmentSyncReportEntry> diagnostics, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "segment-sync-report.json"), json, cancellationToken);
    }

    private static void ValidateSegmentSynchronization(RenderPlanScene scene, int index, double audioDurationSeconds, double visualDurationSeconds)
    {
        var difference = Math.Abs(audioDurationSeconds - visualDurationSeconds);
        if (difference > 0.25d)
        {
            throw new InvalidOperationException($"Segment synchronization failed for scene #{index + 1} ({scene.SceneId ?? scene.Caption}): audio={audioDurationSeconds:F3}s visual={visualDurationSeconds:F3}s difference={difference:F3}s.");
        }
    }

    private static SegmentSyncReportEntry CreateSegmentSyncReportEntry(RenderPlanScene scene, int index, double audioDurationSeconds, double visualDurationSeconds)
    {
        var narrationText = string.IsNullOrWhiteSpace(scene.NarrationText) ? scene.Caption : scene.NarrationText!;
        var wordCount = CountWords(narrationText);
        var language = string.IsNullOrWhiteSpace(scene.NarrationLanguage) ? InferLanguage(narrationText) : scene.NarrationLanguage!;
        var difference = Math.Abs(audioDurationSeconds - visualDurationSeconds);
        return new SegmentSyncReportEntry(
            SceneId: string.IsNullOrWhiteSpace(scene.SceneId) ? $"scene-{index + 1:000}" : scene.SceneId!,
            SceneType: string.IsNullOrWhiteSpace(scene.SceneType) ? scene.Segment : scene.SceneType!,
            NarrationLanguage: language,
            AudioDurationSeconds: Math.Round(audioDurationSeconds, 3, MidpointRounding.AwayFromZero),
            VisualDurationSeconds: Math.Round(visualDurationSeconds, 3, MidpointRounding.AwayFromZero),
            DurationDifference: Math.Round(difference, 3, MidpointRounding.AwayFromZero),
            SynchronizationStatus: difference <= 0.25d ? "Synchronized" : "Mismatch",
            NarrationWords: wordCount,
            EstimatedWordsPerMinute: Math.Round(CalculateWordsPerMinute(narrationText, audioDurationSeconds), 1, MidpointRounding.AwayFromZero),
            ObjectName: scene.ObjectName ?? string.Empty,
            SegmentIndex: index + 1);
    }

    private static int CountWords(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private Task WriteSpeechSpeedDiagnosticsAsync(string outputDirectory, IReadOnlyCollection<SpeechSpeedDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "speech-speed-diagnostics.json"), json, cancellationToken);
    }

    private static SpeechSpeedDiagnostic CreateSpeechSpeedDiagnostic(RenderPlanScene scene, int index, double audioDurationSeconds, double finalSegmentDurationSeconds)
    {
        var language = InferLanguage(scene.Caption);
        var wordsPerMinute = CalculateWordsPerMinute(scene.Caption, audioDurationSeconds);
        var warnings = new List<string>();
        if ((string.Equals(language, "Hindi", StringComparison.OrdinalIgnoreCase) && wordsPerMinute > 180d)
            || (string.Equals(language, "English", StringComparison.OrdinalIgnoreCase) && wordsPerMinute > 170d))
        {
            warnings.Add("Narration may be too fast.");
        }

        return new SpeechSpeedDiagnostic(
            SceneId: $"scene-{index + 1:000}",
            Language: language,
            SsmlProsodyRate: "medium",
            TextLength: scene.Caption.Length,
            AudioDurationSeconds: Math.Round(audioDurationSeconds, 3, MidpointRounding.AwayFromZero),
            CalculatedWordsPerMinute: Math.Round(wordsPerMinute, 1, MidpointRounding.AwayFromZero),
            TempoApplied: false,
            TempoFactor: 1.0d,
            FinalSegmentDurationSeconds: Math.Round(finalSegmentDurationSeconds, 3, MidpointRounding.AwayFromZero),
            Warnings: warnings);
    }

    private static string InferLanguage(string text)
        => text.Any(character => character >= '\u0900' && character <= '\u097F') ? "Hindi" : "English";

    private static double CalculateWordsPerMinute(string text, double durationSeconds)
    {
        if (durationSeconds <= 0)
        {
            return 0d;
        }

        var wordCount = CountWords(text);
        return wordCount * 60d / durationSeconds;
    }

    private string BuildKenBurnsFilter(double durationSeconds, int fps, int outputWidth, int outputHeight, MotionProfile motionProfile)
    {
        var zoomStart = Math.Max(1d, motionProfile.ZoomStart);
        var zoomEnd = Math.Max(zoomStart, motionProfile.ZoomEnd);
        var totalFrames = Math.Max(1, (int)Math.Round(durationSeconds * fps, MidpointRounding.AwayFromZero));
        var zoomStartText = zoomStart.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var zoomEndText = zoomEnd.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var zoomDeltaText = (zoomEnd - zoomStart).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        var zoomExpression = _options.KenBurnsUseEasing
            ? $"{zoomStartText} + ({zoomDeltaText})*pow(on/{totalFrames}.0,1.2)"
            : $"{zoomStartText} + ({zoomDeltaText})*(on/{totalFrames}.0)";
        var panOffsetExpression = $"{motionProfile.PanDirectionSign.ToString(System.Globalization.CultureInfo.InvariantCulture)}*{motionProfile.PanStrength.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}*iw*(on/{totalFrames}.0)";
        var xExpression = $"iw/2-(iw/zoom/2)+({panOffsetExpression})";
        return $"zoompan=z='{zoomExpression}':x='{xExpression}':y='ih/2-(ih/zoom/2)':d={totalFrames}:s={outputWidth}x{outputHeight}";
    }

    private MotionProfile ResolveMotionProfile(RenderPlanScene scene)
    {
        var isOverview = string.Equals(scene.Segment, "intro", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scene.Segment, "outro", StringComparison.OrdinalIgnoreCase);
        var isPlanetary = string.Equals(scene.Segment, "main", StringComparison.OrdinalIgnoreCase);

        if (!_options.EnableDirectionalMotion)
        {
            return new MotionProfile(_options.KenBurnsZoomStart, _options.KenBurnsZoomEnd, 0d, 0d, "none");
        }

        var zoomStart = _options.KenBurnsZoomStart;
        var zoomEnd = _options.KenBurnsZoomEnd;
        if (isOverview)
        {
            zoomEnd = Math.Max(1.0d, zoomStart + (zoomEnd - zoomStart) * 0.25d);
        }
        else if (isPlanetary)
        {
            zoomEnd = Math.Max(zoomEnd, zoomStart + 0.12d);
        }

        return new MotionProfile(zoomStart, zoomEnd, Math.Clamp(_options.DirectionalPanStrength, 0d, 0.08d), 0d, "none");
    }
    private sealed record SpeechSpeedDiagnostic(
        string SceneId,
        string Language,
        string SsmlProsodyRate,
        int TextLength,
        double AudioDurationSeconds,
        double CalculatedWordsPerMinute,
        bool TempoApplied,
        double TempoFactor,
        double FinalSegmentDurationSeconds,
        IReadOnlyList<string> Warnings);

    private sealed record MotionProfile(double ZoomStart, double ZoomEnd, double PanStrength, double PanDirectionSign, string PanDirection);

    private sealed record SegmentSyncReportEntry(
        string SceneId,
        string SceneType,
        string NarrationLanguage,
        double AudioDurationSeconds,
        double VisualDurationSeconds,
        double DurationDifference,
        string SynchronizationStatus,
        int NarrationWords,
        double EstimatedWordsPerMinute,
        string ObjectName,
        int SegmentIndex);
    private static string EscapeForJson(string? value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static (int Width, int Height) GetOutputSize(RenderManifest manifest)
        => IsShortManifest(manifest) ? (ShortOutputWidth, ShortOutputHeight) : (LongOutputWidth, LongOutputHeight);

    private static bool IsShortManifest(RenderManifest manifest)
        => manifest.OutputHeight.GetValueOrDefault() > manifest.OutputWidth.GetValueOrDefault();

    private static string BuildExactOutputFilter(int outputWidth, int outputHeight)
        => $"scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=increase,crop={outputWidth}:{outputHeight},pad={outputWidth}:{outputHeight}:(ow-iw)/2:(oh-ih)/2,setsar=1";

    private static int GetShortSafeFps()
        => 30;

    private async Task WriteShortDiagnosticsIfNeededAsync(RenderManifest manifest, string outputPath, string outputDirectory, CancellationToken cancellationToken)
    {
        if (!IsShortManifest(manifest))
        {
            return;
        }

        var diagnostics = await YouTubeShortsValidation.ProbeAndWriteDiagnosticsAsync(outputPath, outputDirectory, ResolveFfprobePath(), cancellationToken);
        _logger.LogInformation("Short video diagnostics complete for {OutputPath}. isValidYouTubeShort={IsValidYouTubeShort}", outputPath, diagnostics.IsValidYouTubeShort);
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
