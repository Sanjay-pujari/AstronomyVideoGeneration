using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;

public sealed class AstronomyObservationContextValidationRule : AstronomyKnowledgeValidationRule<AstronomyObservationConditionsPayload>
{
    public const string Id = "observational.context.integrity";
    public override string RuleId => Id;
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Observational;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.ObservationCondition;
    public override int Order => 100;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyObservationConditionsPayload payload, AstronomyKnowledgeValidationContext context) => AstronomyObservationContextValidator.Validate(payload.ObservationContext, "$.observationContext", RuleId, Domain, Family, false);
}

internal static class AstronomyObservationContextValidator
{
    public static IEnumerable<AstronomyKnowledgeValidationIssue> Validate(AstronomyObservationContext c, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family, bool visibilityCodes)
    {
        if (c.ObservationTimeUtc.Offset != TimeSpan.Zero) yield return Issue(Code("time", visibilityCodes), AstronomyKnowledgeValidationSeverity.Error, "Observation time must use UTC.", path + ".observationTimeUtc", ruleId, domain, family);
        if (!Enum.IsDefined(c.ReferenceFrame)) yield return Issue(Code("frame", visibilityCodes), AstronomyKnowledgeValidationSeverity.Error, "Reference frame must be defined.", path + ".referenceFrame", ruleId, domain, family);
        if (!Enum.IsDefined(c.ReferenceOrigin)) yield return Issue(Code("origin", visibilityCodes), AstronomyKnowledgeValidationSeverity.Error, "Reference origin must be defined.", path + ".referenceOrigin", ruleId, domain, family);
        if (c.CoordinateSystem.HasValue && !Enum.IsDefined(c.CoordinateSystem.Value)) yield return Issue(Code("system", visibilityCodes), AstronomyKnowledgeValidationSeverity.Error, "Coordinate system must be defined.", path + ".coordinateSystem", ruleId, domain, family);
        if (c.CoordinateSystem == AstronomyCoordinateSystem.Horizontal && c.ReferenceOrigin != AstronomyReferenceOrigin.Topocentric) yield return Issue(Code("system", visibilityCodes), AstronomyKnowledgeValidationSeverity.Error, "Horizontal coordinates require a topocentric reference origin.", path + ".coordinateSystem", ruleId, domain, family);
        if (c.ReferenceOrigin == AstronomyReferenceOrigin.Topocentric && string.IsNullOrWhiteSpace(c.ObserverLocationReference)) yield return Issue(Code("origin", visibilityCodes), AstronomyKnowledgeValidationSeverity.Error, "Topocentric observations require an observer location reference.", path + ".observerLocationReference", ruleId, domain, family);
    }
    private static string Code(string kind, bool visibility) => visibility ? Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Visibility.AstronomyVisibilityValidationCodes.ContextInvalid : kind == "time" ? AstronomyObservationalValidationCodes.ContextTimeInvalid : kind == "system" ? AstronomyObservationalValidationCodes.ContextCoordinateSystemMismatch : AstronomyObservationalValidationCodes.ContextFrameOriginMismatch;
    private static AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family) => new(code, severity, message, path, ruleId, domain, family);
}
