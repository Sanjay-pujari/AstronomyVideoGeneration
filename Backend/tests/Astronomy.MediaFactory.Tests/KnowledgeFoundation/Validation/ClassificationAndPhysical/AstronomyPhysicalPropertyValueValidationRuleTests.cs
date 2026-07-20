using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;
public sealed class AstronomyPhysicalPropertyValueValidationRuleTests
{
    [Fact] public void AllPhysicalValueVariantsExecute() { var payload = ValidationFixture.Physical(ValidationFixture.Scalar(), ValidationFixture.Range(), ValidationFixture.Text(), ValidationFixture.Boolean()); Assert.Empty(new AstronomyPhysicalPropertyValueValidationRule().Validate(payload, ValidationFixture.Context())); }
    [Fact] public void TextWhitespaceIsConstructorProtected() { Assert.Throws<ArgumentException>(() => new Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical.AstronomyTextPhysicalPropertyValue("   ")); }
}
