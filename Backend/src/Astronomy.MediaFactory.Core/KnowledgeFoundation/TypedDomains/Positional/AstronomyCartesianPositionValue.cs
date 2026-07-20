using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

public sealed record AstronomyCartesianPositionValue : AstronomyPositionValue
{
    public AstronomyCartesianPositionValue(AstronomyCartesianCoordinate coordinate)
    {
        Coordinate = coordinate ?? throw new ArgumentNullException(nameof(coordinate));
    }

    public override AstronomyPositionRepresentationKind Kind => AstronomyPositionRepresentationKind.Cartesian;
    public AstronomyCartesianCoordinate Coordinate { get; }
}
