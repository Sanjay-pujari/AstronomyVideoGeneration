using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Positional;

public sealed class AstronomyAngularPositionValidationRule : AstronomyKnowledgeValidationRule<AstronomySpatialPositionPayload>
{
    public const string Id = "positional.angular.integrity";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Positional; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.SpatialPosition; public override int Order => 300;
    public override bool Supports(AstronomySpatialPositionPayload payload, AstronomyKnowledgeValidationContext context) => payload.Position.Value is AstronomyAngularPositionValue;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomySpatialPositionPayload payload, AstronomyKnowledgeValidationContext context)
    {
        var a = (AstronomyAngularPositionValue)payload.Position.Value;
        foreach (var issue in AstronomyAngularComponentValidator.ValidatePair(a.FirstAngle, a.SecondAngle, payload.Position.ReferenceContext.CoordinateSystem, "$.position.value.firstAngle", "$.position.value.secondAngle", RuleId, Domain, Family)) yield return issue;
    }
}

internal static class AstronomyAngularComponentValidator
{
    public static IEnumerable<AstronomyKnowledgeValidationIssue> ValidatePair(AstronomyAngularCoordinateValue first, AstronomyAngularCoordinateValue second, AstronomyCoordinateSystem system, string firstPath, string secondPath, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family)
    {
        if (!Enum.IsDefined(first.Component)) yield return Issue(AstronomyPositionalValidationCodes.AngularComponentInvalid, AstronomyKnowledgeValidationSeverity.Error, "First angular component is not defined.", firstPath + ".component", ruleId, domain, family);
        if (!Enum.IsDefined(second.Component)) yield return Issue(AstronomyPositionalValidationCodes.AngularComponentInvalid, AstronomyKnowledgeValidationSeverity.Error, "Second angular component is not defined.", secondPath + ".component", ruleId, domain, family);
        if (first.Component == second.Component) yield return Issue(AstronomyPositionalValidationCodes.AngularComponentDuplicate, AstronomyKnowledgeValidationSeverity.Error, "Angular components must be distinct.", secondPath, ruleId, domain, family);
        if (first.Angle.Unit.Dimension != AstronomyMeasurementDimension.Angle) yield return Issue(AstronomyPositionalValidationCodes.AngularDimensionMismatch, AstronomyKnowledgeValidationSeverity.Error, "First angular component must use Angle dimension.", firstPath + ".angle.unit.dimension", ruleId, domain, family);
        if (second.Angle.Unit.Dimension != AstronomyMeasurementDimension.Angle) yield return Issue(AstronomyPositionalValidationCodes.AngularDimensionMismatch, AstronomyKnowledgeValidationSeverity.Error, "Second angular component must use Angle dimension.", secondPath + ".angle.unit.dimension", ruleId, domain, family);
        if (!IsValidPair(system, first.Component, second.Component)) yield return Issue(AstronomyPositionalValidationCodes.CoordinateSystemMismatch, AstronomyKnowledgeValidationSeverity.Error, "Angular components do not match the declared coordinate system.", "$.position.referenceContext.coordinateSystem", ruleId, domain, family);
    }
    public static bool IsValidPair(AstronomyCoordinateSystem system, AstronomyAngularCoordinateComponent first, AstronomyAngularCoordinateComponent second)
    {
        static bool Pair(AstronomyAngularCoordinateComponent a, AstronomyAngularCoordinateComponent b, AstronomyAngularCoordinateComponent x, AstronomyAngularCoordinateComponent y) => a == x && b == y;
        return system switch
        {
            AstronomyCoordinateSystem.Equatorial => Pair(first, second, AstronomyAngularCoordinateComponent.RightAscension, AstronomyAngularCoordinateComponent.Declination),
            AstronomyCoordinateSystem.Ecliptic or AstronomyCoordinateSystem.Galactic or AstronomyCoordinateSystem.Supergalactic or AstronomyCoordinateSystem.Spherical => Pair(first, second, AstronomyAngularCoordinateComponent.Longitude, AstronomyAngularCoordinateComponent.Latitude),
            AstronomyCoordinateSystem.Horizontal => Pair(first, second, AstronomyAngularCoordinateComponent.Azimuth, AstronomyAngularCoordinateComponent.Altitude),
            _ => false
        };
    }
    private static AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family) => new(code, severity, message, path, ruleId, domain, family);
}
