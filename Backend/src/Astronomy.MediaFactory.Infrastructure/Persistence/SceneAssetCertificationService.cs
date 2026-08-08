using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Phase 10 is a read-only consumer of the committed Phase 6, 8 and 9 authorities.</summary>
public sealed class SceneAssetCertificationService(IPhase8AuthorityLoader phase6Loader,
    IPhase8SceneAssetManifestValidator phase8Validator, IPhase9CommittedAuthorityReader phase9Reader,
    ILongSceneImageManifestValidator phase9Validator) : ISceneAssetCertificationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<Phase10CertificationResult> CertifyAsync(Phase10CertificationRequest request, CancellationToken ct)
    {
        var requested = new[] { request.RequestedLong ? "Long" : null, request.RequestedShort ? "Short" : null }
            .Where(x => x is not null).Cast<string>().ToArray();
        var authority = await phase6Loader.LoadAsync(new(request.OutputRoot, request.PlanId, request.EventId,
            request.Language, requested), ct);
        var phase8Path = Path.Combine(request.OutputRoot, "08-scene-assets", "scene-asset-manifest.json");
        var phase8Report = Path.Combine(request.OutputRoot, "08-scene-assets", "phase8-publication-report.json");
        var phase8 = await ReadRequiredAsync<SceneAssetManifest>(phase8Path, Phase10ReasonCodes.Phase8Invalid, ct);
        RequirePublication(phase8Report, phase8.DeterministicChecksum, Phase10ReasonCodes.Phase8Invalid);
        if (phase8.PublicationState != "Committed" || phase8.ValidationStatus != "Valid"
            || !phase8.RequestedVariants.ToHashSet(StringComparer.OrdinalIgnoreCase).IsSupersetOf(requested))
            Fail(Phase10ReasonCodes.Phase8Invalid, "Phase 8 is not a valid committed authority for the requested variants.");
        var p8Validation = await phase8Validator.ValidateAsync(phase8, authority, request.OutputRoot, ct);
        if (!p8Validation.IsValid) Fail(Phase10ReasonCodes.Phase8Invalid, string.Join("; ", p8Validation.Errors));
        var phase8Root = Path.GetFullPath(Path.Combine(request.OutputRoot, "08-scene-assets")) + Path.DirectorySeparatorChar;
        foreach (var asset in phase8.Assets.Where(x => requested.Contains(x.Variant, StringComparer.OrdinalIgnoreCase) && x.RequiresScientificGeometry))
        {
            var evidence = string.IsNullOrWhiteSpace(asset.AccuracyEvidencePath) ? string.Empty
                : Path.GetFullPath(Path.Combine(request.OutputRoot, asset.AccuracyEvidencePath));
            if (!asset.ScientificGeometryCertified || !evidence.StartsWith(phase8Root, StringComparison.Ordinal) || !File.Exists(evidence))
                Fail(Phase10ReasonCodes.Phase8Invalid, $"Scientific evidence linkage is invalid for '{asset.SceneId}'.");
        }

        var shortAssets = phase8.Assets.Where(x => x.Variant.Equals("Short", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.SceneOrder).ToArray();
        var longAssets = phase8.Assets.Where(x => x.Variant.Equals("Long", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.SceneOrder).ToArray();
        var shortSection = CertifyPhase8Variant(request.RequestedShort, authority.ShortScenes, shortAssets, 2160, 3840, Phase10ReasonCodes.ShortSetMismatch);

        LongSceneImageManifest? phase9 = null;
        var phase9Path = Path.Combine(request.OutputRoot, "09-long-scenes", "long-scene-image-manifest.json");
        var phase9Report = Path.Combine(request.OutputRoot, "09-long-scenes", "phase9-publication-report.json");
        SceneVariantCertification longSection;
        if (request.RequestedLong)
        {
            phase9 = await phase9Reader.ReadAsync(request.OutputRoot, ct);
            if (phase9 is null) Fail(Phase10ReasonCodes.Phase9Invalid, "Committed Phase 9 authority is missing.");
            RequirePublication(phase9Report, phase9!.DeterministicChecksum, Phase10ReasonCodes.Phase9Invalid);
            if (phase9.PublicationState != "Committed" || phase9.ValidationStatus != "Valid" || !phase9.DownstreamReady)
                Fail(Phase10ReasonCodes.Phase9Invalid, "Phase 9 is not committed and downstream ready.");
            var validation = await phase9Validator.ValidateAsync(phase9, phase8, authority,
                Path.Combine(request.OutputRoot, "09-long-scenes"), ct);
            if (!validation.IsValid) Fail(Phase10ReasonCodes.LongEquivalenceMismatch, string.Join("; ", validation.Errors));
            var phase8ByScene = longAssets.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
            if (phase9.Images.Any(x => !phase8ByScene.TryGetValue(x.SceneId, out var source)
                || !x.PhysicalSha256.Equals(source.PhysicalSha256, StringComparison.OrdinalIgnoreCase)
                || x.ScientificEvidencePath != source.AccuracyEvidencePath))
                Fail(Phase10ReasonCodes.LongEquivalenceMismatch, "Phase 9 physical checksum or scientific evidence lineage differs from Phase 8.");
            if (phase9.Images.Any(x => x.Width != 1920 || x.Height != 1080))
                Fail(Phase10ReasonCodes.LongSetMismatch, "Long assets do not match the configured 1920x1080 profile.");
            longSection = BuildSection(true, authority.LongScenes.Select(x => x.SceneId), phase9.Images.Select(x => x.SceneId),
                phase9.Images.Count, longAssets.Length, phase9.Images.Count, true);
        }
        else longSection = BuildSection(false, [], [], 0, longAssets.Length, 0, null);

        var shortIds = shortSection.SceneIds.ToHashSet(StringComparer.Ordinal);
        var crossVariant = !shortIds.Overlaps(longSection.SceneIds)
            && NoUnexpectedDuplicateAssignments(shortAssets.Concat(longAssets).Where(x => requested.Contains(x.Variant, StringComparer.OrdinalIgnoreCase)));
        if (!crossVariant) Fail(Phase10ReasonCodes.LongEquivalenceMismatch, "Cross-variant identity or physical assignment validation failed.");
        var totalExpected = shortSection.ExpectedSceneCount + longSection.ExpectedSceneCount;
        var totalCertified = shortSection.CertifiedSceneCount + longSection.CertifiedSceneCount;
        var checksum = Checksum(string.Join('|', request.PlanId, authority.ExecutionId, request.EventId, request.Language,
            authority.StoryFrameManifestChecksum, phase8.DeterministicChecksum, phase9?.DeterministicChecksum,
            string.Join(',', requested), string.Join(',', shortSection.SceneIds), string.Join(',', longSection.SceneIds)));
        var certification = new SceneAssetCertification("1.0", request.PlanId, authority.ExecutionId, request.EventId,
            request.Language, DateTimeOffset.UtcNow, requested, authority.StoryFrameManifestChecksum,
            phase8.DeterministicChecksum, phase9?.DeterministicChecksum, shortSection, longSection, totalExpected,
            totalCertified, true, "Valid", "Committed", checksum, true);

        var root = Path.Combine(request.OutputRoot, "10-scene-validation");
        var staging = root + $".candidate-{Guid.NewGuid():N}";
        Directory.CreateDirectory(staging);
        var candidate = Path.Combine(staging, "scene-asset-certification.json");
        await File.WriteAllTextAsync(candidate, JsonSerializer.Serialize(certification, Json), ct);
        var readback = await ReadRequiredAsync<SceneAssetCertification>(candidate, "P10_CANDIDATE_READBACK_FAILED", ct);
        if (readback.DeterministicChecksum != checksum || !readback.DownstreamReady) Fail("P10_CANDIDATE_READBACK_FAILED", "Candidate certification readback failed.");
        var diagnosticsPath = Path.Combine(staging, "phase10-authority-diagnostics.json");
        var reportPath = Path.Combine(staging, "phase10-publication-report.json");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new { phase10Applicable = true, requestedShort = request.RequestedShort, requestedLong = request.RequestedLong, phase6AuthorityLoaded = true, phase8AuthorityLoaded = true, phase9AuthorityLoaded = !request.RequestedLong || phase9 is not null, expectedShortSceneCount = shortSection.ExpectedSceneCount, actualShortSceneCount = shortSection.ActualSceneCount, certifiedShortSceneCount = shortSection.CertifiedSceneCount, expectedLongSceneCount = longSection.ExpectedSceneCount, phase8LongSceneCount = longAssets.Length, phase9LongSceneCount = phase9?.Images.Count ?? 0, certifiedLongSceneCount = longSection.CertifiedSceneCount, totalExpectedSceneCount = totalExpected, totalCertifiedSceneCount = totalCertified, missingShortSceneIds = shortSection.MissingSceneIds, extraShortSceneIds = shortSection.ExtraSceneIds, missingLongSceneIds = longSection.MissingSceneIds, extraLongSceneIds = longSection.ExtraSceneIds, phase8Phase9LongEquivalencePassed = longSection.Phase8Phase9EquivalencePassed, dimensionValidationPassed = true, physicalChecksumValidationPassed = true, lineageValidationPassed = true, scientificEvidenceValidationPassed = true, crossVariantValidationPassed = true, cinematicAssetCount = phase8.Assets.Count(x => x.VisualStyle == "Cinematic"), hybridCinematicAssetCount = phase8.Assets.Count(x => x.VisualStyle == "HybridCinematic"), explicitInfographicAssetCount = phase8.Assets.Count(x => x.VisualStyle is "Infographic" or "ScientificChart"), azureCallsThisPhase = 0, sceneAssetsV3GenerationCallsThisPhase = 0, imageMaterializationCallsThisPhase = 0, candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, downstreamReady = true, legacyFiveSceneContractUsed = false, legacyNineSceneContractUsed = false, upstreamArtifactsModified = false, compatibilityValidation = false, isAuthoritative = true }, Json), ct);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { candidateCreated = true, candidateValidationPassed = true, candidateReadbackPassed = true, publicationCommitted = true, committedReadbackPassed = true, certificationChecksum = checksum, shortCertifiedCount = shortSection.CertifiedSceneCount, longCertifiedCount = longSection.CertifiedSceneCount, totalCertifiedCount = totalCertified, upstreamArtifactsModified = false }, Json), ct);
        var backup = root + $".backup-{Guid.NewGuid():N}";
        if (Directory.Exists(root)) Directory.Move(root, backup);
        try { Directory.Move(staging, root); }
        catch { if (Directory.Exists(backup)) Directory.Move(backup, root); throw; }
        var committed = await ReadRequiredAsync<SceneAssetCertification>(Path.Combine(root, "scene-asset-certification.json"), "P10_COMMITTED_READBACK_FAILED", ct);
        if (committed.DeterministicChecksum != checksum)
        {
            Directory.Delete(root, true); if (Directory.Exists(backup)) Directory.Move(backup, root);
            Fail("P10_COMMITTED_READBACK_FAILED", "Committed certification readback failed.");
        }
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        var inputs = new List<string> { phase8Path, phase8Report };
        if (request.RequestedLong) { inputs.Add(phase9Path); inputs.Add(phase9Report); }
        return new(Phase10ReasonCodes.Accepted, "Requested scene assets validated, certified, committed and read back.", committed,
            inputs, Directory.EnumerateFiles(root).ToArray());
    }

    private static SceneVariantCertification CertifyPhase8Variant(bool requested, IReadOnlyList<Phase8SceneRequirement> expected,
        IReadOnlyList<SceneAssetManifestItem> assets, int width, int height, string code)
    {
        if (!requested) return BuildSection(false, [], [], 0, assets.Count, null, null);
        if (assets.Any(x => x.Width != width || x.Height != height)) Fail(code, $"Assets do not match the {width}x{height} profile.");
        return BuildSection(true, expected.Select(x => x.SceneId), assets.Select(x => x.SceneId), assets.Count, assets.Count, null, null);
    }

    private static SceneVariantCertification BuildSection(bool requested, IEnumerable<string> expectedIds, IEnumerable<string> actualIds,
        int actualCount, int? phase8Count, int? phase9Count, bool? equivalence)
    {
        var expected = expectedIds.ToArray(); var actual = actualIds.ToArray();
        var missing = expected.Except(actual, StringComparer.Ordinal).ToArray(); var extra = actual.Except(expected, StringComparer.Ordinal).ToArray();
        var valid = !requested || (missing.Length == 0 && extra.Length == 0 && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length);
        if (!valid) Fail(requested && phase9Count is null ? Phase10ReasonCodes.ShortSetMismatch : Phase10ReasonCodes.LongSetMismatch, "Requested scene set does not exactly match Phase 6.");
        return new(requested, requested ? expected.Length : 0, requested ? actualCount : 0, requested ? actualCount : 0,
            requested ? actual : [], missing, extra, valid, valid, valid, valid, valid ? "Valid" : "Invalid", phase8Count, phase9Count, equivalence);
    }

    private static bool NoUnexpectedDuplicateAssignments(IEnumerable<SceneAssetManifestItem> assets) =>
        assets.GroupBy(x => x.PhysicalPath, StringComparer.OrdinalIgnoreCase).All(g => g.Count() == 1 || g.All(x => x.SharedAsset));
    private static async Task<T> ReadRequiredAsync<T>(string path, string code, CancellationToken ct) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json) ?? throw new InvalidOperationException($"{code}: Authority cannot be parsed.") : throw new InvalidOperationException($"{code}: Required authority is missing: {path}");
    private static void RequirePublication(string path, string checksum, string code)
    {
        if (!File.Exists(path)) Fail(code, "Publication report is missing.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path)); var root = doc.RootElement;
        bool Flag(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        var reported = root.TryGetProperty("authorityChecksum", out var a) ? a.GetString() : root.TryGetProperty("manifestChecksum", out var m) ? m.GetString() : null;
        var validationPassed = Flag("manifestValidationPassed") || Flag("candidateValidationPassed");
        var downstreamReady = !root.TryGetProperty("downstreamReady", out _) || Flag("downstreamReady");
        if (!Flag("publicationCommitted") || !validationPassed || !Flag("candidateReadbackPassed") || !Flag("committedReadbackPassed") || !downstreamReady || (reported is not null && !reported.Equals(checksum, StringComparison.OrdinalIgnoreCase))) Fail(code, "Publication evidence is invalid.");
    }
    private static string Checksum(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Fail(string code, string message) => throw new InvalidOperationException($"{code}: {message}");
}
