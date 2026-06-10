namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyEventVerificationRequest(
    int Year,
    string RegionId,
    string Language = "en",
    bool DryRun = false,
    bool OverwriteExisting = true);

public sealed record AstronomyEventVerificationResponse(
    int Year,
    string RegionId,
    bool EventVerificationGenerated,
    string EventVerificationPath,
    int InputEventCount,
    int VerifiedEventCount,
    int DeduplicatedCount,
    int HighPriorityCount,
    int ManualReviewCount,
    int AutoGenerateAllowedCount,
    IReadOnlyList<string> GeneratedFiles);

public interface IAstronomyEventVerificationService
{
    Task<AstronomyEventVerificationResponse> VerifyAstronomyEventsAsync(AstronomyEventVerificationRequest request, CancellationToken cancellationToken);
}
