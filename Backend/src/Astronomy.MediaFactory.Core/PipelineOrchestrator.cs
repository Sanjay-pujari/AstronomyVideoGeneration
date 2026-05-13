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
    private readonly LocalizationOptions _localizationOptions;
    private readonly SchedulerOptions _schedulerOptions;
    private readonly GrowthOptions _growthOptions;
    private readonly IAstronomyEventDecisionService? _eventDecisionService;
    private readonly IAstronomyEventStore? _eventStore;

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
        IPipelineStageExecutor? pipelineStageExecutor = null,
        IOptions<LocalizationOptions>? localizationOptions = null,
        IOptions<GrowthOptions>? growthOptions = null,
        IOptions<SchedulerOptions>? schedulerOptions = null,
        IAstronomyEventDecisionService? eventDecisionService = null,
        IAstronomyEventStore? eventStore = null)
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
        _localizationOptions = localizationOptions?.Value ?? new LocalizationOptions();
        _schedulerOptions = schedulerOptions?.Value ?? new SchedulerOptions();
        _growthOptions = growthOptions?.Value ?? new GrowthOptions();
        _eventDecisionService = eventDecisionService;
        _eventStore = eventStore;
    }

    public async Task<PipelineRun> RunAsync(RunPipelineRequest request, CancellationToken cancellationToken)
    {
        var regionLanguage = ResolveRegionLanguage(request.RegionId, request.LocationName, _schedulerOptions);
        var localization = LocalizationResolver.Resolve(request.Language, regionLanguage, _localizationOptions);
        var run = new PipelineRun
        {
            RunDate = request.Date,
            ContentType = request.ContentType,
            RegionId = NormalizeRegionId(request.RegionId, request.LocationName),
            LocationName = request.LocationName,
            TimeZone = request.TimeZone,
            Language = localization.ResolvedLanguage,
            PublishToYouTube = request.PublishToYouTube,
            UseTopicPlanner = request.UseTopicPlanner,
            Status = PipelineRunStatus.Queued,
            ResumeSupported = true,
            EventId = request.EventId,
            EventType = request.EventType,
            EventTitle = request.EventTitle,
            EventDescription = request.EventDescription,
            DecisionType = request.ContentType == ContentType.SpecialEventGuide ? "GenerateSpecialEventGuide" : null,
            SpecialEventGuideGenerated = request.ContentType == ContentType.SpecialEventGuide
        };

        await _repository.CreateAsync(run, cancellationToken);
        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["pipelineRunId"] = run.Id,
            ["contentType"] = run.ContentType.ToString(),
            ["runDate"] = run.RunDate,
            ["language"] = run.Language
        });
        _logger.LogInformation("Pipeline run {PipelineRunId} starting for {ContentType} ({RunDate})", run.Id, run.ContentType, run.RunDate);

        run.Status = PipelineRunStatus.Running;
        run.StartedUtc = DateTimeOffset.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            var outputDir = BuildOutputDirectory(_maintenanceOptions.WorkingDirectory, request.ContentType, request.Date, request.RegionId, request.LocationName, run.Id);
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

            async Task<T> RunStageAsync<T>(string stageName, Func<Task<T>> action, bool continueWithFallback = false, Func<Exception, Task<T>>? fallback = null, string? outputPath = null)
            {
                var persistentStageName = MapPersistentStageName(stageName);
                outputPath ??= GetExpectedOutputPath(persistentStageName, outputDir);
                if (_pipelineStageExecutor is not null)
                {
                    return await _pipelineStageExecutor.ExecuteStageAsync(run.Id, persistentStageName, _ => RunInstrumentedStageAsync(stageName, action, continueWithFallback, fallback), new StageExecutionOptions { MaxAttempts = continueWithFallback ? 1 : 3, RetryDelaySeconds = 0, IsRetryableExceptionFunc = ex => !continueWithFallback && PipelineRetryClassifier.IsRetryable(ex), OutputPath = outputPath, DiagnosticPath = Path.Combine(outputDir, $"{persistentStageName}.diagnostic.json") }, cancellationToken);
                }

                return await RunInstrumentedStageAsync(stageName, action, continueWithFallback, fallback);
            }

            async Task SetStageStatusAsync(string stageName, string status, string? outputPath = null, string? reason = null)
            {
                if (_pipelineStageExecutor is null)
                    return;

                outputPath ??= GetExpectedOutputPath(stageName, outputDir);
                var stage = await _repository.GetLatestStageExecutionAsync(run.Id, stageName, cancellationToken);
                if (stage is null)
                {
                    stage = new PipelineStageExecution { PipelineRunId = run.Id, StageName = stageName, MaxAttempts = 1 };
                    await _repository.AddStageExecutionAsync(stage, cancellationToken);
                }

                stage.Status = status;
                stage.StartedUtc = stage.StartedUtc ?? DateTimeOffset.UtcNow;
                stage.CompletedUtc = DateTimeOffset.UtcNow;
                stage.OutputPath = outputPath;
                stage.DiagnosticPath = Path.Combine(outputDir, $"{stageName}.diagnostic.json");
                stage.LastError = reason;
                stage.AttemptCount = Math.Max(1, stage.AttemptCount);
                await _repository.SaveChangesAsync(cancellationToken);
            }

            async Task EnsurePublishStagesHandledAsync(bool currentPublishingEnabled, bool currentValidationPassed)
            {
                var stages = await _repository.GetStageExecutionsAsync(run.Id, cancellationToken);
                var failedEnabledStages = stages
                    .Where(s => IsEnabledPublishStage(s.StageName, currentPublishingEnabled, currentValidationPassed, request.PublishToYouTube, _publishingOptions.PublishShortVideo, _metaPublishingOptions))
                    .Where(s => s.Status == PersistentStageStatuses.Failed)
                    .Select(s => s.StageName)
                    .ToArray();

                if (failedEnabledStages.Length > 0)
                {
                    throw new InvalidOperationException($"Enabled publish stages failed: {string.Join(", ", failedEnabledStages)}");
                }
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
            ApplySpecialEventRequestContext(request, context);
            context.Localization = localization;
            EventContentDecision? eventDecision = null;
            if (request.ContentType == ContentType.DailySkyGuide && _eventDecisionService is not null)
            {
                eventDecision = await ApplyDailyGuideEventDecisionAsync(request, context, outputDir, cancellationToken);
                run.DecisionType = eventDecision.DecisionType;
                run.InjectedIntoDailyGuide = eventDecision.DecisionType is "InjectIntoDailyGuide" or "GenerateBoth";
                run.SpecialEventGuideGenerated = eventDecision.DecisionType is "GenerateSpecialEventGuide" or "GenerateBoth";
                if (eventDecision.PrimaryEvent is not null)
                {
                    run.EventId = eventDecision.PrimaryEvent.EventId;
                    run.EventType = eventDecision.PrimaryEvent.EventType;
                    run.EventTitle = eventDecision.PrimaryEvent.Title;
                }
                await _repository.SaveChangesAsync(cancellationToken);
            }
            await WriteLocalizationContextAsync(context.Localization, outputDir, cancellationToken);
            await WritePrimaryContextArtifactsAsync(context, outputDir, cancellationToken);
            if (_pipelineStageExecutor is not null)
            {
                await _pipelineStageExecutor.ExecuteStageAsync(run.Id, PipelineStageNames.SceneContextCompleted, _ => Task.FromResult(true), new StageExecutionOptions { MaxAttempts = 1, RetryDelaySeconds = 0, OutputPath = GetExpectedOutputPath(PipelineStageNames.SceneContextCompleted, outputDir), DiagnosticPath = Path.Combine(outputDir, "scene-context.diagnostic.json") }, cancellationToken);
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

            optimizedMetadata = ApplyGrowthMetadata(optimizedMetadata, _growthOptions, context.Localization.ResolvedLanguage, request.RegionId ?? context.LocationName);

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

            ValidateNarrationLanguage(script.ScriptBody, context.Localization.ResolvedLanguage);
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
                PromptFeedbackContextJson = System.Text.Json.JsonSerializer.Serialize(context.PromptFeedbackContext),
                Language = context.Localization.ResolvedLanguage
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
            var sceneSections = new List<(SceneObservationContext Scene, string SectionText)>();
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

                var usedSceneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var fallbackSceneIds = new List<string>();
                var alignedSectionsBySceneId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var visualScene in visualSceneContexts)
                {
                    var matchedSceneId = sectionsBySceneId.Keys.FirstOrDefault(id => !usedSceneIds.Contains(id) && id.Equals(visualScene.SceneId, StringComparison.OrdinalIgnoreCase));
                    if (matchedSceneId is null)
                    {
                        matchedSceneId = sectionsBySceneId.Keys
                            .FirstOrDefault(id => !usedSceneIds.Contains(id) && id.Contains(visualScene.ObjectName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (matchedSceneId is null)
                    {
                        matchedSceneId = sectionsBySceneId.Keys.FirstOrDefault(id => !usedSceneIds.Contains(id));
                    }

                    var sectionText = matchedSceneId is null
                        ? BuildFallbackSceneNarration(visualScene, context.Localization.ResolvedLanguage)
                        : sectionsBySceneId[matchedSceneId];

                    if (matchedSceneId is null)
                    {
                        fallbackSceneIds.Add(visualScene.SceneId);
                    }
                    else
                    {
                        usedSceneIds.Add(matchedSceneId);
                    }

                    alignedSectionsBySceneId[visualScene.SceneId] = sectionText;
                    sceneSections.Add((visualScene, sectionText));
                }

                var missingFromNarration = expectedVisualObjects.Except(actualNarrationObjects, StringComparer.OrdinalIgnoreCase).ToList();
                var extraInNarration = actualNarrationObjects.Except(expectedVisualObjects, StringComparer.OrdinalIgnoreCase).ToList();
                if (missingFromNarration.Count > 0 || extraInNarration.Count > 0 || fallbackSceneIds.Count > 0)
                {
                    var diagnostics = new
                    {
                        expectedVisualObjects,
                        actualNarrationObjects,
                        missingFromNarration,
                        extraInNarration,
                        mappedSceneIds = usedSceneIds.OrderBy(x => x).ToList(),
                        fallbackSceneIds
                    };
                    _logger.LogWarning("Narration/visual scene mismatch resolved using fallback mapping: {Diagnostics}", JsonSerializer.Serialize(diagnostics));
                }

                script = CopyScriptWithSceneSections(script, alignedSectionsBySceneId);

                var sceneNarrationEntries = new List<(int Index, string Title, string TextPath, string AudioPath, string Text)>();
                for (var i = 0; i < sceneSections.Count; i++)
                {
                    var sceneOutputDirectory = _renderingOptions.OutputCleanup.CreateLegacySegmentFolders
                        ? Path.Combine(outputDir, $"scene-narration-{i + 1:000}")
                        : outputDir;
                    var sceneNumber = i + 1;
                    ValidateNarrationLanguage(sceneSections[i].SectionText, context.Localization.ResolvedLanguage);
                    var perSceneAudioPath = await RunStageAsync($"SceneSpeechSynthesis-{sceneNumber:000}", () => _speechSynthesisService.SynthesizeAsync(sceneSections[i].SectionText, sceneOutputDirectory, cancellationToken));

                    if (string.IsNullOrWhiteSpace(perSceneAudioPath))
                    {
                        throw new InvalidOperationException($"Scene speech synthesis did not return an audio path for scene {sceneNumber:000}.");
                    }

                    if (!File.Exists(perSceneAudioPath))
                    {
                        throw new InvalidOperationException($"Scene speech synthesis did not create an audio file for scene {sceneNumber:000}: {perSceneAudioPath}");
                    }

                    var sceneTextPath = Path.Combine(outputDir, $"scene-narration-{sceneNumber:000}.txt");
                    var sceneAudioPath = Path.Combine(outputDir, $"scene-audio-{sceneNumber:000}.mp3");
                    var sourceTextPath = Path.Combine(sceneOutputDirectory, "narration.txt");

                    var sceneText = File.Exists(sourceTextPath)
                        ? await File.ReadAllTextAsync(sourceTextPath, cancellationToken)
                        : sceneSections[i].SectionText;

                    await File.WriteAllTextAsync(sceneTextPath, sceneText, cancellationToken);
                    File.Copy(perSceneAudioPath, sceneAudioPath, overwrite: true);
                    sceneAudioSegments.Add(sceneAudioPath);
                    sceneNarrationEntries.Add((i + 1, sceneSections[i].Scene.SceneTitle, sceneTextPath, sceneAudioPath, sceneText));
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
                        Caption = i < sceneSections.Count ? sceneSections[i].SectionText : (i < context.VisualIdeas.Count ? context.VisualIdeas[i].Title : $"Scene {i + 1}"),
                        VisualPath = v,
                        DurationSeconds = durationPerScene,
                        AudioPath = i < sceneAudioSegments.Count ? sceneAudioSegments[i] : null,
                        ObjectName = observation?.ObjectName,
                        ObjectType = observation?.ObjectType,
                        SceneType = observation?.SceneType,
                        SceneId = observation?.SceneId,
                        SegmentIndex = i + 1,
                        NarrationLanguage = context.Localization.ResolvedLanguage,
                        NarrationText = i < sceneSections.Count ? sceneSections[i].SectionText : null,
                        DirectionLabel = observation?.DirectionLabel,
                        AzimuthDegrees = observation?.AzimuthDegrees
                    };
                }).ToList()
            };

            var videoPath = await RunStageAsync("Rendering", () => _videoRenderService.RenderAsync(manifest, cancellationToken));

            ThumbnailPlan? thumbnailPlan;
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
                thumbnailPlan = BuildFallbackThumbnailPlan(optimizedMetadata, visuals);
            }

            if (thumbnailPlan is null)
            {
                _logger.LogWarning("Thumbnail generation returned no plan for run {PipelineRunId}. Continuing with a source visual fallback.", run.Id);
                thumbnailPlan = BuildFallbackThumbnailPlan(optimizedMetadata, visuals);
            }

            var thumbnailPath = thumbnailPlan.ThumbnailPath;
            await WriteThumbnailSelectionAsync(thumbnailPlan, outputDir, cancellationToken);
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
                ThumbnailVariants = (thumbnailPlan.ThumbnailVariantPaths ?? []).ToArray(),
                ContentType = request.ContentType,
                EventId = request.EventId,
                EventType = request.EventType,
                EventTitle = request.EventTitle,
                EventDescription = request.EventDescription,
                Language = context.Localization.ResolvedLanguage,
                RegionId = request.RegionId
            }, cancellationToken));
            await SeoMetadataGeneratorService.WriteToFileAsync(seoMetadata, outputDir, cancellationToken);
            await WriteLanguagePipelineDiagnosticsAsync(context.Localization.ResolvedLanguage, script.ScriptBody, seoMetadata, outputDir, null, cancellationToken);
            await WriteSpecialEventDiagnosticsAsync(request, context, outputDir, seoMetadata, cancellationToken);
            await WriteContentExpansionReportAsync(context, request, outputDir, script.EstimatedDurationSeconds, cancellationToken);

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
                await SetStageStatusAsync(PipelineStageNames.YouTubeLongPublished, PersistentStageStatuses.Succeeded);
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
                    await SetStageStatusAsync(PipelineStageNames.YouTubeLongPublished, PersistentStageStatuses.Skipped, reason: "YouTube publishing dry run.");
                    _logger.LogInformation("Uploaded/Skipped=Skipped");
                }
                else
                {
                    run.YouTubeVideoId = await _youTubePublishingService.UploadAsync(videoPath, script.OptimizedMetadata?.PrimaryTitle ?? script.Title, script.OptimizedMetadata?.OptimizedDescription ?? script.Description, script.OptimizedMetadata?.Tags ?? script.Tags, selectedPrivacyStatus, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(run.YouTubeVideoId) && _publishingOptions.UploadThumbnail && _youTubeThumbnailPublisher is not null && !string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
                    {
                        youtubeThumbnailUploaded = await _youTubeThumbnailPublisher.UploadThumbnailAsync(run.YouTubeVideoId, thumbnailPath, cancellationToken);
                    }
                    await SetStageStatusAsync(PipelineStageNames.YouTubeLongPublished, string.IsNullOrWhiteSpace(run.YouTubeVideoId) ? PersistentStageStatuses.Failed : PersistentStageStatuses.Succeeded, reason: string.IsNullOrWhiteSpace(run.YouTubeVideoId) ? "YouTube upload did not return a video id." : null);
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
                    await SetStageStatusAsync(PipelineStageNames.YouTubeLongPublished, string.Equals(youtubePublishResult.Mode, "DryRun", StringComparison.OrdinalIgnoreCase) || IsPublishSkip(youtubePublishResult) ? PersistentStageStatuses.Skipped : PersistentStageStatuses.Succeeded);
                    _logger.LogInformation("Uploaded/Skipped={UploadedOrSkipped}", string.Equals(youtubePublishResult.Mode, "DryRun", StringComparison.OrdinalIgnoreCase) ? "Skipped" : "Uploaded");
                }
                else if (youtubePublishResult is not null)
                {
                    publishStatus = "PublishFailed";
                    run.FailureReason = $"Publishing failed: {youtubePublishResult.Error}";
                    await SetStageStatusAsync(PipelineStageNames.YouTubeLongPublished, PersistentStageStatuses.Failed, reason: youtubePublishResult.Error ?? "YouTube publishing failed.");
                    _logger.LogWarning("YouTube publishing failed for pipeline run {PipelineRunId}: {Error}", run.Id, youtubePublishResult.Error);
                }
            }

            if (!request.PublishToYouTube || !publishingEnabled || !validationPassed)
            {
                await SetStageStatusAsync(PipelineStageNames.YouTubeLongPublished, PersistentStageStatuses.Skipped, reason: !validationPassed ? "Pre-publish validation blocked publishing." : "YouTube publishing disabled.");
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
                Status = publishStatus,
                EventId = run.EventId ?? request.EventId,
                EventType = run.EventType ?? request.EventType,
                EventTitle = run.EventTitle ?? request.EventTitle
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

            if (_eventStore is not null && !string.IsNullOrWhiteSpace(run.EventId))
            {
                var astronomyEvent = await _eventStore.GetByEventIdAsync(run.EventId, cancellationToken);
                if (astronomyEvent is not null)
                {
                    var generationMode = request.ContentType == ContentType.SpecialEventGuide ? "SeparateSpecialEventGuide" : eventDecision?.DecisionType is "GenerateBoth" or "InjectIntoDailyGuide" ? "InjectedDailyGuide" : "MentionOnly";
                    await _eventStore.AddGenerationHistoryAsync(astronomyEvent.Id, run.Id, run.RegionId ?? request.LocationName, request.Date, request.ContentType, generationMode, cancellationToken);
                }
            }

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
                        ThumbnailPath = thumbnailPath,
                        Language = context.Localization.ResolvedLanguage
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

                if (_contentPublishService is not null && request.PublishToYouTube && publishingEnabled && validationPassed && _publishingOptions.PublishShortVideo)
                {
                    var shortPublishResults = await RunStageAsync("YouTubeShortPublish", async () =>
                    {
                        var results = await _contentPublishService.PublishForPipelineRunAsync(run.Id, "short", cancellationToken);
                        var attemptedShortResults = results.Where(x => x.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase) && x.IsShort && !IsPublishSkip(x)).ToList();
                        if (attemptedShortResults.Count > 0 && attemptedShortResults.All(x => !x.Success))
                        {
                            throw new InvalidOperationException(attemptedShortResults.First().Error ?? "YouTube Shorts publishing failed.");
                        }

                        return results;
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

                    var youtubeShortResult = shortPublishResults.FirstOrDefault(x => x.Platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase) && x.IsShort);
                    if (youtubeShortResult is { Success: true })
                    {
                        await SetStageStatusAsync(PipelineStageNames.YouTubeShortPublished, string.Equals(youtubeShortResult.Mode, "DryRun", StringComparison.OrdinalIgnoreCase) || IsPublishSkip(youtubeShortResult) ? PersistentStageStatuses.Skipped : PersistentStageStatuses.Succeeded);
                    }
                    else if (youtubeShortResult is not null)
                    {
                        await SetStageStatusAsync(PipelineStageNames.YouTubeShortPublished, PersistentStageStatuses.Failed, reason: youtubeShortResult.Error ?? "YouTube Shorts publishing failed.");
                    }
                }
                else
                {
                    await SetStageStatusAsync(PipelineStageNames.YouTubeShortPublished, PersistentStageStatuses.Skipped, reason: "YouTube Shorts publishing disabled.");
                }

                if (_metaPublishService is not null
                    && _metaPublishingOptions.Enabled
                    && (_metaPublishingOptions.PublishFacebookReel || _metaPublishingOptions.PublishInstagramReel)
                    && !string.Equals(_metaPublishingOptions.Mode, "Disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var metaAsset = _metaPublishingOptions.PublishFacebookReel && _metaPublishingOptions.PublishInstagramReel
                        ? "all"
                        : _metaPublishingOptions.PublishInstagramReel ? "instagram-reel" : "facebook-reel";
                    var metaResults = await RunStageAsync("MetaReelPublish-Internal", async () =>
                    {
                        return await _metaPublishService.PublishForPipelineRunAsync(run.Id, metaAsset, cancellationToken);
                    }, continueWithFallback: true, fallback: ex => Task.FromResult<IReadOnlyList<MetaPublishResult>>([new MetaPublishResult
                    {
                        Success = false,
                        Platform = _metaPublishingOptions.PublishInstagramReel && !_metaPublishingOptions.PublishFacebookReel ? "Instagram" : "Facebook",
                        Mode = _metaPublishingOptions.Mode,
                        Error = ex.Message,
                        PublishedUtc = DateTime.UtcNow
                    }]));

                    await SetMetaPublishStageAsync(PipelineStageNames.FacebookReelPublished, "Facebook", _metaPublishingOptions.PublishFacebookReel, metaResults, SetStageStatusAsync);
                    await SetMetaPublishStageAsync(PipelineStageNames.InstagramReelPublished, "Instagram", _metaPublishingOptions.PublishInstagramReel, metaResults, SetStageStatusAsync);
                }
                else
                {
                    await SetMetaPublishStageAsync(PipelineStageNames.FacebookReelPublished, "Facebook", false, [], SetStageStatusAsync);
                    await SetMetaPublishStageAsync(PipelineStageNames.InstagramReelPublished, "Instagram", false, [], SetStageStatusAsync);
                }
            }
            else
            {
                await SetStageStatusAsync(PipelineStageNames.YouTubeShortPublished, PersistentStageStatuses.Skipped, reason: "Short video generation did not produce a publishable result.");
                await SetMetaPublishStageAsync(PipelineStageNames.FacebookReelPublished, "Facebook", false, [], SetStageStatusAsync);
                await SetMetaPublishStageAsync(PipelineStageNames.InstagramReelPublished, "Instagram", false, [], SetStageStatusAsync);
            }

            await EnsurePublishStagesHandledAsync(publishingEnabled, validationPassed);

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
            if (!string.IsNullOrWhiteSpace(run.OutputFolder))
            {
                await WriteLanguagePipelineDiagnosticsAsync(run.Language, null, null, run.OutputFolder, ex.Message, CancellationToken.None);
            }
            run.Status = PipelineRunStatus.Failed;
            run.FailureReason = ex.Message;
            run.FinishedUtc = DateTimeOffset.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Pipeline run {PipelineRunId} failed with status {Status}", run.Id, run.Status);
            throw;
        }
    }


    private static string? ResolveRegionLanguage(string? regionId, string locationName, SchedulerOptions schedulerOptions)
    {
        var normalizedRegionId = NormalizeRegionId(regionId, locationName);
        var region = schedulerOptions.Regions.Items.FirstOrDefault(item =>
            string.Equals(NormalizeRegionId(item.RegionId, item.DisplayName), normalizedRegionId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.DisplayName, locationName, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(region?.Language) ? null : region.Language;
    }

    private static bool ContainsDevanagari(string? text)
        => !string.IsNullOrWhiteSpace(text) && text.Any(character => character >= '\u0900' && character <= '\u097F');

    private static void ValidateNarrationLanguage(string narrationText, string resolvedLanguage)
    {
        if (LocalizationResolver.IsHindi(resolvedLanguage) && !ContainsDevanagari(narrationText))
        {
            throw new InvalidOperationException("Hindi narration validation failed: generated narration is not Hindi.");
        }
    }

    private static async Task WriteLanguagePipelineDiagnosticsAsync(string resolvedLanguage, string? narrationText, SeoMetadataResult? seoMetadata, string outputDirectory, string? failedStage, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var diagnostics = new
        {
            resolvedLanguage,
            voiceName = LocalizationResolver.IsHindi(resolvedLanguage) ? "hi-IN-SwaraNeural" : "en-US-JennyNeural",
            narrationLanguageDetected = ContainsDevanagari(narrationText) ? "hi" : string.IsNullOrWhiteSpace(narrationText) ? null : "en",
            shortNarrationLanguageDetected = DetectShortNarrationLanguage(outputDirectory),
            seoLanguageDetected = ContainsDevanagari((seoMetadata?.Title ?? string.Empty) + " " + (seoMetadata?.Description ?? string.Empty)) ? "hi" : seoMetadata is null ? null : "en",
            failedStage
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "language-pipeline-diagnostics.json"), JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static string? DetectShortNarrationLanguage(string outputDirectory)
    {
        var shortsDirectory = Path.Combine(outputDirectory, "shorts");
        if (!Directory.Exists(shortsDirectory))
        {
            return null;
        }

        var text = string.Join(" ", Directory.EnumerateFiles(shortsDirectory, "scene-narration-*.txt").Select(File.ReadAllText));
        return ContainsDevanagari(text) ? "hi" : string.IsNullOrWhiteSpace(text) ? null : "en";
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

    private static ThumbnailPlan BuildFallbackThumbnailPlan(OptimizedVideoMetadata optimizedMetadata, IReadOnlyCollection<string> visuals)
    {
        var fallbackVisual = visuals.FirstOrDefault(File.Exists);
        return new ThumbnailPlan
        {
            PrimaryThumbnailText = optimizedMetadata.ThumbnailTextSuggestions.FirstOrDefault() ?? "ASTRONOMY UPDATE",
            AlternateThumbnailTexts = optimizedMetadata.ThumbnailTextSuggestions,
            SelectedVisualPath = fallbackVisual,
            ThumbnailPath = fallbackVisual,
            ThumbnailVariantPaths = fallbackVisual is null ? [] : [fallbackVisual],
            LayoutType = ThumbnailLayoutType.CenteredTitleOverlay
        };
    }



    private static string? GetExpectedOutputPath(string stageName, string outputDirectory)
        => stageName switch
        {
            PipelineStageNames.Created => outputDirectory,
            PipelineStageNames.ObservationWindowCompleted => Path.Combine(outputDirectory, "observation-window.json"),
            PipelineStageNames.SkyfieldCompleted => Path.Combine(outputDirectory, "skyfield-night-plan-response.json"),
            PipelineStageNames.SceneContextCompleted => Path.Combine(outputDirectory, "scene-observation-context.json"),
            PipelineStageNames.NarrationCompleted => Path.Combine(outputDirectory, "narration-context.json"),
            PipelineStageNames.SpeechCompleted => Path.Combine(outputDirectory, "narration.mp3"),
            PipelineStageNames.StellariumCompleted => outputDirectory,
            PipelineStageNames.RenderingCompleted => Path.Combine(outputDirectory, "final-video.mp4"),
            PipelineStageNames.ThumbnailCompleted => Path.Combine(outputDirectory, "thumbnail-selection.json"),
            PipelineStageNames.SeoCompleted => Path.Combine(outputDirectory, "seo-metadata.json"),
            "BlobUpload" => Path.Combine(outputDirectory, "public-media-upload-result.json"),
            PipelineStageNames.ValidationCompleted => Path.Combine(outputDirectory, "pre-publish-validation-report.json"),
            PipelineStageNames.YouTubeLongPublished => Path.Combine(outputDirectory, "youtube-publish-result-long.json"),
            PipelineStageNames.YouTubeShortPublished => Path.Combine(outputDirectory, "youtube-publish-result-short.json"),
            PipelineStageNames.FacebookReelPublished => Path.Combine(outputDirectory, "facebook-reel-publish-result.json"),
            PipelineStageNames.InstagramReelPublished => Path.Combine(outputDirectory, "instagram-reel-publish-result.json"),
            PipelineStageNames.Completed => outputDirectory,
            PipelineStageNames.Failed => outputDirectory,
            _ => null
        };

    private static async Task SetMetaPublishStageAsync(
        string stageName,
        string platform,
        bool enabled,
        IReadOnlyList<MetaPublishResult> results,
        Func<string, string, string?, string?, Task> setStageStatusAsync)
    {
        if (!enabled)
        {
            await setStageStatusAsync(stageName, PersistentStageStatuses.Skipped, null, $"{platform} Reel publishing disabled.");
            return;
        }

        var result = results.FirstOrDefault(x => x.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase));
        if (result is { Success: true })
        {
            await setStageStatusAsync(stageName, PersistentStageStatuses.Succeeded, null, null);
            return;
        }

        await setStageStatusAsync(stageName, PersistentStageStatuses.Failed, null, result?.Error ?? $"{platform} Reel publishing did not produce a successful result.");
    }

    private static bool IsEnabledPublishStage(
        string stageName,
        bool publishingEnabled,
        bool validationPassed,
        bool publishToYouTube,
        bool publishShortVideo,
        MetaPublishingOptions metaPublishingOptions)
        => stageName switch
        {
            PipelineStageNames.YouTubeLongPublished => publishingEnabled && validationPassed && publishToYouTube,
            PipelineStageNames.YouTubeShortPublished => publishingEnabled && validationPassed && publishToYouTube && publishShortVideo,
            PipelineStageNames.FacebookReelPublished => metaPublishingOptions.Enabled && metaPublishingOptions.PublishFacebookReel && !string.Equals(metaPublishingOptions.Mode, "Disabled", StringComparison.OrdinalIgnoreCase),
            PipelineStageNames.InstagramReelPublished => metaPublishingOptions.Enabled && metaPublishingOptions.PublishInstagramReel && !string.Equals(metaPublishingOptions.Mode, "Disabled", StringComparison.OrdinalIgnoreCase),
            _ => false
        };



    private async Task<EventContentDecision> ApplyDailyGuideEventDecisionAsync(RunPipelineRequest request, AstronomyContext context, string outputDirectory, CancellationToken cancellationToken)
    {
        var regionId = NormalizeRegionId(request.RegionId, request.LocationName) ?? request.LocationName;
        var decision = await _eventDecisionService!.DecideAsync(regionId, request.Date, cancellationToken);
        if (decision.DecisionType is not ("InjectIntoDailyGuide" or "GenerateBoth") || decision.InjectedEvents.Count == 0)
        {
            await WriteDailyGuideEventInjectionDiagnosticsAsync(outputDirectory, decision, null, [], context.SceneObservationContexts, cancellationToken);
            return decision;
        }

        var selected = decision.InjectedEvents[0];
        var eventObject = ResolveSpecialEventObjectName(selected.Title, selected.EventType);
        var removed = new List<string>();
        var scenes = context.SceneObservationContexts.ToList();
        var overview = scenes.FirstOrDefault(s => s.SceneType.Equals("Overview", StringComparison.OrdinalIgnoreCase)) ?? scenes.FirstOrDefault();
        var closing = scenes.LastOrDefault(s => s.SceneId.Equals("closing", StringComparison.OrdinalIgnoreCase) || s.SceneType.Equals("Closing", StringComparison.OrdinalIgnoreCase) || s.SceneType.Equals("Tips", StringComparison.OrdinalIgnoreCase));
        var objectScenes = scenes
            .Where(s => !ReferenceEquals(s, overview) && !ReferenceEquals(s, closing))
            .Where(s => !s.ObjectName.Equals(eventObject, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();
        removed.AddRange(scenes.Where(s => !ReferenceEquals(s, overview) && !ReferenceEquals(s, closing) && (s.ObjectName.Equals(eventObject, StringComparison.OrdinalIgnoreCase) || !objectScenes.Contains(s))).Select(s => s.ObjectName));

        var baseLocal = overview?.LocalObservationTime ?? request.Date.ToDateTime(new TimeOnly(21, 0));
        var timezone = ResolveTimeZoneOrUtc(context.TimeZone);
        var eventLocal = selected.PeakUtc?.UtcDateTime ?? selected.StartUtc.UtcDateTime;
        try { eventLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(eventLocal, DateTimeKind.Utc), timezone); } catch { }
        var highlight = BuildEventScene("special-event-highlight", selected.Title, "SpecialEventHighlight", eventObject, eventLocal == default ? baseLocal.AddMinutes(20) : eventLocal, timezone, context, $"Prioritize {selected.Title}: {selected.Description}");

        var final = new List<SceneObservationContext>();
        if (overview is not null) final.Add(CopyScene(overview, 1));
        final.Add(CopyScene(highlight, final.Count + 1));
        foreach (var scene in objectScenes.Take(3)) final.Add(CopyScene(scene, final.Count + 1));
        if (closing is not null) final.Add(CopyScene(closing, final.Count + 1));
        context.SceneObservationContexts = final;
        context.SpecialEvent = new SpecialEventContext { EventId = selected.EventId, EventType = selected.EventType, EventTitle = LocalizeEventTitle(selected.Title, context.Localization.ResolvedLanguage), EventDescription = LocalizeEventDescription(selected, context.Localization.ResolvedLanguage), ContentOpportunityScore = selected.ContentOpportunityScore };
        context.Events.Insert(0, new AstronomyEventModel { Category = selected.EventType, ObjectName = eventObject, VisibilityWindow = "Special event window", Direction = ResolveSpecialEventDirection(selected.EventType), ObservationTool = ResolveSpecialEventTool(selected.EventType), Details = context.SpecialEvent.EventDescription, Score = selected.ContentOpportunityScore });
        context.VisualIdeas.Add(new VisualIdeaModel { Title = "daily-guide-event-injection", Description = JsonSerializer.Serialize(new { decision.DecisionType, selectedEvent = selected, injectedSceneIndex = 2, removedOrSkippedObjects = removed, finalSceneOrder = final.Select(s => new { s.SceneIndex, s.SceneId, s.SceneType, s.ObjectName }) }, new JsonSerializerOptions { WriteIndented = true }) });
        await WriteDailyGuideEventInjectionDiagnosticsAsync(outputDirectory, decision, selected, removed, final, cancellationToken);
        return decision;
    }

    private static SceneObservationContext CopyScene(SceneObservationContext source, int index) => new()
    {
        SceneId = source.SceneId,
        SceneTitle = source.SceneTitle,
        SceneType = source.SceneType,
        SceneIndex = index,
        ObjectName = source.ObjectName,
        ObjectType = source.ObjectType,
        PrimaryObject = source.PrimaryObject,
        IncludePolarisOrientation = source.IncludePolarisOrientation,
        LocalObservationTime = source.LocalObservationTime,
        UtcObservationTime = source.UtcObservationTime,
        Timezone = source.Timezone,
        AltitudeDegrees = source.AltitudeDegrees,
        AzimuthDegrees = source.AzimuthDegrees,
        DirectionLabel = source.DirectionLabel,
        IsVisible = source.IsVisible,
        VisibilityReason = source.VisibilityReason,
        RecommendedTool = source.RecommendedTool,
        NarrationFocus = source.NarrationFocus,
        Latitude = source.Latitude,
        Longitude = source.Longitude,
        LocationName = source.LocationName
    };

    private static async Task WriteDailyGuideEventInjectionDiagnosticsAsync(string outputDirectory, EventContentDecision decision, AstronomyEvent? selectedEvent, IReadOnlyCollection<string> removedOrSkippedObjects, IReadOnlyCollection<SceneObservationContext> scenes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "daily-guide-event-injection.json"), JsonSerializer.Serialize(new
        {
            decision.DecisionType,
            selectedEvent,
            injectedSceneIndex = selectedEvent is null ? (int?)null : 2,
            removedOrSkippedObjects,
            finalSceneOrder = scenes.Select(s => new { s.SceneIndex, s.SceneId, s.SceneType, s.ObjectName })
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static string LocalizeEventTitle(string title, string language)
        => language.Equals("hi", StringComparison.OrdinalIgnoreCase) ? title.Replace("Full Moon", "पूर्णिमा (Full Moon)", StringComparison.OrdinalIgnoreCase).Replace("New Moon", "अमावस्या (New Moon)", StringComparison.OrdinalIgnoreCase).Replace("meteor shower", "उल्का वर्षा (meteor shower)", StringComparison.OrdinalIgnoreCase) : title;

    private static string LocalizeEventDescription(AstronomyEvent astronomyEvent, string language)
        => language.Equals("hi", StringComparison.OrdinalIgnoreCase)
            ? $"आज रात {LocalizeEventTitle(astronomyEvent.Title, language)} देखने का शानदार मौका है। {astronomyEvent.Description}"
            : astronomyEvent.Description;

    private static void ApplySpecialEventRequestContext(RunPipelineRequest request, AstronomyContext context)
    {
        if (request.ContentType != ContentType.SpecialEventGuide)
            return;

        var eventTitle = string.IsNullOrWhiteSpace(request.EventTitle) ? "Astronomy special event" : request.EventTitle.Trim();
        var eventType = string.IsNullOrWhiteSpace(request.EventType) ? "special_event" : request.EventType.Trim();
        var eventDescription = string.IsNullOrWhiteSpace(request.EventDescription) ? "A high-opportunity astronomy event selected for a dedicated video." : request.EventDescription.Trim();
        context.SpecialEvent = new SpecialEventContext
        {
            EventId = request.EventId ?? string.Empty,
            EventType = eventType,
            EventTitle = eventTitle,
            EventDescription = eventDescription,
            ContentOpportunityScore = 1.0
        };

        var eventObject = ResolveSpecialEventObjectName(eventTitle, eventType);
        context.Events.RemoveAll(e => e.Category.Equals("Overview", StringComparison.OrdinalIgnoreCase) && e.ObjectName.Equals("Night sky", StringComparison.OrdinalIgnoreCase));
        context.Events.Insert(0, new AstronomyEventModel
        {
            Category = eventType,
            ObjectName = eventObject,
            VisibilityWindow = "Event window / best local visibility",
            Direction = ResolveSpecialEventDirection(eventType),
            ObservationTool = ResolveSpecialEventTool(eventType),
            Details = eventDescription,
            Score = 1.0
        });

        var baseLocal = context.SceneObservationContexts.FirstOrDefault()?.LocalObservationTime ?? context.Date.ToDateTime(new TimeOnly(21, 0));
        var timezone = ResolveTimeZoneOrUtc(context.TimeZone);
        context.SceneObservationContexts = BuildSpecialEventScenes(context, eventTitle, eventType, eventObject, baseLocal, timezone);
        context.VisualIdeas.Add(new VisualIdeaModel { Title = "special-event-context", Description = JsonSerializer.Serialize(context.SpecialEvent, new JsonSerializerOptions { WriteIndented = true }) });
    }

    private static List<SceneObservationContext> BuildSpecialEventScenes(AstronomyContext context, string eventTitle, string eventType, string eventObject, DateTime baseLocal, TimeZoneInfo timezone)
    {
        var lower = eventType.ToLowerInvariant();
        var peakFocus = "Best viewing time, direction, and what viewers should notice.";
        if (lower.Contains("meteor"))
            peakFocus = "Meteor radiant direction, moonlight impact, and patient scanning tips.";
        else if (lower.Contains("eclipse"))
            peakFocus = "Eclipse phase timing, safe viewing requirements, and what changes to watch.";
        else if (lower.Contains("conjunction"))
            peakFocus = "Planet alignment, separation impression, and framing both objects together.";
        else if (lower.Contains("moon") || eventTitle.Contains("Moon", StringComparison.OrdinalIgnoreCase))
            peakFocus = "Moon phase appearance, rise direction, and cinematic close-up framing.";

        return
        [
            BuildEventScene("event-hook", eventTitle, "EventFocus", eventObject, baseLocal, timezone, context, "Cinematic opening focused on the event and why it matters."),
            BuildEventScene("event-peak", $"{eventTitle} best viewing", "Object", eventObject, baseLocal.AddMinutes(25), timezone, context, peakFocus),
            BuildEventScene("event-how-to-watch", "How to watch", "Tips", eventObject, baseLocal.AddMinutes(50), timezone, context, "Beginner observation tips and safe observing guidance."),
            BuildEventScene("event-rarity", "Why this event is special", "EventFocus", eventObject, baseLocal.AddMinutes(75), timezone, context, "Rarity, urgency, and what makes this event worth a dedicated video."),
            BuildEventScene("closing", "Viewing reminder", "Closing", eventObject, baseLocal.AddMinutes(95), timezone, context, "Concise reminder to check weather, horizon, and local timing.")
        ];
    }

    private static SceneObservationContext BuildEventScene(string id, string title, string sceneType, string objectName, DateTime local, TimeZoneInfo timezone, AstronomyContext context, string narrationFocus)
        => new()
        {
            SceneId = id,
            SceneTitle = title,
            SceneType = sceneType,
            ObjectName = objectName,
            ObjectType = objectName.Equals("Moon", StringComparison.OrdinalIgnoreCase) ? "Moon" : "Event",
            LocalObservationTime = local,
            UtcObservationTime = new DateTimeOffset(local, timezone.GetUtcOffset(local)).ToUniversalTime(),
            Timezone = context.TimeZone,
            DirectionLabel = ResolveSpecialEventDirection(context.SpecialEvent?.EventType ?? string.Empty),
            AltitudeDegrees = 45,
            AzimuthDegrees = 180,
            IsVisible = true,
            VisibilityReason = title,
            RecommendedTool = ResolveSpecialEventTool(context.SpecialEvent?.EventType ?? string.Empty),
            NarrationFocus = narrationFocus,
            Latitude = context.Latitude,
            Longitude = context.Longitude,
            LocationName = context.LocationName
        };

    private static string ResolveSpecialEventObjectName(string title, string eventType)
    {
        if (title.Contains("Moon", StringComparison.OrdinalIgnoreCase) || eventType.Contains("moon", StringComparison.OrdinalIgnoreCase)) return "Moon";
        if (eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return title.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Meteor radiant";
        if (eventType.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return title.Contains("Moon", StringComparison.OrdinalIgnoreCase) ? "Moon" : "Sun";
        return title;
    }

    private static string ResolveSpecialEventDirection(string eventType)
        => eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase) ? "Radiant direction / darkest sky" : "Event-specific horizon";

    private static string ResolveSpecialEventTool(string eventType)
        => eventType.Contains("eclipse", StringComparison.OrdinalIgnoreCase) ? "Proper eclipse safety gear when solar; naked eye for lunar" : "Naked eye / binoculars";

    private static TimeZoneInfo ResolveTimeZoneOrUtc(string timeZone)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
        catch { return TimeZoneInfo.Utc; }
    }

    private static async Task WriteSpecialEventDiagnosticsAsync(RunPipelineRequest request, AstronomyContext context, string outputDirectory, SeoMetadataResult seoMetadata, CancellationToken cancellationToken)
    {
        if (request.ContentType != ContentType.SpecialEventGuide)
            return;

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "special-event-context.json"), JsonSerializer.Serialize(context.SpecialEvent, jsonOptions), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "special-event-visual-plan.json"), JsonSerializer.Serialize(context.SceneObservationContexts.Select(s => new { s.SceneId, s.SceneTitle, s.SceneType, s.ObjectName, s.DirectionLabel, s.NarrationFocus, s.RecommendedTool }), jsonOptions), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "special-event-seo.json"), JsonSerializer.Serialize(seoMetadata, jsonOptions), cancellationToken);
    }


    private static async Task WriteThumbnailSelectionAsync(ThumbnailPlan thumbnailPlan, string outputDirectory, CancellationToken cancellationToken)
    {
        var payload = new
        {
            thumbnailPlan.PrimaryThumbnailText,
            thumbnailPlan.AlternateThumbnailTexts,
            thumbnailPlan.SelectedVisualPath,
            thumbnailPlan.ThumbnailPath,
            thumbnailPlan.ThumbnailVariantPaths,
            LayoutType = thumbnailPlan.LayoutType.ToString()
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "thumbnail-selection.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static async Task WritePrimaryContextArtifactsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        WriteDiagnosticFromVisualIdea(context, outputDirectory, "observation-window");
        WriteDiagnosticFromVisualIdea(context, outputDirectory, "skyfield-night-plan-response");
        WriteDiagnosticFromVisualIdea(context, outputDirectory, "scene-observation-context");
        WriteDiagnosticFromVisualIdea(context, outputDirectory, "narration-context");
        await WriteLocalizationContextAsync(context.Localization, outputDirectory, cancellationToken);
    }


    private static async Task WriteLocalizationContextAsync(LocalizationContext localization, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var payload = new
        {
            requestedLanguage = localization.RequestedLanguage,
            regionLanguage = localization.RegionLanguage,
            resolvedLanguage = localization.ResolvedLanguage,
            fallbackUsed = localization.FallbackUsed
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "localization-context.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
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

    private static async Task WriteContentExpansionReportAsync(AstronomyContext context, RunPipelineRequest request, string outputDirectory, int estimatedDurationSeconds, CancellationToken cancellationToken)
    {
        var selectedObjects = context.SceneObservationContexts
            .Where(scene => !string.IsNullOrWhiteSpace(scene.ObjectName) && !scene.ObjectName.Equals("Sky", StringComparison.OrdinalIgnoreCase))
            .Select(scene => new
            {
                scene.SceneId,
                scene.SceneType,
                scene.ObjectName,
                scene.ObjectType,
                scene.AltitudeDegrees,
                scene.DirectionLabel,
                scene.VisibilityReason
            })
            .DistinctBy(x => x.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selectedNames = selectedObjects.Select(x => x.ObjectName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var skippedObjects = context.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.ObjectName) && !selectedNames.Contains(e.ObjectName))
            .Select(e => new { e.ObjectName, reason = "Not selected after visibility, ranking, duplicate, or maximum-count constraints." })
            .ToList();

        var report = new
        {
            selectedObjects,
            skippedObjects,
            reason = "Dynamic production expansion selected visible, beginner-friendly astronomy targets while avoiding duplicates and overcrowding.",
            estimatedVideoMinutes = Math.Round(estimatedDurationSeconds / 60d, 2),
            finalVideoMinutes = Math.Round(estimatedDurationSeconds / 60d, 2),
            eventInjected = context.SpecialEvent is not null || request.EventId is not null || context.Events.Any(e => e.Score >= 0.85),
            specialEventGuideGenerated = request.ContentType == ContentType.SpecialEventGuide
        };

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "content-expansion-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
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

    private static ScriptResult CopyScriptWithSceneSections(ScriptResult script, IReadOnlyDictionary<string, string> alignedSectionsBySceneId)
        => new()
        {
            Prompt = script.Prompt,
            Title = script.Title,
            Description = script.Description,
            ScriptBody = script.ScriptBody,
            Tags = script.Tags,
            EstimatedDurationSeconds = script.EstimatedDurationSeconds,
            OptimizedMetadata = script.OptimizedMetadata,
            SceneScriptSections = new SceneScriptSections
            {
                SectionsBySceneId = new Dictionary<string, string>(alignedSectionsBySceneId, StringComparer.OrdinalIgnoreCase)
            }
        };

    private static string BuildFallbackSceneNarration(SceneObservationContext scene, string resolvedLanguage)
    {
        if (LocalizationResolver.IsHindi(resolvedLanguage))
        {
            return $"इस दृश्य में {scene.ObjectName} पर ध्यान दें। {FormatHindiObservationDetails(scene)}".Trim();
        }

        var title = string.IsNullOrWhiteSpace(scene.SceneTitle) ? scene.ObjectName : scene.SceneTitle;
        var direction = string.IsNullOrWhiteSpace(scene.DirectionLabel) ? "the best visible part of the sky" : $"the {scene.DirectionLabel} sky";
        var tool = string.IsNullOrWhiteSpace(scene.RecommendedTool) ? "your eyes" : scene.RecommendedTool;
        var focus = string.IsNullOrWhiteSpace(scene.NarrationFocus)
            ? "Take a moment to connect this view with the rest of tonight's observing plan."
            : scene.NarrationFocus;

        return $"{title}: look for {scene.ObjectName} toward {direction} using {tool}. {focus}";
    }

    private static string FormatHindiObservationDetails(SceneObservationContext scene)
    {
        var direction = string.IsNullOrWhiteSpace(scene.DirectionLabel) ? "आसमान में" : $"{scene.DirectionLabel} दिशा में";
        var tool = string.IsNullOrWhiteSpace(scene.RecommendedTool) ? "अपनी आंखों" : scene.RecommendedTool;
        var focus = string.IsNullOrWhiteSpace(scene.NarrationFocus)
            ? "धीरे-धीरे देखें और इस दृश्य को आज रात की बाकी observing plan से जोड़ें।"
            : scene.NarrationFocus;

        return $"{direction} {tool} से देखें। {focus}";
    }

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

    private static OptimizedVideoMetadata ApplyGrowthMetadata(OptimizedVideoMetadata source, GrowthOptions growthOptions, string language, string? region)
    {
        var description = GrowthMetadataComposer.ApplyGrowthBlock(source.OptimizedDescription, growthOptions, new GrowthMetadataInput
        {
            Platform = "YouTube",
            Language = language,
            Region = region,
            IsShortForm = false
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
    public static string BuildOutputDirectory(string workingDirectory, ContentType contentType, DateOnly date, string? regionId, string locationName, Guid runId)
        => Path.Combine(workingDirectory, contentType.ToString(), date.ToString("yyyy-MM-dd"), NormalizeRegionId(regionId, locationName), runId.ToString("N"));

    private static string NormalizeRegionId(string? regionId, string locationName)
    {
        if (!string.IsNullOrWhiteSpace(regionId))
            return Slugify(regionId);

        return Slugify(locationName);
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-') is { Length: > 0 } slug ? slug : "default-region";
    }

}
