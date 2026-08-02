using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class StoryFrameContractCompatibilityTests
{
    [Theory]
    [InlineData("1.0", true)] [InlineData("1.1", true)] [InlineData("1.2", true)]
    [InlineData(null, false)] [InlineData("", false)] [InlineData("1", false)]
    [InlineData("0.9", false)] [InlineData("2.0", false)] [InlineData("-1.0", false)] [InlineData("1.-1", false)]
    [InlineData("01.1", false)] [InlineData("1.01", false)]
    public void Compatibility_is_an_explicit_allow_list(string? version, bool expected) =>
        Assert.Equal(expected, StoryFrameContractCompatibility.IsSupported(version));

    [Theory]
    [InlineData("1.0", 1, 0)] [InlineData("1.1", 1, 1)] [InlineData("1.2", 1, 2)]
    public void Contract_version_parser_accepts_canonical_major_minor(string value, int major, int minor)
    {
        Assert.True(StoryFrameContractVersion.TryParse(value, out var parsed));
        Assert.Equal((major, minor), (parsed.Major, parsed.Minor));
    }

    [Fact]
    public void Authority_identity_uses_one_canonical_constructor()
    {
        Assert.Equal("story-frames-execution", StoryFrameAuthorityIdentity.BuildAuthorityId("execution"));
        Assert.True(StoryFrameAuthorityIdentity.IsExpectedAuthorityId("story-frames-execution", "execution"));
        Assert.False(StoryFrameAuthorityIdentity.IsExpectedAuthorityId("prefix-execution", "execution"));
    }
}
