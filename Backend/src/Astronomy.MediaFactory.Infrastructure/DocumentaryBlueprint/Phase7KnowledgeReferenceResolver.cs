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
}
