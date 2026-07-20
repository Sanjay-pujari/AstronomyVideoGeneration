namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public sealed record AstronomyInstantEventTemporalExtent : AstronomyEventTemporalExtent
{
    public AstronomyInstantEventTemporalExtent(DateTimeOffset instantUtc, bool isApproximate = false)
    { InstantUtc = RequireUtc(instantUtc, nameof(instantUtc)); IsApproximate = isApproximate; }
    public DateTimeOffset InstantUtc { get; }
    public bool IsApproximate { get; }
    public override AstronomyEventTimeKind Kind => IsApproximate ? AstronomyEventTimeKind.ApproximateInstant : AstronomyEventTimeKind.Instant;
}
