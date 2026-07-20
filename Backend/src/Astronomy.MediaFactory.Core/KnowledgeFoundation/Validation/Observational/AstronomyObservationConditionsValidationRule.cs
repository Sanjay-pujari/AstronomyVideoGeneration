using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;

public sealed class AstronomyObservationConditionsValidationRule : AstronomyKnowledgeValidationRule<AstronomyObservationConditionsPayload>
{
    public const string Id = "observational.conditions.integrity";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Observational; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.ObservationCondition; public override int Order => 200;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyObservationConditionsPayload payload, AstronomyKnowledgeValidationContext context)
    {
        var c = payload.Conditions;
        if (c.LimitingMagnitude is not null) foreach (var i in AstronomyObservationalMeasurementValidator.Validate(c.LimitingMagnitude, "$.conditions.limitingMagnitude", RuleId, Domain, Family)) yield return i;
        if (c.SkyBrightness is not null) foreach (var i in AstronomyObservationalMeasurementValidator.Validate(c.SkyBrightness, "$.conditions.skyBrightness", RuleId, Domain, Family)) yield return i;
        if ((c.SkyCondition == AstronomySkyConditionKind.Clear || c.SkyCondition == AstronomySkyConditionKind.MostlyClear) && c.Transparency == AstronomyTransparencyQuality.VeryPoor)
            yield return new(AstronomyObservationalValidationCodes.ConditionCombinationSuspicious, AstronomyKnowledgeValidationSeverity.Warning, "Clear sky conditions with very poor transparency are suspicious and should be reviewed.", "$.conditions", RuleId, Domain, Family);
        if (c.Seeing == AstronomySeeingQuality.Excellent && c.SkyCondition is AstronomySkyConditionKind.Overcast or AstronomySkyConditionKind.Foggy)
            yield return new(AstronomyObservationalValidationCodes.ConditionCombinationSuspicious, AstronomyKnowledgeValidationSeverity.Warning, "Excellent seeing with obstructed sky conditions is suspicious and should be reviewed.", "$.conditions", RuleId, Domain, Family);
    }
}
