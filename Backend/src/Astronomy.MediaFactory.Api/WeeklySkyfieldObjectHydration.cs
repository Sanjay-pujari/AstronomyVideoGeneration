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

        var hydrated = events
            .SelectMany(ev => (ev.Objects ?? []).Select(o => new { Event = ev, Object = o }))
            .Where(x => matchesAliases(x.Object.ObjectCode, x.Object.ObjectName, targetAliases))
            .Select(x =>
            {
                var ts = resolveEventUtc(x.Event);
                if (!ts.HasValue || !x.Object.AltitudeDegrees.HasValue || !x.Object.AzimuthDegrees.HasValue) return null;
                return new HydratedTemporalObject(
                    normalizeName(x.Object.ObjectCode ?? x.Object.ObjectName),
                    ts.Value,
                    x.Object.AltitudeDegrees.Value,
                    x.Object.AzimuthDegrees.Value,
                    x.Object.Magnitude,
                    x.Object.GetType().Name);
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        LogStage(logger, sceneCode, requestedObject, "OBJECT_HYDRATION_STAGE", hydrated.Count, hydrated.Select(h => h.NormalizedName), hydrated.Select(h => (DateTime?)h.SnapshotUtc), nameof(HydratedTemporalObject));

        return hydrated
            .Select(h => new SkyfieldTemporalCandidate(h.NormalizedName.ToUpperInvariant(), h.SnapshotUtc, h.AltitudeDegrees, h.AzimuthDegrees, h.Magnitude))
            .ToList();
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
