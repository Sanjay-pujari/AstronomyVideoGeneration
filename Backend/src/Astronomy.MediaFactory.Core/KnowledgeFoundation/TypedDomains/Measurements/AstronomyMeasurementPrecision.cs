using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

public sealed record AstronomyMeasurementPrecision
{
    public const int MaxDigits = 30;

    public AstronomyMeasurementPrecision(AstronomyPrecisionKind kind, int digits)
    {
        Kind = TypedKnowledgeEnumGuard.RequireDefined(kind, nameof(kind));
        if (digits < 0 || digits > MaxDigits)
            throw new ArgumentOutOfRangeException(nameof(digits), digits, $"Precision digits must be between 0 and {MaxDigits}.");
        Digits = digits;
    }

    public AstronomyPrecisionKind Kind { get; }
    public int Digits { get; }
}
