using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;

internal static class AstronomyPhysicalMeasurementValidator
{
    public static IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyMeasurement measurement, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family)
    {
        if (string.IsNullOrWhiteSpace(measurement.Unit.Code)) yield return Issue(AstronomyPhysicalValidationCodes.MeasurementUnitInvalid, AstronomyKnowledgeValidationSeverity.Error, "Measurement unit code is required.", path + ".unit.code", ruleId, domain, family);
        if (string.IsNullOrWhiteSpace(measurement.Unit.Symbol)) yield return Issue(AstronomyPhysicalValidationCodes.MeasurementUnitInvalid, AstronomyKnowledgeValidationSeverity.Error, "Measurement unit symbol is required.", path + ".unit.symbol", ruleId, domain, family);
        if (!Enum.IsDefined(measurement.Unit.Dimension)) yield return Issue(AstronomyPhysicalValidationCodes.MeasurementUnitInvalid, AstronomyKnowledgeValidationSeverity.Error, "Measurement unit dimension is not defined.", path + ".unit.dimension", ruleId, domain, family);
        if (measurement.Precision is { } p)
        {
            if (!Enum.IsDefined(p.Kind) || p.Digits < 0 || p.Digits > AstronomyMeasurementPrecision.MaxDigits || (p.Kind == AstronomyPrecisionKind.SignificantFigures && p.Digits == 0)) yield return Issue(AstronomyPhysicalValidationCodes.MeasurementPrecisionInvalid, AstronomyKnowledgeValidationSeverity.Error, "Measurement precision is inconsistent with its kind.", path + ".precision", ruleId, domain, family);
        }
        if (measurement.Uncertainty is { } u)
        {
            if (!Enum.IsDefined(u.Kind) || u.LowerValue < 0 || u.UpperValue < 0 || ((u.Kind == AstronomyUncertaintyKind.SymmetricAbsolute || u.Kind == AstronomyUncertaintyKind.StandardDeviation) && u.LowerValue != u.UpperValue) || (u.Kind == AstronomyUncertaintyKind.RelativePercentage && (u.LowerValue > 100m || u.UpperValue > 100m)))
                yield return Issue(AstronomyPhysicalValidationCodes.MeasurementUncertaintyInvalid, AstronomyKnowledgeValidationSeverity.Error, "Measurement uncertainty is inconsistent with its kind.", path + ".uncertainty", ruleId, domain, family);
        }
    }

    private static AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family) => new(code, severity, message, path, ruleId, domain, family);
}
