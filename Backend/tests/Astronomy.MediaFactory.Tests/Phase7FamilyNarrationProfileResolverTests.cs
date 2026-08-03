using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7FamilyNarrationProfileResolverTests
{
    private readonly FamilyNarrationProfileResolver resolver = new();

    [Fact]
    public void Resolve_ConstellationReturnsConstellationDocumentaryProfile()
    {
        var result = resolver.Resolve("CONSTELLATION", "en");
        Assert.True(result.IsValid);
        Assert.Equal("constellation-documentary-v1", result.Profile!.ProfileId);
        Assert.Equal(600, result.Profile.LongProfile.Duration.PreferredSeconds);
        Assert.Equal(["hook","central-discovery","viewing-action","memorable-close"], result.Profile.ShortProfile.BeatKeys);
    }

    [Theory]
    [InlineData("PLANET", "planet-documentary-v1")]
    [InlineData("METEOR_SHOWER", "meteor-shower-documentary-v1")]
    [InlineData("CONJUNCTION", "conjunction-documentary-v1")]
    [InlineData("ECLIPSE", "eclipse-documentary-v1")]
    [InlineData("COMET", "comet-documentary-v1")]
    [InlineData("SATELLITE", "satellite-documentary-v1")]
    [InlineData("ISS_PASS", "satellite-documentary-v1")]
    public void Resolve_SupportedFamiliesReturnRegisteredProfiles(string family, string profile)
        => Assert.Equal(profile, resolver.Resolve(family, "hi").Profile!.ProfileId);

    [Fact]
    public void Profiles_HaveDeterministicChecksums()
    {
        var first = new FamilyNarrationProfileResolver();
        Assert.All(resolver.Profiles, profile => Assert.Equal(profile.DeterministicChecksum,
            first.Profiles.Single(x => x.ProfileId == profile.ProfileId).DeterministicChecksum));
    }

    [Fact]
    public void Resolve_UnsupportedFamilyFailsWithDeterministicCode()
        => Assert.Equal("P7INPUT_EVENT_FAMILY_UNSUPPORTED", resolver.Resolve("UNKNOWN", "en").ReasonCode);
}
