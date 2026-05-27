namespace Astronomy.SscIntelligence.Contracts;

public sealed record SkyObjectPosition(
    string Name,
    double AltitudeDeg,
    double AzimuthDeg,
    double Magnitude,
    string? ObjectType = null,
    double Weight = 1.0);
