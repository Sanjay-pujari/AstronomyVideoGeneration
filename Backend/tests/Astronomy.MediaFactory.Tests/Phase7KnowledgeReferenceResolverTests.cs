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

public sealed class Phase7KnowledgeReferenceIdentityBridgeTests
{
    [Fact]
    public void Resolve_PrimaryObjectsExactApprovedFieldPathBindsWithoutProseInference()
    {
        var authority = Authority([
            Claim("claim-primary", ["ref.other"]),
            Claim("claim-prose", [])
        ], [Evidence("claim-primary", "/primaryObjects")]);

        var result = new Phase7KnowledgeReferenceResolver().Resolve(
            new Phase7KnowledgeReferenceRequest("production-event-intelligence#/primaryObjects", "Long", false, []), authority);

        Assert.Equal(Phase7KnowledgeReferenceStatus.Resolved, result.Status);
        Assert.Equal("P7PACKET_REFERENCE_RESOLVED_APPROVED_FIELD", result.ReasonCode);
        Assert.Equal("claim-primary", Assert.Single(result.Claims).ClaimId);
    }

    [Fact]
    public void Resolve_PrimaryObjectsDescendantsAreBoundarySafe()
    {
        var authority = Authority([
            Claim("claim-descendant", []), Claim("claim-legacy", []), Claim("claim-secondary", [])
        ], [Evidence("claim-descendant", "/primaryObjects/0/name"), Evidence("claim-legacy", "/primaryObjectsLegacy"), Evidence("claim-secondary", "/secondaryObjects")]);

        var result = new Phase7KnowledgeReferenceResolver().Resolve(
            new Phase7KnowledgeReferenceRequest("production-event-intelligence#/primaryObjects", "Long", false, []), authority);

        Assert.Equal("P7PACKET_REFERENCE_RESOLVED_COLLECTION_DESCENDANT", result.ReasonCode);
        Assert.Equal(["claim-descendant"], result.Claims.Select(x => x.ClaimId).ToArray());
    }

    [Fact]
    public void Resolve_ScientificContextExactAndDescendantPathsBind()
    {
        var authority = Authority([Claim("claim-summary", []), Claim("claim-recognition", [])],
            [Evidence("claim-summary", "/scientificContext"), Evidence("claim-recognition", "/scientificContext/recognition")]);

        var exact = new Phase7KnowledgeReferenceResolver().Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/scientificContext", "Long", false, []), authority);

        Assert.Equal("P7PACKET_REFERENCE_RESOLVED_APPROVED_FIELD", exact.ReasonCode);
        Assert.Equal(["claim-summary"], exact.Claims.Select(x => x.ClaimId).ToArray());
    }

    [Fact]
    public void Resolve_OptionalCandidatesRemainResolvedForRequiredRequest()
    {
        var authority = Authority([Claim("claim-optional", [], Phase7ClaimDisposition.Optional)], [Evidence("claim-optional", "/primaryObjects")]);

        var result = new Phase7KnowledgeReferenceResolver().Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/primaryObjects", "Long", false, []), authority);

        Assert.Equal(Phase7KnowledgeReferenceStatus.Resolved, result.Status);
        Assert.Equal(Phase7ClaimDisposition.Optional, Assert.Single(result.Claims).Disposition);
    }

    private static CertifiedNarrationClaim Claim(string id, IReadOnlyList<string> refs, Phase7ClaimDisposition disposition = Phase7ClaimDisposition.Required) =>
        new(id, "Identity", "Orion prose is not binding evidence", ["source-a"], refs, .9m, false, false, false, false, false, false, false, false, "en", "checksum")
        { Disposition = disposition, SemanticIdentity = "semantic-" + id };

    private static Phase7ClaimSupportEvidence Evidence(string claimId, string path) =>
        new(claimId, "semantic-" + claimId, "source-a", "knowledge-" + claimId, path,
            Phase7ProvenancePrecision.ExactApprovedField, "adapter", Phase7KnowledgeOrigin.Event,
            "test", null, .9m)
        { SourceEligibility = Phase7SourceEligibility.EligibleForRequiredClaim };

    private static Phase7ScenePacketInputAuthority Authority(IReadOnlyList<CertifiedNarrationClaim> claims, IReadOnlyList<Phase7ClaimSupportEvidence> evidence)
    {
        var k = new Phase7KnowledgeAuthority("v", "ka", "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", "p6", "c6", "idx", "ic", "p4", "c4", "p5", "payload", "pc", "Certified", "eg", "egc", "Reviewed", "eg.json", "reg", "rc", [], [], claims, [], evidence, [], [], new(0,0,0,0), [], [], [], [], "checksum", new Dictionary<string,string>());
        var published = new PublishedPhase7KnowledgeAuthority(k, [], new Dictionary<string,string>(), new Dictionary<string,string>(), new Dictionary<string,long>(), [], [], "pub", false, true, true, new Dictionary<string,string>(), new Dictionary<string,string>());
        return new Phase7ScenePacketInputAuthority(published, null!, null!, "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", [], [], [], [], new Dictionary<string,string>(), new Dictionary<string,string>());
    }
}

