using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

public sealed record AstronomyOrbitalParameter
{
    private const int MaxNoteLength = 512;

    public AstronomyOrbitalParameter(AstronomyOrbitalParameterId parameterId, AstronomyOrbitalParameterCategory category, AstronomyMeasurement measurement, AstronomyOrbitalParameterQualifier? qualifier = null, AstronomyEpochReference? epoch = null, string? note = null)
    {
        if (!parameterId.IsValid) throw new ArgumentException("Orbital parameter ID is required.", nameof(parameterId));
        ParameterId = parameterId;
        Category = EnumGuard.RequireDefined(category, nameof(category));
        Measurement = measurement ?? throw new ArgumentNullException(nameof(measurement));
        Qualifier = qualifier.HasValue ? EnumGuard.RequireDefined(qualifier.Value, nameof(qualifier)) : null;
        Epoch = epoch;
        Note = TypedKnowledgeTextGuards.NormalizeOptionalText(note, MaxNoteLength, nameof(note), "Orbital parameter note");
    }

    public AstronomyOrbitalParameterId ParameterId { get; }
    public AstronomyOrbitalParameterCategory Category { get; }
    public AstronomyMeasurement Measurement { get; }
    public AstronomyOrbitalParameterQualifier? Qualifier { get; }
    public AstronomyEpochReference? Epoch { get; }
    public string? Note { get; }
}
