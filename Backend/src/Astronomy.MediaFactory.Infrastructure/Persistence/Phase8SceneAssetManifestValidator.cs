using System.Security.Cryptography;
using Astronomy.MediaFactory.Core;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class Phase8SceneAssetManifestValidator : IPhase8SceneAssetManifestValidator
{
    public async Task<Phase8ManifestValidationResult> ValidateAsync(SceneAssetManifest manifest,
        Phase8AuthorityInput authority, string outputRoot, CancellationToken cancellationToken)
    {
        var errors = new List<string>(); var codes = new HashSet<string>(StringComparer.Ordinal);
        void Error(string code, string message) { codes.Add(code); errors.Add(message); }
        if (manifest.PlanId != authority.PlanId || manifest.ExecutionId != authority.ExecutionId || manifest.EventId != authority.EventId || manifest.Language != authority.Language)
            Error(Phase8AuthorityReasonCodes.IdentityMismatch, "Manifest execution identity does not match authority.");
        if (manifest.DocumentaryBlueprintChecksum != authority.DocumentaryBlueprintChecksum || manifest.StoryFrameManifestChecksum != authority.StoryFrameManifestChecksum
            || manifest.LongNarrationReleaseCandidateChecksum != authority.LongNarrationReleaseCandidateChecksum || manifest.ShortNarrationReleaseCandidateChecksum != authority.ShortNarrationReleaseCandidateChecksum)
            Error(Phase8AuthorityReasonCodes.ChecksumMismatch, "Manifest upstream checksums do not match current authority.");
        var expected = authority.LongScenes.Concat(authority.ShortScenes).ToDictionary(x => $"{x.Variant}:{x.SceneId}", StringComparer.Ordinal);
        var actual = manifest.Assets.ToDictionary(x => $"{x.Variant}:{x.SceneId}", StringComparer.Ordinal);
        foreach (var key in expected.Keys.Except(actual.Keys)) Error(Phase8AuthorityReasonCodes.SceneLineageMismatch, $"Missing asset '{key}'.");
        foreach (var key in actual.Keys.Except(expected.Keys)) Error(Phase8AuthorityReasonCodes.SceneLineageMismatch, $"Extra asset '{key}'.");
        foreach (var (key, item) in actual)
        {
            if (!expected.TryGetValue(key, out var scene)) continue;
            if (item.BlueprintSceneId != scene.BlueprintSceneId || item.StoryFrameId != scene.StoryFrameId || item.SceneOrder != scene.SceneOrder)
                Error(Phase8AuthorityReasonCodes.SceneLineageMismatch, $"Lineage differs for '{key}'.");
            if (string.IsNullOrWhiteSpace(item.ProviderType) || string.IsNullOrWhiteSpace(item.ProviderStatus) || string.IsNullOrWhiteSpace(item.SourceInstructionId) || string.IsNullOrWhiteSpace(item.SemanticIdentity))
                Error(Phase8AuthorityReasonCodes.NotCommitted, $"Provider/source evidence is incomplete for '{key}'.");
            if (item.SharedAsset && (string.IsNullOrWhiteSpace(item.SharedAssetOwner) || item.SharedAssetConsumers.Count == 0))
                Error(Phase8AuthorityReasonCodes.SceneLineageMismatch, $"Shared asset ownership is incomplete for '{key}'.");
            var path = Path.GetFullPath(Path.Combine(outputRoot, item.PhysicalPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(Path.GetFullPath(outputRoot), StringComparison.Ordinal) || !File.Exists(path)) { Error(Phase8AuthorityReasonCodes.NotCommitted, $"Physical asset is missing for '{key}'."); continue; }
            try
            {
                var info = await Image.IdentifyAsync(path, cancellationToken);
                if (info is null || info.Width != item.Width || info.Height != item.Height) Error(Phase8AuthorityReasonCodes.NotCommitted, $"Dimensions differ for '{key}'.");
                await using var stream = File.OpenRead(path);
                var checksum = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
                if (!checksum.Equals(item.Checksum, StringComparison.OrdinalIgnoreCase)) Error(Phase8AuthorityReasonCodes.ChecksumMismatch, $"Physical checksum differs for '{key}'.");
            }
            catch { Error(Phase8AuthorityReasonCodes.NotCommitted, $"Physical asset cannot be decoded for '{key}'."); }
        }
        return new(errors.Count == 0, codes.ToArray(), errors);
    }
}
