using System.Reflection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class TypedKnowledgeDomainFoundationTests
{
    [Fact]
    public void DomainTaxonomyHasExactStableValues()
    {
        Assert.Equal(new[] { "Classification", "Physical", "Orbital", "Positional", "Observational", "Event", "Temporal", "Catalog", "Derived" }, Enum.GetNames<AstronomyKnowledgeDomain>());
        Assert.Equal(Enum.GetValues<AstronomyKnowledgeDomain>().Distinct().Count(), Enum.GetValues<AstronomyKnowledgeDomain>().Length);
        foreach (var domain in Enum.GetValues<AstronomyKnowledgeDomain>()) Assert.Equal(domain, TypedKnowledgeEnumGuard.RequireDefined(domain));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypedKnowledgeEnumGuard.RequireDefined((AstronomyKnowledgeDomain)999));
    }

    [Fact]
    public void PayloadFamilyTaxonomyHasExactStableValues()
    {
        Assert.Equal(new[] { "EntityClassification", "PhysicalProperty", "OrbitalParameter", "SpatialPosition", "ObservationCondition", "VisibilityWindow", "AstronomicalEvent", "TemporalCycle", "CatalogReference", "DerivedProperty" }, Enum.GetNames<AstronomyKnowledgePayloadFamily>());
        Assert.Equal(Enum.GetValues<AstronomyKnowledgePayloadFamily>().Distinct().Count(), Enum.GetValues<AstronomyKnowledgePayloadFamily>().Length);
        foreach (var family in Enum.GetValues<AstronomyKnowledgePayloadFamily>()) Assert.Equal(family, TypedKnowledgeEnumGuard.RequireDefined(family));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypedKnowledgeEnumGuard.RequireDefined((AstronomyKnowledgePayloadFamily)999));
    }

    [Fact]
    public void TypedPayloadMarkerExtendsFrozenPayloadMarkerOnlyWithDomainAndFamily()
    {
        var payload = new SyntheticTypedPayload();
        Assert.IsAssignableFrom<IAstronomyKnowledgePayload>(payload);
        Assert.Equal(AstronomyKnowledgeDomain.Catalog, payload.Domain);
        Assert.Equal(AstronomyKnowledgePayloadFamily.CatalogReference, payload.Family);
        var propertyNames = typeof(ITypedAstronomyKnowledgePayload).GetProperties().Select(x => x.Name).ToArray();
        Assert.Equal(new[] { "Domain", "Family" }, propertyNames);
        Assert.DoesNotContain(propertyNames, x => x.Contains("Evidence", StringComparison.OrdinalIgnoreCase) || x.Contains("Confidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void KnowledgeTypeIdEnforcesStableTokenRules()
    {
        var id = new AstronomyKnowledgeTypeId(" Classification.Entity ");
        Assert.Equal("classification.entity", id.Value);
        Assert.Equal("classification.entity", id.ToString());
        Assert.True(id.IsValid);
        Assert.Equal(id, new AstronomyKnowledgeTypeId("classification.entity"));
        Assert.False(default(AstronomyKnowledgeTypeId).IsValid);
        Assert.Equal(string.Empty, default(AstronomyKnowledgeTypeId).ToString());
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeTypeId(" "));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeTypeId("catalog reference"));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeTypeId("catalog\nreference"));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeTypeId(new string('a', 129)));
    }

    [Fact]
    public void MeasurementDimensionTaxonomyHasExactStableValues()
    {
        Assert.Equal(new[] { "Dimensionless", "Angle", "AngularRate", "Distance", "Area", "Volume", "Mass", "Time", "Temperature", "Velocity", "Acceleration", "Luminosity", "Flux", "Frequency", "Wavelength", "Magnitude", "Percentage" }, Enum.GetNames<AstronomyMeasurementDimension>());
        foreach (var dimension in Enum.GetValues<AstronomyMeasurementDimension>()) Assert.Equal(dimension, TypedKnowledgeEnumGuard.RequireDefined(dimension));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypedKnowledgeEnumGuard.RequireDefined((AstronomyMeasurementDimension)999));
    }

    [Fact]
    public void UnitReferenceNormalizesCodePreservesSymbolAndEnforcesLocalInvariants()
    {
        var km = new AstronomyMeasurementUnit(" Kilometre ", "KM", AstronomyMeasurementDimension.Distance, " kilometre ");
        var degree = new AstronomyMeasurementUnit("degree", "°", AstronomyMeasurementDimension.Angle);
        Assert.Equal("kilometre", km.Code);
        Assert.Equal("KM", km.Symbol);
        Assert.Equal("kilometre", km.DisplayName);
        Assert.Equal(AstronomyMeasurementDimension.Angle, degree.Dimension);
        Assert.Equal(km, new AstronomyMeasurementUnit("kilometre", "KM", AstronomyMeasurementDimension.Distance, "kilometre"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyMeasurementUnit("x", "x", (AstronomyMeasurementDimension)999));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementUnit(" ", "km", AstronomyMeasurementDimension.Distance));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementUnit("km", " ", AstronomyMeasurementDimension.Distance));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementUnit("k\nm", "km", AstronomyMeasurementDimension.Distance));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementUnit(new string('a', 97), "km", AstronomyMeasurementDimension.Distance));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementUnit("km", new string('a', 33), AstronomyMeasurementDimension.Distance));
    }

    [Fact]
    public void MeasurementValuePreservesValuesWithoutConversionOrRounding()
    {
        var unit = new AstronomyMeasurementUnit("astronomical-unit", "AU", AstronomyMeasurementDimension.Distance);
        Assert.Equal(42.123456789m, new AstronomyMeasurement(42.123456789m, unit).Value);
        Assert.Equal(0m, new AstronomyMeasurement(0m, unit).Value);
        Assert.Equal(-1.25m, new AstronomyMeasurement(-1.25m, unit).Value);
        Assert.Equal(decimal.MaxValue, new AstronomyMeasurement(decimal.MaxValue, unit).Value);
        Assert.Equal(new AstronomyMeasurement(1m, unit), new AstronomyMeasurement(1m, unit));
        Assert.Throws<ArgumentNullException>(() => new AstronomyMeasurement(1m, null!));
    }

    [Fact]
    public void PrecisionIsDescriptiveBoundedAndDoesNotMutateMeasurementValue()
    {
        foreach (var kind in Enum.GetValues<AstronomyPrecisionKind>()) Assert.Equal(kind, TypedKnowledgeEnumGuard.RequireDefined(kind));
        var decimalPlaces = new AstronomyMeasurementPrecision(AstronomyPrecisionKind.DecimalPlaces, 0);
        var sigFigs = new AstronomyMeasurementPrecision(AstronomyPrecisionKind.SignificantFigures, AstronomyMeasurementPrecision.MaxDigits);
        Assert.Equal(decimalPlaces, new AstronomyMeasurementPrecision(AstronomyPrecisionKind.DecimalPlaces, 0));
        Assert.Equal(12.3456m, new AstronomyMeasurement(12.3456m, new("degree", "deg", AstronomyMeasurementDimension.Angle), sigFigs).Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyMeasurementPrecision(AstronomyPrecisionKind.DecimalPlaces, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyMeasurementPrecision(AstronomyPrecisionKind.DecimalPlaces, AstronomyMeasurementPrecision.MaxDigits + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyMeasurementPrecision((AstronomyPrecisionKind)999, 1));
    }

    [Fact]
    public void UncertaintySupportsMinimalKindsWithoutKnowledgeConfidenceDependency()
    {
        foreach (var kind in Enum.GetValues<AstronomyUncertaintyKind>()) Assert.Equal(kind, TypedKnowledgeEnumGuard.RequireDefined(kind));
        Assert.Equal(new AstronomyMeasurementUncertainty(AstronomyUncertaintyKind.SymmetricAbsolute, 1m, 1m), AstronomyMeasurementUncertainty.SymmetricAbsolute(1m));
        Assert.Equal(2m, new AstronomyMeasurementUncertainty(AstronomyUncertaintyKind.AsymmetricAbsolute, 1m, 2m).UpperValue);
        Assert.Equal(50m, new AstronomyMeasurementUncertainty(AstronomyUncertaintyKind.RelativePercentage, 5m, 50m).UpperValue);
        Assert.Equal(new AstronomyMeasurementUncertainty(AstronomyUncertaintyKind.StandardDeviation, 3m, 3m), AstronomyMeasurementUncertainty.StandardDeviation(3m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyMeasurementUncertainty(AstronomyUncertaintyKind.AsymmetricAbsolute, -1m, 1m));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementUncertainty(AstronomyUncertaintyKind.SymmetricAbsolute, 1m, 2m));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementUncertainty(AstronomyUncertaintyKind.StandardDeviation, 1m, 2m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyMeasurementUncertainty(AstronomyUncertaintyKind.RelativePercentage, 0m, 101m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyMeasurementUncertainty((AstronomyUncertaintyKind)999, 0m, 0m));
        Assert.DoesNotContain(typeof(AstronomyMeasurementUncertainty).GetProperties().Select(p => p.PropertyType.Name), n => n.Contains("Confidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReferenceFrameAndCoordinateSystemTaxonomiesAreStable()
    {
        Assert.Equal(new[] { "Unspecified", "ICRS", "FK5", "FK4", "BodyFixed" }, Enum.GetNames<AstronomyReferenceFrame>());
        Assert.Equal(new[] { "Unspecified", "Barycentric", "Heliocentric", "Geocentric", "Topocentric", "BodyCentric" }, Enum.GetNames<AstronomyReferenceOrigin>());
        Assert.Equal(new[] { "Equatorial", "Ecliptic", "Galactic", "Supergalactic", "Horizontal", "Cartesian", "Spherical", "Geographic", "BodyFixed" }, Enum.GetNames<AstronomyCoordinateSystem>());
        foreach (var frame in Enum.GetValues<AstronomyReferenceFrame>()) Assert.Equal(frame, TypedKnowledgeEnumGuard.RequireDefined(frame));
        foreach (var origin in Enum.GetValues<AstronomyReferenceOrigin>()) Assert.Equal(origin, TypedKnowledgeEnumGuard.RequireDefined(origin));
        foreach (var system in Enum.GetValues<AstronomyCoordinateSystem>()) Assert.Equal(system, TypedKnowledgeEnumGuard.RequireDefined(system));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypedKnowledgeEnumGuard.RequireDefined((AstronomyReferenceFrame)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypedKnowledgeEnumGuard.RequireDefined((AstronomyReferenceOrigin)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypedKnowledgeEnumGuard.RequireDefined((AstronomyCoordinateSystem)999));
    }

    [Fact]
    public void EpochReferencePreservesUtcWithoutImplicitDefaultsOrConversions()
    {
        foreach (var kind in Enum.GetValues<AstronomyEpochKind>()) Assert.Equal(kind, TypedKnowledgeEnumGuard.RequireDefined(kind));
        Assert.Equal(AstronomyEpochKind.Unspecified, AstronomyEpochReference.Unspecified.Kind);
        Assert.Equal(AstronomyEpochKind.J2000, AstronomyEpochReference.J2000.Kind);
        Assert.Equal(AstronomyEpochKind.B1950, AstronomyEpochReference.B1950.Kind);
        Assert.Equal(AstronomyEpochKind.ObservationTime, AstronomyEpochReference.ObservationTime.Kind);
        var custom = AstronomyEpochReference.Custom(DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"), custom.InstantUtc);
        Assert.Equal(custom, AstronomyEpochReference.Custom(DateTimeOffset.Parse("2026-01-01T00:00:00+00:00")));
        Assert.Throws<ArgumentException>(() => AstronomyEpochReference.Custom(DateTimeOffset.Parse("2026-01-01T00:00:00+05:30")));
        Assert.Throws<ArgumentException>(() => new AstronomyEpochReference(AstronomyEpochKind.Custom));
        Assert.Throws<ArgumentException>(() => new AstronomyEpochReference(AstronomyEpochKind.J2000, DateTimeOffset.Parse("2026-01-01T00:00:00+00:00")));
    }

    [Fact]
    public void ObservationContextCapturesOnlyExplicitContextAndReusesLocationReference()
    {
        var time = DateTimeOffset.Parse("2026-07-19T00:00:00+00:00");
        var context = new AstronomyObservationContext("IN-RJ-UDAIPUR", time, AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin.Topocentric, AstronomyCoordinateSystem.Horizontal, 600m);
        Assert.Equal("IN-RJ-UDAIPUR", context.ObserverLocationReference);
        Assert.Equal(time, context.ObservationTimeUtc);
        Assert.Equal(AstronomyReferenceFrame.ICRS, context.ReferenceFrame);
        Assert.Equal(AstronomyReferenceOrigin.Topocentric, context.ReferenceOrigin);
        Assert.Equal(AstronomyCoordinateSystem.Horizontal, context.CoordinateSystem);
        Assert.Equal(600m, context.AltitudeMetres);
        Assert.Equal(context, new AstronomyObservationContext("IN-RJ-UDAIPUR", time, AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin.Topocentric, AstronomyCoordinateSystem.Horizontal, 600m));
        Assert.Throws<ArgumentException>(() => new AstronomyObservationContext(" ", time));
        Assert.Throws<ArgumentException>(() => new AstronomyObservationContext("site", DateTimeOffset.Parse("2026-07-19T00:00:00+05:30")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyObservationContext("site", time, (AstronomyReferenceFrame)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyObservationContext("site", time, referenceOrigin: (AstronomyReferenceOrigin)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyObservationContext("site", time, coordinateSystem: (AstronomyCoordinateSystem)999));
    }

    [Fact]
    public void PublicValueObjectsExposeNoPublicSettersOrMutableCollections()
    {
        var types = new[] { typeof(AstronomyKnowledgeTypeId), typeof(AstronomyMeasurementUnit), typeof(AstronomyMeasurement), typeof(AstronomyMeasurementPrecision), typeof(AstronomyMeasurementUncertainty), typeof(AstronomyEpochReference), typeof(AstronomyObservationContext) };
        foreach (var type in types)
        {
            Assert.Empty(type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.SetMethod is { IsPublic: true }));
            Assert.Empty(type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string)));
        }
    }

    [Fact]
    public void Task23AProductionBoundaryHasNoForbiddenDependenciesOrBehaviors()
    {
        var forbidden = new[] { "EvidenceId", "ConfidenceAssessmentId", "KnowledgeConfidenceLevel", "JsonConverter", "JsonSerializerOptions", "IServiceCollection", "DbContext", "IQueryable", "HttpClient", "Stellarium", "Skyfield", "SPICE", "NASA API", "DateTimeOffset.UtcNow", "CertificationCoordinator", "ConvertTo", "Calculate", "Compute", "Infrastructure", "Persistence", "EntityFrameworkCore", "Publishing", "Rendering", "AIOptimization", "ContentGen", "Json", "Serialize", "ServiceCollection" };
        var root = FindTypedDomainRoot();
        Assert.True(Directory.Exists(root), root);
        var content = string.Join('\n', Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        foreach (var term in forbidden)
            Assert.DoesNotContain(term, content, StringComparison.Ordinal);
    }

    private static string FindTypedDomainRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "TypedDomains");
            if (Directory.Exists(candidate)) return candidate;
            candidate = Path.Combine(directory.FullName, "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "TypedDomains");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "TypedDomainsNotFound");
    }

    private sealed record SyntheticTypedPayload : ITypedAstronomyKnowledgePayload
    {
        public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Catalog;
        public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.CatalogReference;
    }
}
