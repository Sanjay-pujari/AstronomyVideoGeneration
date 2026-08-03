using System.Globalization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Scope-first conservative classifier. Comparison metadata can compare facts but cannot establish scope.</summary>
public sealed class Phase7KnowledgeMergeClassifier : IPhase7KnowledgeMergeClassifier
{
    private static readonly Regex Number = new(@"[-+]?\d+(?:\.\d+)?", RegexOptions.Compiled);
    private readonly IPhase7KnowledgeScopeComparer scopeComparer;
    public Phase7KnowledgeMergeClassifier() : this(new Phase7KnowledgeScopeComparer()) { }
    public Phase7KnowledgeMergeClassifier(IPhase7KnowledgeScopeComparer scopeComparer) => this.scopeComparer = scopeComparer;

    public Phase7KnowledgeMergeResult Classify(Phase7KnowledgeMergeRequest request)
    {
        if (!string.Equals(request.EvergreenCandidate.SemanticIdentity, request.EventCandidate.SemanticIdentity, StringComparison.Ordinal)
            || request.EvergreenCandidate.Domain != request.EventCandidate.Domain
            || !string.Equals(Phase7CanonicalFieldPathPolicy.Canonicalize(request.EvergreenCandidate.ApprovedFieldPath),
                Phase7CanonicalFieldPathPolicy.Canonicalize(request.EventCandidate.ApprovedFieldPath), StringComparison.Ordinal))
            return Incomparable("Semantic identity, domain, and approved field must match before merge classification.");

        var scope = scopeComparer.Compare(request.EvergreenScope, request.EventScope);
        if (scope == Phase7KnowledgeScopeComparison.DistinctNonConflictingScopes)
            return Incomparable("The scope comparer established distinct non-conflicting authority scopes.");
        if (scope == Phase7KnowledgeScopeComparison.ConflictingScope)
            return Block("The candidates assert conflicting authority scopes.");

        var fact = CompareValues(request.EvergreenComparisonMetadata, request.EventComparisonMetadata);
        if (fact == ValueComparison.UnitMismatch)
            return Incomparable("The values cannot be compared authoritatively because their governed units differ.");
        if (scope == Phase7KnowledgeScopeComparison.EventIsSpecialization)
            return fact == ValueComparison.Conflict
                ? Block("The execution-scoped value contradicts the general fact.")
                : Result(Phase7KnowledgeMergeClassification.EventSpecificSpecialization, "True authority fields establish a narrower event scope.");
        if (fact == ValueComparison.Equal)
            return Precision(request.EvergreenComparisonMetadata, request.EventComparisonMetadata);
        if (fact == ValueComparison.Conflict && scope == Phase7KnowledgeScopeComparison.SameScope)
            return Block("Typed normalized values conflict under the same authority scope.");

        // Prose is intentionally only a conservative equality fallback. It cannot create scope.
        if (Normalize(request.EvergreenCandidate.Text) == Normalize(request.EventCandidate.Text))
            return Result(Phase7KnowledgeMergeClassification.Equivalent, "Conservative normalized prose comparison is equal.");
        return Incomparable("No governed evidence establishes equivalence, precision, or contradiction; human review is required.");
    }

    private static Phase7KnowledgeMergeResult Precision(Phase7KnowledgeComparisonMetadata evergreen, Phase7KnowledgeComparisonMetadata @event)
    {
        var en = Numeric(evergreen.NormalizedValue); var ev = Numeric(@event.NormalizedValue);
        var eventBetter = evergreen.Approximation == true && @event.Approximation != true
            || evergreen.Uncertainty.HasValue && @event.Uncertainty.HasValue && @event.Uncertainty < evergreen.Uncertainty
            || en.HasValue && ev.HasValue && ev.Value.Decimals > en.Value.Decimals
            || evergreen.Confidence.HasValue && @event.Confidence.HasValue && @event.Confidence > evergreen.Confidence;
        var evergreenBetter = @event.Approximation == true && evergreen.Approximation != true
            || evergreen.Uncertainty.HasValue && @event.Uncertainty.HasValue && evergreen.Uncertainty < @event.Uncertainty
            || en.HasValue && ev.HasValue && en.Value.Decimals > ev.Value.Decimals
            || evergreen.Confidence.HasValue && @event.Confidence.HasValue && evergreen.Confidence > @event.Confidence;
        if (eventBetter && !evergreenBetter) return Result(Phase7KnowledgeMergeClassification.EventMorePrecise, "The event expresses greater governed precision.");
        if (evergreenBetter && !eventBetter) return Result(Phase7KnowledgeMergeClassification.EvergreenMorePrecise, "The evergreen authority expresses greater governed precision.");
        return Result(Phase7KnowledgeMergeClassification.Equivalent, "Typed normalized facts and units are equivalent.");
    }

    private static ValueComparison CompareValues(Phase7KnowledgeComparisonMetadata a, Phase7KnowledgeComparisonMetadata b)
    {
        if (string.IsNullOrWhiteSpace(a.NormalizedValue) || string.IsNullOrWhiteSpace(b.NormalizedValue)) return ValueComparison.InsufficientEvidence;
        if (!string.IsNullOrWhiteSpace(a.ValueType) && !string.IsNullOrWhiteSpace(b.ValueType)
            && !string.Equals(a.ValueType, b.ValueType, StringComparison.OrdinalIgnoreCase)) return ValueComparison.Conflict;
        if (!string.IsNullOrWhiteSpace(a.Unit) && !string.IsNullOrWhiteSpace(b.Unit)
            && !string.Equals(a.Unit, b.Unit, StringComparison.OrdinalIgnoreCase)) return ValueComparison.UnitMismatch;

        if (string.Equals(Normalize(a.NormalizedValue), Normalize(b.NormalizedValue), StringComparison.Ordinal))
            return ValueComparison.Equal;

        var numericA = Numeric(a.NormalizedValue);
        var numericB = Numeric(b.NormalizedValue);
        if (numericA.HasValue && numericB.HasValue)
            return numericA.Value.Value == numericB.Value.Value ? ValueComparison.Equal : ValueComparison.Conflict;

        return ValueComparison.Conflict;
    }
    private enum ValueComparison { Equal, Conflict, UnitMismatch, InsufficientEvidence }
    private static Phase7KnowledgeMergeResult Result(Phase7KnowledgeMergeClassification c, string reason) => new(c, reason, [], []);
    private static Phase7KnowledgeMergeResult Block(string reason) => new(Phase7KnowledgeMergeClassification.Contradictory, reason, [], ["P7KNOWLEDGE_CONTRADICTION"]);
    private static Phase7KnowledgeMergeResult Incomparable(string reason) => new(Phase7KnowledgeMergeClassification.Incomparable, reason, ["P7KNOWLEDGE_INCOMPARABLE_REQUIRES_HUMAN_REVIEW"], []);
    private static string Normalize(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ").TrimEnd('.');
    private static (decimal Value, int Decimals)? Numeric(string? value) { if (value is null) return null; var m=Number.Match(value); if (!m.Success || !decimal.TryParse(m.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) return null; var dot=m.Value.IndexOf('.'); return (n, dot < 0 ? 0 : m.Value.Length-dot-1); }
}
