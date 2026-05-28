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

        TryHydrateFromPersistedJson(hydrated, targetAliases, normalizeName, matchesAliases, logger, sceneCode, requestedObject);

        hydrated = hydrated
            .GroupBy(h => new { Name = h.NormalizedName.ToUpperInvariant(), Time = h.SnapshotUtc, Alt = Math.Round(h.AltitudeDegrees, 6), Az = Math.Round(h.AzimuthDegrees, 6) })
            .Select(g => g.First())
            .ToList();

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

    private static readonly string[] SkyfieldJsonProbePaths = [
        Path.Combine("debug", "skyfield-weekly-response.json"),
        Path.Combine("Backend", "debug", "skyfield-weekly-response.json")
    ];

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

    private static void TryHydrateFromPersistedJson(
        List<HydratedTemporalObject> hydrated,
        HashSet<string> targetAliases,
        Func<string?, string> normalizeName,
        Func<string?, string?, HashSet<string>, bool> matchesAliases,
        Microsoft.Extensions.Logging.ILogger logger,
        string sceneCode,
        string requestedObject)
    {
        var jsonPath = SkyfieldJsonProbePaths.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(jsonPath)) return;

        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        TraverseNode(document.RootElement, "$", null, logger, sceneCode, requestedObject, targetAliases, normalizeName, matchesAliases, hydrated, jsonPath);
    }

    private static void TraverseNode(JsonElement node, string path, DateTime? inheritedTimeUtc, Microsoft.Extensions.Logging.ILogger logger, string sceneCode, string requestedObject, HashSet<string> targetAliases, Func<string?, string> normalizeName, Func<string?, string?, HashSet<string>, bool> matchesAliases, List<HydratedTemporalObject> hydrated, string jsonFilePath)
    {
        var nodeTimestamp = ResolveTimestampFromJsonNode(node) ?? inheritedTimeUtc;
        if (node.ValueKind == JsonValueKind.Object)
        {
            var rawName = TryGetString(node, ["objectCode", "objectName", "name", "body", "target"]);
            var rawAlt = TryGetDouble(node, ["altitudeDegrees", "altitudeDeg", "altitude", "alt"]);
            var rawAz = TryGetDouble(node, ["azimuthDegrees", "azimuthDeg", "azimuth", "az"]);
            var rawMagnitude = TryGetDouble(node, ["magnitude", "apparentMagnitude"]);
            if (!string.IsNullOrWhiteSpace(rawName) && rawAlt.HasValue && rawAz.HasValue)
            {
                var matches = matchesAliases(rawName, rawName, targetAliases);
                if (matches && nodeTimestamp.HasValue)
                {
                    logger.LogInformation("SKYFIELD_JSON_PATH_USED object={Object} jsonPath={JsonPath} time={Time} alt={Alt} az={Az} magnitude={Magnitude}", rawName, path, nodeTimestamp.Value.ToString("O"), rawAlt.Value, rawAz.Value, rawMagnitude);
                    hydrated.Add(new HydratedTemporalObject(normalizeName(rawName), nodeTimestamp.Value, rawAlt.Value, rawAz.Value, rawMagnitude, "SkyfieldJsonElement"));
                }
            }

            foreach (var p in node.EnumerateObject())
            {
                TraverseNode(p.Value, $"{path}.{p.Name}", nodeTimestamp, logger, sceneCode, requestedObject, targetAliases, normalizeName, matchesAliases, hydrated, jsonFilePath);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var child in node.EnumerateArray())
            {
                TraverseNode(child, $"{path}[{idx++}]", nodeTimestamp, logger, sceneCode, requestedObject, targetAliases, normalizeName, matchesAliases, hydrated, jsonFilePath);
            }
        }
    }

    private static DateTime? ResolveTimestampFromJsonNode(JsonElement node)
    {
        var raw = TryGetString(node, ["timeUtc", "timestampUtc", "observationUtc", "utc", "dateTimeUtc", "timestamp", "bestTimeUtc", "bestStartUtc"]);
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)) return parsed;

        var date = TryGetString(node, ["date", "bestDateLocal"]);
        var time = TryGetString(node, ["time", "bestTimeLocal", "observationTimeLocal"]);
        if (!string.IsNullOrWhiteSpace(date) && !string.IsNullOrWhiteSpace(time) && DateTime.TryParse($"{date} {time}", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var combined))
        {
            return combined;
        }
        return null;
    }

    private static string? TryGetString(JsonElement node, IReadOnlyList<string> aliases)
    {
        if (node.ValueKind != JsonValueKind.Object) return null;
        foreach (var alias in aliases)
        {
            foreach (var p in node.EnumerateObject())
            {
                if (!string.Equals(p.Name, alias, StringComparison.OrdinalIgnoreCase)) continue;
                if (p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
                return p.Value.ToString();
            }
        }
        return null;
    }

    private static double? TryGetDouble(JsonElement node, IReadOnlyList<string> aliases)
    {
        var raw = TryGetString(node, aliases);
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
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
