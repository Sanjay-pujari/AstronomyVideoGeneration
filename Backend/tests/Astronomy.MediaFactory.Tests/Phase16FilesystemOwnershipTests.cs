using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase16FilesystemOwnershipTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"phase16-fs-{Guid.NewGuid():N}");

    [Fact]
    public async Task Phase16OverwriteExistingCommittedLanguageDirectoryDoesNotThrowAccessDenied()
    {
        var final = LanguageRoot();
        var stage = Stage("tx-overwrite");
        var backup = Backup("tx-overwrite");
        Write(final, "authority.json", "old");
        Write(stage, "authority.json", "new");

        await Phase16DurationCalibrationPublisher.ReplaceCommittedDirectoryAsync(stage, final, backup,
            () => Task.CompletedTask);

        Assert.Equal("new", File.ReadAllText(Path.Combine(final, "authority.json")));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void Phase16CleanupTreatsOwnedLanguageRootAsDirectory()
    {
        var final = LanguageRoot();
        Write(final, "authority.json", "old");
        var target = new PhaseOutputTarget(16, final, true, "16-duration-calibration/en", "Authority",
            "Phase16", true, false, false, false, true);

        var deleted = PhaseOwnedCleanupExecutor.TryDelete(target, 16, 16, new HashSet<int> { 16 }, [], [], []);

        Assert.True(deleted);
        Assert.False(Directory.Exists(final));
    }

    [Fact]
    public void Phase16OuterCleanupDoesNotPreDeleteCommittedTransactionalAuthority()
    {
        var final = LanguageRoot();
        Write(final, "authority.json", "old");
        var target = new PhaseOutputTarget(16, final, true, "16-duration-calibration/en", "Authority",
            "Phase16", true, false, false, false, false);

        PhaseOwnedCleanupExecutor.TryDelete(target, 16, 16, new HashSet<int> { 16 }, [], [], []);

        Assert.Equal("old", File.ReadAllText(Path.Combine(final, "authority.json")));
    }

    [Fact]
    public void Phase16OuterCleanupMayDeleteOnlyPhase16ValidationProjection()
    {
        var final = LanguageRoot();
        var validation = Path.Combine(root, "validation", "phase-16-validation.json");
        Write(final, "authority.json", "old");
        Write(Path.GetDirectoryName(validation)!, Path.GetFileName(validation), "{}");
        var authority = new PhaseOutputTarget(16, final, true, "16-duration-calibration/en", "Authority",
            "Phase16", true, false, false, false, false);
        var projection = new PhaseOutputTarget(16, validation, false, "validation/phase-16-validation.json", "Validation",
            "Phase16", false, false, true, false, true);
        var phases = new HashSet<int> { 16 };

        PhaseOwnedCleanupExecutor.TryDelete(authority, 16, 16, phases, [], [], []);
        PhaseOwnedCleanupExecutor.TryDelete(projection, 16, 16, phases, [], [], []);

        Assert.True(Directory.Exists(final));
        Assert.False(File.Exists(validation));
    }

    [Fact]
    public async Task Phase16FailedCandidateRestoresPreviousAuthorityAndCleansStage()
    {
        var final = LanguageRoot();
        var stage = Stage("tx-fail");
        var backup = Backup("tx-fail");
        Write(final, "authority.json", "old-byte-identical");
        Write(stage, "authority.json", "invalid-candidate");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Phase16DurationCalibrationPublisher.ReplaceCommittedDirectoryAsync(stage, final, backup,
                () => throw new InvalidDataException("readback failed")));

        Assert.Equal("old-byte-identical", File.ReadAllText(Path.Combine(final, "authority.json")));
        Assert.False(Directory.Exists(stage));
        Assert.False(Directory.Exists(backup));
    }

    private string LanguageRoot() => Path.Combine(root, "16-duration-calibration", "en");
    private string Stage(string tx) => Path.Combine(root, "16-duration-calibration", ".staging", tx, "en");
    private string Backup(string tx) => Path.Combine(root, "16-duration-calibration", ".backup", tx, "en");
    private static void Write(string directory, string name, string value)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), value);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
