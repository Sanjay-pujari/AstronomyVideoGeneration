using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

public sealed record AstronomyHorizontalObservationCoordinate
{
    public AstronomyHorizontalObservationCoordinate(AstronomyAngularCoordinateValue azimuth, AstronomyAngularCoordinateValue altitude)
    {
        Azimuth = azimuth ?? throw new ArgumentNullException(nameof(azimuth));
        Altitude = altitude ?? throw new ArgumentNullException(nameof(altitude));
        if (Azimuth.Component != AstronomyAngularCoordinateComponent.Azimuth) throw new ArgumentException("Azimuth coordinate must use the Azimuth component.", nameof(azimuth));
        if (Altitude.Component != AstronomyAngularCoordinateComponent.Altitude) throw new ArgumentException("Altitude coordinate must use the Altitude component.", nameof(altitude));
    }
    public AstronomyAngularCoordinateValue Azimuth { get; }
    public AstronomyAngularCoordinateValue Altitude { get; }
}
