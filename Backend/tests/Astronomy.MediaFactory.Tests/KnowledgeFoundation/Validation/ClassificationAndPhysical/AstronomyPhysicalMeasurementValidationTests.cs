using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;
public sealed class AstronomyPhysicalMeasurementValidationTests
{
    [Fact] public void NegativeMeasurementValueIsAllowed() { var measurement = ValidationFixture.Measurement(-1m); Assert.Equal(-1m, measurement.Value); }
    [Fact] public void NegativeUncertaintyIsConstructorProtected() => Assert.Throws<ArgumentOutOfRangeException>(() => AstronomyMeasurementUncertainty.SymmetricAbsolute(-1m));
    [Fact] public void InvalidPrecisionIsConstructorProtected() => Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyMeasurementPrecision(AstronomyPrecisionKind.DecimalPlaces, -1));
}
