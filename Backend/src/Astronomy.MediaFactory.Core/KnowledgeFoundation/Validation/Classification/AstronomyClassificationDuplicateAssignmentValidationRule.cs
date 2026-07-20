using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Classification;

public sealed class AstronomyClassificationDuplicateAssignmentValidationRule : AstronomyKnowledgeValidationRule<AstronomyEntityClassificationPayload>
{
    public const string Id = "classification.assignment.uniqueness";
    public override string RuleId => Id;
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Classification;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.EntityClassification;
    public override int Order => 200;

    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyEntityClassificationPayload payload, AstronomyKnowledgeValidationContext context)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < payload.Assignments.Count; i++)
        {
            var a = payload.Assignments[i];
            var identity = string.Concat(a.SchemeId.Value, "\u001f", a.Value.Code, "\u001f", (int)a.Qualifier);
            if (!seen.Add(identity))
            {
                yield return new AstronomyKnowledgeValidationIssue(AstronomyClassificationValidationCodes.AssignmentDuplicate, AstronomyKnowledgeValidationSeverity.Error, "Classification assignments must be unique by scheme ID, value code, and qualifier.", $"$.assignments[{i}]", RuleId, Domain, Family);
            }
        }
    }
}
