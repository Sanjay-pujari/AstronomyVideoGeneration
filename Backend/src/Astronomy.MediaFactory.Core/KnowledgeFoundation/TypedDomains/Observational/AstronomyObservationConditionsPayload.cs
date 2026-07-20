using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

public sealed record AstronomyObservationConditionsPayload : ITypedAstronomyKnowledgePayload, IEquatable<AstronomyObservationConditionsPayload>
{
    public AstronomyObservationConditionsPayload(
        AstronomyKnowledgeTypeId typeId,
        AstronomyObservationContext observationContext,
        AstronomyObservationConditions conditions,
        IEnumerable<AstronomyObservationalQuantity>? quantities = null,
        AstronomyHorizontalObservationCoordinate? horizontalCoordinate = null,
        AstronomyHorizonSector? horizonSector = null)
    {
        if (!typeId.IsValid) throw new ArgumentException("Observation conditions payload type ID is required.", nameof(typeId));
        TypeId = typeId;
        ObservationContext = observationContext ?? throw new ArgumentNullException(nameof(observationContext));
        Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        Quantities = CopyQuantities(quantities ?? []);
        HorizontalCoordinate = horizontalCoordinate;
        HorizonSector = horizonSector.HasValue ? EnumGuard.RequireDefined(horizonSector.Value, nameof(horizonSector)) : null;
    }
    public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Observational;
    public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.ObservationCondition;
    public AstronomyKnowledgeTypeId TypeId { get; }
    public AstronomyObservationContext ObservationContext { get; }
    public AstronomyObservationConditions Conditions { get; }
    public IReadOnlyList<AstronomyObservationalQuantity> Quantities { get; }
    public AstronomyHorizontalObservationCoordinate? HorizontalCoordinate { get; }
    public AstronomyHorizonSector? HorizonSector { get; }
    public bool Equals(AstronomyObservationConditionsPayload? other)
        => other is not null
            && TypeId == other.TypeId
            && ObservationContext == other.ObservationContext
            && Conditions == other.Conditions
            && Quantities.SequenceEqual(other.Quantities)
            && HorizontalCoordinate == other.HorizontalCoordinate
            && HorizonSector == other.HorizonSector;
    public override int GetHashCode() => Quantities.Aggregate(HashCode.Combine(TypeId, ObservationContext, Conditions, HorizontalCoordinate, HorizonSector), HashCode.Combine);
    private static IReadOnlyList<AstronomyObservationalQuantity> CopyQuantities(IEnumerable<AstronomyObservationalQuantity> quantities)
    {
        var ordered = quantities
            .Select(q => q ?? throw new ArgumentException("Observational quantities cannot contain null entries.", nameof(quantities)))
            .OrderBy(q => q.QuantityId.Value, StringComparer.Ordinal)
            .ThenBy(q => q.Qualifier)
            .ThenBy(q => q.Epoch?.Kind)
            .ThenBy(q => q.Epoch?.InstantUtc)
            .ToArray();
        if (ordered.GroupBy(q => new { q.QuantityId, q.Qualifier, q.Epoch }).Any(g => g.Count() > 1))
        {
            throw new ArgumentException("Observational quantities must be unique by quantity ID, qualifier and epoch.", nameof(quantities));
        }
        return Array.AsReadOnly(ordered);
    }
}
