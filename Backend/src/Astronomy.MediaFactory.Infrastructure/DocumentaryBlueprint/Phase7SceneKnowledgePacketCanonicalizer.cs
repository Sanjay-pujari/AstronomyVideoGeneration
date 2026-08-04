using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>
/// Immutable checksum evidence for a certified claim.  This deliberately is not the public
/// authority contract: hashing may normalize unordered membership without manufacturing a new
/// <see cref="CertifiedNarrationClaim"/> that appears to be certified.
/// </summary>
internal sealed record Phase7ClaimChecksumProjection(
    string ClaimId,
    string SemanticIdentity,
    string Domain,
    string Text,
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<string> KnowledgeReferenceIds,
    decimal Confidence,
    bool IsApproximate,
    bool IsLocationDependent,
    bool IsDateTimeDependent,
    bool IsCultural,
    bool IsMythological,
    bool IsAstrologyRelated,
    bool RequiresQualification,
    bool RequiresHumanReview,
    string Language,
    Phase7ClaimDisposition Disposition,
    string ProvenancePrecision,
    string SelectionReason,
    bool WeatherDependent,
    bool MoonDependent,
    bool Uncertain,
    string Checksum);

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

    // The packet shell has empty claim partitions. Claim authority is represented exclusively by the
    // dedicated immutable projections, which can never escape as CertifiedNarrationClaim instances.
    private static object ChecksumProjection(SceneKnowledgePacket packet)
    {
        static Phase7ClaimChecksumProjection Claim(CertifiedNarrationClaim claim) => new(
            claim.ClaimId, claim.SemanticIdentity, claim.Domain, claim.Text,
            claim.SourceIds.Order(StringComparer.Ordinal).ToArray(),
            claim.KnowledgeReferenceIds.Order(StringComparer.Ordinal).ToArray(),
            claim.Confidence, claim.IsApproximate, claim.IsLocationDependent,
            claim.IsDateTimeDependent, claim.IsCultural, claim.IsMythological,
            claim.IsAstrologyRelated, claim.RequiresQualification, claim.RequiresHumanReview,
            claim.Language, claim.Disposition, claim.ProvenancePrecision, claim.SelectionReason,
            claim.WeatherDependent, claim.MoonDependent, claim.Uncertain, claim.Checksum);

        static Phase7ClaimChecksumProjection[] Claims(IEnumerable<CertifiedNarrationClaim> claims) =>
            claims.OrderBy(x => x.ClaimId, StringComparer.Ordinal).Select(Claim).ToArray();

        var shell = Canonicalize(packet) with
        {
            RequiredClaims = [], OptionalClaims = [], DeferredClaims = []
        };
        return new PacketChecksumProjection(shell, Claims(packet.RequiredClaims),
            Claims(packet.OptionalClaims), Claims(packet.DeferredClaims));
    }

    private sealed record PacketChecksumProjection(SceneKnowledgePacket Packet,
        IReadOnlyList<Phase7ClaimChecksumProjection> RequiredClaims,
        IReadOnlyList<Phase7ClaimChecksumProjection> OptionalClaims,
        IReadOnlyList<Phase7ClaimChecksumProjection> DeferredClaims);
}
