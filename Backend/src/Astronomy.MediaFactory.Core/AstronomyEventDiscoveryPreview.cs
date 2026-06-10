namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyEventDiscoveryPreviewRequest(
    int Year,
    string RegionId,
    string Language = "en",
    bool DryRun = false,
    bool OverwriteExisting = true);

public sealed record AstronomyEventDiscoveryPreviewResponse(
    int Year,
    string RegionId,
    bool EventPreviewGenerated,
    string EventPreviewPath,
    int EventCount,
    int TopEventCount,
    IReadOnlyList<string> GeneratedFiles);

public sealed record AstronomyEventPreviewDocument(
    int Year,
    string RegionId,
    string Language,
    int EventCount,
    IReadOnlyList<AstronomyEventPreviewItem> Events,
    IReadOnlyDictionary<string, int> EventTypeCounts,
    IReadOnlyList<string> TopEvents,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedUtc);

public sealed record AstronomyEventPreviewItem(
    string EventId,
    string EventType,
    string Title,
    string ShortTitle,
    DateTimeOffset StartUtc,
    DateTimeOffset PeakUtc,
    DateTimeOffset EndUtc,
    string LocalPeakTime,
    string VisibilityRegion,
    IReadOnlyList<string> PrimaryObjects,
    IReadOnlyList<string> SecondaryObjects,
    string SkyDirectionHint,
    int ContentWorthinessScore,
    int VisibilityScore,
    int RarityScore,
    int PublicInterestScore,
    IReadOnlyList<string> RecommendedContentTypes,
    RecommendedPublishWindow RecommendedPublishWindow,
    string SourceType,
    string SourceNotes,
    IReadOnlyList<string> Warnings);

public sealed record RecommendedPublishWindow(
    DateTimeOffset PublishStartUtc,
    DateTimeOffset PublishEndUtc);

public interface IAstronomyEventDiscoveryPreviewService
{
    Task<AstronomyEventDiscoveryPreviewResponse> DiscoverAstronomyEventsAsync(AstronomyEventDiscoveryPreviewRequest request, CancellationToken cancellationToken);
}
