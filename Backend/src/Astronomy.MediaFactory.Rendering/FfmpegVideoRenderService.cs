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
    private const string EncodingReportFileName = "video-encoding-report.json";
    private const string RenderPerformanceReportFileName = "video-render-performance-report.json";
    private static readonly JsonSerializerOptions DiagnosticJsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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
        await WriteShortsRenderManifestFinalAsync(manifest, plan, outputDirectory, cancellationToken);

        var hasSegmentedAudio = plan.Scenes.Any(s => !string.IsNullOrWhiteSpace(s.AudioPath));
        if (!hasSegmentedAudio)
        {
            var missingAssets = FindMissingAssets(manifest, plan);
            if (missingAssets.Count > 0)
            {
                var validationError = BuildMissingAssetMessage(missingAssets);
                _logger.LogWarning("Skipping FFmpeg render because input validation failed: {Reason}", validationError);
                await _fileSystem.WriteAllTextAsync(ffmpegLogPath, validationError, cancellationToken);
                throw new InvalidOperationException($"Video render input validation failed: {validationError}");
            }
        }
        if (hasSegmentedAudio)
        {
            await RenderFromSegmentsAsync(manifest, plan, outputDirectory, segmentConcatPath, cancellationToken);
            await WriteEncodingReportAsync(manifest, outputPath, outputDirectory, cancellationToken);
            await WriteShortDiagnosticsIfNeededAsync(manifest, outputPath, outputDirectory, cancellationToken);
            return outputPath;
        }

        try
        {
            var (command, finalResult, finalDiagnostics) = await RenderFromImageSegmentsAsync(manifest, plan, manifest.AudioPath, outputPath, outputDirectory, segmentConcatPath, commandPath, cancellationToken);
            await _fileSystem.WriteAllTextAsync(commandPath, command, cancellationToken);
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, $"{BuildProcessDiagnostics(finalResult)}{Environment.NewLine}{finalDiagnostics}", cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Final long render timed out", StringComparison.Ordinal))
        {
            _logger.LogWarning(ex, "FFmpeg final long render timed out.");
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, ex.Message, cancellationToken);
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "FFmpeg render timed out.");
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, ex.ToString(), cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FFmpeg execution failed.");
            await _fileSystem.WriteAllTextAsync(ffmpegLogPath, ex.ToString(), cancellationToken);
            throw new InvalidOperationException($"FFmpeg execution failed. See {ffmpegLogPath} for details.", ex);
        }

        await WriteEncodingReportAsync(manifest, outputPath, outputDirectory, cancellationToken);
        await WriteShortDiagnosticsIfNeededAsync(manifest, outputPath, outputDirectory, cancellationToken);
        return outputPath;
    }

    private async Task RenderFromSegmentsAsync(RenderManifest manifest, RenderPlan plan, string outputDirectory, string segmentConcatPath, CancellationToken cancellationToken)
    {
        var segmentClipPaths = new List<string>();
        var speechDiagnostics = new List<SpeechSpeedDiagnostic>();
        var syncReports = new List<SegmentSyncReportEntry>();
        var effectsReports = new List<VideoEffectsReportEntry>();
        var performanceReports = new List<SegmentRenderPerformanceReportEntry>();
        for (var i = 0; i < plan.Scenes.Count; i++)
        {
            var scene = plan.Scenes[i];
            var sceneAudioPath = scene.AudioPath;
            var segmentOutputPath = Path.Combine(outputDirectory, $"segment-{i + 1:000}.mp4");
            var (outputWidth, outputHeight) = GetOutputSize(manifest);
            var validationErrors = ValidateSegmentInputs(scene, i, outputDirectory, segmentOutputPath, outputWidth, outputHeight, requireAudio: true);
            if (validationErrors.Count > 0)
            {
                var message = $"Scene #{i + 1} validation failed: {string.Join("; ", validationErrors)}";
                await WriteSegmentValidationFailureDiagnosticsAsync(scene, i, segmentOutputPath, outputDirectory, outputWidth, outputHeight, message, scene.DurationSeconds, cancellationToken);
                throw new InvalidOperationException(message);
            }

            var audioDurationSeconds = await ProbeMediaDurationSecondsAsync(sceneAudioPath!, cancellationToken);
            if (audioDurationSeconds <= 0)
            {
                var message = $"Scene #{i + 1} validation failed: duration must be greater than 0; actual {audioDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                await WriteSegmentValidationFailureDiagnosticsAsync(scene, i, segmentOutputPath, outputDirectory, outputWidth, outputHeight, message, audioDurationSeconds, cancellationToken);
                throw new InvalidOperationException(message);
            }

            var isShort = IsShortManifest(manifest);
            var fps = isShort ? GetShortSafeFps() : Math.Max(1, _options.KenBurnsFps > 0 ? _options.KenBurnsFps : _options.FrameRate);
            var lockedDurationSeconds = audioDurationSeconds;
            var motionProfile = ResolveMotionProfile(scene, isShort);
            var effects = ResolveVideoEffects(lockedDurationSeconds, fps, outputWidth, outputHeight, isShort, motionProfile);
            var segmentPreset = ResolveIntermediateEncodingPreset(outputWidth, outputHeight);
            var segmentFilter = BuildSegmentVisualFilter(outputWidth, outputHeight, fps, effects, motionProfile, segmentPreset.ScaleFlags);
            var durationArgument = lockedDurationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var segmentArguments = $"-y -loop 1 -i {QuoteFfmpegPath(scene.VisualPath)} -i {QuoteFfmpegPath(sceneAudioPath!)} -vf \"{segmentFilter}\" -t {durationArgument} {BuildVideoEncodeArguments(segmentPreset)} -r {fps} -c:a aac -b:a {segmentPreset.AudioBitrate} -f mp4 {QuoteFfmpegPath(segmentOutputPath)}";
            speechDiagnostics.Add(CreateSpeechSpeedDiagnostic(scene, i, audioDurationSeconds, lockedDurationSeconds));
            ProcessExecutionResult segmentResult;
            try
            {
                var timeout = TimeSpan.FromSeconds(CalculateEffectiveSegmentTimeoutSeconds(_options.SegmentRenderTimeoutSeconds, lockedDurationSeconds));
                segmentResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, segmentArguments, cancellationToken, timeout);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                segmentResult = new ProcessExecutionResult(-1, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, _options.FfmpegPath, segmentArguments, "FFmpeg segment render was canceled by timeout.", true);
            }
            catch (Exception ex)
            {
                segmentResult = new ProcessExecutionResult(-1, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, _options.FfmpegPath, segmentArguments, ex.ToString(), false);
            }

            AddSegmentRenderPerformanceReport(performanceReports, scene, i, lockedDurationSeconds, segmentPreset.Name, segmentArguments, segmentResult);

            if (segmentResult.ExitCode != 0 || !File.Exists(segmentOutputPath))
            {
                await WriteSegmentFailureDiagnosticsAsync(scene, i, segmentOutputPath, outputDirectory, lockedDurationSeconds, segmentArguments, segmentResult, cancellationToken);
                var stderr = string.IsNullOrWhiteSpace(segmentResult.StandardError) ? segmentResult.ExceptionText : segmentResult.StandardError;
                var message = $"FFmpeg segmented clip generation failed for scene #{i + 1}. See {Path.Combine(outputDirectory, $"render-segment-failure-scene-{i}.json")} for details. FFmpeg stderr: {stderr}";
                throw new InvalidOperationException(message);
            }

            var visualDurationSeconds = await ProbeMediaDurationSecondsAsync(segmentOutputPath, cancellationToken);
            if (visualDurationSeconds <= 0)
            {
                visualDurationSeconds = lockedDurationSeconds;
            }

            ValidateSegmentSynchronization(scene, i, audioDurationSeconds, visualDurationSeconds);
            syncReports.Add(CreateSegmentSyncReportEntry(scene, i, audioDurationSeconds, visualDurationSeconds));
            effectsReports.Add(CreateVideoEffectsReportEntry(scene, i, isShort, effects, audioDurationSeconds, visualDurationSeconds));
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
        await WriteVideoEffectsReportAsync(outputDirectory, effectsReports, cancellationToken);
        await WriteRenderPerformanceReportAsync(outputDirectory, performanceReports, cancellationToken);

        var combinedPath = Path.Combine(outputDirectory, "combined.mp4");
        var concatArguments = $"-y -f concat -safe 0 -i \"{NormalizePath(segmentConcatPath)}\" -c copy \"{NormalizePath(combinedPath)}\"";
        var concatResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, concatArguments, cancellationToken);
        if (concatResult.ExitCode != 0 || !File.Exists(combinedPath))
        {
            throw new InvalidOperationException("FFmpeg concat of segmented clips failed.");
        }

        var productionPreset = ResolveFinalEncodingPreset(manifest);
        var finalFilter = BuildFinalOutputFilter(productionPreset, IsShortManifest(manifest) || manifest.EnableVerticalCrop);
        var finalArguments = $"-y -i \"{NormalizePath(combinedPath)}\" -vf \"{finalFilter}\" {BuildVideoEncodeArguments(productionPreset)} -r {(IsShortManifest(manifest) ? GetShortSafeFps() : Math.Max(1, _options.FrameRate))} -c:a aac -b:a {productionPreset.AudioBitrate} -movflags +faststart -f mp4 \"{NormalizePath(manifest.OutputPath)}\"";
        var inputDurationSeconds = await ProbeMediaDurationSecondsAsync(combinedPath, cancellationToken);
        var timeoutSeconds = CalculateEffectiveFinalRenderTimeoutSeconds(manifest.EncodingProfile, IsShortManifest(manifest), GetConfiguredFinalRenderTimeoutSeconds(manifest.EncodingProfile, IsShortManifest(manifest)), inputDurationSeconds);
        var finalResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, finalArguments, cancellationToken, TimeSpan.FromSeconds(timeoutSeconds));
        var finalDiagnostics = BuildFinalRenderDiagnostics(inputDurationSeconds, finalResult, productionPreset.Name, timeoutSeconds);
        LogFinalRenderDiagnostics(finalDiagnostics);
        await _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "ffmpeg.log"), $"{BuildProcessDiagnostics(finalResult)}{Environment.NewLine}{finalDiagnostics}", cancellationToken);
        if (finalResult.TimedOut)
        {
            throw new InvalidOperationException($"Final long render timed out after {timeoutSeconds} seconds. Increase FinalLongRenderTimeoutSeconds or use faster preset.");
        }
        if (finalResult.ExitCode != 0 || !File.Exists(manifest.OutputPath))
        {
            throw new InvalidOperationException("FFmpeg final render of segmented clips failed.");
        }
    }


    private async Task<(string Command, ProcessExecutionResult FinalResult, string FinalDiagnostics)> RenderFromImageSegmentsAsync(
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
        var effectsReports = new List<VideoEffectsReportEntry>();
        var performanceReports = new List<SegmentRenderPerformanceReportEntry>();
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
            var isShort = IsShortManifest(manifest);
            var motionProfile = ResolveMotionProfile(scene, isShort);
            var effects = ResolveVideoEffects(duration, fps, outputWidth, outputHeight, isShort, motionProfile);
            var segmentPreset = ResolveIntermediateEncodingPreset(outputWidth, outputHeight);
            var segmentFilter = BuildSegmentVisualFilter(outputWidth, outputHeight, fps, effects, motionProfile, segmentPreset.ScaleFlags);
            var segmentArguments =
                $"-y -nostdin -loop 1 -i \"{NormalizePath(scene.VisualPath)}\" -vf \"{segmentFilter}\" -frames:v {frameCount} {BuildVideoEncodeArguments(segmentPreset)} -r {fps} -f mp4 \"{NormalizePath(segmentPath)}\"";
            var segmentCommand = $"{_options.FfmpegPath} {segmentArguments}";
            await _fileSystem.WriteAllTextAsync(commandPath, segmentCommand, cancellationToken);
            var segmentCommandPath = Path.Combine(outputDirectory, $"ffmpeg-segment-{i + 1:000}-command.txt");

            var effectiveSegmentTimeoutSeconds = CalculateEffectiveSegmentTimeoutSeconds(_options.SegmentRenderTimeoutSeconds, duration);
            var segmentDurationSeconds = (int)Math.Ceiling(duration);
            var segmentDiagnosticsEntry = string.Join(Environment.NewLine, new[]
            {
                $"Segment #{i + 1} duration: {segmentDurationSeconds} seconds",
                $"Segment #{i + 1} frameCount: {frameCount}",
                $"Configured segment timeout: {_options.SegmentRenderTimeoutSeconds} seconds",
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
                $"  \"zoomStart\": {effects.ZoomStart.ToString(System.Globalization.CultureInfo.InvariantCulture)},",
                $"  \"zoomEnd\": {effects.ZoomEnd.ToString(System.Globalization.CultureInfo.InvariantCulture)},",
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
                _options.SegmentRenderTimeoutSeconds,
                effectiveSegmentTimeoutSeconds);

            var segmentResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, segmentArguments, cancellationToken, TimeSpan.FromSeconds(effectiveSegmentTimeoutSeconds));
            AddSegmentRenderPerformanceReport(performanceReports, scene, i, duration, segmentPreset.Name, segmentArguments, segmentResult);
            var segmentExists = File.Exists(segmentPath);
            var segmentSize = segmentExists ? new FileInfo(segmentPath).Length : 0L;
            if (segmentResult.ExitCode != 0 || !segmentExists || segmentSize <= 0)
            {
                var timedOut = segmentResult.TimedOut || segmentResult.ExceptionText?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true;
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
            effectsReports.Add(CreateVideoEffectsReportEntry(scene, i, isShort, effects, duration, finalSegmentDurationSeconds));
        }

        var combinedPath = Path.Combine(outputDirectory, "combined.mp4");
        var concatBody = string.Join(Environment.NewLine, segmentPaths.Select(path => $"file '{NormalizePath(path).Replace("'", "'\\''")}'"));
        await _fileSystem.WriteAllTextAsync(segmentConcatPath, concatBody, cancellationToken);
        var (combinedWidth, combinedHeight) = GetOutputSize(manifest);
        var concatArguments = BuildSegmentTransitionArguments(segmentPaths, segmentDurationsSeconds, segmentConcatPath, combinedPath, transitionsEnabled, transitionDurationSeconds, ResolveIntermediateEncodingPreset(combinedWidth, combinedHeight));
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

        var finalPreset = ResolveFinalEncodingPreset(manifest);
        var finalFilter = BuildFinalOutputFilter(finalPreset, IsShortManifest(manifest) || manifest.EnableVerticalCrop);
        var finalArguments = $"-y -i \"{NormalizePath(combinedPath)}\" -i \"{NormalizePath(narrationAudioPath)}\" -map 0:v:0 -map 1:a:0 -vf \"{finalFilter}\" {BuildVideoEncodeArguments(finalPreset)} -r {(IsShortManifest(manifest) ? GetShortSafeFps() : Math.Max(1, _options.FrameRate))} -c:a aac -b:a {finalPreset.AudioBitrate} -movflags +faststart -f mp4 \"{NormalizePath(outputPath)}\"";
        var finalCommand = $"{_options.FfmpegPath} {finalArguments}";
        await _fileSystem.WriteAllTextAsync(commandPath, finalCommand, cancellationToken);
        await _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "ffmpeg.log"), string.Join($"{Environment.NewLine}{Environment.NewLine}", segmentDiagnostics), cancellationToken);
        await WriteSpeechSpeedDiagnosticsAsync(outputDirectory, speechDiagnostics, cancellationToken);
        await WriteSegmentSyncReportAsync(outputDirectory, syncReports, cancellationToken);
        await WriteVideoEffectsReportAsync(outputDirectory, effectsReports, cancellationToken);
        await WriteRenderPerformanceReportAsync(outputDirectory, performanceReports, cancellationToken);
        await _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "video-motion-settings.json"), $"[{Environment.NewLine}{string.Join($",{Environment.NewLine}", motionDiagnostics)}{Environment.NewLine}]", cancellationToken);
        await _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-motion-settings.json"), $"[{Environment.NewLine}{string.Join($",{Environment.NewLine}", motionDiagnostics)}{Environment.NewLine}]", cancellationToken);
        _logger.LogInformation("Rendering final FFmpeg output with narration: {Command}", finalCommand);

        var timeoutSeconds = CalculateEffectiveFinalRenderTimeoutSeconds(manifest.EncodingProfile, IsShortManifest(manifest), GetConfiguredFinalRenderTimeoutSeconds(manifest.EncodingProfile, IsShortManifest(manifest)), combinedDurationSeconds);
        var finalResult = await _processRunner.ExecuteAsync(_options.FfmpegPath, finalArguments, cancellationToken, TimeSpan.FromSeconds(timeoutSeconds));
        var finalDiagnostics = BuildFinalRenderDiagnostics(combinedDurationSeconds, finalResult, finalPreset.Name, timeoutSeconds);
        LogFinalRenderDiagnostics(finalDiagnostics);
        if (finalResult.TimedOut)
        {
            throw new InvalidOperationException($"Final long render timed out after {timeoutSeconds} seconds. Increase FinalLongRenderTimeoutSeconds or use faster preset.");
        }
        if (finalResult.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException("FFmpeg final render with narration failed.");
        }

        return (finalCommand, finalResult, finalDiagnostics);
    }

    private Task WriteRenderPerformanceReportAsync(string outputDirectory, IReadOnlyCollection<SegmentRenderPerformanceReportEntry> diagnostics, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(diagnostics, DiagnosticJsonOptions);
        return _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, RenderPerformanceReportFileName), json, cancellationToken);
    }

    private void AddSegmentRenderPerformanceReport(List<SegmentRenderPerformanceReportEntry> reports, RenderPlanScene scene, int index, double durationSeconds, string profileUsed, string ffmpegCommand, ProcessExecutionResult result)
    {
        var elapsedMs = Math.Max(0d, result.Duration.TotalMilliseconds);
        var elapsedSeconds = elapsedMs / 1000d;
        var speedRatio = elapsedSeconds > 0d ? durationSeconds / elapsedSeconds : 0d;
        if (elapsedSeconds > durationSeconds * 2d)
        {
            _logger.LogWarning("Segment render slower than expected.");
        }

        reports.Add(new SegmentRenderPerformanceReportEntry(
            SegmentIndex: index + 1,
            SceneId: string.IsNullOrWhiteSpace(scene.SceneId) ? $"scene-{index + 1:000}" : scene.SceneId!,
            DurationSeconds: Math.Round(durationSeconds, 3, MidpointRounding.AwayFromZero),
            RenderElapsedMs: Math.Round(elapsedMs, 3, MidpointRounding.AwayFromZero),
            RenderSpeedRatio: Math.Round(speedRatio, 3, MidpointRounding.AwayFromZero),
            ProfileUsed: profileUsed,
            FfmpegCommand: $"{_options.FfmpegPath} {ffmpegCommand}",
            ExitCode: result.ExitCode));
    }

    private Task WriteSegmentSyncReportAsync(string outputDirectory, IReadOnlyCollection<SegmentSyncReportEntry> diagnostics, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "segment-sync-report.json"), json, cancellationToken);
    }

    private Task WriteVideoEffectsReportAsync(string outputDirectory, IReadOnlyCollection<VideoEffectsReportEntry> diagnostics, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "video-effects-report.json"), json, cancellationToken);
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

    private SegmentEffects ResolveVideoEffects(double durationSeconds, int fps, int outputWidth, int outputHeight, bool isShort, MotionProfile motionProfile)
    {
        var safeDurationSeconds = Math.Max(0d, durationSeconds);
        var requestedFadeDuration = isShort && _options.ShortFadeDurationSeconds > 0d
            ? _options.ShortFadeDurationSeconds
            : _options.FadeDurationSeconds;
        var fadeDuration = Math.Clamp(requestedFadeDuration, 0d, isShort ? 0.5d : 1.5d);
        var fadeInApplied = _options.EnableFadeInOut && fadeDuration > 0d && safeDurationSeconds > 0d;
        var fadeOutApplied = fadeInApplied && safeDurationSeconds > fadeDuration * 2d;

        return new SegmentEffects(
            KenBurnsApplied: _options.EnableKenBurns,
            FadeInApplied: fadeInApplied,
            FadeOutApplied: fadeOutApplied,
            FadeDuration: fadeInApplied ? fadeDuration : 0d,
            ZoomStart: motionProfile.ZoomStart,
            ZoomEnd: motionProfile.ZoomEnd,
            DurationSeconds: safeDurationSeconds,
            Fps: fps,
            OutputWidth: outputWidth,
            OutputHeight: outputHeight);
    }

    private string BuildSegmentVisualFilter(int outputWidth, int outputHeight, int fps, SegmentEffects effects, MotionProfile motionProfile, string scaleFlags)
    {
        var filters = new List<string> { $"fps={fps}", BuildScaleBeforeMotionFilter(outputWidth, outputHeight, scaleFlags) };
        if (effects.KenBurnsApplied)
        {
            filters.Add(BuildKenBurnsFilter(effects.DurationSeconds, effects.Fps, outputWidth, outputHeight, motionProfile));
        }

        if (effects.FadeInApplied)
        {
            var fadeDuration = effects.FadeDuration.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            filters.Add($"fade=t=in:st=0:d={fadeDuration}");
            if (effects.FadeOutApplied)
            {
                var fadeOutStart = Math.Round(Math.Max(0d, effects.DurationSeconds - effects.FadeDuration), 3, MidpointRounding.AwayFromZero)
                    .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                filters.Add($"fade=t=out:st={fadeOutStart}:d={fadeDuration}");
            }
        }

        return string.Join(',', filters);
    }

    private static string BuildScaleBeforeMotionFilter(int outputWidth, int outputHeight, string scaleFlags)
        => outputHeight > outputWidth
            ? BuildExactOutputFilter(outputWidth, outputHeight, scaleFlags)
            : $"scale={outputWidth}:{outputHeight}:flags={scaleFlags}";

    private static VideoEffectsReportEntry CreateVideoEffectsReportEntry(RenderPlanScene scene, int index, bool isShort, SegmentEffects effects, double inputDurationSeconds, double outputDurationSeconds)
    {
        var difference = Math.Abs(inputDurationSeconds - outputDurationSeconds);
        return new VideoEffectsReportEntry(
            SceneId: string.IsNullOrWhiteSpace(scene.SceneId) ? $"scene-{index + 1:000}" : scene.SceneId!,
            IsShort: isShort,
            KenBurnsApplied: effects.KenBurnsApplied,
            FadeInApplied: effects.FadeInApplied,
            FadeOutApplied: effects.FadeOutApplied,
            FadeDuration: Math.Round(effects.FadeDuration, 3, MidpointRounding.AwayFromZero),
            ZoomStart: Math.Round(effects.ZoomStart, 3, MidpointRounding.AwayFromZero),
            ZoomEnd: Math.Round(effects.ZoomEnd, 3, MidpointRounding.AwayFromZero),
            InputDuration: Math.Round(inputDurationSeconds, 3, MidpointRounding.AwayFromZero),
            OutputDuration: Math.Round(outputDurationSeconds, 3, MidpointRounding.AwayFromZero),
            DurationDifference: Math.Round(difference, 3, MidpointRounding.AwayFromZero));
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

    private MotionProfile ResolveMotionProfile(RenderPlanScene scene, bool isShort)
    {
        var isOverview = string.Equals(scene.Segment, "intro", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scene.Segment, "outro", StringComparison.OrdinalIgnoreCase);
        var isPlanetary = string.Equals(scene.Segment, "main", StringComparison.OrdinalIgnoreCase);

        if (!_options.EnableDirectionalMotion)
        {
            return new MotionProfile(_options.KenBurnsZoomStart, GetConfiguredZoomEnd(isShort), 0d, 0d, "none");
        }

        var zoomStart = _options.KenBurnsZoomStart;
        var zoomEnd = GetConfiguredZoomEnd(isShort);
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

    private double GetConfiguredZoomEnd(bool isShort)
        => isShort && _options.ShortKenBurnsZoomEnd > 0d ? _options.ShortKenBurnsZoomEnd : _options.KenBurnsZoomEnd;



    private sealed record ShortRenderManifestEntry(
        int RenderOrder,
        int SceneIndex,
        string? SceneId,
        string? ObjectName,
        string VisualPath,
        string? AudioPath,
        double Duration,
        string OutputSegmentPath);

    private sealed record SegmentFailureDiagnostics(
        int SceneIndexZeroBased,
        int SceneIndexOneBased,
        int SceneIndex,
        string? SceneId,
        string SceneTitle,
        string? SceneType,
        string? ObjectName,
        string VisualPath,
        string? AudioPath,
        double DurationSeconds,
        double Duration,
        string OutputSegmentPath,
        string FfmpegCommand,
        string Stdout,
        string Stderr,
        int? ExitCode,
        bool TimedOut,
        int? OutputWidth,
        int? OutputHeight,
        string ValidationError);

    private sealed record SegmentRenderPerformanceReportEntry(
        int SegmentIndex,
        string SceneId,
        double DurationSeconds,
        double RenderElapsedMs,
        double RenderSpeedRatio,
        string ProfileUsed,
        string FfmpegCommand,
        int ExitCode);

    private sealed record VideoEncodingReport(
        string PresetName,
        string Resolution,
        int Width,
        int Height,
        string Bitrate,
        string MaxBitrate,
        string Codec,
        int Crf,
        string Preset,
        int Fps,
        string PixelFormat,
        string AudioBitrate,
        bool FaststartEnabled,
        long EstimatedUploadSizeBytes,
        long OutputFileSizeBytes);

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

    private sealed record SegmentEffects(
        bool KenBurnsApplied,
        bool FadeInApplied,
        bool FadeOutApplied,
        double FadeDuration,
        double ZoomStart,
        double ZoomEnd,
        double DurationSeconds,
        int Fps,
        int OutputWidth,
        int OutputHeight);

    private sealed record VideoEffectsReportEntry(
        string SceneId,
        bool IsShort,
        bool KenBurnsApplied,
        bool FadeInApplied,
        bool FadeOutApplied,
        double FadeDuration,
        double ZoomStart,
        double ZoomEnd,
        double InputDuration,
        double OutputDuration,
        double DurationDifference);

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
    private (int Width, int Height) GetOutputSize(RenderManifest manifest)
    {
        var preset = ResolveFinalEncodingPreset(manifest);
        return (preset.Width, preset.Height);
    }

    private static bool IsShortManifest(RenderManifest manifest)
        => manifest.OutputHeight.GetValueOrDefault() > manifest.OutputWidth.GetValueOrDefault();

    private static string BuildExactOutputFilter(int outputWidth, int outputHeight, string scaleFlags = "bicubic")
        => $"scale={outputWidth}:{outputHeight}:flags={scaleFlags}:force_original_aspect_ratio=increase,crop={outputWidth}:{outputHeight},pad={outputWidth}:{outputHeight}:(ow-iw)/2:(oh-ih)/2,setsar=1";

    private static string BuildFinalOutputFilter(VideoEncodingPreset preset, bool cropToFill)
        => cropToFill
            ? BuildExactOutputFilter(preset.Width, preset.Height, preset.ScaleFlags)
            : $"scale={preset.Width}:{preset.Height}:flags={preset.ScaleFlags}:force_original_aspect_ratio=decrease,pad={preset.Width}:{preset.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1";

    private static int GetShortSafeFps()
        => 30;


    private VideoEncodingPreset ResolveFinalEncodingPreset(RenderManifest manifest)
        => manifest.EncodingProfile switch
        {
            VideoRenderProfileKind.MetaReelFinal => VideoEncodingPreset.MetaReelFinal(_options),
            VideoRenderProfileKind.ShortsFinal => VideoEncodingPreset.ShortsFinal(_options),
            VideoRenderProfileKind.YouTubeLongFinal => VideoEncodingPreset.YouTubeLongFinal(_options),
            _ => IsShortManifest(manifest) ? VideoEncodingPreset.ShortsFinal(_options) : VideoEncodingPreset.YouTubeLongFinal(_options)
        };

    private VideoEncodingPreset ResolveIntermediateEncodingPreset(int width, int height)
        => VideoEncodingPreset.IntermediateSegment(_options, width, height);

    private static string BuildVideoEncodeArguments(VideoEncodingPreset preset)
    {
        var parts = new List<string>
        {
            $"-c:v {preset.Codec}",
            $"-preset {preset.Preset}",
            $"-crf {preset.Crf}"
        };
        if (!string.IsNullOrWhiteSpace(preset.VideoBitrate)) parts.Add($"-b:v {preset.VideoBitrate}");
        if (!string.IsNullOrWhiteSpace(preset.MaxVideoBitrate)) parts.Add($"-maxrate {preset.MaxVideoBitrate}");
        if (!string.IsNullOrWhiteSpace(preset.BufferSize)) parts.Add($"-bufsize {preset.BufferSize}");
        parts.Add($"-pix_fmt {preset.PixelFormat}");
        return string.Join(' ', parts);
    }

    private async Task WriteEncodingReportAsync(RenderManifest manifest, string outputPath, string outputDirectory, CancellationToken cancellationToken)
    {
        var preset = ResolveFinalEncodingPreset(manifest);
        var fps = IsShortManifest(manifest) ? GetShortSafeFps() : Math.Max(1, _options.KenBurnsFps > 0 ? _options.KenBurnsFps : _options.FrameRate);
        var durationSeconds = await ProbeMediaDurationSecondsAsync(outputPath, cancellationToken);
        var fileSizeBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0L;
        var estimatedUploadSizeBytes = EstimateUploadSizeBytes(preset.VideoBitrate, preset.AudioBitrate, durationSeconds);
        if (estimatedUploadSizeBytes <= 0 && fileSizeBytes > 0)
        {
            estimatedUploadSizeBytes = fileSizeBytes;
        }
        var report = new VideoEncodingReport(
            PresetName: preset.Name,
            Resolution: $"{preset.Width}x{preset.Height}",
            Width: preset.Width,
            Height: preset.Height,
            Bitrate: preset.VideoBitrate,
            MaxBitrate: preset.MaxVideoBitrate,
            Codec: preset.Codec,
            Crf: preset.Crf,
            Preset: preset.Preset,
            Fps: fps,
            PixelFormat: preset.PixelFormat,
            AudioBitrate: preset.AudioBitrate,
            FaststartEnabled: true,
            EstimatedUploadSizeBytes: estimatedUploadSizeBytes,
            OutputFileSizeBytes: fileSizeBytes);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, EncodingReportFileName), json, cancellationToken);
    }

    private static long EstimateUploadSizeBytes(string videoBitrate, string audioBitrate, double durationSeconds)
    {
        if (durationSeconds <= 0d)
        {
            return 0L;
        }

        var totalBitsPerSecond = ParseBitrateBitsPerSecond(videoBitrate) + ParseBitrateBitsPerSecond(audioBitrate);
        return totalBitsPerSecond <= 0d ? 0L : (long)Math.Ceiling(totalBitsPerSecond * durationSeconds / 8d);
    }

    private static double ParseBitrateBitsPerSecond(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0d;
        }

        var trimmed = value.Trim();
        var multiplier = trimmed.EndsWith("M", StringComparison.OrdinalIgnoreCase) ? 1_000_000d
            : trimmed.EndsWith("k", StringComparison.OrdinalIgnoreCase) ? 1_000d
            : 1d;
        var numberText = multiplier == 1d ? trimmed : trimmed[..^1];
        return double.TryParse(numberText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number * multiplier
            : 0d;
    }

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

        var probeArguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 {QuoteFfmpegPath(mediaPath)}";
        var probeResult = await _processRunner.ExecuteAsync(ffprobePath, probeArguments, cancellationToken);
        _logger.LogInformation(
            "ffprobe duration probe completed for {MediaPath} in {DurationMs:F0}ms with exitCode={ExitCode} timedOut={TimedOut}.",
            mediaPath,
            probeResult.Duration.TotalMilliseconds,
            probeResult.ExitCode,
            probeResult.TimedOut);
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


    private async Task WriteShortsRenderManifestFinalAsync(RenderManifest manifest, RenderPlan plan, string outputDirectory, CancellationToken cancellationToken)
    {
        if (!IsShortManifest(manifest))
        {
            return;
        }

        var entries = plan.Scenes.Select((scene, index) => new ShortRenderManifestEntry(
            RenderOrder: scene.Order,
            SceneIndex: index,
            SceneId: scene.SceneId,
            ObjectName: scene.ObjectName,
            VisualPath: scene.VisualPath,
            AudioPath: scene.AudioPath,
            Duration: scene.DurationSeconds,
            OutputSegmentPath: Path.Combine(outputDirectory, $"segment-{index + 1:000}.mp4"))).ToList();
        var json = JsonSerializer.Serialize(entries, DiagnosticJsonOptions);
        await _fileSystem.WriteAllTextAsync(Path.Combine(outputDirectory, "shorts-render-manifest-final.json"), json, cancellationToken);
    }

    private async Task WriteSegmentValidationFailureDiagnosticsAsync(
        RenderPlanScene scene,
        int sceneIndex,
        string outputSegmentPath,
        string outputDirectory,
        int outputWidth,
        int outputHeight,
        string validationError,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        var diagnostics = new SegmentFailureDiagnostics(
            SceneIndexZeroBased: sceneIndex,
            SceneIndexOneBased: sceneIndex + 1,
            SceneIndex: sceneIndex + 1,
            SceneId: scene.SceneId,
            SceneTitle: scene.Caption,
            SceneType: scene.SceneType,
            ObjectName: scene.ObjectName,
            VisualPath: scene.VisualPath,
            AudioPath: scene.AudioPath,
            DurationSeconds: durationSeconds,
            Duration: durationSeconds,
            OutputSegmentPath: outputSegmentPath,
            FfmpegCommand: string.Empty,
            Stdout: string.Empty,
            Stderr: validationError,
            ExitCode: null,
            TimedOut: false,
            OutputWidth: outputWidth,
            OutputHeight: outputHeight,
            ValidationError: validationError);
        LogSegmentFailureDiagnostics(diagnostics);
        await WriteSegmentFailureDiagnosticsFileAsync(outputDirectory, sceneIndex, diagnostics, cancellationToken);
    }

    private async Task WriteSegmentFailureDiagnosticsAsync(
        RenderPlanScene scene,
        int sceneIndex,
        string outputSegmentPath,
        string outputDirectory,
        double durationSeconds,
        string segmentArguments,
        ProcessExecutionResult result,
        CancellationToken cancellationToken)
    {
        var diagnostics = new SegmentFailureDiagnostics(
            SceneIndexZeroBased: sceneIndex,
            SceneIndexOneBased: sceneIndex + 1,
            SceneIndex: sceneIndex + 1,
            SceneId: scene.SceneId,
            SceneTitle: scene.Caption,
            SceneType: scene.SceneType,
            ObjectName: scene.ObjectName,
            VisualPath: scene.VisualPath,
            AudioPath: scene.AudioPath,
            DurationSeconds: durationSeconds,
            Duration: durationSeconds,
            OutputSegmentPath: outputSegmentPath,
            FfmpegCommand: $"{_options.FfmpegPath} {segmentArguments}".TrimEnd(),
            Stdout: result.StandardOutput,
            Stderr: string.IsNullOrWhiteSpace(result.StandardError) ? result.ExceptionText : result.StandardError,
            ExitCode: result.ExitCode,
            TimedOut: result.TimedOut,
            OutputWidth: null,
            OutputHeight: null,
            ValidationError: string.Empty);

        LogSegmentFailureDiagnostics(diagnostics);
        await WriteSegmentFailureDiagnosticsFileAsync(outputDirectory, sceneIndex, diagnostics, cancellationToken);
    }

    private void LogSegmentFailureDiagnostics(SegmentFailureDiagnostics diagnostics)
    {
        _logger.LogError(
            "FFmpeg segment render failed. sceneIndexZeroBased={SceneIndexZeroBased}; sceneIndexOneBased={SceneIndexOneBased}; sceneId={SceneId}; sceneTitle={SceneTitle}; sceneType={SceneType}; objectName={ObjectName}; visualPath={VisualPath}; audioPath={AudioPath}; durationSeconds={DurationSeconds}; outputSegmentPath={OutputSegmentPath}; command={Command}; stdout={Stdout}; stderr={Stderr}; exitCode={ExitCode}; timedOut={TimedOut}",
            diagnostics.SceneIndexZeroBased,
            diagnostics.SceneIndexOneBased,
            diagnostics.SceneId,
            diagnostics.SceneTitle,
            diagnostics.SceneType,
            diagnostics.ObjectName,
            diagnostics.VisualPath,
            diagnostics.AudioPath,
            diagnostics.DurationSeconds,
            diagnostics.OutputSegmentPath,
            diagnostics.FfmpegCommand,
            diagnostics.Stdout,
            diagnostics.Stderr,
            diagnostics.ExitCode,
            diagnostics.TimedOut);
    }

    private async Task WriteSegmentFailureDiagnosticsFileAsync(string outputDirectory, int sceneIndex, SegmentFailureDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        var path = Path.Combine(outputDirectory, $"render-segment-failure-scene-{sceneIndex}.json");
        var json = JsonSerializer.Serialize(diagnostics, DiagnosticJsonOptions);
        await _fileSystem.WriteAllTextAsync(path, json, cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken);
    }

    private static List<string> ValidateSegmentInputs(RenderPlanScene scene, int sceneIndex, string outputDirectory, string outputSegmentPath, int outputWidth, int outputHeight, bool requireAudio)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(scene.VisualPath))
        {
            errors.Add("missing visualPath");
        }
        else
        {
            ValidatePath(scene.VisualPath, "visualPath", mustExist: true, errors);
        }

        if (requireAudio && string.IsNullOrWhiteSpace(scene.AudioPath))
        {
            errors.Add("missing audioPath");
        }
        else if (!string.IsNullOrWhiteSpace(scene.AudioPath))
        {
            ValidatePath(scene.AudioPath, "audioPath", mustExist: true, errors);
        }

        if (scene.DurationSeconds <= 0d)
        {
            errors.Add($"duration must be greater than 0; actual {scene.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            errors.Add($"output directory does not exist: '{outputDirectory}'");
        }

        if (outputWidth <= 0 || outputHeight <= 0)
        {
            errors.Add($"dimensions are invalid: {outputWidth}x{outputHeight}");
        }

        ValidatePath(outputSegmentPath, "outputSegmentPath", mustExist: false, errors);
        return errors;
    }

    private static void ValidatePath(string path, string fieldName, bool mustExist, ICollection<string> errors)
    {
        if (path.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            errors.Add($"{fieldName} is not safe to quote");
            return;
        }

        try
        {
            _ = QuoteFfmpegPath(path);
        }
        catch (ArgumentException ex)
        {
            errors.Add($"{fieldName} is not safe to quote: {ex.Message}");
            return;
        }

        if (mustExist && !File.Exists(path))
        {
            errors.Add(fieldName switch
            {
                "visualPath" => $"visual file not found: {path}",
                "audioPath" => $"audio file not found: {path}",
                _ => $"missing {fieldName}: '{path}'"
            });
            return;
        }

        if (mustExist)
        {
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (!stream.CanRead)
                {
                    errors.Add($"{fieldName} is not readable: '{path}'");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{fieldName} is not readable: '{path}' ({ex.GetType().Name}: {ex.Message})");
            }
        }
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

    public static int CalculateEffectiveFinalLongRenderTimeoutSeconds(int configuredTimeoutSeconds, double videoDurationSeconds)
        => CalculateEffectiveFinalRenderTimeoutSeconds(VideoRenderProfileKind.YouTubeLongFinal, isShortManifest: false, configuredTimeoutSeconds, videoDurationSeconds);

    private static int CalculateEffectiveFinalRenderTimeoutSeconds(VideoRenderProfileKind profileKind, bool isShortManifest, int configuredTimeoutSeconds, double videoDurationSeconds)
    {
        var configured = Math.Max(1, configuredTimeoutSeconds);
        if (profileKind == VideoRenderProfileKind.YouTubeLongFinal || (!isShortManifest && profileKind == VideoRenderProfileKind.Auto))
        {
            return Math.Max(configured, (int)Math.Ceiling(Math.Max(0d, videoDurationSeconds) * 4d));
        }

        return configured;
    }

    private int GetConfiguredFinalRenderTimeoutSeconds(VideoRenderProfileKind profileKind, bool isShortManifest)
        => profileKind switch
        {
            VideoRenderProfileKind.MetaReelFinal => Math.Max(1, _options.FinalMetaRenderTimeoutSeconds),
            VideoRenderProfileKind.ShortsFinal => Math.Max(1, _options.FinalShortRenderTimeoutSeconds),
            VideoRenderProfileKind.YouTubeLongFinal => Math.Max(1, _options.FinalLongRenderTimeoutSeconds),
            _ => isShortManifest ? Math.Max(1, _options.FinalShortRenderTimeoutSeconds) : Math.Max(1, _options.FinalLongRenderTimeoutSeconds)
        };

    private void LogFinalRenderDiagnostics(string diagnostics)
        => _logger.LogInformation("Final FFmpeg encode diagnostics: {Diagnostics}", diagnostics);

    private static string BuildFinalRenderDiagnostics(double inputDurationSeconds, ProcessExecutionResult result, string profileUsed, int timeoutSeconds)
    {
        var elapsedSeconds = Math.Max(0d, result.Duration.TotalSeconds);
        var renderSpeedRatio = elapsedSeconds > 0d ? inputDurationSeconds / elapsedSeconds : 0d;
        return string.Join(Environment.NewLine, new[]
        {
            $"inputDurationSeconds: {Math.Round(inputDurationSeconds, 3, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"elapsedSeconds: {Math.Round(elapsedSeconds, 3, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"renderSpeedRatio: {Math.Round(renderSpeedRatio, 3, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"profileUsed: {profileUsed}",
            $"timeoutSeconds: {timeoutSeconds}"
        });
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');

    private static string QuoteFfmpegPath(string path)
    {
        if (path.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("Path contains control characters.", nameof(path));
        }

        return $"\"{NormalizePath(path).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private string BuildSegmentTransitionArguments(IReadOnlyList<string> segmentPaths, IReadOnlyList<double> segmentDurationsSeconds, string segmentConcatPath, string combinedPath, bool transitionsEnabled, double transitionDurationSeconds, VideoEncodingPreset preset)
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
        return $"-y {inputArguments} -filter_complex \"{filterComplex}\" -map \"[vout]\" {BuildVideoEncodeArguments(preset)} \"{NormalizePath(combinedPath)}\"";
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
