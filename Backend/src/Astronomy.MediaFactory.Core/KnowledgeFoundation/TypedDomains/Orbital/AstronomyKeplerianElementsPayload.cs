using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

public sealed record AstronomyKeplerianElementsPayload : ITypedAstronomyKnowledgePayload, IEquatable<AstronomyKeplerianElementsPayload>
{
    public AstronomyKeplerianElementsPayload(AstronomyKnowledgeTypeId typeId, AstronomyOrbitalReferenceContext referenceContext, IEnumerable<AstronomyKeplerianElement> elements)
    {
        if (!typeId.IsValid) throw new ArgumentException("Keplerian elements payload type ID is required.", nameof(typeId));
        TypeId = typeId;
        ReferenceContext = referenceContext ?? throw new ArgumentNullException(nameof(referenceContext));
        Elements = CopyElements(elements);
    }

    public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Orbital;
    public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.OrbitalParameter;
    public AstronomyKnowledgeTypeId TypeId { get; }
    public AstronomyOrbitalReferenceContext ReferenceContext { get; }
    public IReadOnlyList<AstronomyKeplerianElement> Elements { get; }

    public bool Equals(AstronomyKeplerianElementsPayload? other) => other is not null && TypeId == other.TypeId && ReferenceContext == other.ReferenceContext && Elements.SequenceEqual(other.Elements);
    public override int GetHashCode() => Elements.Aggregate(HashCode.Combine(TypeId, ReferenceContext), HashCode.Combine);

    private static IReadOnlyList<AstronomyKeplerianElement> CopyElements(IEnumerable<AstronomyKeplerianElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var ordered = elements.Select(element => element ?? throw new ArgumentException("Keplerian elements cannot contain null entries.", nameof(elements))).OrderBy(element => element.ElementType).ToArray();
        if (ordered.Length == 0) throw new ArgumentException("At least one Keplerian element is required.", nameof(elements));
        if (ordered.GroupBy(element => element.ElementType).Any(group => group.Count() > 1)) throw new ArgumentException("Keplerian elements must be unique by element type.", nameof(elements));
        return Array.AsReadOnly(ordered);
    }
}
