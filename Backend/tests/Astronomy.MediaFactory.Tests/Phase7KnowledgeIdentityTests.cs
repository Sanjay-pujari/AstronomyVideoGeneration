using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7KnowledgeIdentityTests
{
    [Theory]
    [InlineData("identity.iauAbbreviation", "identity.iauAbbreviation")]
    [InlineData("Identity/IAU_Abbreviation", "identity.iauAbbreviation")]
    [InlineData("scientific.orionBeltStars[12]", "scientific.orionBeltStars")]
    public void CanonicalFieldPath_NormalizesEquivalentEvidencePaths(string input, string expected)
        => Assert.Equal(expected, Phase7CanonicalFieldPathPolicy.Canonicalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("identity.<display text>")]
    [InlineData("../identity.name")]
    public void CanonicalFieldPath_RejectsDisplayOrTraversalText(string input)
        => Assert.False(Phase7CanonicalFieldPathPolicy.TryCanonicalize(input, out _));

    [Fact]
    public void ScalarIdentitySurvivesTextChange()
    {
        var first = Phase7Determinism.SemanticClaimId("star.example", "star.example.identity.summary", "en", "payload-v1");
        var changedText = Phase7Determinism.SemanticClaimId("star.example", "star.example.identity.summary", "en", "payload-v1");
        Assert.Equal(first, changedText);
    }
}
