using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Visibility;

public sealed class AstronomyVisibilityContextValidationRule : AstronomyKnowledgeValidationRule<AstronomyVisibilityWindowsPayload>
{
    public const string Id = "visibility.context.integrity";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Observational; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.VisibilityWindow; public override int Order => 100;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyVisibilityWindowsPayload payload, AstronomyKnowledgeValidationContext context) => AstronomyObservationContextValidator.Validate(payload.ObservationContext, "$.observationContext", RuleId, Domain, Family, true);
}
