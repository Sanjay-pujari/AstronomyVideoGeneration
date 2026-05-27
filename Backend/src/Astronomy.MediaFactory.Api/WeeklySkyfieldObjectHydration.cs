using Astronomy.MediaFactory.Core;
using Astronomy.SscIntelligence.Resolution;

namespace Astronomy.MediaFactory.Api;

public static class WeeklySkyfieldObjectHydration
{
    internal sealed record HydratedTemporalObject(string NormalizedName, DateTime SnapshotUtc, double AltitudeDegrees, double AzimuthDegrees, double? Magnitude, string DtoTypeName);

    public static IReadOnlyList<SkyfieldTemporalCandidate> BuildTemporalCandidates(
        IEnumerable<WeeklyAstronomyEvent> extractedEvents,
        HashSet<string> targetAliases,
        Func<WeeklyAstronomyEvent, DateTime?> resolveEventUtc,
        Func<string?, string> normalizeName,
        Func<string?, string?, HashSet<string>, bool> matchesAliases,
        Microsoft.Extensions.Logging.ILogger logger,
        string sceneCode,
        string requestedObject)
    {
        var events = extractedEvents?.ToList() ?? [];
        LogStage(logger, sceneCode, requestedObject, "ExtractedEvents", events.Count, events.SelectMany(e => e.Objects ?? []).Select(o => o.ObjectCode ?? o.ObjectName), events.Select(resolveEventUtc), nameof(WeeklyAstronomyEvent));

        var objects = events.SelectMany(e => e.Objects ?? []).ToList();
        LogStage(logger, sceneCode, requestedObject, "Objects", objects.Count, objects.Select(o => o.ObjectCode ?? o.ObjectName), events.Select(resolveEventUtc), nameof(WeeklyAstronomyEventObject));

        var hydrated = new List<HydratedTemporalObject>();
        foreach (var item in events.SelectMany(ev => (ev.Objects ?? []).Select(o => new { Event = ev, Object = o })))
        {
            var sourceName = item.Object.ObjectCode ?? item.Object.ObjectName;
            var normalizedName = normalizeName(sourceName);
            var timestamp = resolveEventUtc(item.Event);
            var altitude = item.Object.AltitudeDegrees;
            var azimuth = item.Object.AzimuthDegrees;
            var magnitude = item.Object.Magnitude;

            if (!matchesAliases(item.Object.ObjectCode, item.Object.ObjectName, targetAliases))
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, normalizedName, timestamp, altitude, azimuth, magnitude, "normalization mismatch");
                continue;
            }

            if (!timestamp.HasValue)
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, normalizedName, timestamp, altitude, azimuth, magnitude, "timestamp parse failure");
                continue;
            }

            if (!altitude.HasValue)
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, normalizedName, timestamp, altitude, azimuth, magnitude, "null altitude");
                continue;
            }

            if (double.IsNaN(altitude.Value))
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, normalizedName, timestamp, altitude, azimuth, magnitude, "NaN value (altitude)");
                continue;
            }

            if (!azimuth.HasValue)
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, normalizedName, timestamp, altitude, azimuth, magnitude, "invalid azimuth");
                continue;
            }

            if (double.IsNaN(azimuth.Value))
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, normalizedName, timestamp, altitude, azimuth, magnitude, "NaN value (azimuth)");
                continue;
            }

            if (magnitude.HasValue && double.IsNaN(magnitude.Value))
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, normalizedName, timestamp, altitude, azimuth, magnitude, "NaN value (magnitude)");
                continue;
            }

            if (item.Object.GetType().Name != nameof(WeeklyAstronomyEventObject))
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, normalizedName, timestamp, altitude, azimuth, magnitude, "unsupported DTO");
                continue;
            }

            LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, normalizedName, timestamp, altitude, azimuth, magnitude, null);
            hydrated.Add(new HydratedTemporalObject(normalizedName, timestamp.Value, altitude.Value, azimuth.Value, magnitude, item.Object.GetType().Name));
        }

        LogStage(logger, sceneCode, requestedObject, "OBJECT_HYDRATION_STAGE", hydrated.Count, hydrated.Select(h => h.NormalizedName), hydrated.Select(h => (DateTime?)h.SnapshotUtc), nameof(HydratedTemporalObject));
        if (objects.Count > 0 && hydrated.Count == 0)
        {
            logger.LogCritical(
                "OBJECT_HYDRATION_STAGE_CRITICAL sceneCode={SceneCode} object={Object} sourceObjectCount={SourceObjectCount} hydratedCount={HydratedCount}",
                sceneCode,
                requestedObject,
                objects.Count,
                hydrated.Count);
        }

        return hydrated
            .Select(h => new SkyfieldTemporalCandidate(h.NormalizedName.ToUpperInvariant(), h.SnapshotUtc, h.AltitudeDegrees, h.AzimuthDegrees, h.Magnitude))
            .ToList();
    }

    private static void LogHydratedMappingAttempt(Microsoft.Extensions.Logging.ILogger logger, string sceneCode, string requestedObject, string? sourceName, string normalizedName, DateTime? timestamp, double? altitude, double? azimuth, double? magnitude, string? rejectionReason)
    {
        logger.LogInformation(
            "HYDRATED_OBJECT_MAPPING sceneCode={SceneCode} object={Object} sourceName={SourceName} normalizedName={NormalizedName} timestamp={Timestamp} altitudeField=AltitudeDegrees altitudeValue={AltitudeValue} azimuthField=AzimuthDegrees azimuthValue={AzimuthValue} magnitudeField=Magnitude magnitudeValue={MagnitudeValue} rejectionReason={RejectionReason}",
            sceneCode,
            requestedObject,
            sourceName ?? string.Empty,
            normalizedName,
            timestamp?.ToString("O") ?? string.Empty,
            altitude,
            azimuth,
            magnitude,
            rejectionReason ?? "accepted");
    }

    private static void LogStage(Microsoft.Extensions.Logging.ILogger logger, string sceneCode, string requestedObject, string stage, int count, IEnumerable<string?> names, IEnumerable<DateTime?> timestamps, string dtoType)
    {
        logger.LogInformation(
            "{Stage} sceneCode={SceneCode} object={Object} count={Count} names={Names} timestamps={Timestamps} dtoType={DtoType}",
            stage,
            sceneCode,
            requestedObject,
            count,
            string.Join(",", names.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)),
            string.Join(",", timestamps.Where(x => x.HasValue).Select(x => x!.Value.ToString("O")).Distinct().OrderBy(x => x)),
            dtoType);
    }
}
