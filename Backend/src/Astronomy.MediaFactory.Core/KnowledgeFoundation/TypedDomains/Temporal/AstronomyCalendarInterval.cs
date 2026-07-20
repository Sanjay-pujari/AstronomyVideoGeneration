namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyCalendarInterval
{
    public AstronomyCalendarInterval(int interval, AstronomyCadenceUnit unit) { if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval)); Interval = interval; Unit = TemporalGuards.Defined(unit, nameof(unit)); }
    public int Interval { get; }
    public AstronomyCadenceUnit Unit { get; }
}
