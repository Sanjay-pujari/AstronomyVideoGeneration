namespace Astronomy.SscIntelligence.Composition;

public sealed record ScreenSpaceFramingResult(double FinalCameraAltitudeDeg, double CameraAzimuthDeg, bool WasAdjusted, string Reason);
