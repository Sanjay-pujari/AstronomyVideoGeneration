using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Positional;

public sealed class AstronomyPositionValueValidationRule : AstronomyKnowledgeValidationRule<AstronomySpatialPositionPayload>
{
    public const string Id = "positional.position.integrity";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Positional; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.SpatialPosition; public override int Order => 200;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomySpatialPositionPayload payload, AstronomyKnowledgeValidationContext context)
    {
        var system = payload.Position.ReferenceContext.CoordinateSystem;
        switch (payload.Position.Value)
        {
            case AstronomyAngularPositionValue angular when angular.Kind != AstronomyPositionRepresentationKind.Angular: yield return KindIssue(); break;
            case AstronomySphericalPositionValue spherical when spherical.Kind != AstronomyPositionRepresentationKind.Spherical: yield return KindIssue(); break;
            case AstronomyCartesianPositionValue cartesian when cartesian.Kind != AstronomyPositionRepresentationKind.Cartesian: yield return KindIssue(); break;
            case AstronomyCartesianPositionValue when system != AstronomyCoordinateSystem.Cartesian: yield return Issue(AstronomyPositionalValidationCodes.PositionReferenceMismatch, AstronomyKnowledgeValidationSeverity.Error, "Cartesian position values require a Cartesian coordinate system.", "$.position.value"); break;
            case AstronomyAngularPositionValue when system == AstronomyCoordinateSystem.Cartesian: yield return Issue(AstronomyPositionalValidationCodes.PositionReferenceMismatch, AstronomyKnowledgeValidationSeverity.Error, "Angular position values are not Cartesian coordinates.", "$.position.value"); break;
            case AstronomyPositionValue: break;
            default: yield return KindIssue(); break;
        }
    }
    private AstronomyKnowledgeValidationIssue KindIssue() => Issue(AstronomyPositionalValidationCodes.PositionKindMismatch, AstronomyKnowledgeValidationSeverity.Critical, "Position value runtime representation does not match its fixed kind.", "$.position.value");
    private AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path) => new(code, severity, message, path, RuleId, Domain, Family);
}
