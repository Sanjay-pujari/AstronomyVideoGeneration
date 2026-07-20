using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

public sealed record AstronomyObservationTimeWindow
{
    public AstronomyObservationTimeWindow(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (startUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Observation window start must use UTC (zero offset).", nameof(startUtc));
        if (endUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Observation window end must use UTC (zero offset).", nameof(endUtc));
        if (startUtc > endUtc) throw new ArgumentException("Observation window start must be earlier than or equal to end.", nameof(startUtc));
        StartUtc = startUtc; EndUtc = endUtc;
    }
    public DateTimeOffset StartUtc { get; }
    public DateTimeOffset EndUtc { get; }
}
