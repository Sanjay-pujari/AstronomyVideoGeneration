using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7ProfileIdentityIntegrationTests
{
    [Fact]
    public void PublishedProfile_MatchesExpectedProfile()
        => Assert.True(Phase7InputAuthorityEvaluator.ProfileIdentityMatches("orion-gold", "1.0", "orion-gold", "1.0"));

    [Fact]
    public void ProfileVersionMismatch_Fails()
        => Assert.False(Phase7InputAuthorityEvaluator.ProfileIdentityMatches("orion-gold", "1.0", "orion-gold", "1.1"));

    [Fact]
    public void ProfileIdMismatch_Fails()
        => Assert.False(Phase7InputAuthorityEvaluator.ProfileIdentityMatches("orion-gold", "1.0", "constellation-documentary-v1", "1.0"));

    [Fact]
    public void FamilyResolver_ReturnsCanonicalProfile()
    {
        var resolved = new FamilyNarrationProfileResolver().Resolve("CONSTELLATION", "en");

        Assert.True(resolved.IsValid);
        Assert.Equal("constellation-documentary-v1", resolved.Profile!.ProfileId);
    }

    [Fact]
    public void LegacyStoryFrameProfile_IsNotTheCanonicalNarrationProfile()
    {
        const string phase4ProfileId = "orion-gold";
        const string phase4ProfileVersion = "1.0";
        const string publishedPhase6ProfileId = "orion-gold";
        const string publishedPhase6ProfileVersion = "1.0";

        Assert.True(Phase7InputAuthorityEvaluator.ProfileIdentityMatches(
            phase4ProfileId, phase4ProfileVersion, publishedPhase6ProfileId, publishedPhase6ProfileVersion));
        Assert.NotEqual(phase4ProfileId,
            new FamilyNarrationProfileResolver().Resolve("CONSTELLATION", "en").Profile!.ProfileId);
    }
}
