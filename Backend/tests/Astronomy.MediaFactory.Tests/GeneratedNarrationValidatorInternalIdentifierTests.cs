using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class GeneratedNarrationValidatorInternalIdentifierTests
{
    private const string Rule = "ProviderInternalIdentifierOrPlaceholder";

    [Fact]
    public void LookIsNotInternalIdentifier()
        => AssertNoInternalIdentifier("Look toward Orion on a clear winter evening.");

    [Fact]
    public void LookingIsNotInternalIdentifier()
        => AssertNoInternalIdentifier("Keep looking toward the southern sky.");

    [Fact]
    public void LoreIsNotInternalIdentifier()
        => AssertNoInternalIdentifier("Arabic star lore preserves several familiar names.");

    [Fact]
    public void LongIsNotInternalIdentifier()
        => AssertNoInternalIdentifier("Orion has a long cultural history.");

    [Fact]
    public void KrishnaIsNotKrIdentifier()
        => AssertNoInternalIdentifier("Krishna appears in cultural narration.");

    [Theory]
    [InlineData("look")]
    [InlineData("Look")]
    [InlineData("LOOK")]
    [InlineData("looking")]
    [InlineData("Looking")]
    [InlineData("looked")]
    [InlineData("looks")]
    [InlineData("lore")]
    [InlineData("Lore")]
    [InlineData("long")]
    [InlineData("Long")]
    [InlineData("longer")]
    [InlineData("local")]
    [InlineData("location")]
    [InlineData("logical")]
    [InlineData("lovely")]
    [InlineData("low")]
    [InlineData("lower")]
    [InlineData("luminous")]
    [InlineData("Krishna")]
    [InlineData("claimant")]
    [InlineData("knowledgeable")]
    public void OrdinaryPrefixCollisionIsNotInternalIdentifier(string word)
        => AssertNoInternalIdentifier($"The narration uses the word {word} naturally.");

    [Theory]
    [InlineData("LO-12")]
    [InlineData("LO_12")]
    public void ExplicitLoDelimitedIdentifierIsRejected(string identifier)
        => AssertInternalIdentifier(identifier, "DelimitedInternalIdentifier");

    [Theory]
    [InlineData("VQ-03")]
    [InlineData("VQ_03")]
    [InlineData("VQ03")]
    public void ExplicitVqIdentifierIsRejected(string identifier)
        => AssertInternalIdentifier(identifier, identifier.Contains('-') || identifier.Contains('_') ? "DelimitedInternalIdentifier" : "CompactInternalIdentifier");

    [Theory]
    [InlineData("CLM-1234")]
    [InlineData("CLAIM-ab114715")]
    public void ExplicitClaimIdentifierIsRejected(string identifier)
        => AssertInternalIdentifier(identifier, "DelimitedInternalIdentifier");

    [Theory]
    [InlineData("KR-42")]
    [InlineData("KNOWLEDGE-ABC123")]
    public void ExplicitKnowledgeReferenceIdentifierIsRejected(string identifier)
        => AssertInternalIdentifier(identifier, "DelimitedInternalIdentifier");

    [Fact]
    public void AdvancePlaceholderIsRejected()
        => AssertInternalIdentifier("Advance03", "AdvancePlaceholder");

    [Theory]
    [InlineData("final narration remains owned by Phase 7", "final narration remains owned")]
    [InlineData("advance the certified planning authority", "advance the certified")]
    public void RealInternalLeakageStillRejected(string phrase, string matchedPhrase)
    {
        var failure = Assert.Single(GeneratedNarrationValidator.Validate(phrase), failure => failure.RuleId == Rule);
        Assert.Equal(matchedPhrase, failure.MatchedPhrase, ignoreCase: true);
        Assert.Equal("InternalOwnershipPhrase", failure.SourceField);
    }

    [Fact]
    public void CurrentLongAttempt2FalsePositiveRegression()
    {
        string[] passages =
        [
            "Looking upward, the Belt is easy to recognize.",
            "Arabic star lore preserves several familiar names.",
            "Ancient lore connects Orion with stories across cultures.",
            "Take a closer look at Orion's Belt.",
            "Look toward Orion on a clear winter evening."
        ];

        Assert.Empty(passages.SelectMany(passage => GeneratedNarrationValidator.Validate(passage))
            .Where(failure => failure.RuleId == Rule));
    }

    [Theory]
    [InlineData("The constellation has long fascinated observers.")]
    [InlineData("Looking upward, the Belt is easy to recognize.")]
    [InlineData("Take a closer look at Orion's Belt.")]
    public void AdditionalNaturalDocumentaryProsePasses(string narration)
        => AssertNoInternalIdentifier(narration);

    private static void AssertNoInternalIdentifier(string narration)
        => Assert.DoesNotContain(GeneratedNarrationValidator.Validate(narration), failure => failure.RuleId == Rule);

    private static void AssertInternalIdentifier(string narration, string expectedCategory)
    {
        var failure = Assert.Single(GeneratedNarrationValidator.Validate(narration), failure => failure.RuleId == Rule);
        Assert.Equal(narration, failure.MatchedPhrase);
        Assert.Equal(expectedCategory, failure.SourceField);
    }
}
