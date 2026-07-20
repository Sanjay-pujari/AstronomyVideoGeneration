namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public sealed record AstronomyEventCircumstance
{
    public AstronomyEventCircumstance(AstronomyEventCircumstanceId circumstanceId, string? value = null, string? note = null)
    {
        if (!circumstanceId.IsValid) throw new ArgumentException("Event circumstance ID is required.", nameof(circumstanceId));
        CircumstanceId = circumstanceId;
        Value = EventText.Optional(value, EventText.MaxLabelLength, nameof(value), "Event circumstance value");
        Note = EventText.Optional(note, EventText.MaxNoteLength, nameof(note), "Event circumstance note");
    }
    public AstronomyEventCircumstanceId CircumstanceId { get; }
    public string? Value { get; }
    public string? Note { get; }
}
