namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyContentOpportunityRequest(
    string? RegionId = null,
    DateTimeOffset? StartUtc = null,
    DateTimeOffset? EndUtc = null,
    IReadOnlyList<string>? EventTypes = null,
    bool DryRun = true,
    int? MaxOpportunities = null);

public sealed record AstronomyContentOpportunityResult(
    int OpportunityCount,
    int SavedCount,
    int SkippedDuplicates,
    bool DryRun,
    IReadOnlyList<AstronomyContentOpportunityDto> GeneratedOpportunities);

public sealed record AstronomyContentOpportunityDto(
    Guid? Id,
    Guid AstronomyEventIntelligenceId,
    string EventCode,
    string EventType,
    string EventTitle,
    string ContentCategory,
    string Title,
    string? Angle,
    string? AudienceSegment,
    decimal PriorityScore,
    decimal VisibilityScore,
    decimal RarityScore,
    decimal StoryScore,
    decimal ViralPotentialScore,
    decimal ConfidenceScore,
    decimal EducationalValueScore,
    decimal ViralScore,
    decimal ProductionReadinessScore,
    bool RequiresSkyfield,
    bool RequiresConstellationGuide,
    bool RequiresStellarium,
    bool RequiresNasaAssets,
    bool RequiresAiImages,
    string Status,
    bool DuplicateSkipped);

public interface IAstronomyContentOpportunityService
{
    Task<AstronomyContentOpportunityResult> GenerateAsync(AstronomyContentOpportunityRequest request, CancellationToken cancellationToken);
}
