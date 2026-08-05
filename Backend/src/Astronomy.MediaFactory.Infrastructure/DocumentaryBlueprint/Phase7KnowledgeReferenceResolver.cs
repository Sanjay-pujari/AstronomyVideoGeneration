using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7KnowledgeReferenceResolver : IPhase7KnowledgeReferenceResolver, IPhase7KnowledgeReferenceIdentityBridge
{
    public IReadOnlyList<Phase7KnowledgeReferenceResolution> Resolve(IReadOnlyList<string> ids, ResolvedNarrationKnowledge knowledge, bool optional = false)
    {
        var claims = knowledge.Domains.SelectMany(x => x.Claims).ToArray();
        return ids.Select(id =>
        {
            if (string.IsNullOrWhiteSpace(id)) return new Phase7KnowledgeReferenceResolution(id, Phase7KnowledgeReferenceStatus.Unsupported, [], "P7REF_BLANK");
            var matches = claims.Where(c => c.ClaimId.Equals(id, StringComparison.OrdinalIgnoreCase)
                || c.SemanticIdentity.Equals(id, StringComparison.OrdinalIgnoreCase)
                || c.KnowledgeReferenceIds.Contains(id, StringComparer.OrdinalIgnoreCase)).ToArray();
            return matches.Length switch
            {
                0 when optional => new(id, Phase7KnowledgeReferenceStatus.Deferred, [], "P7REF_OPTIONAL_DEFERRED"),
                0 => new(id, Phase7KnowledgeReferenceStatus.Missing, [], "P7REF_PRIMARY_MISSING"),
                _ => new(id, Phase7KnowledgeReferenceStatus.Resolved, matches, "P7REF_RESOLVED")
            };
        }).ToArray();
    }

    public Phase7KnowledgeReferenceResolution Resolve(Phase7KnowledgeReferenceRequest request,
        Phase7ScenePacketInputAuthority authority)
    {
        var id = request.ReferenceId;
        if (string.IsNullOrWhiteSpace(id) || id.Any(char.IsWhiteSpace))
            return new(id, Phase7KnowledgeReferenceStatus.Unsupported, [], "P7REF_UNSUPPORTED_SHAPE");
        var k = authority.Knowledge.KnowledgeAuthority;
        var claims = ResolveClaims(id, k, out var reason).ToArray();
        if (claims.Length == 0 && request.OtherVariantReferenceIds.Contains(id, StringComparer.Ordinal))
            return new(id, Phase7KnowledgeReferenceStatus.CrossVariantInvalid, [], "P7REF_CROSS_VARIANT_INVALID");
        if (claims.Length == 0)
            return new(id, request.Optional ? Phase7KnowledgeReferenceStatus.Deferred : Phase7KnowledgeReferenceStatus.Missing,
                [], request.Optional ? "P7PACKET_REFERENCE_PATH_UNMAPPED" : reason);
        return new(id, Phase7KnowledgeReferenceStatus.Resolved, claims, reason);
    }

    public Phase7KnowledgeReferenceIdentityBridgeResult Resolve(string phase6ReferenceId, PublishedPhase7KnowledgeAuthority authority)
    {
        var claims = ResolveClaims(phase6ReferenceId, authority.KnowledgeAuthority, out var reason).ToArray();
        var claimIds = claims.Select(x => x.ClaimId).Order(StringComparer.Ordinal).ToArray();
        var paths = PathsForClaims(authority.KnowledgeAuthority, claimIds).Order(StringComparer.Ordinal).ToArray();
        var entities = EntitiesForClaims(authority.KnowledgeAuthority, claimIds).Order(StringComparer.Ordinal).ToArray();
        var pointer = NormalizePointer(phase6ReferenceId);
        var evidence = new[] { $"sourceReferenceId={phase6ReferenceId}", $"normalizedPointer={pointer}", $"resolutionMethod={reason}" }
            .Concat(paths.Select(x => $"matchedApprovedFieldPath={x}"))
            .Concat(entities.Select(x => $"matchedKnowledgeEntityId={x}"))
            .Concat(claimIds.Select(x => $"candidateClaimId={x}"))
            .ToArray();
        return new(claims.Length > 0, phase6ReferenceId, CanonicalIds(phase6ReferenceId).ToArray(), paths, entities,
            claimIds, claims.Length > 0 ? reason : "P7PACKET_REFERENCE_PATH_UNMAPPED", evidence);
    }

    private static IEnumerable<CertifiedNarrationClaim> ResolveClaims(string id, Phase7KnowledgeAuthority k, out string reason)
    {
        var canonical = CanonicalIds(id).ToArray();
        var exactIds = k.Claims.Where(c => c.ClaimId == id || c.SemanticIdentity == id || c.KnowledgeReferenceIds.Intersect(canonical, StringComparer.Ordinal).Any());
        if (exactIds.Any()) { reason = "P7PACKET_REFERENCE_RESOLVED_EXACT_ID"; return exactIds.OrderBy(c => c.ClaimId, StringComparer.Ordinal); }
        var pointer = NormalizePointer(id);
        var exactPaths = ClaimIdsForApprovedPath(k, pointer, descendant: false).ToArray();
        if (exactPaths.Length > 0) { reason = "P7PACKET_REFERENCE_RESOLVED_APPROVED_FIELD"; return Claims(k, exactPaths); }
        var descendants = ClaimIdsForApprovedPath(k, pointer, descendant: true).ToArray();
        if (descendants.Length > 0) { reason = "P7PACKET_REFERENCE_RESOLVED_COLLECTION_DESCENDANT"; return Claims(k, descendants); }
        var entityClaims = k.KnowledgeEntities.Where(e => canonical.Contains(e.KnowledgeId, StringComparer.Ordinal)).Select(e => e.KnowledgeId)
            .Concat(k.ClaimSupportEvidence.Where(e => canonical.Contains(e.KnowledgeId, StringComparer.Ordinal)).Select(e => e.ClaimId))
            .Concat(k.Claims.Where(c => c.KnowledgeReferenceIds.Intersect(canonical, StringComparer.Ordinal).Any()).Select(c => c.ClaimId));
        var entityIds = entityClaims.Distinct(StringComparer.Ordinal).ToArray();
        if (entityIds.Length > 0) { reason = "P7PACKET_REFERENCE_RESOLVED_KNOWLEDGE_ENTITY"; return Claims(k, entityIds); }
        reason = "P7PACKET_REFERENCE_PATH_UNMAPPED"; return [];
    }

    private static IEnumerable<string> ClaimIdsForApprovedPath(Phase7KnowledgeAuthority k, string pointer, bool descendant)
    {
        bool Match(string p) => descendant ? IsDescendant(pointer, NormalizePointer(p)) : NormalizePointer(p) == pointer;
        return k.ClaimSupportEvidence.Where(e => Match(e.ApprovedFieldPath)).Select(e => e.ClaimId)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
    }

    private static IEnumerable<CertifiedNarrationClaim> Claims(Phase7KnowledgeAuthority k, IEnumerable<string> ids) =>
        k.Claims.Where(c => ids.Contains(c.ClaimId, StringComparer.Ordinal)).OrderBy(c => c.ClaimId, StringComparer.Ordinal);
    private static IEnumerable<string> PathsForClaims(Phase7KnowledgeAuthority k, IReadOnlyList<string> ids) =>
        k.ClaimSupportEvidence.Where(e => ids.Contains(e.ClaimId, StringComparer.Ordinal)).Select(e => e.ApprovedFieldPath).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal);
    private static IEnumerable<string> EntitiesForClaims(Phase7KnowledgeAuthority k, IReadOnlyList<string> ids) =>
        k.ClaimSupportEvidence.Where(e => ids.Contains(e.ClaimId, StringComparer.Ordinal)).Select(e => e.KnowledgeId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal);
    private static IEnumerable<string> CanonicalIds(string id) { yield return id; var p = NormalizePointer(id); yield return p; yield return "#" + p; yield return p.TrimStart('/'); yield return "Event:" + p.TrimStart('/'); yield return "event." + p.TrimStart('/').Replace('/', '.'); }
    private static string NormalizePointer(string id) { var s = id; var hash = s.IndexOf('#'); if (hash >= 0) s = s[(hash + 1)..]; if (s.StartsWith("Event:", StringComparison.Ordinal)) s = "/" + s[6..]; if (s.StartsWith("event.", StringComparison.Ordinal)) s = "/" + s[6..].Replace('.', '/'); if (!s.StartsWith('/')) s = "/" + s; return s; }
    private static bool IsDescendant(string parent, string path) => path.Length > parent.Length && path.StartsWith(parent, StringComparison.Ordinal) && path[parent.Length] == '/';
}
