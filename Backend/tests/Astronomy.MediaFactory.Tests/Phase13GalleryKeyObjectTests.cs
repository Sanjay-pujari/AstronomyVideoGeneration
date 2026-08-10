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

    [Fact] public void GalleryKeyObjectsAreNotSelectedByRawArrayOrder() => Assert.Equal("Great Nebula / M77", Select()[0].SourceValue);
    [Fact] public void GalleryKeyObjectsPreferHighValueCertifiedObjects() => Assert.Equal(["Great Nebula / M77", "Beacon", "Pattern Star"], Select(3).Select(x => x.SourceValue));
    [Fact] public void GalleryDeepSkyAliasIsAudienceFriendly() => Assert.Equal("GREAT NEBULA • M77", Select().Single(x => x.SourceValue.Contains('/')).DisplayValue);
    [Fact] public void GalleryKeyObjectsRespectDisplayCapacity() => Assert.Equal(3, Select(3).Count);

    [Fact]
    public void GalleryKeyObjectSelectionIndependentOfInputOrder()
    {
        var expected = Select().Select(x => x.SourceValue).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shuffled = MatureGalleryCandidateGenerator.SelectGalleryKeyObjects("CONSTELLATION", "Example",
            Objects.Reverse().ToArray(), Facts.Reverse().ToArray(), Facts.Reverse().ToArray(), Facts.Reverse().ToArray(), 5);
        Assert.True(expected.SetEquals(shuffled.Select(x => x.SourceValue)));
    }

    [Fact]
    public void GalleryKeyObjectsPreferSemanticDiversity()
    {
        var result = MatureGalleryCandidateGenerator.EvaluateGalleryKeyObjects("CONSTELLATION", "Example",
            ["Alpha", "Beta", "Gamma Nebula"], ["Alpha is a major star.", "Beta is a major star."], [], ["Gamma Nebula is a nebula."], 2);
        Assert.Equal(2, result.SelectedCategoryCount);
        Assert.True(result.DiversityPassed);
    }

    [Fact]
    public void GalleryKeyObjectsIncludeCertifiedDeepSkyHighlightWhenCapacityAllows() =>
        Assert.Contains(Select(3), x => x.Category == "DeepSkyObject");

    [Fact]
    public void GalleryKeyObjectAliasCountsAsOneSemanticObject()
    {
        var alias = Assert.Single(Select(3), x => x.SourceValue.Contains('/'));
        Assert.Equal("GREAT NEBULA • M77", alias.DisplayValue);
        Assert.Equal(3, Select(3).Count);
    }

    [Fact]
    public void GalleryKeyObjectsDoNotHardcodeOrion()
    {
        var selected = MatureGalleryCandidateGenerator.SelectGalleryKeyObjects("GALAXY", "Example",
            ["Andromeda Galaxy / M31", "Core Star"], ["Core Star is a key star."], [], ["Andromeda Galaxy / M31 is a galaxy."], 2);
        Assert.Contains(selected, x => x.DisplayValue == "ANDROMEDA GALAXY • M31");
        Assert.DoesNotContain(selected, x => x.SourceValue.Contains("Orion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GalleryKeyObjectSelectionIsDeterministic()
    {
        var first = Select().Select(x => (x.SourceValue, x.FinalScore, x.SelectionReason));
        Assert.Equal(first, Select().Select(x => (x.SourceValue, x.FinalScore, x.SelectionReason)));
    }

    [Fact]
    public void GalleryKeyObjectDiversityCanReplaceLowerValueSameCategoryObject()
    {
        var result = MatureGalleryCandidateGenerator.EvaluateGalleryKeyObjects("CONSTELLATION", "Example",
            ["Star A", "Star B", "Star C", "Star D", "Remote Nebula"],
            ["Star A is a major star.", "Star B is a major star.", "Star C is a major star.", "Star D is a major star."],
            [], ["Remote Nebula is a nebula."], 4);
        Assert.Contains(result.Selected, x => x.SourceValue == "Remote Nebula" && x.DiversityBonus > 0);
        Assert.Equal(3, result.Selected.Count(x => x.Category == "ProminentOrKeyStar"));
    }

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
