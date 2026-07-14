using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public static class LegacyRequiredSemanticFactCompatibilityMapper
{
    public static ResolvedSemanticFact? Map(ResolvedSemanticFactV1 fact, string legacyFactType, string beatId, string requiredness, string language)
    {
        if (fact.Status is not (SemanticResolutionStatusV1.Resolved or SemanticResolutionStatusV1.ResolvedByCombination)) return null;

        var value = fact.TypedValue?.Value ?? fact.CanonicalValue;
        if (value is null) return null;

        return new ResolvedSemanticFact(
            legacyFactType,
            legacyFactType,
            value,
            null,
            legacyFactType,
            fact.WinningSourceId ?? fact.WinningAdapterId ?? "SemanticResolutionEngineV1",
            fact.WinningCandidateId ?? fact.CapabilityId.Value,
            beatId,
            "Verified",
            fact.Confidence,
            requiredness,
            fact.SpeakableValue,
            fact.SpeakableValue,
            language,
            true,
            "Source",
            null,
            fact.Provenance.Select(p => p.SourcePropertyPath).ToArray());
    }
}
