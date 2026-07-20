namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyDayOfYearTemporalAnchor : AstronomyTemporalAnchor
{
    public AstronomyDayOfYearTemporalAnchor(int dayOfYear) { if (dayOfYear is < 1 or > 366) throw new ArgumentOutOfRangeException(nameof(dayOfYear)); DayOfYear = dayOfYear; }
    public override AstronomyTemporalAnchorKind Kind => AstronomyTemporalAnchorKind.DayOfYear;
    public int DayOfYear { get; }
}
