namespace Astronomy.SscIntelligence.Resolution;

public sealed class SkyfieldTemporalResolver : ISkyfieldTemporalResolver
{
    public const double DefaultMaximumDeltaMinutes = 180;

    public TemporalResolutionResult Resolve(string requestedObjectName, DateTime selectedObservationUtc, IEnumerable<SkyfieldTemporalCandidate> candidates, double? maximumDeltaMinutes = null)
    {
        var tolerance = maximumDeltaMinutes ?? DefaultMaximumDeltaMinutes;
        var normalized = Normalize(requestedObjectName);
        var filtered = candidates
            .Where(x => Normalize(x.ObjectName) == normalized)
            .ToList();

        if (filtered.Count == 0)
        {
            return new(false, false, "fallback", selectedObservationUtc, null, null, null, null, null, "no-name-match");
        }

        var exact = filtered.FirstOrDefault(x => x.SnapshotUtc == selectedObservationUtc);
        if (exact is not null)
        {
            return new(true, true, "skyfield.exact", selectedObservationUtc, exact.SnapshotUtc, 0, exact.AltitudeDegrees, exact.AzimuthDegrees, exact.Magnitude);
        }

        var sameDay = filtered
            .Where(x => x.SnapshotUtc.Date == selectedObservationUtc.Date)
            .OrderBy(x => Math.Abs((x.SnapshotUtc - selectedObservationUtc).TotalMinutes))
            .FirstOrDefault();

        if (sameDay is not null)
        {
            var delta = Math.Abs((sameDay.SnapshotUtc - selectedObservationUtc).TotalMinutes);
            if (delta <= tolerance)
            {
                return new(true, false, "skyfield.nearest-time", selectedObservationUtc, sameDay.SnapshotUtc, delta, sameDay.AltitudeDegrees, sameDay.AzimuthDegrees, sameDay.Magnitude);
            }

            return new(false, false, "fallback", selectedObservationUtc, sameDay.SnapshotUtc, delta, null, null, null, "same-day-delta-exceeds-tolerance");
        }

        var adjacent = filtered
            .Where(x => Math.Abs((x.SnapshotUtc.Date - selectedObservationUtc.Date).TotalDays) == 1)
            .OrderBy(x => Math.Abs((x.SnapshotUtc - selectedObservationUtc).TotalMinutes))
            .FirstOrDefault();

        if (adjacent is null)
        {
            return new(false, false, "fallback", selectedObservationUtc, null, null, null, null, null, "no-adjacent-day-candidate");
        }

        var adjacentDelta = Math.Abs((adjacent.SnapshotUtc - selectedObservationUtc).TotalMinutes);
        if (adjacentDelta <= tolerance)
        {
            return new(true, false, "skyfield.nearest-time", selectedObservationUtc, adjacent.SnapshotUtc, adjacentDelta, adjacent.AltitudeDegrees, adjacent.AzimuthDegrees, adjacent.Magnitude);
        }

        return new(false, false, "fallback", selectedObservationUtc, adjacent.SnapshotUtc, adjacentDelta, null, null, null, "adjacent-day-delta-exceeds-tolerance");
    }

    private static string Normalize(string raw)
        => string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToLowerInvariant();
}