public sealed class Phase7KnowledgeReferenceNormalizerTests
{
    [Theory]
    [InlineData("production-event-intelligence#/primaryObjects", "production-event-intelligence", "/primaryObjects")]
    [InlineData("production-event-intelligence#/scientificContext", "production-event-intelligence", "/scientificContext")]
    public void Normalize_GovernedReferencesProduceCanonicalPointer(string reference, string ns, string pointer)
    {
        var result = new Phase7KnowledgeReferenceNormalizer().Normalize(reference);
        Assert.True(result.IsValid);
        Assert.Equal(ns, result.AuthorityNamespace);
        Assert.Equal(pointer, result.CanonicalJsonPointer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("production-event-intelligence#primaryObjects")]
    [InlineData("unknown#/primaryObjects")]
    [InlineData("production-event-intelligence#/bad~2escape")]
    public void Normalize_InvalidReferencesFailDeterministically(string reference)
    {
        var result = new Phase7KnowledgeReferenceNormalizer().Normalize(reference);
        Assert.False(result.IsValid);
        Assert.StartsWith("P7REF_", result.ReasonCode);
    }
}

public sealed class Phase7GovernedReferenceResolutionBehaviorTests
{
    [Fact]
    public void Resolve_SharedLongShortReferenceDoesNotCrossVariantFail()
    {
        var authority = MakeAuthority([MakeClaim("claim-primary", [])], [MakeEvidence("claim-primary", "/primaryObjects")]);
        var resolver = new Phase7KnowledgeReferenceResolver();
        var result = resolver.Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/primaryObjects", "Long", false, ["production-event-intelligence#/primaryObjects"]), authority);
        Assert.Equal(Phase7KnowledgeReferenceStatus.Resolved, result.Status);
        Assert.NotEqual("P7REF_CROSS_VARIANT_INVALID", result.ReasonCode);
    }

    [Fact]
    public void Resolve_OptionalOnlyCandidateIsResolvedButNotRequiredEligible()
    {
        var authority = MakeAuthority([MakeClaim("claim-optional", [], Phase7ClaimDisposition.Optional)], [MakeEvidence("claim-optional", "/primaryObjects")]);
        var result = new Phase7KnowledgeReferenceResolver().Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/primaryObjects", "Long", false, []), authority);
        Assert.Equal(["claim-optional"], result.CandidateClaimIds);
        Assert.Empty(result.EligibleRequiredClaimIds);
    }

    [Fact]
    public void Resolve_HumanReviewCandidateIsNotRequiredEligible()
    {
        var claim = MakeClaim("claim-review", []) with { RequiresHumanReview = true };
        var authority = MakeAuthority([claim], [MakeEvidence("claim-review", "/primaryObjects") with { RequiresHumanReview = true }]);
        var result = new Phase7KnowledgeReferenceResolver().Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/primaryObjects", "Long", false, []), authority);
        Assert.Equal(["claim-review"], result.CandidateClaimIds);
        Assert.Empty(result.EligibleRequiredClaimIds);
    }

    [Fact]
    public void Resolve_ReorderedAuthorityProducesIdenticalCandidateOrdering()
    {
        var claims = new[] { MakeClaim("claim-b", []), MakeClaim("claim-a", []) };
        var evidence = new[] { MakeEvidence("claim-b", "/scientificContext/summary"), MakeEvidence("claim-a", "/scientificContext/recognition") };
        var resolver = new Phase7KnowledgeReferenceResolver();
        var first = resolver.Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/scientificContext", "Long", false, []), MakeAuthority(claims, evidence));
        var second = resolver.Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/scientificContext", "Long", false, []), MakeAuthority(claims.Reverse().ToArray(), evidence.Reverse().ToArray()));
        Assert.Equal(first.CandidateClaimIds, second.CandidateClaimIds);
        Assert.Equal(["claim-a", "claim-b"], first.CandidateClaimIds);
    }

    private static CertifiedNarrationClaim MakeClaim(string id, IReadOnlyList<string> refs, Phase7ClaimDisposition disposition = Phase7ClaimDisposition.Required) =>
        new(id, "Identity", "Authority text", ["source-a"], refs, .9m, false, false, false, false, false, false, false, false, "en", "checksum")
        { Disposition = disposition, SemanticIdentity = "semantic-" + id };
    private static Phase7ClaimSupportEvidence MakeEvidence(string claimId, string path) =>
        new(claimId, "semantic-" + claimId, "source-a", "knowledge-" + claimId, path,
            Phase7ProvenancePrecision.ExactApprovedField, "adapter", Phase7KnowledgeOrigin.Event, "test", null, .9m)
        { SourceEligibility = Phase7SourceEligibility.EligibleForRequiredClaim };
    private static Phase7ScenePacketInputAuthority MakeAuthority(IReadOnlyList<CertifiedNarrationClaim> claims, IReadOnlyList<Phase7ClaimSupportEvidence> evidence)
    {
        var k = new Phase7KnowledgeAuthority("v", "ka", "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", "p6", "c6", "idx", "ic", "p4", "c4", "p5", "payload", "pc", "Certified", "eg", "egc", "Reviewed", "eg.json", "reg", "rc", [], [], claims, [], evidence, [], [], new(0,0,0,0), [], [], [], [], "checksum", new Dictionary<string,string>());
        var published = new PublishedPhase7KnowledgeAuthority(k, [], new Dictionary<string,string>(), new Dictionary<string,string>(), new Dictionary<string,long>(), [], [], "pub", false, true, true, new Dictionary<string,string>(), new Dictionary<string,string>());
        return new Phase7ScenePacketInputAuthority(published, null!, null!, "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", [], [], [], [], new Dictionary<string,string>(), new Dictionary<string,string>());
    }
}
