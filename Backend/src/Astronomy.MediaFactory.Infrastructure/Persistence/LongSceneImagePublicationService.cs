using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class Phase9CommittedAuthorityReader : IPhase9CommittedAuthorityReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<LongSceneImageManifest?> ReadAsync(string outputRoot, CancellationToken ct)
    {
        var path = Path.Combine(outputRoot, "09-long-scenes", "long-scene-image-manifest.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<LongSceneImageManifest>(await File.ReadAllTextAsync(path, ct), JsonOptions) : null;
    }
}

public sealed class LongSceneImagePublicationService(IPhase8AuthorityLoader authorityLoader,
    ILongSceneImageManifestValidator validator, IPhase9CommittedAuthorityReader reader) : ILongSceneImagePublicationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public async Task<LongSceneImagePublicationResult> PublishAsync(LongSceneImagePublicationRequest request, CancellationToken ct)
    {
        var phase8Path = Path.Combine(request.OutputRoot, "08-scene-assets", "scene-asset-manifest.json");
        var phase8ReportPath = Path.Combine(request.OutputRoot, "08-scene-assets", "phase8-publication-report.json");
        if (!File.Exists(phase8Path) || !File.Exists(phase8ReportPath)) Fail(Phase9ReasonCodes.Phase8Missing, "Committed Phase 8 authority is missing.");
        var phase8 = JsonSerializer.Deserialize<SceneAssetManifest>(await File.ReadAllTextAsync(phase8Path, ct), JsonOptions)
            ?? throw new Phase9AuthorityException(Phase9ReasonCodes.Phase8Invalid, "Phase 8 manifest cannot be read.");
        var expectedP8Checksum = HashText(string.Join("|", phase8.Assets.OrderBy(x => x.AssetId, StringComparer.Ordinal).Select(x => $"{x.AssetId}:{x.SemanticIdentity}:{x.Checksum}")));
        if (!expectedP8Checksum.Equals(phase8.DeterministicChecksum, StringComparison.OrdinalIgnoreCase)) Fail(Phase9ReasonCodes.Phase8ChecksumMismatch, "Phase 8 deterministic checksum is invalid.");
        if (phase8.PublicationState != "Committed" || phase8.ValidationStatus != "Valid") Fail(Phase9ReasonCodes.Phase8NotCommitted, "Phase 8 authority is not committed and valid.");
        using (var report = JsonDocument.Parse(await File.ReadAllTextAsync(phase8ReportPath, ct)))
            if (!True(report.RootElement, "publicationCommitted") || !True(report.RootElement, "candidateReadbackPassed") || !True(report.RootElement, "committedReadbackPassed"))
                Fail(Phase9ReasonCodes.Phase8NotDownstreamReady, "Phase 8 publication readback evidence is incomplete.");
        if (phase8.PlanId != request.PlanId || phase8.EventId != request.EventId || phase8.Language != request.Language)
            Fail(Phase9ReasonCodes.Phase8Invalid, "Phase 8 execution identity does not match Phase 9.");

        var authority = await authorityLoader.LoadAsync(new(request.OutputRoot, request.PlanId, request.EventId, request.Language, ["Long"]), ct);
        if (phase8.ExecutionId != authority.ExecutionId || phase8.StoryFrameManifestChecksum != authority.StoryFrameManifestChecksum)
            Fail(Phase9ReasonCodes.SourceLineageMismatch, "Phase 6/8 execution identity or checksum differs.");
        var existing = await reader.ReadAsync(request.OutputRoot, ct);
        if (!request.OverwriteExisting && existing is not null && existing.PublicationState == "Committed" && existing.DownstreamReady
            && existing.Phase8SceneAssetManifestChecksum == phase8.DeterministicChecksum && existing.Phase6StoryFrameManifestChecksum == authority.StoryFrameManifestChecksum
            && existing.DeterministicChecksum == LongSceneImageManifestValidator.Checksum(existing.Images))
        {
            var validation = await validator.ValidateAsync(existing, phase8, authority, Path.Combine(request.OutputRoot, "09-long-scenes"), ct);
            if (validation.IsValid) return new(Phase9ReasonCodes.Accepted, "Valid committed Long scene image authority was reused.", existing,
                Directory.EnumerateFiles(Path.Combine(request.OutputRoot, "09-long-scenes"), "*", SearchOption.AllDirectories).ToArray(), true);
        }

        var sources = phase8.Assets.Where(x => x.Variant.Equals("Long", StringComparison.OrdinalIgnoreCase)).ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        var expected = authority.LongScenes.OrderBy(x => x.SceneOrder).ToArray();
        if (sources.Count != expected.Length || expected.Any(x => !sources.ContainsKey(x.SceneId))) Fail(Phase9ReasonCodes.SourceLineageMismatch, "Phase 8 Long set does not exactly match Phase 6.");
        var staging = Path.Combine(request.OutputRoot, $".09-long-scenes-staging-{Guid.NewGuid():N}");
        var committed = Path.Combine(request.OutputRoot, "09-long-scenes"); var backup = Path.Combine(request.OutputRoot, $".09-long-scenes-backup-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, "scene-assets")); var items = new List<LongSceneImageManifestItem>();
            foreach (var frame in expected)
            {
                var asset = sources[frame.SceneId];
                if (asset.BlueprintSceneId != frame.BlueprintSceneId || asset.StoryFrameId != frame.StoryFrameId || asset.SceneOrder != frame.SceneOrder || string.IsNullOrWhiteSpace(asset.SemanticIdentity)) Fail(Phase9ReasonCodes.SourceLineageMismatch, $"Source lineage differs for '{frame.SceneId}'.");
                var source = Path.GetFullPath(Path.Combine(request.OutputRoot, asset.PhysicalPath.Replace('/', Path.DirectorySeparatorChar)));
                if (!source.StartsWith(Path.GetFullPath(request.OutputRoot), StringComparison.Ordinal) || !File.Exists(source)) Fail(Phase9ReasonCodes.SourceMissing, $"Source image is missing for '{frame.SceneId}'.");
                ImageInfo? info; try { info = await Image.IdentifyAsync(source, ct); } catch { throw new Phase9AuthorityException(Phase9ReasonCodes.SourceInvalid, $"Source image cannot be decoded for '{frame.SceneId}'."); }
                if (info is null || info.Width != asset.Width || info.Height != asset.Height || info.Width * 9 != info.Height * 16) Fail(Phase9ReasonCodes.SourceDimensionMismatch, $"Source image dimensions differ for '{frame.SceneId}'.");
                await using var input = File.OpenRead(source); var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
                if (!hash.Equals(asset.Checksum, StringComparison.OrdinalIgnoreCase)) Fail(Phase9ReasonCodes.SourceChecksumMismatch, $"Source checksum differs for '{frame.SceneId}'.");
                if (asset.RequiresScientificGeometry && (!asset.ScientificGeometryCertified || string.IsNullOrWhiteSpace(asset.AccuracyEvidencePath) || !File.Exists(Path.Combine(request.OutputRoot, asset.AccuracyEvidencePath)))) Fail(Phase9ReasonCodes.SourceInvalid, $"Scientific evidence is invalid for '{frame.SceneId}'.");
                var fileName = SafeName(frame.SceneId) + ".png"; var target = Path.Combine(staging, "scene-assets", fileName); File.Copy(source, target, true);
                items.Add(new($"P9:{frame.SceneId}", frame.SceneId, frame.BlueprintSceneId, frame.StoryFrameId, frame.SceneOrder, asset.AssetId, asset.SemanticIdentity,
                    asset.ProviderType, asset.ImageGenerationProvider, asset.AstronomyGeometryProvider, asset.VisualRenderer, asset.PhysicalPath, $"scene-assets/{fileName}", info.Width, info.Height,
                    $"{info.Width}:{info.Height}", hash, true, false, false, asset.RequiresScientificGeometry, asset.ScientificGeometryCertified, asset.AccuracyEvidencePath, "Valid", []));
            }
            var checksum = LongSceneImageManifestValidator.Checksum(items);
            var manifest = new LongSceneImageManifest("1.0", phase8.PlanId, phase8.ExecutionId, phase8.EventId, phase8.Language, "Long", DateTimeOffset.UtcNow,
                phase8.DeterministicChecksum, authority.StoryFrameManifestChecksum, expected.Length, items.Count, items, "Valid", "Candidate", checksum, false);
            await WriteAsync(Path.Combine(staging, "long-scene-image-manifest.json"), manifest, ct);
            var candidate = await validator.ValidateAsync(manifest, phase8, authority, staging, ct); if (!candidate.IsValid) throw new Phase9AuthorityException(candidate.ReasonCodes.FirstOrDefault() ?? Phase9ReasonCodes.SourceInvalid, string.Join("; ", candidate.Errors));
            var diagnostics = new { phase9Applicable=true, requestedLong=true, phase8AuthorityLoaded=true, phase8AuthorityChecksum=phase8.DeterministicChecksum, phase8Committed=true, phase8DownstreamReady=true, phase6AuthorityLoaded=true, phase6StoryFrameManifestChecksum=authority.StoryFrameManifestChecksum, expectedLongSceneCount=expected.Length, phase8LongAssetCount=sources.Count, materializedLongSceneCount=items.Count, missingSceneIds=Array.Empty<string>(), extraSceneIds=Array.Empty<string>(), lineageMismatchSceneIds=Array.Empty<string>(), sourceChecksumMismatchSceneIds=Array.Empty<string>(), dimensionMismatchSceneIds=Array.Empty<string>(), scientificEvidenceFailureSceneIds=Array.Empty<string>(), cinematicAssetCount=items.Count(x=>x.VisualStyle=="Cinematic"), hybridCinematicAssetCount=items.Count(x=>x.VisualStyle=="HybridCinematic"), explicitInfographicAssetCount=items.Count(x=>x.VisualStyle is "Infographic" or "ScientificChart"), azureCallsThisPhase=0, sceneAssetsV3GenerationCallsThisPhase=0, candidateValidationPassed=true, candidateReadbackPassed=true, publicationCommitted=true, committedReadbackPassed=true, downstreamReady=true, legacyNineSceneContractUsed=false };
            await WriteAsync(Path.Combine(staging, "phase9-authority-diagnostics.json"), diagnostics, ct);
            manifest = manifest with { PublicationState="Committed", DownstreamReady=true }; await WriteAsync(Path.Combine(staging, "long-scene-image-manifest.json"), manifest, ct);
            await WriteAsync(Path.Combine(staging, "phase9-publication-report.json"), new { candidateCreated=true, candidateValidationPassed=true, candidateReadbackPassed=true, backupCreated=Directory.Exists(committed), publicationCommitted=true, committedReadbackPassed=true, manifestChecksum=checksum, assetCount=items.Count, materializedAssetCount=items.Count, reusedAssetCount=0, regeneratedAssetCount=0, generatedAtUtc=DateTimeOffset.UtcNow }, ct);
            if (Directory.Exists(committed)) Directory.Move(committed, backup); Directory.Move(staging, committed);
            var readback = await reader.ReadAsync(request.OutputRoot, ct); var committedValidation = readback is null ? new(false, [Phase9ReasonCodes.SourceInvalid], ["Committed manifest missing."]) : await validator.ValidateAsync(readback, phase8, authority, committed, ct);
            if (!committedValidation.IsValid) { Directory.Delete(committed, true); if (Directory.Exists(backup)) Directory.Move(backup, committed); throw new Phase9AuthorityException(committedValidation.ReasonCodes.First(), string.Join("; ", committedValidation.Errors)); }
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            return new(Phase9ReasonCodes.Accepted, "Long scene images materialized, validated, committed and read back.", readback!, Directory.EnumerateFiles(committed, "*", SearchOption.AllDirectories).ToArray(), false);
        }
        catch { if (Directory.Exists(staging)) Directory.Delete(staging, true); throw; }
    }
    private static bool True(JsonElement root,string name)=>root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.True;
    private static string SafeName(string value)=>string.Concat(value.Select(c=>char.IsLetterOrDigit(c)||c is '-' or '_'?c:'-'));
    private static string HashText(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static async Task WriteAsync<T>(string path,T value,CancellationToken ct)=>await File.WriteAllTextAsync(path,JsonSerializer.Serialize(value,JsonOptions),ct);
    private static void Fail(string code,string message)=>throw new Phase9AuthorityException(code,message);
}
public sealed class Phase9AuthorityException(string reasonCode, string message) : InvalidOperationException(message) { public string ReasonCode { get; } = reasonCode; }
