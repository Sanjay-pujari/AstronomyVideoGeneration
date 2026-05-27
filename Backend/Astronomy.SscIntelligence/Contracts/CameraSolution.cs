namespace Astronomy.SscIntelligence.Contracts;

public sealed record CameraSolution(double AltitudeDeg, double AzimuthDeg, double FovDeg, bool RequiresSplit, double AngularSpreadDeg);
