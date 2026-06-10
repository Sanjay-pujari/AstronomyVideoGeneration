namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyEventVerifiedImportRequest(
    int Year,
    string RegionId,
    string Language = "en",
    bool DryRun = false,
    bool OverwriteExisting = true,
    bool CreateContentPlans = true);

public sealed record AstronomyEventVerifiedImportResponse(
    int Year,
    string RegionId,
    int EventsImported,
    int EventsInserted,
    int EventsUpdated,
    int ContentPlansCreated,
    int ContentPlansSkipped,
    int ManualReviewEvents,
    int AutoGenerateAllowedEvents,
    int HighPriorityPlans,
    bool DryRun,
    IReadOnlyList<string> GeneratedFiles);

public interface IAstronomyEventVerifiedImportService
{
    Task<AstronomyEventVerifiedImportResponse> ImportVerifiedEventsAsync(AstronomyEventVerifiedImportRequest request, CancellationToken cancellationToken);
}
