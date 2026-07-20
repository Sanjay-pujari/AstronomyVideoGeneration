using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

public sealed record AstronomySphericalPositionValue : AstronomyPositionValue
{
    public AstronomySphericalPositionValue(AstronomySphericalCoordinate coordinate)
    {
        Coordinate = coordinate ?? throw new ArgumentNullException(nameof(coordinate));
    }

    public override AstronomyPositionRepresentationKind Kind => AstronomyPositionRepresentationKind.Spherical;
    public AstronomySphericalCoordinate Coordinate { get; }
}
