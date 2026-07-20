namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyCalendarDateTemporalAnchor : AstronomyTemporalAnchor
{
    public AstronomyCalendarDateTemporalAnchor(int month, int day) { if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month)); var max = month == 2 ? 29 : DateTime.DaysInMonth(2001, month); if (day < 1 || day > max) throw new ArgumentOutOfRangeException(nameof(day)); Month = month; Day = day; }
    public override AstronomyTemporalAnchorKind Kind => AstronomyTemporalAnchorKind.CalendarDate;
    public int Month { get; }
    public int Day { get; }
}
