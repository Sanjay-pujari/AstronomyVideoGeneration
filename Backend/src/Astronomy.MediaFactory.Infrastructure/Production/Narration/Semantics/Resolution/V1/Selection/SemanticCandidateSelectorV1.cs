using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Selection;

public interface ISemanticCandidateSelectorV1
{
    SemanticCandidateSelectionV1 Select(SemanticResolutionRequestV1 request, SemanticSourcePolicyV1 policy, IEnumerable<SemanticSourceCandidateV1> candidates, IEnumerable<SemanticCandidateEvaluationV1> evaluations, SemanticConflictSetV1 conflicts);
}

public sealed class SemanticCandidateSelectorV1 : ISemanticCandidateSelectorV1
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> SemanticPropertyCache = new();

    public SemanticCandidateSelectionV1 Select(SemanticResolutionRequestV1 r, SemanticSourcePolicyV1 p, IEnumerable<SemanticSourceCandidateV1> cand, IEnumerable<SemanticCandidateEvaluationV1> evals, SemanticConflictSetV1 conflicts)
    {
        var es = evals.Where(e => e.Eligible).OrderBy(e => e.CandidateId, StringComparer.Ordinal).ToArray();
        var all = cand.ToArray();
        var rejected = all.Where(c => !es.Any(e => e.AdapterId == c.AdapterId && e.SourceId == c.SourceId && e.CapabilityId.Equals(c.CapabilityId))).ToImmutableArray();
        if (es.Length == 0) return new(r.Required ? SemanticResolutionStatusV1.InsufficientEvidence : SemanticResolutionStatusV1.UnavailableOptional, null, rejected, [SemanticTieBreakReasonV1.NoSelection], p.MultipleCandidatePolicy.ToString(), "NoEligibleCandidate", "No eligible candidate.");
        if (conflicts.HasBlockingMaterialConflict && (p.ConflictPolicy == SemanticSourceConflictPolicyV1.BlockRequired || r.Required)) return new(r.Required ? SemanticResolutionStatusV1.ConflictBlocked : SemanticResolutionStatusV1.UnavailableOptional, null, rejected, [SemanticTieBreakReasonV1.NoSelection], p.MultipleCandidatePolicy.ToString(), "ConflictBlocked", "Material unresolved conflict blocked selection.");
        if (p.MultipleCandidatePolicy == SemanticSourceMultiplicityV1.RejectMultiple && es.Length > 1) return new(SemanticResolutionStatusV1.ConflictBlocked, null, rejected, [SemanticTieBreakReasonV1.NoSelection], p.MultipleCandidatePolicy.ToString(), "RejectMultiple", "Multiple candidates rejected by policy.");
        if (p.MultipleCandidatePolicy == SemanticSourceMultiplicityV1.RequireAgreement && conflicts.Conflicts.Any(c => c.Material)) return new(r.Required ? SemanticResolutionStatusV1.ConflictBlocked : SemanticResolutionStatusV1.UnavailableOptional, null, rejected, [SemanticTieBreakReasonV1.NoSelection], p.MultipleCandidatePolicy.ToString(), "AgreementRequired", "Candidate agreement is required.");
        var reasons = new List<SemanticTieBreakReasonV1>();
        SemanticCandidateEvaluationV1 win;
        if (p.MultipleCandidatePolicy == SemanticSourceMultiplicityV1.FirstApprovedByPriority)
        {
            win = es.OrderBy(e => e.SourcePriority).ThenBy(e => e.AdapterId, StringComparer.Ordinal).First();
            reasons.Add(SemanticTieBreakReasonV1.SourcePriority);
        }
        else if (p.MultipleCandidatePolicy == SemanticSourceMultiplicityV1.CombineStructuredFields && es.Length > 1)
        {
            var combined = Combine(all.Where(c => es.Any(e => e.AdapterId == c.AdapterId)).ToArray(), p);
            return new(SemanticResolutionStatusV1.ResolvedByCombination, combined, rejected, [SemanticTieBreakReasonV1.StructuredCombination], p.MultipleCandidatePolicy.ToString(), "ResolvedByCombination", "Structured fields combined.", true);
        }
        else
        {
            win = es.OrderByDescending(e => e.EvidenceStrength)
                .ThenBy(e => e.SourcePriority)
                .ThenByDescending(e => e.ProvenanceComplete)
                .ThenBy(e => e.CompatibilityOnly)
                .ThenByDescending(e => Completeness(all.First(c => c.AdapterId == e.AdapterId).TypedValue.Value))
                .ThenByDescending(e => e.Confidence)
                .ThenBy(e => e.AdapterId, StringComparer.Ordinal)
                .ThenBy(e => e.CandidateId, StringComparer.Ordinal)
                .First();
            reasons.AddRange([SemanticTieBreakReasonV1.HigherEvidenceStrength, SemanticTieBreakReasonV1.SourcePriority, SemanticTieBreakReasonV1.ProvenanceCompleteness, SemanticTieBreakReasonV1.NonCompatibilitySource, SemanticTieBreakReasonV1.StructuredCompleteness, SemanticTieBreakReasonV1.Confidence, SemanticTieBreakReasonV1.AdapterIdOrdinal, SemanticTieBreakReasonV1.CandidateIdOrdinal]);
        }

        var selected = all.First(c => c.AdapterId == win.AdapterId && c.SourceId == win.SourceId);
        return new(SemanticResolutionStatusV1.Resolved, selected, rejected, reasons.ToImmutableArray(), p.MultipleCandidatePolicy.ToString(), "Resolved", "Candidate selected.");
    }

    public static int Completeness(object? value)
    {
        if (value is null) return 0;

        var type = value.GetType();
        if (IsScalar(type)) return IsMeaningfulScalar(value) ? 1 : 0;
        if (value is JsonElement json) return JsonElementCompleteness(json);
        if (IsCollection(type) && value is IEnumerable enumerable) return CollectionCompleteness(enumerable);

        var properties = GetSemanticReadableProperties(type);
        if (properties.Length == 0) return 0;

        return properties.Count(p => IsMeaningfulValue(p.GetValue(value)));
    }

    private static bool IsMeaningfulValue(object? value)
    {
        if (value is null) return false;
        var type = value.GetType();
        if (IsScalar(type)) return IsMeaningfulScalar(value);
        if (value is JsonElement json) return JsonElementCompleteness(json) > 0;
        if (IsCollection(type) && value is IEnumerable enumerable) return CollectionCompleteness(enumerable) > 0;
        return true;
    }

    private static bool IsMeaningfulScalar(object value) => value switch
    {
        string s => !string.IsNullOrWhiteSpace(s),
        JsonElement json => JsonElementCompleteness(json) > 0,
        _ => true
    };

    private static bool IsScalar(Type type)
    {
        var effective = Nullable.GetUnderlyingType(type) ?? type;
        return effective.IsPrimitive
            || effective.IsEnum
            || effective == typeof(string)
            || effective == typeof(decimal)
            || effective == typeof(DateTime)
            || effective == typeof(DateTimeOffset)
            || effective == typeof(TimeSpan)
            || effective == typeof(Guid)
            || effective == typeof(Uri);
    }

    private static bool IsCollection(Type type) => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    private static int CollectionCompleteness(IEnumerable enumerable)
    {
        if (enumerable is ICollection collection) return collection.Count > 0 ? 1 : 0;

        var enumerator = enumerable.GetEnumerator();
        try
        {
            return enumerator.MoveNext() ? 1 : 0;
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private static int JsonElementCompleteness(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => 0,
        JsonValueKind.String => string.IsNullOrWhiteSpace(json.GetString()) ? 0 : 1,
        JsonValueKind.Array => json.GetArrayLength() > 0 ? 1 : 0,
        JsonValueKind.Object => json.EnumerateObject().Any() ? 1 : 0,
        _ => 1
    };

    private static PropertyInfo[] GetSemanticReadableProperties(Type type) => SemanticPropertyCache.GetOrAdd(type, static t =>
        t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetMethod is { IsStatic: false })
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => !IsInfrastructureProperty(p))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray());

    private static bool IsInfrastructureProperty(PropertyInfo property) => property.Name is "EqualityContract";

    private static SemanticSourceCandidateV1 Combine(SemanticSourceCandidateV1[] cs, SemanticSourcePolicyV1 p)
    {
        var best = cs.OrderByDescending(c => c.EvidenceStrength).ThenBy(c => p.ApprovedSources.First(s => s.SourceId == c.SourceId).Priority).First();
        return best with { AdapterId = "combined." + best.AdapterId, SourceId = string.Join('+', cs.Select(c => c.SourceId).Distinct().Order()), Provenance = cs.SelectMany(c => c.Provenance).ToImmutableArray(), Warnings = best.Warnings.Add("Field-level provenance retained from combined sources.") };
    }
}
