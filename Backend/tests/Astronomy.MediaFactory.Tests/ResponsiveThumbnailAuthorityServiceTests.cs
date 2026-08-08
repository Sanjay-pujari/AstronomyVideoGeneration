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
}
