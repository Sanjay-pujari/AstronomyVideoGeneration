using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Diagnostics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Diagnostics;

public static class MeteorActivityLifecycleDiagnostics
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly ConcurrentDictionary<string, AdapterAggregate> Adapter =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, ResolutionAggregate> Resolution =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, ProjectionAggregate> Projection =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, BeatAggregate> Beat =
        new(StringComparer.Ordinal);

    // One lock per destination file. This prevents parallel xUnit tests and parallel
    // resolver calls in the same process from overwriting the same diagnostic file.
    private static readonly ConcurrentDictionary<string, object> WriteLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public const string AdapterId = "v1.meteor-activity.production-event-intelligence";

    public static string Fingerprint(SemanticSourceAdapterContextV1 context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var meteorActivity = context.ProductionEventIntelligence?.MeteorActivity;
        var payload = JsonSerializer.Serialize(
            new
            {
                context.EventIdentity?.SourceEventType,
                context.EventIdentity?.ShortTitle,
                context.EventIdentity?.SourceEventId,
                radiant = First(
                    meteorActivity?.RadiantConstellation,
                    meteorActivity?.Radiant),
                peakWindow = meteorActivity?.PeakWindow?.LocalizedWindowDescription,
                meteorActivity?.PeakWindow?.PeakUtc,
                bestViewingWindowLocal = meteorActivity?.VisibilityNotes,
                primaryObjects = context.ProductionEventIntelligence?.PrimaryObjects
                    .Select(o => o.Name)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? []
            },
            Options);

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16]
            .ToLowerInvariant();
    }

    public static void WriteContext(
        SemanticSourceAdapterContextV1 context,
        object sourceMapping,
        object? normalization)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sourceMapping);

        var meteorActivity = context.ProductionEventIntelligence?.MeteorActivity;

        Write(
            "narration-v5/meteor-activity-context-diagnostics.json",
            new
            {
                productionRequestPresent = context.EventIdentity is not null,
                productionEventIntelligencePresent = context.ProductionEventIntelligence is not null,
                meteorActivityPresent = meteorActivity is not null,
                eventType = context.EventIdentity?.SourceEventType,
                title = context.EventIdentity?.Title,
                shortTitle = context.EventIdentity?.ShortTitle,
                sourceExternalEventId = context.EventIdentity?.SourceEventId,
                radiantConstellation = First(
                    meteorActivity?.RadiantConstellation,
                    meteorActivity?.Radiant),
                peakWindowPresent = meteorActivity?.PeakWindow is not null,
                peakWindowValue = meteorActivity?.PeakWindow?.LocalizedWindowDescription,
                startUtc = meteorActivity?.PeakWindow?.StartUtc,
                peakUtc = meteorActivity?.PeakWindow?.PeakUtc,
                endUtc = meteorActivity?.PeakWindow?.EndUtc,
                localPeakTime = meteorActivity?.PeakWindow?.LocalizedWindowDescription,
                bestViewingWindowLocal = meteorActivity?.VisibilityNotes,
                radiantVisibilityNote = meteorActivity?.VisibilityNotes,
                primaryObjects = context.ProductionEventIntelligence?.PrimaryObjects
                    .Select(o => o.Name)
                    .ToArray() ?? [],
                secondaryObjects = context.ProductionEventIntelligence?.SecondaryObjects
                    .Select(o => o.Name)
                    .ToArray() ?? [],
                missingMeteorActivityInputs = Missing(meteorActivity).ToArray(),
                contextFingerprint = Fingerprint(context),
                meteorActivity = sourceMapping,
                normalization
            });
    }

    public static void RecordAdapter(
        SemanticSourceAdapterContextV1 context,
        SemanticSourceAdapterResultV1 result,
        string capability,
        string sourceId,
        string? family,
        string? format,
        string? beatRole)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        var meteorActivity = context.ProductionEventIntelligence?.MeteorActivity;
        var fingerprint = Fingerprint(context);
        var outcome = result.Candidate is not null ? "candidate" : "rejected";
        var key = string.Join('|', capability, family, format, beatRole, fingerprint, outcome);

        Adapter.AddOrUpdate(
            key,
            _ => new AdapterAggregate(
                capability,
                AdapterId,
                sourceId,
                family,
                format,
                beatRole,
                fingerprint,
                1,
                context.ProductionEventIntelligence is not null,
                meteorActivity is not null,
                !string.IsNullOrWhiteSpace(First(
                    meteorActivity?.RadiantConstellation,
                    meteorActivity?.Radiant)),
                meteorActivity?.PeakWindow is not null,
                result.Candidate is not null,
                result.Rejection is not null,
                result.Rejection?.Reason,
                result.Candidate?.TypedValue.TypeName,
                Summary(result.Candidate?.TypedValue.Value),
                result.Candidate?.Provenance.Length ?? 0,
                result.Candidate?.Confidence,
                null),
            (_, current) => current with
            {
                InvocationCount = current.InvocationCount + 1
            });

        Write(
            "narration-v5/meteor-activity-adapter-diagnostics.json",
            Adapter.Values
                .OrderBy(v => v.ContextFingerprint, StringComparer.Ordinal)
                .ToArray());
    }

    public static void RecordResolution(SemanticResolutionResultV1 result, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(result);

        var meteorActivity = result.Fact.TypedValue?.Value as MeteorActivityValue;
        var key = string.Join('|', result.Fact.Status, result.Fact.WinningAdapterId, fingerprint);

        Resolution.AddOrUpdate(
            key,
            _ => new ResolutionAggregate(
                1,
                result.Fact.Status.ToString(),
                result.Fact.WinningAdapterId,
                result.Fact.WinningSourceId,
                result.Diagnostics.CandidateCount,
                result.Diagnostics.InvokedAdapterIds.ToArray(),
                result.Diagnostics.CandidateEvaluations
                    .Where(e => !e.Eligible)
                    .Select(e => e.AdapterId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                result.Fact.DiagnosticMessage,
                result.Fact.TypedValue is not null,
                result.Fact.TypedValue?.TypeName,
                First(
                    meteorActivity?.RadiantConstellation,
                    meteorActivity?.Radiant),
                meteorActivity?.PeakWindow is not null,
                meteorActivity?.PeakWindow?.LocalizedWindowDescription,
                fingerprint),
            (_, current) => current with
            {
                RequestCount = current.RequestCount + 1
            });

        Write(
            "narration-v5/meteor-activity-resolution-diagnostics.json",
            Resolution.Values
                .OrderBy(v => v.ContextFingerprint, StringComparer.Ordinal)
                .ToArray());
    }

    public static void RecordProjection(
        string requested,
        ResolvedSemanticFactV1 canonical,
        object? projected,
        string fingerprint,
        string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requested);
        ArgumentNullException.ThrowIfNull(canonical);

        var projectedType = projected?.GetType();
        var sourceInputs = projectedType?
            .GetProperty("SourceInputs")?
            .GetValue(projected);

        var sourceInputCount = sourceInputs switch
        {
            Array array => array.Length,
            System.Collections.ICollection collection => collection.Count,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object?>().Count(),
            _ => 0
        };

        var key = string.Join('|', requested, fingerprint, projected is null);

        Projection.AddOrUpdate(
            key,
            _ => new ProjectionAggregate(
                canonical.Status.ToString(),
                canonical.TypedValue is not null,
                requested,
                true,
                projected is null,
                projectedType?.GetProperty("FactType")?.GetValue(projected)?.ToString(),
                projectedType?.GetProperty("SpeakableValue")?.GetValue(projected)?.ToString(),
                projectedType?.GetProperty("SemanticMeaning")?.GetValue(projected)?.ToString(),
                projectedType?.GetProperty("DerivationRuleId")?.GetValue(projected)?.ToString(),
                sourceInputCount,
                reason,
                fingerprint,
                1),
            (_, current) => current with
            {
                InvocationCount = current.InvocationCount + 1
            });

        Write(
            "narration-v5/meteor-activity-projection-diagnostics.json",
            Projection.Values
                .OrderBy(v => v.ContextFingerprint, StringComparer.Ordinal)
                .ThenBy(v => v.RequestedLegacyFact, StringComparer.Ordinal)
                .ToArray());
    }

    public static void RecordBeat(
        string format,
        string scene,
        string role,
        string requested,
        bool available,
        bool required,
        bool optional,
        bool skipped,
        string? reason,
        string fingerprint)
    {
        var key = string.Join(
            '|',
            format,
            scene,
            role,
            requested,
            available,
            required,
            optional,
            skipped,
            reason,
            fingerprint);

        Beat.AddOrUpdate(
            key,
            _ => new BeatAggregate(
                format,
                scene,
                role,
                requested,
                available,
                required,
                optional,
                skipped,
                reason,
                false,
                false,
                $"{requested}|{fingerprint}",
                fingerprint,
                1),
            (_, current) => current with
            {
                InvocationCount = current.InvocationCount + 1
            });

        Write(
            "narration-v5/meteor-activity-beat-assignment-diagnostics.json",
            Beat.Values
                .OrderBy(v => v.ContextFingerprint, StringComparer.Ordinal)
                .ThenBy(v => v.Format, StringComparer.Ordinal)
                .ThenBy(v => v.SceneId, StringComparer.Ordinal)
                .ThenBy(v => v.RequestedLegacyFact, StringComparer.Ordinal)
                .ToArray());
    }


    internal static SemanticLifecycleFailure ClassifyMeteorActivityFailure(
        bool inputPopulated,
        SemanticSourceAdapterContextV1? context,
        IReadOnlyList<string> adapterIds,
        int candidateCount,
        string? canonicalStatus,
        int projectedFactCount,
        int retainedRadiantCount,
        int retainedPeakWindowCount,
        string? contentStrategy,
        string? eventType)
    {
        if (!inputPopulated)
        {
            return Failure(SemanticLifecycleStage.InputPopulation, "MeteorActivity input was not populated before semantic resolution.", context, contentStrategy, eventType);
        }

        if (context?.ProductionEventIntelligence?.MeteorActivity is null)
        {
            return Failure(SemanticLifecycleStage.ContextPopulation, "MeteorActivity was not populated into SemanticSourceAdapterContextV1.", context, contentStrategy, eventType);
        }

        if (adapterIds.Count == 0)
        {
            return Failure(SemanticLifecycleStage.AdapterDiscovery, "No MeteorActivity adapter was discovered.", context, contentStrategy, eventType);
        }

        if (!adapterIds.Contains(AdapterId, StringComparer.Ordinal))
        {
            return Failure(SemanticLifecycleStage.AdapterExecution, "MeteorActivity production adapter was not executed.", context, contentStrategy, eventType);
        }

        if (candidateCount == 0)
        {
            return Failure(SemanticLifecycleStage.CandidateSelection, "No MeteorActivity candidate produced.", context, contentStrategy, eventType);
        }

        if (!string.Equals(canonicalStatus, "Resolved", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(canonicalStatus, "ResolvedByCombination", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(SemanticLifecycleStage.CanonicalResolution, $"MeteorActivity canonical resolution status was {canonicalStatus ?? "unknown"}.", context, contentStrategy, eventType);
        }

        if (projectedFactCount == 0)
        {
            return Failure(SemanticLifecycleStage.CompatibilityProjection, "MeteorActivity did not project required legacy facts.", context, contentStrategy, eventType);
        }

        if (retainedRadiantCount == 0 || retainedPeakWindowCount == 0)
        {
            return Failure(SemanticLifecycleStage.BeatRetention, "Projected Radiant/PeakWindow facts were not retained in resolver beats.", context, contentStrategy, eventType);
        }

        return Failure(SemanticLifecycleStage.NarrationGeneration, "MeteorActivity lifecycle reached narration generation without a known earlier blocker.", context, contentStrategy, eventType);
    }

    private static SemanticLifecycleFailure Failure(SemanticLifecycleStage stage, string reason, SemanticSourceAdapterContextV1? context, string? contentStrategy, string? eventType)
        => new(stage, reason, new Dictionary<string, object?>
        {
            ["ContentStrategy"] = contentStrategy,
            ["EventType"] = eventType ?? context?.EventIdentity?.SourceEventType,
            ["MeteorActivity"] = context?.ProductionEventIntelligence?.MeteorActivity,
            ["SourceExternalEventId"] = context?.EventIdentity?.SourceEventId,
            ["TimeZone"] = context?.TimeZone,
            ["PrimaryObjects"] = context?.ProductionEventIntelligence?.PrimaryObjects.Select(o => o.Name).ToArray() ?? [],
            ["SecondaryObjects"] = context?.ProductionEventIntelligence?.SecondaryObjects.Select(o => o.Name).ToArray() ?? []
        });

    private static IEnumerable<string> Missing(MeteorActivityValue? meteorActivity)
    {
        if (meteorActivity is null)
        {
            yield return "MeteorActivity";
            yield break;
        }

        if (string.IsNullOrWhiteSpace(First(
                meteorActivity.RadiantConstellation,
                meteorActivity.Radiant)))
        {
            yield return "RadiantConstellation";
        }

        if (meteorActivity.PeakWindow is null)
        {
            yield return "PeakWindow";
        }
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? Summary(object? value) =>
        value is MeteorActivityValue meteorActivity
            ? $"radiant={First(meteorActivity.RadiantConstellation, meteorActivity.Radiant)}; " +
              $"peakWindow={meteorActivity.PeakWindow?.LocalizedWindowDescription}"
            : value?.ToString();

    private static void Write(string path, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(value);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var writeLock = WriteLocks.GetOrAdd(fullPath, static _ => new object());

        try
        {
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(value, Options);

            lock (writeLock)
            {
                WriteAtomically(fullPath, json);
            }
        }
        catch (IOException)
        {
            // Diagnostics are best-effort and must never fail tests or production.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostics are best-effort and must never fail tests or production.
        }
        catch (NotSupportedException)
        {
            // Invalid or unsupported diagnostic path must not break the pipeline.
        }
    }

    private static void WriteAtomically(string destinationPath, string content)
    {
        var directory = Path.GetDirectoryName(destinationPath)!;
        var fileName = Path.GetFileName(destinationPath);
        var temporaryPath = Path.Combine(
            directory,
            $".{fileName}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, content, Encoding.UTF8);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup only.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup only.
            }
        }
    }

    private sealed record AdapterAggregate(
        string Capability,
        string AdapterId,
        string SourceId,
        string? Family,
        string? Format,
        string? BeatRole,
        string ContextFingerprint,
        int InvocationCount,
        bool ProductionEventIntelligencePresent,
        bool MeteorActivityPresent,
        bool RadiantPresent,
        bool PeakWindowPresent,
        bool CandidateEmitted,
        bool CandidateRejected,
        string? RejectionReason,
        string? TypedValueType,
        string? TypedValueSummary,
        int EvidenceCount,
        decimal? Confidence,
        string? RequirementLevel);

    private sealed record ResolutionAggregate(
        int RequestCount,
        string Status,
        string? WinningAdapterId,
        string? WinningSourceId,
        int CandidateCount,
        string[] InvokedAdapterIds,
        string[] RejectedAdapterIds,
        string DiagnosticMessage,
        bool TypedValuePresent,
        string? TypedValueType,
        string? RadiantConstellation,
        bool PeakWindowPresent,
        string? PeakWindowValue,
        string ContextFingerprint);

    private sealed record ProjectionAggregate(
        string CanonicalStatus,
        bool CanonicalTypedValuePresent,
        string RequestedLegacyFact,
        bool MapperCalled,
        bool MapperReturnedNull,
        string? ProjectedFactType,
        string? ProjectedSpeakableValue,
        string? SemanticMeaning,
        string? DerivationRuleId,
        int SourceInputCount,
        string? ProjectionRejectionReason,
        string ContextFingerprint,
        int InvocationCount);

    private sealed record BeatAggregate(
        string Format,
        string SceneId,
        string BeatRole,
        string RequestedLegacyFact,
        bool ProjectedFactAvailable,
        bool AssignedToRequiredFacts,
        bool AssignedToOptionalFacts,
        bool Skipped,
        string? SkipReason,
        bool Overwritten,
        bool Deduplicated,
        string DeduplicationKey,
        string ContextFingerprint,
        int InvocationCount);
}