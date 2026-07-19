using System.Reflection;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedPhysicalAndClassificationKnowledgeTests
{
    [Fact]
    public void Classification_contracts_normalize_and_guard_local_invariants()
    {
        var scheme = new AstronomyClassificationSchemeId(" Morgan-Keenan.Spectral ");
        var value = new AstronomyClassificationValue(" Main-Sequence-Star ", " Main-sequence star ", " Stable hydrogen burning ");
        var assignment = new AstronomyClassificationAssignment(scheme, value, AstronomyClassificationQualifier.Primary, " principal ");
        var payload = new AstronomyEntityClassificationPayload(new AstronomyKnowledgeTypeId("typed.classification.entity.v1"), AstronomyEntityKind.Star, [assignment]);

        Assert.Equal("morgan-keenan.spectral", scheme.ToString());
        Assert.Equal("main-sequence-star", value.Code);
        Assert.Equal("Main-sequence star", value.DisplayName);
        Assert.Equal("Stable hydrogen burning", value.Description);
        Assert.Equal("principal", assignment.Note);
        Assert.IsAssignableFrom<ITypedAstronomyKnowledgePayload>(payload);
        Assert.Equal(global::Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgeDomain.Classification, payload.Domain);
        Assert.Equal(AstronomyKnowledgePayloadFamily.EntityClassification, payload.Family);
        Assert.Equal(AstronomyEntityKind.Star, payload.SubjectKind);
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationSchemeId("bad token"));
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationValue("", "Name"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyClassificationAssignment(scheme, value, (AstronomyClassificationQualifier)99));
        Assert.Throws<ArgumentException>(() => new AstronomyEntityClassificationPayload(new AstronomyKnowledgeTypeId("typed.classification.entity.v1"), AstronomyEntityKind.Star, [assignment, assignment]));
    }

    [Fact]
    public void Classification_payload_orders_and_allows_primary_per_scheme()
    {
        var first = new AstronomyClassificationAssignment(new("z.scheme"), new("b", "B"), AstronomyClassificationQualifier.Secondary);
        var primaryA = new AstronomyClassificationAssignment(new("a.scheme"), new("a", "A"), AstronomyClassificationQualifier.Primary);
        var primaryB = new AstronomyClassificationAssignment(new("b.scheme"), new("a", "A"), AstronomyClassificationQualifier.Primary);
        var payload = new AstronomyEntityClassificationPayload(new("typed.classification.entity.v1"), AstronomyEntityKind.Galaxy, [first, primaryB, primaryA]);

        Assert.Equal(["a.scheme", "b.scheme", "z.scheme"], payload.Assignments.Select(a => a.SchemeId.Value).ToArray());
        Assert.Throws<ArgumentException>(() => new AstronomyEntityClassificationPayload(new("typed.classification.entity.v1"), AstronomyEntityKind.Galaxy, [primaryA, new AstronomyClassificationAssignment(new("a.scheme"), new("c", "C"), AstronomyClassificationQualifier.Primary)]));
        Assert.Equal(payload, new AstronomyEntityClassificationPayload(new("typed.classification.entity.v1"), AstronomyEntityKind.Galaxy, [primaryA, primaryB, first]));
        Assert.Equal(payload.GetHashCode(), new AstronomyEntityClassificationPayload(new("typed.classification.entity.v1"), AstronomyEntityKind.Galaxy, [primaryA, primaryB, first]).GetHashCode());
    }

    [Fact]
    public void Physical_contracts_reuse_measurements_and_guard_local_invariants()
    {
        var kilometer = new AstronomyMeasurementUnit("km", "km", AstronomyMeasurementDimension.Distance, "Kilometer");
        var min = new AstronomyMeasurement(1m, kilometer);
        var max = new AstronomyMeasurement(2m, kilometer);
        var range = new AstronomyMeasurementRange(min, max);
        var radius = new AstronomyPhysicalProperty(new(" physical.radius "), AstronomyPhysicalPropertyCategory.Size, new AstronomyRangePhysicalPropertyValue(range), AstronomyPhysicalPropertyQualifier.Mean, " observed descriptor ");
        var mass = new AstronomyPhysicalProperty(new("physical.mass"), AstronomyPhysicalPropertyCategory.Mass, new AstronomyScalarPhysicalPropertyValue(new AstronomyMeasurement(5m, new("kg", "kg", AstronomyMeasurementDimension.Mass))));
        var text = new AstronomyTextPhysicalPropertyValue(" Hydrogen rich ");
        var payload = new AstronomyPhysicalPropertiesPayload(new("typed.physical.properties.v1"), [radius, mass]);

        Assert.Equal("physical.radius", radius.PropertyId.Value);
        Assert.Equal("observed descriptor", radius.Note);
        Assert.Equal(AstronomyPhysicalPropertyValueKind.MeasurementRange, radius.Value.Kind);
        Assert.Equal("Hydrogen rich", text.Value);
        Assert.IsAssignableFrom<ITypedAstronomyKnowledgePayload>(payload);
        Assert.Equal(global::Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgeDomain.Physical, payload.Domain);
        Assert.Equal(AstronomyKnowledgePayloadFamily.PhysicalProperty, payload.Family);
        Assert.Equal([AstronomyPhysicalPropertyCategory.Size, AstronomyPhysicalPropertyCategory.Mass], payload.Properties.Select(p => p.Category).OrderBy(c => c).ToArray());
        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalPropertyId("bad token"));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementRange(max, min));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementRange(min, new AstronomyMeasurement(2m, new("m", "m", AstronomyMeasurementDimension.Distance))));
        Assert.Throws<ArgumentException>(() => new AstronomyTextPhysicalPropertyValue(" "));
        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalPropertiesPayload(new("typed.physical.properties.v1"), [mass, mass]));
    }

    [Fact]
    public void Physical_payload_allows_same_property_with_different_qualifiers_and_uses_value_equality()
    {
        var unit = new AstronomyMeasurementUnit("km", "km", AstronomyMeasurementDimension.Distance);
        var eq = new AstronomyPhysicalProperty(new("physical.radius"), AstronomyPhysicalPropertyCategory.Size, new AstronomyScalarPhysicalPropertyValue(new(10m, unit)), AstronomyPhysicalPropertyQualifier.Equatorial);
        var polar = new AstronomyPhysicalProperty(new("physical.radius"), AstronomyPhysicalPropertyCategory.Size, new AstronomyScalarPhysicalPropertyValue(new(9m, unit)), AstronomyPhysicalPropertyQualifier.Polar);
        var payload = new AstronomyPhysicalPropertiesPayload(new("typed.physical.properties.v1"), [polar, eq]);
        var equivalent = new AstronomyPhysicalPropertiesPayload(new("typed.physical.properties.v1"), [eq, polar]);

        Assert.Equal([AstronomyPhysicalPropertyQualifier.Equatorial, AstronomyPhysicalPropertyQualifier.Polar], payload.Properties.Select(p => p.Qualifier).ToArray());
        Assert.Equal(payload, equivalent);
        Assert.Equal(payload.GetHashCode(), equivalent.GetHashCode());
    }

    [Fact]
    public void Task23B_public_api_shape_has_no_mutable_or_boundary_properties()
    {
        var types = new[] { typeof(AstronomyEntityClassificationPayload), typeof(AstronomyPhysicalPropertiesPayload), typeof(AstronomyPhysicalProperty), typeof(AstronomyClassificationAssignment) };
        var forbiddenNames = new[] { "Evidence", "Confidence", "Audit", "Validity", "Observer", "ReferenceFrame", "ReferenceOrigin", "Coordinate", "Serialization" };
        foreach (var type in types)
        {
            Assert.All(type.GetProperties(BindingFlags.Instance | BindingFlags.Public), property =>
            {
                Assert.Null(property.SetMethod);
                Assert.NotEqual(typeof(object), property.PropertyType);
                Assert.DoesNotContain(property.Name, forbiddenNames, StringComparer.OrdinalIgnoreCase);
                Assert.False(property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(IDictionary<,>));
            });
        }
    }
}
