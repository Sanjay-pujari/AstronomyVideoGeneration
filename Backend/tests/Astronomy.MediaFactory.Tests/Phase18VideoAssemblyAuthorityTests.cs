using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase18VideoAssemblyAuthorityTests
{
    [Fact]
    public void Phase18NeverUsesShortestToTerminateVideo()
    {
        Assert.False(Phase18VideoAssemblyAuthorityPublisher.CanonicalArgumentsAreSafe(["-shortest"]));
        Assert.False(Phase18VideoAssemblyAuthorityPublisher.CanonicalArgumentsAreSafe(["-af", "atrim=end=1"]));
        Assert.True(Phase18VideoAssemblyAuthorityPublisher.CanonicalArgumentsAreSafe(["-af", "apad=whole_dur=30"]));
    }

    [Fact]
    public void Phase18CodecPolicyIsFrozenForShortAndLong()
    {
        var policy = Phase18VideoAssemblyAuthorityPublisher.VideoPolicy;
        Assert.Equal((1080, 1920), (policy.ShortWidth, policy.ShortHeight));
        Assert.Equal((1280, 720), (policy.LongWidth, policy.LongHeight));
        Assert.Equal(30, policy.FramesPerSecond);
        Assert.Equal("libx264", policy.Encoder);
        Assert.Equal("yuv420p", policy.PixelFormat);
        Assert.Equal("veryfast", policy.Preset);
    }

    [Fact]
    public void Phase18AudioAndSubtitlePoliciesAreExplicit()
    {
        var audio = Phase18VideoAssemblyAuthorityPublisher.AudioPolicy;
        Assert.Equal(("aac", 48_000, 2, 192_000), (audio.Codec, audio.SampleRate, audio.Channels, audio.Bitrate));
        Assert.Equal(Phase18SubtitleMode.BurnInAndSidecar,
            Phase18VideoAssemblyAuthorityPublisher.SubtitlePolicy.EnglishMode);
        Assert.Equal(Phase18SubtitleMode.SidecarOnly,
            Phase18VideoAssemblyAuthorityPublisher.SubtitlePolicy.HindiMode);
    }

    [Fact]
    public void Phase18AcceptedAuthorityProjectsGovernedReasonCode()
    {
        Assert.Equal("P18_VIDEO_ASSEMBLY_AUTHORITY_ACCEPTED", Phase18ReasonCodes.Accepted);
        Assert.NotEqual(Phase18ReasonCodes.Accepted, Phase18ReasonCodes.UpstreamPhase17Invalid);
    }
}
