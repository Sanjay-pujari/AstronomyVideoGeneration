using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public sealed record AstronomyEventPayload : ITypedAstronomyKnowledgePayload
{
    public AstronomyEventPayload(AstronomyKnowledgeTypeId typeId, AstronomyEvent @event)
    {
        if (!typeId.IsValid) throw new ArgumentException("Astronomical event payload type ID is required.", nameof(typeId));
        TypeId = typeId; Event = @event ?? throw new ArgumentNullException(nameof(@event));
    }
    public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Event;
    public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.AstronomicalEvent;
    public AstronomyKnowledgeTypeId TypeId { get; }
    public AstronomyEvent Event { get; }
}
