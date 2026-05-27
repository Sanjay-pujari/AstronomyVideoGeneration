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
    SceneIntentType SceneIntent = SceneIntentType.Grouping,
    string? SceneCode = null,
    string? SceneTitle = null,
    IReadOnlyList<string>? ExplicitTargetObjectNames = null);

public sealed record SscIntelligenceResult(
    IReadOnlyList<SkyObjectPosition> VisibleObjects,
    IReadOnlyList<string> RemovedObjects,
    double CameraAltitudeDeg,
    double CameraAzimuthDeg,
    double FovDeg,
    bool RequiresSplit,
    double RawCameraAltitudeDeg,
    string CompositionBiasReason,
    IReadOnlyList<string> PrimaryTargets,
    IReadOnlyList<string> SecondaryTargets,
    IReadOnlyList<string> ContextTargets,
    string SscScript,
    NightWindowResult NightWindow);
