using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase19VideoQaAuthorityTests
{
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

    [Fact]
    public void ValidateSrt_RejectsOverlappingOrReversedCues()
    {
        const string valid = "1\n00:00:00,000 --> 00:00:01,000\nHello\n\n2\n00:00:01,000 --> 00:00:02,000\nWorld\n";
        Phase19VideoQaAuthorityPublisher.ValidateSrt(valid, []);
        Assert.Throws<Phase19AuthorityValidationException>(() =>
            Phase19VideoQaAuthorityPublisher.ValidateSrt(valid.Replace("00:00:01,000 --> 00:00:02,000", "00:00:00,500 --> 00:00:00,400"), []));
    }
}
