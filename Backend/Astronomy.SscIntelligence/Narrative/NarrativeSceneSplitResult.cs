namespace Astronomy.SscIntelligence.Narrative;
public sealed record NarrativeSceneSplitResult(bool SplitApplied,string Reason,IReadOnlyList<NarrativeSplitScene> Scenes);
