using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Visibility;

public sealed class AstronomyVisibilityAssessmentValidationRule : AstronomyKnowledgeValidationRule<AstronomyVisibilityWindowsPayload>
{
    public const string Id = "visibility.assessment.integrity";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Observational; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.VisibilityWindow; public override int Order => 300;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyVisibilityWindowsPayload payload, AstronomyKnowledgeValidationContext context)
    {
        for (var i = 0; i < payload.Windows.Count; i++)
        {
            var a = payload.Windows[i].Assessment; var path = $"$.windows[{i}].assessment";
            if (!Enum.IsDefined(a.Status)) yield return new(AstronomyVisibilityValidationCodes.AssessmentInvalid, AstronomyKnowledgeValidationSeverity.Error, "Visibility status must be defined.", path + ".status", RuleId, Domain, Family);
            if (!Enum.IsDefined(a.Method)) yield return new(AstronomyVisibilityValidationCodes.AssessmentInvalid, AstronomyKnowledgeValidationSeverity.Error, "Visibility method must be defined.", path + ".method", RuleId, Domain, Family);
            var seen = new HashSet<AstronomyVisibilityLimitation>();
            for (var j = 0; j < a.Limitations.Count; j++)
            {
                var limitation = a.Limitations[j];
                if (!Enum.IsDefined(limitation)) yield return new(AstronomyVisibilityValidationCodes.AssessmentInvalid, AstronomyKnowledgeValidationSeverity.Error, "Visibility limitation must be defined.", path + $".limitations[{j}]", RuleId, Domain, Family);
                if (!seen.Add(limitation)) yield return new(AstronomyVisibilityValidationCodes.LimitationDuplicate, AstronomyKnowledgeValidationSeverity.Error, "Duplicate visibility limitation.", path + $".limitations[{j}]", RuleId, Domain, Family);
            }
            if (a.Limitations.Count > 1 && a.Limitations.Contains(AstronomyVisibilityLimitation.None)) yield return new(AstronomyVisibilityValidationCodes.LimitationConflict, AstronomyKnowledgeValidationSeverity.Error, "No limitation cannot be combined with concrete visibility limitations.", path + ".limitations", RuleId, Domain, Family);
        }
    }
}
