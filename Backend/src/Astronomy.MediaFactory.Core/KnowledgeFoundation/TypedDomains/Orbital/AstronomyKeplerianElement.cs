using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

public sealed record AstronomyKeplerianElement
{
    private const int MaxNoteLength = 512;

    public AstronomyKeplerianElement(AstronomyKeplerianElementType elementType, AstronomyMeasurement measurement, AstronomyOrbitalParameterQualifier? qualifier = null, string? note = null)
    {
        ElementType = EnumGuard.RequireDefined(elementType, nameof(elementType));
        Measurement = measurement ?? throw new ArgumentNullException(nameof(measurement));
        Qualifier = qualifier.HasValue ? EnumGuard.RequireDefined(qualifier.Value, nameof(qualifier)) : null;
        Note = TypedKnowledgeTextGuards.NormalizeOptionalText(note, MaxNoteLength, nameof(note), "Keplerian element note");
    }

    public AstronomyKeplerianElementType ElementType { get; }
    public AstronomyMeasurement Measurement { get; }
    public AstronomyOrbitalParameterQualifier? Qualifier { get; }
    public string? Note { get; }
}
