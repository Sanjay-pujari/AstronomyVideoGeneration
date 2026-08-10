using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase13GalleryKeyObjectTests
{
    private static readonly string[] Objects = ["Ordinary", "Beacon", "Great Nebula / M77", "Pattern Star", "Fifth", "Sixth"];
    private static readonly string[] Facts =
    [
        "Beacon is a bright star.",
        "Pattern Star is a recognition anchor in the pattern.",
        "Great Nebula / M77 is a certified nebula."
    ];

    private static IReadOnlyList<MatureGalleryCandidateGenerator.GalleryKeyObjectSelection> Select(int capacity = 5) =>
        MatureGalleryCandidateGenerator.SelectGalleryKeyObjects("CONSTELLATION", "Example", Objects, Facts, Facts, Facts, capacity);

    [Fact] public void GalleryKeyObjectsAreNotSelectedByRawArrayOrder() => Assert.Equal("Beacon", Select()[0].SourceValue);
    [Fact] public void GalleryKeyObjectsPreferHighValueCertifiedObjects() => Assert.Equal(["Beacon", "Pattern Star", "Great Nebula / M77"], Select(3).Select(x => x.SourceValue));
    [Fact] public void GalleryDeepSkyAliasIsAudienceFriendly() => Assert.Equal("GREAT NEBULA • M77", Select().Single(x => x.SourceValue.Contains('/')).DisplayValue);
    [Fact] public void GalleryKeyObjectsRespectDisplayCapacity() => Assert.Equal(3, Select(3).Count);

    [Fact]
    public void GalleryBeltGroupingRequiresCertifiedRelationship()
    {
        var selected = MatureGalleryCandidateGenerator.SelectGalleryKeyObjects("CONSTELLATION", "Example", ["One", "Two"], [], [], [], 5);
        Assert.DoesNotContain(selected, x => x.DisplayValue.Contains("BELT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GalleryBrightStarGroupingRequiresCertifiedClassification()
    {
        var selected = MatureGalleryCandidateGenerator.SelectGalleryKeyObjects("CONSTELLATION", "Example", ["Beacon"], [], [], [], 5);
        Assert.DoesNotContain(selected, x => x.DisplayValue.Contains("BRIGHT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("CertifiedMemberObject", Assert.Single(selected).RankingReasons.Single());
    }
}
