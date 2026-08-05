using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7KnowledgeReferenceNormalizer : IPhase7KnowledgeReferenceNormalizer
{
    private static readonly HashSet<string> SupportedNamespaces = new(StringComparer.Ordinal) { "production-event-intelligence" };

    public Phase7KnowledgeReferenceNormalizationResult Normalize(string referenceId)
    {
        var original = referenceId ?? "";
        var trimmed = original.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return Invalid(original, "P7REF_BLANK");
        var hash = trimmed.IndexOf('#');
        if (hash <= 0 || hash != trimmed.LastIndexOf('#')) return Invalid(original, "P7REF_MALFORMED");
        var ns = trimmed[..hash];
        var pointer = trimmed[(hash + 1)..];
        if (!SupportedNamespaces.Contains(ns)) return new(false, original, ns, "", [], "P7REF_UNSUPPORTED_NAMESPACE");
        if (!IsValidJsonPointer(pointer)) return new(false, original, ns, "", [], "P7REF_INVALID_JSON_POINTER");
        var canonical = DecodePointer(pointer);
        var candidates = new[] { trimmed, canonical, "#" + canonical, canonical.TrimStart('/') }
            .Distinct(StringComparer.Ordinal).ToArray();
        return new(true, original, ns, canonical, candidates, "P7REF_NORMALIZED");
    }

    private static Phase7KnowledgeReferenceNormalizationResult Invalid(string original, string code) => new(false, original, "", "", [], code);
    private static bool IsValidJsonPointer(string pointer)
    {
        if (!pointer.StartsWith("/", StringComparison.Ordinal)) return false;
        for (var i = 0; i < pointer.Length; i++)
            if (pointer[i] == '~' && (i + 1 >= pointer.Length || pointer[i + 1] is not ('0' or '1'))) return false;
        return true;
    }
    private static string DecodePointer(string pointer) => pointer.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
}

public sealed class Phase7KnowledgeReferenceIdentityBridge : IPhase7KnowledgeReferenceIdentityBridge
{
    private readonly IPhase7KnowledgeReferenceNormalizer normalizer;
    public Phase7KnowledgeReferenceIdentityBridge(IPhase7KnowledgeReferenceNormalizer normalizer) => this.normalizer = normalizer;

    public Phase7KnowledgeReferenceIdentityBridgeResult Resolve(string phase6ReferenceId, PublishedPhase7KnowledgeAuthority authority)
    {
        var normalized = normalizer.Normalize(phase6ReferenceId);
        var input = new Phase7ScenePacketInputAuthority(authority, null!, null!, "", "", "", "", "", authority.KnowledgeAuthority.Language, "", "", [], [], [], [], new Dictionary<string,string>(), new Dictionary<string,string>());
        return Resolve(normalized, input);
    }

