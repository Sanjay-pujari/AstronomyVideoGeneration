using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ClassificationAndPhysical;

internal static class ValidationFixture
{
    public static AstronomyKnowledgeValidationContext Context(AstronomyKnowledgeValidationMode mode = AstronomyKnowledgeValidationMode.Standard, AstronomyKnowledgeValidationSeverity minimum = AstronomyKnowledgeValidationSeverity.Information) => new(new AstronomyKnowledgeValidationRunId("test-run"), new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero), mode, minimum);
    public static AstronomyEntityClassificationPayload Classification(params AstronomyClassificationAssignment[] assignments) => new(new AstronomyKnowledgeTypeId("typed.classification.entity.v1"), AstronomyEntityKind.Planet, assignments);
    public static AstronomyClassificationAssignment Assignment(string scheme = "iau.taxonomy", string code = "planet", AstronomyClassificationQualifier qualifier = AstronomyClassificationQualifier.Primary, string? description = "A planet.") => new(new AstronomyClassificationSchemeId(scheme), new AstronomyClassificationValue(code, code, description), qualifier);
    public static AstronomyPhysicalPropertiesPayload Physical(params AstronomyPhysicalProperty[] properties) => new(new AstronomyKnowledgeTypeId("typed.physical.properties.v1"), properties);
    public static AstronomyPhysicalProperty Scalar(string id = "physical.radius.mean", AstronomyPhysicalPropertyQualifier? qualifier = AstronomyPhysicalPropertyQualifier.Mean, decimal value = 1m) => new(new AstronomyPhysicalPropertyId(id), AstronomyPhysicalPropertyCategory.Size, new AstronomyScalarPhysicalPropertyValue(Measurement(value)), qualifier);
    public static AstronomyPhysicalProperty Range(decimal min = 1m, decimal max = 2m) => new(new AstronomyPhysicalPropertyId("physical.radius.range"), AstronomyPhysicalPropertyCategory.Size, new AstronomyRangePhysicalPropertyValue(new AstronomyMeasurementRange(Measurement(min), Measurement(max))), AstronomyPhysicalPropertyQualifier.Reference);
    public static AstronomyPhysicalProperty Text() => new(new AstronomyPhysicalPropertyId("physical.composition.summary"), AstronomyPhysicalPropertyCategory.Compositional, new AstronomyTextPhysicalPropertyValue("Rocky"));
    public static AstronomyPhysicalProperty Boolean() => new(new AstronomyPhysicalPropertyId("physical.has.atmosphere"), AstronomyPhysicalPropertyCategory.Atmospheric, new AstronomyBooleanPhysicalPropertyValue(true));
    public static AstronomyMeasurement Measurement(decimal value = 1m, string unit = "km", AstronomyMeasurementDimension dimension = AstronomyMeasurementDimension.Distance) => new(value, new AstronomyMeasurementUnit(unit, unit, dimension), new AstronomyMeasurementPrecision(AstronomyPrecisionKind.DecimalPlaces, 0), AstronomyMeasurementUncertainty.SymmetricAbsolute(0));
}
