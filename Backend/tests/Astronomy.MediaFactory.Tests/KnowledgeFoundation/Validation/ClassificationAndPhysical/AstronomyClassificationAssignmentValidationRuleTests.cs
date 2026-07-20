using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Classification;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;
public sealed class AstronomyClassificationAssignmentValidationRuleTests
{
    [Fact] public void ValidClassificationPayloadHasNoErrors() { var result = new AstronomyClassificationAssignmentValidationRule().Validate(ValidationFixture.Classification(), ValidationFixture.Context()).ToArray(); Assert.Empty(result); }
    [Fact] public void StrictModeReportsMissingDescriptionWithStableMetadata() { var issues = new AstronomyClassificationAssignmentValidationRule().Validate(ValidationFixture.Classification(ValidationFixture.Assignment(description:null)), ValidationFixture.Context(Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.AstronomyKnowledgeValidationMode.Strict)).ToArray(); var issue = Assert.Single(issues); Assert.Equal(AstronomyClassificationValidationCodes.ValueDescriptionMissing, issue.Code); Assert.Equal("$.assignments[0].value.description", issue.Path); Assert.Equal(AstronomyClassificationAssignmentValidationRule.Id, issue.RuleId); }
}
