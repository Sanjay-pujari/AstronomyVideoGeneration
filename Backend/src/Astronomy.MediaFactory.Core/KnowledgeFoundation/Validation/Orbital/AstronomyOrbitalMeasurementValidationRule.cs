using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Orbital;

internal static class AstronomyOrbitalMeasurementValidator
{
    public static bool HasDimension(AstronomyMeasurement measurement, AstronomyMeasurementDimension expected)
        => measurement.Unit is not null && Enum.IsDefined(measurement.Unit.Dimension) && measurement.Unit.Dimension == expected;

    public static IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyMeasurement measurement, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family)
    {
        if (measurement.Unit is null || string.IsNullOrWhiteSpace(measurement.Unit.Code) || !Enum.IsDefined(measurement.Unit.Dimension))
            yield return new(AstronomyOrbitalValidationCodes.ReferenceContextInvalid, AstronomyKnowledgeValidationSeverity.Error, "Measurement unit is structurally invalid.", path + ".unit", ruleId, domain, family);
    }
}
