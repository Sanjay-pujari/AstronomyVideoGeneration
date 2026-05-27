using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence;

public interface ISscIntelligenceService
{
    SscIntelligenceResult Generate(SscIntelligenceRequest request, string? screenshotDirectory = null, string? screenshotFileNameWithoutExtension = null);
}

public sealed record SscIntelligenceRequest(
    DateTime ObservationUtc,
    double Longitude,
    double Latitude,
    double ElevationMeters,
    string LocationName,
    IReadOnlyList<SkyObjectPosition> SkyObjectPositions,
    VisibilityRules? VisibilityRules = null,
    double? SunAltitudeDeg = null,
    string Timezone = "Asia/Kolkata",
    DateTime? AstronomicalNightStartUtc = null,
    DateTime? AstronomicalNightEndUtc = null,
    SceneIntentType SceneIntent = SceneIntentType.Grouping);

public sealed record SscIntelligenceResult(
    IReadOnlyList<SkyObjectPosition> VisibleObjects,
    IReadOnlyList<string> RemovedObjects,
    double CameraAltitudeDeg,
    double CameraAzimuthDeg,
    double FovDeg,
    bool RequiresSplit,
    string SscScript,
    NightWindowResult NightWindow);
