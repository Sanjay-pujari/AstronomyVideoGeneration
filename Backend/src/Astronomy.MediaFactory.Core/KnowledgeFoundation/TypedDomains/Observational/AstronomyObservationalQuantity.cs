using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

public sealed record AstronomyObservationalQuantity
{
    private const int MaxNoteLength = 512;
    public AstronomyObservationalQuantity(AstronomyObservationalQuantityId quantityId, AstronomyObservationalQuantityCategory category, AstronomyMeasurement measurement, AstronomyObservationalQuantityQualifier? qualifier = null, AstronomyEpochReference? epoch = null, string? note = null)
    {
        if (!quantityId.IsValid) throw new ArgumentException("Observational quantity ID is required.", nameof(quantityId));
        QuantityId = quantityId;
        Category = EnumGuard.RequireDefined(category, nameof(category));
        Measurement = measurement ?? throw new ArgumentNullException(nameof(measurement));
        Qualifier = qualifier.HasValue ? EnumGuard.RequireDefined(qualifier.Value, nameof(qualifier)) : null;
        Epoch = epoch;
        if (note is not null && string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("Observational quantity note must not be blank when supplied.", nameof(note));
        }
        Note = TypedKnowledgeTextGuards.NormalizeOptionalText(note, MaxNoteLength, nameof(note), "Observational quantity note");
    }
    public AstronomyObservationalQuantityId QuantityId { get; }
    public AstronomyObservationalQuantityCategory Category { get; }
    public AstronomyMeasurement Measurement { get; }
    public AstronomyObservationalQuantityQualifier? Qualifier { get; }
    public AstronomyEpochReference? Epoch { get; }
    public string? Note { get; }
}
