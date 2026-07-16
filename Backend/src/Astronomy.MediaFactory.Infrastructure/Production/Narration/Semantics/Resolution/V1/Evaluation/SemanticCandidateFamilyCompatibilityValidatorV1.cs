using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Evaluation;

public sealed class SemanticCandidateFamilyCompatibilityValidatorV1
{
    public const string FamilyIncompatibleCandidate = nameof(FamilyIncompatibleCandidate);

    public bool IsCompatible(string? activeFamilyId, SemanticSourceCandidateV1 candidate, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(activeFamilyId)) return true;
        if (candidate.CapabilityId.Value == SemanticCapabilityVocabularyV1.DomainScientificKnowledge && candidate.TypedValue.Value is DomainScientificKnowledgeValue dk)
        {
            var text = string.Join(" ", dk.Mechanism, dk.PerspectiveAlignmentExplanation, dk.ScientificSignificance, dk.StableObservingPrinciples);
            var planetPairing = text.Contains("planet", StringComparison.OrdinalIgnoreCase) || text.Contains("line-of-sight", StringComparison.OrdinalIgnoreCase) || text.Contains("line of sight", StringComparison.OrdinalIgnoreCase);
            var meteorScience = text.Contains("meteor", StringComparison.OrdinalIgnoreCase) || text.Contains("debris stream", StringComparison.OrdinalIgnoreCase) || text.Contains("radiant", StringComparison.OrdinalIgnoreCase);
            var planetPairingFamily = activeFamilyId.Equals("PlanetPairing", StringComparison.OrdinalIgnoreCase) || activeFamilyId.Equals("PlanetGrouping", StringComparison.OrdinalIgnoreCase);
            if (!planetPairingFamily && planetPairing) { reason = FamilyIncompatibleCandidate; return false; }
            if (!activeFamilyId.Equals("MeteorShower", StringComparison.OrdinalIgnoreCase) && meteorScience) { reason = FamilyIncompatibleCandidate; return false; }
        }
        return true;
    }
}
