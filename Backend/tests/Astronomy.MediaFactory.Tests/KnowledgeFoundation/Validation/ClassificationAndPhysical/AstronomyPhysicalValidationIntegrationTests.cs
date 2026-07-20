using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;
public sealed class AstronomyPhysicalValidationIntegrationTests
{
    [Fact] public void RuleExecutionIsDeterministic() { var rule = new AstronomyPhysicalPropertyValueValidationRule(); var payload = ValidationFixture.Physical(ValidationFixture.Scalar()); Assert.Equal(rule.Validate(payload, ValidationFixture.Context()).ToArray(), rule.Validate(payload, ValidationFixture.Context()).ToArray()); }
}
