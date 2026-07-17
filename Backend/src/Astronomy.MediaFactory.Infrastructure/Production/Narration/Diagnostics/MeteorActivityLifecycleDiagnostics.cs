using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Diagnostics;

public static class MeteorActivityLifecycleDiagnostics
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly ConcurrentDictionary<string, AdapterAggregate> Adapter = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ResolutionAggregate> Resolution = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ProjectionAggregate> Projection = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, BeatAggregate> Beat = new(StringComparer.Ordinal);
    public const string AdapterId = "v1.meteor-activity.production-event-intelligence";

    public static string Fingerprint(SemanticSourceAdapterContextV1 c)
    {
        var m = c.ProductionEventIntelligence?.MeteorActivity;
        var payload = JsonSerializer.Serialize(new { c.EventIdentity?.SourceEventType, c.EventIdentity?.ShortTitle, c.EventIdentity?.SourceEventId, radiant = First(m?.RadiantConstellation, m?.Radiant), peakWindow = m?.PeakWindow?.LocalizedWindowDescription, m?.PeakWindow?.PeakUtc, bestViewingWindowLocal = m?.VisibilityNotes, primaryObjects = c.ProductionEventIntelligence?.PrimaryObjects.Select(o => o.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() ?? [] }, Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16].ToLowerInvariant();
    }

    public static void WriteContext(SemanticSourceAdapterContextV1 c, object sourceMapping, object? normalization)
    {
        var m = c.ProductionEventIntelligence?.MeteorActivity;
        Write("narration-v5/meteor-activity-context-diagnostics.json", new { productionRequestPresent = c.EventIdentity is not null, productionEventIntelligencePresent = c.ProductionEventIntelligence is not null, meteorActivityPresent = m is not null, eventType = c.EventIdentity?.SourceEventType, title = c.EventIdentity?.Title, shortTitle = c.EventIdentity?.ShortTitle, sourceExternalEventId = c.EventIdentity?.SourceEventId, radiantConstellation = First(m?.RadiantConstellation, m?.Radiant), peakWindowPresent = m?.PeakWindow is not null, peakWindowValue = m?.PeakWindow?.LocalizedWindowDescription, startUtc = m?.PeakWindow?.StartUtc, peakUtc = m?.PeakWindow?.PeakUtc, endUtc = m?.PeakWindow?.EndUtc, localPeakTime = m?.PeakWindow?.LocalizedWindowDescription, bestViewingWindowLocal = m?.VisibilityNotes, radiantVisibilityNote = m?.VisibilityNotes, primaryObjects = c.ProductionEventIntelligence?.PrimaryObjects.Select(o => o.Name).ToArray() ?? [], secondaryObjects = c.ProductionEventIntelligence?.SecondaryObjects.Select(o => o.Name).ToArray() ?? [], missingMeteorActivityInputs = Missing(m).ToArray(), contextFingerprint = Fingerprint(c), meteorActivity = sourceMapping, normalization });
    }

    public static void RecordAdapter(SemanticSourceAdapterContextV1 c, SemanticSourceAdapterResultV1 r, string capability, string sourceId, string? family, string? format, string? beatRole)
    {
        var m = c.ProductionEventIntelligence?.MeteorActivity; var fp = Fingerprint(c); var outcome = r.Candidate is not null ? "candidate" : "rejected"; var key = string.Join('|', capability, family, format, beatRole, fp, outcome);
        Adapter.AddOrUpdate(key, _ => new AdapterAggregate(capability, AdapterId, sourceId, family, format, beatRole, fp, 1, c.ProductionEventIntelligence is not null, m is not null, !string.IsNullOrWhiteSpace(First(m?.RadiantConstellation, m?.Radiant)), m?.PeakWindow is not null, r.Candidate is not null, r.Rejection is not null, r.Rejection?.Reason, r.Candidate?.TypedValue.TypeName, Summary(r.Candidate?.TypedValue.Value), r.Candidate?.Provenance.Length ?? 0, r.Candidate?.Confidence, null), (_, a) => a with { InvocationCount = a.InvocationCount + 1 });
        Write("narration-v5/meteor-activity-adapter-diagnostics.json", Adapter.Values.OrderBy(v => v.ContextFingerprint).ToArray());
    }

    public static void RecordResolution(SemanticResolutionResultV1 r, string fp)
    {
        var m = r.Fact.TypedValue?.Value as MeteorActivityValue; var key = string.Join('|', r.Fact.Status, r.Fact.WinningAdapterId, fp);
        Resolution.AddOrUpdate(key, _ => new ResolutionAggregate(1, r.Fact.Status.ToString(), r.Fact.WinningAdapterId, r.Fact.WinningSourceId, r.Diagnostics.CandidateCount, r.Diagnostics.InvokedAdapterIds.ToArray(), r.Diagnostics.CandidateEvaluations.Where(e => !e.Eligible).Select(e => e.AdapterId).Distinct().ToArray(), r.Fact.DiagnosticMessage, r.Fact.TypedValue is not null, r.Fact.TypedValue?.TypeName, First(m?.RadiantConstellation, m?.Radiant), m?.PeakWindow is not null, m?.PeakWindow?.LocalizedWindowDescription, fp), (_, a) => a with { RequestCount = a.RequestCount + 1 });
        Write("narration-v5/meteor-activity-resolution-diagnostics.json", Resolution.Values.ToArray());
    }

    public static void RecordProjection(string requested, ResolvedSemanticFactV1 canonical, object? projected, string fp, string? reason) { var key = requested + "|" + fp + "|" + (projected is null); Projection.AddOrUpdate(key, _ => new ProjectionAggregate(canonical.Status.ToString(), canonical.TypedValue is not null, requested, true, projected is null, projected?.GetType().GetProperty("FactType")?.GetValue(projected)?.ToString(), projected?.GetType().GetProperty("SpeakableValue")?.GetValue(projected)?.ToString(), projected?.GetType().GetProperty("SemanticMeaning")?.GetValue(projected)?.ToString(), projected?.GetType().GetProperty("DerivationRuleId")?.GetValue(projected)?.ToString(), (projected?.GetType().GetProperty("SourceInputs")?.GetValue(projected) as Array)?.Length ?? 0, reason, fp, 1), (_, a) => a with { InvocationCount = a.InvocationCount + 1 }); Write("narration-v5/meteor-activity-projection-diagnostics.json", Projection.Values.ToArray()); }
    public static void RecordBeat(string format,string scene,string role,string requested,bool available,bool req,bool opt,bool skipped,string? reason,string fp){ var key=string.Join('|',format,scene,role,requested,available,req,opt,skipped,reason,fp); Beat.AddOrUpdate(key,_=>new BeatAggregate(format,scene,role,requested,available,req,opt,skipped,reason,false,false,requested+"|"+fp,fp,1),(_,a)=>a with{InvocationCount=a.InvocationCount+1}); Write("narration-v5/meteor-activity-beat-assignment-diagnostics.json", Beat.Values.ToArray()); }
    private static IEnumerable<string> Missing(MeteorActivityValue? m){ if(m is null){yield return "MeteorActivity"; yield break;} if(string.IsNullOrWhiteSpace(First(m.RadiantConstellation,m.Radiant))) yield return "RadiantConstellation"; if(m.PeakWindow is null) yield return "PeakWindow"; }
    private static string? First(params string?[] v)=>v.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
    private static string? Summary(object? v)=>v is MeteorActivityValue m?$"radiant={First(m.RadiantConstellation,m.Radiant)}; peakWindow={m.PeakWindow?.LocalizedWindowDescription}":v?.ToString();
    private static void Write(string path, object value){ Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(value, Options)); }
    private sealed record AdapterAggregate(string Capability,string AdapterId,string SourceId,string? Family,string? Format,string? BeatRole,string ContextFingerprint,int InvocationCount,bool ProductionEventIntelligencePresent,bool MeteorActivityPresent,bool RadiantPresent,bool PeakWindowPresent,bool CandidateEmitted,bool CandidateRejected,string? RejectionReason,string? TypedValueType,string? TypedValueSummary,int EvidenceCount,decimal? Confidence,string? RequirementLevel);
    private sealed record ResolutionAggregate(int RequestCount,string Status,string? WinningAdapterId,string? WinningSourceId,int CandidateCount,string[] InvokedAdapterIds,string[] RejectedAdapterIds,string DiagnosticMessage,bool TypedValuePresent,string? TypedValueType,string? RadiantConstellation,bool PeakWindowPresent,string? PeakWindowValue,string ContextFingerprint);
    private sealed record ProjectionAggregate(string CanonicalStatus,bool CanonicalTypedValuePresent,string RequestedLegacyFact,bool MapperCalled,bool MapperReturnedNull,string? ProjectedFactType,string? ProjectedSpeakableValue,string? SemanticMeaning,string? DerivationRuleId,int SourceInputCount,string? ProjectionRejectionReason,string ContextFingerprint,int InvocationCount);
    private sealed record BeatAggregate(string Format,string SceneId,string BeatRole,string RequestedLegacyFact,bool ProjectedFactAvailable,bool AssignedToRequiredFacts,bool AssignedToOptionalFacts,bool Skipped,string? SkipReason,bool Overwritten,bool Deduplicated,string DeduplicationKey,string ContextFingerprint,int InvocationCount);
}
