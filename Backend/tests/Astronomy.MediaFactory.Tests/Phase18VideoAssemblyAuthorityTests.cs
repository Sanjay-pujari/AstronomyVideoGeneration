using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using System.Text.Json;

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

    [Fact]
    public async Task Phase18AcceptsCurrentPhase15ArtifactFieldOwnership()
    {
        using var fixture = Phase15Fixture.Create();
        var snapshot = await Phase18VideoAssemblyAuthorityPublisher.LoadPhase15AuthorityAsync(
            fixture.Files, "en", CancellationToken.None);

        Assert.True(snapshot.SemanticValidationPassed);
        Assert.True(snapshot.PublicationCommitted);
        Assert.Equal(fixture.Checksum, snapshot.AuthorityChecksum);
        Assert.Equal(fixture.Files, snapshot.LoadedAuthorityArtifacts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Phase18FailsClosedOnInvalidCanonicalSemanticOwner(bool? semanticValidationPassed)
    {
        using var fixture = Phase15Fixture.Create(semanticValidationPassed);
        var error = await Assert.ThrowsAsync<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.LoadPhase15AuthorityAsync(fixture.Files, "en", CancellationToken.None));

        Assert.Equal(Phase18ReasonCodes.UpstreamPhase15Invalid, error.ReasonCode);
        Assert.Equal(fixture.Files, error.LoadedAuthorityArtifacts);
        Assert.Contains("semanticValidationPassed", error.Message);
    }

    [Fact]
    public async Task Phase18Phase15ChecksumMismatchReportsLoadedAuthorityArtifacts()
    {
        using var fixture = Phase15Fixture.Create(reportChecksum: "different");
        var error = await Assert.ThrowsAsync<Phase18AuthorityValidationException>(() =>
            Phase18VideoAssemblyAuthorityPublisher.LoadPhase15AuthorityAsync(fixture.Files, "en", CancellationToken.None));

        Assert.Equal(Phase18ReasonCodes.UpstreamPhase15Invalid, error.ReasonCode);
        Assert.Equal(3, error.LoadedAuthorityArtifacts.Count);
    }

    private sealed class Phase15Fixture : IDisposable
    {
        private Phase15Fixture(string root, string[] files, string checksum)
        { Root = root; Files = files; Checksum = checksum; }
        public string Root { get; }
        public string[] Files { get; }
        public string Checksum { get; }

        public static Phase15Fixture Create(bool? semanticValidationPassed = true, string? reportChecksum = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "phase18-phase15-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            const string checksum = "phase15-checksum";
            const string source = "phase14-checksum";
            var files = new[] { "phase15-manifest.json", "phase15-publication-report.json", "phase-15-validation.json" }
                .Select(name => Path.Combine(root, name)).ToArray();
            // These shapes intentionally mirror the frozen publisher: semantic/checksum/manifest
            // gates are absent from both manifest and publication report.
            File.WriteAllText(files[0], JsonSerializer.Serialize(new { schemaVersion = "phase15.manifest/1.0",
                language = "en", sourcePhase14AuthorityChecksum = source, authorityChecksum = checksum,
                validationStatus = "Valid", publicationCommitted = true, downstreamReady = true }));
            File.WriteAllText(files[1], JsonSerializer.Serialize(new { schemaVersion = "phase15.publication/1.0",
                candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true,
                committedReadbackPassed = true, committedStateValidationPassed = true, downstreamReady = true,
                sourcePhase14AuthorityChecksum = source, authorityChecksum = reportChecksum ?? checksum }));
            var validation = new Dictionary<string, object?> { ["phaseNo"] = 15, ["status"] = "Succeeded",
                ["reasonCode"] = "P15_TTS_AUTHORITY_ACCEPTED", ["sourcePhase14AuthorityChecksum"] = source,
                ["authorityChecksum"] = checksum, ["validationStatus"] = "Valid",
                ["semanticValidationPassed"] = semanticValidationPassed, ["checksumValidationPassed"] = true,
                ["manifestValidationPassed"] = true, ["downstreamReady"] = true };
            if (semanticValidationPassed is null) validation.Remove("semanticValidationPassed");
            File.WriteAllText(files[2], JsonSerializer.Serialize(validation));
            return new(root, files, checksum);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
