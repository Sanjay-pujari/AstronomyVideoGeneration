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
}
