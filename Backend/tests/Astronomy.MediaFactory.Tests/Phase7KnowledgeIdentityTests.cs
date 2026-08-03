using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using System.Text.Json;

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

    [Theory]
    [InlineData("cultureAndMythology.greek.summary")]
    [InlineData("culture_and_mythology.greek.summary")]
    [InlineData("cultureAndMythology[0].greek.summary")]
    public void EquivalentCanonicalPathsProduceSameSemanticIdentity(string path)
        => Assert.Equal("cultureAndMythology.greek.summary", Phase7CanonicalFieldPathPolicy.Canonicalize(path));

    [Fact]
    public void KnownObjectIdIsPreferredOverContentHash()
    {
        using var document = JsonDocument.Parse("""{"objectId":"star.example","canonicalName":"Localized name"}""");
        var result = new Phase7KnowledgeEntityIdentityResolver().Resolve(document.RootElement, "objects", [], true);
        Assert.Equal("star.example", result.KnowledgeId);
        Assert.Equal("objectId", result.IdentityPrecision);
        Assert.False(result.RequiresHumanReview);
    }

    [Fact]
    public void ContentHashIsUsedOnlyForAnonymousMultiValueItem()
    {
        using var document = JsonDocument.Parse("\"unregistered value\"");
        var result = new Phase7KnowledgeEntityIdentityResolver().Resolve(document.RootElement, "facts", [], true);
        Assert.StartsWith("facts.anonymous.", result.KnowledgeId);
        Assert.Equal("AnonymousContentFallback", result.IdentityPrecision);
        Assert.True(result.RequiresHumanReview);
    }

    [Fact]
    public void CertifiedRegistryMappingReplacesLocalizedDisplayIdentity()
    {
        using var document = JsonDocument.Parse("\"Betelgeuse\"");
        var source = new CertifiedNarrationSource("source", "catalog", "catalog", "authority", "ref", true, true,
            ["star.betelgeuse"], [], [], "en", 1m, "checksum");
        var result = new Phase7KnowledgeEntityIdentityResolver().Resolve(document.RootElement, "stars", [source], true);
        Assert.Equal("star.betelgeuse", result.KnowledgeId);
        Assert.Equal("CertifiedObjectRegistry", result.IdentityPrecision);
    }
}
