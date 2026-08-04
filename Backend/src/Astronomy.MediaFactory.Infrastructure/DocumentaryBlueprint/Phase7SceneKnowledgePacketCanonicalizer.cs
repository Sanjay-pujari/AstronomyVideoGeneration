using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Canonical projection for packet content whose contract semantics are unordered.</summary>
public static class Phase7SceneKnowledgePacketCanonicalizer
{
    public static SceneKnowledgePacket Canonicalize(SceneKnowledgePacket packet)
    {
        static string[] Sort(IEnumerable<string> values) => values.Order(StringComparer.Ordinal).ToArray();
        static CertifiedNarrationClaim Claim(CertifiedNarrationClaim claim) => claim with
        {
            SourceIds = Sort(claim.SourceIds),
            KnowledgeReferenceIds = Sort(claim.KnowledgeReferenceIds)
        };
        static CertifiedNarrationClaim[] Claims(IEnumerable<CertifiedNarrationClaim> claims) =>
            claims.Select(Claim).OrderBy(x => x.ClaimId, StringComparer.Ordinal).ToArray();
        static SortedDictionary<string,string> Dictionary(IReadOnlyDictionary<string,string> values)
        {
            var result = new SortedDictionary<string,string>(StringComparer.Ordinal);
            foreach (var pair in values) result.Add(pair.Key, pair.Value);
            return result;
        }

        return packet with
        {
            RequiredClaims = Claims(packet.RequiredClaims), OptionalClaims = Claims(packet.OptionalClaims),
            DeferredClaims = Claims(packet.DeferredClaims), SourceIds = Sort(packet.SourceIds),
            VisualEvidenceIds = Sort(packet.VisualEvidenceIds), ProtectedTerms = Sort(packet.ProtectedTerms),
            ApproximationWarnings = Sort(packet.ApproximationWarnings), Warnings = Sort(packet.Warnings),
            BlockingIssues = Sort(packet.BlockingIssues), CulturalContext = Sort(packet.CulturalContext),
            LocalizedVocabulary = Dictionary(packet.LocalizedVocabulary),
            PronunciationHints = Dictionary(packet.PronunciationHints),
            UpstreamSemanticLineage = Dictionary(packet.UpstreamSemanticLineage),
            ReferenceResolutions = packet.ReferenceResolutions.Select(x => x with
                { ResolvedClaimIds = Sort(x.ResolvedClaimIds) }).ToArray()
        };
    }
}
