using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public sealed record AstronomyEventGeometryQuantity
{
    public AstronomyEventGeometryQuantity(AstronomyEventGeometryQuantityId quantityId, AstronomyEventGeometryCategory category, AstronomyMeasurement measurement, AstronomyEpochReference? epoch = null, string? note = null)
    {
        if (!quantityId.IsValid) throw new ArgumentException("Event geometry quantity ID is required.", nameof(quantityId));
        QuantityId = quantityId;
        Category = EnumGuard.RequireDefined(category, nameof(category));
        Measurement = measurement ?? throw new ArgumentNullException(nameof(measurement));
        Epoch = epoch;
        Note = EventText.Optional(note, EventText.MaxNoteLength, nameof(note), "Event geometry note");
    }
    public AstronomyEventGeometryQuantityId QuantityId { get; }
    public AstronomyEventGeometryCategory Category { get; }
    public AstronomyMeasurement Measurement { get; }
    public AstronomyEpochReference? Epoch { get; }
    public string? Note { get; }
}
