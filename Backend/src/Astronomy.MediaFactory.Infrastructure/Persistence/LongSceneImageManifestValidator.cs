using System.Security.Cryptography;
using System.Text;
using Astronomy.MediaFactory.Core;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class LongSceneImageManifestValidator : ILongSceneImageManifestValidator
{
    public async Task<LongSceneImageValidationResult> ValidateAsync(LongSceneImageManifest manifest,
        SceneAssetManifest phase8, Phase8AuthorityInput authority, string packageRoot, CancellationToken ct)
    {
        var errors = new List<string>(); var codes = new HashSet<string>();
        void Error(string code, string error) { codes.Add(code); errors.Add(error); }
        if (manifest.Variant != "Long" || manifest.PlanId != authority.PlanId || manifest.ExecutionId != authority.ExecutionId
            || manifest.EventId != authority.EventId || manifest.Language != authority.Language)
            Error(Phase9ReasonCodes.SourceLineageMismatch, "Phase 9 identity does not match committed upstream authority.");
        var expected = authority.LongScenes.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        var source = phase8.Assets.Where(x => x.Variant.Equals("Long", StringComparison.OrdinalIgnoreCase)).ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        if (manifest.ExpectedSceneCount != expected.Count || manifest.ActualSceneCount != manifest.Images.Count || manifest.Images.Count != expected.Count)
            Error(Phase9ReasonCodes.SourceLineageMismatch, "Long scene count differs from Phase 6.");
        if (manifest.Images.GroupBy(x => x.SceneId, StringComparer.Ordinal).Any(x => x.Count() > 1)) Error(Phase9ReasonCodes.SourceLineageMismatch, "Duplicate scene IDs exist.");
        foreach (var item in manifest.Images)
        {
            if (!expected.TryGetValue(item.SceneId, out var frame) || !source.TryGetValue(item.SceneId, out var asset)
                || item.BlueprintSceneId != frame.BlueprintSceneId || item.StoryFrameId != frame.StoryFrameId || item.SceneOrder != frame.SceneOrder
                || item.SourcePhase8AssetId != asset.AssetId || item.SourcePhase8SemanticIdentity != asset.SemanticIdentity
                || item.VisualStyle != asset.ProviderType)
            { Error(Phase9ReasonCodes.SourceLineageMismatch, $"Lineage differs for '{item.SceneId}'."); continue; }
            if (item.RequiresScientificGeometry && (!item.ScientificGeometryCertified || string.IsNullOrWhiteSpace(item.ScientificEvidencePath)))
                Error(Phase9ReasonCodes.SourceInvalid, $"Scientific evidence is absent for '{item.SceneId}'.");
            var path = Path.GetFullPath(Path.Combine(packageRoot, item.PhysicalPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(Path.GetFullPath(packageRoot), StringComparison.Ordinal) || !File.Exists(path)) { Error(Phase9ReasonCodes.SourceMissing, $"Image is missing for '{item.SceneId}'."); continue; }
            try { var info = await Image.IdentifyAsync(path, ct); if (info is null || info.Width != item.Width || info.Height != item.Height || info.Width * 9 != info.Height * 16) Error(Phase9ReasonCodes.SourceDimensionMismatch, $"Image dimensions are invalid for '{item.SceneId}'.");
                await using var stream = File.OpenRead(path); var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(); if (!hash.Equals(item.PhysicalSha256, StringComparison.OrdinalIgnoreCase)) Error(Phase9ReasonCodes.SourceChecksumMismatch, $"Image checksum differs for '{item.SceneId}'."); }
            catch { Error(Phase9ReasonCodes.SourceInvalid, $"Image cannot be decoded for '{item.SceneId}'."); }
        }
        var duplicateHashes = manifest.Images.GroupBy(x => x.PhysicalSha256, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1);
        foreach (var duplicate in duplicateHashes) Error(Phase9ReasonCodes.SourceInvalid, $"Unexpected duplicate physical image '{duplicate.Key}'.");
        return new(errors.Count == 0, codes.ToArray(), errors);
    }

    internal static string Checksum(IEnumerable<LongSceneImageManifestItem> images) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("|", images.OrderBy(x => x.AssetId, StringComparer.Ordinal).Select(x => $"{x.AssetId}:{x.SourcePhase8SemanticIdentity}:{x.PhysicalSha256}"))))).ToLowerInvariant();
}
