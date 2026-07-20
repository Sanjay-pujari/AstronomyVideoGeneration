using System.Reflection;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using TypedDomain = Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.AstronomyKnowledgeDomain;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedPositionalKnowledgeTests
{
    [Fact]
    public void Angular_cartesian_and_spherical_coordinates_guard_local_structure_only()
    {
        var ra = new AstronomyAngularCoordinateValue(AstronomyAngularCoordinateComponent.RightAscension, Measure(-1, AstronomyMeasurementDimension.Angle, "deg"));
        var dec = new AstronomyAngularCoordinateValue(AstronomyAngularCoordinateComponent.Declination, Measure(2, AstronomyMeasurementDimension.Angle, "deg"));
        var spherical = new AstronomySphericalCoordinate(ra, dec, Measure(3, AstronomyMeasurementDimension.Distance, "km"));
        var cartesian = new AstronomyCartesianCoordinate(Measure(-1, AstronomyMeasurementDimension.Distance, "km"), Measure(0, AstronomyMeasurementDimension.Distance, "km"), Measure(1, AstronomyMeasurementDimension.Distance, "km"));

        Assert.Equal(-1, ra.Angle.Value);
        Assert.Equal(AstronomyAngularCoordinateComponent.RightAscension, spherical.LongitudeLike.Component);
        Assert.Equal(0, cartesian.Y.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyAngularCoordinateValue((AstronomyAngularCoordinateComponent)99, ra.Angle));
        Assert.Throws<ArgumentException>(() => new AstronomyAngularCoordinateValue(AstronomyAngularCoordinateComponent.RightAscension, Measure(1, AstronomyMeasurementDimension.Distance, "km")));
        Assert.Throws<ArgumentException>(() => new AstronomyCartesianCoordinate(Measure(1, AstronomyMeasurementDimension.Distance, "km"), Measure(1, AstronomyMeasurementDimension.Distance, "m"), Measure(1, AstronomyMeasurementDimension.Distance, "km")));
        Assert.Throws<ArgumentException>(() => new AstronomyCartesianCoordinate(Measure(1, AstronomyMeasurementDimension.Angle, "deg"), Measure(1, AstronomyMeasurementDimension.Angle, "deg"), Measure(1, AstronomyMeasurementDimension.Angle, "deg")));
        Assert.Throws<ArgumentException>(() => new AstronomySphericalCoordinate(ra, ra));
        Assert.Throws<ArgumentException>(() => new AstronomySphericalCoordinate(ra, dec, Measure(1, AstronomyMeasurementDimension.Angle, "deg")));
        Assert.Equal(cartesian, new AstronomyCartesianCoordinate(Measure(-1, AstronomyMeasurementDimension.Distance, "km"), Measure(0, AstronomyMeasurementDimension.Distance, "km"), Measure(1, AstronomyMeasurementDimension.Distance, "km")));
    }

    [Fact]
    public void Position_value_hierarchy_is_closed_and_variants_have_fixed_kinds()
    {
        var ra = new AstronomyAngularCoordinateValue(AstronomyAngularCoordinateComponent.RightAscension, Measure(1, AstronomyMeasurementDimension.Angle, "deg"));
        var dec = new AstronomyAngularCoordinateValue(AstronomyAngularCoordinateComponent.Declination, Measure(2, AstronomyMeasurementDimension.Angle, "deg"));
        var angular = new AstronomyAngularPositionValue(ra, dec);
        var spherical = new AstronomySphericalPositionValue(new AstronomySphericalCoordinate(ra, dec));
        var cartesian = new AstronomyCartesianPositionValue(new AstronomyCartesianCoordinate(Measure(1, AstronomyMeasurementDimension.Distance, "km"), Measure(2, AstronomyMeasurementDimension.Distance, "km"), Measure(3, AstronomyMeasurementDimension.Distance, "km")));

        Assert.Equal(AstronomyPositionRepresentationKind.Angular, angular.Kind);
        Assert.Equal(AstronomyPositionRepresentationKind.Spherical, spherical.Kind);
        Assert.Equal(AstronomyPositionRepresentationKind.Cartesian, cartesian.Kind);
        var constructors = typeof(AstronomyPositionValue).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Contains(constructors, c => c.IsFamilyAndAssembly);
        Assert.DoesNotContain(constructors, c => c.IsPublic || c.IsFamily);
        Assert.Equal([typeof(AstronomyAngularPositionValue), typeof(AstronomyCartesianPositionValue), typeof(AstronomySphericalPositionValue)], typeof(AstronomyPositionValue).Assembly.GetTypes().Where(t => t.BaseType == typeof(AstronomyPositionValue)).OrderBy(t => t.Name).ToArray());
    }

    [Fact]
    public void Spatial_position_payload_is_single_typed_epoch_bound_state_without_observer_context()
    {
        var context = new AstronomyPositionReferenceContext(AstronomyReferenceFrame.ICRS, AstronomyReferenceOrigin.Barycentric, AstronomyCoordinateSystem.Cartesian, AstronomyEpochReference.J2000, new AstronomyEntityReference("solar-system-barycenter", AstronomyEntityKind.ScientificConcept));
        var value = new AstronomyCartesianPositionValue(new AstronomyCartesianCoordinate(Measure(1, AstronomyMeasurementDimension.Distance, "km"), Measure(2, AstronomyMeasurementDimension.Distance, "km"), Measure(3, AstronomyMeasurementDimension.Distance, "km")));
        var position = new AstronomySpatialPosition(context, value, " note ");
        var payload = new AstronomySpatialPositionPayload(new("typed.positional.spatial-position.v1"), position);

        Assert.IsAssignableFrom<ITypedAstronomyKnowledgePayload>(payload);
        Assert.Equal(TypedDomain.Positional, payload.Domain);
        Assert.Equal(AstronomyKnowledgePayloadFamily.SpatialPosition, payload.Family);
        Assert.Equal("note", payload.Position.Note);
        Assert.Equal(payload, new AstronomySpatialPositionPayload(new("typed.positional.spatial-position.v1"), position));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyPositionReferenceContext((AstronomyReferenceFrame)99, AstronomyReferenceOrigin.Barycentric, AstronomyCoordinateSystem.Cartesian, AstronomyEpochReference.J2000));
        Assert.Throws<ArgumentException>(() => new AstronomySpatialPosition(context, value, "bad\r"));
        Assert.DoesNotContain(typeof(AstronomyPositionReferenceContext).GetProperties(), p => p.Name.Contains("Observation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(AstronomySpatialPositionPayload).GetProperties(), p => typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string));
    }

    private static AstronomyMeasurement Measure(decimal value, AstronomyMeasurementDimension dimension, string code) => new(value, new AstronomyMeasurementUnit(code, code, dimension));
}
