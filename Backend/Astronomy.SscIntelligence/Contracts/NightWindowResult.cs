namespace Astronomy.SscIntelligence.Contracts;

public sealed record NightWindowResult(
    DateTime BestObservationUtc,
    DateTime BestObservationLocalTime,
    bool IsNight,
    double? SunAltitudeDeg = null,
    string Reason = "");
