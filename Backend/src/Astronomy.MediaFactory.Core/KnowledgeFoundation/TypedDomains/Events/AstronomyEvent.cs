namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

public sealed record AstronomyEvent : IEquatable<AstronomyEvent>
{
    public AstronomyEvent(AstronomyEventId eventId, AstronomyEventKind kind, AstronomyEventTemporalExtent temporalExtent, AstronomyEventReferenceContext referenceContext, IEnumerable<AstronomyEventParticipant> participants, IEnumerable<AstronomyEventPhaseMarker>? phaseMarkers = null, IEnumerable<AstronomyEventGeometryQuantity>? geometry = null, IEnumerable<AstronomyEventCircumstance>? circumstances = null, AstronomyEventSignificance significance = AstronomyEventSignificance.Unspecified, string? name = null, string? summary = null)
    {
        if (!eventId.IsValid) throw new ArgumentException("Astronomy event ID is required.", nameof(eventId));
        EventId = eventId; Kind = EnumGuard.RequireDefined(kind, nameof(kind));
        TemporalExtent = temporalExtent ?? throw new ArgumentNullException(nameof(temporalExtent));
        ReferenceContext = referenceContext ?? throw new ArgumentNullException(nameof(referenceContext));
        Participants = CopyParticipants(participants);
        PhaseMarkers = CopyPhaseMarkers(phaseMarkers ?? []);
        Geometry = CopyGeometry(geometry ?? []);
        Circumstances = CopyCircumstances(circumstances ?? []);
        Significance = EnumGuard.RequireDefined(significance, nameof(significance));
        Name = EventText.Optional(name, EventText.MaxNameLength, nameof(name), "Event name");
        Summary = EventText.Optional(summary, EventText.MaxSummaryLength, nameof(summary), "Event summary");
    }
    public AstronomyEventId EventId { get; }
    public AstronomyEventKind Kind { get; }
    public AstronomyEventTemporalExtent TemporalExtent { get; }
    public AstronomyEventReferenceContext ReferenceContext { get; }
    public IReadOnlyList<AstronomyEventParticipant> Participants { get; }
    public IReadOnlyList<AstronomyEventPhaseMarker> PhaseMarkers { get; }
    public IReadOnlyList<AstronomyEventGeometryQuantity> Geometry { get; }
    public IReadOnlyList<AstronomyEventCircumstance> Circumstances { get; }
    public AstronomyEventSignificance Significance { get; }
    public string? Name { get; }
    public string? Summary { get; }
    public bool Equals(AstronomyEvent? other) => other is not null && EventId == other.EventId && Kind == other.Kind && TemporalExtent == other.TemporalExtent && ReferenceContext == other.ReferenceContext && Participants.SequenceEqual(other.Participants) && PhaseMarkers.SequenceEqual(other.PhaseMarkers) && Geometry.SequenceEqual(other.Geometry) && Circumstances.SequenceEqual(other.Circumstances) && Significance == other.Significance && Name == other.Name && Summary == other.Summary;
    public override int GetHashCode() { var h = HashCode.Combine(EventId, Kind, TemporalExtent, ReferenceContext, Significance, Name, Summary); foreach (var x in Participants) h = HashCode.Combine(h, x); foreach (var x in PhaseMarkers) h = HashCode.Combine(h, x); foreach (var x in Geometry) h = HashCode.Combine(h, x); foreach (var x in Circumstances) h = HashCode.Combine(h, x); return h; }
    private static IReadOnlyList<AstronomyEventParticipant> CopyParticipants(IEnumerable<AstronomyEventParticipant> items) { ArgumentNullException.ThrowIfNull(items); var a=items.Select(x=>x??throw new ArgumentException("Event participants cannot contain null entries.", nameof(items))).OrderBy(x=>x.Role).ThenBy(x=>x.Entity.EntityId,StringComparer.Ordinal).ToArray(); if(a.Length==0) throw new ArgumentException("At least one event participant is required.", nameof(items)); if(a.GroupBy(x=>new{x.Entity.EntityId,x.Role}).Any(g=>g.Count()>1)) throw new ArgumentException("Event participants must be unique by entity and role.", nameof(items)); return Array.AsReadOnly(a); }
    private static IReadOnlyList<AstronomyEventPhaseMarker> CopyPhaseMarkers(IEnumerable<AstronomyEventPhaseMarker> items) { ArgumentNullException.ThrowIfNull(items); var a=items.Select(x=>x??throw new ArgumentException("Event phase markers cannot contain null entries.", nameof(items))).OrderBy(x=>x.TimeUtc).ThenBy(x=>x.Kind).ToArray(); if(a.GroupBy(x=>new{x.Kind,x.TimeUtc}).Any(g=>g.Count()>1)) throw new ArgumentException("Event phase markers must be unique by kind and time.", nameof(items)); return Array.AsReadOnly(a); }
    private static IReadOnlyList<AstronomyEventGeometryQuantity> CopyGeometry(IEnumerable<AstronomyEventGeometryQuantity> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var normalized = items
            .Select(x => x ?? throw new ArgumentException("Event geometry cannot contain null entries.", nameof(items)))
            .OrderBy(x => x.QuantityId.Value, StringComparer.Ordinal)
            .ThenBy(x => x.Epoch?.Kind)
            .ThenBy(x => x.Epoch?.InstantUtc)
            .ToArray();

        if (normalized.GroupBy(x => new { x.QuantityId, x.Epoch }).Any(g => g.Count() > 1))
            throw new ArgumentException("Event geometry must be unique by quantity ID and epoch.", nameof(items));

        return Array.AsReadOnly(normalized);
    }
    private static IReadOnlyList<AstronomyEventCircumstance> CopyCircumstances(IEnumerable<AstronomyEventCircumstance> items) { ArgumentNullException.ThrowIfNull(items); var a=items.Select(x=>x??throw new ArgumentException("Event circumstances cannot contain null entries.", nameof(items))).OrderBy(x=>x.CircumstanceId.Value,StringComparer.Ordinal).ToArray(); if(a.GroupBy(x=>x.CircumstanceId).Any(g=>g.Count()>1)) throw new ArgumentException("Event circumstances must be unique by circumstance ID.", nameof(items)); return Array.AsReadOnly(a); }
}
