using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class LanguageOutputValidatorTests
{
    [Theory]
    [InlineData("hi")]
    [InlineData("hi-IN")]
    [InlineData("Hindi")]
    [InlineData("हिन्दी")]
    [InlineData("हिंदी")]
    public void Resolver_NormalizesHindiAliases(string requested)
    {
        var profile = LanguageProfileResolver.Resolve(requested);
        Assert.Equal("hi", profile.LanguageCode);
        Assert.Equal("hi-IN", profile.Culture);
        Assert.Equal("Hindi", profile.DisplayName);
        Assert.Equal("Devanagari", profile.Script);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("en-IN")]
    [InlineData("English")]
    public void Resolver_NormalizesEnglishAliases(string requested)
    {
        var profile = LanguageProfileResolver.Resolve(requested);
        Assert.Equal("en", profile.LanguageCode);
        Assert.StartsWith("en-", profile.Culture);
        Assert.Equal("English", profile.DisplayName);
    }

    [Fact]
    public void HindiValidator_PassesDevanagariWithApprovedEnglishTerms()
    {
        var profile = LanguageProfileResolver.Resolve("hi");
        var result = LanguageOutputValidator.Validate("सूर्यास्त के बाद Jupiter और Venus पश्चिमी आकाश में पास दिखाई देंगे। बृहस्पति और शुक्र की कोणीय दूरी लगभग 1.63 डिग्री होगी।", profile);
        Assert.True(result.Passed);
        Assert.Equal("hi", result.DetectedLanguage);
        Assert.True(result.ApprovedForeignTermCount >= 2);
    }

    [Fact]
    public void HindiValidator_FailsPredominantlyEnglishOutput()
    {
        var profile = LanguageProfileResolver.Resolve("hi");
        var result = LanguageOutputValidator.Validate("As daylight fades, two brilliant worlds appear close together. Look west after sunset and enjoy the view.", profile);
        Assert.False(result.Passed);
        Assert.True(result.UnapprovedEnglishSentenceCount > 0);
    }

    [Fact]
    public void HindiValidator_FailsRawIsoTimestamps()
    {
        var profile = LanguageProfileResolver.Resolve("hi");
        var result = LanguageOutputValidator.Validate("सूर्यास्त के बाद 2026-06-09T13:53:00Z पर पश्चिमी आकाश देखें।", profile);
        Assert.False(result.Passed);
        Assert.True(result.RawTimestampCount > 0);
    }

    [Fact]
    public void UnsupportedLanguage_FailsDescriptively()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LanguageProfileResolver.Resolve("fr"));
        Assert.Contains("Unsupported narration language", ex.Message);
    }
}
