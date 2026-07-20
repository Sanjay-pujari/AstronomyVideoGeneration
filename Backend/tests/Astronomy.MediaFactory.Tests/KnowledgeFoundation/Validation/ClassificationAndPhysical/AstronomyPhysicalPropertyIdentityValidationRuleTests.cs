using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;
public sealed class AstronomyPhysicalPropertyIdentityValidationRuleTests
{
    [Fact] public void ValidScalarPropertyPasses() => Assert.Empty(new AstronomyPhysicalPropertyIdentityValidationRule().Validate(ValidationFixture.Physical(ValidationFixture.Scalar()), ValidationFixture.Context()));
    [Fact] public void SamePropertyIdWithDifferentQualifierAllowed() { var payload = ValidationFixture.Physical(ValidationFixture.Scalar(qualifier:AstronomyPhysicalPropertyQualifier.Mean), ValidationFixture.Scalar(qualifier:AstronomyPhysicalPropertyQualifier.Equatorial)); Assert.Empty(new AstronomyPhysicalPropertyIdentityValidationRule().Validate(payload, ValidationFixture.Context()).Where(i => i.Code == AstronomyPhysicalValidationCodes.PropertyDuplicate)); }
    [Fact] public void DuplicateIdentityIsConstructorProtected() { Assert.Throws<ArgumentException>(() => ValidationFixture.Physical(ValidationFixture.Scalar(), ValidationFixture.Scalar())); }
}
