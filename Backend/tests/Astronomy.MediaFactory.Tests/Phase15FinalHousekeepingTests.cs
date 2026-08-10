using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase15FinalHousekeepingTests
{
    [Fact]
    public void Phase15SuccessfulExecutionLeavesNoTransactionGuidDirectory()
        => AssertTransactionCleanup(remainsCommittedAuthority: false);

    [Fact]
    public void Phase15FailedExecutionLeavesNoTransactionGuidDirectory()
        => AssertTransactionCleanup(remainsCommittedAuthority: false);

    [Fact]
    public void Phase15DoesNotDeleteCommittedAuthorityDuringStagingCleanup()
        => AssertTransactionCleanup(remainsCommittedAuthority: true);

    [Fact]
    public void Phase15SuccessfulShortAuthorityProjectsShortTtsGeneratedTrue()
    {
        using var fixture = new AuthorityFixture("Short");
        Assert.Equal((true, false), ProductionPipelineExecutionService.ResolveCommittedPhase15TtsFormats(fixture.Root, "en"));
    }

    [Fact]
    public void Phase15SuccessfulLongAuthorityProjectsLongTtsGeneratedTrue()
    {
        using var fixture = new AuthorityFixture("Long");
        Assert.Equal((false, true), ProductionPipelineExecutionService.ResolveCommittedPhase15TtsFormats(fixture.Root, "en"));
    }

    [Fact]
    public void Phase15AggregateTtsFlagsComeFromCommittedAuthority()
    {
        using var fixture = new AuthorityFixture("Short", "Long");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "tts", "short"));
        File.WriteAllBytes(Path.Combine(fixture.Root, "tts", "short", "narration.mp3"), [1]);
        Assert.Equal((true, true), ProductionPipelineExecutionService.ResolveCommittedPhase15TtsFormats(fixture.Root, "en"));

        File.Delete(Path.Combine(fixture.Root, "15-tts", "en", "long", "long.mp3"));
        Assert.Equal((true, false), ProductionPipelineExecutionService.ResolveCommittedPhase15TtsFormats(fixture.Root, "en"));
    }

    private static void AssertTransactionCleanup(bool remainsCommittedAuthority)
    {
        var root = Path.Combine(Path.GetTempPath(), "phase15-housekeeping-" + Guid.NewGuid().ToString("N"));
        try
        {
            var staging = Path.Combine(root, "15-tts", ".staging");
            var transaction = Path.Combine(staging, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(transaction, "en"));
            var committed = Path.Combine(root, "15-tts", "en");
            if (remainsCommittedAuthority)
            {
                Directory.CreateDirectory(committed);
                File.WriteAllText(Path.Combine(committed, "phase15-manifest.json"), "committed");
            }

            ProductionPipelineExecutionService.CleanupPhase15Transaction(staging, transaction);

            Assert.False(Directory.Exists(transaction));
            Assert.False(Directory.Exists(staging));
            Assert.Equal(remainsCommittedAuthority, Directory.Exists(committed));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class AuthorityFixture : IDisposable
    {
        public AuthorityFixture(params string[] formats)
        {
            Root = Path.Combine(Path.GetTempPath(), "phase15-flags-" + Guid.NewGuid().ToString("N"));
            var authority = Path.Combine(Root, "15-tts", "en");
            Directory.CreateDirectory(authority);
            File.WriteAllText(Path.Combine(authority, "phase15-manifest.json"), JsonSerializer.Serialize(new
                { publicationCommitted = true, downstreamReady = true, validationStatus = "Valid" }));
            var entries = formats.Select(format =>
            {
                var relative = $"15-tts/en/{format.ToLowerInvariant()}/{format.ToLowerInvariant()}.mp3";
                var audio = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(audio)!);
                File.WriteAllBytes(audio, [1, 2, 3]);
                return new { format, audioRelativePath = relative };
            }).ToArray();
            File.WriteAllText(Path.Combine(authority, "tts-timeline.json"), JsonSerializer.Serialize(new { entries }));
        }

        public string Root { get; }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
