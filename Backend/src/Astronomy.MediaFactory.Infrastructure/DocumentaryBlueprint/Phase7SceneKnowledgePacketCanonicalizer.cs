using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Canonical projection for packet content whose contract semantics are unordered.</summary>
public static class Phase7SceneKnowledgePacketCanonicalizer
{
    public static SceneKnowledgePacket Canonicalize(SceneKnowledgePacket packet)
    {
        static string[] Sort(IEnumerable<string> values) => values.Order(StringComparer.Ordinal).ToArray();
        static CertifiedNarrationClaim[] Claims(IEnumerable<CertifiedNarrationClaim> claims) =>
            claims.OrderBy(x => x.ClaimId, StringComparer.Ordinal).ToArray();
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

    /// <summary>Computes identity from a checksum-only projection; packet claims are never replaced or mutated.</summary>
    public static string ComputePacketId(SceneKnowledgePacket packet) =>
        $"packet-{packet.Variant.ToLowerInvariant()}-{Phase7Determinism.Hash(ChecksumProjection(packet with
        {
            PacketId = "", DeterministicChecksum = ""
        }))[..20]}";

    /// <summary>Computes the packet checksum from the canonical semantic projection.</summary>
    public static string ComputeChecksum(SceneKnowledgePacket packet) =>
        Phase7Determinism.Hash(ChecksumProjection(packet with { DeterministicChecksum = "" }));

    // Source/reference membership is semantically unordered for packet hashing.  Copies exist only in
    // this private projection and can therefore never escape into serialized packet authority.
    private static SceneKnowledgePacket ChecksumProjection(SceneKnowledgePacket packet)
    {
        static CertifiedNarrationClaim Claim(CertifiedNarrationClaim claim) => claim with
        {
            SourceIds = claim.SourceIds.Order(StringComparer.Ordinal).ToArray(),
            KnowledgeReferenceIds = claim.KnowledgeReferenceIds.Order(StringComparer.Ordinal).ToArray()
        };
        return Canonicalize(packet) with
        {
            RequiredClaims = packet.RequiredClaims.OrderBy(x => x.ClaimId, StringComparer.Ordinal).Select(Claim).ToArray(),
            OptionalClaims = packet.OptionalClaims.OrderBy(x => x.ClaimId, StringComparer.Ordinal).Select(Claim).ToArray(),
            DeferredClaims = packet.DeferredClaims.OrderBy(x => x.ClaimId, StringComparer.Ordinal).Select(Claim).ToArray()
        };
    }
}
