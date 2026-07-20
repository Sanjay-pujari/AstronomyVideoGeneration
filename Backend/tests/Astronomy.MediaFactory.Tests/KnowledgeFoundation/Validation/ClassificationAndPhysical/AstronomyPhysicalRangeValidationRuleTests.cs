using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Physical;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;
public sealed class AstronomyPhysicalRangeValidationRuleTests
{
    [Fact] public void ValidRangePasses() => Assert.Empty(new AstronomyPhysicalRangeValidationRule().Validate(ValidationFixture.Physical(ValidationFixture.Range()), ValidationFixture.Context()));
    [Fact] public void InvalidRangeOrderingIsConstructorProtected() => Assert.Throws<ArgumentException>(() => new AstronomyMeasurementRange(ValidationFixture.Measurement(3), ValidationFixture.Measurement(2)));
    [Fact] public void RangeDimensionMismatchIsConstructorProtected() => Assert.Throws<ArgumentException>(() => new AstronomyMeasurementRange(ValidationFixture.Measurement(1, "km", AstronomyMeasurementDimension.Distance), ValidationFixture.Measurement(2, "s", AstronomyMeasurementDimension.Time)));
}
