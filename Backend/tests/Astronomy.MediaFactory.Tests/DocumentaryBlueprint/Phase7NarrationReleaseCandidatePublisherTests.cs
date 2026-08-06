using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class Phase7NarrationReleaseCandidatePublisherTests
{
    [Fact]
    public async Task AcceptedLifecycle_Publishes07Narration_transactionally()
    {
        var root = Path.Combine(Path.GetTempPath(), "phase7-publication-" + Guid.NewGuid().ToString("N"));
        try
        {
            var artifacts = RequiredArtifacts("new");
            var result = await new Phase7NarrationReleaseCandidatePublisher().PublishAsync(
                new(root, "publication-1", artifacts), CancellationToken.None);

            result.PublicationCommitted.Should().BeTrue();
            result.PhysicalReadbackPassed.Should().BeTrue();
            result.ChecksumsPassed.Should().BeTrue();
            artifacts.Keys.Should().OnlyContain(relative => File.Exists(Path.Combine(root, "07-narration", relative)));
            Directory.Exists(Path.Combine(root, "07-narration", ".staging")).Should().BeFalse();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PublicationFailure_DoesNotPartiallyCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), "phase7-publication-" + Guid.NewGuid().ToString("N"));
        try
        {
            var authority = Path.Combine(root, "07-narration");
            Directory.CreateDirectory(authority);
            await File.WriteAllTextAsync(Path.Combine(authority, "narration-manifest.json"), "{\"release\":\"previous\"}");
            var invalid = RequiredArtifacts("new").ToDictionary(pair => pair.Key, pair => pair.Value);
            invalid["short/accepted-release-candidate.json"] = "not-json";

            var result = await new Phase7NarrationReleaseCandidatePublisher().PublishAsync(
                new(root, "publication-2", invalid), CancellationToken.None);

            result.PublicationCommitted.Should().BeFalse();
            (await File.ReadAllTextAsync(Path.Combine(authority, "narration-manifest.json"))).Should().Contain("previous");
            File.Exists(Path.Combine(authority, "long", "accepted-release-candidate.json")).Should().BeFalse();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static IReadOnlyDictionary<string, string> RequiredArtifacts(string value) =>
        new Dictionary<string, string>
        {
            ["long/accepted-release-candidate.json"] = $"{{\"value\":\"{value}\"}}",
            ["long/acceptance-record.json"] = "{}",
            ["short/accepted-release-candidate.json"] = $"{{\"value\":\"{value}\"}}",
            ["short/acceptance-record.json"] = "{}",
            ["revision-history.json"] = "{}",
            ["narration-manifest.json"] = "{}",
            ["narration-certification.json"] = "{}"
        };
}
