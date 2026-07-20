using System.Text.Json;
using Astronomy.MediaFactory.Core.AstronomyDomain.Families;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedKnowledgeSerializationTests
{
    [Fact]
    public void RegisteredPayloads_RoundTripThroughTypedPayloadEnvelope()
    {
        var options = CreateOptions();
        RoundTrip(ClassificationPayload(), options);
        var physical = RoundTrip(PhysicalPayload(), options);
        physical.Properties.Single(p => p.PropertyId.Value == "physical.radius.mean").Value.Should().BeOfType<AstronomyScalarPhysicalPropertyValue>();
        physical.Properties.Single(p => p.PropertyId.Value == "physical.radius.range").Value.Should().BeOfType<AstronomyRangePhysicalPropertyValue>();
        physical.Properties.Single(p => p.PropertyId.Value == "physical.atmosphere.summary").Value.Should().BeOfType<AstronomyTextPhysicalPropertyValue>();
        physical.Properties.Single(p => p.PropertyId.Value == "physical.has-rings").Value.Should().BeOfType<AstronomyBooleanPhysicalPropertyValue>();
        RoundTrip(KeplerianPayload(), options);
        RoundTrip(OrbitalParametersPayload(), options);
        RoundTrip(SpatialPayload(), options);
        RoundTrip(ObservationConditionsPayload(), options);
        RoundTrip(VisibilityPayload(), options);
        RoundTrip(EventPayload(), options);
        RoundTrip(TemporalPayload(), options);
    }

    [Fact]
    public void PhysicalPropertyValueHierarchy_RoundTripsThroughBaseConverter()
    {
        var options = CreateOptions();
        AssertHierarchy(new AstronomyScalarPhysicalPropertyValue(Measure(1.23m, "km", "km", AstronomyMeasurementDimension.Distance)), AstronomyPhysicalPropertyValueKind.ScalarMeasurement, options);
        AssertHierarchy(new AstronomyRangePhysicalPropertyValue(new AstronomyMeasurementRange(Measure(1m, "km", "km", AstronomyMeasurementDimension.Distance), Measure(2m, "km", "km", AstronomyMeasurementDimension.Distance))), AstronomyPhysicalPropertyValueKind.MeasurementRange, options);
        AssertHierarchy(new AstronomyTextPhysicalPropertyValue("silicate rich"), AstronomyPhysicalPropertyValueKind.Text, options);
        AssertHierarchy(new AstronomyBooleanPhysicalPropertyValue(true), AstronomyPhysicalPropertyValueKind.Boolean, options);
    }

    [Fact]
    public void PositionValueHierarchy_RoundTripsThroughBaseConverter()
    {
        var options = CreateOptions();
        RoundTripHierarchy<AstronomyPositionValue>(AngularPosition(), options).Should().BeOfType<AstronomyAngularPositionValue>();
        RoundTripHierarchy<AstronomyPositionValue>(SphericalPosition(), options).Should().BeOfType<AstronomySphericalPositionValue>();
        RoundTripHierarchy<AstronomyPositionValue>(new AstronomyCartesianPositionValue(new AstronomyCartesianCoordinate(Measure(1m, "au", "AU", AstronomyMeasurementDimension.Distance), Measure(2m, "au", "AU", AstronomyMeasurementDimension.Distance), Measure(3m, "au", "AU", AstronomyMeasurementDimension.Distance))), options).Should().BeOfType<AstronomyCartesianPositionValue>();
    }

    [Fact]
    public void EventTemporalExtentHierarchy_RoundTripsThroughBaseConverterAndPreservesUtcKind()
    {
        var options = CreateOptions();
        var extents = new AstronomyEventTemporalExtent[]
        {
            new AstronomyInstantEventTemporalExtent(Utc("2026-08-01T00:00:00Z")),
            new AstronomyInstantEventTemporalExtent(Utc("2026-08-01T00:01:00Z"), true),
            new AstronomyIntervalEventTemporalExtent(Utc("2026-08-01T00:00:00Z"), Utc("2026-08-01T01:00:00Z")),
            new AstronomyIntervalEventTemporalExtent(Utc("2026-08-02T00:00:00Z"), Utc("2026-08-02T01:00:00Z"), true)
        };
        foreach (var original in extents)
        {
            var result = RoundTripHierarchy<AstronomyEventTemporalExtent>(original, options);
            result.GetType().Should().Be(original.GetType());
            result.Kind.Should().Be(original.Kind);
            switch (result)
            {
                case AstronomyInstantEventTemporalExtent instant: instant.InstantUtc.Offset.Should().Be(TimeSpan.Zero); break;
                case AstronomyIntervalEventTemporalExtent interval: interval.StartUtc.Offset.Should().Be(TimeSpan.Zero); interval.EndUtc.Offset.Should().Be(TimeSpan.Zero); break;
            }
        }
    }

    [Fact]
    public void TemporalAnchorHierarchy_RoundTripsThroughBaseConverter()
    {
        var options = CreateOptions();
        var anchors = new AstronomyTemporalAnchor[]
        {
            new AstronomyUtcTemporalAnchor(Utc("2026-01-01T00:00:00Z")),
            new AstronomyEpochTemporalAnchor(AstronomyEpochReference.J2000),
            new AstronomyCalendarDateTemporalAnchor(3, 20),
            new AstronomyDayOfYearTemporalAnchor(80),
            new AstronomyMonthTemporalAnchor(7),
            new AstronomyCustomTemporalAnchor("custom.anchor", "custom note")
        };
        foreach (var original in anchors)
        {
            var result = RoundTripHierarchy<AstronomyTemporalAnchor>(original, options);
            result.GetType().Should().Be(original.GetType());
            result.Kind.Should().Be(original.Kind);
        }
    }

    [Fact]
    public void Measurements_RoundTripPrecisionAndEveryUncertaintyRepresentation()
    {
        var options = CreateOptions();
        var uncertainties = new AstronomyMeasurementUncertainty?[]
        {
            null,
            AstronomyMeasurementUncertainty.SymmetricAbsolute(0.1m),
            new(AstronomyUncertaintyKind.AsymmetricAbsolute, 0.1m, 0.2m),
            new(AstronomyUncertaintyKind.RelativePercentage, 1.5m, 2.5m),
            AstronomyMeasurementUncertainty.StandardDeviation(0.3m)
        };
        foreach (var uncertainty in uncertainties)
        {
            var original = new AstronomyMeasurement(123.456m, new AstronomyMeasurementUnit("unit.test", "u", AstronomyMeasurementDimension.Distance, "Unit Test"), new AstronomyMeasurementPrecision(AstronomyPrecisionKind.DecimalPlaces, 3), uncertainty);
            JsonSerializer.Deserialize<AstronomyMeasurement>(JsonSerializer.Serialize(original, options), options).Should().Be(original);
        }
    }

    [Fact]
    public void StrongIds_SerializeAsJsonStrings()
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize<ITypedAstronomyKnowledgePayload>(EventPayload(), options);
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement.GetProperty("value");
        value.GetProperty("typeId").ValueKind.Should().Be(JsonValueKind.String);
        var ev = value.GetProperty("event");
        ev.GetProperty("eventId").ValueKind.Should().Be(JsonValueKind.String);
        ev.GetProperty("geometry")[0].GetProperty("quantityId").ValueKind.Should().Be(JsonValueKind.String);
        ev.GetProperty("circumstances")[0].GetProperty("circumstanceId").ValueKind.Should().Be(JsonValueKind.String);

        json = JsonSerializer.Serialize<ITypedAstronomyKnowledgePayload>(TemporalPayload(), options);
        using var temporal = JsonDocument.Parse(json);
        var pattern = temporal.RootElement.GetProperty("value").GetProperty("pattern");
        pattern.GetProperty("patternId").ValueKind.Should().Be(JsonValueKind.String);
        pattern.GetProperty("phases")[0].GetProperty("phaseId").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public void Enums_SerializeAsCamelCaseStringsAndRejectNumbersAndUnknownStrings()
    {
        var options = CreateOptions();
        var eventJson = JsonSerializer.Serialize<ITypedAstronomyKnowledgePayload>(EventPayload(), options);
        eventJson.Should().Contain("\"kind\":\"conjunction\"");
        AssertBadJson(eventJson.Replace("\"kind\":\"conjunction\"", "\"kind\":999"), options);
        AssertBadJson(eventJson.Replace("\"kind\":\"conjunction\"", "\"kind\":\"notARealEventKind\""), options);

        var visibilityJson = JsonSerializer.Serialize<ITypedAstronomyKnowledgePayload>(VisibilityPayload(), options);
        visibilityJson.Should().Contain("\"status\":\"visible\"");
        AssertBadJson(visibilityJson.Replace("\"status\":\"visible\"", "\"status\":999"), options);
        AssertBadJson(visibilityJson.Replace("\"status\":\"visible\"", "\"status\":\"notARealVisibilityStatus\""), options);

        var temporalJson = JsonSerializer.Serialize<ITypedAstronomyKnowledgePayload>(TemporalPayload(), options);
        temporalJson.Should().Contain("\"kind\":\"periodic\"");
        AssertBadJson(temporalJson.Replace("\"kind\":\"periodic\"", "\"kind\":999"), options);
        AssertBadJson(temporalJson.Replace("\"kind\":\"periodic\"", "\"kind\":\"notARealTemporalPatternKind\""), options);
    }

    [Fact]
    public void TypedPayloadEnvelope_UsesOnlyPublicTypeAndValueProperties()
    {
        var options = CreateOptions();
        var json = JsonSerializer.Serialize<ITypedAstronomyKnowledgePayload>(ClassificationPayload(), options);
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("type", "value");
        json.Should().NotContain("$type");
        json.Should().NotContain("Astronomy.MediaFactory");
        json.Should().NotContain(nameof(AstronomyEntityClassificationPayload));
    }

    [Theory]
    [MemberData(nameof(MalformedHierarchyCases))]
    public void ClosedHierarchyConverters_RejectMalformedEnvelopeJson(Type baseType, string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, baseType, CreateOptions()));
    }

    [Fact]
    public void TypedPayloadConverter_RejectsDomainAndFamilyMismatches()
    {
        var registry = new AstronomyTypedPayloadRegistry([new AstronomyTypedPayloadDescriptor("typed.test.invalid.v1", typeof(InvalidTypedPayload), AstronomyKnowledgeDomain.Physical, AstronomyKnowledgePayloadFamily.PhysicalProperty)]);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddAstronomyTypedKnowledgeJson(registry);
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<ITypedAstronomyKnowledgePayload>(new InvalidTypedPayload(), options));
    }

    [Fact]
    public void GenericTypedKnowledgeStatements_RoundTripTypedPayloads()
    {
        var options = CreateOptions();
        AssertStatementRoundTrip(PhysicalPayload(), options);
        AssertStatementRoundTrip(EventPayload(), options);
        AssertStatementRoundTrip(TemporalPayload(), options);
    }

    [Fact]
    public void BuiltInPayloadMatrix_HasSerializationCoverageForEveryDescriptor()
    {
        var testedPayloadTypes = new[] { typeof(AstronomyEntityClassificationPayload), typeof(AstronomyPhysicalPropertiesPayload), typeof(AstronomyKeplerianElementsPayload), typeof(AstronomyOrbitalParametersPayload), typeof(AstronomySpatialPositionPayload), typeof(AstronomyObservationConditionsPayload), typeof(AstronomyVisibilityWindowsPayload), typeof(AstronomyEventPayload), typeof(AstronomyTemporalPatternPayload) };
        AstronomyBuiltInTypedPayloadDescriptors.BuiltIn.Select(d => d.PayloadType).OrderBy(t => t.FullName).Should().Equal(testedPayloadTypes.OrderBy(t => t.FullName));
    }

    public static IEnumerable<object[]> MalformedHierarchyCases()
    {
        var bases = new[] { typeof(AstronomyPhysicalPropertyValue), typeof(AstronomyPositionValue), typeof(AstronomyEventTemporalExtent), typeof(AstronomyTemporalAnchor) };
        var cases = new[] { "null", "[]", "{}", "{\"value\":{}}", "{\"type\":\"\",\"value\":{}}", "{\"type\":\"unknown\",\"value\":{}}", "{\"type\":\"scalar\"}", "{\"type\":\"scalar\",\"value\":null}", "{\"type\":\"scalar\",\"type\":\"scalar\",\"value\":{}}", "{\"type\":\"scalar\",\"value\":{},\"value\":{}}" };
        foreach (var b in bases) foreach (var c in cases) yield return [b, c];
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var registry = new AstronomyTypedPayloadRegistry(AstronomyBuiltInTypedPayloadDescriptors.BuiltIn);
        return new JsonSerializerOptions(JsonSerializerDefaults.Web).AddAstronomyTypedKnowledgeJson(registry);
    }

    private static TPayload RoundTrip<TPayload>(TPayload original, JsonSerializerOptions options) where TPayload : ITypedAstronomyKnowledgePayload
    {
        var json = JsonSerializer.Serialize<ITypedAstronomyKnowledgePayload>(original, options);
        var result = JsonSerializer.Deserialize<ITypedAstronomyKnowledgePayload>(json, options);
        Assert.NotNull(result);
        var typedResult = Assert.IsType<TPayload>(result);
        Assert.Equal(original, typedResult);
        Assert.Equal(original.GetHashCode(), typedResult.GetHashCode());
        return typedResult;
    }

    private static TBase RoundTripHierarchy<TBase>(TBase original, JsonSerializerOptions options)
    {
        var json = JsonSerializer.Serialize(original, typeof(TBase), options);
        var result = JsonSerializer.Deserialize(json, typeof(TBase), options);
        Assert.NotNull(result);
        var typedResult = Assert.IsAssignableFrom<TBase>(result);
        Assert.Equal(original, typedResult);
        return typedResult;
    }

    private static void AssertHierarchy<T>(T original, AstronomyPhysicalPropertyValueKind kind, JsonSerializerOptions options) where T : AstronomyPhysicalPropertyValue
    {
        var result = RoundTripHierarchy<AstronomyPhysicalPropertyValue>(original, options);
        result.Should().BeOfType<T>();
        result.Kind.Should().Be(kind);
    }

    private static void AssertBadJson(string json, JsonSerializerOptions options) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ITypedAstronomyKnowledgePayload>(json, options));

    private static void AssertStatementRoundTrip<TPayload>(TPayload payload, JsonSerializerOptions options) where TPayload : ITypedAstronomyKnowledgePayload
    {
        var original = new AstronomyKnowledgeStatement<ITypedAstronomyKnowledgePayload>(new KnowledgeId($"knowledge.{payload.TypeId.Value}"), new KnowledgeVersion(2), KnowledgeStatementKind.Scientific, KnowledgeFoundationStatus.Reviewed, new AstronomyEntityReference("body.mars", AstronomyEntityKind.Planet, "Mars"), payload, new KnowledgeAuditMetadata(Utc("2026-01-01T00:00:00Z"), "author", Utc("2026-01-02T00:00:00Z"), "reviewer"), new AstronomyFamilyReference("solar-system", AstronomyFamilyKind.PlanetarySystem), [new(new KnowledgeLanguageTag("en-US"), "resource.key", false, true)], [new KnowledgeTag("mars"), new KnowledgeTag("typed")], new KnowledgeValidityRange(Utc("2026-01-01T00:00:00Z"), Utc("2026-12-31T00:00:00Z")));
        var result = JsonSerializer.Deserialize<AstronomyKnowledgeStatement<ITypedAstronomyKnowledgePayload>>(JsonSerializer.Serialize(original, options), options)!;
        result.Id.Should().Be(original.Id); result.Version.Should().Be(original.Version); result.Kind.Should().Be(original.Kind); result.Status.Should().Be(original.Status); result.Validity.Should().Be(original.Validity); result.Audit.Should().Be(original.Audit); result.Tags.Should().Equal(original.Tags); result.LocalizationReferences.Should().Equal(original.LocalizationReferences); result.Payload.Should().BeOfType(payload.GetType()); result.Payload.Should().Be(payload);
    }

    private static AstronomyEntityClassificationPayload ClassificationPayload() => new(new("typed.classification.entity.v1"), AstronomyEntityKind.Planet, [new(new("taxonomy.iau.body"), new("planet", "Planet", "IAU planet class"), AstronomyClassificationQualifier.Primary, "Primary scheme assignment")]);

    private static AstronomyPhysicalPropertiesPayload PhysicalPayload() => new(new("typed.physical.properties.v1"), [
        new(new("physical.radius.mean"), AstronomyPhysicalPropertyCategory.Size, new AstronomyScalarPhysicalPropertyValue(Measure(3389.5m, "km", "km", AstronomyMeasurementDimension.Distance)), AstronomyPhysicalPropertyQualifier.Mean, "Mean radius"),
        new(new("physical.radius.range"), AstronomyPhysicalPropertyCategory.Size, new AstronomyRangePhysicalPropertyValue(new AstronomyMeasurementRange(Measure(3376.2m, "km", "km", AstronomyMeasurementDimension.Distance), Measure(3396.2m, "km", "km", AstronomyMeasurementDimension.Distance))), AstronomyPhysicalPropertyQualifier.Observed, "Observed radius range"),
        new(new("physical.atmosphere.summary"), AstronomyPhysicalPropertyCategory.Atmospheric, new AstronomyTextPhysicalPropertyValue("Thin carbon dioxide atmosphere"), null, "Composition summary"),
        new(new("physical.has-rings"), AstronomyPhysicalPropertyCategory.Structural, new AstronomyBooleanPhysicalPropertyValue(false), null, "Ring flag")]);

    private static AstronomyKeplerianElementsPayload KeplerianPayload() => new(new("typed.orbital.keplerian-elements.v1"), OrbitalContext(), [new(AstronomyKeplerianElementType.SemiMajorAxis, Measure(1.523679m, "au", "AU", AstronomyMeasurementDimension.Distance)), new(AstronomyKeplerianElementType.Eccentricity, Measure(0.0934m, "one", "1", AstronomyMeasurementDimension.Dimensionless)), new(AstronomyKeplerianElementType.Inclination, Measure(1.85m, "deg", "°", AstronomyMeasurementDimension.Angle))]);
    private static AstronomyOrbitalParametersPayload OrbitalParametersPayload() => new(new("typed.orbital.parameters.v1"), OrbitalContext(), [new(new("orbital.period.sidereal"), AstronomyOrbitalParameterCategory.Period, Measure(686.98m, "day", "d", AstronomyMeasurementDimension.Time), AstronomyOrbitalParameterQualifier.Mean, AstronomyEpochReference.J2000, "Sidereal period"), new(new("orbital.distance.current"), AstronomyOrbitalParameterCategory.Distance, Measure(1.6m, "au", "AU", AstronomyMeasurementDimension.Distance), AstronomyOrbitalParameterQualifier.Instantaneous, AstronomyEpochReference.Custom(Utc("2026-01-01T00:00:00Z")), "Current distance")]);
    private static AstronomySpatialPositionPayload SpatialPayload() => new(new("typed.positional.spatial-position.v1"), new(new(AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin.Barycentric, AstronomyCoordinateSystem.Equatorial, AstronomyEpochReference.J2000, new("sun", AstronomyEntityKind.Star, "Sun")), AngularPosition(), "J2000 equatorial position"));
    private static AstronomyObservationConditionsPayload ObservationConditionsPayload() => new(new("typed.observational.conditions.v1"), ObservationContext(), new(AstronomySkyConditionKind.Clear, AstronomySeeingQuality.Good, AstronomyTransparencyQuality.VeryGood, Measure(-1.2m, "mag", "mag", AstronomyMeasurementDimension.Magnitude), null, "Clear winter night"), [new(new("obs.quantity.altitude"), AstronomyObservationalQuantityCategory.HorizontalPosition, Measure(42m, "deg", "°", AstronomyMeasurementDimension.Angle), AstronomyObservationalQuantityQualifier.Observed, AstronomyEpochReference.ObservationTime, "Altitude")], new(Az(120m), Alt(42m)), AstronomyHorizonSector.SouthEast);
    private static AstronomyVisibilityWindowsPayload VisibilityPayload() => new(new("typed.observational.visibility-windows.v1"), ObservationContext(), [new(new(Utc("2026-08-01T02:00:00Z"), Utc("2026-08-01T04:00:00Z")), new(AstronomyVisibilityStatus.Visible, AstronomyVisibilityMethod.NakedEye, [AstronomyVisibilityLimitation.Twilight, AstronomyVisibilityLimitation.Moonlight], "Visible"), Utc("2026-08-01T03:00:00Z"), Measure(35m, "deg", "°", AstronomyMeasurementDimension.Angle), "First"), new(new(Utc("2026-08-02T02:00:00Z"), Utc("2026-08-02T04:00:00Z")), new(AstronomyVisibilityStatus.Marginal, AstronomyVisibilityMethod.Binocular, [AstronomyVisibilityLimitation.LowTransparency, AstronomyVisibilityLimitation.TerrainObstruction], "Marginal"), Utc("2026-08-02T03:00:00Z"), Measure(25m, "deg", "°", AstronomyMeasurementDimension.Angle), "Second")]);
    private static AstronomyEventPayload EventPayload() => new(new("typed.event.astronomical.v1"), new(new("event.jupiter.venus.conjunction"), AstronomyEventKind.Conjunction, new AstronomyIntervalEventTemporalExtent(Utc("2026-08-01T00:00:00Z"), Utc("2026-08-01T02:00:00Z"), true), new(AstronomyEventScope.ReferenceFrameSpecific, AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin.Geocentric, AstronomyCoordinateSystem.Equatorial), [new(new("body.jupiter", AstronomyEntityKind.Planet, "Jupiter"), AstronomyEventParticipantRole.Primary, "Jupiter"), new(new("body.venus", AstronomyEntityKind.Planet, "Venus"), AstronomyEventParticipantRole.Secondary, "Venus")], [new(AstronomyEventPhaseMarkerKind.Start, Utc("2026-08-01T00:00:00Z"), "Start"), new(AstronomyEventPhaseMarkerKind.Peak, Utc("2026-08-01T01:00:00Z"), "Peak")], [new(new("geometry.angular-separation"), AstronomyEventGeometryCategory.AngularSeparation, Measure(0.5m, "deg", "°", AstronomyMeasurementDimension.Angle), AstronomyEpochReference.Custom(Utc("2026-08-01T01:00:00Z")), "Minimum separation")], [new(new("circumstance.visibility"), "Dawn sky", "Best before sunrise")], AstronomyEventSignificance.Notable, "Jupiter Venus conjunction", "Close apparent approach."));
    private static AstronomyTemporalPatternPayload TemporalPayload() => new(new("typed.temporal.pattern.v1"), new(new("temporal.mars.synodic"), AstronomyTemporalPatternKind.Periodic, new(AstronomyTemporalReferenceBasis.Utc), new(AstronomyRecurrenceDescription(AstronomyRecurrenceKind.FixedPeriod, new AstronomyCyclePeriod(Measure(779.94m, "day", "d", AstronomyMeasurementDimension.Time), true), anchor: new AstronomyUtcTemporalAnchor(Utc("2026-01-01T00:00:00Z")), isApproximate: true, note: "Approximate recurrence"), new AstronomyCyclePeriod(Measure(779.94m, "day", "d", AstronomyMeasurementDimension.Time), true), [new(new("phase.opposition"), Measure(0m, "one", "1", AstronomyMeasurementDimension.Dimensionless), Measure(5m, "day", "d", AstronomyMeasurementDimension.Time), "Opposition", "Best visibility")], [new(Utc("2026-02-19T00:00:00Z"), Utc("2026-02-24T00:00:00Z"), new("phase.opposition"), true, "Supplied window")], new(new(1, 1), new(3, 31), false, "Winter season"), new(Utc("2026-01-01T00:00:00Z"), Utc("2027-01-01T00:00:00Z")), "Mars synodic cycle", "Mars opposition cadence."));

    private static AstronomyOrbitalReferenceContext OrbitalContext() => new(new("sun", AstronomyEntityKind.Star, "Sun"), AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin.Heliocentric, AstronomyEpochReference.J2000);
    private static AstronomyObservationContext ObservationContext() => new("observatory.test", Utc("2026-08-01T03:00:00Z"), AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin.Topocentric, AstronomyCoordinateSystem.Horizontal, 1234m);
    private static AstronomyAngularPositionValue AngularPosition() => new(new(AstronomyAngularCoordinateComponent.RightAscension, Measure(120m, "deg", "°", AstronomyMeasurementDimension.Angle)), new(AstronomyAngularCoordinateComponent.Declination, Measure(-20m, "deg", "°", AstronomyMeasurementDimension.Angle)));
    private static AstronomySphericalPositionValue SphericalPosition() => new(new(new(AstronomyAngularCoordinateComponent.Longitude, Measure(10m, "deg", "°", AstronomyMeasurementDimension.Angle)), new(AstronomyAngularCoordinateComponent.Latitude, Measure(20m, "deg", "°", AstronomyMeasurementDimension.Angle)), Measure(1m, "au", "AU", AstronomyMeasurementDimension.Distance)));
    private static AstronomyAngularCoordinateValue Az(decimal value) => new(AstronomyAngularCoordinateComponent.Azimuth, Measure(value, "deg", "°", AstronomyMeasurementDimension.Angle));
    private static AstronomyAngularCoordinateValue Alt(decimal value) => new(AstronomyAngularCoordinateComponent.Altitude, Measure(value, "deg", "°", AstronomyMeasurementDimension.Angle));
    private static AstronomyMeasurement Measure(decimal value, string code, string symbol, AstronomyMeasurementDimension dimension) => new(value, new AstronomyMeasurementUnit(code, symbol, dimension), new AstronomyMeasurementPrecision(AstronomyPrecisionKind.DecimalPlaces, 2), AstronomyMeasurementUncertainty.SymmetricAbsolute(0.01m));
    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private sealed record InvalidTypedPayload : ITypedAstronomyKnowledgePayload
    {
        public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Event;
        public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.TemporalCycle;
        public AstronomyKnowledgeTypeId TypeId { get; } = new("typed.test.invalid.v1");
    }
}
