using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public class TypedObservationalKnowledgeTests
{
    [Fact]
    public void ObservationalQuantityId_UsesStableTokenSemantics()
    {
        var id = new AstronomyObservationalQuantityId(" Observational.Apparent-Magnitude ");
        Assert.Equal("observational.apparent-magnitude", id.Value);
        Assert.Equal(id, new AstronomyObservationalQuantityId("observational.apparent-magnitude"));
        Assert.Equal("observational.apparent-magnitude", id.ToString());
        Assert.False(default(AstronomyObservationalQuantityId).IsValid);
        Assert.Throws<ArgumentException>(() => new AstronomyObservationalQuantityId(" "));
        Assert.Throws<ArgumentException>(() => new AstronomyObservationalQuantityId("observational apparent"));
        Assert.Throws<ArgumentException>(() => new AstronomyObservationalQuantityId("observational\tapparent"));
        Assert.Throws<ArgumentException>(() => new AstronomyObservationalQuantityId("observational\rapparent"));
        Assert.Throws<ArgumentException>(() => new AstronomyObservationalQuantityId("observational\napparent"));
        Assert.True(new AstronomyObservationalQuantityId(new string('a', 128)).IsValid);
        Assert.Throws<ArgumentException>(() => new AstronomyObservationalQuantityId(new string('a', 129)));
    }

    [Fact]
    public void ObservationalQuantityTaxonomies_AreClosedAndGuarded()
    {
        Assert.Equal(new[] { "Brightness", "AngularSize", "AngularSeparation", "HorizontalPosition", "Illumination", "SkyCondition", "AtmosphericCondition", "InstrumentCondition", "Other" }, Enum.GetNames<AstronomyObservationalQuantityCategory>());
        Assert.Equal(new[] { "Apparent", "Observed", "Estimated", "ModelDerived", "Corrected", "Uncorrected", "Mean", "Minimum", "Maximum", "Reference" }, Enum.GetNames<AstronomyObservationalQuantityQualifier>());
        Assert.Equal(Enum.GetValues<AstronomyObservationalQuantityCategory>().Distinct().Count(), Enum.GetValues<AstronomyObservationalQuantityCategory>().Length);
        Assert.Equal(Enum.GetValues<AstronomyObservationalQuantityQualifier>().Distinct().Count(), Enum.GetValues<AstronomyObservationalQuantityQualifier>().Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyObservationalQuantity(QuantityId(), (AstronomyObservationalQuantityCategory)999, Magnitude()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyObservationalQuantity(QuantityId(), AstronomyObservationalQuantityCategory.Brightness, Magnitude(), (AstronomyObservationalQuantityQualifier)999));
    }

    [Fact]
    public void ObservationalQuantity_EnforcesLocalInvariants()
    {
        var epoch = AstronomyEpochReference.Custom(Utc(2026, 7, 20, 1));
        var quantity = new AstronomyObservationalQuantity(QuantityId(), AstronomyObservationalQuantityCategory.Brightness, Magnitude(), AstronomyObservationalQuantityQualifier.Apparent, epoch, " supplied ");
        Assert.Equal("supplied", quantity.Note);
        Assert.Equal(quantity, new AstronomyObservationalQuantity(QuantityId(), AstronomyObservationalQuantityCategory.Brightness, Magnitude(), AstronomyObservationalQuantityQualifier.Apparent, epoch, "supplied"));
        Assert.Null(new AstronomyObservationalQuantity(QuantityId(), AstronomyObservationalQuantityCategory.Brightness, Magnitude(), note: " ").Note);
        Assert.Throws<ArgumentException>(() => new AstronomyObservationalQuantity(default, AstronomyObservationalQuantityCategory.Brightness, Magnitude()));
        Assert.Throws<ArgumentNullException>(() => new AstronomyObservationalQuantity(QuantityId(), AstronomyObservationalQuantityCategory.Brightness, null!));
        Assert.Throws<ArgumentException>(() => new AstronomyObservationalQuantity(QuantityId(), AstronomyObservationalQuantityCategory.Brightness, Magnitude(), note: "bad\n"));
        Assert.Throws<ArgumentException>(() => new AstronomyObservationalQuantity(QuantityId(), AstronomyObservationalQuantityCategory.Brightness, Magnitude(), note: new string('a', 513)));
    }

    [Fact]
    public void HorizontalCoordinate_RequiresExplicitAzimuthAndAltitudeComponents()
    {
        var az = new AstronomyAngularCoordinateValue(AstronomyAngularCoordinateComponent.Azimuth, Angle(180));
        var alt = new AstronomyAngularCoordinateValue(AstronomyAngularCoordinateComponent.Altitude, Angle(-5));
        var coordinate = new AstronomyHorizontalObservationCoordinate(az, alt);
        Assert.Equal(coordinate, new AstronomyHorizontalObservationCoordinate(az, alt));
        Assert.Throws<ArgumentException>(() => new AstronomyHorizontalObservationCoordinate(alt, az));
        Assert.Throws<ArgumentNullException>(() => new AstronomyHorizontalObservationCoordinate(null!, alt));
        Assert.DoesNotContain(typeof(AstronomyHorizontalObservationCoordinate).GetMethods().Select(m => m.Name), n => n.Contains("Transform", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ObservationConditionsPayload_MapsDomainAndNormalizesQuantities()
    {
        var q1 = new AstronomyObservationalQuantity(new("observational.sky-brightness"), AstronomyObservationalQuantityCategory.SkyCondition, Magnitude(), AstronomyObservationalQuantityQualifier.Observed);
        var q2 = new AstronomyObservationalQuantity(new("observational.apparent-magnitude"), AstronomyObservationalQuantityCategory.Brightness, Magnitude(), AstronomyObservationalQuantityQualifier.Apparent);
        var source = new List<AstronomyObservationalQuantity> { q1, q2 };
        var payload = new AstronomyObservationConditionsPayload(new("typed.observation.conditions.v1"), Context(), new(AstronomySkyConditionKind.Clear, AstronomySeeingQuality.Good, AstronomyTransparencyQuality.VeryGood, note: " clear "), source);
        source.Clear();
        Assert.IsAssignableFrom<ITypedAstronomyKnowledgePayload>(payload);
        Assert.Equal(AstronomyKnowledgeDomain.Observational, payload.Domain);
        Assert.Equal(AstronomyKnowledgePayloadFamily.ObservationCondition, payload.Family);
        Assert.Equal("clear", payload.Conditions.Note);
        Assert.Equal(new[] { q2, q1 }, payload.Quantities);
        Assert.Throws<ArgumentException>(() => new AstronomyObservationConditionsPayload(default, Context(), payload.Conditions));
        Assert.Throws<ArgumentNullException>(() => new AstronomyObservationConditionsPayload(new("typed.observation.conditions.v1"), null!, payload.Conditions));
        Assert.Throws<ArgumentException>(() => new AstronomyObservationConditionsPayload(new("typed.observation.conditions.v1"), Context(), payload.Conditions, [q1, q1]));
        Assert.Equal(payload.GetHashCode(), new AstronomyObservationConditionsPayload(new("typed.observation.conditions.v1"), Context(), payload.Conditions, [q2, q1]).GetHashCode());
    }

    private static AstronomyObservationalQuantityId QuantityId() => new("observational.apparent-magnitude");
    private static AstronomyMeasurementUnit Unit(AstronomyMeasurementDimension dimension) => new($"unit.{dimension.ToString().ToLowerInvariant()}", "u", dimension);
    private static AstronomyMeasurement Magnitude() => new(1.2m, Unit(AstronomyMeasurementDimension.Magnitude));
    private static AstronomyMeasurement Angle(decimal value) => new(value, Unit(AstronomyMeasurementDimension.Angle));
    private static DateTimeOffset Utc(int y, int m, int d, int h) => new(y, m, d, h, 0, 0, TimeSpan.Zero);
    private static AstronomyObservationContext Context() => new("site.alpha", Utc(2026, 7, 20, 0), coordinateSystem: AstronomyCoordinateSystem.Horizontal);
}
