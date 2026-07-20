namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public abstract record AstronomyEventTemporalExtent
{
    private protected AstronomyEventTemporalExtent() { }
    public abstract AstronomyEventTimeKind Kind { get; }
    protected static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        => value.Offset == TimeSpan.Zero ? value : throw new ArgumentException("Event time must use UTC (zero offset).", parameterName);
}
