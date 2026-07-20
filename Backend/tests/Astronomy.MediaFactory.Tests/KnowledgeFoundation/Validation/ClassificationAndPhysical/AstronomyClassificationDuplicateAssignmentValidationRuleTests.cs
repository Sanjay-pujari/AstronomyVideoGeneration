using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Classification;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;
public sealed class AstronomyClassificationDuplicateAssignmentValidationRuleTests
{
    [Fact] public void SameValueUnderDifferentSchemesIsNotDuplicate() { var payload = ValidationFixture.Classification(ValidationFixture.Assignment("iau.taxonomy", "planet", AstronomyClassificationQualifier.Primary), ValidationFixture.Assignment("local.taxonomy", "planet", AstronomyClassificationQualifier.Primary)); Assert.Empty(new AstronomyClassificationDuplicateAssignmentValidationRule().Validate(payload, ValidationFixture.Context())); }
    [Fact] public void DuplicateExactAssignmentIsConstructorProtected() { Assert.Throws<ArgumentException>(() => ValidationFixture.Classification(ValidationFixture.Assignment(), ValidationFixture.Assignment())); }
}
