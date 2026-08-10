using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase15FinalHousekeepingTests
{
    [Fact]
    public void Phase15SuccessRemovesCurrentTransactionDirectory()
        => AssertTransactionCleanup(remainsCommittedAuthority: false);

    [Fact]
    public void Phase15FailureRemovesCurrentTransactionDirectory()
        => AssertTransactionCleanup(remainsCommittedAuthority: false);

    [Fact]
    public void Phase15StagingCleanupNeverDeletesCommittedEnglishAuthority()
        => AssertTransactionCleanup(remainsCommittedAuthority: true);

    [Fact]
    public void Phase15StartupRemovesEmptyStaleTransactionDirectories()
    {
        using var fixture = new TemporaryDirectory("phase15-stale-");
        var staging = Path.Combine(fixture.Root, "15-tts", ".staging");
        var stale = Path.Combine(staging, Guid.NewGuid().ToString("N"));
        var nonEmpty = Path.Combine(staging, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(nonEmpty);
        File.WriteAllText(Path.Combine(nonEmpty, "candidate.json"), "retain");

        ProductionPipelineExecutionService.CleanupPhase15Transaction(staging, null);

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(nonEmpty));
    }

    [Fact]
    public void Phase15RemovesEmptyStagingParent()
    {
        using var fixture = new TemporaryDirectory("phase15-parent-");
        var staging = Path.Combine(fixture.Root, "15-tts", ".staging");
        Directory.CreateDirectory(staging);
        ProductionPipelineExecutionService.CleanupPhase15Transaction(staging, null);
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void Phase15StagingCleanupNeverDeletesCommittedHindiAuthority()
    {
        using var fixture = new TemporaryDirectory("phase15-hi-");
        var committed = Path.Combine(fixture.Root, "15-tts", "hi");
        var staging = Path.Combine(fixture.Root, "15-tts", ".staging");
        Directory.CreateDirectory(committed);
        Directory.CreateDirectory(Path.Combine(staging, Guid.NewGuid().ToString("N")));
        File.WriteAllText(Path.Combine(committed, "phase15-manifest.json"), "committed");
        ProductionPipelineExecutionService.CleanupPhase15Transaction(staging, null);
        Assert.True(File.Exists(Path.Combine(committed, "phase15-manifest.json")));
    }

    [Fact]
    public void Phase15CommittedShortAuthorityProjectsShortTtsGeneratedTrue()
    {
        using var fixture = new AuthorityFixture("Short");
        Assert.Equal((true, false), ProductionPipelineExecutionService.ResolveCommittedPhase15TtsFormats(fixture.Root, "en"));
    }

    [Fact]
    public void Phase15CommittedLongAuthorityProjectsLongTtsGeneratedTrue()
    {
        using var fixture = new AuthorityFixture("Long");
        Assert.Equal((false, true), ProductionPipelineExecutionService.ResolveCommittedPhase15TtsFormats(fixture.Root, "en"));
    }

    [Fact]
    public void Phase15CommittedShortAndLongProjectsBothFlagsTrue()
    {
        using var fixture = new AuthorityFixture("Short", "Long");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "tts", "short"));
        File.WriteAllBytes(Path.Combine(fixture.Root, "tts", "short", "narration.mp3"), [1]);
        Assert.Equal((true, true), ProductionPipelineExecutionService.ResolveCommittedPhase15TtsFormats(fixture.Root, "en"));

    }

    [Fact]
    public void Phase15ReuseProjectsTtsGeneratedFlagsFromCommittedAuthority()
    {
        using var fixture = new AuthorityFixture("Short", "Long");
        Assert.Equal((true, true), ProductionPipelineExecutionService.ResolveCommittedPhase15TtsFormats(fixture.Root, "en"));
    }

    [Fact]
    public void Phase15FailureDoesNotProjectFlagsFromStaging()
    {
        using var fixture = new TemporaryDirectory("phase15-failed-");
        var stagedAudio = Path.Combine(fixture.Root, "15-tts", ".staging", Guid.NewGuid().ToString("N"), "en", "short", "unit.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedAudio)!);
        File.WriteAllBytes(stagedAudio, [1, 2, 3]);
        Assert.Equal((false, false), ProductionPipelineExecutionService.ResolveCommittedPhase15TtsFormats(fixture.Root, "en"));
    }

    [Fact]
    public void Phase15ShortOnlyDoesNotSetLongFlag()
    {
        using var fixture = new AuthorityFixture("Short");
        Assert.Equal((true, false), ProductionPipelineExecutionService.ResolveCommittedPhase15TtsFormats(fixture.Root, "en"));
    }

    [Fact]
    public void Phase15LongOnlyDoesNotSetShortFlag()
    {
        using var fixture = new AuthorityFixture("Long");
        Assert.Equal((false, true), ProductionPipelineExecutionService.ResolveCommittedPhase15TtsFormats(fixture.Root, "en"));
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(string prefix) => Root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        public string Root { get; }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
