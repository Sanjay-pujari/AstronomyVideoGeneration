using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

public sealed record AstronomyAngularPositionValue : AstronomyPositionValue
{
    public AstronomyAngularPositionValue(AstronomyAngularCoordinateValue firstAngle, AstronomyAngularCoordinateValue secondAngle)
    {
        FirstAngle = firstAngle ?? throw new ArgumentNullException(nameof(firstAngle));
        SecondAngle = secondAngle ?? throw new ArgumentNullException(nameof(secondAngle));
        if (FirstAngle.Component == SecondAngle.Component) throw new ArgumentException("Angular position component types must differ.", nameof(secondAngle));
    }

    public override AstronomyPositionRepresentationKind Kind => AstronomyPositionRepresentationKind.Angular;
    public AstronomyAngularCoordinateValue FirstAngle { get; }
    public AstronomyAngularCoordinateValue SecondAngle { get; }
}
