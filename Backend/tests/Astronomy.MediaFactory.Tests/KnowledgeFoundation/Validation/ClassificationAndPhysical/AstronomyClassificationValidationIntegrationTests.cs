using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Classification;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;
public sealed class AstronomyClassificationValidationIntegrationTests
{
    [Fact] public void RuleExecutionIsDeterministic() { var rule = new AstronomyClassificationPrimaryAssignmentValidationRule(); var payload = ValidationFixture.Classification(ValidationFixture.Assignment(qualifier: Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification.AstronomyClassificationQualifier.Secondary)); Assert.Equal(rule.Validate(payload, ValidationFixture.Context()).ToArray(), rule.Validate(payload, ValidationFixture.Context()).ToArray()); }
}
