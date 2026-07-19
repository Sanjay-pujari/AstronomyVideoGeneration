namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

public sealed record AstronomyMeasurement
{
    public AstronomyMeasurement(decimal value, AstronomyMeasurementUnit unit, AstronomyMeasurementPrecision? precision = null, AstronomyMeasurementUncertainty? uncertainty = null)
    {
        Value = value;
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
        Precision = precision;
        Uncertainty = uncertainty;
    }

    public decimal Value { get; }
    public AstronomyMeasurementUnit Unit { get; }
    public AstronomyMeasurementPrecision? Precision { get; }
    public AstronomyMeasurementUncertainty? Uncertainty { get; }
}
