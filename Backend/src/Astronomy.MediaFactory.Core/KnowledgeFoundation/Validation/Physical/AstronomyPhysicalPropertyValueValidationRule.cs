using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;

public sealed class AstronomyPhysicalPropertyValueValidationRule : AstronomyKnowledgeValidationRule<AstronomyPhysicalPropertiesPayload>
{
    public const string Id = "physical.property.value";
    public override string RuleId => Id;
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Physical;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.PhysicalProperty;
    public override int Order => 200;

    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyPhysicalPropertiesPayload payload, AstronomyKnowledgeValidationContext context)
    {
        for (var i = 0; i < payload.Properties.Count; i++)
        {
            var path = $"$.properties[{i}].value";
            foreach (var issue in ValidateValue(payload.Properties[i].Value, path)) yield return issue;
        }
    }

    private IEnumerable<AstronomyKnowledgeValidationIssue> ValidateValue(AstronomyPhysicalPropertyValue value, string path)
    {
        switch (value)
        {
            case AstronomyScalarPhysicalPropertyValue scalar:
                if (scalar.Kind != AstronomyPhysicalPropertyValueKind.ScalarMeasurement) yield return Issue(AstronomyPhysicalValidationCodes.ValueKindMismatch, AstronomyKnowledgeValidationSeverity.Critical, "Scalar physical property value kind must be ScalarMeasurement.", path + ".kind");
                foreach (var issue in AstronomyPhysicalMeasurementValidator.Validate(scalar.Measurement, path + ".measurement", RuleId, Domain, Family)) yield return issue;
                break;
            case AstronomyRangePhysicalPropertyValue range:
                if (range.Kind != AstronomyPhysicalPropertyValueKind.MeasurementRange) yield return Issue(AstronomyPhysicalValidationCodes.ValueKindMismatch, AstronomyKnowledgeValidationSeverity.Critical, "Range physical property value kind must be MeasurementRange.", path + ".kind");
                foreach (var issue in AstronomyPhysicalMeasurementValidator.Validate(range.Range.Minimum, path + ".range.minimum", RuleId, Domain, Family)) yield return issue;
                foreach (var issue in AstronomyPhysicalMeasurementValidator.Validate(range.Range.Maximum, path + ".range.maximum", RuleId, Domain, Family)) yield return issue;
                break;
            case AstronomyTextPhysicalPropertyValue text:
                if (text.Kind != AstronomyPhysicalPropertyValueKind.Text) yield return Issue(AstronomyPhysicalValidationCodes.ValueKindMismatch, AstronomyKnowledgeValidationSeverity.Critical, "Text physical property value kind must be Text.", path + ".kind");
                if (string.IsNullOrWhiteSpace(text.Value)) yield return Issue(AstronomyPhysicalValidationCodes.TextValueBlank, AstronomyKnowledgeValidationSeverity.Error, "Physical text value cannot be blank.", path + ".value");
                break;
            case AstronomyBooleanPhysicalPropertyValue boolean:
                if (boolean.Kind != AstronomyPhysicalPropertyValueKind.Boolean) yield return Issue(AstronomyPhysicalValidationCodes.ValueKindMismatch, AstronomyKnowledgeValidationSeverity.Critical, "Boolean physical property value kind must be Boolean.", path + ".kind");
                break;
            default:
                yield return Issue(AstronomyPhysicalValidationCodes.ValueKindMismatch, AstronomyKnowledgeValidationSeverity.Critical, "Physical property value runtime type is not a supported closed hierarchy variant.", path);
                break;
        }
    }
    private AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path) => new(code, severity, message, path, RuleId, Domain, Family);
}
