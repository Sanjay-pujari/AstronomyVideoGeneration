using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Orbital;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.OrbitalAndPositional;

public sealed class AstronomyOrbitalMeasurementValidationTests
{
    private static readonly IReadOnlyDictionary<AstronomyKeplerianElementType, AstronomyMeasurementDimension> Mapped = new Dictionary<AstronomyKeplerianElementType, AstronomyMeasurementDimension>
    {
        [AstronomyKeplerianElementType.SemiMajorAxis] = AstronomyMeasurementDimension.Distance,
        [AstronomyKeplerianElementType.PeriapsisDistance] = AstronomyMeasurementDimension.Distance,
        [AstronomyKeplerianElementType.ApoapsisDistance] = AstronomyMeasurementDimension.Distance,
        [AstronomyKeplerianElementType.Eccentricity] = AstronomyMeasurementDimension.Dimensionless,
        [AstronomyKeplerianElementType.Inclination] = AstronomyMeasurementDimension.Angle,
        [AstronomyKeplerianElementType.LongitudeOfAscendingNode] = AstronomyMeasurementDimension.Angle,
        [AstronomyKeplerianElementType.ArgumentOfPeriapsis] = AstronomyMeasurementDimension.Angle,
        [AstronomyKeplerianElementType.MeanAnomaly] = AstronomyMeasurementDimension.Angle,
        [AstronomyKeplerianElementType.TrueAnomaly] = AstronomyMeasurementDimension.Angle,
        [AstronomyKeplerianElementType.EccentricAnomaly] = AstronomyMeasurementDimension.Angle,
        [AstronomyKeplerianElementType.MeanLongitude] = AstronomyMeasurementDimension.Angle,
        [AstronomyKeplerianElementType.LongitudeOfPeriapsis] = AstronomyMeasurementDimension.Angle,
        [AstronomyKeplerianElementType.OrbitalPeriod] = AstronomyMeasurementDimension.Time,
    };

    private static readonly AstronomyKeplerianElementType[] DeliberatelyUnmapped = [];

    [Fact]
    public void Keplerian_dimension_catalog_has_explicit_policy_for_every_element()
    {
        var explicitPolicy = Mapped.Keys.Concat(DeliberatelyUnmapped).OrderBy(x => (int)x).ToArray();
        Assert.Equal(Enum.GetValues<AstronomyKeplerianElementType>().OrderBy(x => (int)x), explicitPolicy);
    }

    [Fact]
    public void Every_mapped_keplerian_element_has_correct_dimension()
    {
        foreach (var (type, expected) in Mapped)
        {
            Assert.True(AstronomyKeplerianElementDimensionCatalog.TryGetExpectedDimension(type, out var actual));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Defined_but_unmapped_keplerian_element_does_not_receive_default_dimension()
    {
        foreach (var type in DeliberatelyUnmapped)
        {
            Assert.False(AstronomyKeplerianElementDimensionCatalog.TryGetExpectedDimension(type, out var actual));
            Assert.Equal(default, actual);
        }
    }

    [Fact]
    public void Orbital_measurement_validation_is_executed_and_uses_measurement_codes()
    {
        var measurement = new AstronomyMeasurement(1m, new AstronomyMeasurementUnit("km", "km", AstronomyMeasurementDimension.Distance), new AstronomyMeasurementPrecision(AstronomyPrecisionKind.SignificantFigures, 0));
        var payload = new AstronomyOrbitalParametersPayload(new("typed.orbital.parameters.v1"), OrbitalPositionalValidationFixture.OrbitalContext(), [new AstronomyOrbitalParameter(new("orbital.distance.current"), AstronomyOrbitalParameterCategory.Distance, measurement)]);
        var issues = new AstronomyOrbitalParametersValidationRule().Validate(payload, OrbitalPositionalValidationFixture.Context()).ToArray();
        Assert.Contains(issues, i => i.Code == AstronomyOrbitalValidationCodes.MeasurementPrecisionInvalid && i.Path == "$.parameters[0].measurement.precision");
        Assert.DoesNotContain(issues, i => i.Code == AstronomyOrbitalValidationCodes.ReferenceContextInvalid);
    }

    [Fact]
    public void Orbital_validation_registration_is_idempotent_and_descriptor_matches_runtime_metadata()
    {
        var services = new ServiceCollection().AddAstronomyOrbitalValidation().AddAstronomyOrbitalValidation();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAstronomyKnowledgeValidationRuleRegistry>();
        var descriptor = Assert.Single(registry.Descriptors, d => d.RuleId == AstronomyOrbitalParametersValidationRule.Id);
        var runtime = Assert.IsType<AstronomyOrbitalParametersValidationRule>(provider.GetServices<IAstronomyKnowledgeValidationRule>().Single(r => r.RuleId == AstronomyOrbitalParametersValidationRule.Id));
        Assert.Equal(runtime.Domain, descriptor.Domain);
        Assert.Equal(runtime.Family, descriptor.Family);
        Assert.Equal(runtime.Order, descriptor.Order);
    }

    [Fact]
    public void Exact_epoch_type_defines_parameter_duplicate_identity()
    {
        var identityType = typeof(AstronomyOrbitalParametersValidationRule).GetNestedType("ParameterIdentity", System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(identityType);
        Assert.Contains(identityType!.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance), property => property.Name == "Epoch" && property.PropertyType == typeof(AstronomyEpochReference));
    }
}
