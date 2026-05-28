namespace Astronomy.SscIntelligence.Camera;

public sealed record CinematicCameraPlan(
    double CameraAzimuth,
    double CameraAltitude,
    double FovDegrees,
    string FramingMode,
    double VerticalBias,
    double HorizonBias,
    string TrackingMode,
    string MotionHint,
    IReadOnlyList<string> SafetyWarnings,
    string Reason);
