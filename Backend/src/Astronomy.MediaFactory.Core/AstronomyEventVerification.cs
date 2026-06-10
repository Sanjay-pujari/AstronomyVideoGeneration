namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyEventVerificationRequest(
    int Year,
    string RegionId,
    string Language = "en",
    bool DryRun = false,
    bool OverwriteExisting = true,
    bool AccuracyUpgrade = false);

public sealed record AstronomyEventVerificationResponse(
    int Year,
    string RegionId,
    bool EventVerificationGenerated,
    string EventVerificationPath,
    int InputEventCount,
    int VerifiedEventCount,
    int DeduplicatedCount,
    int SkyfieldVerifiedCount,
    int ManualReviewCount,
    int AutoGenerateAllowedCount,
    int PlanetPairingComputedCount,
    int MoonPhaseVerifiedCount,
    int MeteorMoonlightAdjustedCount,
    IReadOnlyList<string> GeneratedFiles)
{
    public int HighPriorityCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}


public interface IAstronomyEventVerificationService
{
    Task<AstronomyEventVerificationResponse> VerifyAstronomyEventsAsync(AstronomyEventVerificationRequest request, CancellationToken cancellationToken);
}
