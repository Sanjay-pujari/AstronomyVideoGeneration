using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase19VideoQaAuthorityTests
{
    [Fact]
    public async Task Phase15CanonicalReaderReadsRootEntriesAndIgnoresCompatibilityItems()
    {
        var path = Path.Combine(Path.GetTempPath(), $"phase19-p15-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion":"phase15/1.0", "language":"en", "authorityChecksum":"p15-checksum",
              "entries":[{"sceneAudioUnitId":"audio-1","sceneId":"scene-1","sequence":1,"format":"Short",
                "language":"en","audioRelativePath":"audio/one.mp3","audioByteLength":12,
                "audioSha256":"abc","textChecksum":"text","actualAudioDurationMs":800}],
              "short":{"items":[{"deliberately":"wrong"}]}, "long":{"items":null}
            }
            """);
        try
        {
            var loaded = new List<string>();
            var entries = await Phase19VideoQaAuthorityPublisher.ReadPhase15Timeline(
                path, "p15-checksum", "en", loaded, CancellationToken.None);

            Assert.Equal("scene-1", Assert.Single(entries).Value.SceneId);
            Assert.Equal([path], loaded);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Phase15CanonicalReaderRejectsRootArrayWithStructuredSupportReason()
    {
        var path = Path.Combine(Path.GetTempPath(), $"phase19-p15-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "[]");
        try
        {
            var exception = await Assert.ThrowsAsync<Phase19AuthorityValidationException>(() =>
                Phase19VideoQaAuthorityPublisher.ReadPhase15Timeline(
                    path, "p15-checksum", "en", [], CancellationToken.None));

            Assert.Equal(Phase19ReasonCodes.SupportAuthorityInvalid, exception.ReasonCode);
            Assert.Contains("root object containing canonical entries[]", exception.Reason);
            Assert.DoesNotContain("could not be converted", exception.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResolveManifestPath_RejectsTraversalAndAbsolutePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "p19-authority");
        Assert.Throws<Phase19AuthorityValidationException>(() =>
            Phase19VideoQaAuthorityPublisher.ResolveManifestPath(root, "../escape.mp4", []));
        Assert.Throws<Phase19AuthorityValidationException>(() =>
            Phase19VideoQaAuthorityPublisher.ResolveManifestPath(root, Path.GetFullPath("escape.mp4"), []));
        Assert.Equal(Path.Combine(root, "short", "final.mp4"),
            Phase19VideoQaAuthorityPublisher.ResolveManifestPath(root, "short/final.mp4", []));
    }

    [Fact]
    public void ValidatePhase18Governance_RequiresChecksumAgreementAndFullGate()
    {
        var manifest = new Phase18Manifest("phase18.video-assembly/1.0", "en", ["Short"], "15", "16", "17",
            "render", "video", "audio", "subtitle", "tools", [], "authority", true, "Valid", true);
        using var diagnostics = JsonDocument.Parse("""{"publicationCommitted":true,"committedReadbackPassed":true,"authorityChecksum":"authority"}""");
        const string governed = """{"status":"Succeeded","publicationCommitted":true,"committedReadbackPassed":true,"committedStateValidationPassed":true,"semanticValidationPassed":true,"checksumValidationPassed":true,"manifestValidationPassed":true,"manifestValidationStatus":"Valid","validationStatus":"Valid","downstreamReady":true,"authorityChecksum":"authority"}""";
        using var publication = JsonDocument.Parse(governed);
        using var validation = JsonDocument.Parse(governed);
        Phase19VideoQaAuthorityPublisher.ValidatePhase18Governance(manifest, diagnostics.RootElement,
            publication.RootElement, validation.RootElement, "en", []);

        using var bad = JsonDocument.Parse(governed.Replace("authority\"", "different\""));
        var exception = Assert.Throws<Phase19AuthorityValidationException>(() =>
            Phase19VideoQaAuthorityPublisher.ValidatePhase18Governance(manifest, diagnostics.RootElement,
                bad.RootElement, validation.RootElement, "en", []));
        Assert.Equal(Phase19ReasonCodes.UpstreamPhase18Invalid, exception.ReasonCode);
    }

    [Fact]
    public void LumaMetric_IsDeterministic()
    {
        Assert.Equal(0, Phase19VideoQaAuthorityPublisher.MeanAbsoluteDifference([10, 20], [10, 20]));
        Assert.Equal(5, Phase19VideoQaAuthorityPublisher.MeanAbsoluteDifference([10, 20], [15, 25]));
    }

    [Theory]
    [InlineData("Static", false, 0.10, 0.20, 0.15, true)]
    [InlineData("SlowZoomIn", true, 1.30, 1.40, 2.50, true)]
    [InlineData("SlowZoomOut", true, 1.26, 0.80, 1.90, true)]
    [InlineData("ZoomInPanLeft", true, 2.10, 1.60, 3.20, true)]
    [InlineData("ZoomInPanRight", true, 1.80, 2.00, 3.40, true)]
    public void MotionQaPolicyV1_CalibratedFixtureMetricsCertifyMaterialChange(
        string fixture, bool moving, double earlyMiddle, double middleLate, double earlyLate, bool expected)
    {
        Assert.Equal(expected, Phase19VideoQaAuthorityPublisher.IsMaterialMotionDetected(moving,
            [earlyMiddle, middleLate, earlyLate]));
        Assert.False(string.IsNullOrWhiteSpace(fixture));
    }

    [Fact]
    public void MotionQaPolicyV1_FrozenNonStaticFailsClosedAndDoesNotDependOnDirection()
    {
        Assert.False(Phase19VideoQaAuthorityPublisher.IsMaterialMotionDetected(true, [.10, .12, .15]));
        Assert.True(Phase19VideoQaAuthorityPublisher.IsMaterialMotionDetected(true, [1.30, .20, 1.45]));
        Assert.Equal("MotionQaPolicyV1", Phase19VideoQaAuthorityPublisher.MotionMetricPolicy);
    }

    [Fact]
    public void MotionQaPolicyV1_RequiresThreeSamplePairs()
    {
        Assert.Throws<ArgumentException>(() =>
            Phase19VideoQaAuthorityPublisher.IsMaterialMotionDetected(true, [2, 3]));
    }

    [Fact]
    public void ValidateSrt_RejectsOverlappingOrReversedCues()
    {
        const string valid = "1\n00:00:00,000 --> 00:00:01,000\nHello\n\n2\n00:00:01,000 --> 00:00:02,000\nWorld\n";
        Phase19VideoQaAuthorityPublisher.ValidateSrt(valid, []);
        Assert.Throws<Phase19AuthorityValidationException>(() =>
            Phase19VideoQaAuthorityPublisher.ValidateSrt(valid.Replace("00:00:01,000 --> 00:00:02,000", "00:00:00,500 --> 00:00:00,400"), []));
    }
}
