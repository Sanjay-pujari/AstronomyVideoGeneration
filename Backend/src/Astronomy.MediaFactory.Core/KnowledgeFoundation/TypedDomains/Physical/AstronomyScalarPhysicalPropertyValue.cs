using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;

public sealed record AstronomyScalarPhysicalPropertyValue : AstronomyPhysicalPropertyValue
{
    public AstronomyScalarPhysicalPropertyValue(AstronomyMeasurement measurement)
    {
        Measurement = measurement ?? throw new ArgumentNullException(nameof(measurement));
    }

    public override AstronomyPhysicalPropertyValueKind Kind => AstronomyPhysicalPropertyValueKind.ScalarMeasurement;

    public AstronomyMeasurement Measurement { get; }
}
