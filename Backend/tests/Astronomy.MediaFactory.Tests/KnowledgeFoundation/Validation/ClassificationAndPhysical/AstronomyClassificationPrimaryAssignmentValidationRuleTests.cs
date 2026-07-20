using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Classification;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;
public sealed class AstronomyClassificationPrimaryAssignmentValidationRuleTests
{
    [Fact] public void MissingPrimaryDiffersByMode() { var payload = ValidationFixture.Classification(ValidationFixture.Assignment(qualifier:AstronomyClassificationQualifier.Secondary)); Assert.Equal(AstronomyKnowledgeValidationSeverity.Warning, Assert.Single(new AstronomyClassificationPrimaryAssignmentValidationRule().Validate(payload, ValidationFixture.Context())).Severity); Assert.Equal(AstronomyKnowledgeValidationSeverity.Error, Assert.Single(new AstronomyClassificationPrimaryAssignmentValidationRule().Validate(payload, ValidationFixture.Context(AstronomyKnowledgeValidationMode.Strict))).Severity); }
    [Fact] public void MultiplePrimaryAssignmentsDetectedAcrossSchemes() { var payload = ValidationFixture.Classification(ValidationFixture.Assignment("iau", "planet"), ValidationFixture.Assignment("local", "world")); var issue = Assert.Single(new AstronomyClassificationPrimaryAssignmentValidationRule().Validate(payload, ValidationFixture.Context())); Assert.Equal(AstronomyClassificationValidationCodes.PrimaryAssignmentMultiple, issue.Code); }
}
