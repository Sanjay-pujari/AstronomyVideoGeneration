namespace Astronomy.SscIntelligence.Composition;

public sealed record CinematicAnchorResult(double AnchoredCameraAltitudeDeg, double DesiredY, double DesiredX, double TargetAltitudeDeg, double AppliedDeltaDeg, string Reason);
