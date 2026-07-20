using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;

public sealed class AstronomyPhysicalRangeValidationRule : AstronomyKnowledgeValidationRule<AstronomyPhysicalPropertiesPayload>
{
    public const string Id = "physical.range.integrity";
    public override string RuleId => Id;
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Physical;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.PhysicalProperty;
    public override int Order => 300;

    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyPhysicalPropertiesPayload payload, AstronomyKnowledgeValidationContext context)
    {
        for (var i = 0; i < payload.Properties.Count; i++)
        {
            if (payload.Properties[i].Value is not AstronomyRangePhysicalPropertyValue value) continue;
            var range = value.Range;
            var path = $"$.properties[{i}].value.range";
            if (range.Minimum.Unit.Dimension != range.Maximum.Unit.Dimension) yield return Issue(AstronomyPhysicalValidationCodes.RangeDimensionMismatch, AstronomyKnowledgeValidationSeverity.Error, "Range minimum and maximum dimensions must match.", path);
            else if (!StringComparer.Ordinal.Equals(range.Minimum.Unit.Code, range.Maximum.Unit.Code) || range.Minimum.Unit.Dimension != range.Maximum.Unit.Dimension) yield return Issue(AstronomyPhysicalValidationCodes.RangeUnitMismatch, AstronomyKnowledgeValidationSeverity.Error, "Range minimum and maximum unit identities must match.", path);
            else if (range.Minimum.Value > range.Maximum.Value) yield return Issue(AstronomyPhysicalValidationCodes.RangeOrderInvalid, AstronomyKnowledgeValidationSeverity.Error, "Range minimum cannot be greater than maximum.", path);
        }
    }
    private AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path) => new(code, severity, message, path, RuleId, Domain, Family);
}
