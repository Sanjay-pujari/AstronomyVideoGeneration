using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Classification;

public sealed class AstronomyClassificationPrimaryAssignmentValidationRule : AstronomyKnowledgeValidationRule<AstronomyEntityClassificationPayload>
{
    public const string Id = "classification.primary.cardinality";
    public override string RuleId => Id;
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Classification;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.EntityClassification;
    public override int Order => 300;

    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyEntityClassificationPayload payload, AstronomyKnowledgeValidationContext context)
    {
        var primary = payload.Assignments.Select((Assignment, Index) => (Assignment, Index)).Where(x => x.Assignment.Qualifier == AstronomyClassificationQualifier.Primary).ToArray();
        if (primary.Length == 0)
        {
            var severity = context.Mode == AstronomyKnowledgeValidationMode.Standard ? AstronomyKnowledgeValidationSeverity.Warning : AstronomyKnowledgeValidationSeverity.Error;
            yield return new AstronomyKnowledgeValidationIssue(AstronomyClassificationValidationCodes.PrimaryAssignmentMissing, severity, "A primary classification assignment is required for classification completeness.", "$.assignments", RuleId, Domain, Family);
        }
        else if (primary.Length > 1)
        {
            foreach (var duplicate in primary.Skip(1))
            {
                yield return new AstronomyKnowledgeValidationIssue(AstronomyClassificationValidationCodes.PrimaryAssignmentMultiple, AstronomyKnowledgeValidationSeverity.Error, "Only one primary classification assignment is allowed per payload.", $"$.assignments[{duplicate.Index}]", RuleId, Domain, Family);
            }
        }
    }
}
