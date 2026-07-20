using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;

internal static class AstronomyObservationalMeasurementValidator
{
    public static IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyMeasurement measurement, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family)
    {
        if (string.IsNullOrWhiteSpace(measurement.Unit.Code)) yield return Issue(AstronomyObservationalValidationCodes.MeasurementUnitInvalid, "Measurement unit code is required.", path + ".unit.code", ruleId, domain, family);
        if (!Enum.IsDefined(measurement.Unit.Dimension)) yield return Issue(AstronomyObservationalValidationCodes.MeasurementUnitInvalid, "Measurement unit dimension must be defined.", path + ".unit.dimension", ruleId, domain, family);
        if (measurement.Precision is not null)
        {
            if (IsPrecisionInvalid(measurement.Precision))
                yield return Issue(AstronomyObservationalValidationCodes.MeasurementPrecisionInvalid, "Measurement precision must use a defined kind and supported digit count.", path + ".precision", ruleId, domain, family);
        }
        if (measurement.Uncertainty is not null)
        {
            var u = measurement.Uncertainty;
            if (!Enum.IsDefined(u.Kind) || u.LowerValue < 0 || u.UpperValue < 0 || ((u.Kind == AstronomyUncertaintyKind.SymmetricAbsolute || u.Kind == AstronomyUncertaintyKind.StandardDeviation) && u.LowerValue != u.UpperValue) || (u.Kind == AstronomyUncertaintyKind.RelativePercentage && (u.LowerValue > 100m || u.UpperValue > 100m)))
                yield return Issue(AstronomyObservationalValidationCodes.MeasurementUncertaintyInvalid, "Measurement uncertainty must be structurally consistent.", path + ".uncertainty", ruleId, domain, family);
        }
    }

    private static bool IsPrecisionInvalid(AstronomyMeasurementPrecision precision)
    {
        if (!Enum.IsDefined(precision.Kind))
            return true;

        if (precision.Digits > AstronomyMeasurementPrecision.MaxDigits)
            return true;

        return precision.Kind switch
        {
            AstronomyPrecisionKind.DecimalPlaces => precision.Digits < 0,
            AstronomyPrecisionKind.SignificantFigures => precision.Digits <= 0,
            _ => false
        };
    }

    private static AstronomyKnowledgeValidationIssue Issue(string code, string message, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family) => new(code, AstronomyKnowledgeValidationSeverity.Error, message, path, ruleId, domain, family);
}
