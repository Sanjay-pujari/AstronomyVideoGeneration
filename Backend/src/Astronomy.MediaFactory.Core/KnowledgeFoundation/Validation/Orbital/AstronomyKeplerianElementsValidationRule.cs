using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Orbital;

public sealed class AstronomyKeplerianElementsValidationRule : AstronomyKnowledgeValidationRule<AstronomyKeplerianElementsPayload>
{
    public const string Id = "orbital.keplerian.elements";
    private static readonly AstronomyKeplerianElementType[] Standard = [AstronomyKeplerianElementType.SemiMajorAxis, AstronomyKeplerianElementType.Eccentricity];
    private static readonly AstronomyKeplerianElementType[] Classical = [AstronomyKeplerianElementType.SemiMajorAxis, AstronomyKeplerianElementType.Eccentricity, AstronomyKeplerianElementType.Inclination, AstronomyKeplerianElementType.LongitudeOfAscendingNode, AstronomyKeplerianElementType.ArgumentOfPeriapsis, AstronomyKeplerianElementType.MeanAnomaly];
    public override string RuleId => Id;
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Orbital;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.OrbitalParameter;
    public override int Order => 200;

    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyKeplerianElementsPayload payload, AstronomyKnowledgeValidationContext context)
    {
        var seen = new HashSet<AstronomyKeplerianElementType>();
        for (var i = 0; i < payload.Elements.Count; i++)
        {
            var e = payload.Elements[i];
            if (!Enum.IsDefined(e.ElementType)) { yield return Issue(AstronomyOrbitalValidationCodes.ElementMissing, AstronomyKnowledgeValidationSeverity.Error, "Keplerian element type is not defined.", $"$.elements[{i}].elementType"); continue; }
            if (!seen.Add(e.ElementType)) yield return Issue(AstronomyOrbitalValidationCodes.ElementDuplicate, AstronomyKnowledgeValidationSeverity.Error, "Keplerian elements must be unique by element type.", $"$.elements[{i}]");
            foreach (var issue in AstronomyOrbitalMeasurementValidator.Validate(e.Measurement, $"$.elements[{i}].measurement", RuleId, Domain, Family)) yield return issue;
            if (AstronomyKeplerianElementDimensionCatalog.TryGetExpectedDimension(e.ElementType, out var expected) && !AstronomyOrbitalMeasurementValidator.HasDimension(e.Measurement, expected))
                yield return Issue(AstronomyOrbitalValidationCodes.ElementDimensionMismatch, AstronomyKnowledgeValidationSeverity.Error, "Keplerian element measurement dimension does not match the element type.", $"$.elements[{i}].measurement.unit.dimension");
            if (e.ElementType == AstronomyKeplerianElementType.Eccentricity && e.Measurement.Value < 0m) yield return Issue(AstronomyOrbitalValidationCodes.ElementValueOutOfRange, AstronomyKnowledgeValidationSeverity.Error, "Eccentricity cannot be negative.", $"$.elements[{i}].measurement.value");
        }
        var required = context.Mode == AstronomyKnowledgeValidationMode.Standard ? Standard : Classical;
        var severity = context.Mode == AstronomyKnowledgeValidationMode.Standard ? AstronomyKnowledgeValidationSeverity.Warning : AstronomyKnowledgeValidationSeverity.Error;
        foreach (var missing in required.Where(r => !seen.Contains(r)).OrderBy(r => (int)r)) yield return Issue(AstronomyOrbitalValidationCodes.ElementMissing, severity, "Required Keplerian element is missing for the validation mode.", "$.elements");
    }
    private AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path) => new(code, severity, message, path, RuleId, Domain, Family);
}

public static class AstronomyKeplerianElementDimensionCatalog
{
    public static bool TryGetExpectedDimension(AstronomyKeplerianElementType type, out AstronomyMeasurementDimension dimension)
    {
        switch (type)
        {
            case AstronomyKeplerianElementType.SemiMajorAxis:
            case AstronomyKeplerianElementType.PeriapsisDistance:
            case AstronomyKeplerianElementType.ApoapsisDistance:
                dimension = AstronomyMeasurementDimension.Distance;
                return true;
            case AstronomyKeplerianElementType.Eccentricity:
                dimension = AstronomyMeasurementDimension.Dimensionless;
                return true;
            case AstronomyKeplerianElementType.Inclination:
            case AstronomyKeplerianElementType.LongitudeOfAscendingNode:
            case AstronomyKeplerianElementType.ArgumentOfPeriapsis:
            case AstronomyKeplerianElementType.MeanAnomaly:
            case AstronomyKeplerianElementType.TrueAnomaly:
            case AstronomyKeplerianElementType.EccentricAnomaly:
            case AstronomyKeplerianElementType.MeanLongitude:
            case AstronomyKeplerianElementType.LongitudeOfPeriapsis:
                dimension = AstronomyMeasurementDimension.Angle;
                return true;
            case AstronomyKeplerianElementType.OrbitalPeriod:
                dimension = AstronomyMeasurementDimension.Time;
                return true;
            default:
                dimension = default;
                return false;
        }
    }
}
