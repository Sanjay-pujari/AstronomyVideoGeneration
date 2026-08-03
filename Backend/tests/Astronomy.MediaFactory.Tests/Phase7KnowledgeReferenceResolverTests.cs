using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7KnowledgeReferenceResolverTests
{
    [Fact]
    public void Resolve_ExactKnowledgeReferenceReturnsOnlyMatchingClaim()
    {
        var claim = new CertifiedNarrationClaim("claim-a", "Identity", "A grounded fact", ["source-a"], ["object.a"], .95m,
            false, false, false, false, false, false, false, false, "en", "checksum");
        var knowledge = new ResolvedNarrationKnowledge("payload", "payload-checksum", "registry", "registry-checksum", "en",
            [new("Identity", KnowledgeDomainStatus.Available, [claim], [])], new Dictionary<string,string>(), [],
            new Dictionary<string,string>(), ["source-a"], [], [], "knowledge-checksum");

        var result = new Phase7KnowledgeReferenceResolver().Resolve(["object.a"], knowledge);

        Assert.Equal(Phase7KnowledgeReferenceStatus.Resolved, Assert.Single(result).Status);
        Assert.Equal("claim-a", Assert.Single(result[0].Claims).ClaimId);
    }

    [Fact]
    public void Resolve_UnresolvedPrimaryIsMissingButOptionalIsDeferred()
    {
        var knowledge = new ResolvedNarrationKnowledge("payload", "checksum", "registry", "registry-checksum", "en", [],
            new Dictionary<string,string>(), [], new Dictionary<string,string>(), [], [], [], "knowledge-checksum");
        var resolver = new Phase7KnowledgeReferenceResolver();
        Assert.Equal(Phase7KnowledgeReferenceStatus.Missing, resolver.Resolve(["missing"], knowledge)[0].Status);
        Assert.Equal(Phase7KnowledgeReferenceStatus.Deferred, resolver.Resolve(["missing"], knowledge, optional:true)[0].Status);
    }
}
