using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Positional;

public sealed class AstronomyCartesianPositionValidationRule : AstronomyKnowledgeValidationRule<AstronomySpatialPositionPayload>
{
    public const string Id = "positional.cartesian.integrity";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Positional; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.SpatialPosition; public override int Order => 500;
    public override bool Supports(AstronomySpatialPositionPayload payload, AstronomyKnowledgeValidationContext context) => payload.Position.Value is AstronomyCartesianPositionValue;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomySpatialPositionPayload payload, AstronomyKnowledgeValidationContext context)
    {
        var c = ((AstronomyCartesianPositionValue)payload.Position.Value).Coordinate;
        var components = new[] { ("x", c.X), ("y", c.Y), ("z", c.Z) };
        foreach (var (name, m) in components)
            if (m.Unit.Dimension != AstronomyMeasurementDimension.Distance) yield return Issue(AstronomyPositionalValidationCodes.CartesianDimensionMismatch, AstronomyKnowledgeValidationSeverity.Error, "Cartesian components must use Distance dimension.", $"$.position.value.coordinate.{name}.unit.dimension");
        if (components.Select(x => (x.Item2.Unit.Code, x.Item2.Unit.Dimension)).Distinct().Count() > 1) yield return Issue(AstronomyPositionalValidationCodes.CartesianUnitMismatch, AstronomyKnowledgeValidationSeverity.Error, "Cartesian component units must match exactly by code and dimension.", "$.position.value.coordinate");
        if (payload.Position.ReferenceContext.CoordinateSystem != AstronomyCoordinateSystem.Cartesian) yield return Issue(AstronomyPositionalValidationCodes.PositionReferenceMismatch, AstronomyKnowledgeValidationSeverity.Error, "Cartesian position values require a Cartesian coordinate system.", "$.position.referenceContext.coordinateSystem");
    }
    private AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path) => new(code, severity, message, path, RuleId, Domain, Family);
}
