using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7KnowledgeCultureDomainAuthorityTests
{
    [Fact] public void MandatoryCultureDomain_RequiresAtLeastOneSafeRequiredClaim() => AssertAvailable(Safe());
    [Fact] public void MandatoryCultureDomain_DoesNotRequireEveryTraditionBranch() => AssertAvailable(Safe("\"chinese\":{\"summary\":\"A disputed mapping.\"},"));
    [Fact] public void MandatoryCultureDomain_WithOneRequiredAndSeveralHumanReviewClaims_Passes() =>
        AssertAvailable(Safe("\"indianHindu\":{\"rashiNote\":\"A traditional association.\",\"nakshatraNote\":\"A traditional association.\"},"));

    [Fact] public void MandatoryCultureDomain_WithOnlyHumanReviewClaims_Fails() =>
        Assert.Equal(KnowledgeDomainStatus.RequiresHumanReview,Domain(Resolve(Json("\"indianHindu\":{\"rashiNote\":\"A traditional association.\"}"),[])).Status);

    [Fact] public void OptionalCultureChild_Unsupported_DoesNotEmitRequiredUnsupported()
    {
        var result=Resolve(Json("\"chinese\":{\"summary\":\"A tradition-scoped account.\"}"),[]);
        Assert.DoesNotContain(result.BlockingIssues,x=>x.Contains("REQUIRED_CLAIM_UNSUPPORTED",StringComparison.Ordinal));
        Assert.Contains(result.Warnings,x=>x.Contains("OPTIONAL_CLAIM_UNSUPPORTED",StringComparison.Ordinal));
    }

    [Fact] public void HumanReviewCultureChild_Unsupported_DoesNotEmitRequiredUnsupported()
    {
        var result=Resolve(Json("\"indianHindu\":{\"rashiNote\":\"A traditional association.\"}"),[]);
        Assert.DoesNotContain(result.BlockingIssues,x=>x.Contains("REQUIRED_CLAIM_UNSUPPORTED",StringComparison.Ordinal));
    }

    [Fact] public void RequiredCultureChild_Unsupported_StillFails()
    {
        // A source selected for this claim but ineligible for required authority cannot
        // manufacture domain availability.
        var source=Source("culture-source","cultureAndMythology.greek.summary") with { Confidence=.2m };
        var result=Resolve(Json("\"greek\":{\"summary\":\"In Greek tradition, a named myth is associated.\"}"),[source]);
        Assert.NotEqual(KnowledgeDomainStatus.Available,Domain(result).Status);
    }

    [Fact] public void SafeQualifiedTraditionClaim_WithExactRequiredEvidence_Passes() => AssertAvailable(Safe());
    [Fact] public void UnresolvedCulturalClaim_RemainsHumanReview() => AssertReview("uncertaintyNote","The mapping is disputed.","UnresolvedCulturalUncertainty");
    [Fact] public void UnresolvedCulturalClaim_DoesNotSatisfyMandatoryDomain() =>
        Assert.NotEqual(KnowledgeDomainStatus.Available,Domain(Resolve(Json("\"greek\":{\"uncertaintyNote\":\"The mapping is disputed.\"}"),[])).Status);
    [Fact] public void RashiNote_IsNotAutomaticallyRequired() => AssertReview("rashiNote","A Rashi association.","SensitiveCulturalAssociationRequiresReview");
    [Fact] public void NakshatraNote_IsNotAutomaticallyRequired() => AssertReview("nakshatraNote","A Nakshatra association.","SensitiveCulturalAssociationRequiresReview");

    [Fact] public void DifferentTraditions_RemainSeparate()
    {
        var result=Resolve(Json("\"greek\":{\"summary\":\"Greek account.\"},\"roman\":{\"summary\":\"Roman account.\"}"),
            [Source("g","cultureAndMythology.greek.summary"),Source("r","cultureAndMythology.roman.summary")]);
        Assert.Equal(2,Domain(result).Claims.Count);
    }

    [Fact] public void EquivalentEventAndEvergreenCultureClaims_MergeWithoutDuplicateErrors()
    {
        var json=Json("\"greek\":{\"summary\":\"In Greek tradition, a named myth is associated.\"}");
        var result=Resolve(json,[Source("culture-source","cultureAndMythology.greek.summary")],json);
        Assert.Single(Domain(result).Claims);
        Assert.DoesNotContain(result.BlockingIssues,x=>x.Contains("DUPLICATE",StringComparison.Ordinal));
    }

    [Fact] public void StableCultureIdentity_PreventsAnonymousDuplicateClaims() => EquivalentEventAndEvergreenCultureClaims_MergeWithoutDuplicateErrors();
    [Fact] public void RealConstellationFixture_PassesCultureMandatoryDomainWhenSafeEvidenceExists() => AssertAvailable(Safe());
    [Fact] public void RealConstellationFixture_RetainsSensitiveClaimsAsHumanReview() =>
        MandatoryCultureDomain_WithOneRequiredAndSeveralHumanReviewClaims_Passes();

    [Fact] public void DuplicateCultureCandidates_MergeDeterministically() => EquivalentEventAndEvergreenCultureClaims_MergeWithoutDuplicateErrors();
    [Fact] public void EquivalentCultureCandidates_DoNotProduceDuplicateBlockingErrors() => EquivalentEventAndEvergreenCultureClaims_MergeWithoutDuplicateErrors();
    [Fact] public void StableCultureIdentity_PreferredOverAnonymousFallback() => EquivalentEventAndEvergreenCultureClaims_MergeWithoutDuplicateErrors();
    [Fact] public void EventAndEvergreenCultureClaims_PreserveCorrectLineage()
    {
        var json=Json("\"greek\":{\"summary\":\"In Greek tradition, a named myth is associated.\"}");
        var result=Resolve(json,[Source("culture-source","cultureAndMythology.greek.summary")],json);
        Assert.Equal(2,Assert.Single(result.MergeDecisions).EvergreenClaimCandidate.SourceIds.Concat(Assert.Single(result.MergeDecisions).EventClaimCandidate.SourceIds).Count());
    }

    private static void AssertReview(string field,string value,string reason)
    {
        var result=Resolve(Json($"\"indianHindu\":{{\"{field}\":\"{value}\"}}"),[]);
        Assert.Equal(Phase7ClaimDisposition.HumanReview,Assert.Single(Domain(result).Claims).Disposition);
        Assert.Equal(reason,Assert.Single(result.ClaimResolutionDiagnostics).HumanReviewReason);
    }
    private static void AssertAvailable(ResolvedNarrationKnowledge result) => Assert.Equal(KnowledgeDomainStatus.Available,Domain(result).Status);
    private static NarrationKnowledgeDomain Domain(ResolvedNarrationKnowledge result) => result.Domains.Single(x=>x.Domain=="CultureAndMythology");
    private static ResolvedNarrationKnowledge Safe(string extra="") => Resolve(Json($"{extra}\"greek\":{{\"summary\":\"In Greek tradition, a named myth is associated.\"}}"),[Source("culture-source","cultureAndMythology.greek.summary")]);
    private static string Json(string children) => $"{{\"cultureAndMythology\":{{\"stableKnowledgeId\":\"constellation.generic\",\"sourceIds\":[\"culture-source\"],{children}}}}}";
    private static ResolvedNarrationKnowledge Resolve(string evergreen,IReadOnlyList<CertifiedNarrationSource> sources,string eventJson="{}")
    {
        var payload=new CertifiedKnowledgePayload("payload","event","CONSTELLATION","CONSTELLATION","en",eventJson,null,evergreen,"registry",sources.Select(x=>x.SourceId).ToArray(),"Certified")
        { CertificationStatus="Certified",EvergreenPayloadId="evergreen",AllResolvedSources=sources,CertifiedSupportingSources=sources };
        return new Phase7KnowledgeResolver().Resolve(payload,new FamilyNarrationProfileResolver().Resolve("CONSTELLATION","en").Profile!);
    }
    private static CertifiedNarrationSource Source(string id,string path) => new(id,"reference","Reviewed reference","governing authority","ref",true,true,[],[],[],"en",.95m,"checksum")
        { ReviewState="Approved",AuthorityState="Certified",Disposition="CertifiedSupporting",SupportedApprovedFieldPaths=[path] };
}
