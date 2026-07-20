using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public sealed record AstronomyEventParticipant
{
    public AstronomyEventParticipant(AstronomyEntityReference entity, AstronomyEventParticipantRole role, string? label = null)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        Role = EnumGuard.RequireDefined(role, nameof(role));
        Label = EventText.Optional(label, EventText.MaxLabelLength, nameof(label), "Event participant label");
    }
    public AstronomyEntityReference Entity { get; }
    public AstronomyEventParticipantRole Role { get; }
    public string? Label { get; }
}
