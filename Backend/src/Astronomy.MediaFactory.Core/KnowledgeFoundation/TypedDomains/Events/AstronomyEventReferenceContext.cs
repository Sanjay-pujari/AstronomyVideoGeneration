using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public sealed record AstronomyEventReferenceContext
{
    public AstronomyEventReferenceContext(AstronomyEventScope scope, AstronomyReferenceFrame? referenceFrame = null, AstronomyReferenceOrigin? referenceOrigin = null, AstronomyCoordinateSystem? coordinateSystem = null, AstronomyObservationContext? observationContext = null)
    {
        Scope = EnumGuard.RequireDefined(scope, nameof(scope));
        ReferenceFrame = referenceFrame.HasValue ? Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.TypedKnowledgeEnumGuard.RequireDefined(referenceFrame.Value, nameof(referenceFrame)) : null;
        ReferenceOrigin = referenceOrigin.HasValue ? Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.TypedKnowledgeEnumGuard.RequireDefined(referenceOrigin.Value, nameof(referenceOrigin)) : null;
        CoordinateSystem = coordinateSystem.HasValue ? Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.TypedKnowledgeEnumGuard.RequireDefined(coordinateSystem.Value, nameof(coordinateSystem)) : null;
        ObservationContext = observationContext;
        if (Scope == AstronomyEventScope.ObserverSpecific && ObservationContext is null) throw new ArgumentException("Observer-specific events require an observation context.", nameof(observationContext));
    }
    public AstronomyEventScope Scope { get; }
    public AstronomyReferenceFrame? ReferenceFrame { get; }
    public AstronomyReferenceOrigin? ReferenceOrigin { get; }
    public AstronomyCoordinateSystem? CoordinateSystem { get; }
    public AstronomyObservationContext? ObservationContext { get; }
}
