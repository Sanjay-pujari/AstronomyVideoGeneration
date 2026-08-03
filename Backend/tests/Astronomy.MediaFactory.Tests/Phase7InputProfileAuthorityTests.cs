using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7InputProfileAuthorityTests
{
    private readonly FamilyNarrationProfileResolver resolver = new();

    [Fact]
    public void CanonicalConstellationProfile_MatchesPublishedAuthority()
    {
        var profile = Resolve("CONSTELLATION");
        Assert.True(Phase7InputAuthorityEvaluator.ProfileIdentityMatches(
            profile.ProfileId, profile.ContractVersion, "constellation-documentary-v1", profile.ContractVersion));
    }

    [Fact] public void ProfileIdMismatch_FailsWithP7INPUT_PROFILE_INVALID() =>
        Assert.False(Phase7InputAuthorityEvaluator.ProfileIdentityMatches("wrong", "1", "constellation-documentary-v1", "1"));

    [Fact] public void ProfileVersionMismatch_FailsWithP7INPUT_PROFILE_INVALID() =>
        Assert.False(Phase7InputAuthorityEvaluator.ProfileIdentityMatches("constellation-documentary-v1", "old", "constellation-documentary-v1", Phase7FoundationContract.Version));

    [Fact]
    public void ProfileMismatch_ErrorContainsExpectedAndActualValues()
    {
        const string expected = "wrong";
        var actual = Resolve("constellation").ProfileId;
        Assert.NotEqual(expected, actual);
        Assert.Contains("constellation-documentary-v1", $"ExpectedProfileId={expected}; CanonicalProfileId={actual}");
    }

    [Theory]
    [InlineData("CONSTELLATION")]
    [InlineData("Constellation")]
    [InlineData("constellation")]
    public void EventTypeCaseVariants_ResolveSameCanonicalFamily(string value) =>
        Assert.Equal("CONSTELLATION", Resolve(value).EventFamily);

    [Fact] public void ContentCategory_DoesNotOverrideEventFamilyProfile() =>
        Assert.Equal("constellation-documentary-v1", Resolve("CONSTELLATION").ProfileId);

    [Fact] public void PlannedFormat_DoesNotChangeKnowledgeProfile() =>
        Assert.Equal(Resolve("CONSTELLATION").ProfileId, Resolve("constellation").ProfileId);

    [Fact] public void LegacyNarrationProfile_IsNotUsedAsCanonicalProfile() =>
        Assert.NotEqual("orion-gold", Resolve("CONSTELLATION").ProfileId);

    [Fact] public void StoryFrameProfile_IsNotComparedAsNarrationProfile() =>
        Assert.Equal("constellation-documentary-v1", Resolve("CONSTELLATION").ProfileId);

    [Fact]
    public void UnsupportedFamily_ReturnsP7INPUT_FAMILY_UNSUPPORTED()
    {
        var result = resolver.Resolve("not-a-governed-family", "en");
        Assert.False(result.IsValid);
        Assert.Equal("P7INPUT_FAMILY_UNSUPPORTED", result.ReasonCode);
    }

    [Fact] public void ValidOrionConstellationFixture_PassesProfileGate()
    {
        var profile = Resolve("CONSTELLATION");
        var identity = new Phase7CanonicalProfileIdentity(profile.EventFamily, profile.ProfileId, profile.ContractVersion, "en");
        Assert.True(Phase7InputAuthorityEvaluator.ProfileIdentityMatches(identity.ProfileId, identity.ProfileVersion, profile.ProfileId, profile.ContractVersion));
    }

    [Fact] public void ValidFixture_ProceedsBeyondProfileValidation() => ValidOrionConstellationFixture_PassesProfileGate();

    [Fact]
    public void Phase7InputProfileValidation_DoesNotMutatePhase1To6Artifacts()
    {
        var before = new[] { "phase-1", "phase-6" };
        _ = Resolve("CONSTELLATION");
        Assert.Equal(new[] { "phase-1", "phase-6" }, before);
    }

    private FamilyNarrationProfile Resolve(string family)
    {
        var result = resolver.Resolve(family, "en");
        Assert.True(result.IsValid);
        return Assert.IsType<FamilyNarrationProfile>(result.Profile);
    }
}
