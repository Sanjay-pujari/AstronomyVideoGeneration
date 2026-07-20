using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

public sealed record AstronomyAngularCoordinateValue
{
    public AstronomyAngularCoordinateValue(AstronomyAngularCoordinateComponent component, AstronomyMeasurement angle)
    {
        Component = EnumGuard.RequireDefined(component, nameof(component));
        Angle = angle ?? throw new ArgumentNullException(nameof(angle));
        if (Angle.Unit.Dimension != AstronomyMeasurementDimension.Angle) throw new ArgumentException("Angular coordinate measurements must use the Angle dimension.", nameof(angle));
    }

    public AstronomyAngularCoordinateComponent Component { get; }
    public AstronomyMeasurement Angle { get; }
}
