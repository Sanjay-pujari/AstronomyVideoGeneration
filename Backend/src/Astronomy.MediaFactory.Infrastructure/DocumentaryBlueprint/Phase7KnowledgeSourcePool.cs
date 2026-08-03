using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>The single governed source population used during resolution and publication.</summary>
public static class Phase7KnowledgeSourcePool
{
    public static IReadOnlyList<CertifiedNarrationSource> Get(CertifiedKnowledgePayload payload) =>
        (payload.AllResolvedSources.Count > 0 ? payload.AllResolvedSources : payload.ReviewedSources)
        .GroupBy(x => x.SourceId, StringComparer.Ordinal)
        .Select(x => x.First()).OrderBy(x => x.SourceId, StringComparer.Ordinal).ToArray();
}
