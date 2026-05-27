namespace Astronomy.SscIntelligence.Resolution;

public sealed record TemporalResolutionResult(
    bool MatchFound,
    bool ExactMatch,
    string Source,
    DateTime RequestedTimeUtc,
    DateTime? MatchedTimeUtc,
    double? DeltaMinutes,
    double? AltitudeDegrees,
    double? AzimuthDegrees,
    double? Magnitude,
    string? RejectionReason = null);
