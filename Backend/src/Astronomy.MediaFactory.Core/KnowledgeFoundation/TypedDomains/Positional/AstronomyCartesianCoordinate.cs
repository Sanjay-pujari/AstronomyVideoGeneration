using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

public sealed record AstronomyCartesianCoordinate
{
    public AstronomyCartesianCoordinate(AstronomyMeasurement x, AstronomyMeasurement y, AstronomyMeasurement z)
    {
        X = RequireDistance(x, nameof(x));
        Y = RequireDistance(y, nameof(y));
        Z = RequireDistance(z, nameof(z));
        if (X.Unit != Y.Unit || X.Unit != Z.Unit) throw new ArgumentException("Cartesian coordinate units must match exactly.");
        if (X.Unit.Dimension != Y.Unit.Dimension || X.Unit.Dimension != Z.Unit.Dimension) throw new ArgumentException("Cartesian coordinate dimensions must match exactly.");
    }

    public AstronomyMeasurement X { get; }
    public AstronomyMeasurement Y { get; }
    public AstronomyMeasurement Z { get; }

    private static AstronomyMeasurement RequireDistance(AstronomyMeasurement measurement, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(measurement, parameterName);
        if (measurement.Unit.Dimension != AstronomyMeasurementDimension.Distance) throw new ArgumentException("Cartesian coordinate components must use the Distance dimension.", parameterName);
        return measurement;
    }
}
