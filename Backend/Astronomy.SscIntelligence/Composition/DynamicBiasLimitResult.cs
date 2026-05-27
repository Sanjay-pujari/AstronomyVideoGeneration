namespace Astronomy.SscIntelligence.Composition;

public sealed record DynamicBiasLimitResult(double LimitedBiasDeg, double OriginalBiasDeg, bool WasLimited, string Reason, double MaxPrimaryAltitudeDeg, double MinPrimaryAltitudeDeg);
