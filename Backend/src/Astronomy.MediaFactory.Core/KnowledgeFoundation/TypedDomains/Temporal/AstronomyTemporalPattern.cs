using System.Collections.ObjectModel;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyTemporalPattern : IEquatable<AstronomyTemporalPattern>
{
    public AstronomyTemporalPattern(AstronomyTemporalPatternId patternId, AstronomyTemporalPatternKind kind, AstronomyTemporalPatternReferenceContext referenceContext, AstronomyRecurrenceDescription recurrence, AstronomyCyclePeriod? cyclePeriod = null, IEnumerable<AstronomyCyclePhase>? phases = null, IEnumerable<AstronomyTemporalOccurrence>? suppliedOccurrences = null, AstronomySeasonalPattern? season = null, AstronomyTemporalApplicability? applicability = null, string? name = null, string? summary = null)
    { if (!patternId.IsValid) throw new ArgumentException("Temporal pattern ID is required.", nameof(patternId)); PatternId = patternId; Kind = TemporalGuards.Defined(kind, nameof(kind)); ReferenceContext = referenceContext ?? throw new ArgumentNullException(nameof(referenceContext)); Recurrence = recurrence ?? throw new ArgumentNullException(nameof(recurrence)); CyclePeriod = cyclePeriod; Phases = CopyPhases(phases ?? []); SuppliedOccurrences = CopyOccurrences(suppliedOccurrences ?? []); Season = season; Applicability = applicability; Name = TemporalGuards.OptionalText(name, TemporalGuards.MaxNameLength, nameof(name), "Temporal pattern name"); Summary = TemporalGuards.OptionalText(summary, TemporalGuards.MaxTextLength, nameof(summary), "Temporal pattern summary"); }
    public AstronomyTemporalPatternId PatternId { get; }
    public AstronomyTemporalPatternKind Kind { get; }
    public AstronomyTemporalPatternReferenceContext ReferenceContext { get; }
    public AstronomyRecurrenceDescription Recurrence { get; }
    public AstronomyCyclePeriod? CyclePeriod { get; }
    public IReadOnlyList<AstronomyCyclePhase> Phases { get; }
    public IReadOnlyList<AstronomyTemporalOccurrence> SuppliedOccurrences { get; }
    public AstronomySeasonalPattern? Season { get; }
    public AstronomyTemporalApplicability? Applicability { get; }
    public string? Name { get; }
    public string? Summary { get; }
    public bool Equals(AstronomyTemporalPattern? other) => other is not null && PatternId == other.PatternId && Kind == other.Kind && Equals(ReferenceContext, other.ReferenceContext) && Equals(Recurrence, other.Recurrence) && Equals(CyclePeriod, other.CyclePeriod) && Phases.SequenceEqual(other.Phases) && SuppliedOccurrences.SequenceEqual(other.SuppliedOccurrences) && Equals(Season, other.Season) && Equals(Applicability, other.Applicability) && Name == other.Name && Summary == other.Summary;
    public override int GetHashCode() { var hash = new HashCode(); hash.Add(PatternId); hash.Add(Kind); hash.Add(ReferenceContext); hash.Add(Recurrence); hash.Add(CyclePeriod); foreach (var phase in Phases) hash.Add(phase); foreach (var occurrence in SuppliedOccurrences) hash.Add(occurrence); hash.Add(Season); hash.Add(Applicability); hash.Add(Name); hash.Add(Summary); return hash.ToHashCode(); }
    private static IReadOnlyList<AstronomyCyclePhase> CopyPhases(IEnumerable<AstronomyCyclePhase> phases) { var ordered = phases.Select(x => x ?? throw new ArgumentException("Phases cannot contain null entries.", nameof(phases))).OrderBy(x => x.Position is null ? 0 : 1).ThenBy(x => x.Position?.Value ?? 0m).ThenBy(x => x.PhaseId.Value, StringComparer.Ordinal).ToArray(); if (ordered.GroupBy(x => x.PhaseId).Any(g => g.Count() > 1)) throw new ArgumentException("Phases must be unique by phase ID.", nameof(phases)); return Array.AsReadOnly(ordered); }
    private static IReadOnlyList<AstronomyTemporalOccurrence> CopyOccurrences(IEnumerable<AstronomyTemporalOccurrence> occurrences) { var ordered = occurrences.Select(x => x ?? throw new ArgumentException("Occurrences cannot contain null entries.", nameof(occurrences))).OrderBy(x => x.StartUtc).ThenBy(x => x.EndUtc).ThenBy(x => x.PhaseId?.Value, StringComparer.Ordinal).ToArray(); if (ordered.GroupBy(x => new { x.StartUtc, x.EndUtc, x.PhaseId }).Any(g => g.Count() > 1)) throw new ArgumentException("Occurrences must be unique by start, end, and phase ID.", nameof(occurrences)); return Array.AsReadOnly(ordered); }
}
