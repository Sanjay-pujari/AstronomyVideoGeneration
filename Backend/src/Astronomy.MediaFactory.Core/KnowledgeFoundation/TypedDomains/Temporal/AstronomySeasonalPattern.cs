namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomySeasonalPattern
{
    public AstronomySeasonalPattern(AstronomyCalendarDateTemporalAnchor start, AstronomyCalendarDateTemporalAnchor end, bool crossesYearBoundary = false, string? label = null) { Start = start ?? throw new ArgumentNullException(nameof(start)); End = end ?? throw new ArgumentNullException(nameof(end)); CrossesYearBoundary = crossesYearBoundary; Label = TemporalGuards.OptionalText(label, TemporalGuards.MaxNameLength, nameof(label), "Seasonal pattern label"); }
    public AstronomyCalendarDateTemporalAnchor Start { get; }
    public AstronomyCalendarDateTemporalAnchor End { get; }
    public bool CrossesYearBoundary { get; }
    public string? Label { get; }
}
