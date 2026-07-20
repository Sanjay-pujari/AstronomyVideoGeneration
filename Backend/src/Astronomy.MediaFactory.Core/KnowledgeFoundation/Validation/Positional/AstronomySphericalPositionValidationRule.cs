using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Positional;

public sealed class AstronomySphericalPositionValidationRule : AstronomyKnowledgeValidationRule<AstronomySpatialPositionPayload>
{
    public const string Id = "positional.spherical.integrity";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Positional; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.SpatialPosition; public override int Order => 400;
    public override bool Supports(AstronomySpatialPositionPayload payload, AstronomyKnowledgeValidationContext context) => payload.Position.Value is AstronomySphericalPositionValue;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomySpatialPositionPayload payload, AstronomyKnowledgeValidationContext context)
    {
        var s = (AstronomySphericalPositionValue)payload.Position.Value;
        foreach (var issue in AstronomyAngularComponentValidator.ValidatePair(s.Coordinate.LongitudeLike, s.Coordinate.LatitudeLike, payload.Position.ReferenceContext.CoordinateSystem, "$.position.value.coordinate.longitudeLike", "$.position.value.coordinate.latitudeLike", RuleId, Domain, Family)) yield return issue;
        if (s.Coordinate.Distance is { } d && d.Unit.Dimension != AstronomyMeasurementDimension.Distance) yield return new(AstronomyPositionalValidationCodes.SphericalDistanceDimensionMismatch, AstronomyKnowledgeValidationSeverity.Error, "Spherical distance must use Distance dimension.", "$.position.value.coordinate.distance.unit.dimension", RuleId, Domain, Family);
    }
}
