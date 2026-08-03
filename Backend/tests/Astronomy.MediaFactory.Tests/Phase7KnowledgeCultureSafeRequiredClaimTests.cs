using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7KnowledgeCultureSafeRequiredClaimTests
{
    [Fact] public void QualifiedCulturalClaim_DoesNotAutomaticallyRequireHumanReview()
    {
        var claim=Required(Safe());
        Assert.True(claim.RequiresQualification);
        Assert.False(claim.RequiresHumanReview);
    }

    [Fact] public void SafeTraditionScopedClaim_WithExactRequiredEvidence_BecomesRequired() =>
        Assert.Equal(Phase7ClaimDisposition.Required,Required(Safe()).Disposition);

    [Fact] public void SafeRequiredCulturalClaim_SatisfiesMandatoryDomain() => AssertAvailable(Safe());

    [Fact] public void MandatoryCultureDomain_WithRequiredAndHumanReviewClaims_Passes() =>
        AssertAvailable(Resolve(Body("\"greek\":{\"summary\":\"In Greek tradition, the constellation is associated with a named myth.\"},\"indianHindu\":{\"rashiNote\":\"A tradition-specific association.\"}"),
            [Source("culture-source","cultureAndMythology.greek.summary")]));

    [Fact] public void SensitiveCulturalAssociation_RemainsHumanReview() => AssertReview("rashiNote","SensitiveCulturalAssociationRequiresReview");
    [Fact] public void UncategorisedTradition_RemainsHumanReview() => AssertReview("summary","UncategorisedTraditionRequiresReview","other");
    [Fact] public void UnresolvedCulturalUncertainty_RemainsHumanReview() => AssertReview("uncertaintyNote","UnresolvedCulturalUncertainty","greek");
    [Fact] public void RashiClaim_IsNotPromotedToRequired() => AssertReview("rashiNote","SensitiveCulturalAssociationRequiresReview");
    [Fact] public void NakshatraClaim_IsNotPromotedToRequired() => AssertReview("nakshatraNote","SensitiveCulturalAssociationRequiresReview");

    [Fact] public void ReviewedNonCertifiedEvidence_CannotProduceRequiredCulturalClaim()
    {
        var reviewed=Source("reviewed","cultureAndMythology.greek.summary") with { Certified=false,AuthorityState="Reviewed" };
        Assert.DoesNotContain(Culture(Resolve(Body("\"greek\":{\"summary\":\"Greek tradition account.\"}"),[reviewed])).Claims,c=>c.Disposition==Phase7ClaimDisposition.Required);
    }

    [Fact] public void RequiredCulturalClaim_MustHaveTraditionIdentity()
    {
        var result=Resolve("{\"cultureAndMythology\":{\"stableKnowledgeId\":\"constellation.generic\",\"summary\":\"An unscoped myth.\",\"sourceIds\":[\"culture-source\"]}}",
            [Source("culture-source","cultureAndMythology.summary")]);
        Assert.Equal("MissingTraditionIdentity",Assert.Single(result.ClaimResolutionDiagnostics).HumanReviewReason);
    }

    [Fact] public void RequiredCulturalClaim_MustBeQualified() => Assert.True(Required(Safe()).RequiresQualification);

    [Fact] public void RequiredCulturalClaim_MustHaveExactProvenance()
    {
        var result=Resolve(Body("\"greek\":{\"summary\":\"Greek tradition account.\"}"),[Source("wrong","history.summary")]);
        Assert.DoesNotContain(Culture(result).Claims,c=>c.Disposition==Phase7ClaimDisposition.Required);
    }

    [Fact] public void RealConstellationFixture_HasOneSafeRequiredCulturalClaim() => Assert.Single(Culture(RealFixture()).Claims.Where(c=>c.Disposition==Phase7ClaimDisposition.Required));
    [Fact] public void RealConstellationFixture_PreservesSensitiveClaimsAsHumanReview() => Assert.Equal(4,Culture(RealFixture()).Claims.Count(c=>c.Disposition==Phase7ClaimDisposition.HumanReview));
    [Fact] public void RealConstellationFixture_PassesCultureDomainGate() => AssertAvailable(RealFixture());

    private static ResolvedNarrationKnowledge RealFixture() => Resolve(Body("\"greek\":{\"summary\":\"Often associated with a hunter in Greek myth.\"},\"indianHindu\":{\"rashiNote\":\"Not a Rashi.\",\"nakshatraNote\":\"Mappings vary.\",\"uncertaintyNote\":\"Avoid a single equivalence.\"},\"other\":{\"summary\":\"Many cultures recognized the pattern.\"}"),
        [Source("culture-source","cultureAndMythology.greek.summary")]);
    private static ResolvedNarrationKnowledge Safe() => Resolve(Body("\"greek\":{\"summary\":\"In Greek tradition, the constellation is associated with a named myth.\"}"),[Source("culture-source","cultureAndMythology.greek.summary")]);
    private static CertifiedNarrationClaim Required(ResolvedNarrationKnowledge result) => Assert.Single(Culture(result).Claims.Where(c=>c.Disposition==Phase7ClaimDisposition.Required));
    private static void AssertAvailable(ResolvedNarrationKnowledge result) => Assert.Equal(KnowledgeDomainStatus.Available,Culture(result).Status);
    private static NarrationKnowledgeDomain Culture(ResolvedNarrationKnowledge result) => result.Domains.Single(d=>d.Domain=="CultureAndMythology");
    private static void AssertReview(string field,string reason,string tradition="indianHindu")
    {
        var result=Resolve(Body($"\"{tradition}\":{{\"{field}\":\"A governed cultural statement.\"}}"),[]);
        Assert.Equal(Phase7ClaimDisposition.HumanReview,Assert.Single(Culture(result).Claims).Disposition);
        Assert.Equal(reason,Assert.Single(result.ClaimResolutionDiagnostics).HumanReviewReason);
    }
    private static string Body(string children) => $"{{\"cultureAndMythology\":{{\"stableKnowledgeId\":\"constellation.generic\",\"sourceIds\":[\"culture-source\"],{children}}}}}";
    private static ResolvedNarrationKnowledge Resolve(string evergreen,IReadOnlyList<CertifiedNarrationSource> sources)
    {
        var payload=new CertifiedKnowledgePayload("payload","event","CONSTELLATION","CONSTELLATION","en","{}",null,evergreen,"registry",sources.Select(s=>s.SourceId).ToArray(),"Certified")
        { CertificationStatus="Certified",EvergreenPayloadId="evergreen",AllResolvedSources=sources,CertifiedSupportingSources=sources };
        return new Phase7KnowledgeResolver().Resolve(payload,new FamilyNarrationProfileResolver().Resolve("CONSTELLATION","en").Profile!);
    }
    private static CertifiedNarrationSource Source(string id,string path) => new(id,"reference","Reviewed reference","authority","ref",true,true,[],[],[],"en",.95m,"checksum")
        { ReviewState="Approved",AuthorityState="Certified",Disposition="CertifiedSupporting",SupportedApprovedFieldPaths=[path] };
}
