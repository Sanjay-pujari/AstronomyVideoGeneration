using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7KnowledgeCultureClaimRootCauseTests
{
    [Fact] public void GreekSummaryPath_ResolvesGreekTradition()=>Tradition("greek","Greek");
    [Fact] public void RomanSummaryPath_ResolvesRomanTradition()=>Tradition("roman","Roman");
    [Fact] public void ArabicSummaryPath_ResolvesArabicTradition()=>Tradition("arabic","Arabic");
    [Fact] public void ChineseSummaryPath_ResolvesChineseTradition()=>Tradition("chinese","Chinese");
    [Fact] public void IndianHinduSummaryPath_ResolvesIndianHinduTradition()=>Tradition("indianHindu","IndianHindu");
    [Fact] public void OtherSummaryPath_RemainsUncategorised()=>Assert.Empty(Phase7CulturalClaimPolicy.ResolveCulturalTradition("cultureAndMythology.other.summary"));
    [Fact] public void ResolveCulturalTradition_EmptyPath_UsesMetadata()=>Assert.Equal("Greek",ResolveTradition("","greek"));
    [Fact] public void ResolveCulturalTradition_NullPath_UsesMetadata()=>Assert.Equal("Greek",ResolveTradition(null,"greek"));
    [Fact] public void ResolveCulturalTradition_WhitespacePath_UsesMetadata()=>Assert.Equal("Greek",ResolveTradition(" \t","greek"));
    [Fact] public void ResolveCulturalTradition_EmptyPath_UnknownMetadata_ReturnsEmpty()=>Assert.Empty(ResolveTradition("","unsupported"));
    [Fact] public void ResolveCulturalTradition_ValidCanonicalPath_UsesPath()=>Assert.Equal("Greek",Phase7CulturalClaimPolicy.ResolveCulturalTradition("cultureAndMythology.greek.summary"));
    [Fact] public void ResolveCulturalTradition_PathHasPriorityOverMetadata()=>Assert.Equal("Greek",ResolveTradition("cultureAndMythology.greek.summary","roman"));
    [Fact] public void ResolveCulturalTradition_NonblankMalformedPath_StillThrows()=>Assert.Throws<ArgumentException>(()=>ResolveTradition("not-a-governed-path","greek"));
    [Fact] public void ResolveCanonicalCulturalTradition_DiagnosticIdentityFallback_DoesNotThrow()
    {
        var resolution=Safe();var claim=Required(resolution);
        Assert.Equal("Greek",Phase7CulturalKnowledgeSafetyPolicy.ResolveCanonicalCulturalTradition(null!,resolution,claim));
    }

    [Fact] public void CulturalQualification_DoesNotImplyHumanReview()=>Assert.False(Required(Safe()).RequiresHumanReview);
    [Fact] public void SafeCulturalHistoryClaim_CanBeRequired()=>Assert.Equal(Phase7ClaimDisposition.Required,Required(Safe()).Disposition);
    [Fact] public void SensitiveAssociation_RemainsHumanReview()=>Review("rashiNote","SensitiveCulturalAssociationRequiresReview");
    [Fact] public void RashiNote_RemainsHumanReview()=>Review("rashiNote","SensitiveCulturalAssociationRequiresReview");
    [Fact] public void NakshatraNote_RemainsHumanReview()=>Review("nakshatraNote","SensitiveCulturalAssociationRequiresReview");
    [Fact] public void UncertainCultureClaim_RemainsHumanReview()=>Review("uncertaintyNote","UnresolvedCulturalUncertainty","greek");

    [Fact] public void RequiredCulturalClaim_RequiresExactRequiredEligibleEvidence()=>Assert.Empty(Culture(Resolve(Json("\"greek\":{\"summary\":\"Greek account.\"}"),[])).Claims.Where(x=>x.Disposition==Phase7ClaimDisposition.Required));
    [Fact] public void SectionLevelSourceSupport_IsNotExactClaimEvidence()=>Assert.Empty(Culture(Resolve(Json("\"greek\":{\"summary\":\"Greek account.\"}"),[Source("s","cultureAndMythology")])).Claims.Where(x=>x.Disposition==Phase7ClaimDisposition.Required));
    [Fact] public void ReviewedOptionalEvidence_CannotCreateRequiredCulturalClaim()=>Assert.Empty(Culture(Resolve(Json("\"greek\":{\"summary\":\"Greek account.\"}"),[Source("s","cultureAndMythology.greek.summary") with{Certified=false,AuthorityState="Reviewed"}])).Claims.Where(x=>x.Disposition==Phase7ClaimDisposition.Required));

    [Fact] public void RequiredAndHumanReviewCultureClaims_CanCoexist()=>Assert.Equal(2,Culture(Fixture()).Claims.Count);
    [Fact] public void OneSafeRequiredClaim_MakesCultureDomainAvailable()=>Assert.Equal(KnowledgeDomainStatus.Available,Culture(Safe()).Status);
    [Fact] public void OnlyHumanReviewClaims_LeavesCultureDomainRequiresHumanReview()=>Assert.Equal(KnowledgeDomainStatus.RequiresHumanReview,Culture(Resolve(Json("\"other\":{\"summary\":\"Broad account.\"}"),[])).Status);
    [Fact] public void RealConstellationFixture_ReportsEveryCultureCandidateDecision()
    {
        var result=Fixture(); var diagnostics=result.ClaimResolutionDiagnostics.Where(x=>x.Domain=="CultureAndMythology").ToArray();
        Assert.Equal(2,diagnostics.Length); Assert.All(diagnostics,x=>{Assert.NotEmpty(x.CandidateId);Assert.NotEmpty(x.ClaimId);Assert.NotEmpty(x.KnowledgeEntityId);Assert.NotEmpty(x.SelectedClaimIds);});
    }
    [Fact] public void RealConstellationFixture_HasSafeRequiredCultureClaim_OrReportsDataGapPrecisely()=>Assert.Single(Culture(Fixture()).Claims.Where(x=>x.Disposition==Phase7ClaimDisposition.Required));
    [Fact] public void RealConstellationFixture_CrossesCultureDomainGate_WhenEvidenceSupportsIt()=>Assert.Equal(KnowledgeDomainStatus.Available,Culture(Fixture()).Status);

    private static void Tradition(string branch,string expected)=>Assert.Equal(expected,Phase7CulturalClaimPolicy.ResolveCulturalTradition($"cultureAndMythology.{branch}.summary"));
    private static string ResolveTradition(string? path,string identity)=>Phase7CulturalClaimPolicy.ResolveCulturalTradition(path,
        new Dictionary<string,string>{{"traditionIdentity",identity}});
    private static void Review(string field,string reason,string branch="indianHindu")
    { var result=Resolve(Json($"\"{branch}\":{{\"{field}\":\"Governed statement.\"}}"),[]); Assert.Equal(reason,Assert.Single(result.ClaimResolutionDiagnostics).HumanReviewReason); }
    private static ResolvedNarrationKnowledge Safe()=>Resolve(Json("\"greek\":{\"summary\":\"In Greek tradition, a named myth is associated.\"}"),[Source("culture-source","cultureAndMythology.greek.summary")]);
    private static ResolvedNarrationKnowledge Fixture()=>Resolve(Json("\"greek\":{\"summary\":\"In Greek tradition, a named myth is associated.\"},\"other\":{\"summary\":\"Broad account.\"}"),[Source("culture-source","cultureAndMythology.greek.summary")]);
    private static CertifiedNarrationClaim Required(ResolvedNarrationKnowledge x)=>Assert.Single(Culture(x).Claims.Where(c=>c.Disposition==Phase7ClaimDisposition.Required));
    private static NarrationKnowledgeDomain Culture(ResolvedNarrationKnowledge x)=>x.Domains.Single(d=>d.Domain=="CultureAndMythology");
    private static string Json(string children)=>$"{{\"cultureAndMythology\":{{\"stableKnowledgeId\":\"constellation.generic\",\"sourceIds\":[\"culture-source\"],{children}}}}}";
    private static ResolvedNarrationKnowledge Resolve(string json,IReadOnlyList<CertifiedNarrationSource> sources)
    { var payload=new CertifiedKnowledgePayload("payload","event","CONSTELLATION","CONSTELLATION","en","{}",null,json,"registry",sources.Select(x=>x.SourceId).ToArray(),"Certified"){CertificationStatus="Certified",EvergreenPayloadId="evergreen",AllResolvedSources=sources,CertifiedSupportingSources=sources}; return new Phase7KnowledgeResolver().Resolve(payload,new FamilyNarrationProfileResolver().Resolve("CONSTELLATION","en").Profile!); }
    private static CertifiedNarrationSource Source(string id,string path)=>new(id,"reference","Reviewed reference","authority","ref",true,true,[],[],[],"en",.95m,"checksum"){ReviewState="Approved",AuthorityState="Certified",Disposition="CertifiedSupporting",SupportedApprovedFieldPaths=[path]};
}
