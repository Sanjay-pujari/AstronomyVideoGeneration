using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyCyclePhase
{
    public AstronomyCyclePhase(AstronomyCyclePhaseId phaseId, AstronomyMeasurement? position = null, AstronomyMeasurement? duration = null, string? name = null, string? note = null)
    { if (!phaseId.IsValid) throw new ArgumentException("Cycle phase ID is required.", nameof(phaseId)); PhaseId = phaseId; if (position is not null) { TemporalGuards.RequireDimension(position, AstronomyMeasurementDimension.Dimensionless, nameof(position)); if (position.Value is < 0m or > 1m) throw new ArgumentOutOfRangeException(nameof(position), position.Value, "Normalized phase position must be between 0 and 1 inclusive."); } if (duration is not null) TemporalGuards.Positive(duration, AstronomyMeasurementDimension.Time, nameof(duration)); Position = position; Duration = duration; Name = TemporalGuards.OptionalText(name, TemporalGuards.MaxNameLength, nameof(name), "Cycle phase name"); Note = TemporalGuards.OptionalText(note, TemporalGuards.MaxTextLength, nameof(note), "Cycle phase note"); }
    public AstronomyCyclePhaseId PhaseId { get; }
    public AstronomyMeasurement? Position { get; }
    public AstronomyMeasurement? Duration { get; }
    public string? Name { get; }
    public string? Note { get; }
}
