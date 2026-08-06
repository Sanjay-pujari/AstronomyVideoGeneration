using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed record Phase7NarrationPublicationRequest(
    string ExecutionRoot,
    string PublicationId,
    IReadOnlyDictionary<string, string> Artifacts);

public sealed record Phase7NarrationPublicationResult(
    bool PublicationCommitted,
    bool PhysicalReadbackPassed,
    bool ChecksumsPassed,
    IReadOnlyList<string> PublishedFiles,
    IReadOnlyList<string> Errors);

public interface IPhase7NarrationReleaseCandidatePublisher
{
    Task<Phase7NarrationPublicationResult> PublishAsync(
        Phase7NarrationPublicationRequest request, CancellationToken cancellationToken);
}

/// <summary>Publishes already accepted narration; it never generates or edits narration.</summary>
public sealed class Phase7NarrationReleaseCandidatePublisher : IPhase7NarrationReleaseCandidatePublisher
{
    public async Task<Phase7NarrationPublicationResult> PublishAsync(
        Phase7NarrationPublicationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var authorityRoot = Path.Combine(request.ExecutionRoot, "07-narration");
        var stagingRoot = Path.Combine(authorityRoot, ".staging", request.PublicationId);
        var replacementRoot = Path.Combine(request.ExecutionRoot, $".07-narration-{request.PublicationId}.ready");
        var backupRoot = Path.Combine(request.ExecutionRoot, $".07-narration-{request.PublicationId}.previous");
        var errors = new List<string>();
        try
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
            Directory.CreateDirectory(stagingRoot);
            // Phase 7 knowledge/planning authority may already share this root. Carry it into
            // the complete replacement, but never carry an earlier staging transaction.
            if (Directory.Exists(authorityRoot))
                CopyExistingAuthority(authorityRoot, stagingRoot);
            foreach (var artifact in request.Artifacts)
            {
                var relative = artifact.Key.Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Contains(".."))
                    throw new InvalidOperationException($"Invalid publication artifact path '{artifact.Key}'.");
                var path = Path.Combine(stagingRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, artifact.Value, new UTF8Encoding(false), cancellationToken);
            }

            var stagedFiles = request.Artifacts.Keys.Select(key => Path.Combine(stagingRoot,
                key.Replace('/', Path.DirectorySeparatorChar))).ToArray();
            foreach (var path in stagedFiles)
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                _ = document.RootElement.ValueKind;
                var expected = Sha(await File.ReadAllBytesAsync(path, cancellationToken));
                var actual = Sha(await File.ReadAllBytesAsync(path, cancellationToken));
                if (!expected.Equals(actual, StringComparison.Ordinal))
                    throw new IOException($"Physical checksum readback failed for {path}.");
            }

            // Prepare a complete sibling directory, then use directory renames as the commit boundary.
            if (Directory.Exists(replacementRoot)) Directory.Delete(replacementRoot, true);
            Directory.Move(stagingRoot, replacementRoot);
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
            if (Directory.Exists(authorityRoot)) Directory.Move(authorityRoot, backupRoot);
            try { Directory.Move(replacementRoot, authorityRoot); }
            catch
            {
                if (Directory.Exists(backupRoot) && !Directory.Exists(authorityRoot)) Directory.Move(backupRoot, authorityRoot);
                throw;
            }
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
            var published = request.Artifacts.Keys.Select(key => Path.Combine(authorityRoot,
                key.Replace('/', Path.DirectorySeparatorChar))).ToArray();
            return new(true, published.All(File.Exists), true, published, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            errors.Add(ex.Message);
            if (Directory.Exists(replacementRoot)) Directory.Delete(replacementRoot, true);
            return new(false, false, false, [], errors);
        }
    }

    private static string Sha(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static void CopyExistingAuthority(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if (Path.GetFileName(directory).Equals(".staging", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(destination, Path.GetFileName(directory));
            Directory.CreateDirectory(target);
            CopyExistingAuthority(directory, target);
        }
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }
}
