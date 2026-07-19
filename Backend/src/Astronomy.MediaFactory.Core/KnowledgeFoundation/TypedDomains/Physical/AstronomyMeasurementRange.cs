using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
public sealed record AstronomyMeasurementRange
{
    public AstronomyMeasurementRange(AstronomyMeasurement minimum, AstronomyMeasurement maximum)
    {
        Minimum = minimum ?? throw new ArgumentNullException(nameof(minimum));
        Maximum = maximum ?? throw new ArgumentNullException(nameof(maximum));
        if (Minimum.Unit != Maximum.Unit) throw new ArgumentException("Measurement range units must match exactly.", nameof(maximum));
        if (Minimum.Unit.Dimension != Maximum.Unit.Dimension) throw new ArgumentException("Measurement range dimensions must match.", nameof(maximum));
        if (Minimum.Value > Maximum.Value) throw new ArgumentException("Measurement range minimum cannot be greater than maximum.", nameof(minimum));
    }
    public AstronomyMeasurement Minimum { get; }
    public AstronomyMeasurement Maximum { get; }
}
