using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

public sealed record AstronomySphericalCoordinate
{
    public AstronomySphericalCoordinate(AstronomyAngularCoordinateValue longitudeLike, AstronomyAngularCoordinateValue latitudeLike, AstronomyMeasurement? distance = null)
    {
        LongitudeLike = longitudeLike ?? throw new ArgumentNullException(nameof(longitudeLike));
        LatitudeLike = latitudeLike ?? throw new ArgumentNullException(nameof(latitudeLike));
        if (LongitudeLike.Component == LatitudeLike.Component) throw new ArgumentException("Spherical coordinate angular component types must differ.", nameof(latitudeLike));
        if (distance is not null && distance.Unit.Dimension != AstronomyMeasurementDimension.Distance) throw new ArgumentException("Spherical coordinate distance must use the Distance dimension.", nameof(distance));
        Distance = distance;
    }

    public AstronomyAngularCoordinateValue LongitudeLike { get; }
    public AstronomyAngularCoordinateValue LatitudeLike { get; }
    public AstronomyMeasurement? Distance { get; }
}
