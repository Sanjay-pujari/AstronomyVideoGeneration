using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class PipelineOrchestrator
{
    private readonly IAstronomyContextProvider _contextProvider;
    private readonly ITopicRankingService _topicRankingService;
    private readonly IVisualAssetProvider _visualAssetProvider;
    private readonly IScriptGenerationService _scriptGenerationService;
    private readonly ISpeechSynthesisService _speechSynthesisService;
    private readonly IVideoRenderService _videoRenderService;
    private readonly IAzureBlobStorageService _azureBlobStorageService;
    private readonly IYouTubePublishingService _youTubePublishingService;
    private readonly IShortsVideoRenderService _shortsVideoRenderService;
    private readonly IMetadataOptimizationService _metadataOptimizationService;
    private readonly IContentMonetizationService? _contentMonetizationService;
    private readonly IThumbnailGenerationService _thumbnailGenerationService;
    private readonly ISeoMetadataGeneratorService _seoMetadataGeneratorService;
    private readonly IThumbnailGeneratorService? _thumbnailGeneratorService;
    private readonly IPipelineRepository _repository;
    private readonly YouTubeOptions _youTubeOptions;
    private readonly OperationsOptions _operationsOptions;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly ILogger<PipelineOrchestrator> _logger;
    private readonly IAnalyticsFeedbackProvider? _analyticsFeedbackProvider;
    private readonly IYouTubeThumbnailPublisher? _youTubeThumbnailPublisher;
    private readonly ITopicSelectionService? _topicSelectionService;
    private readonly IPromptFeedbackService? _promptFeedbackService;
    private readonly IPipelineStageRecorder? _pipelineStageRecorder;
    private readonly IStageAlertPublisher _stageAlertPublisher;
    private readonly IOperationalAlertNotifier? _operationalAlertNotifier;
    private readonly IContentExperimentService? _contentExperimentService;
    private readonly IShortFormPublishingService? _shortFormPublishingService;
    private readonly RenderingOptions _renderingOptions;
    private readonly IPrePublishValidationService _prePublishValidationService;
    private readonly PublishingValidationOptions _publishingValidationOptions;

    public PipelineOrchestrator(
        IAstronomyContextProvider contextProvider,
        ITopicRankingService topicRankingService,
        IVisualAssetProvider visualAssetProvider,
        IScriptGenerationService scriptGenerationService,
        ISpeechSynthesisService speechSynthesisService,
        IVideoRenderService videoRenderService,
        IAzureBlobStorageService azureBlobStorageService,
        IYouTubePublishingService youTubePublishingService,
        IShortsVideoRenderService shortsVideoRenderService,
        IMetadataOptimizationService metadataOptimizationService,
        IThumbnailGenerationService thumbnailGenerationService,
        ISeoMetadataGeneratorService seoMetadataGeneratorService,
        IPipelineRepository repository,
        IOptions<YouTubeOptions> youTubeOptions,
        IOptions<RenderingOptions> renderingOptions,
        IOptions<PublishingValidationOptions> publishingValidationOptions,
        ILogger<PipelineOrchestrator> logger,
        IThumbnailGeneratorService? thumbnailGeneratorService = null,
        IContentMonetizationService? contentMonetizationService = null,
        IAnalyticsFeedbackProvider? analyticsFeedbackProvider = null,
        IYouTubeThumbnailPublisher? youTubeThumbnailPublisher = null,
        ITopicSelectionService? topicSelectionService = null,
        IPromptFeedbackService? promptFeedbackService = null,
        IPipelineStageRecorder? pipelineStageRecorder = null,
        IStageAlertPublisher? stageAlertPublisher = null,
        IOperationalAlertNotifier? operationalAlertNotifier = null,
        IOptions<OperationsOptions>? operationsOptions = null,
        IOptions<MaintenanceOptions>? maintenanceOptions = null,
        IContentExperimentService? contentExperimentService = null,
        IShortFormPublishingService? shortFormPublishingService = null,
        IPrePublishValidationService? prePublishValidationService = null)
    {
        _contextProvider = contextProvider;
        _topicRankingService = topicRankingService;
        _visualAssetProvider = visualAssetProvider;
        _scriptGenerationService = scriptGenerationService;
        _speechSynthesisService = speechSynthesisService;
        _videoRenderService = videoRenderService;
        _azureBlobStorageService = azureBlobStorageService;
        _youTubePublishingService = youTubePublishingService;
        _shortsVideoRenderService = shortsVideoRenderService;
        _metadataOptimizationService = metadataOptimizationService;
        _thumbnailGenerationService = thumbnailGenerationService;
        _seoMetadataGeneratorService = seoMetadataGeneratorService;
        _thumbnailGeneratorService = thumbnailGeneratorService;
        _repository = repository;
        _youTubeOptions = youTubeOptions.Value;
        _renderingOptions = renderingOptions.Value;
        _contentMonetizationService = contentMonetizationService;
        _operationsOptions = operationsOptions?.Value ?? new OperationsOptions();
        _maintenanceOptions = maintenanceOptions?.Value ?? new MaintenanceOptions();
        _logger = logger;
        _analyticsFeedbackProvider = analyticsFeedbackProvider;
        _youTubeThumbnailPublisher = youTubeThumbnailPublisher;
        _topicSelectionService = topicSelectionService;
        _promptFeedbackService = promptFeedbackService;
        _pipelineStageRecorder = pipelineStageRecorder;
        _stageAlertPublisher = stageAlertPublisher ?? new NullStageAlertPublisher();
        _operationalAlertNotifier = operationalAlertNotifier;
        _contentExperimentService = contentExperimentService;
        _shortFormPublishingService = shortFormPublishingService;
        _prePublishValidationService = prePublishValidationService ?? new PrePublishValidationService(Options.Create(_renderingOptions), Options.Create(new PublishingValidationOptions()), Microsoft.Extensions.Logging.Abstractions.NullLogger<PrePublishValidationService>.Instance);
        _publishingValidationOptions = publishingValidationOptions.Value;
    }

    public async Task<PipelineRun> RunAsync(RunPipelineRequest request, CancellationToken cancellationToken)
    {
        var run = new PipelineRun
        {
            RunDate = request.Date,
            ContentType = request.ContentType,
            LocationName = request.LocationName,
            TimeZone = request.TimeZone,
            PublishToYouTube = request.PublishToYouTube,
            Status = PipelineRunStatus.Queued
        };

        await _repository.CreateAsync(run, cancellationToken);
        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["pipelineRunId"] = run.Id,
            ["contentType"] = run.ContentType.ToString(),
            ["runDate"] = run.RunDate
        });
        _logger.LogInformation("Pipeline run {PipelineRunId} starting for {ContentType} ({RunDate})", run.Id, run.ContentType, run.RunDate);

        run.Status = PipelineRunStatus.Running;
        run.StartedUtc = DateTimeOffset.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            var outputDir = Path.Combine(_maintenanceOptions.WorkingDirectory, request.ContentType.ToString(), request.Date.ToString("yyyy-MM-dd"), run.Id.ToString("N"));
            Directory.CreateDirectory(outputDir);

            static StageAlertContext BuildAlertContext(PipelineRun pipelineRun, PipelineStageExecution stage)
                => new(pipelineRun.Id, stage.StageName, stage.Status, stage.DurationMs, stage.ErrorMessage, stage.MetadataJson, stage.StartedAt, stage.FinishedAt);

            async Task PublishSlowStageAlertAsync(PipelineStageExecution stage)
            {
                if (stage.DurationMs.HasValue && stage.DurationMs.Value >= _operationsOptions.SlowStageThresholdMs)
                {
                    _logger.LogWarning("Slow stage detected: {StageName} took {DurationMs}ms for run {PipelineRunId}", stage.StageName, stage.DurationMs.Value, run.Id);
                    await _stageAlertPublisher.PublishSlowStageAsync(BuildAlertContext(run, stage), cancellationToken);
                }
            }

            async Task PublishFailureAlertAsync(PipelineStageExecution stage)
                => await _stageAlertPublisher.PublishStageFailureAsync(BuildAlertContext(run, stage), cancellationToken);

            async Task<T> RunStageAsync<T>(string stageName, Func<Task<T>> action, bool continueWithFallback = false, Func<Exception, Task<T>>? fallback = null)
            {
                PipelineStageExecution? stage = null;
                if (_pipelineStageRecorder is not null)
                {
                    stage = await _pipelineStageRecorder.StartStageAsync(run.Id, stageName, null, cancellationToken);
                }

                _logger.LogInformation("Stage {StageName} started for pipeline run {PipelineRunId}", stageName, run.Id);
                try
                {
                    var result = await action();
                    if (stage is not null)
                    {
                        await _pipelineStageRecorder!.CompleteStageAsync(stage, null, cancellationToken);
                        await PublishSlowStageAlertAsync(stage);
                    }

                    _logger.LogInformation("Stage {StageName} completed for pipeline run {PipelineRunId}", stageName, run.Id);
                    return result;
                }
                catch (Exception ex)
                {
                    if (stage is not null)
                    {
                        await _pipelineStageRecorder!.FailStageAsync(stage, ex.Message, continueWithFallback && fallback is not null, null, cancellationToken);
                        await PublishFailureAlertAsync(stage);
                    }

                    _logger.LogError(ex, "Stage {StageName} failed for pipeline run {PipelineRunId}", stageName, run.Id);
                    if (continueWithFallback && fallback is not null)
                    {
                        return await fallback(ex);
                    }

                    throw;
                }
            }

            var context = await RunStageAsync("AstronomyData", () => _contextProvider.BuildContextAsync(request.Date, request.ContentType, request.LocationName, request.TimeZone, cancellationToken));
            TopicSelectionPlan? topicSelectionPlan = null;
            if (request.UseTopicPlanner && _topicSelectionService is not null)
            {
                topicSelectionPlan = await RunStageAsync("TopicSelection", () => _topicSelectionService.BuildPlanAsync(new TopicSelectionRequest
                {
                    Date = request.Date,
                    ContentType = request.ContentType,
                    LocationName = request.LocationName,
                    TimeZone = request.TimeZone,
                    MaxCandidates = 5
                }, cancellationToken));

                var selected = topicSelectionPlan.PrimaryLongForm;
                if (selected is not null)
                {
                    var selectedEvent = context.Events.FirstOrDefault(x => x.ObjectName.Equals(selected.ObjectName, StringComparison.OrdinalIgnoreCase));
                    if (selectedEvent is not null)
                    {
                        context.Events.Remove(selectedEvent);
                        context.Events.Insert(0, selectedEvent);
                    }

                    context.NewsItems.Insert(0, new NewsItemModel
                    {
                        Headline = selected.TitleCandidate,
                        Summary = selected.Rationale,
                        SourceName = "Topic Planner",
                        PublishedDate = request.Date
                    });
                }
            }

            context.TopicSelectionPlan = topicSelectionPlan;
            if (_promptFeedbackService is not null)
            {
                context.PromptFeedbackContext = await _promptFeedbackService.BuildContextAsync(new PromptFeedbackRequest
                {
                    ContentType = request.ContentType,
                    IsShortForm = false,
                    TopicSelectionPlan = topicSelectionPlan
                }, cancellationToken);
            }

            var feedbackSignals = _analyticsFeedbackProvider is null
                ? new FeedbackSignals()
                : await _analyticsFeedbackProvider.GetSignalsAsync(10, cancellationToken);

            _ = await RunStageAsync("TopicSelection", () => _topicRankingService.RankAsync(context, request.ContentType, cancellationToken));
            var script = await RunStageAsync("PromptGeneration", () => _scriptGenerationService.GenerateAsync(request.ContentType, context, cancellationToken));
            var optimizedMetadata = await _metadataOptimizationService.OptimizeForVideoAsync(new MetadataOptimizationInput
            {
                ContentType = request.ContentType,
                Context = context,
                SourceTitle = script.Title,
                SourceDescription = script.Description,
                SourceTags = script.Tags,
                SourceScript = script.ScriptBody,
                FeedbackKeywords = feedbackSignals.TopKeywords,
                FeedbackContext = context.PromptFeedbackContext
            }, cancellationToken);

            MonetizationPlan? monetizationPlan = null;
            if (_contentMonetizationService is not null)
            {
                try
                {
                    monetizationPlan = await _contentMonetizationService.BuildPlanAsync(new MonetizationInput
                    {
                        ContentType = request.ContentType,
                        Context = context,
                        Metadata = optimizedMetadata,
                        AnalyticsFeedback = feedbackSignals
                    }, cancellationToken);
                    optimizedMetadata = ApplyMonetizationPlan(optimizedMetadata, monetizationPlan);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Monetization generation failed for pipeline run {PipelineRunId}. Continuing with optimized metadata.", run.Id);
                }
            }

            script = new ScriptResult
            {
                Prompt = script.Prompt,
                Title = script.Title,
                Description = script.Description,
                ScriptBody = script.ScriptBody,
                Tags = script.Tags,
                EstimatedDurationSeconds = script.EstimatedDurationSeconds,
                OptimizedMetadata = optimizedMetadata,
                SceneScriptSections = script.SceneScriptSections
            };

            var audioPath = await RunStageAsync("SpeechSynthesis", () => _speechSynthesisService.SynthesizeAsync(script.ScriptBody, outputDir, cancellationToken));
            var visuals = await RunStageAsync("VisualGeneration", () => _visualAssetProvider.PrepareVisualsAsync(context, outputDir, cancellationToken));

            if (_thumbnailGeneratorService is not null)
            {
                _ = await RunStageAsync("ThumbnailVariants", () => _thumbnailGeneratorService.GenerateAsync(context, visuals, outputDir, script.ScriptBody, cancellationToken), continueWithFallback: true, fallback: _ => Task.FromResult<IReadOnlyCollection<string>>([]));
            }

            await _repository.AddScriptAsync(new GeneratedScript
            {
                PipelineRunId = run.Id,
                ContentType = request.ContentType,
                ScriptDate = request.Date,
                Prompt = script.Prompt,
                ScriptBody = script.ScriptBody,
                Title = script.OptimizedMetadata?.PrimaryTitle ?? script.Title,
                Description = script.Description,
                TagsCsv = string.Join(",", script.Tags),
                EstimatedDurationSeconds = script.EstimatedDurationSeconds,
                OptimizedTitle = script.OptimizedMetadata?.PrimaryTitle,
                AlternateTitlesCsv = string.Join("|", script.OptimizedMetadata?.AlternateTitles ?? []),
                OptimizedDescription = script.OptimizedMetadata?.OptimizedDescription,
                OptimizedTagsCsv = string.Join(",", script.OptimizedMetadata?.Tags ?? []),
                OptimizedHashtagsCsv = string.Join(",", script.OptimizedMetadata?.Hashtags ?? []),
                ThumbnailTextSuggestionsCsv = string.Join("|", script.OptimizedMetadata?.ThumbnailTextSuggestions ?? []),
                HookLine = script.OptimizedMetadata?.HookLine,
                PromptFeedbackContextJson = System.Text.Json.JsonSerializer.Serialize(context.PromptFeedbackContext)
            }, cancellationToken);

            await _repository.AddAssetAsync(new MediaAsset
            {
                PipelineRunId = run.Id,
                AssetType = "audio",
                FileName = Path.GetFileName(audioPath),
                LocalPath = audioPath,
                SizeBytes = File.Exists(audioPath) ? new FileInfo(audioPath).Length : 0
            }, cancellationToken);

            foreach (var visual in visuals)
            {
                await _repository.AddAssetAsync(new MediaAsset
                {
                    PipelineRunId = run.Id,
                    AssetType = "visual",
                    FileName = Path.GetFileName(visual),
                    LocalPath = visual,
                    SizeBytes = File.Exists(visual) ? new FileInfo(visual).Length : 0
                }, cancellationToken);
            }

            var sceneAudioSegments = new List<string>();
            if (script.SceneScriptSections?.HasAllSections() == true)
            {
                await WriteSceneObservationDiagnosticsAsync(context, outputDir, cancellationToken);
                var visualSceneContexts = context.SceneObservationContexts
                    .Take(Math.Min(context.SceneObservationContexts.Count, visuals.Count))
                    .ToList();
                var expectedVisualObjects = NormalizeObjects(visualSceneContexts.Select(x => x.ObjectName));
                var actualNarrationObjects = NormalizeObjects(script.SceneScriptSections.SectionsBySceneId.Keys
                    .Where(sceneId => script.SceneScriptSections.SectionsBySceneId.TryGetValue(sceneId, out _))
                    .Select(sceneId => context.SceneObservationContexts.FirstOrDefault(s => s.SceneId.Equals(sceneId, StringComparison.OrdinalIgnoreCase))?.ObjectName ?? sceneId));
                var missingFromNarration = expectedVisualObjects.Except(actualNarrationObjects, StringComparer.OrdinalIgnoreCase).ToList();
                var extraInNarration = actualNarrationObjects.Except(expectedVisualObjects, StringComparer.OrdinalIgnoreCase).ToList();
                if (missingFromNarration.Count > 0 || extraInNarration.Count > 0)
                {
                    var diagnostics = new { expectedVisualObjects, actualNarrationObjects, missingFromNarration, extraInNarration };
                    _logger.LogError("Narration/visual scene mismatch diagnostics: {Diagnostics}", JsonSerializer.Serialize(diagnostics));
                    throw new InvalidOperationException("Narration/visual scene mismatch");
                }

                var sceneSections = visualSceneContexts
                    .Select(s => (s.SceneTitle, script.SceneScriptSections.SectionsBySceneId[s.SceneId]))
                    .ToList();

                var sceneNarrationEntries = new List<(int Index, string Title, string TextPath, string AudioPath, string Text)>();
                for (var i = 0; i < sceneSections.Count; i++)
                {
                    var sceneOutputDirectory = Path.Combine(outputDir, $"scene-narration-{i + 1:000}");
                    var perSceneAudioPath = await RunStageAsync("SpeechSynthesis", () => _speechSynthesisService.SynthesizeAsync(sceneSections[i].Item2, sceneOutputDirectory, cancellationToken));

                    var sceneTextPath = Path.Combine(outputDir, $"scene-narration-{i + 1:000}.txt");
                    var sceneAudioPath = Path.Combine(outputDir, $"scene-audio-{i + 1:000}.mp3");
                    var sourceTextPath = Path.Combine(sceneOutputDirectory, "narration.txt");

                    var sceneText = File.Exists(sourceTextPath)
                        ? await File.ReadAllTextAsync(sourceTextPath, cancellationToken)
                        : sceneSections[i].Item2;

                    await File.WriteAllTextAsync(sceneTextPath, sceneText, cancellationToken);
                    File.Copy(perSceneAudioPath, sceneAudioPath, overwrite: true);
                    sceneAudioSegments.Add(sceneAudioPath);
                    sceneNarrationEntries.Add((i + 1, sceneSections[i].Item1, sceneTextPath, sceneAudioPath, sceneText));
                }

                if (sceneNarrationEntries.Count > 0)
                {
                    await WriteSceneNarrationArtifactsAsync(sceneNarrationEntries, outputDir, audioPath, cancellationToken);
                }
            }

            var totalScenes = Math.Max(1, visuals.Count);
            var durationPerScene = Math.Max(6, script.EstimatedDurationSeconds / totalScenes);
            var manifest = new RenderManifest
            {
                Title = script.OptimizedMetadata?.PrimaryTitle ?? script.Title,
                AudioPath = audioPath,
                OutputPath = Path.Combine(outputDir, "final-video.mp4"),
                Scenes = visuals.Select((v, i) => new RenderScene
                {
                    Caption = i < context.VisualIdeas.Count ? context.VisualIdeas[i].Title : $"Scene {i + 1}",
                    VisualPath = v,
                    DurationSeconds = durationPerScene,
                    AudioPath = i < sceneAudioSegments.Count ? sceneAudioSegments[i] : null
                }).ToList()
            };

            var videoPath = await RunStageAsync("Rendering", () => _videoRenderService.RenderAsync(manifest, cancellationToken));

            ThumbnailPlan thumbnailPlan;
            try
            {
                thumbnailPlan = await _thumbnailGenerationService.GenerateAsync(new ThumbnailGenerationRequest
                {
                    ContentType = request.ContentType,
                    Context = context,
                    Metadata = optimizedMetadata,
                    AvailableVisuals = visuals,
                    OutputDirectory = outputDir,
                    FeedbackSignals = feedbackSignals
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Thumbnail generation failed for run {PipelineRunId}. Continuing without a generated thumbnail.", run.Id);
                thumbnailPlan = new ThumbnailPlan
                {
                    PrimaryThumbnailText = optimizedMetadata.ThumbnailTextSuggestions.FirstOrDefault() ?? "ASTRONOMY UPDATE",
                    AlternateThumbnailTexts = optimizedMetadata.ThumbnailTextSuggestions,
                    SelectedVisualPath = visuals.FirstOrDefault(File.Exists),
                    ThumbnailPath = visuals.FirstOrDefault(File.Exists),
                    LayoutType = ThumbnailLayoutType.CenteredTitleOverlay
                };
            }

            var thumbnailPath = thumbnailPlan.ThumbnailPath;
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
                IsShortForm = false,
                ThumbnailVariants = thumbnailPlan.ThumbnailVariantPaths.ToArray()
            }, cancellationToken);
            await SeoMetadataGeneratorService.WriteToFileAsync(seoMetadata, outputDir, cancellationToken);

            await _repository.AddAssetAsync(new MediaAsset
            {
                PipelineRunId = run.Id,
                AssetType = "video",
                FileName = Path.GetFileName(videoPath),
                LocalPath = videoPath,
                SizeBytes = File.Exists(videoPath) ? new FileInfo(videoPath).Length : 0
            }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
            {
                await _repository.AddAssetAsync(new MediaAsset
                {
                    PipelineRunId = run.Id,
                    AssetType = "thumbnail",
                    FileName = Path.GetFileName(thumbnailPath),
                    LocalPath = thumbnailPath,
                    SizeBytes = new FileInfo(thumbnailPath).Length
                }, cancellationToken);
            }

            var blobUploadResult = await RunStageAsync("BlobUpload", () => _azureBlobStorageService.UploadAsync(new BlobUploadRequest
            {
                BasePath = $"{request.ContentType}/{request.Date:yyyy-MM-dd}/{run.Id:N}",
                VideoPath = videoPath,
                AudioPath = audioPath,
                ThumbnailPath = thumbnailPath
            }, cancellationToken), continueWithFallback: true, fallback: _ => Task.FromResult(new BlobUploadResult()));

            var publishStatus = "Published";
            var youtubeThumbnailUploaded = false;
            if (_publishingValidationOptions.Enabled)
            {
                var validationReport = await RunStageAsync("PrePublishValidation", () => _prePublishValidationService.ValidateAsync(new PrePublishValidationRequest
                {
                    PipelineRunId = run.Id,
                    ContentType = request.ContentType,
                    IsShort = false,
                    OutputDirectory = outputDir,
                    FinalVideoPath = videoPath,
                    VisualPaths = visuals.ToList(),
                    Context = context,
                    Script = script
                }, cancellationToken));

                if (validationReport.Errors.Count > 0 || (_publishingValidationOptions.BlockPublishOnWarning && validationReport.Warnings.Count > 0))
                {
                    var reason = validationReport.Errors.Count > 0
                        ? $"Pre-publish validation failed: {string.Join("; ", validationReport.Errors)}"
                        : $"Pre-publish validation warnings blocked publishing: {string.Join("; ", validationReport.Warnings)}";
                    publishStatus = "ValidationFailed";
                    if (request.PublishToYouTube)
                    {
                        _logger.LogWarning("Skipping publish for run {PipelineRunId}: {Reason}", run.Id, reason);
                        request = request with { PublishToYouTube = false };
                    }
                }
            }
            if (request.PublishToYouTube)
            {
                try
                {
                    run.YouTubeVideoId = await RunStageAsync("YouTubeUpload", () => _youTubePublishingService.UploadAsync(
                        videoPath,
                        script.OptimizedMetadata?.PrimaryTitle ?? script.Title,
                        script.OptimizedMetadata?.OptimizedDescription ?? script.Description,
                        script.OptimizedMetadata?.Tags ?? script.Tags,
                        _youTubeOptions.PrivacyStatus,
                        cancellationToken), continueWithFallback: true, fallback: _ => Task.FromResult<string?>(null));

                    if (string.IsNullOrWhiteSpace(run.YouTubeVideoId))
                    {
                        publishStatus = "UploadFailed";
                        if (_operationalAlertNotifier is not null)
                        {
                            await _operationalAlertNotifier.NotifyAsync(new OperationalAlert(
                                AlertCategory.PublishFailed,
                                string.Empty,
                                run.Id,
                                "YouTubeUpload",
                                request.ContentType,
                                request.Date,
                                request.LocationName,
                                ErrorSummary: "YouTube upload returned no video id",
                                OccurredAt: DateTimeOffset.UtcNow), cancellationToken);
                        }
                    }
                    else if (_youTubeThumbnailPublisher is not null && !string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
                    {
                        try
                        {
                            youtubeThumbnailUploaded = await _youTubeThumbnailPublisher.UploadThumbnailAsync(run.YouTubeVideoId, thumbnailPath, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "YouTube thumbnail upload failed for pipeline run {PipelineRunId}.", run.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "YouTube upload failed for pipeline run {PipelineRunId}.", run.Id);
                    publishStatus = "UploadFailed";
                    if (_operationalAlertNotifier is not null)
                    {
                        await _operationalAlertNotifier.NotifyAsync(new OperationalAlert(
                            AlertCategory.PublishFailed,
                            string.Empty,
                            run.Id,
                            "YouTubeUpload",
                            request.ContentType,
                            request.Date,
                            request.LocationName,
                            ErrorSummary: ex.Message,
                            OccurredAt: DateTimeOffset.UtcNow), cancellationToken);
                    }
                }
            }

            var publishedVideo = new PublishedVideo
            {
                PipelineRunId = run.Id,
                Title = script.OptimizedMetadata?.PrimaryTitle ?? script.Title,
                OptimizedTitle = script.OptimizedMetadata?.PrimaryTitle,
                OptimizedDescription = script.OptimizedMetadata?.OptimizedDescription,
                OptimizedTagsCsv = string.Join(",", script.OptimizedMetadata?.Tags ?? []),
                YouTubeVideoId = run.YouTubeVideoId,
                BlobUrl = blobUploadResult.VideoUrl,
                ThumbnailPath = thumbnailPath,
                ThumbnailUrl = blobUploadResult.ThumbnailUrl,
                ThumbnailUploadedToYouTube = youtubeThumbnailUploaded,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = publishStatus
            };

            await _repository.AddPublishedVideoAsync(publishedVideo, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            if (_contentExperimentService is not null)
            {
                try
                {
                    await _contentExperimentService.InitializeExperimentsAsync(publishedVideo, optimizedMetadata, thumbnailPlan, monetizationPlan, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Content experiments could not be initialized for pipeline run {PipelineRunId}. Publishing will continue without growth experiments.", run.Id);
                }
            }

            if (monetizationPlan?.AffiliateLinks.Count > 0)
            {
                await _repository.AddMonetizationRecordAsync(new MonetizationRecord
                {
                    VideoId = publishedVideo.Id,
                    YouTubeVideoId = run.YouTubeVideoId,
                    ContentType = request.ContentType,
                    AffiliateLinksJson = JsonSerializer.Serialize(monetizationPlan.AffiliateLinks),
                    LinkTypesCsv = string.Join(",", monetizationPlan.AffiliateLinks.Select(x => x.LinkType).Distinct(StringComparer.OrdinalIgnoreCase)),
                    PinnedCommentText = monetizationPlan.PinnedCommentText,
                    CreatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }

            var shortResult = await RunStageAsync("ShortsGeneration", () => _shortsVideoRenderService.RenderAsync(
                request.ContentType,
                context,
                visuals,
                Path.Combine(outputDir, "shorts"),
                request.PublishToYouTube,
                cancellationToken), continueWithFallback: true, fallback: _ => Task.FromResult<ShortVideoRenderResult?>(null));

            if (shortResult is not null)
            {
                await _repository.AddAssetAsync(new MediaAsset
                {
                    PipelineRunId = run.Id,
                    AssetType = "short-video",
                    FileName = Path.GetFileName(shortResult.VideoPath),
                    LocalPath = shortResult.VideoPath,
                    PublicUrl = shortResult.BlobUrl,
                    SizeBytes = File.Exists(shortResult.VideoPath) ? new FileInfo(shortResult.VideoPath).Length : 0
                }, cancellationToken);

                var shortVideo = new ShortVideo
                {
                    ParentVideoId = publishedVideo.Id,
                    YouTubeVideoId = shortResult.YouTubeVideoId,
                    Duration = shortResult.Script.EstimatedDurationSeconds,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await _repository.AddShortVideoAsync(shortVideo, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);

                if (_shortFormPublishingService is not null)
                {
                    var publicationResults = await _shortFormPublishingService.PublishAsync(new ShortFormPublicationRequest
                    {
                        ParentShortVideoId = shortVideo.Id,
                        ContentType = request.ContentType,
                        PublishToYouTube = request.PublishToYouTube,
                        Title = shortResult.Script.OptimizedMetadata?.PrimaryTitle ?? shortResult.Script.Title,
                        Caption = shortResult.Script.OptimizedMetadata?.OptimizedDescription ?? shortResult.Script.ShortScript,
                        HookLine = shortResult.Script.OptimizedMetadata?.HookLine ?? shortResult.Script.Hook,
                        Tags = shortResult.Script.OptimizedMetadata?.Tags ?? shortResult.Script.Tags,
                        Hashtags = shortResult.Script.OptimizedMetadata?.Hashtags ?? [],
                        VideoPath = shortResult.VideoPath,
                        ThumbnailPath = thumbnailPath
                    }, cancellationToken);

                    foreach (var publication in publicationResults)
                    {
                        await _repository.AddPlatformPublicationRecordAsync(new PlatformPublicationRecord
                        {
                            ParentShortVideoId = shortVideo.Id,
                            Platform = publication.Platform,
                            ExternalPostId = publication.ExternalPostId,
                            ExternalUrl = publication.ExternalUrl,
                            Status = publication.Status,
                            PublishedAt = publication.PublishedAt,
                            ErrorMessage = publication.ErrorMessage
                        }, cancellationToken);
                    }

                    shortVideo.YouTubeVideoId = publicationResults
                        .FirstOrDefault(x => x.Platform == ShortFormPlatform.YouTubeShorts && x.Status == PlatformPublicationStatus.Published)
                        ?.ExternalPostId;
                }
            }

            run.Status = PipelineRunStatus.Succeeded;
            if (_operationalAlertNotifier is not null && request.PublishToYouTube && publishStatus == "Published")
            {
                await _operationalAlertNotifier.NotifyAsync(new OperationalAlert(
                    AlertCategory.PublishSucceeded,
                    string.Empty,
                    run.Id,
                    "YouTubeUpload",
                    request.ContentType,
                    request.Date,
                    request.LocationName,
                    OccurredAt: DateTimeOffset.UtcNow), cancellationToken);
            }
            run.FinishedUtc = DateTimeOffset.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Pipeline run {PipelineRunId} completed with status {Status}", run.Id, run.Status);
            return run;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline run failed for {PipelineRunId}", run.Id);
            if (_operationalAlertNotifier is not null)
            {
                await _operationalAlertNotifier.NotifyAsync(new OperationalAlert(
                    AlertCategory.PipelineFailed,
                    string.Empty,
                    run.Id,
                    ContentType: request.ContentType,
                    RunDate: request.Date,
                    LocationName: request.LocationName,
                    ErrorSummary: ex.Message,
                    OccurredAt: DateTimeOffset.UtcNow), cancellationToken);
            }
            run.Status = PipelineRunStatus.Failed;
            run.FailureReason = ex.Message;
            run.FinishedUtc = DateTimeOffset.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Pipeline run {PipelineRunId} failed with status {Status}", run.Id, run.Status);
            throw;
        }
    }

    private static async Task WriteSceneObservationDiagnosticsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken)
    {
        var observationOptions = new ObservationOptions();
        var selected = new ObservationTimeService().SelectSceneTimes(context, context.Date, observationOptions);
        var objectSearch = selected.Where(x => x.VisibilitySearchSamples.Count > 0).Select(x => new { x.SceneId, x.ObjectName, x.VisibilitySearchSamples });
        var selectedObservationTimes = selected.Select(x => new { x.SceneId, x.ObjectName, x.LocalObservationTime, x.UtcObservationTime, x.AltitudeDegrees, x.AzimuthDegrees, x.DirectionLabel, x.IsVisible, x.VisibilityReason });
        var visibleCandidates = selected
            .Where(x => x.SceneId.StartsWith("candidate-", StringComparison.OrdinalIgnoreCase) || x.SceneId.StartsWith("object-", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.IsVisible)
            .Select(x => new { x.SceneId, x.ObjectName, x.LocalObservationTime, x.AltitudeDegrees, x.VisibilityReason })
            .ToList();
        var selectedDistinctObjects = selected
            .Where(x => x.SceneId.StartsWith("object-", StringComparison.OrdinalIgnoreCase))
            .Select(x => new { x.SceneId, x.ObjectName, x.LocalObservationTime, x.AltitudeDegrees, x.VisibilityReason })
            .ToList();
        var fillerScenes = selected
            .Where(x => x.SceneId.StartsWith("filler-", StringComparison.OrdinalIgnoreCase))
            .Select(x => new { x.SceneId, x.SceneTitle, x.ObjectName, x.VisibilityReason })
            .ToList();
        var rejectedDuplicates = visibleCandidates
            .GroupBy(x => x.ObjectName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Skip(1).Select(x => new { x.ObjectName, x.SceneId, RejectedReason = "duplicate object name" }))
            .ToList();
        var selectedVisibleDiagnostics = new { allVisibleCandidates = visibleCandidates, rejectedDuplicates, selectedDistinctObjects, fillerScenesAdded = fillerScenes };

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "object-visibility-search.json"), JsonSerializer.Serialize(objectSearch, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "selected-observation-times.json"), JsonSerializer.Serialize(selectedObservationTimes, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "scene-observation-context.json"), JsonSerializer.Serialize(selected, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "selected-visible-objects.json"), JsonSerializer.Serialize(selectedVisibleDiagnostics, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        WriteDiagnosticFromVisualIdea(context, outputDirectory, "effective-observation-settings");
        WriteDiagnosticFromVisualIdea(context, outputDirectory, "skyfield-night-plan-request");
        WriteDiagnosticFromVisualIdea(context, outputDirectory, "skyfield-night-plan-response");
        WriteDiagnosticFromVisualIdea(context, outputDirectory, "selected-visible-objects");
        WriteDiagnosticFromVisualIdea(context, outputDirectory, "scene-observation-context");
        WriteDiagnosticFromVisualIdea(context, outputDirectory, "narration-context");
        WriteDiagnosticFromVisualIdea(context, outputDirectory, "visual-scene-context");
    }

    private static void WriteDiagnosticFromVisualIdea(AstronomyContext context, string outputDirectory, string title)
    {
        var payload = context.VisualIdeas.FirstOrDefault(x => x.Title.Equals(title, StringComparison.OrdinalIgnoreCase))?.Description;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            File.WriteAllText(Path.Combine(outputDirectory, $"{title}.json"), payload);
        }
    }


    private static List<string> NormalizeObjects(IEnumerable<string> objects)
        => objects
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Equals("Night sky", StringComparison.OrdinalIgnoreCase) ? "Sky" : x)
            .ToList();

    private static SceneObservationContextEntry BuildSceneObservationContextEntry(string sceneId, AstronomyEventModel? astronomyEvent) => new()
    {
        sceneId = sceneId,
        objectName = astronomyEvent?.ObjectName ?? "Unknown",
        objectType = astronomyEvent?.Category ?? "Unknown",
        bestViewingLocalTime = astronomyEvent?.VisibilityWindow ?? "Not specified",
        directionLabel = astronomyEvent?.Direction ?? "Not specified",
        altitudeDegrees = null,
        azimuthDegrees = null,
        magnitude = null,
        visibilityLevel = "Unknown",
        recommendedTool = astronomyEvent?.ObservationTool ?? "Naked eye",
        observingTip = astronomyEvent?.Details ?? "Let your eyes adapt to darkness for 15-20 minutes.",
        whyInteresting = astronomyEvent?.Details ?? "A useful beginner observing target."
    };

    private sealed class SceneObservationContextEntry
    {
        public required string sceneId { get; init; }
        public required string objectName { get; init; }
        public required string objectType { get; init; }
        public required string bestViewingLocalTime { get; init; }
        public required string directionLabel { get; init; }
        public double? altitudeDegrees { get; init; }
        public double? azimuthDegrees { get; init; }
        public double? magnitude { get; init; }
        public required string visibilityLevel { get; init; }
        public required string recommendedTool { get; init; }
        public required string observingTip { get; init; }
        public required string whyInteresting { get; init; }
    }


    private async Task WriteSceneNarrationArtifactsAsync(
        IReadOnlyList<(int Index, string Title, string TextPath, string AudioPath, string Text)> sceneNarrationEntries,
        string outputDirectory,
        string narrationAudioPath,
        CancellationToken cancellationToken)
    {
        var narrationTextPath = Path.Combine(outputDirectory, "narration.txt");
        var combinedText = string.Join(Environment.NewLine + Environment.NewLine,
            sceneNarrationEntries.Select(entry => $"[Scene {entry.Index}: {entry.Title}]" + Environment.NewLine + entry.Text));
        await File.WriteAllTextAsync(narrationTextPath, combinedText, cancellationToken);

        var segmentsDiagnosticsPath = Path.Combine(outputDirectory, "narration-segments.txt");
        var segmentLines = sceneNarrationEntries.Select(entry => $"{entry.Index:000}|{entry.Title}|{entry.TextPath}|{entry.AudioPath}");
        await File.WriteAllLinesAsync(segmentsDiagnosticsPath, segmentLines, cancellationToken);

        var concatListPath = Path.Combine(outputDirectory, "audio-concat-list.txt");
        var concatLines = sceneNarrationEntries.Select(entry => $"file '{entry.AudioPath.Replace("'", "'\\''")}'");
        await File.WriteAllLinesAsync(concatListPath, concatLines, cancellationToken);

        var commandPath = Path.Combine(outputDirectory, "ffmpeg-audio-concat-command.txt");
        var copyArgs = $"-y -nostdin -f concat -safe 0 -i \"{concatListPath}\" -c copy \"{narrationAudioPath}\"";
        var ffmpegPath = ResolveExecutablePath("ffmpeg");
        await File.WriteAllTextAsync(commandPath, $"{ffmpegPath} {copyArgs}", cancellationToken);

        var copyExitCode = await RunProcessAsync(ffmpegPath, copyArgs, cancellationToken);
        if (copyExitCode != 0 || !File.Exists(narrationAudioPath) || new FileInfo(narrationAudioPath).Length <= 0)
        {
            var reencodeArgs = $"-y -nostdin -f concat -safe 0 -i \"{concatListPath}\" -c:a libmp3lame -q:a 2 \"{narrationAudioPath}\"";
            await File.WriteAllTextAsync(commandPath, $"{ffmpegPath} {reencodeArgs}", cancellationToken);
            var reencodeExitCode = await RunProcessAsync(ffmpegPath, reencodeArgs, cancellationToken);
            if (reencodeExitCode != 0 || !File.Exists(narrationAudioPath) || new FileInfo(narrationAudioPath).Length <= 0)
            {
                throw new InvalidOperationException("Failed to produce final narration.mp3 from scene audio segments.");
            }
        }
    }

    private async Task<int> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var executablePath = ResolveExecutablePath(fileName);
        var psi = new ProcessStartInfo(executablePath, arguments)
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
                $"Could not locate executable '{fileName}'. Install FFmpeg and ensure it is on PATH, or set the FFMPEG_PATH environment variable to the full ffmpeg executable path.",
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

        if (Path.IsPathRooted(fileName) || fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
        {
            return fileName;
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var candidates = new List<string> { fileName };
        if (OperatingSystem.IsWindows() && !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add($"{fileName}.exe");
        }

        foreach (var entry in pathEntries)
        {
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(entry, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return fileName;
    }

    private static OptimizedVideoMetadata ApplyMonetizationPlan(OptimizedVideoMetadata source, MonetizationPlan plan)
        => new()
        {
            PrimaryTitle = source.PrimaryTitle,
            AlternateTitles = source.AlternateTitles,
            OptimizedDescription = string.IsNullOrWhiteSpace(plan.FinalDescription) ? source.OptimizedDescription : plan.FinalDescription,
            Tags = source.Tags,
            Hashtags = source.Hashtags,
            ThumbnailTextSuggestions = source.ThumbnailTextSuggestions,
            HookLine = source.HookLine
        };
}
