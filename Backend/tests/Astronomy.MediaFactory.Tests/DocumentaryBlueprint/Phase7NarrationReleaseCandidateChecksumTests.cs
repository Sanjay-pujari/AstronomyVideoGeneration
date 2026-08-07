using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using System.Text.Json;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class Phase7NarrationReleaseCandidateChecksumTests
{
    private static readonly Phase7AcceptedNarrationScene[] Scenes =
    [
        new("orion-short-01", 1, "orion-short-01", "sf-01", ["knowledge-1"], ["claim-1"], "Orion rises over the winter horizon.")
    ];

    [Fact]
    public void Phase8UsesPhase7ChecksumSemantics()
    {
        var checksum = Phase7NarrationReleaseCandidateChecksum.ComputeScenes(Scenes);

        Assert.Equal(checksum, Phase7NarrationReleaseCandidateChecksum.ComputeScenes(Scenes));
        Assert.Equal(64, checksum.Length);
    }

    [Fact]
    public void StoredChecksumFieldDoesNotHashIntoItself()
    {
        var checksum = Phase7NarrationReleaseCandidateChecksum.ComputeScenes(Scenes);
        var candidate = Candidate(checksum);

        Assert.Equal(candidate.DeterministicChecksum,
            Phase7NarrationReleaseCandidateChecksum.ComputeScenes(candidate.Scenes));
    }

    [Fact]
    public void DifferentDtoSerializationDoesNotDefineAuthorityChecksum()
    {
        var candidate = Candidate(Phase7NarrationReleaseCandidateChecksum.ComputeScenes(Scenes));
        var phase8ProjectionJson = JsonSerializer.Serialize(new
        {
            candidate.Scenes[0].SceneId,
            acceptedNarration = candidate.Scenes[0].NarrationText,
            mutablePhase8Field = "not-authority"
        });

        Assert.NotEqual(candidate.DeterministicChecksum, Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(phase8ProjectionJson))).ToLowerInvariant());
        Assert.Equal(candidate.DeterministicChecksum,
            Phase7NarrationReleaseCandidateChecksum.ComputeScenes(candidate.Scenes));
    }

    private static Phase7AcceptedReleaseCandidate Candidate(string checksum)
    {
        using var acceptance = JsonDocument.Parse("{\"accepted\":true}");
        return new("2.0", "attempt", DateTimeOffset.UnixEpoch, "candidate", "execution", "plan", "orion", "en", "Short",
            "blueprint", "blueprint-checksum", "short-blueprint", "variant-checksum", "story", "story-checksum",
            1, 1, Scenes, acceptance.RootElement.Clone(), checksum);
    }
}
