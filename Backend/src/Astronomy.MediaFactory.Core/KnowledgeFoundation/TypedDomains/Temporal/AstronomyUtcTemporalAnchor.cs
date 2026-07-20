namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyUtcTemporalAnchor : AstronomyTemporalAnchor
{
    public AstronomyUtcTemporalAnchor(DateTimeOffset instantUtc) { InstantUtc = TemporalGuards.Utc(instantUtc, nameof(instantUtc)); }
    public override AstronomyTemporalAnchorKind Kind => AstronomyTemporalAnchorKind.UtcInstant;
    public DateTimeOffset InstantUtc { get; }
}
