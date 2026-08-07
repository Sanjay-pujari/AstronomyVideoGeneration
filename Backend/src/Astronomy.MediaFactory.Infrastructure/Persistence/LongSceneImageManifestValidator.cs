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
        if (manifest.SchemaVersion != "1.0" || manifest.ValidationStatus != "Valid"
            || manifest.DeterministicChecksum != Checksum(manifest.Images))
            Error(Phase9ReasonCodes.SourceInvalid, "Phase 9 manifest schema, validation state, or checksum is invalid.");
        if (manifest.Phase8SceneAssetManifestChecksum != phase8.DeterministicChecksum
            || manifest.Phase6StoryFrameManifestChecksum != authority.StoryFrameManifestChecksum)
            Error(Phase9ReasonCodes.SourceLineageMismatch, "Phase 9 upstream authority checksums differ.");
        var expected = authority.LongScenes.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        var longSources = phase8.Assets.Where(x => x.Variant.Equals("Long", StringComparison.OrdinalIgnoreCase)).ToArray();
        var source = longSources.GroupBy(x => x.SceneId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        if (longSources.Length != source.Count)
            Error(Phase9ReasonCodes.SceneSetMismatch, "Phase 8 contains duplicate Long scene IDs.");
        if (manifest.ExpectedSceneCount != expected.Count || manifest.ActualSceneCount != manifest.Images.Count || manifest.Images.Count != expected.Count)
            Error(Phase9ReasonCodes.SceneSetMismatch, "Long scene count differs from Phase 6.");
        if (manifest.Images.Select(x => x.SceneId).ToHashSet(StringComparer.Ordinal).SetEquals(expected.Keys) is false)
            Error(Phase9ReasonCodes.SceneSetMismatch, "Long scene IDs do not exactly match Phase 6.");
        if (manifest.Images.GroupBy(x => x.SceneId, StringComparer.Ordinal).Any(x => x.Count() > 1)
            || manifest.Images.GroupBy(x => x.SceneOrder).Any(x => x.Count() > 1))
            Error(Phase9ReasonCodes.SceneSetMismatch, "Duplicate scene IDs or orders exist.");
        foreach (var item in manifest.Images)
        {
            if (!expected.TryGetValue(item.SceneId, out var frame) || !source.TryGetValue(item.SceneId, out var asset)
                || item.BlueprintSceneId != frame.BlueprintSceneId || item.StoryFrameId != frame.StoryFrameId || item.SceneOrder != frame.SceneOrder
                || item.SourcePhase8AssetId != asset.AssetId || item.SourcePhase8SemanticIdentity != asset.SemanticIdentity
                || item.SourcePhase8PhysicalPath != asset.PhysicalPath || item.VisualStyle != asset.VisualStyle
                || item.BaseImageProvider != asset.BaseImageProvider || item.AstronomyGeometryProvider != asset.AstronomyGeometryProvider
                || item.FinalRenderer != asset.FinalRenderer || item.RequiresScientificGeometry != asset.RequiresScientificGeometry
                || item.ScientificGeometryCertified != asset.ScientificGeometryCertified || !item.Materialized || item.Regenerated)
            { Error(Phase9ReasonCodes.SourceLineageMismatch, $"Lineage differs for '{item.SceneId}'."); continue; }
            var outputRoot = Directory.GetParent(Path.GetFullPath(packageRoot))?.FullName ?? Path.GetFullPath(packageRoot);
            var evidencePath = string.IsNullOrWhiteSpace(item.ScientificEvidencePath) ? null : Path.GetFullPath(Path.Combine(outputRoot, item.ScientificEvidencePath));
            if (item.RequiresScientificGeometry && (!item.ScientificGeometryCertified || evidencePath is null
                || !evidencePath.StartsWith(Path.GetFullPath(Path.Combine(outputRoot, "08-scene-assets")) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || !File.Exists(evidencePath)))
                Error(Phase9ReasonCodes.ScientificEvidenceInvalid, $"Scientific evidence is absent for '{item.SceneId}'.");
            var path = Path.GetFullPath(Path.Combine(packageRoot, item.PhysicalPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(Path.GetFullPath(packageRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path)) { Error(Phase9ReasonCodes.SourceMissing, $"Image is missing for '{item.SceneId}'."); continue; }
            try { var info = await Image.IdentifyAsync(path, ct); if (info is null || info.Width != item.Width || info.Height != item.Height || item.AspectRatio != $"{item.Width}:{item.Height}" || item.Width != asset.Width || item.Height != asset.Height || item.AspectRatio != asset.AspectRatio) Error(Phase9ReasonCodes.SourceDimensionMismatch, $"Image dimensions are invalid for '{item.SceneId}'.");
                await using var stream = File.OpenRead(path); var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(); if (!hash.Equals(item.PhysicalSha256, StringComparison.OrdinalIgnoreCase)) Error(Phase9ReasonCodes.SourceChecksumMismatch, $"Image checksum differs for '{item.SceneId}'."); }
            catch { Error(Phase9ReasonCodes.SourceInvalid, $"Image cannot be decoded for '{item.SceneId}'."); }
        }
        var duplicateHashes = manifest.Images.GroupBy(x => x.PhysicalSha256, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1);
        foreach (var duplicate in duplicateHashes) Error(Phase9ReasonCodes.SourceInvalid, $"Unexpected duplicate physical image '{duplicate.Key}'.");
        return new(errors.Count == 0, codes.ToArray(), errors);
    }

    internal static string Checksum(IEnumerable<LongSceneImageManifestItem> images) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("|", images.OrderBy(x => x.SceneOrder).Select(x => string.Join(":", x.AssetId, x.SceneId,
            x.BlueprintSceneId, x.StoryFrameId, x.SceneOrder, x.SourcePhase8AssetId, x.SourcePhase8SemanticIdentity,
            x.SourcePhase8PhysicalPath, x.VisualStyle, x.BaseImageProvider, x.AstronomyGeometryProvider, x.FinalRenderer,
            x.PhysicalPath, x.Width, x.Height, x.AspectRatio, x.PhysicalSha256, x.Materialized, x.Reused,
            x.Regenerated, x.RequiresScientificGeometry, x.ScientificGeometryCertified, x.ScientificEvidencePath,
            x.ValidationStatus, string.Join(',', x.Warnings))))))).ToLowerInvariant();
}
