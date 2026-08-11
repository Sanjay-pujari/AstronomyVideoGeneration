using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase18PublicationFilesystemTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "phase18-publication-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task NestedCaptionFilesAreEnumeratedAndPublishedOverExistingAuthority()
    {
        var stage = Candidate("tx-success");
        var canonical = Canonical();
        var backup = Backup("tx-success");
        CreateCandidate(stage, "new");
        Write(canonical, "prior-authority.txt", "old");

        var files = Phase18VideoAssemblyAuthorityPublisher.EnumeratePublicationFiles(stage);

        Assert.Equal(6, files.Count);
        Assert.All(files, AssertFileNotDirectory);
        await Phase16DurationCalibrationPublisher.ReplaceCommittedDirectoryAsync(stage, canonical, backup,
            () => Task.CompletedTask);
        Assert.False(File.Exists(Path.Combine(canonical, "prior-authority.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(canonical, "short", "captions", "en.ass")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(canonical, "long", "captions", "en.srt")));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public async Task FailedCommittedReadbackRestoresExistingAuthority()
    {
        var stage = Candidate("tx-rollback");
        var canonical = Canonical();
        var backup = Backup("tx-rollback");
        CreateCandidate(stage, "new");
        Write(canonical, "prior-authority.txt", "old");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Phase16DurationCalibrationPublisher.ReplaceCommittedDirectoryAsync(stage, canonical, backup,
                () => throw new InvalidDataException("injected committed readback failure")));

        Assert.Equal("old", File.ReadAllText(Path.Combine(canonical, "prior-authority.txt")));
        Assert.False(Directory.Exists(stage));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void DirectoryCannotBeUsedAsManifestFile()
    {
        var stage = Candidate("tx-types");
        CreateCandidate(stage, "bytes");

        var files = Phase18VideoAssemblyAuthorityPublisher.EnumeratePublicationFiles(stage);

        Assert.DoesNotContain(files, path => Directory.Exists(path));
        Assert.Equal(6, files.Count);
    }

    private string Candidate(string transaction) => Path.Combine(root, "18-video-assembly", ".staging", transaction, "en");
    private string Backup(string transaction) => Path.Combine(root, "18-video-assembly", ".backup", transaction, "en");
    private string Canonical() => Path.Combine(root, "18-video-assembly", "en");

    private static void CreateCandidate(string candidate, string value)
    {
        foreach (var format in new[] { "short", "long" })
        {
            Write(Path.Combine(candidate, format), "final.mp4", value);
            Write(Path.Combine(candidate, format, "captions"), "en.srt", value);
            Write(Path.Combine(candidate, format, "captions"), "en.ass", value);
        }
    }

    private static void Write(string directory, string file, string value)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, file), value);
    }

    private static void AssertFileNotDirectory(string path)
    {
        Assert.True(File.Exists(path));
        Assert.False(Directory.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
