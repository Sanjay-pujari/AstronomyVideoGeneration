using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7CertifiedCulturalSourceMappingTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SectionLevelCultureSupport_DoesNotExpandToEveryCulturalField()
    {
        var culture = Element("""{"greek":{"summary":"Greek account."}}""");
        Assert.Empty(Phase7CulturalSourcePathMapper.Map(Source("section-only"), culture));
    }

    [Fact]
    public void ExactGreekSummarySourceMapping_IsPreserved()
    {
        var culture = Element("""{"greek":{"summary":{"text":"Greek account.","sourceIds":["britannica-orion"]}}}""");
        Assert.Equal(["cultureAndMythology.greek.summary"], Phase7CulturalSourcePathMapper.Map(Source("britannica-orion"), culture));
    }

    [Fact]
    public void TraditionParentSourceIds_AreInheritedBySummaryCandidate()
    {
        var culture = Element("""{"greek":{"summary":"Greek account.","sourceIds":["britannica-orion"]}}""");
        Assert.Equal(["cultureAndMythology.greek.summary"], Phase7CulturalSourcePathMapper.Map(Source("britannica-orion"), culture));
    }

    [Fact]
    public void CultureParentSourceIds_AreInheritedOnlyWhenTraditionSourceIdsAreAbsent()
    {
        var culture = Element("""{"sourceIds":["parent"],"greek":{"summary":"Greek account."},"roman":{"summary":"Roman account.","sourceIds":["roman-source"]}}""");
        Assert.Equal(["cultureAndMythology.greek.summary"], Phase7CulturalSourcePathMapper.Map(Source("parent"), culture));
        Assert.Equal(["cultureAndMythology.roman.summary"], Phase7CulturalSourcePathMapper.Map(Source("roman-source"), culture));
    }

    [Fact]
    public void CandidateWithoutSourceIds_DoesNotBorrowRegistrySource()
    {
        var culture = Element("""{"greek":{"summary":"Greek account."}}""");
        Assert.Empty(Phase7CulturalSourcePathMapper.Map(Source("britannica-orion"), culture));
    }

    [Fact]
    public void BritannicaGreekSummaryFixture_ProducesExactApprovedFieldEvidence()
    {
        var package = Orion();
        var britannica = package.Sources.Single(x => x.SourceId == "britannica-orion");
        Assert.Contains("cultureAndMythology.greek.summary", Phase7CulturalSourcePathMapper.Map(britannica, package.CultureAndMythology));
    }

    [Fact]
    public void UnrelatedIauStarNameSource_DoesNotSupportGreekMythologyClaim()
    {
        var package = Orion();
        var iau = package.Sources.Single(x => x.SourceId == "iau-star-names");
        Assert.DoesNotContain("cultureAndMythology.greek.summary", Phase7CulturalSourcePathMapper.Map(iau, package.CultureAndMythology));
    }

    [Fact]
    public void RequiredCultureSource_MustRemainAuthoritativeAndExact()
    {
        var source = CertifiedOrionSources().Single(x => x.SourceId == "britannica-orion");
        var result = new Phase7SourceEligibilityPolicy().Classify(new(source, "en", "constellation.orion",
            "constellation.orion.cultureAndMythology.greek.summary", "cultureAndMythology.greek.summary", true, false, false));
        Assert.Equal(Phase7SourceEligibility.EligibleForRequiredClaim, result.Eligibility);
        Assert.True(result.Authoritative);
        Assert.Equal(Phase7ProvenancePrecision.ExactApprovedField, result.Precision);
        Assert.Equal("Reviewed", source.ReviewState);
        Assert.Equal("Certified", source.AuthorityState);
    }

    [Fact]
    public void RealOrionFixture_HasAtLeastOneRequiredEligibleCulturalSummary()
    {
        var result = ResolveOrion();
        var diagnostic = result.ClaimResolutionDiagnostics.First(x => x.ApprovedFieldPath == "cultureAndMythology.greek.summary");
        var claim = Culture(result).Claims.Single(x => x.ClaimId == diagnostic.ClaimId);
        Assert.Equal(Phase7ClaimDisposition.Required, diagnostic.Disposition);
        Assert.False(diagnostic.RequiresHumanReview);
        Assert.True(diagnostic.RequiresQualification);
        Assert.Contains("CulturalTraditionQualification", diagnostic.QualificationReasons);
        Assert.Contains(diagnostic.SourceEligibility.Values, x => x.StartsWith(Phase7SourceEligibility.EligibleForRequiredClaim.ToString(), StringComparison.Ordinal));
        Assert.Equal(Phase7ProvenancePrecision.ExactApprovedField, diagnostic.ProvenancePrecision);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.TraditionIdentity));
        Assert.Equal(Phase7ClaimDisposition.Required, claim.Disposition);
    }

    [Fact]
    public void RealOrionFixture_KeepsSensitiveClaimsHumanReview()
    {
        var diagnostics = ResolveOrion().ClaimResolutionDiagnostics.Where(x => x.ApprovedFieldPath.EndsWith("rashiNote") ||
            x.ApprovedFieldPath.EndsWith("nakshatraNote") || x.ApprovedFieldPath.EndsWith("uncertaintyNote") ||
            x.ApprovedFieldPath.Contains(".other.")).ToArray();
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, x => Assert.Equal(Phase7ClaimDisposition.HumanReview, x.Disposition));
    }

    [Fact] public void RealOrionFixture_CultureDomainBecomesAvailable() =>
        Assert.Equal(KnowledgeDomainStatus.Available, Culture(ResolveOrion()).Status);

    [Fact] public void RealOrionFixture_ProceedsBeyondP7INPUT_KNOWLEDGE_PAYLOAD_INVALID() =>
        Assert.DoesNotContain(ResolveOrion().BlockingIssues, x => x.Contains("P7KNOWLEDGE_MANDATORY_DOMAIN_REQUIRESHUMANREVIEW:CultureAndMythology"));

    private static ResolvedNarrationKnowledge ResolveOrion()
    {
        var package = Orion();
        var sources = CertifiedOrionSources();
        var evergreen = JsonSerializer.Serialize(package, Json);
        var payload = new CertifiedKnowledgePayload("payload", "event", "CONSTELLATION", "CONSTELLATION", "en", "{}", null,
            evergreen, "registry", sources.Select(x => x.SourceId).ToArray(), "Certified")
        { CertificationStatus = "Certified", EvergreenPayloadId = package.KnowledgeId, AllResolvedSources = sources, CertifiedSupportingSources = sources };
        return new Phase7KnowledgeResolver().Resolve(payload, new FamilyNarrationProfileResolver().Resolve("CONSTELLATION", "en").Profile!);
    }

    private static IReadOnlyList<CertifiedNarrationSource> CertifiedOrionSources()
    {
        var package = Orion();
        return package.Sources.Select(s => new CertifiedNarrationSource(s.SourceId, s.SourceType, s.Title, s.Authority, s.Reference,
            true, true, package.Objects.Where(o => o.SourceIds.Contains(s.SourceId)).Select(o => o.ObjectId).ToArray(), [],
            s.SupportedSections, "en", s.Confidence == "High" ? .98m : .85m, "checksum")
        {
            SupportedApprovedFieldPaths = Phase7CulturalSourcePathMapper.Map(s, package.CultureAndMythology),
            ReviewState = s.ReviewStatus, AuthorityState = "Certified", Disposition = "CertifiedSupporting"
        }).ToArray();
    }

    private static NarrationKnowledgeDomain Culture(ResolvedNarrationKnowledge result) => result.Domains.Single(x => x.Domain == "CultureAndMythology");
    private static EvergreenKnowledgeSource Source(string id) => new() { SourceId = id, SupportedSections = ["cultureAndMythology"] };
    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static EvergreenAstronomyKnowledgePackage Orion() => JsonSerializer.Deserialize<EvergreenAstronomyKnowledgePackage>(File.ReadAllText(Path.Combine("Knowledge", "Constellations", "Orion", "Orion.v1.json")), Json)!;
}
