using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyCyclePeriod
{
    public AstronomyCyclePeriod(AstronomyMeasurement duration, bool isApproximate = false) { Duration = TemporalGuards.Positive(duration, AstronomyMeasurementDimension.Time, nameof(duration)); IsApproximate = isApproximate; }
    public AstronomyMeasurement Duration { get; }
    public bool IsApproximate { get; }
}
