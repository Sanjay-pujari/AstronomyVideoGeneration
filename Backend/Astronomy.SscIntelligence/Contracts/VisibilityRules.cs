namespace Astronomy.SscIntelligence.Contracts;

public sealed record VisibilityRules
{
    public double MinimumObjectAltitudeDeg { get; init; } = 10;
    public double TwilightSunAltitudeThresholdDeg { get; init; } = -12;
    public double MaximumMagnitude { get; init; } = 6.0;
    public double MaximumGroupSpreadDeg { get; init; } = 70;
}
