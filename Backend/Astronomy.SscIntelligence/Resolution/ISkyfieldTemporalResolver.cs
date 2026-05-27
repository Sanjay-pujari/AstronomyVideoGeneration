namespace Astronomy.SscIntelligence.Resolution;

public interface ISkyfieldTemporalResolver
{
    TemporalResolutionResult Resolve(
        string requestedObjectName,
        DateTime selectedObservationUtc,
        IEnumerable<SkyfieldTemporalCandidate> candidates,
        double? maximumDeltaMinutes = null);
}

public sealed record SkyfieldTemporalCandidate(
    string ObjectName,
    DateTime SnapshotUtc,
    double AltitudeDegrees,
    double AzimuthDegrees,
    double? Magnitude);
