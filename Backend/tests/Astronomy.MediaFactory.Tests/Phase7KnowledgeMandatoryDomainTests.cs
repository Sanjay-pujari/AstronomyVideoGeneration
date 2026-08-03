using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7KnowledgeMandatoryDomainTests
{
    [Fact]
    public void ConstellationProfile_CultureAndAstrologyAreMandatory()
    {
        var result=new FamilyNarrationProfileResolver().Resolve("CONSTELLATION","en");
        Assert.True(result.IsValid);
        Assert.Contains("CultureAndMythology",result.Profile!.MandatoryKnowledgeDomains);
        Assert.Contains("AstrologyClarification",result.Profile.MandatoryKnowledgeDomains);
    }

    [Fact]
    public void CultureMandatoryDomain_WithSafeQualifiedRequiredClaim_Passes()
    {
        var result=Resolve("""{"cultureAndMythology":{"stableKnowledgeId":"constellation.generic","sourceIds":["culture-source"],"greek":{"summary":"In the Greek tradition, this constellation is associated with a named myth."}}}""",
            Source("culture-source","cultureAndMythology.greek.summary"));
        var domain=result.Domains.Single(x=>x.Domain=="CultureAndMythology");
        Assert.Equal(KnowledgeDomainStatus.Available,domain.Status);
        Assert.Contains(domain.Claims,x=>x.Disposition==Phase7ClaimDisposition.Required&&!x.RequiresHumanReview);
    }

    [Fact]
    public void CultureMandatoryDomain_WithOnlyHumanReviewClaim_Fails()
    {
        var result=Resolve("""{"cultureAndMythology":{"stableKnowledgeId":"constellation.generic","sourceIds":["culture-source"],"summary":"A myth is associated with it."}}""",
            Source("culture-source","cultureAndMythology.summary"));
        Assert.Equal(KnowledgeDomainStatus.RequiresHumanReview,result.Domains.Single(x=>x.Domain=="CultureAndMythology").Status);
        Assert.Equal("MissingTraditionIdentity",result.ClaimResolutionDiagnostics.Single().HumanReviewReason);
    }

    [Fact]
    public void AstrologyClarification_WithSafeRequiredClarification_Passes()
    {
        var result=Resolve("""{"astrologyRelationships":{"stableKnowledgeId":"constellation.generic","sourceIds":["astrology-source"],"disclaimer":"Astronomical classification is distinct from traditional astrological association."}}""",
            Source("astrology-source","astrologyRelationships.disclaimer"));
        var domain=result.Domains.Single(x=>x.Domain=="AstrologyClarification");
        Assert.Equal(KnowledgeDomainStatus.Available,domain.Status);
        var diagnostic=Assert.Single(result.ClaimResolutionDiagnostics);
        Assert.Equal("EligibleForRequiredClaim:P7KNOWLEDGE_SOURCE_REQUIRED_ELIGIBLE",diagnostic.SourceEligibility["astrology-source"]);
        Assert.Equal(Phase7ProvenancePrecision.ExactApprovedField,diagnostic.ProvenancePrecision);
    }

    [Theory]
    [InlineData("westernZodiacNotes","This constellation equals a zodiac sign.")]
    [InlineData("indianRashiNotes","This constellation is the same as a Rashi.")]
    [InlineData("nakshatraNotes","This constellation causes scientific influence through a Nakshatra.")]
    public void UnqualifiedEquivalence_RemainsHumanReview(string field,string text)
    {
        var result=Resolve($"{{\"astrologyRelationships\":{{\"stableKnowledgeId\":\"constellation.generic\",\"sourceIds\":[\"astrology-source\"],\"{field}\":\"{text}\"}}}}",
            Source("astrology-source",$"astrologyRelationships.{field}"));
        var claim=Assert.Single(result.Domains.Single(x=>x.Domain=="AstrologyClarification").Claims);
        Assert.Equal(Phase7ClaimDisposition.HumanReview,claim.Disposition);
        Assert.Equal("UnsafeAstrologyEquivalenceOrCausation",Assert.Single(result.ClaimResolutionDiagnostics).HumanReviewReason);
    }

    private static ResolvedNarrationKnowledge Resolve(string json,CertifiedNarrationSource source)
    {
        var profile=new FamilyNarrationProfileResolver().Resolve("CONSTELLATION","en").Profile!;
        var payload=new CertifiedKnowledgePayload("payload","event","CONSTELLATION","CONSTELLATION","en","{}",null,json,"registry",[source.SourceId],"Certified")
        { CertificationStatus="Certified",EvergreenPayloadId="evergreen",AllResolvedSources=[source],CertifiedSupportingSources=[source] };
        return new Phase7KnowledgeResolver().Resolve(payload,profile);
    }

    private static CertifiedNarrationSource Source(string id,string path) =>
        new(id,"reference","Reviewed reference","governing authority","ref",true,true,[],[],[],"en",.95m,"checksum")
        { ReviewState="Approved",AuthorityState="Certified",Disposition="CertifiedSupporting",SupportedApprovedFieldPaths=[path] };
}
