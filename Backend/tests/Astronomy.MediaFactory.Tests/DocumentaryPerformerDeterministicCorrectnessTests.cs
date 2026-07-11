using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class DocumentaryPerformerDeterministicCorrectnessTests
{
    [Theory]
    [InlineData("PlanetPairingApparentLineOfSightGeometry")]
    [InlineData("ApparentAlignmentExplanation")]
    public void SemanticIdentifiers_AreRealized_NotLeaked(string semanticKey)
    {
        var en = SemanticTermRealizer.Realize(semanticKey, LanguageProfileResolver.Resolve("en-US"));
        var hi = SemanticTermRealizer.Realize(semanticKey, LanguageProfileResolver.Resolve("hi-IN"));

        Assert.DoesNotContain(semanticKey, en);
        Assert.DoesNotContain(semanticKey, hi);
        Assert.True(GeneratedNarrationValidator.Validate(semanticKey).Any(f => f.RuleId == "PascalCaseSemanticKey"));
    }

    [Theory]
    [InlineData("1.19", "degrees", false, "about 1.19 degrees")]
    [InlineData("0.5", "degrees", false, "about 0.5 degrees")]
    [InlineData("12.75", "degrees", false, "about 12.75 degrees")]
    [InlineData("1.19", "degrees", true, "लगभग 1.19 डिग्री")]
    public void NumberUnitFormatter_KeepsDecimalAtomic(string value, string unit, bool hi, string expected)
    {
        Assert.Equal(expected, NumberUnitFormatter.Format(value, unit, hi));
    }

    [Fact]
    public void LocalTime_ExplicitOffset_IsRealizedInsteadOfRawUtc()
    {
        var counters = new NarrationInputNormalizer.Counter();
        var en = AstronomyDateTimeLocalizer.LocalizeTime("2026-11-16 05:30 +0530", false, counters);
        var hi = AstronomyDateTimeLocalizer.LocalizeTime("2026-11-16 05:30 +0530", true, counters);

        Assert.Equal("around 5:30 AM on November 16", en);
        Assert.Equal("16 नवंबर की सुबह लगभग साढ़े पाँच बजे", hi);
        Assert.DoesNotContain("00:00", hi);
    }

    [Fact]
    public void GeneratedValidator_BlocksIncompleteTransitionsAndFactFragments()
    {
        var failures = GeneratedNarrationValidator.Validate("The next idea becomes clearer through the. Mars, Jupiter.");

        Assert.Contains(failures, f => f.RuleId == "IncompleteTransition");
        Assert.Contains(failures, f => f.RuleId == "StandaloneFactListFragment");
    }

    [Theory]
    [InlineData("hi", "hi-IN", "सूर्यास्त के बाद बृहस्पति और शुक्र पश्चिमी आकाश में पास दिखाई देंगे।")]
    [InlineData("en", "en-US", "After sunset, Jupiter and Venus appear close in the western sky.")]
    public void LanguageValidator_NormalizesFamily_AndScriptRatioStaysBounded(string family, string requested, string narration)
    {
        var result = LanguageOutputValidator.Validate(narration, LanguageProfileResolver.Resolve(requested));

        Assert.Equal(family, result.RequestedLanguageFamily);
        Assert.True(result.LanguageFamilyMatch);
        Assert.InRange(result.ScriptRatio, 0m, 1m);
    }

    [Fact]
    public void LanguageValidator_FlagsSemanticIdentifierLeakage()
    {
        var result = LanguageOutputValidator.Validate("PlanetPairingApparentLineOfSightGeometry explains this view.", LanguageProfileResolver.Resolve("en-US"));

        Assert.False(result.Passed);
        Assert.True(result.InternalIdentifierCount > 0);
    }
}
