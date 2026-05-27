namespace Astronomy.SscIntelligence.Contracts;

public sealed record SscRenderRequest(
    DateTime ObservationUtc,
    double Longitude,
    double Latitude,
    double ElevationMeters,
    string LocationName,
    double CameraAltitudeDeg,
    double CameraAzimuthDeg,
    double FovDeg,
    string ScreenshotDirectory,
    string ScreenshotFileNameWithoutExtension);
