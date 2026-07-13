using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

public sealed record FamilyNarrativeStructureV1
{
    public FamilyNarrativeStructureV1(string format, IReadOnlyList<FamilyNarrativeBeatV1> beats) : this(format, beats.ToImmutableArray()) { }
    [JsonConstructor]
    public FamilyNarrativeStructureV1(string format, ImmutableArray<FamilyNarrativeBeatV1> beats) { Format = format; Beats = beats.IsDefault ? [] : beats; }
    public string Format { get; init; }
    public ImmutableArray<FamilyNarrativeBeatV1> Beats { get; init; }
    public bool Equals(FamilyNarrativeStructureV1? other) => other is not null && Format == other.Format && Beats.SequenceEqual(other.Beats);
    public override int GetHashCode() => Beats.Aggregate(Format.GetHashCode(), (h, b) => HashCode.Combine(h, b));
}
