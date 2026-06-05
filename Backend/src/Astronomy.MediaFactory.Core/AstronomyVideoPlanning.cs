namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyVideoPlanningRequest(
    string? RegionId = null,
    DateTimeOffset? StartUtc = null,
    DateTimeOffset? EndUtc = null,
    IReadOnlyList<string>? ContentCategories = null,
    decimal MinPriorityScore = 0m,
    int? MaxPlans = null,
    bool DryRun = true);

public sealed record AstronomyVideoPlanningResult(
    int PlanCount,
    int SavedCount,
    int SkippedDuplicates,
    bool DryRun,
    IReadOnlyList<AstronomyVideoPlanDto> GeneratedPlans,
    IReadOnlyList<string> Warnings);

public sealed record AstronomyVideoPlanDto(
    Guid? ContentGenerationPlanId,
    Guid OpportunityId,
    Guid AstronomyEventIntelligenceId,
    string EventCode,
    string EventType,
    string ContentCategory,
    string SuggestedTitle,
    string Language,
    string RegionId,
    string? LocationName,
    string PlannedFormat,
    int SceneCount,
    string VisualStrategyJson,
    string NarrationStrategyJson,
    string ThumbnailStrategyJson,
    string Status,
    decimal PriorityScore,
    DateTimeOffset ScheduledUtc,
    string? SelectedEventObjectIdsJson,
    string? SelectedObjectNamesJson,
    string? SourceEventObjectIdsJson,
    string? PlannedObjectNamesJson,
    bool DuplicateSkipped);

public interface IAstronomyVideoPlanningService
{
    Task<AstronomyVideoPlanningResult> GenerateVideoPlansAsync(AstronomyVideoPlanningRequest request, CancellationToken cancellationToken);
}
