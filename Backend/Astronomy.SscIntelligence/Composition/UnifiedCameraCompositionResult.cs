namespace Astronomy.SscIntelligence.Composition;

public sealed record UnifiedCameraCompositionResult(
    double FinalCameraAltitudeDeg,
    double FinalCameraAzimuthDeg,
    double RawCameraAltitudeDeg,
    double RawCameraAzimuthDeg,
    double FovDeg,
    double DesiredY,
    double DesiredX,
    IReadOnlyList<string> AnchorTargetNames,
    double TargetAltitudeDeg,
    double TopSafeAltitudeDeg,
    double BottomSafeAltitudeDeg,
    double AppliedAltitudeAdjustmentDeg,
    double HorizontalAdjustmentDeg,
    string Reason);
