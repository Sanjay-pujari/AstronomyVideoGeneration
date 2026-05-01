using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Diagnostics;

namespace Astronomy.MediaFactory.Core;

public sealed class ShortsVideoRenderService : IShortsVideoRenderService
{
    private readonly IShortsScriptGenerationService _shortsScriptGenerationService;
    private readonly ISpeechSynthesisService _speechSynthesisService;
    private readonly IVisualAssetProvider _visualAssetProvider;
    private readonly IVideoRenderService _videoRenderService;
    private readonly IAzureBlobStorageService _blobStorageService;
    private readonly IMetadataOptimizationService _metadataOptimizationService;
    private readonly ILogger<ShortsVideoRenderService> _logger;
    private readonly IContentMonetizationService? _contentMonetizationService;
    private readonly IAnalyticsFeedbackProvider? _analyticsFeedbackProvider;
    private readonly IPromptFeedbackService? _promptFeedbackService;
    private readonly RenderingOptions _renderingOptions;

    public ShortsVideoRenderService(
        IShortsScriptGenerationService shortsScriptGenerationService,
        ISpeechSynthesisService speechSynthesisService,
        IVisualAssetProvider visualAssetProvider,
        IVideoRenderService videoRenderService,
        IAzureBlobStorageService blobStorageService,
        IYouTubePublishingService youTubePublishingService,
        IMetadataOptimizationService metadataOptimizationService,
        IOptions<YouTubeOptions> youTubeOptions,
        IOptions<RenderingOptions> renderingOptions,
        ILogger<ShortsVideoRenderService> logger,
        IContentMonetizationService? contentMonetizationService = null,
        IAnalyticsFeedbackProvider? analyticsFeedbackProvider = null,
        IPromptFeedbackService? promptFeedbackService = null)
    {
        _shortsScriptGenerationService = shortsScriptGenerationService;
        _speechSynthesisService = speechSynthesisService;
        _visualAssetProvider = visualAssetProvider;
        _videoRenderService = videoRenderService;
        _blobStorageService = blobStorageService;
        _metadataOptimizationService = metadataOptimizationService;
        _renderingOptions = renderingOptions.Value;
        _logger = logger;
        _contentMonetizationService = contentMonetizationService;
        _analyticsFeedbackProvider = analyticsFeedbackProvider;
        _promptFeedbackService = promptFeedbackService;
    }

