using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Orbital;

public sealed class AstronomyOrbitalReferenceContextValidationRule : AstronomyKnowledgeValidationRule<AstronomyKeplerianElementsPayload>
{
    public const string Id = "orbital.keplerian.reference-context";
    public override string RuleId => Id;
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Orbital;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.OrbitalParameter;
    public override int Order => 100;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyKeplerianElementsPayload payload, AstronomyKnowledgeValidationContext context) => AstronomyOrbitalReferenceContextValidator.Validate(payload.ReferenceContext, RuleId, Domain, Family, context.Mode);
}

public sealed class AstronomyOrbitalParametersReferenceContextValidationRule : AstronomyKnowledgeValidationRule<AstronomyOrbitalParametersPayload>
{
    public const string Id = "orbital.parameters.reference-context";
    public override string RuleId => Id;
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Orbital;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.OrbitalParameter;
    public override int Order => 100;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyOrbitalParametersPayload payload, AstronomyKnowledgeValidationContext context) => AstronomyOrbitalReferenceContextValidator.Validate(payload.ReferenceContext, RuleId, Domain, Family, context.Mode);
}

internal static class AstronomyOrbitalReferenceContextValidator
{
    public static IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyOrbitalReferenceContext c, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family, AstronomyKnowledgeValidationMode mode)
    {
        if (c.CentralBody is null || string.IsNullOrWhiteSpace(c.CentralBody.EntityId)) yield return Issue(AstronomyOrbitalValidationCodes.CentralBodyMissing, AstronomyKnowledgeValidationSeverity.Error, "Central body reference is required.", "$.referenceContext.centralBody", ruleId, domain, family);
        if (!Enum.IsDefined(c.ReferenceFrame) || c.ReferenceFrame == AstronomyReferenceFrame.Unspecified) yield return Issue(AstronomyOrbitalValidationCodes.ReferenceContextInvalid, AstronomyKnowledgeValidationSeverity.Error, "Reference frame must be defined.", "$.referenceContext.referenceFrame", ruleId, domain, family);
        if (!Enum.IsDefined(c.ReferenceOrigin) || c.ReferenceOrigin == AstronomyReferenceOrigin.Unspecified) yield return Issue(AstronomyOrbitalValidationCodes.ReferenceContextInvalid, AstronomyKnowledgeValidationSeverity.Error, "Reference origin must be defined.", "$.referenceContext.referenceOrigin", ruleId, domain, family);
        if (c.ReferenceOrigin == AstronomyReferenceOrigin.Topocentric) yield return Issue(AstronomyOrbitalValidationCodes.FrameOriginMismatch, AstronomyKnowledgeValidationSeverity.Critical, "Topocentric origins are contradictory for generic orbital payloads.", "$.referenceContext.referenceOrigin", ruleId, domain, family);
        if (c.ReferenceFrame == AstronomyReferenceFrame.BodyFixed && c.ReferenceOrigin != AstronomyReferenceOrigin.BodyCentric) yield return Issue(AstronomyOrbitalValidationCodes.FrameOriginMismatch, AstronomyKnowledgeValidationSeverity.Critical, "Body-fixed orbital frames require a body-centric origin.", "$.referenceContext", ruleId, domain, family);
        if (c.Epoch is null || !Enum.IsDefined(c.Epoch.Kind) || (mode == AstronomyKnowledgeValidationMode.Certification && c.Epoch.Kind == AstronomyEpochKind.Unspecified)) yield return Issue(AstronomyOrbitalValidationCodes.EpochInvalid, AstronomyKnowledgeValidationSeverity.Error, "Epoch must be structurally valid and specified for certification.", "$.referenceContext.epoch", ruleId, domain, family);
    }
    private static AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family) => new(code, severity, message, path, ruleId, domain, family);
}
