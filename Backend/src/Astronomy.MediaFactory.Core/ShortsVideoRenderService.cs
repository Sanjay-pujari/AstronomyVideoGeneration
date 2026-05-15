using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core;

public sealed class ShortsVideoRenderService : IShortsVideoRenderService
{
    private readonly IShortsScriptGenerationService _shortsScriptGenerationService;
    private readonly ISpeechSynthesisService _speechSynthesisService;
    private readonly IVisualAssetProvider _visualAssetProvider;
    private readonly IVideoRenderService _videoRenderService;
    private readonly IAzureBlobStorageService _blobStorageService;
    private readonly IMetadataOptimizationService _metadataOptimizationService;
    private readonly ISeoMetadataGeneratorService _seoMetadataGeneratorService;
    private readonly ILogger<ShortsVideoRenderService> _logger;
    private readonly IContentMonetizationService? _contentMonetizationService;
    private readonly IAnalyticsFeedbackProvider? _analyticsFeedbackProvider;
    private readonly IPromptFeedbackService? _promptFeedbackService;
    private readonly IThumbnailGenerationService? _thumbnailGenerationService;
    private readonly RenderingOptions _renderingOptions;
    private readonly GrowthOptions _growthOptions;

    public ShortsVideoRenderService(
        IShortsScriptGenerationService shortsScriptGenerationService,
        ISpeechSynthesisService speechSynthesisService,
        IVisualAssetProvider visualAssetProvider,
        IVideoRenderService videoRenderService,
        IAzureBlobStorageService blobStorageService,
        IYouTubePublishingService youTubePublishingService,
        IMetadataOptimizationService metadataOptimizationService,
        ISeoMetadataGeneratorService seoMetadataGeneratorService,
        IOptions<YouTubeOptions> youTubeOptions,
        IOptions<RenderingOptions> renderingOptions,
        ILogger<ShortsVideoRenderService> logger,
        IContentMonetizationService? contentMonetizationService = null,
        IAnalyticsFeedbackProvider? analyticsFeedbackProvider = null,
        IPromptFeedbackService? promptFeedbackService = null,
        IOptions<GrowthOptions>? growthOptions = null,
        IThumbnailGenerationService? thumbnailGenerationService = null)
    {
        _shortsScriptGenerationService = shortsScriptGenerationService;
        _speechSynthesisService = speechSynthesisService;
        _visualAssetProvider = visualAssetProvider;
        _videoRenderService = videoRenderService;
        _blobStorageService = blobStorageService;
        _metadataOptimizationService = metadataOptimizationService;
        _seoMetadataGeneratorService = seoMetadataGeneratorService;
        _renderingOptions = renderingOptions.Value;
        _logger = logger;
        _contentMonetizationService = contentMonetizationService;
        _analyticsFeedbackProvider = analyticsFeedbackProvider;
        _promptFeedbackService = promptFeedbackService;
        _thumbnailGenerationService = thumbnailGenerationService;
        _growthOptions = growthOptions?.Value ?? new GrowthOptions();
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
        var shortAudioPath = LocalizationResolver.IsHindi(context.Localization.ResolvedLanguage)
            ? Path.Combine(outputDirectory, "narration-fallback-disabled-for-hi.mp3")
            : await _speechSynthesisService.SynthesizeAsync(scriptBody, outputDirectory, cancellationToken);

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

        var shortScenesOrdered = BuildShortScenesOrdered(context, visualCandidates);
        var sceneCount = shortScenesOrdered.Count;
        var generatedPerSceneNarration = BuildShortSceneNarration(shortScenesOrdered, context.Localization.ResolvedLanguage);
        ValidateShortNarrationBeforeAudioSynthesis(generatedPerSceneNarration, shortScenesOrdered);
        var segmentedNarration = await TryBuildSegmentedNarrationAsync(generatedPerSceneNarration, scriptBody, shortScenesOrdered, outputDirectory, cancellationToken);
        ValidateShortNarrationBeforeAudioSynthesis(segmentedNarration, shortScenesOrdered);
        if (LocalizationResolver.IsHindi(context.Localization.ResolvedLanguage) && segmentedNarration.Count == 0)
        {
            throw new InvalidOperationException("Hindi short narration validation failed: generated short narration is not Hindi.");
        }
        var finalNarrationPath = await BuildFinalNarrationAudioAsync(segmentedNarration, shortAudioPath, outputDirectory, cancellationToken);
        var shortSequence = BuildShortSequenceMap(shortScenesOrdered, segmentedNarration, outputDirectory, context.Localization.ResolvedLanguage);
        await ValidateAndWriteShortSequenceDiagnosticsAsync(shortSequence, shortScenesOrdered, context.Localization.ResolvedLanguage, outputDirectory, cancellationToken);

        optimizedMetadata = ApplyGrowthMetadata(optimizedMetadata, _growthOptions, context.Localization.ResolvedLanguage, context.LocationName);

        var shortVideoPath = Path.Combine(outputDirectory, "short-video.mp4");
        var selectedObjects = context.SceneObservationContexts
            .Where(s => !string.IsNullOrWhiteSpace(s.ObjectName) && !s.ObjectName.Equals("Sky", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.ObjectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var seoMetadata = await _seoMetadataGeneratorService.GenerateAsync(new SeoMetadataRequest
        {
            SceneObservationContext = context.SceneObservationContexts,
            SelectedVisibleObjects = selectedObjects,
            LocationName = context.LocationName,
            TargetDate = context.Date,
            IsShortForm = true,
            ThumbnailVariants = visualCandidates.ToArray(),
            ContentType = contentType,
            EventId = context.SpecialEvent?.EventId,
            EventType = context.SpecialEvent?.EventType,
            EventTitle = context.SpecialEvent?.EventTitle,
            EventDescription = context.SpecialEvent?.EventDescription,
            Language = context.Localization.ResolvedLanguage,
            RegionId = context.LocationName
        }, cancellationToken);
        await SeoMetadataGeneratorService.WriteToFileAsync(seoMetadata, outputDirectory, cancellationToken);

        var manifest = new RenderManifest
        {
            Title = shortScript.OptimizedMetadata?.PrimaryTitle ?? shortScript.Title,
            AudioPath = finalNarrationPath,
            OutputPath = shortVideoPath,
            OutputWidth = 1080,
            OutputHeight = 1920,
            EnableVerticalCrop = true,
            Scenes = shortSequence.Select(scene => new RenderScene
            {
                Caption = BuildLocalizedSceneCaption(context.Localization.ResolvedLanguage, scene.Index, scene.ObjectName),
                VisualPath = scene.VisualPath,
                DurationSeconds = scene.DurationSeconds,
                AudioPath = scene.AudioPath,
                ObjectName = scene.ObjectName,
                ObjectType = string.Equals(scene.SceneType, "Overview", StringComparison.OrdinalIgnoreCase) ? "Overview" : "Object",
                SceneType = scene.SceneType,
                DirectionLabel = shortScenesOrdered.FirstOrDefault(s => s.SceneId.Equals(scene.SceneId, StringComparison.OrdinalIgnoreCase))?.DirectionLabel
            }).ToList()
        };

        await ValidateShortSequenceBeforeRenderAsync(shortSequence, shortScenesOrdered, context.Localization.ResolvedLanguage, outputDirectory, cancellationToken);
        var thumbnailPath = await GenerateShortThumbnailAsync(contentType, context, optimizedMetadata, visualCandidates, manifest.Scenes, outputDirectory, cancellationToken);
        var videoPath = await _videoRenderService.RenderAsync(manifest, cancellationToken);

        string? blobUrl = null;
        try
        {
            var blobResult = await _blobStorageService.UploadAsync(new BlobUploadRequest
            {
                BasePath = $"shorts/{contentType}/{context.Date:yyyy-MM-dd}",
                VideoPath = videoPath,
                AudioPath = finalNarrationPath,
                ThumbnailPath = thumbnailPath ?? visualCandidates.FirstOrDefault()
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
            ThumbnailPath = thumbnailPath,
            BlobUrl = blobUrl,
            PublishStatus = publishToYouTube ? "ReadyToPublish" : "Skipped"
        };
    }

    private async Task<string?> GenerateShortThumbnailAsync(ContentType contentType, AstronomyContext context, OptimizedVideoMetadata optimizedMetadata, IReadOnlyCollection<string> visualCandidates, IReadOnlyCollection<RenderScene> scenes, string outputDirectory, CancellationToken cancellationToken)
    {
        if (_thumbnailGenerationService is null)
            return null;

        try
        {
            var plan = await _thumbnailGenerationService.GenerateAsync(new ThumbnailGenerationRequest
            {
                ContentType = contentType,
                Context = context,
                Metadata = optimizedMetadata,
                AvailableVisuals = visualCandidates,
                OutputDirectory = outputDirectory,
                IsShortForm = true,
                Scenes = scenes
            }, cancellationToken);

            return plan.ShortThumbnailPath ?? plan.ThumbnailPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Short thumbnail generation failed. Continuing with best available short frame.");
            return visualCandidates.FirstOrDefault(File.Exists);
        }
    }

    private static OptimizedVideoMetadata ApplyGrowthMetadata(OptimizedVideoMetadata source, GrowthOptions growthOptions, string language, string? region)
    {
        var description = GrowthMetadataComposer.ApplyGrowthBlock(source.OptimizedDescription, growthOptions, new GrowthMetadataInput
        {
            Platform = "YouTubeShorts",
            Language = language,
            Region = region,
            IsShortForm = true
        });

        return new OptimizedVideoMetadata
        {
            PrimaryTitle = source.PrimaryTitle,
            AlternateTitles = source.AlternateTitles,
            OptimizedDescription = description,
            Tags = source.Tags,
            Hashtags = source.Hashtags,
            ThumbnailTextSuggestions = source.ThumbnailTextSuggestions,
            HookLine = source.HookLine
        };
    }

    private async Task<List<SceneNarrationSegment>> TryBuildSegmentedNarrationAsync(IReadOnlyCollection<SceneNarrationSegment> generatedSceneNarration, string scriptBody, IReadOnlyList<ShortSceneOrderEntry> shortScenesOrdered, string outputDirectory, CancellationToken cancellationToken)
    {
        var sceneCount = shortScenesOrdered.Count;
        if (sceneCount <= 0 || string.IsNullOrWhiteSpace(scriptBody))
        {
            return [];
        }

        try
        {
            var sourceSegments = BuildSourceNarrationSegments(generatedSceneNarration, scriptBody, shortScenesOrdered);
            var results = new List<SceneNarrationSegment>(sourceSegments.Count);
            for (var i = 0; i < sourceSegments.Count; i++)
            {
                var segmentDirectory = _renderingOptions.OutputCleanup.CreateLegacySegmentFolders
                    ? Path.Combine(outputDirectory, $"scene-audio-{i + 1:000}")
                    : outputDirectory;
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

        if (_renderingOptions.OutputCleanup.KeepDiagnostics)
        {
            var outputFileMapPath = Path.Combine(outputDirectory, "output-file-map.json");
            var outputFileMap = new[]
            {
                new { filePath = "scene-narration-###.txt", purpose = "Per-scene short narration text mapped to ordered short sequence.", usedBy = new[] { "short-sequence-map.json" }, requiredForFinalRender = false },
                new { filePath = "scene-audio-###.mp3", purpose = "Per-scene short narration audio used for concat and/or segmented render.", usedBy = new[] { "audio-concat-list.txt", "render-manifest.json" }, requiredForFinalRender = true },
                new { filePath = "audio-concat-list.txt", purpose = "FFmpeg concat input list for shorts narration.mp3.", usedBy = new[] { "ffmpeg-audio-concat-command.txt", "ffmpeg" }, requiredForFinalRender = true },
                new { filePath = "narration.mp3", purpose = "Short final narration audio consumed by render manifest.", usedBy = new[] { "render-manifest.json", "ffmpeg final render" }, requiredForFinalRender = true },
                new { filePath = "render-manifest.json", purpose = "Canonical render plan for final-video.mp4 generation.", usedBy = new[] { "FfmpegVideoRenderService" }, requiredForFinalRender = true }
            };
            await File.WriteAllTextAsync(outputFileMapPath, JsonSerializer.Serialize(outputFileMap, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        }

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

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutputTask, standardErrorTask);

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

    private static List<SceneNarrationSegment> BuildSourceNarrationSegments(IReadOnlyCollection<SceneNarrationSegment> generatedSceneNarration, string scriptBody, IReadOnlyList<ShortSceneOrderEntry> shortScenesOrdered)
        => generatedSceneNarration.Where(x => !string.IsNullOrWhiteSpace(x.NarrationText)).ToList();

    private static void ValidateHindiShortScript(ShortScriptResult shortScript, string language)
    {
        if (!LocalizationResolver.IsHindi(language))
        {
            return;
        }

        var text = string.Join(" ", new[] { shortScript.Hook, shortScript.ShortScript }.Concat(shortScript.SceneNarrationSegments.Select(s => s.NarrationText)));
        if (!ContainsDevanagari(text))
        {
            throw new InvalidOperationException("Hindi short narration validation failed: generated short narration is not Hindi.");
        }
    }

    private static List<SceneNarrationSegment> BuildShortSceneNarration(IReadOnlyList<ShortSceneOrderEntry> shortScenesOrdered, string language)
        => shortScenesOrdered.Select(scene => new SceneNarrationSegment
        {
            SceneId = scene.SceneId,
            SceneTitle = scene.SceneTitle,
            VisualTarget = scene.ObjectName,
            NarrationText = BuildNarrationForScene(scene, language)
        }).ToList();

    private static string BuildNarrationForScene(ShortSceneOrderEntry scene, string language)
    {
        var isHindi = LocalizationResolver.IsHindi(language);
        if (scene.SceneType.Equals("overview", StringComparison.OrdinalIgnoreCase))
        {
            return isHindi ? "आज रात के आसमान में सुंदर हाइलाइट्स हैं—सबसे पहले इन्हें देखें।" : "Tonight's sky has beautiful highlights—here is what to watch for first.";
        }

        if (scene.SceneType.Equals("closing", StringComparison.OrdinalIgnoreCase))
        {
            return isHindi ? "आज रात ऊपर देखें और अगली तेज़ स्काई गाइड के लिए फॉलो करें।" : "Look up tonight and follow for your next quick sky guide.";
        }

        var direction = string.IsNullOrWhiteSpace(scene.DirectionLabel) ? isHindi ? "आसमान" : "the sky" : isHindi ? $"{scene.DirectionLabel} दिशा" : $"the {scene.DirectionLabel} sky";
        var localTime = scene.LocalObservationTime == default ? isHindi ? "आज रात" : "tonight" : scene.LocalObservationTime.ToString("h:mm tt");
        return isHindi
            ? $"{scene.ObjectName} लगभग {localTime} पर {direction} में दिखाई देगा।"
            : $"{scene.ObjectName} is visible around {localTime} in {direction}, high above the horizon.";
    }

    private static string BuildLocalizedSceneCaption(string language, int index, string objectName)
        => LocalizationResolver.IsHindi(language) ? $"{index}. {objectName} देखें" : $"{index}. {objectName}";

    private static List<ShortSceneOrderEntry> BuildShortScenesOrdered(AstronomyContext context, IReadOnlyList<string> visualCandidates)
    {
        var scenes = context.SceneObservationContexts
            .Where(scene => !string.IsNullOrWhiteSpace(scene.SceneId))
            .OrderBy(scene => scene.SceneId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (scenes.Count == 0)
        {
            scenes = context.SceneObservationContexts.ToList();
        }

        return visualCandidates.Select((visualPath, index) =>
        {
            var sceneContext = index < scenes.Count ? scenes[index] : null;
            return new ShortSceneOrderEntry(
                sceneContext?.SceneId ?? $"short-scene-{index + 1:000}",
                sceneContext?.ObjectName ?? $"Scene {index + 1}",
                sceneContext?.SceneTitle ?? $"Scene {index + 1}",
                index + 1,
                visualPath,
                ResolveSceneType(sceneContext),
                sceneContext?.LocalObservationTime ?? default,
                sceneContext?.DirectionLabel);
        }).ToList();
    }


    private static string ResolveSceneType(SceneObservationContext? sceneContext)
    {
        if (sceneContext is null) return "unknown";
        if (sceneContext.ObjectName.Equals("Sky", StringComparison.OrdinalIgnoreCase)) return "overview";
        return "object";
    }
    private static List<ShortSequenceItem> BuildShortSequenceMap(IReadOnlyList<ShortSceneOrderEntry> shortScenesOrdered, IReadOnlyList<SceneNarrationSegment> shortNarrationSegments, string outputDirectory, string language)
        => shortScenesOrdered.Select((scene, index) =>
        {
            var narration = shortNarrationSegments.FirstOrDefault(x => x.SceneId.Equals(scene.SceneId, StringComparison.OrdinalIgnoreCase))
                ?? new SceneNarrationSegment { SceneId = scene.SceneId };
            var narrationText = narration.NarrationText ?? string.Empty;
            var narrationTextPath = Path.Combine(outputDirectory, $"scene-narration-{index + 1:000}.txt");
            File.WriteAllText(narrationTextPath, narrationText);
            var audioPath = Path.Combine(outputDirectory, $"scene-audio-{index + 1:000}.mp3");
            return new ShortSequenceItem(index + 1, scene.SceneId, scene.ObjectName, scene.SceneType, scene.VisualPath, narrationText, narrationTextPath, audioPath, Math.Max(1, narration.DurationSeconds), Path.Combine(outputDirectory, $"segment-{index + 1:000}.mp4"), language);
        }).ToList();

    private async Task ValidateAndWriteShortSequenceDiagnosticsAsync(IReadOnlyList<ShortSequenceItem> orderedSequenceMap, IReadOnlyList<ShortSceneOrderEntry> expectedVisualScenes, string language, string outputDirectory, CancellationToken cancellationToken)
    {
        var diagnosticsPath = Path.Combine(outputDirectory, "short-sequence-map.json");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(orderedSequenceMap, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        for (var i = 0; i < orderedSequenceMap.Count; i++)
        {
            if (i >= expectedVisualScenes.Count)
            {
                throw new InvalidOperationException($"Short mismatch at index {i}: unexpected sequence length");
            }

            if (!orderedSequenceMap[i].SceneId.Equals(expectedVisualScenes[i].SceneId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Short mismatch at index {i}: {orderedSequenceMap[i].SceneId}");
            }

            var narrationText = orderedSequenceMap[i].NarrationText;
            if (LocalizationResolver.IsHindi(language) && !ContainsDevanagari(narrationText))
            {
                throw new InvalidOperationException("Hindi short narration validation failed: generated short narration is not Hindi.");
            }

            if (!File.Exists(orderedSequenceMap[i].NarrationTextPath))
            {
                throw new InvalidOperationException($"Short narration file missing before rendering: index={orderedSequenceMap[i].Index}");
            }

            if (!IsGenericSkyScene(orderedSequenceMap[i].ObjectName, orderedSequenceMap[i].SceneType)
                && !string.IsNullOrWhiteSpace(orderedSequenceMap[i].ObjectName)
                && !narrationText.Contains(orderedSequenceMap[i].ObjectName, StringComparison.OrdinalIgnoreCase))
            {
                var narrationObject = ExtractMentionedObjectName(narrationText, expectedVisualScenes.Select(s => s.ObjectName));
                throw new InvalidOperationException($"Short narration/object mismatch at index {orderedSequenceMap[i].Index}: visual={orderedSequenceMap[i].ObjectName} narration={narrationObject}");
            }
        }

        _logger.LogInformation("Short sequence map written: {DiagnosticsPath}", diagnosticsPath);
    }

    private static bool IsGenericSkyScene(string objectName, string sceneType)
        => objectName.Equals("Sky", StringComparison.OrdinalIgnoreCase)
           || sceneType.Equals("overview", StringComparison.OrdinalIgnoreCase)
           || sceneType.Equals("sky", StringComparison.OrdinalIgnoreCase);

    private static string ExtractMentionedObjectName(string narrationText, IEnumerable<string> candidateObjectNames)
        => candidateObjectNames.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) && narrationText.Contains(name, StringComparison.OrdinalIgnoreCase)) ?? "unknown";

    private static bool ContainsDevanagari(string? text)
        => !string.IsNullOrWhiteSpace(text) && text.Any(character => character >= '\u0900' && character <= '\u097F');

    private static void ValidateShortNarrationBeforeAudioSynthesis(IReadOnlyList<SceneNarrationSegment> narration, IReadOnlyList<ShortSceneOrderEntry> scenes)
    {
        for (var i = 0; i < scenes.Count; i++)
        {
            var scene = scenes[i];
            var segment = narration.FirstOrDefault(n => n.SceneId.Equals(scene.SceneId, StringComparison.OrdinalIgnoreCase));
            var text = segment?.NarrationText ?? string.Empty;
            if (scene.SceneType.Equals("object", StringComparison.OrdinalIgnoreCase)
                && !text.Contains(scene.ObjectName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Short narration/object mismatch before audio synthesis: index={i + 1} visual={scene.ObjectName}");
            }
        }
    }

    private static async Task ValidateShortSequenceBeforeRenderAsync(IReadOnlyList<ShortSequenceItem> sequence, IReadOnlyList<ShortSceneOrderEntry> scenes, string language, string outputDirectory, CancellationToken cancellationToken)
    {
        for (var i = 0; i < sequence.Count; i++)
        {
            var item = sequence[i];
            var scene = scenes[i];
            if (scene.SceneType.Equals("object", StringComparison.OrdinalIgnoreCase))
            {
                if (!Path.GetFileNameWithoutExtension(item.VisualPath).Contains(scene.ObjectName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Short visual/object mismatch before rendering: index={item.Index} visual={scene.ObjectName}");
                }

                if (!item.NarrationText.Contains(scene.ObjectName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Short narration/object mismatch before rendering: index={item.Index} visual={scene.ObjectName}");
                }
            }

            if (LocalizationResolver.IsHindi(language) && !ContainsDevanagari(item.NarrationText))
            {
                throw new InvalidOperationException("Hindi short narration validation failed: generated short narration is not Hindi.");
            }

            if (string.IsNullOrWhiteSpace(item.AudioPath) || !File.Exists(item.AudioPath))
            {
                throw new InvalidOperationException($"Short audio missing before rendering: index={item.Index}");
            }
        }

        var diagnosticsPath = Path.Combine(outputDirectory, "short-sequence-map.json");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(sequence, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private string ResolveFfprobePath()
    {
        if (!string.IsNullOrWhiteSpace(_renderingOptions.FfprobePath))
        {
            return _renderingOptions.FfprobePath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_renderingOptions.FfmpegPath))
        {
            var ffmpegPath = _renderingOptions.FfmpegPath.Trim();
            var ffmpegDirectory = Path.GetDirectoryName(ffmpegPath);
            if (!string.IsNullOrWhiteSpace(ffmpegDirectory))
            {
                var extension = Path.GetExtension(ffmpegPath);
                var ffprobeFileName = string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
                    ? "ffprobe.exe"
                    : "ffprobe";

                return Path.Combine(ffmpegDirectory, ffprobeFileName);
            }
        }

        return "ffprobe";
    }

    private sealed record ShortSceneOrderEntry(string SceneId, string ObjectName, string SceneTitle, int SceneIndex, string VisualPath, string SceneType, DateTime LocalObservationTime, string? DirectionLabel);
    private sealed record ShortSequenceItem(int Index, string SceneId, string ObjectName, string SceneType, string VisualPath, string NarrationText, string NarrationTextPath, string AudioPath, int DurationSeconds, string SegmentPath, string NarrationLanguage);

    private int GetAudioDurationSeconds(string audioPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ResolveFfprobePath(),
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