    public Phase7KnowledgeReferenceIdentityBridgeResult Resolve(Phase7KnowledgeReferenceNormalizationResult normalized, Phase7ScenePacketInputAuthority authority)
    {
        if (!normalized.IsValid) return new(false, normalized.OriginalReferenceId, normalized.CanonicalJsonPointer, [], [], [], normalized.ReasonCode, normalized.ReasonCode, [$"sourceReferenceId={normalized.OriginalReferenceId}"]);
        var k = authority.Knowledge.KnowledgeAuthority;
        var pointer = normalized.CanonicalJsonPointer;
        var candidates = normalized.CanonicalIdentityCandidates;
        var claimIds = new SortedSet<string>(StringComparer.Ordinal);
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        var entities = new SortedSet<string>(StringComparer.Ordinal);
        var method = "P7PACKET_REFERENCE_PATH_UNMAPPED";

        bool AddClaims(IEnumerable<string> ids) { var before = claimIds.Count; foreach (var id in ids.Where(x => !string.IsNullOrWhiteSpace(x))) claimIds.Add(id); return claimIds.Count > before; }
        bool AddPaths(IEnumerable<string> ps) { var before = paths.Count; foreach (var p in ps.Where(x => !string.IsNullOrWhiteSpace(x))) paths.Add(NormalizePointer(p)); return paths.Count > before; }
        bool AddEntities(IEnumerable<string> es) { var before = entities.Count; foreach (var e in es.Where(x => !string.IsNullOrWhiteSpace(x))) entities.Add(e); return entities.Count > before; }

        if (AddClaims(k.Claims.Where(c => c.ClaimId == normalized.OriginalReferenceId || c.ClaimId == pointer).Select(c => c.ClaimId))) method = "P7PACKET_REFERENCE_RESOLVED_EXACT_CLAIM_ID";
        else if (AddClaims(k.Claims.Where(c => c.SemanticIdentity == normalized.OriginalReferenceId || c.SemanticIdentity == pointer || candidates.Contains(c.SemanticIdentity, StringComparer.Ordinal)).Select(c => c.ClaimId))) method = "P7PACKET_REFERENCE_RESOLVED_EXACT_SEMANTIC_IDENTITY";
        else if (AddClaims(k.Claims.Where(c => c.KnowledgeReferenceIds.Intersect(candidates, StringComparer.Ordinal).Any()).Select(c => c.ClaimId))) method = "P7PACKET_REFERENCE_RESOLVED_EXACT_KNOWLEDGE_REFERENCE";
        else
        {
            var exact = k.ClaimSupportEvidence.Where(e => NormalizePointer(e.ApprovedFieldPath) == pointer).ToArray();
            if (exact.Length > 0) { AddClaims(exact.Select(e => e.ClaimId)); AddPaths(exact.Select(e => e.ApprovedFieldPath)); AddEntities(exact.Select(e => e.KnowledgeId)); method = "P7PACKET_REFERENCE_RESOLVED_APPROVED_FIELD"; }
            else
            {
                var descendant = k.ClaimSupportEvidence.Where(e => IsDescendant(pointer, NormalizePointer(e.ApprovedFieldPath))).ToArray();
                if (descendant.Length > 0) { AddClaims(descendant.Select(e => e.ClaimId)); AddPaths(descendant.Select(e => e.ApprovedFieldPath)); AddEntities(descendant.Select(e => e.KnowledgeId)); method = "P7PACKET_REFERENCE_RESOLVED_COLLECTION_DESCENDANT"; }
                else
                {
                    var entityIds = k.KnowledgeEntities.Where(e => candidates.Contains(e.KnowledgeId, StringComparer.Ordinal)).Select(e => e.KnowledgeId).ToArray();
                    var support = k.ClaimSupportEvidence.Where(e => entityIds.Contains(e.KnowledgeId, StringComparer.Ordinal)).ToArray();
                    if (entityIds.Length > 0 || support.Length > 0) { AddEntities(entityIds.Concat(support.Select(e => e.KnowledgeId))); AddClaims(support.Select(e => e.ClaimId)); method = "P7PACKET_REFERENCE_RESOLVED_KNOWLEDGE_ENTITY"; }
                }
            }
        }
        var matched = k.Claims.Where(c => claimIds.Contains(c.ClaimId)).OrderBy(c => c.ClaimId, StringComparer.Ordinal).ToArray();
        foreach (var e in k.ClaimSupportEvidence.Where(e => claimIds.Contains(e.ClaimId))) { AddPaths([e.ApprovedFieldPath]); AddEntities([e.KnowledgeId]); }
        var eligible = matched.Where(c => IsRequiredEligible(c, authority)).Select(c => c.ClaimId).Order(StringComparer.Ordinal).ToArray();
        var evidence = new[] { $"sourceReferenceId={normalized.OriginalReferenceId}", $"normalizedPointer={pointer}", $"resolutionMethod={method}" }
            .Concat(paths.Select(x => $"matchedApprovedFieldPath={x}"))
            .Concat(entities.Select(x => $"matchedKnowledgeEntityId={x}"))
            .Concat(matched.Select(x => $"candidateClaimId={x.ClaimId};disposition={x.Disposition};requiresHumanReview={x.RequiresHumanReview};requiredEligible={eligible.Contains(x.ClaimId, StringComparer.Ordinal)}"))
            .ToArray();
        var idsOut = matched.Select(x => x.ClaimId).ToArray();
        return new(idsOut.Length > 0, normalized.OriginalReferenceId, pointer, paths.ToArray(), entities.ToArray(), idsOut, method, idsOut.Length > 0 ? method : "P7PACKET_REFERENCE_PATH_UNMAPPED", evidence) { CanonicalReferenceIds = candidates };
    }

    internal static bool IsRequiredEligible(CertifiedNarrationClaim c, Phase7ScenePacketInputAuthority input) =>
        c.Disposition == Phase7ClaimDisposition.Required && !c.RequiresHumanReview && string.Equals(c.Language, input.Language, StringComparison.OrdinalIgnoreCase) &&
        input.Knowledge.KnowledgeAuthority.ClaimSupportEvidence.Any(e => e.ClaimId == c.ClaimId && e.SemanticIdentity == c.SemanticIdentity && c.SourceIds.Contains(e.SourceId, StringComparer.Ordinal) && e.SourceEligibility == Phase7SourceEligibility.EligibleForRequiredClaim && !e.RequiresHumanReview && e.ProvenancePrecision is Phase7ProvenancePrecision.ExactClaim or Phase7ProvenancePrecision.ExactKnowledgeEntity or Phase7ProvenancePrecision.ExactApprovedField);
    private static string NormalizePointer(string id) { var s = id ?? ""; var hash = s.IndexOf('#'); if (hash >= 0) s = s[(hash + 1)..]; if (!s.StartsWith("/", StringComparison.Ordinal)) s = "/" + s; return s.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal); }
    private static bool IsDescendant(string parent, string path) => path.Length > parent.Length && path.StartsWith(parent, StringComparison.Ordinal) && path[parent.Length] == '/';
}

