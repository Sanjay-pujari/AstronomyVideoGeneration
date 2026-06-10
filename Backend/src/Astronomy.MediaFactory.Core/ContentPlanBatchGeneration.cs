namespace Astronomy.MediaFactory.Core;

public sealed record BatchGenerateFromPlansRequest(
    int Year,
    string RegionId,
    string Language = "en",
    int MaxPlans = 1,
    bool OnlyHighPriority = false,
    bool DryRun = true,
    IReadOnlyList<string>? PlanTitles = null);

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
    IReadOnlyList<object>? Results = null);

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
