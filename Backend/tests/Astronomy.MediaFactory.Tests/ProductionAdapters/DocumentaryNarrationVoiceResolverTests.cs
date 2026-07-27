using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryNarrationVoiceResolverTests
{
    [Theory]
    [InlineData(DocumentaryMediaLanguage.English,"en-IN","en-US-JennyNeural","medium")]
    [InlineData(DocumentaryMediaLanguage.Hindi,"hi-IN","hi-IN-SwaraNeural","slow")]
    public void Configured_voice_is_resolved(DocumentaryMediaLanguage language,string locale,string voice,string rate)
    {var result=Resolver().Resolve(DocumentaryNarrationTestFixtures.Request(language));Assert.True(result.Succeeded);Assert.Equal(language,result.Language);Assert.Equal(locale,result.Locale);Assert.Equal(voice,result.VoiceId);Assert.Equal(rate,result.SpeakingRate);Assert.Equal("+2%",result.Pitch);Assert.Equal("ExplicitVoice",result.Reason);Assert.Null(result.Failure);}

    [Theory]
    [InlineData(DocumentaryMediaLanguage.English,"en-GB-SoniaNeural")]
    [InlineData(DocumentaryMediaLanguage.Hindi,"hi-IN-MadhurNeural")]
    public void Compatible_explicit_voice_is_preserved(DocumentaryMediaLanguage language,string voice)
    {var request=DocumentaryNarrationTestFixtures.Request(language) with{VoiceProfileId=voice};var result=Resolver().Resolve(request);Assert.True(result.Succeeded);Assert.Equal(voice,result.VoiceId);Assert.Equal("ExplicitVoice",result.Reason);}

    [Fact] public void Default_voice_uses_configuration_and_request_is_unchanged(){var request=DocumentaryNarrationTestFixtures.Request();var before=request;var result=Resolver().Resolve(request);Assert.Equal("en-US-JennyNeural",result.VoiceId);Assert.Equal(before,request);}

    [Theory]
    [InlineData(DocumentaryMediaLanguage.English,"hi-IN-SwaraNeural")]
    [InlineData(DocumentaryMediaLanguage.Hindi,"en-US-JennyNeural")]
    public void Incompatible_voice_is_rejected(DocumentaryMediaLanguage language,string voice){var result=Resolver().Resolve(DocumentaryNarrationTestFixtures.Request(language) with{VoiceProfileId=voice});Assert.False(result.Succeeded);Assert.Equal("Rejected",result.Reason);Assert.Equal(DocumentaryProductionFailureCode.ProviderRejectedRequest,result.Failure!.Code);}

    [Theory]
    [InlineData(DocumentaryMediaLanguage.English)] [InlineData(DocumentaryMediaLanguage.Hindi)]
    public void Missing_language_voice_is_configuration_failure(DocumentaryMediaLanguage language){var o=DocumentaryNarrationTestFixtures.SpeechOptions();o.Voices.Clear();o.DefaultVoiceName=null;o.DefaultLanguage="other";var result=new DocumentaryNarrationVoiceResolver(Options.Create(o)).Resolve(DocumentaryNarrationTestFixtures.Request(language));Assert.False(result.Succeeded);Assert.Equal(DocumentaryProductionFailureCode.ConfigurationMissing,result.Failure!.Code);Assert.Null(result.Locale);}

    [Fact] public void Unsupported_language_is_rejected(){var request=DocumentaryNarrationTestFixtures.Request() with{Language=(DocumentaryMediaLanguage)99};var result=Resolver().Resolve(request);Assert.False(result.Succeeded);Assert.Equal(DocumentaryProductionFailureCode.ProviderRejectedRequest,result.Failure!.Code);}
    [Fact] public void Default_prosody_and_determinism_are_preserved(){var o=DocumentaryNarrationTestFixtures.SpeechOptions();o.ProsodyRate.Clear();o.DefaultProsodyRate="default-rate";var resolver=new DocumentaryNarrationVoiceResolver(Options.Create(o));var request=DocumentaryNarrationTestFixtures.Request();Assert.Equal("default-rate",resolver.Resolve(request).SpeakingRate);Assert.Equal(resolver.Resolve(request),resolver.Resolve(request));}
    private static DocumentaryNarrationVoiceResolver Resolver()=>new(Options.Create(DocumentaryNarrationTestFixtures.SpeechOptions()));
}
