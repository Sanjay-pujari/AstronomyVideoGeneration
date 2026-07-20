namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyMonthTemporalAnchor : AstronomyTemporalAnchor
{
    public AstronomyMonthTemporalAnchor(int month) { if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month)); Month = month; }
    public override AstronomyTemporalAnchorKind Kind => AstronomyTemporalAnchorKind.MonthOfYear;
    public int Month { get; }
}
