using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

public static class AstronomyBuiltInTypedPayloadDescriptors
{
    public static IReadOnlyList<AstronomyTypedPayloadDescriptor> BuiltIn { get; } = Array.AsReadOnly([
        new AstronomyTypedPayloadDescriptor("typed.classification.entity.v1", typeof(AstronomyEntityClassificationPayload), AstronomyKnowledgeDomain.Classification, AstronomyKnowledgePayloadFamily.EntityClassification),
        new AstronomyTypedPayloadDescriptor("typed.event.astronomical.v1", typeof(AstronomyEventPayload), AstronomyKnowledgeDomain.Event, AstronomyKnowledgePayloadFamily.AstronomicalEvent),
        new AstronomyTypedPayloadDescriptor("typed.observational.conditions.v1", typeof(AstronomyObservationConditionsPayload), AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.ObservationCondition),
        new AstronomyTypedPayloadDescriptor("typed.observational.visibility-windows.v1", typeof(AstronomyVisibilityWindowsPayload), AstronomyKnowledgeDomain.Observational, AstronomyKnowledgePayloadFamily.VisibilityWindow),
        new AstronomyTypedPayloadDescriptor("typed.orbital.keplerian-elements.v1", typeof(AstronomyKeplerianElementsPayload), AstronomyKnowledgeDomain.Orbital, AstronomyKnowledgePayloadFamily.OrbitalParameter),
        new AstronomyTypedPayloadDescriptor("typed.orbital.parameters.v1", typeof(AstronomyOrbitalParametersPayload), AstronomyKnowledgeDomain.Orbital, AstronomyKnowledgePayloadFamily.OrbitalParameter),
        new AstronomyTypedPayloadDescriptor("typed.physical.properties.v1", typeof(AstronomyPhysicalPropertiesPayload), AstronomyKnowledgeDomain.Physical, AstronomyKnowledgePayloadFamily.PhysicalProperty),
        new AstronomyTypedPayloadDescriptor("typed.positional.spatial-position.v1", typeof(AstronomySpatialPositionPayload), AstronomyKnowledgeDomain.Positional, AstronomyKnowledgePayloadFamily.SpatialPosition),
        new AstronomyTypedPayloadDescriptor("typed.temporal.pattern.v1", typeof(AstronomyTemporalPatternPayload), AstronomyKnowledgeDomain.Temporal, AstronomyKnowledgePayloadFamily.TemporalCycle)
    ]);
}
