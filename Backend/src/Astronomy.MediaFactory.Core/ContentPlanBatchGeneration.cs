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
    IReadOnlyList<BatchGenerateFromPlansStepResult> Steps,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

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
    decimal? PriorityScore);

public sealed record BatchGenerateFromPlansStepResult(
    string StepName,
    string Status,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DurationMs,
    string? Message,
    string? ErrorMessage,
    object? Result);

public interface IContentPlanBatchGenerationService
{
    Task<BatchGenerateFromPlansResponse> GenerateFromPlansAsync(BatchGenerateFromPlansRequest request, CancellationToken cancellationToken);
}
