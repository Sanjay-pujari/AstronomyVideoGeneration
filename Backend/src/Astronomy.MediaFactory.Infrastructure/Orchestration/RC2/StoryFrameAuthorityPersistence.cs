using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public interface IStoryFrameFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    void MoveDirectory(string source, string destination);
    void DeleteDirectory(string path, bool recursive);
    IEnumerable<string> EnumerateDirectories(string path, string searchPattern);
    DateTimeOffset GetDirectoryLastWriteTimeUtc(string path);
    Stream OpenRead(string path);
}

public sealed class StoryFrameFileSystem : IStoryFrameFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern) =>
        Directory.Exists(path) ? Directory.EnumerateDirectories(path, searchPattern, SearchOption.TopDirectoryOnly) : [];
    public DateTimeOffset GetDirectoryLastWriteTimeUtc(string path) => Directory.GetLastWriteTimeUtc(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
}

public sealed record StoryFrameCommitRequest(string ActiveDirectory, string StagingDirectory, string BackupDirectory);
public sealed record StoryFrameCommitResult(bool BackupRetained, IReadOnlyList<string> Warnings);

public interface IStoryFrameAuthorityCommitter
{
    Task<StoryFrameCommitResult> CommitAsync(StoryFrameCommitRequest request, CancellationToken cancellationToken);
}

public sealed class StoryFrameAuthorityCommitter(IStoryFrameFileSystem fileSystem) : IStoryFrameAuthorityCommitter
{
    public Task<StoryFrameCommitResult> CommitAsync(StoryFrameCommitRequest request, CancellationToken cancellationToken)
    {
        if (!fileSystem.DirectoryExists(request.StagingDirectory))
            throw new DirectoryNotFoundException($"Story Frame staging directory does not exist: {request.StagingDirectory}");
        cancellationToken.ThrowIfCancellationRequested();
        var movedOld = false;
        var committed = false;
        try
        {
            // The two moves are deliberately an uncancellable critical swap. Any exception before
            // the second move completes restores the previous complete authority below.
            if (fileSystem.DirectoryExists(request.ActiveDirectory))
            {
                if (fileSystem.DirectoryExists(request.BackupDirectory)) fileSystem.DeleteDirectory(request.BackupDirectory, true);
                fileSystem.MoveDirectory(request.ActiveDirectory, request.BackupDirectory);
                movedOld = true;
            }
            fileSystem.MoveDirectory(request.StagingDirectory, request.ActiveDirectory);
            committed = true;
        }
        catch
        {
            if (!committed && movedOld && !fileSystem.DirectoryExists(request.ActiveDirectory)
                && fileSystem.DirectoryExists(request.BackupDirectory))
                fileSystem.MoveDirectory(request.BackupDirectory, request.ActiveDirectory);
            throw;
        }

        var warnings = new List<string>();
        if (movedOld && fileSystem.DirectoryExists(request.BackupDirectory))
        {
            try { fileSystem.DeleteDirectory(request.BackupDirectory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warnings.Add($"New Story Frame authority committed; backup cleanup was retained: {ex.Message}"); }
        }
        return Task.FromResult(new StoryFrameCommitResult(fileSystem.DirectoryExists(request.BackupDirectory), warnings));
    }
}

public interface IStoryFrameClock { DateTimeOffset UtcNow { get; } }
public sealed class StoryFrameClock : IStoryFrameClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public sealed record StoryFrameRecoveryRequest(string OutputRoot, string ActiveDirectory,
    StoryFrameIntegrationRequest ValidationRequest, StoryFrameValidationCompatibilityContext CompatibilityContext,
    TimeSpan StaleAge);
public sealed record StoryFrameRecoveryResult(bool BackupRestored, IReadOnlyList<string> DeletedDirectories,
    IReadOnlyList<string> Warnings);

public interface IStoryFrameTemporaryDirectoryRecovery
{
    Task<StoryFrameRecoveryResult> RecoverAsync(StoryFrameRecoveryRequest request, CancellationToken cancellationToken);
}

public sealed class StoryFrameTemporaryDirectoryRecovery(IStoryFrameFileSystem fileSystem, IStoryFrameClock clock)
    : IStoryFrameTemporaryDirectoryRecovery
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public async Task<StoryFrameRecoveryResult> RecoverAsync(StoryFrameRecoveryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleted = new List<string>();
        var warnings = new List<string>();
        var staging = fileSystem.EnumerateDirectories(request.OutputRoot, ".06-story-frames-staging-*").ToArray();
        var backups = fileSystem.EnumerateDirectories(request.OutputRoot, ".06-story-frames-backup-*").ToArray();
        bool Stale(string path) => clock.UtcNow - fileSystem.GetDirectoryLastWriteTimeUtc(path) >= request.StaleAge;

        if (!fileSystem.DirectoryExists(request.ActiveDirectory))
        {
            foreach (var backup in backups.OrderByDescending(fileSystem.GetDirectoryLastWriteTimeUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await IsValidAsync(backup, request, cancellationToken)) continue;
                fileSystem.MoveDirectory(backup, request.ActiveDirectory);
                foreach (var path in staging.Where(Stale).Concat(backups.Where(x => x != backup && Stale(x)))) Delete(path);
                return new(true, deleted, warnings);
            }
        }

        foreach (var path in staging.Where(Stale).Concat(backups.Where(Stale)))
            try { Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(ex.Message); }
        return new(false, deleted, warnings);

        void Delete(string path) { if (fileSystem.DirectoryExists(path)) { fileSystem.DeleteDirectory(path, true); deleted.Add(path); } }
    }

    private async Task<bool> IsValidAsync(string directory, StoryFrameRecoveryRequest request, CancellationToken token)
    {
        try
        {
            async Task<T> Read<T>(string name)
            {
                await using var stream = fileSystem.OpenRead(Path.Combine(directory, name));
                return (await System.Text.Json.JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, token))!;
            }
            var result = new StoryFrameIntegrationResult(await Read<StoryFramesAuthority>("story-frames.json"),
                await Read<StoryFrameIndex>("story-frame-index.json"), await Read<StoryFrameDiagnostics>("story-frame-diagnostics.json"));
            return StoryFrameArtifactValidator.ValidateDetailed(result, request.ValidationRequest, request.CompatibilityContext).IsValid;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return false; }
    }
}

public static class StoryFramePathSecurity
{
    public static bool IsCanonicalContainedPath(string root, string candidate, string expectedDirectoryName, string expectedFileName)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate)
            || HasAlternateDataStream(candidate)
            || candidate.Contains("staging", StringComparison.OrdinalIgnoreCase)
            || candidate.Contains("backup", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(candidate);
            var expectedParent = Path.Combine(canonicalRoot, expectedDirectoryName);
            var rootPrefix = canonicalRoot + Path.DirectorySeparatorChar;
            return full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetDirectoryName(full), expectedParent, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(full), expectedFileName, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static bool HasAlternateDataStream(string path)
    {
        // A colon is valid only as the drive designator in a Windows rooted path. On Unix,
        // Windows paths are not canonical local paths and any colon is therefore rejected.
        var firstColon = path.IndexOf(':');
        if (firstColon < 0) return false;
        var hasDriveDesignator = firstColon == 1 && char.IsAsciiLetter(path[0])
            && path.Length > 2 && (path[2] == '\\' || path[2] == '/');
        return !hasDriveDesignator || path.IndexOf(':', firstColon + 1) >= 0;
    }
}
