using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyTemporalPatternReferenceContext
{
    public AstronomyTemporalPatternReferenceContext(AstronomyTemporalReferenceBasis basis, AstronomyEntityReference? entity = null, AstronomyEpochReference? epoch = null, AstronomyObservationContext? observationContext = null)
    { Basis = TemporalGuards.Defined(basis, nameof(basis)); if (Basis == AstronomyTemporalReferenceBasis.EntityRelative && entity is null) throw new ArgumentException("Entity-relative temporal context requires an entity.", nameof(entity)); if (Basis == AstronomyTemporalReferenceBasis.EpochRelative && epoch is null) throw new ArgumentException("Epoch-relative temporal context requires an epoch.", nameof(epoch)); if (Basis == AstronomyTemporalReferenceBasis.ObserverRelative && observationContext is null) throw new ArgumentException("Observer-relative temporal context requires an observation context.", nameof(observationContext)); Entity = entity; Epoch = epoch; ObservationContext = observationContext; }
    public AstronomyTemporalReferenceBasis Basis { get; }
    public AstronomyEntityReference? Entity { get; }
    public AstronomyEpochReference? Epoch { get; }
    public AstronomyObservationContext? ObservationContext { get; }
}
