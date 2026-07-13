using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

public sealed record FamilyNarrativeBeatV1
{
    public FamilyNarrativeBeatV1(string beatId, string beatRole, int order, string purpose, IReadOnlyList<FamilySemanticRequirementV1> requirements, string? optionalEditorialIntent, bool allowsOmission)
        : this(beatId, beatRole, order, purpose, requirements.ToImmutableArray(), optionalEditorialIntent, allowsOmission) { }
    [JsonConstructor]
    public FamilyNarrativeBeatV1(string beatId, string beatRole, int order, string purpose, ImmutableArray<FamilySemanticRequirementV1> requirements, string? optionalEditorialIntent, bool allowsOmission)
    { BeatId = beatId; BeatRole = beatRole; Order = order; Purpose = purpose; Requirements = requirements.IsDefault ? [] : requirements; OptionalEditorialIntent = optionalEditorialIntent; AllowsOmission = allowsOmission; }
    public string BeatId { get; init; }
    public string BeatRole { get; init; }
    public int Order { get; init; }
    public string Purpose { get; init; }
    public ImmutableArray<FamilySemanticRequirementV1> Requirements { get; init; }
    public string? OptionalEditorialIntent { get; init; }
    public bool AllowsOmission { get; init; }
    public bool Equals(FamilyNarrativeBeatV1? other) => other is not null && BeatId == other.BeatId && BeatRole == other.BeatRole && Order == other.Order && Purpose == other.Purpose && Requirements.SequenceEqual(other.Requirements) && OptionalEditorialIntent == other.OptionalEditorialIntent && AllowsOmission == other.AllowsOmission;
    public override int GetHashCode() => Requirements.Aggregate(HashCode.Combine(BeatId, BeatRole, Order, Purpose, OptionalEditorialIntent, AllowsOmission), (h, r) => HashCode.Combine(h, r));
}
