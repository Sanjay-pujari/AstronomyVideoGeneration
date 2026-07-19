using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

public sealed record AstronomyMeasurementUncertainty
{
    public AstronomyMeasurementUncertainty(AstronomyUncertaintyKind kind, decimal lowerValue, decimal upperValue)
    {
        Kind = TypedKnowledgeEnumGuard.RequireDefined(kind, nameof(kind));
        if (lowerValue < 0) throw new ArgumentOutOfRangeException(nameof(lowerValue), lowerValue, "Uncertainty lower value cannot be negative.");
        if (upperValue < 0) throw new ArgumentOutOfRangeException(nameof(upperValue), upperValue, "Uncertainty upper value cannot be negative.");
        if (Kind == AstronomyUncertaintyKind.SymmetricAbsolute && lowerValue != upperValue)
            throw new ArgumentException("Symmetric absolute uncertainty requires equal lower and upper values.", nameof(upperValue));
        if (Kind == AstronomyUncertaintyKind.RelativePercentage && (lowerValue > 100m || upperValue > 100m))
            throw new ArgumentOutOfRangeException(nameof(upperValue), "Relative percentage uncertainty values must be between 0 and 100.");
        LowerValue = lowerValue;
        UpperValue = upperValue;
    }

    public AstronomyUncertaintyKind Kind { get; }
    public decimal LowerValue { get; }
    public decimal UpperValue { get; }
    public static AstronomyMeasurementUncertainty SymmetricAbsolute(decimal value) => new(AstronomyUncertaintyKind.SymmetricAbsolute, value, value);
}
