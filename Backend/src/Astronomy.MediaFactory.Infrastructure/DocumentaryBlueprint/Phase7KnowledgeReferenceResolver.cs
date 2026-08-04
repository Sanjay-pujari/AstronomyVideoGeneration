using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7KnowledgeReferenceResolver : IPhase7KnowledgeReferenceResolver
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
        // Governed identities are deliberately ordinal: presentation text and fuzzy matching are never authority.
        var claims = k.Claims.Where(c => c.ClaimId == id || c.SemanticIdentity == id ||
            c.KnowledgeReferenceIds.Contains(id, StringComparer.Ordinal)).ToArray();
        var entity = k.KnowledgeEntities.Any(e => e.KnowledgeId == id);
        if (entity) claims = claims.Concat(k.Claims.Where(c => c.KnowledgeReferenceIds.Contains(id, StringComparer.Ordinal)))
            .DistinctBy(c => c.ClaimId, StringComparer.Ordinal).ToArray();
        if (claims.Length == 0 && request.OtherVariantReferenceIds.Contains(id, StringComparer.Ordinal))
            return new(id, Phase7KnowledgeReferenceStatus.CrossVariantInvalid, [], "P7REF_CROSS_VARIANT_INVALID");
        if (claims.Length == 0)
            return new(id, request.Optional ? Phase7KnowledgeReferenceStatus.Deferred : Phase7KnowledgeReferenceStatus.Missing,
                [], request.Optional ? "P7REF_OPTIONAL_DEFERRED" : "P7REF_REQUIRED_MISSING");
        var incompatible = claims.Select(c => c.SemanticIdentity).Distinct(StringComparer.Ordinal).Count() > 1;
        if (incompatible) return new(id, Phase7KnowledgeReferenceStatus.Ambiguous, claims, "P7REF_AMBIGUOUS");
        return new(id, Phase7KnowledgeReferenceStatus.Resolved, claims, "P7REF_RESOLVED");
    }
}
