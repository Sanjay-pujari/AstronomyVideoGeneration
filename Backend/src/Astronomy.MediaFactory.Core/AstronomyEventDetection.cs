namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyEventDetectionRequest(
    string RegionId,
    string LocationName,
    double Latitude,
    double Longitude,
    string Timezone,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    IReadOnlyList<string>? EventTypes = null,
    int? MaxEvents = null,
    bool DryRun = true);

public sealed record AstronomyEventDetectionResult(
    string RegionId,
    string LocationName,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool DryRun,
    int DetectedCount,
    int SavedCount,
    IReadOnlyList<DetectedAstronomyEventDto> Events,
    IReadOnlyList<string> Warnings,
    AstronomyEventDetectionDiagnostics? Diagnostics = null);

public sealed record AstronomyEventDetectionDiagnostics(
    int DaysScanned,
    int SkyfieldDaysSuccessful,
    int VisibleObjectCount,
    IReadOnlyList<AstronomyEventCandidateReason> CandidateReasons);

public sealed record AstronomyEventCandidateReason(
    string EventCode,
    string EventType,
    DateOnly TargetDate,
    string CandidateReason);

public sealed record DetectedAstronomyEventDto(
    Guid? Id,
    string EventCode,
    string EventType,
    string Title,
    string? Summary,
    string? Description,
    DateTimeOffset StartUtc,
    DateTimeOffset? PeakUtc,
    DateTimeOffset? EndUtc,
    string? RegionId,
    string? LocationName,
    string? TimeZone,
    string RecommendedCategory,
    string Status,
    decimal VisibilityScore,
    decimal RarityScore,
    decimal StoryScore,
    decimal ViralPotentialScore,
    decimal ConfidenceScore,
    IReadOnlyList<DetectedAstronomyEventObjectDto> Objects,
    string? RawDataJson,
    string? RulesAppliedJson,
    string? MetadataJson);

public sealed record DetectedAstronomyEventObjectDto(
    Guid? Id,
    string ObjectName,
    string ObjectType,
    string? ObjectRole,
    string? CatalogId,
    decimal? Magnitude,
    decimal? VisibilityScore,
    string? MetadataJson);

public interface IAstronomyEventDetectionService
{
    Task<AstronomyEventDetectionResult> DetectEventsAsync(AstronomyEventDetectionRequest request, CancellationToken cancellationToken);
}
