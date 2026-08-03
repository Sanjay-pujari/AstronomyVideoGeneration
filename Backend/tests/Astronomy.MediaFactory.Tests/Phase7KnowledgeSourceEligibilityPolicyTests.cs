using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7KnowledgeSourceEligibilityPolicyTests
{
    private readonly Phase7SourceEligibilityPolicy policy = new();

    [Theory]
    [InlineData("Approved", "Certified")]
    [InlineData("Reviewed", "Certified")]
    [InlineData("Verified", "Verified")]
    public void GovernedAuthoritySupportsRequiredClaim(string review, string authority)
    {
        var result = policy.Classify(Request(Source(true, true) with { ReviewState=review, AuthorityState=authority }));
        Assert.Equal(Phase7SourceEligibility.EligibleForRequiredClaim, result.Eligibility);
        Assert.True(result.Authoritative);
    }

    [Fact]
    public void ReviewedNonCertifiedSourceCannotSupportRequiredClaim()
    {
        var result=policy.Classify(Request(Source(true, false) with { ReviewState="Reviewed", AuthorityState="Reviewed", Disposition="Reviewed" }));
        Assert.Equal(Phase7SourceEligibility.AuditOnly,result.Eligibility);
    }

    [Theory]
    [InlineData("Rejected", true, true)]
    [InlineData("Unverified", true, false)]
    public void RejectedOrUnverifiedSourceNeverSupportsRequiredClaim(string disposition,bool reviewed,bool certified)
    {
        var result=policy.Classify(Request(Source(reviewed,certified) with { Disposition=disposition }));
        Assert.NotEqual(Phase7SourceEligibility.EligibleForRequiredClaim,result.Eligibility);
    }

    [Fact]
    public void ReviewedOptionalEvidenceRequiresHumanReview()
    {
        var request=Request(Source(true,false) with { ReviewState="Reviewed",Disposition="Reviewed" }) with
            { Required=false, OptionalReviewedEvidenceAllowed=true, RequiresHumanReview=true };
        var result=policy.Classify(request);
        Assert.Equal(Phase7SourceEligibility.EligibleForOptionalClaim,result.Eligibility);
        Assert.False(result.Authoritative);
    }

    private static Phase7SourceEligibilityRequest Request(CertifiedNarrationSource source) =>
        new(source,"en","constellation.example","constellation.example.identity.summary","identity.summary",true,false,false);
    private static CertifiedNarrationSource Source(bool reviewed,bool certified) =>
        new("source","catalog","title","authority","ref",reviewed,certified,[],[],[],"en",.9m,"checksum")
        { SupportedApprovedFieldPaths=["identity.summary"],Disposition="CertifiedSupporting" };
}
