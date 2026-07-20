namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public sealed record AstronomyIntervalEventTemporalExtent : AstronomyEventTemporalExtent
{
    public AstronomyIntervalEventTemporalExtent(DateTimeOffset startUtc, DateTimeOffset endUtc, bool isApproximate = false)
    {
        StartUtc = RequireUtc(startUtc, nameof(startUtc)); EndUtc = RequireUtc(endUtc, nameof(endUtc));
        if (StartUtc > EndUtc) throw new ArgumentException("Event interval start must be earlier than or equal to end.", nameof(startUtc));
        IsApproximate = isApproximate;
    }
    public DateTimeOffset StartUtc { get; }
    public DateTimeOffset EndUtc { get; }
    public bool IsApproximate { get; }
    public override AstronomyEventTimeKind Kind => IsApproximate ? AstronomyEventTimeKind.ApproximateInterval : AstronomyEventTimeKind.Interval;
}
