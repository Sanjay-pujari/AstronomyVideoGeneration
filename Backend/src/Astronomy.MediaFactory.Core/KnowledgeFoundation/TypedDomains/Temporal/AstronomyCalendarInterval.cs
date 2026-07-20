namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;

public sealed record AstronomyCalendarInterval
{
    public AstronomyCalendarInterval(int interval, AstronomyCadenceUnit unit)
    {
        if (interval <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                "Calendar interval must be greater than zero.");
        }

        unit = TemporalGuards.Defined(unit, nameof(unit));

        if (unit is not (AstronomyCadenceUnit.Day or AstronomyCadenceUnit.Week or AstronomyCadenceUnit.Month or AstronomyCadenceUnit.Year))
        {
            throw new ArgumentException(
                "Calendar intervals support only Day, Week, Month, or Year units.",
                nameof(unit));
        }

        Interval = interval;
        Unit = unit;
    }

    public int Interval { get; }

    public AstronomyCadenceUnit Unit { get; }
}
