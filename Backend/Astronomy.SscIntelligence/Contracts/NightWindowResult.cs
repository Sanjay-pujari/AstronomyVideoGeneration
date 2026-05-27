namespace Astronomy.SscIntelligence.Contracts;

public sealed record NightWindowResult(bool IsNight, DateTime ObservationUtc, double? SunAltitudeDeg = null);
