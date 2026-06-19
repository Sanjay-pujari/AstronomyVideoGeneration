using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContentPlanExecutionMode
{
    Normal,
    RetryFailed,
    RecoverRunning,
    RebuildOutputs,
    FullRebuild,
    RerunPhase
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DependencyExpansionMode
{
    None,
    ReadOnly,
    Rebuild
}

public sealed record BatchGenerateFromPlansRequest(
    int Year,
    string RegionId,
    string Language = "en",
    int MaxPlans = 1,
    bool OnlyHighPriority = false,
    bool DryRun = true,
    IReadOnlyList<string>? PlanTitles = null,
    bool UseProductionPipeline = false,
    bool OverwriteExisting = false,
    int? StartPhaseNo = null,
    int? EndPhaseNo = null,
    bool RetryFailedOnly = false,
    bool AllowFailedPlanRetry = false,
    bool AllowRunningPlanRecovery = false,
    Guid? PlanId = null,
    int? RunningPlanRecoveryStaleAfterMinutes = null,
    ContentPlanExecutionMode ExecutionMode = ContentPlanExecutionMode.Normal,
    bool AllowCompletedPlanRerun = false,
    bool ArchivePreviousRun = false,
    bool RebuildIntelligence = false,
    bool EnableSceneVariants = false,
    bool EnableSceneAssetsV3 = false,
    bool? EnableAccurateSkyGuideV2 = null,
    bool EnableSubtitles = false,
    bool PublishApproved = false,
    DependencyExpansionMode DependencyExpansionMode = DependencyExpansionMode.ReadOnly);

public sealed record BatchGenerateFromPlansResponse(
    bool Success,
    bool DryRun,
    int RequestedTitleCount,
    int SelectedPlanCount,
    int MaxPlans,
    IReadOnlyList<BatchGenerateFromPlansSelectedPlan> SelectedPlans,
    IReadOnlyList<object> Steps,
    IReadOnlyList<BatchGenerateFromPlansWarning> Warnings,
    IReadOnlyList<string> Errors,
    int AssetPlansGenerated = 0,
    int AssetJobsCreated = 0,
    int VisualAssetsGenerated = 0,
    int SceneVideosRendered = 0,
    int ShortVideosGenerated = 0,
    int LongVideosGenerated = 0,
    int FailedPlans = 0,
    IReadOnlyList<object>? Results = null,
    bool UseProductionPipeline = false,
    bool UsedPlaceholderVisuals = true,
    Guid? PlanId = null,
    string? Title = null,
    string? OutputRoot = null,
    bool QuestionEngineCompleted = false,
    bool ShortScenesGenerated = false,
    bool LongScenesGenerated = false,
    bool HeroGenerated = false,
    bool ThumbnailsGenerated = false,
    bool ShortNarrationGenerated = false,
    bool LongNarrationGenerated = false,
    bool ShortTtsGenerated = false,
    bool LongTtsGenerated = false,
    bool ShortVideoGenerated = false,
    bool LongVideoGenerated = false,
    string? FinalShortVideoPath = null,
    string? FinalLongVideoPath = null,
    object? ProductionPipelineRequest = null,
    IReadOnlyList<string>? PlannedSteps = null,
    int? LastCompletedPhaseNo = null,
    int? LastFailedPhaseNo = null,
    ContentPlanExecutionMode ExecutionMode = ContentPlanExecutionMode.Normal,
    bool CompletedPlanRerun = false,
    bool PreviousOutputArchived = false,
    string? ArchivePath = null,
    IReadOnlyList<string>? DeletedOutputFolders = null,
    int? StartPhaseNo = null,
    int? EndPhaseNo = null,
    IReadOnlyList<RequestedOutputCompletion>? RequestedOutputCompletion = null,
    bool PartialPhaseExecution = false,
    int? RequestedStartPhase = null,
    int? RequestedEndPhase = null,
    int? ExpandedStartPhase = null,
    int? ExpandedEndPhase = null,
    bool PartialPhaseSuccess = false,
    bool DependencyExpansionApplied = false,
    Guid? RequestedPlanId = null,
    Guid? SelectedPlanId = null,
    bool ManualPlanExecution = false,
    bool? AutoGenerateAllowed = null,
    bool AutoGenerateAllowedIgnoredForManualRun = false,
    string? SelectionMode = null,
    bool PublishGateChecked = false,
    bool PublishApproved = false,
    bool Phase19ReviewApproved = false);

public sealed record BatchGenerateFromPlansSelectedPlan(
    Guid ContentGenerationPlanId,
    string Title,
    string ContentCategoryCode,
    string? PlannedFormat,
    string RegionId,
    string Language,
    DateTimeOffset? ScheduledUtc,
    string Status,
    string PlanStatus,
    int Priority,
    decimal? PriorityScore,
    string? SourceExternalEventId = null,
    string? AstronomyEventTitle = null,
    string? AstronomyEventShortTitle = null,
    string? AstronomyEventExternalEventId = null);

public sealed record BatchGenerateFromPlansWarning(
    string RequestedTitle,
    bool Matched,
    bool Selected,
    string Reason);

public sealed record BatchGenerateFromPlansStepResult(
    string StepName,
    string Status,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DurationMs,
    string? Message,
    string? ErrorMessage,
    object? Result);

public sealed record PlansReadyForGenerationResponse(
    int Year,
    string RegionId,
    string Language,
    int TotalPlansFound,
    IReadOnlyList<PlanReadyForGenerationItem> Plans);

public sealed record PlanReadyForGenerationItem(
    Guid PlanId,
    string Title,
    string? SourceExternalEventId,
    string Status,
    string PlanStatus,
    string Priority,
    decimal PriorityScore,
    string ContentCategoryCode,
    string? PlannedFormat,
    DateTimeOffset? ScheduledUtc,
    string? RequestedOutputTypesJson,
    string? AstronomyEventTitle,
    string? AstronomyEventShortTitle,
    string? AstronomyEventType,
    string? AstronomyEventVerificationStatus,
    bool? AstronomyEventAutoGenerateAllowed,
    string? AstronomyEventContentStrategy);

public interface IContentPlanBatchGenerationService
{
    Task<BatchGenerateFromPlansResponse> GenerateFromPlansAsync(BatchGenerateFromPlansRequest request, CancellationToken cancellationToken);
}

public interface IContentPlanGenerationReadinessService
{
    Task<PlansReadyForGenerationResponse> GetPlansReadyForGenerationAsync(
        int year,
        string regionId,
        string language,
        bool onlyHighPriority,
        int? maxPlans,
        CancellationToken cancellationToken);
}

public sealed record ContentPlanProductionPipelineRequest(
    Guid PlanId,
    string Category,
    string Title,
    string ShortTitle,
    string EventType,
    string RegionId,
    string Language,
    IReadOnlyList<string> PrimaryObjects,
    IReadOnlyList<string> SecondaryObjects,
    DateTimeOffset? StartUtc,
    DateTimeOffset? PeakUtc,
    DateTimeOffset? EndUtc,
    DateTimeOffset? ScheduledUtc,
    string? SourceExternalEventId,
    string? PlannedFormat,
    IReadOnlyList<string> RequestedOutputs,
    decimal? VisibilityScore,
    decimal? RarityScore,
    decimal? AudienceInterestScore,
    decimal? ContentOpportunityScore,
    string? VerificationStatus,
    string? VerificationSource,
    string? ContentStrategy,
    string? LocalPeakTime,
    string? SkyDirectionHint,
    string? VisibilityRegion,
    string? MoonInterference,
    string? BestViewingWindowLocal,
    string? RadiantVisibilityNote,
    decimal? MoonIlluminationPercent,
    string? RecommendedPublishWindow,
    IReadOnlyList<string> RecommendedContentTypes,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> SourceNotes,
    string? TimeZone = null,
    decimal? AngularSeparationDegrees = null);

public sealed record ContentPlanProductionExecutionRequest(
    Guid ContentGenerationPlanId,
    bool DryRun,
    bool OverwriteExisting = false,
    int? StartPhaseNo = null,
    int? EndPhaseNo = null,
    bool RetryFailedOnly = false,
    ContentPlanExecutionMode ExecutionMode = ContentPlanExecutionMode.Normal,
    bool AllowCompletedPlanRerun = false,
    bool ArchivePreviousRun = false,
    bool RebuildIntelligence = false,
    bool EnableSceneVariants = false,
    int? RequestedStartPhaseNo = null,
    int? RequestedEndPhaseNo = null,
    bool EnableSceneAssetsV3 = false,
    bool? EnableAccurateSkyGuideV2 = null,
    bool EnableSubtitles = false,
    bool PublishApproved = false,
    DependencyExpansionMode DependencyExpansionMode = DependencyExpansionMode.ReadOnly);

public sealed record ProductionExecutionContext(
    Guid ContentGenerationPlanId,
    Guid AstronomyEventIntelligenceId,
    string RegionId,
    string Language,
    int Year,
    string EventType,
    string ContentCategory,
    string PlanRoot,
    string QuestionRoot,
    string SceneRoot,
    string HeroRoot,
    string ThumbnailRoot,
    string NarrationRoot,
    string TtsRoot,
    string VideoAssemblyRoot,
    string ValidationRoot,
    ProductionEventIntelligence ProductionEventIntelligence,
    IMediaEventStrategy MediaEventStrategy);

public sealed record ProductionPipelineExecutionContext(
    bool UseProductionPipeline,
    Guid? ContentGenerationPlanId,
    Guid? AstronomyEventIntelligenceId,
    string? SourceExternalEventId,
    bool IsDbApprovedPlanExecution,
    bool ContentGenerationPlanExists = false,
    string? ContentGenerationPlanStatus = null,
    string? ContentGenerationPlanPlanStatus = null,
    bool AstronomyEventIntelligenceExists = false,
    bool AutoGenerateAllowed = false,
    string? VerificationStatus = null,
    string? ContentStrategy = null,
    string? RegionId = null,
    string? Language = null,
    IReadOnlyList<string>? RequestedOutputs = null,
    string? Category = null,
    string? PlannedFormat = null,
    int? Year = null,
    string? EventType = null,
    string? PlanRoot = null,
    string? QuestionRoot = null,
    string? SceneRoot = null,
    string? HeroRoot = null,
    string? ThumbnailRoot = null,
    string? NarrationRoot = null,
    string? TtsRoot = null,
    string? VideoAssemblyRoot = null,
    string? ValidationRoot = null,
    ProductionEventIntelligence? ProductionEventIntelligence = null,
    IMediaEventStrategy? MediaEventStrategy = null,
    bool EnableSubtitles = false,
    ProductionExecutionContext? ProductionExecutionContext = null);

public sealed record ProductionPipelineRequest(
    ContentPlanProductionPipelineRequest Request,
    Guid AstronomyEventIntelligenceId,
    string OutputRoot,
    bool DryRun,
    bool OverwriteExisting = false,
    ProductionPipelineExecutionContext? ExecutionContext = null,
    int? StartPhaseNo = null,
    int? EndPhaseNo = null,
    bool RetryFailedOnly = false,
    ContentPlanExecutionMode ExecutionMode = ContentPlanExecutionMode.Normal,
    bool AllowCompletedPlanRerun = false,
    bool ArchivePreviousRun = false,
    bool RebuildIntelligence = false,
    bool EnableSceneVariants = false,
    int? RequestedStartPhaseNo = null,
    int? RequestedEndPhaseNo = null,
    bool EnableSceneAssetsV3 = false,
    bool? EnableAccurateSkyGuideV2 = null,
    bool EnableSubtitles = false,
    bool PublishApproved = false,
    DependencyExpansionMode DependencyExpansionMode = DependencyExpansionMode.ReadOnly);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductionPhaseStatus
{
    Pending,
    Running,
    Succeeded,
    Skipped,
    Failed
}

public sealed record ProductionPhaseContext(
    ProductionPipelineRequest PipelineRequest,
    ContentPlanProductionPipelineRequest Request,
    Guid AstronomyEventIntelligenceId,
    string EventId,
    string OutputRoot,
    ProductionPipelineExecutionContext ExecutionContext,
    ProductionEventIntelligence ProductionEventIntelligence,
    IMediaEventStrategy MediaEventStrategy,
    bool DryRun,
    bool OverwriteExisting,
    int StartPhaseNo,
    int EndPhaseNo,
    bool RetryFailedOnly,
    ContentPlanExecutionMode ExecutionMode = ContentPlanExecutionMode.Normal,
    IReadOnlyList<string>? DeletedFilesDueToOverwrite = null);

public sealed record ProductionPhaseResult(
    int PhaseNo,
    string PhaseName,
    ProductionPhaseStatus Status,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DurationMs,
    IReadOnlyList<string> InputFiles,
    IReadOnlyList<string> OutputFiles,
    string? ValidationReportPath,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool CanRetry,
    string? Reason = null);

public interface IProductionPhase
{
    int PhaseNo { get; }
    string PhaseName { get; }
    Task<ProductionPhaseResult> ExecuteAsync(ProductionPhaseContext context, CancellationToken cancellationToken);
}

public interface IProductionPhaseRunner
{
    Task<ProductionPipelineExecutionResult> RunAsync(ProductionPipelineRequest request, CancellationToken cancellationToken);
}

public sealed record ProductionPipelineExecutionResult(
    bool Success,
    bool DryRun,
    bool QuestionEngineCompleted,
    bool ShortScenesGenerated,
    bool LongScenesGenerated,
    bool HeroGenerated,
    bool ThumbnailsGenerated,
    bool ShortNarrationGenerated,
    bool LongNarrationGenerated,
    bool ShortTtsGenerated,
    bool LongTtsGenerated,
    bool ShortVideoGenerated,
    bool LongVideoGenerated,
    string FinalShortVideoPath,
    string FinalLongVideoPath,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<ProductionPhaseResult>? PhaseResults = null,
    int? LastCompletedPhaseNo = null,
    int? LastFailedPhaseNo = null,
    ContentPlanExecutionMode ExecutionMode = ContentPlanExecutionMode.Normal,
    bool CompletedPlanRerun = false,
    bool PreviousOutputArchived = false,
    string? ArchivePath = null,
    IReadOnlyList<string>? DeletedOutputFolders = null,
    int? StartPhaseNo = null,
    int? EndPhaseNo = null,
    IReadOnlyList<RequestedOutputCompletion>? RequestedOutputCompletion = null);

public sealed record ContentPlanProductionExecutionResult(
    bool Success,
    bool DryRun,
    bool UseProductionPipeline,
    bool UsedPlaceholderVisuals,
    int SelectedPlanCount,
    Guid PlanId,
    string Title,
    string OutputRoot,
    bool QuestionEngineCompleted,
    bool ShortScenesGenerated,
    bool LongScenesGenerated,
    bool HeroGenerated,
    bool ThumbnailsGenerated,
    bool ShortNarrationGenerated,
    bool LongNarrationGenerated,
    bool ShortTtsGenerated,
    bool LongTtsGenerated,
    bool ShortVideoGenerated,
    bool LongVideoGenerated,
    string FinalShortVideoPath,
    string FinalLongVideoPath,
    ContentPlanProductionPipelineRequest ProductionPipelineRequest,
    IReadOnlyList<string> PlannedProductionSteps,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<ProductionPhaseResult>? PhaseResults = null,
    int? LastCompletedPhaseNo = null,
    int? LastFailedPhaseNo = null,
    ContentPlanExecutionMode ExecutionMode = ContentPlanExecutionMode.Normal,
    bool CompletedPlanRerun = false,
    bool PreviousOutputArchived = false,
    string? ArchivePath = null,
    IReadOnlyList<string>? DeletedOutputFolders = null,
    int? StartPhaseNo = null,
    int? EndPhaseNo = null,
    IReadOnlyList<RequestedOutputCompletion>? RequestedOutputCompletion = null,
    bool PartialPhaseExecution = false,
    int? RequestedStartPhase = null,
    int? RequestedEndPhase = null,
    int? ExpandedStartPhase = null,
    int? ExpandedEndPhase = null,
    bool PartialPhaseSuccess = false,
    bool DependencyExpansionApplied = false,
    Guid? RequestedPlanId = null,
    Guid? SelectedPlanId = null,
    bool ManualPlanExecution = false,
    bool? AutoGenerateAllowed = null,
    bool AutoGenerateAllowedIgnoredForManualRun = false,
    string? SelectionMode = null,
    bool PublishGateChecked = false,
    bool PublishApproved = false,
    bool Phase19ReviewApproved = false);


public sealed record RequestedOutputCompletion(
    string OutputType,
    bool Requested,
    string Status,
    IReadOnlyList<int> RequiredPhases,
    IReadOnlyList<int> SucceededPhases,
    IReadOnlyList<int> FailedPhases,
    IReadOnlyList<int> SkippedPhases);

public interface IContentPlanProductionRequestMapper
{
    ContentPlanProductionPipelineRequest Map(ContentGenerationPlan plan, AstronomyEventIntelligence intelligence);
}

public interface IProductionPipelineExecutionService
{
    Task<ProductionPipelineExecutionResult> ExecuteAsync(ProductionPipelineRequest request, CancellationToken cancellationToken);
}

public interface IContentPlanProductionExecutionService
{
    Task<ContentPlanProductionExecutionResult> ExecuteContentPlanAsync(Guid contentGenerationPlanId, bool dryRun, bool overwriteExisting, CancellationToken cancellationToken);
    Task<ContentPlanProductionExecutionResult> ExecuteContentPlanWithProductionPipelineAsync(ContentPlanProductionExecutionRequest request, CancellationToken cancellationToken);
}
