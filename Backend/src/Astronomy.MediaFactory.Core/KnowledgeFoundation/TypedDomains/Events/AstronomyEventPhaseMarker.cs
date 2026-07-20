namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public sealed record AstronomyEventPhaseMarker
{
    public AstronomyEventPhaseMarker(AstronomyEventPhaseMarkerKind kind, DateTimeOffset timeUtc, string? label = null)
    {
        Kind = EnumGuard.RequireDefined(kind, nameof(kind));
        if (timeUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Event phase marker time must use UTC (zero offset).", nameof(timeUtc));
        TimeUtc = timeUtc;
        Label = EventText.Optional(label, EventText.MaxLabelLength, nameof(label), "Event phase marker label");
    }
    public AstronomyEventPhaseMarkerKind Kind { get; }
    public DateTimeOffset TimeUtc { get; }
    public string? Label { get; }
}
