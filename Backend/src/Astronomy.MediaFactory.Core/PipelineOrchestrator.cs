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
    private readonly PublishingOptions _publishingOptions;
    private readonly MetaPublishingOptions _metaPublishingOptions;
    private readonly IContentPublishService? _contentPublishService;
    private readonly IMetaPublishService? _metaPublishService;
    private readonly IPipelineStageExecutor? _pipelineStageExecutor;

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
        IOptions<PublishingOptions>? publishingOptions = null,
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
        IPrePublishValidationService? prePublishValidationService = null,
        IContentPublishService? contentPublishService = null,
        IOptions<MetaPublishingOptions>? metaPublishingOptions = null,
        IMetaPublishService? metaPublishService = null,
        IPipelineStageExecutor? pipelineStageExecutor = null)
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
        _publishingOptions = publishingOptions?.Value ?? new PublishingOptions();
        _metaPublishingOptions = metaPublishingOptions?.Value ?? new MetaPublishingOptions();
        _contentPublishService = contentPublishService;
        _metaPublishService = metaPublishService;
        _pipelineStageExecutor = pipelineStageExecutor;
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
            Status = PipelineRunStatus.Queued,
            ResumeSupported = true
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
            run.OutputFolder = outputDir;
            await _repository.SaveChangesAsync(cancellationToken);
            if (_pipelineStageExecutor is not null)
            {
                await _pipelineStageExecutor.ExecuteStageAsync(run.Id, PipelineStageNames.Created, _ => Task.FromResult(true), new StageExecutionOptions { MaxAttempts = 1, RetryDelaySeconds = 0, OutputPath = outputDir, DiagnosticPath = Path.Combine(outputDir, "pipeline-state.json") }, cancellationToken);
            }

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
                var persistentStageName = MapPersistentStageName(stageName);
                if (_pipelineStageExecutor is not null)
                {
                    return await _pipelineStageExecutor.ExecuteStageAsync(run.Id, persistentStageName, _ => RunInstrumentedStageAsync(stageName, action, continueWithFallback, fallback), new StageExecutionOptions { MaxAttempts = continueWithFallback ? 1 : 3, RetryDelaySeconds = 0, IsRetryableExceptionFunc = ex => !continueWithFallback && PipelineRetryClassifier.IsRetryable(ex), DiagnosticPath = Path.Combine(outputDir, $"{persistentStageName}.diagnostic.json") }, cancellationToken);
                }

                return await RunInstrumentedStageAsync(stageName, action, continueWithFallback, fallback);
            }

            async Task<T> RunInstrumentedStageAsync<T>(string stageName, Func<Task<T>> action, bool continueWithFallback = false, Func<Exception, Task<T>>? fallback = null)
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
            if (_pipelineStageExecutor is not null)
            {
                await _pipelineStageExecutor.ExecuteStageAsync(run.Id, PipelineStageNames.SceneContextCompleted, _ => Task.FromResult(true), new StageExecutionOptions { MaxAttempts = 1, RetryDelaySeconds = 0, DiagnosticPath = Path.Combine(outputDir, "scene-context.diagnostic.json") }, cancellationToken);
            }
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
                var sectionsBySceneId = script.SceneScriptSections.SectionsBySceneId;
                var expectedVisualObjects = NormalizeObjects(visualSceneContexts.Select(x => x.ObjectName));
                var actualNarrationObjects = NormalizeObjects(sectionsBySceneId.Keys
                    .Select(sceneId => context.SceneObservationContexts.FirstOrDefault(s => s.SceneId.Equals(sceneId, StringComparison.OrdinalIgnoreCase))?.ObjectName ?? sceneId));

                var sceneSections = new List<(string SceneTitle, string SectionText)>();
                var usedSceneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var visualScene in visualSceneContexts)
                {
                    var matchedSceneId = sectionsBySceneId.Keys.FirstOrDefault(id => id.Equals(visualScene.SceneId, StringComparison.OrdinalIgnoreCase));
                    if (matchedSceneId is null)
                    {
                        matchedSceneId = sectionsBySceneId.Keys
                            .FirstOrDefault(id => !usedSceneIds.Contains(id) && id.Contains(visualScene.ObjectName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (matchedSceneId is null)
                    {
                        matchedSceneId = sectionsBySceneId.Keys.FirstOrDefault(id => !usedSceneIds.Contains(id));
                    }

                    if (matchedSceneId is null)
                    {
                        throw new InvalidOperationException("Narration/visual scene mismatch");
                    }

                    usedSceneIds.Add(matchedSceneId);
                    sceneSections.Add((visualScene.SceneTitle, sectionsBySceneId[matchedSceneId]));
                }

                var missingFromNarration = expectedVisualObjects.Except(actualNarrationObjects, StringComparer.OrdinalIgnoreCase).ToList();
                var extraInNarration = actualNarrationObjects.Except(expectedVisualObjects, StringComparer.OrdinalIgnoreCase).ToList();
                if (missingFromNarration.Count > 0 || extraInNarration.Count > 0)
                {
                    var diagnostics = new
                    {
                        expectedVisualObjects,
                        actualNarrationObjects,
                        missingFromNarration,
                        extraInNarration,
                        mappedSceneIds = usedSceneIds.OrderBy(x => x).ToList()
                    };
                    _logger.LogWarning("Narration/visual scene mismatch resolved using fallback mapping: {Diagnostics}", JsonSerializer.Serialize(diagnostics));
                }

                var sceneNarrationEntries = new List<(int Index, string Title, string TextPath, string AudioPath, string Text)>();
                for (var i = 0; i < sceneSections.Count; i++)
                {
                    var sceneOutputDirectory = _renderingOptions.OutputCleanup.CreateLegacySegmentFolders
                        ? Path.Combine(outputDir, $"scene-narration-{i + 1:000}")
                        : outputDir;
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
                Scenes = visuals.Select((v, i) =>
                {
                    var observation = i < context.SceneObservationContexts.Count ? context.SceneObservationContexts[i] : null;
                    return new RenderScene
                    {
                        Caption = i < context.VisualIdeas.Count ? context.VisualIdeas[i].Title : $"Scene {i + 1}",
                        VisualPath = v,
                        DurationSeconds = durationPerScene,
                        AudioPath = i < sceneAudioSegments.Count ? sceneAudioSegments[i] : null,
                        ObjectName = observation?.ObjectName,
                        ObjectType = observation?.ObjectType,
                        SceneType = observation?.SceneType,
                        DirectionLabel = observation?.DirectionLabel,
                        AzimuthDegrees = observation?.AzimuthDegrees
                    };
                }).ToList()
            };

            var videoPath = await RunStageAsync("Rendering", () => _videoRenderService.RenderAsync(manifest, cancellationToken));

            ThumbnailPlan thumbnailPlan;
            try
            {
                thumbnailPlan = await RunStageAsync("ThumbnailGeneration", () => _thumbnailGenerationService.GenerateAsync(new ThumbnailGenerationRequest
                {
                    ContentType = request.ContentType,
                    Context = context,
                    Metadata = optimizedMetadata,
                    AvailableVisuals = visuals,
                    OutputDirectory = outputDir,
                    FeedbackSignals = feedbackSignals
                }, cancellationToken));
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
            var seoMetadata = await RunStageAsync("SeoGeneration", () => _seoMetadataGeneratorService.GenerateAsync(new SeoMetadataRequest
            {
                SceneObservationContext = context.SceneObservationContexts,
                SelectedVisibleObjects = selectedObjects,
                LocationName = context.LocationName,
                TargetDate = context.Date,
                IsShortForm = false,
                ThumbnailVariants = thumbnailPlan.ThumbnailVariantPaths.ToArray()
            }, cancellationToken));
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
            var validationPassed = true;
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
                    validationPassed = false;
                    if (request.PublishToYouTube)
                    {
                        _logger.LogWarning("Skipping publish for run {PipelineRunId}: {Reason}", run.Id, reason);
                        request = request with { PublishToYouTube = false };
                    }
                }
            }

            var mode = (_publishingOptions.Mode ?? "DryRun").Trim();
            var publishingEnabled = _publishingOptions.Enabled && !string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("PublishingMode={PublishingMode} ValidationPassed={ValidationPassed}", mode, validationPassed);
            var existingPublishedVideos = await _repository.GetPublishedVideosByRunAsync(run.Id, cancellationToken);
            var existingSuccessfulYouTube = existingPublishedVideos.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.YouTubeVideoId) && x.Status.Equals("Published", StringComparison.OrdinalIgnoreCase));
            await WritePublishIdempotencyCheckAsync(run.Id, outputDir, existingSuccessfulYouTube, cancellationToken);

            if (!publishingEnabled || !validationPassed)
            {
                _logger.LogInformation("Uploaded/Skipped=Skipped");
            }
            else if (existingSuccessfulYouTube is not null)
            {
                run.YouTubeVideoId = existingSuccessfulYouTube.YouTubeVideoId;
                _logger.LogInformation("Uploaded/Skipped=Skipped duplicate-prevention existing YouTube video {YouTubeVideoId}", run.YouTubeVideoId);
            }
            else if (_contentPublishService is null)
            {
                var selectedPrivacyStatus = string.Equals(mode, "Public", StringComparison.Ordinal) ? "public" : "private";
                if (string.Equals(mode, "DryRun", StringComparison.OrdinalIgnoreCase))
                {
                    await File.WriteAllTextAsync(
                        Path.Combine(outputDir, "youtube-publish-payload.json"),
                        JsonSerializer.Serialize(new { pipelineRunId = run.Id, videoPath, thumbnailPath, title = script.OptimizedMetadata?.PrimaryTitle ?? script.Title, description = script.OptimizedMetadata?.OptimizedDescription ?? script.Description, tags = script.OptimizedMetadata?.Tags ?? script.Tags, privacyStatus = selectedPrivacyStatus, uploadThumbnail = _publishingOptions.UploadThumbnail, mode, generatedAtUtc = DateTime.UtcNow }, new JsonSerializerOptions { WriteIndented = true }),
                        cancellationToken);
                    _logger.LogInformation("Uploaded/Skipped=Skipped");
                }
                else
                {
                    run.YouTubeVideoId = await _youTubePublishingService.UploadAsync(videoPath, script.OptimizedMetadata?.PrimaryTitle ?? script.Title, script.OptimizedMetadata?.OptimizedDescription ?? script.Description, script.OptimizedMetadata?.Tags ?? script.Tags, selectedPrivacyStatus, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(run.YouTubeVideoId) && _publishingOptions.UploadThumbnail && _youTubeThumbnailPublisher is not null && !string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
                    {
                        youtubeThumbnailUploaded = await _youTubeThumbnailPublisher.UploadThumbnailAsync(run.YouTubeVideoId, thumbnailPath, cancellationToken);
                    }
                    _logger.LogInformation("Uploaded/Skipped=Uploaded");
                }
            }
            else
            {
                var publishResults = await RunStageAsync("YouTubePublish", async () =>
                {
                    var results = await _contentPublishService.PublishForPipelineRunAsync(run.Id, cancellationToken);
                    var attemptedResults = results.Where(x => x.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase) && !IsPublishSkip(x)).ToList();
                    if (attemptedResults.Count > 0 && attemptedResults.All(x => !x.Success))
                    {
                        throw new InvalidOperationException(attemptedResults.First().Error ?? "YouTube publishing failed.");
                    }

                    return results;
                }, continueWithFallback: true, fallback: ex => Task.FromResult<IReadOnlyList<PublishResult>>([new PublishResult
                {
                    Success = false,
                    Platform = "YouTube",
                    Mode = mode,
                    Error = ex.Message,
                    PublishedUtc = DateTime.UtcNow
                }]));

                var youtubePublishResult = publishResults.FirstOrDefault(x => x.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase) && x.Success && !x.IsShort)
                    ?? publishResults.FirstOrDefault(x => x.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase) && x.Success)
                    ?? publishResults.FirstOrDefault(x => x.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase) && !IsPublishSkip(x))
                    ?? publishResults.FirstOrDefault(x => x.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase));
                if (youtubePublishResult is { Success: true })
                {
                    if (!youtubePublishResult.IsShort && !string.Equals(youtubePublishResult.Mode, "DryRun", StringComparison.OrdinalIgnoreCase))
                    {
                        run.YouTubeVideoId = youtubePublishResult.VideoId;
                    }
                    _logger.LogInformation("Uploaded/Skipped={UploadedOrSkipped}", string.Equals(youtubePublishResult.Mode, "DryRun", StringComparison.OrdinalIgnoreCase) ? "Skipped" : "Uploaded");
                }
                else if (youtubePublishResult is not null)
                {
                    publishStatus = "PublishFailed";
                    run.FailureReason = $"Publishing failed: {youtubePublishResult.Error}";
                    _logger.LogWarning("YouTube publishing failed for pipeline run {PipelineRunId}: {Error}", run.Id, youtubePublishResult.Error);
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

                if (_contentPublishService is not null && publishingEnabled && validationPassed && _publishingOptions.PublishShortVideo)
                {
                    _ = await RunStageAsync("YouTubeShortPublish", async () =>
                    {
                        var shortPublishResults = await _contentPublishService.PublishForPipelineRunAsync(run.Id, "short", cancellationToken);
                        var attemptedShortResults = shortPublishResults.Where(x => x.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase) && x.IsShort && !IsPublishSkip(x)).ToList();
                        if (attemptedShortResults.Count > 0 && attemptedShortResults.All(x => !x.Success))
                        {
                            throw new InvalidOperationException(attemptedShortResults.First().Error ?? "YouTube Shorts publishing failed.");
                        }

                        return shortPublishResults;
                    }, continueWithFallback: true, fallback: ex => Task.FromResult<IReadOnlyList<PublishResult>>([new PublishResult
                    {
                        Success = false,
                        Platform = "YouTube",
                        AssetType = "ShortVideo",
                        IsShort = true,
                        Mode = mode,
                        Error = ex.Message,
                        PublishedUtc = DateTime.UtcNow
                    }]));
                }

                if (_metaPublishService is not null
                    && _metaPublishingOptions.Enabled
                    && (_metaPublishingOptions.PublishFacebookReel || _metaPublishingOptions.PublishInstagramReel)
                    && !string.Equals(_metaPublishingOptions.Mode, "Disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var metaAsset = _metaPublishingOptions.PublishFacebookReel && _metaPublishingOptions.PublishInstagramReel
                        ? "all"
                        : _metaPublishingOptions.PublishInstagramReel ? "instagram-reel" : "facebook-reel";
                    _ = await RunStageAsync("MetaReelPublish", async () =>
                    {
                        var metaResults = await _metaPublishService.PublishForPipelineRunAsync(run.Id, metaAsset, cancellationToken);
                        var attemptedFacebookResults = metaResults.Where(x => x.Platform.Equals("Facebook", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (attemptedFacebookResults.Count > 0 && attemptedFacebookResults.All(x => !x.Success))
                        {
                            throw new InvalidOperationException(attemptedFacebookResults.First().Error ?? "Facebook Reel publishing failed.");
                        }

                        return metaResults;
                    }, continueWithFallback: true, fallback: ex => Task.FromResult<IReadOnlyList<MetaPublishResult>>([new MetaPublishResult
                    {
                        Success = false,
                        Platform = "Facebook",
                        Mode = _metaPublishingOptions.Mode,
                        Error = ex.Message,
                        PublishedUtc = DateTime.UtcNow
                    }]));
                }
            }

            if (_pipelineStageExecutor is not null)
            {
                await _pipelineStageExecutor.ExecuteStageAsync(run.Id, PipelineStageNames.Completed, _ => Task.FromResult(true), new StageExecutionOptions { MaxAttempts = 1, RetryDelaySeconds = 0, OutputPath = outputDir, DiagnosticPath = Path.Combine(outputDir, "pipeline-state.json") }, cancellationToken);
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
            if (_pipelineStageExecutor is not null && !string.IsNullOrWhiteSpace(run.OutputFolder))
            {
                try
                {
                    await _pipelineStageExecutor.ExecuteStageAsync(run.Id, PipelineStageNames.Failed, _ => Task.FromResult(true), new StageExecutionOptions { MaxAttempts = 1, RetryDelaySeconds = 0, OutputPath = run.OutputFolder, DiagnosticPath = Path.Combine(run.OutputFolder, "pipeline-state.json") }, CancellationToken.None);
                }
                catch
                {
                    // Preserve original pipeline failure.
                }
            }
            run.Status = PipelineRunStatus.Failed;
            run.FailureReason = ex.Message;
            run.FinishedUtc = DateTimeOffset.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Pipeline run {PipelineRunId} failed with status {Status}", run.Id, run.Status);
            throw;
        }
    }


    private static async Task WritePublishIdempotencyCheckAsync(Guid runId, string outputDirectory, PublishedVideo? existingSuccessfulYouTube, CancellationToken cancellationToken)
    {
        var payload = new
        {
            runId,
            checkedAtUtc = DateTimeOffset.UtcNow,
            youtubeAlreadyPublished = existingSuccessfulYouTube is not null,
            existingYouTubeVideoId = existingSuccessfulYouTube?.YouTubeVideoId,
            existingUrl = existingSuccessfulYouTube?.BlobUrl
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "publish-idempotency-check.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static string MapPersistentStageName(string stageName)
        => stageName switch
        {
            "AstronomyData" => PipelineStageNames.ObservationWindowCompleted,
            "TopicSelection" => PipelineStageNames.SkyfieldCompleted,
            "PromptGeneration" => PipelineStageNames.NarrationCompleted,
            "SpeechSynthesis" => PipelineStageNames.SpeechCompleted,
            "VisualGeneration" => PipelineStageNames.StellariumCompleted,
            "Rendering" => PipelineStageNames.RenderingCompleted,
            "ThumbnailVariants" => PipelineStageNames.ThumbnailCompleted,
            "ThumbnailGeneration" => PipelineStageNames.ThumbnailCompleted,
            "SeoGeneration" => PipelineStageNames.SeoCompleted,
            "PrePublishValidation" => PipelineStageNames.ValidationCompleted,
            "YouTubePublish" => PipelineStageNames.YouTubeLongPublished,
            "YouTubeShortPublish" => PipelineStageNames.YouTubeShortPublished,
            "MetaReelPublish" => PipelineStageNames.FacebookReelPublished,
            _ => stageName
        };

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

        if (_renderingOptions.OutputCleanup.KeepDiagnostics)
        {
            var outputFileMapPath = Path.Combine(outputDirectory, "output-file-map.json");
            var outputFileMap = new[]
            {
                new { filePath = "scene-narration-###.txt", purpose = "Per-scene narration text used for diagnostics and short/long sequencing traceability.", usedBy = new[] { "narration-segments.txt", "short-sequence-map.json" }, requiredForFinalRender = false },
                new { filePath = "scene-audio-###.mp3", purpose = "Per-scene narration audio segments used to build narration.mp3 and segmented video rendering.", usedBy = new[] { "audio-concat-list.txt", "render-manifest.json", "ffmpeg segmented flow" }, requiredForFinalRender = true },
                new { filePath = "narration-segments.txt", purpose = "Narration segment diagnostics and scene-to-audio mapping.", usedBy = new[] { "diagnostics" }, requiredForFinalRender = false },
                new { filePath = "audio-concat-list.txt", purpose = "FFmpeg concat input list for final narration.mp3.", usedBy = new[] { "ffmpeg-audio-concat-command.txt", "ffmpeg" }, requiredForFinalRender = true },
                new { filePath = "narration.mp3", purpose = "Final narration audio used by render manifest.", usedBy = new[] { "render-manifest.json", "ffmpeg final render" }, requiredForFinalRender = true },
                new { filePath = "render-manifest.json", purpose = "Canonical render plan for final-video.mp4 generation.", usedBy = new[] { "FfmpegVideoRenderService" }, requiredForFinalRender = true }
            };
            await File.WriteAllTextAsync(outputFileMapPath, JsonSerializer.Serialize(outputFileMap, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
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

    private static bool IsPublishSkip(PublishResult result)
        => result.Error is not null && (result.Error.StartsWith("Skipped because", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("validation", StringComparison.OrdinalIgnoreCase));

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
