using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Certification;

public sealed record SemanticSourcePolicyCertificationEntryV1(
    string FamilyId,
    string CapabilityId,
    bool Required,
    SemanticSourceCertificationStatusV1 Status,
    string DiagnosticMessage);

public sealed record SemanticSourcePolicyCertificationReportV1 : IEquatable<SemanticSourcePolicyCertificationReportV1>
{
    public SemanticSourcePolicyCertificationReportV1(
        string familyId,
        SemanticSourceCertificationStatusV1 status,
        IReadOnlyCollection<SemanticSourcePolicyCertificationEntryV1> entries,
        IReadOnlyCollection<string> optionalGaps,
        IReadOnlyCollection<string> blockers)
        : this(familyId, status, entries.ToImmutableArray(), optionalGaps.ToImmutableArray(), blockers.ToImmutableArray())
    {
    }

    [JsonConstructor]
    public SemanticSourcePolicyCertificationReportV1(
        string familyId,
        SemanticSourceCertificationStatusV1 status,
        ImmutableArray<SemanticSourcePolicyCertificationEntryV1> entries,
        ImmutableArray<string> optionalGaps,
        ImmutableArray<string> blockers)
    {
        FamilyId = familyId;
        Status = status;
        Entries = entries.IsDefault ? [] : entries;
        OptionalGaps = optionalGaps.IsDefault ? [] : optionalGaps;
        Blockers = blockers.IsDefault ? [] : blockers;
    }

    public string FamilyId { get; init; }
    public SemanticSourceCertificationStatusV1 Status { get; init; }
    public ImmutableArray<SemanticSourcePolicyCertificationEntryV1> Entries { get; init; }
    public ImmutableArray<string> OptionalGaps { get; init; }
    public ImmutableArray<string> Blockers { get; init; }

    public bool Equals(SemanticSourcePolicyCertificationReportV1? other) =>
        other is not null &&
        FamilyId == other.FamilyId &&
        Status == other.Status &&
        Entries.SequenceEqual(other.Entries) &&
        OptionalGaps.SequenceEqual(other.OptionalGaps) &&
        Blockers.SequenceEqual(other.Blockers);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FamilyId);
        hash.Add(Status);
        AddRangeHash(ref hash, Entries);
        AddRangeHash(ref hash, OptionalGaps);
        AddRangeHash(ref hash, Blockers);
        return hash.ToHashCode();
    }

    private static void AddRangeHash<T>(ref HashCode hash, ImmutableArray<T> values)
    {
        hash.Add(values.Length);
        foreach (var value in values)
        {
            hash.Add(value);
        }
    }
}
