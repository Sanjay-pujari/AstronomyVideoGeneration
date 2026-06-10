using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public interface ISkyfieldAccuracyProvider
{
    Task<SkyfieldAccuracyResult> ComputeYearlyAccuracyAsync(int year, RegionScheduleOptions region, IReadOnlyList<AstronomyEventPreviewItem> events, CancellationToken cancellationToken);
}

public sealed class SkyfieldAccuracyResult
{
    public List<SkyfieldPlanetPairing> PlanetPairings { get; set; } = [];
    public List<SkyfieldMoonPhase> MoonPhases { get; set; } = [];
    public List<SkyfieldMeteorMoonlight> MeteorMoonlight { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class SkyfieldPlanetPairing
{
    public string PrimaryObject { get; set; } = string.Empty;
    public string SecondaryObject { get; set; } = string.Empty;
    public DateTimeOffset PeakUtc { get; set; }
    public double AngularSeparationDegrees { get; set; }
    public Dictionary<string, double> ObjectAltitudesDegrees { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double SunAltitudeDegrees { get; set; }
    public string BestViewingLocalTime { get; set; } = string.Empty;
    public string SkyDirectionHint { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public bool InvolvesBrightPlanet { get; set; }
}

public sealed class SkyfieldMoonPhase
{
    public string Phase { get; set; } = string.Empty;
    public DateTimeOffset PeakUtc { get; set; }
    public string LocalPeakTime { get; set; } = string.Empty;
}

public sealed class SkyfieldMeteorMoonlight
{
    public string EventId { get; set; } = string.Empty;
    public DateTimeOffset PeakUtc { get; set; }
    public double MoonIlluminationPercent { get; set; }
    public string MoonInterference { get; set; } = string.Empty;
    public int VisibilityScoreAdjustment { get; set; }
    public string BestViewingWindowLocal { get; set; } = string.Empty;
    public string RadiantVisibilityNote { get; set; } = string.Empty;
}