    public async Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken)
    {
        if (_promptFeedbackService is not null)
        {
            context.PromptFeedbackContext = await _promptFeedbackService.BuildContextAsync(new PromptFeedbackRequest
            {
                ContentType = contentType,
                IsShortForm = true,
                TopicSelectionPlan = context.TopicSelectionPlan
            }, cancellationToken);
        }

        var shortScript = await _shortsScriptGenerationService.GenerateShortAsync(contentType, context, cancellationToken);
        var feedbackSignals = _analyticsFeedbackProvider is null
            ? new FeedbackSignals()
            : await _analyticsFeedbackProvider.GetSignalsAsync(10, cancellationToken);
        var sourceHook = feedbackSignals.BestHooks.FirstOrDefault() ?? shortScript.Hook;
        var optimizedMetadata = await _metadataOptimizationService.OptimizeForShortAsync(new MetadataOptimizationInput
        {
            ContentType = contentType,
            Context = context,
            SourceTitle = shortScript.Title,
            SourceDescription = shortScript.ShortScript,
            SourceTags = shortScript.Tags,
            SourceScript = shortScript.ShortScript,
            SourceHookLine = sourceHook,
            FeedbackKeywords = feedbackSignals.TopKeywords,
            FeedbackContext = context.PromptFeedbackContext
        }, cancellationToken);

        if (_contentMonetizationService is not null)
        {
            try
            {
                var monetizationPlan = await _contentMonetizationService.BuildPlanAsync(new MonetizationInput
                {
                    ContentType = contentType,
                    Context = context,
                    Metadata = optimizedMetadata,
                    AnalyticsFeedback = feedbackSignals,
                    IsShortForm = true
                }, cancellationToken);

                optimizedMetadata = new OptimizedVideoMetadata
                {
                    PrimaryTitle = optimizedMetadata.PrimaryTitle,
                    AlternateTitles = optimizedMetadata.AlternateTitles,
                    OptimizedDescription = string.IsNullOrWhiteSpace(monetizationPlan.FinalDescription) ? optimizedMetadata.OptimizedDescription : monetizationPlan.FinalDescription,
                    Tags = optimizedMetadata.Tags,
                    Hashtags = optimizedMetadata.Hashtags,
                    ThumbnailTextSuggestions = optimizedMetadata.ThumbnailTextSuggestions,
                    HookLine = optimizedMetadata.HookLine
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Short-form monetization generation failed. Continuing with optimized short metadata.");
            }
        }

        shortScript = new ShortScriptResult
        {
            Hook = shortScript.Hook,
            ShortScript = shortScript.ShortScript,
            Title = shortScript.Title,
            Tags = shortScript.Tags,
            EstimatedDurationSeconds = shortScript.EstimatedDurationSeconds,
            OptimizedMetadata = optimizedMetadata
        };
        var scriptBody = $"{shortScript.OptimizedMetadata?.HookLine ?? shortScript.Hook} {shortScript.ShortScript}";
        var shortAudioPath = await _speechSynthesisService.SynthesizeAsync(scriptBody, outputDirectory, cancellationToken);

        var visualCandidates = sourceVisuals.Where(File.Exists).ToList();
        if (visualCandidates.Count == 0)
        {
            var generatedVisuals = await _visualAssetProvider.PrepareVisualsAsync(context, outputDirectory, cancellationToken);
            visualCandidates.AddRange(generatedVisuals.Where(File.Exists));
        }

        if (visualCandidates.Count == 0)
        {
            _logger.LogError("Short-form render failed because no visuals were available for {ContentType} on {Date}.", contentType, context.Date);
            throw new InvalidOperationException("Short-form rendering requires at least one visual asset, but none were available from the source list or fallback generator.");
        }

        var sceneCount = Math.Max(1, visualCandidates.Count);
        var segmentedNarration = await TryBuildSegmentedNarrationAsync(shortScript.SceneNarrationSegments, scriptBody, sceneCount, outputDirectory, cancellationToken);
        var finalNarrationPath = await BuildFinalNarrationAudioAsync(segmentedNarration, shortAudioPath, outputDirectory, cancellationToken);

        var shortVideoPath = Path.Combine(outputDirectory, "short-video.mp4");
        var defaultDurationPerScene = 5;
        var hasSegmentedNarration = segmentedNarration.Count == sceneCount;
        var manifest = new RenderManifest
        {
            Title = shortScript.OptimizedMetadata?.PrimaryTitle ?? shortScript.Title,
            AudioPath = finalNarrationPath,
            OutputPath = shortVideoPath,
            OutputWidth = 1080,
            OutputHeight = 1920,
            EnableVerticalCrop = true,
            Scenes = visualCandidates.Select((visualPath, index) => new RenderScene
            {
                Caption = $"Scene {index + 1}",
                VisualPath = visualPath,
                DurationSeconds = hasSegmentedNarration
                    ? segmentedNarration[index].DurationSeconds
                    : defaultDurationPerScene,
                AudioPath = hasSegmentedNarration
                    ? segmentedNarration[index].AudioPath
                    : null
            }).ToList()
        };

        var videoPath = await _videoRenderService.RenderAsync(manifest, cancellationToken);

        string? blobUrl = null;
        try
        {
            var blobResult = await _blobStorageService.UploadAsync(new BlobUploadRequest
            {
                BasePath = $"shorts/{contentType}/{context.Date:yyyy-MM-dd}",
                VideoPath = videoPath,
                AudioPath = shortAudioPath,
                ThumbnailPath = visualCandidates.FirstOrDefault()
            }, cancellationToken);
            blobUrl = blobResult.VideoUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shorts blob upload failed. Continuing with local artifacts.");
        }

        return new ShortVideoRenderResult
        {
            Script = shortScript,
            AudioPath = finalNarrationPath,
            VideoPath = videoPath,
            BlobUrl = blobUrl,
            PublishStatus = publishToYouTube ? "ReadyToPublish" : "Skipped"
        };
    }

    private async Task<List<SceneNarrationSegment>> TryBuildSegmentedNarrationAsync(IReadOnlyCollection<SceneNarrationSegment> generatedSceneNarration, string scriptBody, int sceneCount, string outputDirectory, CancellationToken cancellationToken)
    {
        if (sceneCount <= 0 || string.IsNullOrWhiteSpace(scriptBody))
        {
            return [];
        }

        try
        {
            var sourceSegments = generatedSceneNarration.Count == sceneCount
                ? generatedSceneNarration.ToList()
                : BuildFallbackSceneNarrationSegments(scriptBody, sceneCount);
            var results = new List<SceneNarrationSegment>(sourceSegments.Count);
            for (var i = 0; i < sourceSegments.Count; i++)
            {
                var segmentDirectory = Path.Combine(outputDirectory, $"scene-audio-{i + 1:000}");
                var segmentSourceAudioPath = await _speechSynthesisService.SynthesizeAsync(sourceSegments[i].NarrationText, segmentDirectory, cancellationToken);
                var segmentAudioPath = Path.Combine(outputDirectory, $"scene-audio-{i + 1:000}.mp3");
                File.Copy(segmentSourceAudioPath, segmentAudioPath, true);
                var durationSeconds = GetAudioDurationSeconds(segmentAudioPath);
                _logger.LogInformation("Scene narration segment #{Index}: {Path} ({DurationSeconds}s)", i + 1, segmentAudioPath, durationSeconds);
                results.Add(new SceneNarrationSegment
                {
                    SceneId = sourceSegments[i].SceneId,
                    SceneTitle = sourceSegments[i].SceneTitle,
                    VisualTarget = sourceSegments[i].VisualTarget,
                    NarrationText = sourceSegments[i].NarrationText,
                    AudioPath = segmentAudioPath,
                    DurationSeconds = durationSeconds
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Segmented narration generation failed. Falling back to single narration audio.");
            return [];
        }
    }

    private async Task<string> BuildFinalNarrationAudioAsync(IReadOnlyList<SceneNarrationSegment> segmentedNarration, string fallbackNarrationPath, string outputDirectory, CancellationToken cancellationToken)
    {
        if (segmentedNarration.Count == 0)
        {
            return fallbackNarrationPath;
        }

        var narrationPath = Path.Combine(outputDirectory, "narration.mp3");
        var concatListPath = Path.Combine(outputDirectory, "audio-concat-list.txt");
        var segmentsPath = Path.Combine(outputDirectory, "audio-segments.txt");
        var commandPath = Path.Combine(outputDirectory, "ffmpeg-audio-concat-command.txt");

        var concatLines = segmentedNarration.Select(segment => $"file '{segment.AudioPath!.Replace("'", "'\\''")}'").ToArray();
        await File.WriteAllLinesAsync(concatListPath, concatLines, cancellationToken);

        var segmentLines = segmentedNarration.Select((segment, index) =>
            $"{index + 1:000}|{segment.AudioPath}|{segment.DurationSeconds}").ToArray();
        await File.WriteAllLinesAsync(segmentsPath, segmentLines, cancellationToken);

        var copyArgs = $"-y -nostdin -f concat -safe 0 -i \"{concatListPath}\" -c copy \"{narrationPath}\"";
        var ffmpegPath = ResolveExecutablePath("ffmpeg");
        await File.WriteAllTextAsync(commandPath, $"{ffmpegPath} {copyArgs}", cancellationToken);
        var copyExitCode = await RunProcessAsync(ffmpegPath, copyArgs, cancellationToken);
        if (copyExitCode != 0 || !File.Exists(narrationPath) || new FileInfo(narrationPath).Length <= 0)
        {
            var reencodeArgs = $"-y -nostdin -f concat -safe 0 -i \"{concatListPath}\" -c:a libmp3lame -q:a 2 \"{narrationPath}\"";
            await File.WriteAllTextAsync(commandPath, $"{ffmpegPath} {reencodeArgs}", cancellationToken);
            var reencodeExitCode = await RunProcessAsync(ffmpegPath, reencodeArgs, cancellationToken);
            if (reencodeExitCode != 0 || !File.Exists(narrationPath) || new FileInfo(narrationPath).Length <= 0)
            {
                _logger.LogWarning("Failed to concat segmented narration audio. Falling back to single narration audio.");
                return fallbackNarrationPath;
            }
        }

        var expectedDuration = segmentedNarration.Sum(segment => segment.DurationSeconds);
        var actualDuration = GetAudioDurationSeconds(narrationPath);
        _logger.LogInformation("Final narration generated at {Path}. Expected duration ~{Expected}s, actual duration {Actual}s.", narrationPath, expectedDuration, actualDuration);
        return narrationPath;
    }

    private static async Task<int> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return -1;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new InvalidOperationException(
                $"Could not locate executable '{fileName}'. Install FFmpeg and ensure it is on PATH, or set Rendering:FfmpegPath or the FFMPEG_PATH environment variable to the full ffmpeg executable path.",
                ex);
        }
    }

    private string ResolveExecutablePath(string fileName)
    {
        var configuredPath = string.Equals(fileName, "ffmpeg", StringComparison.OrdinalIgnoreCase)
            ? _renderingOptions.FfmpegPath
            : null;
        configuredPath ??= Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        return fileName;
    }

    private static List<SceneNarrationSegment> BuildFallbackSceneNarrationSegments(string scriptBody, int sceneCount)
    {
        var words = scriptBody.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var wordSegments = new List<SceneNarrationSegment>(sceneCount);
        var wordsPerScene = (int)Math.Ceiling((double)words.Length / sceneCount);
        for (var i = 0; i < sceneCount; i++)
        {
            var start = i * wordsPerScene;
            if (start >= words.Length)
            {
                wordSegments.Add(new SceneNarrationSegment { SceneId = $"scene-{i + 1}", SceneTitle = $"Scene {i + 1}", VisualTarget = "fallback", NarrationText = words[^1] });
                continue;
            }

            var take = Math.Min(wordsPerScene, words.Length - start);
            wordSegments.Add(new SceneNarrationSegment
            {
                SceneId = $"scene-{i + 1}",
                SceneTitle = $"Scene {i + 1}",
                VisualTarget = "fallback",
                NarrationText = string.Join(' ', words.Skip(start).Take(take))
            });
        }

        return wordSegments;
    }

    private int GetAudioDurationSeconds(string audioPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return 30;
            }

            process.WaitForExit(10000);
            var output = process.StandardOutput.ReadToEnd().Trim();
            if (double.TryParse(output, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration))
            {
                return Math.Max(1, (int)Math.Ceiling(duration));
            }

            return 30;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine narration audio duration from {AudioPath}. Falling back to 30 seconds.", audioPath);
            return 30;
        }
    }
}
