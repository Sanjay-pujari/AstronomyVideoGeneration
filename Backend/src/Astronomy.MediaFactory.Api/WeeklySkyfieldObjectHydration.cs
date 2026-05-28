using Astronomy.MediaFactory.Core;
using Astronomy.SscIntelligence.Resolution;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

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
            var sourceName = ResolveRawString(item.Object, ["objectCode", "objectName", "name", "body", "target"]) ?? item.Object.ObjectCode ?? item.Object.ObjectName;
            var normalizedName = normalizeName(sourceName);
            var timestamp = ResolveRawTimestamp(item.Object, item.Event, resolveEventUtc);
            var altitude = ResolveRawDouble(item.Object, ["altitudeDegrees", "altitudeDeg", "altitude", "alt"]) ?? item.Object.AltitudeDegrees;
            var azimuth = ResolveRawDouble(item.Object, ["azimuthDegrees", "azimuthDeg", "azimuth", "az"]) ?? item.Object.AzimuthDegrees;
            var magnitude = ResolveRawDouble(item.Object, ["magnitude", "apparentMagnitude"]) ?? item.Object.Magnitude;

            var matches = MatchesRawAliases(item.Object, targetAliases, matchesAliases) || matchesAliases(item.Object.ObjectCode, item.Object.ObjectName, targetAliases);

            if (!matches)
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, timestamp, altitude, azimuth, magnitude, false, "normalization mismatch");
                continue;
            }

            if (!timestamp.HasValue)
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, timestamp, altitude, azimuth, magnitude, false, "timestamp parse failure");
                continue;
            }

            if (!altitude.HasValue)
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, timestamp, altitude, azimuth, magnitude, false, "null altitude");
                continue;
            }

            if (double.IsNaN(altitude.Value))
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, timestamp, altitude, azimuth, magnitude, false, "NaN value (altitude)");
                continue;
            }

            if (!azimuth.HasValue)
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, timestamp, altitude, azimuth, magnitude, false, "invalid azimuth");
                continue;
            }

            if (double.IsNaN(azimuth.Value))
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, timestamp, altitude, azimuth, magnitude, false, "NaN value (azimuth)");
                continue;
            }

            if (magnitude.HasValue && double.IsNaN(magnitude.Value))
            {
                LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, timestamp, altitude, azimuth, magnitude, false, "NaN value (magnitude)");
                continue;
            }

            LogHydratedMappingAttempt(logger, sceneCode, requestedObject, sourceName, timestamp, altitude, azimuth, magnitude, true, null);
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

    private static void LogHydratedMappingAttempt(Microsoft.Extensions.Logging.ILogger logger, string sceneCode, string requestedObject, string? rawName, DateTime? rawTimestamp, double? rawAltitude, double? rawAzimuth, double? rawMagnitude, bool mapped, string? rejectionReason)
    {
        logger.LogInformation(
            "SKYFIELD_RAW_OBJECT_FIELD_DUMP sceneCode={SceneCode} requestedObject={RequestedObject} rawName={RawName} rawTimestamp={RawTimestamp} rawAltitude={RawAltitude} rawAzimuth={RawAzimuth} rawMagnitude={RawMagnitude} mapped={Mapped} rejectReason={RejectReason}",
            sceneCode, requestedObject, rawName ?? string.Empty, rawTimestamp?.ToString("O") ?? string.Empty, rawAltitude, rawAzimuth, rawMagnitude, mapped, rejectionReason ?? string.Empty);
    }

    private static bool MatchesRawAliases(object source, HashSet<string> targetAliases, Func<string?, string?, HashSet<string>, bool> matchesAliases)
        => matchesAliases(ResolveRawString(source, ["objectCode", "target", "body"]), ResolveRawString(source, ["objectName", "name"]), targetAliases);

    private static DateTime? ResolveRawTimestamp(object source, WeeklyAstronomyEvent weeklyEvent, Func<WeeklyAstronomyEvent, DateTime?> resolveEventUtc)
    {
        var raw = ResolveRawString(source, ["timeUtc", "timestampUtc", "observationUtc", "utc", "dateTimeUtc", "timestamp"]);
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)) return parsed;
        return resolveEventUtc(weeklyEvent);
    }

    private static string? ResolveRawString(object source, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            var value = ResolvePropertyValue(source, alias);
            if (value is null) continue;
            if (value is string s && !string.IsNullOrWhiteSpace(s)) return s;
            if (value is JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.String) return e.GetString();
                return e.ToString();
            }
            var text = value.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        return null;
    }

    private static double? ResolveRawDouble(object source, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            var value = ResolvePropertyValue(source, alias);
            if (value is null) continue;
            if (value is double d) return d;
            if (value is float f) return f;
            if (value is decimal m) return (double)m;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var number)) return number;
                if (e.ValueKind == JsonValueKind.String && double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fromString)) return fromString;
            }

            if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }

        return null;
    }

    private static object? ResolvePropertyValue(object source, string alias)
    {
        var prop = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, alias, StringComparison.OrdinalIgnoreCase));
        return prop?.GetValue(source);
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
