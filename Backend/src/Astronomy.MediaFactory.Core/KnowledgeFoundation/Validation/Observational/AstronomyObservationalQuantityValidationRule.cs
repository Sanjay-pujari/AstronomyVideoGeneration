using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;

public sealed class AstronomyObservationalQuantityValidationRule : AstronomyKnowledgeValidationRule<AstronomyObservationConditionsPayload>
{
    public const string Id = "observational.quantity.integrity";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Observational; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.ObservationCondition; public override int Order => 300;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyObservationConditionsPayload payload, AstronomyKnowledgeValidationContext context)
    {
        var seen = new HashSet<(AstronomyObservationalQuantityId, AstronomyObservationalQuantityQualifier?, object?)>();
        for (var i = 0; i < payload.Quantities.Count; i++)
        {
            var q = payload.Quantities[i]; var path = $"$.quantities[{i}]";
            var key = (q.QuantityId, q.Qualifier, (object?)q.Epoch);
            if (!seen.Add(key)) yield return new(AstronomyObservationalValidationCodes.QuantityDuplicate, AstronomyKnowledgeValidationSeverity.Error, "Duplicate observational quantity identity.", path, RuleId, Domain, Family);
            foreach (var issue in AstronomyObservationalMeasurementValidator.Validate(q.Measurement, path + ".measurement", RuleId, Domain, Family)) yield return issue;
            if (AstronomyObservationalQuantityDimensionCatalog.TryGetExpectedDimension(q.Category, out var expected) && q.Measurement.Unit.Dimension != expected)
                yield return new(AstronomyObservationalValidationCodes.QuantityDimensionMismatch, AstronomyKnowledgeValidationSeverity.Error, "Observational quantity measurement dimension does not match its category.", path + ".measurement.unit.dimension", RuleId, Domain, Family);
        }
    }
}
