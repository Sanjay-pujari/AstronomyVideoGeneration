namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Observation;
public sealed record ObservationGuidance(string Text, IReadOnlyList<string> SourceFields, bool FallbackUsed);
