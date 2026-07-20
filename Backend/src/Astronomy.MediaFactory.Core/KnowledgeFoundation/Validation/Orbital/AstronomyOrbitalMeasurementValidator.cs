using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Orbital;

internal static class AstronomyOrbitalMeasurementValidator
{
    public static bool HasDimension(AstronomyMeasurement measurement, AstronomyMeasurementDimension expected)
        => measurement.Unit is not null && Enum.IsDefined(measurement.Unit.Dimension) && measurement.Unit.Dimension == expected;

    public static IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyMeasurement measurement, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family)
    {
        if (measurement.Unit is null || string.IsNullOrWhiteSpace(measurement.Unit.Code) || string.IsNullOrWhiteSpace(measurement.Unit.Symbol) || !Enum.IsDefined(measurement.Unit.Dimension))
            yield return new(AstronomyOrbitalValidationCodes.MeasurementUnitInvalid, AstronomyKnowledgeValidationSeverity.Error, "Measurement unit is structurally invalid.", path + ".unit", ruleId, domain, family);
        if (measurement.Precision is { } p && (!Enum.IsDefined(p.Kind) || p.Digits < 0 || p.Digits > AstronomyMeasurementPrecision.MaxDigits || (p.Kind == AstronomyPrecisionKind.SignificantFigures && p.Digits == 0)))
            yield return new(AstronomyOrbitalValidationCodes.MeasurementPrecisionInvalid, AstronomyKnowledgeValidationSeverity.Error, "Measurement precision is structurally invalid.", path + ".precision", ruleId, domain, family);
        if (measurement.Uncertainty is { } u && (!Enum.IsDefined(u.Kind) || u.LowerValue < 0 || u.UpperValue < 0 || ((u.Kind == AstronomyUncertaintyKind.SymmetricAbsolute || u.Kind == AstronomyUncertaintyKind.StandardDeviation) && u.LowerValue != u.UpperValue) || (u.Kind == AstronomyUncertaintyKind.RelativePercentage && (u.LowerValue > 100m || u.UpperValue > 100m))))
            yield return new(AstronomyOrbitalValidationCodes.MeasurementUncertaintyInvalid, AstronomyKnowledgeValidationSeverity.Error, "Measurement uncertainty is structurally invalid.", path + ".uncertainty", ruleId, domain, family);
    }
}
