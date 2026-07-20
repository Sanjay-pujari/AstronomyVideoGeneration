using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Positional;

public sealed class AstronomySpatialReferenceContextValidationRule : AstronomyKnowledgeValidationRule<AstronomySpatialPositionPayload>
{
    public const string Id = "positional.reference-context";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Positional; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.SpatialPosition; public override int Order => 100;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomySpatialPositionPayload payload, AstronomyKnowledgeValidationContext context)
    {
        var c = payload.Position.ReferenceContext;
        if (!Enum.IsDefined(c.ReferenceFrame) || c.ReferenceFrame == AstronomyReferenceFrame.Unspecified) yield return Issue(AstronomyPositionalValidationCodes.ReferenceContextInvalid, AstronomyKnowledgeValidationSeverity.Error, "Reference frame must be defined.", "$.position.referenceContext.referenceFrame");
        if (!Enum.IsDefined(c.ReferenceOrigin) || c.ReferenceOrigin == AstronomyReferenceOrigin.Unspecified) yield return Issue(AstronomyPositionalValidationCodes.ReferenceContextInvalid, AstronomyKnowledgeValidationSeverity.Error, "Reference origin must be defined.", "$.position.referenceContext.referenceOrigin");
        if (!Enum.IsDefined(c.CoordinateSystem)) yield return Issue(AstronomyPositionalValidationCodes.ReferenceContextInvalid, AstronomyKnowledgeValidationSeverity.Error, "Coordinate system must be defined.", "$.position.referenceContext.coordinateSystem");
        if (c.Epoch is null || !Enum.IsDefined(c.Epoch.Kind) || (context.Mode == AstronomyKnowledgeValidationMode.Certification && c.Epoch.Kind == AstronomyEpochKind.Unspecified)) yield return Issue(AstronomyPositionalValidationCodes.EpochInvalid, AstronomyKnowledgeValidationSeverity.Error, "Epoch must be structurally valid and specified for certification.", "$.position.referenceContext.epoch");
        if (!AstronomySpatialReferenceCompatibility.IsCompatible(c.CoordinateSystem, c.ReferenceOrigin)) yield return Issue(AstronomyPositionalValidationCodes.CoordinateSystemMismatch, AstronomyKnowledgeValidationSeverity.Critical, "Coordinate system and reference origin are contradictory.", "$.position.referenceContext");
        if (c.ReferenceFrame == AstronomyReferenceFrame.BodyFixed && c.ReferenceOrigin != AstronomyReferenceOrigin.BodyCentric) yield return Issue(AstronomyPositionalValidationCodes.FrameOriginMismatch, AstronomyKnowledgeValidationSeverity.Critical, "Body-fixed reference frames require a body-centric origin.", "$.position.referenceContext");
    }
    private AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path) => new(code, severity, message, path, RuleId, Domain, Family);
}

internal static class AstronomySpatialReferenceCompatibility
{
    public static bool IsCompatible(AstronomyCoordinateSystem system, AstronomyReferenceOrigin origin) => system switch
    {
        AstronomyCoordinateSystem.Horizontal => origin == AstronomyReferenceOrigin.Topocentric,
        AstronomyCoordinateSystem.Ecliptic => origin is AstronomyReferenceOrigin.Geocentric or AstronomyReferenceOrigin.Barycentric or AstronomyReferenceOrigin.Heliocentric,
        _ => true
    };
}
