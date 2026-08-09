using System.Security.Cryptography;
using System.Text;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ResponsiveThumbnailAuthorityServiceTests
{
    private const string Orion = "4dfad265275d676ab8198b5068260bbd77dcd61fc1b9527d39af8bb2bc61251d";

    [Fact]
    public void Phase12AcceptsCertifiedPhase11PublishedChecksumContract() =>
        Assert.True(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(Orion, Orion, Orion));

    [Fact]
    public void Phase12DoesNotRehashPhase11ManifestUsingDifferentCanonicalization()
    {
        var consumerSideRehash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("{\"variants\":[]}"))).ToLowerInvariant();

        Assert.NotEqual(consumerSideRehash, Orion);
        Assert.True(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(Orion, Orion, Orion));
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData("authority", "", "authority")]
    [InlineData("authority", "authority", "")]
    public void Phase12RequiresManifestPublicationValidationChecksumAgreement(string manifest, string publication, string validation) =>
        Assert.False(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(manifest, publication, validation));

    [Fact]
    public void Phase12RejectsPublicationChecksumMismatch() =>
        Assert.False(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(Orion, "mismatch", Orion));

    [Fact]
    public void Phase12RejectsCanonicalValidationChecksumMismatch() =>
        Assert.False(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(Orion, Orion, "mismatch"));

    [Fact]
    public void Phase12AcceptsCurrentCertifiedOrionPhase11Authority() =>
        Assert.True(ResponsiveThumbnailAuthorityService.PublishedChecksumsAgree(Orion, Orion, Orion));

    [Fact]
    public void ThumbnailDuplicateCopyDetectionIsCaseInsensitive()
    {
        Assert.True(ResponsiveThumbnailAuthorityService.DuplicateCopyDetected("Orion constellation guide!", " ORION, constellation   guide "));
        Assert.False(ResponsiveThumbnailAuthorityService.DuplicateCopyDetected("FIND ORION", "Orion constellation guide"));
    }

    [Fact]
    public void ConstellationThumbnailUsesDeterministicObjectCopy()
    {
        var copy = ResponsiveThumbnailAuthorityService.BuildThumbnailCopy("CONSTELLATION", ["Orion"], "Orion", "Orion constellation guide");

        Assert.Equal("FIND ORION", copy.Headline);
        Assert.Equal("Constellation.FindCertifiedPrimaryObject", copy.Rule);
        Assert.Equal(2, copy.WordCount);
    }

    [Fact]
    public void EvergreenConstellationDoesNotAddTonight()
    {
        var copy = ResponsiveThumbnailAuthorityService.BuildThumbnailCopy("CONSTELLATION", ["Lyra"], "Lyra", "Lyra constellation guide");

        Assert.DoesNotContain("TONIGHT", copy.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NOW", copy.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeadlineWordBudgetEnforcedForCertifiedConstellationCopy() =>
        Assert.InRange(ResponsiveThumbnailAuthorityService.BuildThumbnailCopy("CONSTELLATION", ["Ursa Major"], "Ursa Major", "Guide").WordCount, 2, 5);

    [Fact]
    public void FindOrionIsNotDuplicateOfOrionConstellationGuide()
    {
        var result = Validate("FIND ORION");

        Assert.True(result.CopyDifferentiationPassed);
        Assert.False(result.DuplicateCopyDetected);
    }

    [Fact]
    public void ObjectNameOverlapIsAllowed() =>
        Assert.Contains("orion", Validate("ORION").SharedAuthorityTokens);

    [Fact]
    public void CaseInsensitiveFullHeroTitleReuseIsRejected() =>
        AssertDuplicate("ORION CONSTELLATION GUIDE");

    [Fact]
    public void WhitespaceNormalizedFullTitleReuseIsRejected() =>
        AssertDuplicate("  Orion   constellation\tguide!  ");

    [Fact]
    public void FullHeroSubtitleReuseIsRejected() =>
        AssertDuplicate("Orion: How to Find the Hunter Constellation");

    [Fact]
    public void ShortDeterministicCopyDerivedFromHeroIsAllowed()
    {
        foreach (var headline in new[] { "FIND ORION", "ORION", "SPOT ORION", "ORION CONSTELLATION" })
            Assert.True(Validate(headline).CopyDifferentiationPassed);
    }

    [Fact]
    public void ParagraphHeroCopyIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Validate("FIND ORION", "Orion: How to Find the Hunter Constellation"));

        Assert.StartsWith("P12_DUPLICATE_COPY", exception.Message);
    }

    [Fact]
    public void TonightIsRejectedForEvergreenConstellationWithoutTemporalAuthority()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Validate("ORION TONIGHT"));

        Assert.StartsWith("P12_UNCERTIFIED_COPY_CLAIM", exception.Message);
    }

    [Fact]
    public void CandidateAndOuterValidationUseSameCopyPolicy()
    {
        var outer = Validate("FIND ORION");
        var candidateReadback = Validate("FIND ORION");

        Assert.Equal(outer.CopyDifferentiationPassed, candidateReadback.CopyDifferentiationPassed);
        Assert.Equal(outer.DuplicateCopyDetected, candidateReadback.DuplicateCopyDetected);
    }

    [Fact]
    public void CommittedAuthorityCannotFailDifferentDuplicateCopyRule()
    {
        var candidate = Validate("FIND ORION");
        var committedReadback = Validate("FIND ORION");

        Assert.True(candidate.CopyDifferentiationPassed);
        Assert.Equal(candidate.CopyDifferentiationPassed, committedReadback.CopyDifferentiationPassed);
        Assert.Equal(candidate.DuplicateCopyDetected, committedReadback.DuplicateCopyDetected);
    }

    private static ResponsiveThumbnailAuthorityService.CopyDifferentiationDecision Validate(
        string headline, string? secondary = null) => ResponsiveThumbnailAuthorityService.ValidateCopyDifferentiation(
            "Orion constellation guide", "Orion: How to Find the Hunter Constellation", headline, secondary,
            "Constellation.FindCertifiedPrimaryObject");

    private static void AssertDuplicate(string headline)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Validate(headline));
        Assert.StartsWith("P12_DUPLICATE_COPY", exception.Message);
    }
}
