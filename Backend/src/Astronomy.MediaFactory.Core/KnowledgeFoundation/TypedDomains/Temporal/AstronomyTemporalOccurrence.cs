namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyTemporalOccurrence
{
    public AstronomyTemporalOccurrence(DateTimeOffset startUtc, DateTimeOffset? endUtc = null, AstronomyCyclePhaseId? phaseId = null, bool isApproximate = false, string? note = null) { StartUtc = TemporalGuards.Utc(startUtc, nameof(startUtc)); if (endUtc.HasValue) { EndUtc = TemporalGuards.Utc(endUtc.Value, nameof(endUtc)); if (EndUtc.Value < StartUtc) throw new ArgumentException("Occurrence end cannot precede start.", nameof(endUtc)); } if (phaseId.HasValue && !phaseId.Value.IsValid) throw new ArgumentException("Cycle phase ID is required when supplied.", nameof(phaseId)); PhaseId = phaseId; IsApproximate = isApproximate; Note = TemporalGuards.OptionalText(note, TemporalGuards.MaxTextLength, nameof(note), "Temporal occurrence note"); }
    public DateTimeOffset StartUtc { get; }
    public DateTimeOffset? EndUtc { get; }
    public AstronomyCyclePhaseId? PhaseId { get; }
    public bool IsApproximate { get; }
    public string? Note { get; }
}