public sealed class Phase7KnowledgeReferenceResolver : IPhase7KnowledgeReferenceResolver
{
    private readonly IPhase7KnowledgeReferenceNormalizer normalizer;
    private readonly IPhase7KnowledgeReferenceIdentityBridge bridge;
    public Phase7KnowledgeReferenceResolver() : this(new Phase7KnowledgeReferenceNormalizer(), null) { }
    public Phase7KnowledgeReferenceResolver(IPhase7KnowledgeReferenceNormalizer normalizer, IPhase7KnowledgeReferenceIdentityBridge? bridge = null) { this.normalizer = normalizer; this.bridge = bridge ?? new Phase7KnowledgeReferenceIdentityBridge(normalizer); }

    public IReadOnlyList<Phase7KnowledgeReferenceResolution> Resolve(IReadOnlyList<string> ids, ResolvedNarrationKnowledge knowledge, bool optional = false)
    {
        var claims = knowledge.Domains.SelectMany(x => x.Claims).ToArray();
        return ids.Select(id =>
        {
            if (string.IsNullOrWhiteSpace(id)) return new Phase7KnowledgeReferenceResolution(id, Phase7KnowledgeReferenceStatus.Unsupported, [], "P7REF_BLANK");
            var matches = claims.Where(c => c.ClaimId.Equals(id, StringComparison.OrdinalIgnoreCase) || c.SemanticIdentity.Equals(id, StringComparison.OrdinalIgnoreCase) || c.KnowledgeReferenceIds.Contains(id, StringComparer.OrdinalIgnoreCase)).OrderBy(c => c.ClaimId, StringComparer.Ordinal).ToArray();
            return matches.Length switch { 0 when optional => new(id, Phase7KnowledgeReferenceStatus.Deferred, [], "P7REF_OPTIONAL_DEFERRED"), 0 => new(id, Phase7KnowledgeReferenceStatus.Missing, [], "P7REF_PRIMARY_MISSING"), _ => new(id, Phase7KnowledgeReferenceStatus.Resolved, matches, "P7REF_RESOLVED") };
        }).ToArray();
    }

    public Phase7KnowledgeReferenceResolution Resolve(Phase7KnowledgeReferenceRequest request, Phase7ScenePacketInputAuthority authority)
    {
        var normalized = normalizer.Normalize(request.ReferenceId);
        if (!normalized.IsValid) return new(request.ReferenceId, Phase7KnowledgeReferenceStatus.Unsupported, [], normalized.ReasonCode) { OriginalReferenceId = normalized.OriginalReferenceId };
        var bridged = bridge.Resolve(normalized, authority);
        var claims = authority.Knowledge.KnowledgeAuthority.Claims.Where(c => bridged.CandidateClaimIds.Contains(c.ClaimId, StringComparer.Ordinal)).OrderBy(c => c.ClaimId, StringComparer.Ordinal).ToArray();
        var status = claims.Length > 0 ? Phase7KnowledgeReferenceStatus.Resolved : (request.Optional ? Phase7KnowledgeReferenceStatus.Deferred : Phase7KnowledgeReferenceStatus.Missing);
        var reason = claims.Length > 0 ? bridged.ReasonCode : (request.Optional ? "P7PACKET_REFERENCE_PATH_UNMAPPED" : "P7PACKET_REQUIRED_REFERENCE_UNRESOLVED");
        var eligible = claims.Where(c => Phase7KnowledgeReferenceIdentityBridge.IsRequiredEligible(c, authority)).Select(c => c.ClaimId).Order(StringComparer.Ordinal).ToArray();
        return new(request.ReferenceId, status, claims, reason) { OriginalReferenceId = normalized.OriginalReferenceId, NormalizedReferenceId = normalized.AuthorityNamespace + "#" + normalized.CanonicalJsonPointer, CanonicalJsonPointer = normalized.CanonicalJsonPointer, ResolutionMethod = bridged.ResolutionMethod, MatchedApprovedFieldPaths = bridged.MatchedApprovedFieldPaths, MatchedKnowledgeEntityIds = bridged.MatchedKnowledgeEntityIds, CandidateClaimIds = bridged.CandidateClaimIds, EligibleRequiredClaimIds = eligible, Evidence = bridged.Evidence };
    }
}
