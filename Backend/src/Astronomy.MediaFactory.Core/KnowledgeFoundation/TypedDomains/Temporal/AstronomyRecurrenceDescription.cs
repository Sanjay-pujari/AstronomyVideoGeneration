namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyRecurrenceDescription
{
    public AstronomyRecurrenceDescription(AstronomyRecurrenceKind kind, AstronomyCyclePeriod? fixedPeriod = null, AstronomyCalendarInterval? calendarInterval = null, AstronomyTemporalAnchor? anchor = null, bool isApproximate = false, string? note = null)
    { Kind = TemporalGuards.Defined(kind, nameof(kind)); if (Kind == AstronomyRecurrenceKind.FixedPeriod && fixedPeriod is null) throw new ArgumentException("Fixed-period recurrence requires a fixed period.", nameof(fixedPeriod)); if (Kind == AstronomyRecurrenceKind.CalendarInterval && calendarInterval is null) throw new ArgumentException("Calendar-interval recurrence requires a calendar interval.", nameof(calendarInterval)); if (Kind != AstronomyRecurrenceKind.FixedPeriod && fixedPeriod is not null) throw new ArgumentException("Fixed period is only compatible with fixed-period recurrence.", nameof(fixedPeriod)); if (Kind != AstronomyRecurrenceKind.CalendarInterval && calendarInterval is not null) throw new ArgumentException("Calendar interval is only compatible with calendar-interval recurrence.", nameof(calendarInterval)); FixedPeriod = fixedPeriod; CalendarInterval = calendarInterval; Anchor = anchor; IsApproximate = isApproximate; Note = TemporalGuards.OptionalText(note, TemporalGuards.MaxTextLength, nameof(note), "Recurrence note"); }
    public AstronomyRecurrenceKind Kind { get; }
    public AstronomyCyclePeriod? FixedPeriod { get; }
    public AstronomyCalendarInterval? CalendarInterval { get; }
    public AstronomyTemporalAnchor? Anchor { get; }
    public bool IsApproximate { get; }
    public string? Note { get; }
}
