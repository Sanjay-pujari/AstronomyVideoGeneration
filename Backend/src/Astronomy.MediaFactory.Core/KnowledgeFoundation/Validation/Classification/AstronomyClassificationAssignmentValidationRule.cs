using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Classification;

public sealed class AstronomyClassificationAssignmentValidationRule : AstronomyKnowledgeValidationRule<AstronomyEntityClassificationPayload>
{
    public const string Id = "classification.assignment.integrity";
    public override string RuleId => Id;
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Classification;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.EntityClassification;
    public override int Order => 100;

    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyEntityClassificationPayload payload, AstronomyKnowledgeValidationContext context)
    {
        if (payload.Assignments.Count == 0)
        {
            yield return Issue(AstronomyClassificationValidationCodes.AssignmentMissing, AstronomyKnowledgeValidationSeverity.Error, "At least one classification assignment is required.", "$.assignments");
            yield break;
        }

        for (var i = 0; i < payload.Assignments.Count; i++)
        {
            var a = payload.Assignments[i];
            if (!a.SchemeId.IsValid) yield return Issue(AstronomyClassificationValidationCodes.AssignmentMissing, AstronomyKnowledgeValidationSeverity.Error, "Classification scheme ID is required.", $"$.assignments[{i}].schemeId");
            if (string.IsNullOrWhiteSpace(a.Value.Code)) yield return Issue(AstronomyClassificationValidationCodes.AssignmentMissing, AstronomyKnowledgeValidationSeverity.Error, "Classification value code is required.", $"$.assignments[{i}].value.code");
            if (string.IsNullOrWhiteSpace(a.Value.DisplayName)) yield return Issue(AstronomyClassificationValidationCodes.ValueLabelMissing, AstronomyKnowledgeValidationSeverity.Error, "Classification value label is required.", $"$.assignments[{i}].value.displayName");
            if (!Enum.IsDefined(a.Qualifier)) yield return Issue(AstronomyClassificationValidationCodes.AssignmentMissing, AstronomyKnowledgeValidationSeverity.Error, "Classification qualifier is not defined.", $"$.assignments[{i}].qualifier");
            if (context.Mode != AstronomyKnowledgeValidationMode.Standard && string.IsNullOrWhiteSpace(a.Value.Description)) yield return Issue(AstronomyClassificationValidationCodes.ValueDescriptionMissing, AstronomyKnowledgeValidationSeverity.Warning, "Classification value description is recommended in strict validation modes.", $"$.assignments[{i}].value.description");
        }
    }

    private AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path) => new(code, severity, message, path, RuleId, Domain, Family);
}
