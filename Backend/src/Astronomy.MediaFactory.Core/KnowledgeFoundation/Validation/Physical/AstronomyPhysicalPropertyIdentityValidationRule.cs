using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;

public sealed class AstronomyPhysicalPropertyIdentityValidationRule : AstronomyKnowledgeValidationRule<AstronomyPhysicalPropertiesPayload>
{
    public const string Id = "physical.property.identity";
    public override string RuleId => Id;
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Physical;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.PhysicalProperty;
    public override int Order => 100;

    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyPhysicalPropertiesPayload payload, AstronomyKnowledgeValidationContext context)
    {
        if (payload.Properties.Count == 0) { yield return Issue(AstronomyPhysicalValidationCodes.PropertyMissing, AstronomyKnowledgeValidationSeverity.Error, "At least one physical property is required.", "$.properties"); yield break; }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < payload.Properties.Count; i++)
        {
            var p = payload.Properties[i];
            if (!p.PropertyId.IsValid) yield return Issue(AstronomyPhysicalValidationCodes.PropertyMissing, AstronomyKnowledgeValidationSeverity.Error, "Physical property ID is required.", $"$.properties[{i}].propertyId");
            if (!Enum.IsDefined(p.Category)) yield return Issue(AstronomyPhysicalValidationCodes.PropertyCategoryInvalid, AstronomyKnowledgeValidationSeverity.Error, "Physical property category is not defined.", $"$.properties[{i}].category");
            if (p.Qualifier.HasValue && !Enum.IsDefined(p.Qualifier.Value)) yield return Issue(AstronomyPhysicalValidationCodes.PropertyQualifierInvalid, AstronomyKnowledgeValidationSeverity.Error, "Physical property qualifier is not defined.", $"$.properties[{i}].qualifier");
            if (p.Note is not null && string.IsNullOrWhiteSpace(p.Note)) yield return Issue(AstronomyPhysicalValidationCodes.PropertyNoteBlank, AstronomyKnowledgeValidationSeverity.Warning, "Physical property note cannot be blank when supplied.", $"$.properties[{i}].note");
            var identity = string.Concat(p.PropertyId.Value, "\u001f", p.Qualifier.HasValue ? ((int)p.Qualifier.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) : "none");
            if (!seen.Add(identity)) yield return Issue(AstronomyPhysicalValidationCodes.PropertyDuplicate, AstronomyKnowledgeValidationSeverity.Error, "Physical properties must be unique by property ID and qualifier.", $"$.properties[{i}]");
        }
    }
    private AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path) => new(code, severity, message, path, RuleId, Domain, Family);
}
