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
        Assert.Equal("P7PACKET_REFERENCE_RESOLVED_AUTHORITY_APPROVED_FIELD", result.ReasonCode);
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

        Assert.Equal("P7PACKET_REFERENCE_RESOLVED_AUTHORITY_DESCENDANT", result.ReasonCode);
        Assert.Equal(["claim-descendant"], result.Claims.Select(x => x.ClaimId).ToArray());
    }

    [Fact]
    public void Resolve_ScientificContextExactAndDescendantPathsBind()
    {
        var authority = Authority([Claim("claim-summary", []), Claim("claim-recognition", [])],
            [Evidence("claim-summary", "/scientificContext"), Evidence("claim-recognition", "/scientificContext/recognition")]);

        var exact = new Phase7KnowledgeReferenceResolver().Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/scientificContext", "Long", false, []), authority);

        Assert.Equal("P7PACKET_REFERENCE_RESOLVED_AUTHORITY_APPROVED_FIELD", exact.ReasonCode);
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

public sealed class Phase7ApprovedFieldPathCanonicalizerTests
{
    [Theory]
    [InlineData("primaryObjects", "/primaryObjects")]
    [InlineData("scientificContext", "/scientificContext")]
    [InlineData("Event:primaryObjects", "/primaryObjects")]
    [InlineData("Event:/scientificContext", "/scientificContext")]
    [InlineData("production-event-intelligence#/primaryObjects", "/primaryObjects")]
    public void Canonicalize_CommittedOrionFormatsPreservesCanonicalPointer(string rawPath, string canonicalPointer)
    {
        var result = new Phase7ApprovedFieldPathCanonicalizer().Canonicalize(rawPath, Phase7KnowledgeOrigin.Event);

        Assert.True(result.IsValid);
        Assert.Equal(canonicalPointer, result.CanonicalJsonPointer);
        Assert.Equal(Phase7KnowledgeOrigin.Event, result.Origin);
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


public sealed class Phase7RealAuthorityEvidenceReconciliationTests
{
    [Fact]
    public void RealCommittedShape_PrimaryObjectsAndScientificContextResolveThroughProductionResolver()
    {
        var authority = RealCommittedShapeAuthority();
        var resolver = new Phase7KnowledgeReferenceResolver(new Phase7KnowledgeReferenceNormalizer(),
            new Phase7KnowledgeReferenceIdentityBridge(new Phase7KnowledgeReferenceNormalizer(),
                new Phase7CommittedClaimEvidenceIndexBuilder(new Phase7ApprovedFieldPathCanonicalizer())));

        var primary = resolver.Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/primaryObjects", "Long", false, []), authority);
        var scientific = resolver.Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/scientificContext", "Long", false, []), authority);

        Assert.Equal(Phase7KnowledgeReferenceStatus.Resolved, primary.Status);
        Assert.NotEmpty(primary.CandidateClaimIds);
        Assert.Contains("primaryObjects", primary.MatchedApprovedFieldPaths);
        Assert.Equal("P7PACKET_REFERENCE_RESOLVED_AUTHORITY_APPROVED_FIELD", primary.ResolutionMethod);
        Assert.Equal(Phase7KnowledgeReferenceStatus.Resolved, scientific.Status);
        Assert.NotEmpty(scientific.CandidateClaimIds);
        Assert.Contains("scientificContext", scientific.MatchedApprovedFieldPaths);
        Assert.Equal("P7PACKET_REFERENCE_RESOLVED_AUTHORITY_APPROVED_FIELD", scientific.ResolutionMethod);
    }

    [Fact]
    public void ReconciliationPrecedence_AuthorityResolutionAndBothPathsAreDeterministic()
    {
        var authority = RealCommittedShapeAuthority(includeResolutionOnly:true);
        var resolver = new Phase7KnowledgeReferenceResolver();
        Assert.Equal("P7PACKET_REFERENCE_RESOLVED_AUTHORITY_APPROVED_FIELD", resolver.Resolve(new("production-event-intelligence#/primaryObjects", "Long", false, []), authority).ResolutionMethod);
        Assert.Equal("P7PACKET_REFERENCE_RESOLVED_RESOLUTION_APPROVED_FIELD", resolver.Resolve(new("production-event-intelligence#/resolutionOnly", "Long", false, []), authority).ResolutionMethod);
        Assert.Equal("P7PACKET_REFERENCE_RESOLVED_AUTHORITY_APPROVED_FIELD", resolver.Resolve(new("production-event-intelligence#/both", "Long", false, []), authority).ResolutionMethod);
    }

    [Fact]
    public void NoSyntheticOnlySuccess_RequiresCommittedShapeResolutionReportFixture()
    {
        var synthetic = MakeAuthority([MakeClaim("claim-synthetic", [])], [MakeEvidence("claim-synthetic", "/primaryObjects")]);
        Assert.Null(synthetic.Knowledge.ResolvedNarrationKnowledge);
        var result = new Phase7KnowledgeReferenceResolver().Resolve(new("production-event-intelligence#/primaryObjects", "Long", false, []), synthetic);
        Assert.Equal(Phase7KnowledgeReferenceStatus.Resolved, result.Status);
        Assert.DoesNotContain(result.Evidence, x => x.StartsWith("matchingResolutionDiagnosticId=", StringComparison.Ordinal));
    }

    private static Phase7ScenePacketInputAuthority MakeAuthority(IReadOnlyList<CertifiedNarrationClaim> claims, IReadOnlyList<Phase7ClaimSupportEvidence> evidence)
    {
        var k = new Phase7KnowledgeAuthority("v", "ka", "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", "p6", "c6", "idx", "ic", "p4", "c4", "p5", "payload", "pc", "Certified", "eg", "egc", "Reviewed", "eg.json", "reg", "rc", [], [], claims, [], evidence, [], [], new(0,0,0,0), [], [], [], [], "checksum", new Dictionary<string,string>());
        var published = new PublishedPhase7KnowledgeAuthority(k, [], new Dictionary<string,string>(), new Dictionary<string,string>(), new Dictionary<string,long>(), [], [], "pub", false, true, true, new Dictionary<string,string>(), new Dictionary<string,string>());
        return new Phase7ScenePacketInputAuthority(published, null!, null!, "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", [], [], [], [], new Dictionary<string,string>(), new Dictionary<string,string>());
    }

    private static Phase7ScenePacketInputAuthority RealCommittedShapeAuthority(bool includeResolutionOnly = false)
    {
        var claims = new[] { MakeClaim("orion-claim-primary-objects", []), MakeClaim("orion-claim-scientific-context", []), MakeClaim("orion-claim-both", []) }
            .Concat(includeResolutionOnly ? [MakeClaim("orion-claim-resolution-only", [])] : Array.Empty<CertifiedNarrationClaim>()).ToArray();
        var evidence = new[] { MakeEvidence("orion-claim-primary-objects", "primaryObjects"), MakeEvidence("orion-claim-scientific-context", "scientificContext"), MakeEvidence("orion-claim-both", "/both") };
        var resolutionEvidence = includeResolutionOnly ? [MakeEvidence("orion-claim-resolution-only", "/resolutionOnly"), MakeEvidence("orion-claim-both", "/both")] : Array.Empty<Phase7ClaimSupportEvidence>();
        var diagnostics = includeResolutionOnly
            ? [Diag("orion-claim-resolution-only", "/resolutionOnly"), Diag("orion-claim-both", "/both")]
            : new[] { Diag("orion-claim-primary-objects", "/primaryObjects"), Diag("orion-claim-scientific-context", "/scientificContext") };
        var k = new Phase7KnowledgeAuthority("v", "ka", "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", "p6", "c6", "idx", "ic", "p4", "c4", "p5", "payload", "pc", "Certified", "eg", "egc", "Reviewed", "eg.json", "reg", "rc", [], [], claims, [], evidence, [], [], new(0,0,0,0), [], [], [], [], "checksum", new Dictionary<string,string>());
        var r = new ResolvedNarrationKnowledge("payload", "pc", "reg", "rc", "en", [new("Identity", KnowledgeDomainStatus.Available, claims, [])], new Dictionary<string,string>(), [], new Dictionary<string,string>(), ["source-a"], [], [], "knowledge-checksum") { ClaimSupportEvidence = resolutionEvidence, ClaimResolutionDiagnostics = diagnostics };
        return new Phase7ScenePacketInputAuthority(new PublishedPhase7KnowledgeAuthority(k, [], new Dictionary<string,string>(), new Dictionary<string,string>(), new Dictionary<string,long>(), [], [], "pub", false, true, true, new Dictionary<string,string>(), new Dictionary<string,string>()) { ResolvedNarrationKnowledge = r }, null!, null!, "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", [], [], [], [], new Dictionary<string,string>(), new Dictionary<string,string>());
    }
    private static CertifiedNarrationClaim MakeClaim(string id, IReadOnlyList<string> refs) => new(id, "Identity", "Sanitized committed-shape Orion evidence", ["source-a"], refs, .9m, false, false, false, false, false, false, false, false, "en", "checksum") { Disposition = Phase7ClaimDisposition.Required, SemanticIdentity = "semantic-" + id };
    private static Phase7ClaimSupportEvidence MakeEvidence(string claimId, string path) => new(claimId, "semantic-" + claimId, "source-a", "knowledge-" + claimId, path, Phase7ProvenancePrecision.ExactApprovedField, "orion-adapter", Phase7KnowledgeOrigin.Event, "CommittedFixture", null, .9m) { SourceEligibility = Phase7SourceEligibility.EligibleForRequiredClaim, AdapterVersion = "fixture" };
    private static Phase7ClaimResolutionDiagnostic Diag(string claimId, string path) => new("Identity", true, "candidate-" + claimId, "semantic-" + claimId, path, "value", Phase7ClaimDisposition.Required, false, "", false, [], ["source-a"], new Dictionary<string,string>{{"source-a","EligibleForRequiredClaim"}}, Phase7ProvenancePrecision.ExactApprovedField, "Resolved") { ClaimId = claimId, KnowledgeEntityId = "knowledge-" + claimId, Origin = Phase7KnowledgeOrigin.Event };
}

public sealed class Phase7CompatibilityScopeRealShapeTests
{
    [Theory]
    [InlineData("production-event-intelligence#/primaryObjects", "Wonder", "orion-importance")]
    [InlineData("production-event-intelligence#/primaryObjects", "Discovery", "orion-belt")]
    [InlineData("production-event-intelligence#/primaryObjects", "Observation", "orion-binoculars")]
    [InlineData("production-event-intelligence#/primaryObjects", "History", "orion-history")]
    [InlineData("production-event-intelligence#/scientificContext", "Recognition", "orion-position")]
    [InlineData("production-event-intelligence#/scientificContext", "Clarification", "orion-zodiac")]
    public void FrozenPhase6BroadReferencesResolveGovernedSectionScopes(string referenceId, string sectionKey, string expectedClaimId)
    {
        var resolver = new Phase7KnowledgeReferenceResolver();
        var result = resolver.Resolve(new Phase7KnowledgeReferenceRequest(referenceId, "Long", false, []) { SectionKey = sectionKey }, Authority());

        Assert.Equal(Phase7KnowledgeReferenceStatus.Resolved, result.Status);
        Assert.Equal(expectedClaimId, Assert.Single(result.CandidateClaimIds));
        Assert.Equal([expectedClaimId], result.EligibleRequiredClaimIds);
        Assert.StartsWith("P7PACKET_REFERENCE_COMPAT_PHASE6_", result.ResolutionMethod);
    }

    [Fact]
    public void CompatibilityScopePreservesDeterministicAuthorityIdentitiesAndVariantIndependence()
    {
        var resolver = new Phase7KnowledgeReferenceResolver();
        var authority = Authority();
        var longResult = resolver.Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/primaryObjects", "Long", false, []) { SectionKey = "Discovery" }, authority);
        var shortResult = resolver.Resolve(new Phase7KnowledgeReferenceRequest("production-event-intelligence#/scientificContext", "Short", false, []) { SectionKey = "Recognition" }, authority);

        Assert.Equal(["orion-belt"], longResult.CandidateClaimIds);
        Assert.Equal(["orion-position"], shortResult.CandidateClaimIds);
        Assert.Same(authority.Knowledge.KnowledgeAuthority.Claims.Single(c => c.ClaimId == "orion-belt"), Assert.Single(longResult.Claims));
        Assert.Same(authority.Knowledge.KnowledgeAuthority.Claims.Single(c => c.ClaimId == "orion-position"), Assert.Single(shortResult.Claims));
    }

    private static Phase7ScenePacketInputAuthority Authority()
    {
        var claims = new[]
        {
            Claim("orion-importance", "ScientificSignificance"),
            Claim("orion-position", "PhysicalCharacteristics"),
            Claim("orion-belt", "KeyObjects"),
            Claim("orion-binoculars", "Observation"),
            Claim("orion-history", "History"),
            Claim("orion-zodiac", "AstrologyClarification")
        };
        var evidence = new[]
        {
            Evidence("orion-importance", "ScientificSignificance", "scientific.astronomicalImportance"),
            Evidence("orion-position", "PhysicalCharacteristics", "scientific.approximatePosition"),
            Evidence("orion-belt", "KeyObjects", "scientific.orionBeltStars"),
            Evidence("orion-binoculars", "Observation", "observation.binocularGuidance"),
            Evidence("orion-history", "History", "history.historicalCataloguing"),
            Evidence("orion-zodiac", "AstrologyClarification", "astrologyRelationships.westernZodiacNotes")
        };
        var k = new Phase7KnowledgeAuthority("v", "ka", "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", "p6", "c6", "idx", "ic", "p4", "c4", "p5", "payload", "pc", "Certified", "eg", "egc", "Reviewed", "eg.json", "reg", "rc", [], [], claims, [], evidence, [], [], new(0,0,0,0), [], [], [], [], "checksum", new Dictionary<string,string>());
        var published = new PublishedPhase7KnowledgeAuthority(k, [], new Dictionary<string,string>(), new Dictionary<string,string>(), new Dictionary<string,long>(), [], [], "pub", false, true, true, new Dictionary<string,string>(), new Dictionary<string,string>());
        return new Phase7ScenePacketInputAuthority(published, null!, null!, "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", [], [], [], [], new Dictionary<string,string>(), new Dictionary<string,string>());
    }

    private static CertifiedNarrationClaim Claim(string id, string domain) =>
        new(id, domain, "Sanitized real-shape Orion claim", ["source-a"], [], .9m, false, false, false, false, false, false, false, false, "en", "checksum")
        { Disposition = Phase7ClaimDisposition.Required, SemanticIdentity = "semantic-" + id };

    private static Phase7ClaimSupportEvidence Evidence(string claimId, string domain, string path) =>
        new(claimId, "semantic-" + claimId, "source-a", "knowledge-" + claimId, path,
            Phase7ProvenancePrecision.ExactApprovedField, "orion-adapter", Phase7KnowledgeOrigin.Event, domain, null, .9m)
        { SourceEligibility = Phase7SourceEligibility.EligibleForRequiredClaim };
}

public sealed class Phase7SectionAwareRequiredClaimSelectionRegressionTests
{
    private const string OptionalId = "claim-26af7d31e9d9421e1988df56";

    [Theory]
    [InlineData("Wonder", "claim-identity")]
    [InlineData("Science", "claim-summary")]
    [InlineData("ModernAstronomy", "claim-modern")]
    [InlineData("Inspiration", "claim-identity")]
    [InlineData("Culture", "claim-culture")]
    public void PrimaryObjects_SectionScope_SelectsRequiredBeforeOptionalScientificSignificance(string section, string expectedPrimary)
    {
        var result = Resolve("production-event-intelligence#/primaryObjects", section, "Long", Reordered:false);

        Assert.Equal(Phase7KnowledgeReferenceStatus.Resolved, result.Status);
        Assert.Equal(expectedPrimary, result.CandidateClaimIds[0]);
        Assert.Contains(expectedPrimary, result.EligibleRequiredClaimIds);
        Assert.DoesNotContain(OptionalId, result.EligibleRequiredClaimIds);
    }

    [Theory]
    [InlineData("Long")]
    [InlineData("Short")]
    public void ScientificContext_Recognition_ResolvesRequiredRecognitionClaims(string variant)
    {
        var result = Resolve("production-event-intelligence#/scientificContext", "Recognition", variant, Reordered:false);

        Assert.Equal(Phase7KnowledgeReferenceStatus.Resolved, result.Status);
        Assert.StartsWith("claim-", result.CandidateClaimIds[0]);
        Assert.NotEmpty(result.EligibleRequiredClaimIds);
        Assert.DoesNotContain(OptionalId, result.EligibleRequiredClaimIds);
    }

    [Fact]
    public void SectionAwareSelection_IsDeterministicUnderReorderedInput()
    {
        var first = Resolve("production-event-intelligence#/primaryObjects", "Science", "Long", Reordered:false);
        var second = Resolve("production-event-intelligence#/primaryObjects", "Science", "Long", Reordered:true);

        Assert.Equal(first.CandidateClaimIds, second.CandidateClaimIds);
        Assert.Equal(first.EligibleRequiredClaimIds, second.EligibleRequiredClaimIds);
    }

    [Theory]
    [InlineData("Wonder")]
    [InlineData("Science")]
    [InlineData("ModernAstronomy")]
    [InlineData("Inspiration")]
    public void OptionalScientificSignificance_CannotBecomeSoleRequiredCandidate(string section)
    {
        var result = Resolve("production-event-intelligence#/primaryObjects", section, "Long", Reordered:false);

        Assert.NotEqual([OptionalId], result.EligibleRequiredClaimIds);
        Assert.DoesNotContain(OptionalId, result.EligibleRequiredClaimIds);
        Assert.Equal(Phase7ClaimDisposition.Optional, Claims().Single(c => c.ClaimId == OptionalId).Disposition);
    }

    private static Phase7KnowledgeReferenceResolution Resolve(string reference, string section, string variant, bool Reordered)
    {
        var claims = Claims();
        var evidence = Evidence();
        if (Reordered) { Array.Reverse(claims); Array.Reverse(evidence); }
        return new Phase7KnowledgeReferenceResolver().Resolve(new Phase7KnowledgeReferenceRequest(reference, variant, false, []) { SectionKey = section }, Authority(claims, evidence));
    }

    private static CertifiedNarrationClaim[] Claims() =>
    [
        Claim(OptionalId, "ScientificSignificance", Phase7ClaimDisposition.Optional, .99m),
        Claim("claim-identity", "Identity", Phase7ClaimDisposition.Required, .95m),
        Claim("claim-summary", "ScientificStructure", Phase7ClaimDisposition.Required, .94m),
        Claim("claim-major-stars", "ScientificStructure", Phase7ClaimDisposition.Required, .93m),
        Claim("claim-belt-stars", "ScientificStructure", Phase7ClaimDisposition.Required, .92m),
        Claim("claim-object-name", "Identity", Phase7ClaimDisposition.Required, .91m),
        Claim("claim-naked-eye", "Recognition", Phase7ClaimDisposition.Required, .90m),
        Claim("claim-belt-id", "Recognition", Phase7ClaimDisposition.Required, .89m),
        Claim("claim-modern", "History", Phase7ClaimDisposition.Required, .88m),
        Claim("claim-culture", "CultureAndMythology", Phase7ClaimDisposition.Required, .87m)
    ];

    private static Phase7ClaimSupportEvidence[] Evidence() =>
    [
        Ev(OptionalId, "scientific.astronomicalImportance", Phase7SourceEligibility.EligibleForOptionalClaim),
        Ev("claim-identity", "identity.canonicalSubject"),
        Ev("claim-summary", "scientific.summary"),
        Ev("claim-major-stars", "scientific.majorStars"),
        Ev("claim-belt-stars", "scientific.orionBeltStars"),
        Ev("claim-object-name", "objects.objectName"),
        Ev("claim-naked-eye", "observation.nakedEyeRecognition"),
        Ev("claim-belt-id", "observation.orionBeltIdentification"),
        Ev("claim-modern", "history.modernInterpretation"),
        Ev("claim-culture", "cultureAndMythology.grecoRoman")
    ];

    private static CertifiedNarrationClaim Claim(string id, string domain, Phase7ClaimDisposition disposition, decimal confidence) =>
        new(id, domain, "Sanitized Orion authority claim", ["source-a"], [], confidence, false, false, false, false, false, false, false, false, "en", "checksum")
        { Disposition = disposition, SemanticIdentity = "semantic-" + id };

    private static Phase7ClaimSupportEvidence Ev(string claimId, string path, Phase7SourceEligibility eligibility = Phase7SourceEligibility.EligibleForRequiredClaim) =>
        new(claimId, "semantic-" + claimId, "source-a", "knowledge-" + claimId, path,
            Phase7ProvenancePrecision.ExactApprovedField, "adapter", Phase7KnowledgeOrigin.Event, "test", null, .9m)
        { SourceEligibility = eligibility };

    private static Phase7ScenePacketInputAuthority Authority(IReadOnlyList<CertifiedNarrationClaim> claims, IReadOnlyList<Phase7ClaimSupportEvidence> evidence)
    {
        var k = new Phase7KnowledgeAuthority("v", "ka", "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", "p6", "c6", "idx", "ic", "p4", "c4", "p5", "payload", "pc", "Certified", "eg", "egc", "Reviewed", "eg.json", "reg", "rc", [], [], claims, [], evidence, [], [], new(0,0,0,0), [], [], [], [], "checksum", new Dictionary<string,string>());
        var published = new PublishedPhase7KnowledgeAuthority(k, [], new Dictionary<string,string>(), new Dictionary<string,string>(), new Dictionary<string,long>(), [], [], "pub", false, true, true, new Dictionary<string,string>(), new Dictionary<string,string>());
        return new Phase7ScenePacketInputAuthority(published, null!, null!, "ex", "pl", "ev", "fam", "type", "en", "profile", "v1", [], [], [], [], new Dictionary<string,string>(), new Dictionary<string,string>());
    }
}
