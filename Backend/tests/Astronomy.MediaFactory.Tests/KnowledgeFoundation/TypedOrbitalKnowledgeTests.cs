using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using TypedDomain = Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgeDomain;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedOrbitalKnowledgeTests
{
    [Fact]
    public void Orbital_parameter_id_normalizes_and_guards_token_shape()
    {
        var id = new AstronomyOrbitalParameterId(" Orbital.Semi-Major-Axis ");
        Assert.Equal("orbital.semi-major-axis", id.Value);
        Assert.Equal(id, new AstronomyOrbitalParameterId("orbital.semi-major-axis"));
        Assert.Equal("orbital.semi-major-axis", id.ToString());
        Assert.False(default(AstronomyOrbitalParameterId).IsValid);
        Assert.Throws<ArgumentException>(() => new AstronomyOrbitalParameterId(" "));
        Assert.Throws<ArgumentException>(() => new AstronomyOrbitalParameterId("orbital semi-major-axis"));
        Assert.Throws<ArgumentException>(() => new AstronomyOrbitalParameterId("orbital\tsemi"));
        _ = new AstronomyOrbitalParameterId(new string('a', 128));
        Assert.Throws<ArgumentException>(() => new AstronomyOrbitalParameterId(new string('a', 129)));
    }

    [Fact]
    public void Orbital_contracts_reuse_measurement_entity_frame_origin_and_epoch_contracts()
    {
        var measurement = Measure(1, AstronomyMeasurementDimension.Distance);
        var epoch = AstronomyEpochReference.J2000;
        var parameter = new AstronomyOrbitalParameter(new("orbital.semi-major-axis"), AstronomyOrbitalParameterCategory.Size, measurement, AstronomyOrbitalParameterQualifier.Mean, epoch, " note ");
        var context = Context();
        var element = new AstronomyKeplerianElement(AstronomyKeplerianElementType.SemiMajorAxis, measurement, AstronomyOrbitalParameterQualifier.Mean, " element ");

        Assert.Equal("note", parameter.Note);
        Assert.Equal("element", element.Note);
        Assert.Equal(new AstronomyEntityReference("sun", AstronomyEntityKind.Star, "Sun"), context.CentralBody);
        Assert.Throws<ArgumentException>(() => new AstronomyOrbitalParameter(default, AstronomyOrbitalParameterCategory.Size, measurement));
        Assert.Throws<ArgumentNullException>(() => new AstronomyOrbitalParameter(new("orbital.x"), AstronomyOrbitalParameterCategory.Size, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyOrbitalParameter(new("orbital.x"), (AstronomyOrbitalParameterCategory)99, measurement));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyOrbitalParameter(new("orbital.x"), AstronomyOrbitalParameterCategory.Size, measurement, (AstronomyOrbitalParameterQualifier)99));
        Assert.Throws<ArgumentException>(() => new AstronomyOrbitalParameter(new("orbital.x"), AstronomyOrbitalParameterCategory.Size, measurement, note: "bad\n"));
        Assert.Throws<ArgumentNullException>(() => new AstronomyOrbitalReferenceContext(null!, AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin.Heliocentric, epoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyOrbitalReferenceContext(new("sun"), (AstronomyReferenceFrame)99, AstronomyReferenceOrigin.Heliocentric, epoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyKeplerianElement((AstronomyKeplerianElementType)99, measurement));
    }

    [Fact]
    public void Orbital_payloads_are_typed_ordered_copied_and_reject_duplicates()
    {
        var sma = new AstronomyKeplerianElement(AstronomyKeplerianElementType.SemiMajorAxis, Measure(1, AstronomyMeasurementDimension.Distance));
        var ecc = new AstronomyKeplerianElement(AstronomyKeplerianElementType.Eccentricity, Measure(.1m, AstronomyMeasurementDimension.Dimensionless));
        var elements = new List<AstronomyKeplerianElement> { sma, ecc };
        var payload = new AstronomyKeplerianElementsPayload(new("typed.orbital.keplerian.v1"), Context(), elements);
        elements.Clear();

        Assert.IsAssignableFrom<ITypedAstronomyKnowledgePayload>(payload);
        Assert.Equal(TypedDomain.Orbital, payload.Domain);
        Assert.Equal(AstronomyKnowledgePayloadFamily.OrbitalParameter, payload.Family);
        Assert.Equal([AstronomyKeplerianElementType.SemiMajorAxis, AstronomyKeplerianElementType.Eccentricity], payload.Elements.Select(e => e.ElementType).ToArray());
        Assert.Equal(payload, new AstronomyKeplerianElementsPayload(new("typed.orbital.keplerian.v1"), Context(), [ecc, sma]));
        Assert.Equal(payload.GetHashCode(), new AstronomyKeplerianElementsPayload(new("typed.orbital.keplerian.v1"), Context(), [ecc, sma]).GetHashCode());
        Assert.Throws<ArgumentException>(() => new AstronomyKeplerianElementsPayload(new("typed.orbital.keplerian.v1"), Context(), []));
        Assert.Throws<ArgumentException>(() => new AstronomyKeplerianElementsPayload(new("typed.orbital.keplerian.v1"), Context(), [sma, sma]));

        var mean = new AstronomyOrbitalParameter(new("orbital.mean-anomaly"), AstronomyOrbitalParameterCategory.Phase, Measure(2, AstronomyMeasurementDimension.Angle), AstronomyOrbitalParameterQualifier.Mean);
        var osc = new AstronomyOrbitalParameter(new("orbital.mean-anomaly"), AstronomyOrbitalParameterCategory.Phase, Measure(3, AstronomyMeasurementDimension.Angle), AstronomyOrbitalParameterQualifier.Osculating);
        var generic = new AstronomyOrbitalParametersPayload(new("typed.orbital.parameters.v1"), Context(), [osc, mean]);
        Assert.Equal(AstronomyKnowledgePayloadFamily.OrbitalParameter, generic.Family);
        Assert.Equal(generic, new AstronomyOrbitalParametersPayload(new("typed.orbital.parameters.v1"), Context(), [mean, osc]));
        Assert.Throws<ArgumentException>(() => new AstronomyOrbitalParametersPayload(new("typed.orbital.parameters.v1"), Context(), [mean, mean]));
    }

    private static AstronomyOrbitalReferenceContext Context() => new(new AstronomyEntityReference("sun", AstronomyEntityKind.Star, "Sun"), AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin.Heliocentric, AstronomyEpochReference.J2000);
    private static AstronomyMeasurement Measure(decimal value, AstronomyMeasurementDimension dimension) => new(value, new AstronomyMeasurementUnit($"u.{dimension.ToString().ToLowerInvariant()}", "u", dimension));
}
