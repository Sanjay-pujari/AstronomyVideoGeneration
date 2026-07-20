namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyTemporalApplicability
{
    public AstronomyTemporalApplicability(DateTimeOffset? fromUtc = null, DateTimeOffset? throughUtc = null) { FromUtc = fromUtc.HasValue ? TemporalGuards.Utc(fromUtc.Value, nameof(fromUtc)) : null; ThroughUtc = throughUtc.HasValue ? TemporalGuards.Utc(throughUtc.Value, nameof(throughUtc)) : null; if (FromUtc.HasValue && ThroughUtc.HasValue && ThroughUtc.Value < FromUtc.Value) throw new ArgumentException("Applicability end cannot precede start.", nameof(throughUtc)); }
    public DateTimeOffset? FromUtc { get; }
    public DateTimeOffset? ThroughUtc { get; }
}
