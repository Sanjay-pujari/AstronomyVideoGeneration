using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Reads only the committed Phase 6 publication and proves its complete typed boundary.</summary>
public sealed class Phase6CommittedAuthorityEvaluator : IPhase6CommittedAuthorityEvaluator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Artifacts = ["06-story-frames/story-frames.json", "06-story-frames/story-frame-index.json", "06-story-frames/story-frame-diagnostics.json"];

    public async Task<Phase6CommittedAuthorityEvaluation> EvaluateAsync(Phase6CommittedAuthorityRequest request, CancellationToken token = default)
    {
        var inspected = Artifacts.Concat(["validation/phase-06-validation.json", "phase-manifest.json"]).ToArray();
        Phase6CommittedAuthorityEvaluation Bad(string code, IEnumerable<string> errors) => new(false, null, code, errors.ToArray(), inspected);
        token.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(request.ExecutionRoot);
        var paths = inspected.ToDictionary(x => x, x => Path.Combine(root, x.Replace('/', Path.DirectorySeparatorChar)));
        if (paths.Any(x => !File.Exists(x.Value))) return Bad("P6COMMITTED_ARTIFACT_MISSING", ["Required committed evidence is missing: " + string.Join(", ", paths.Where(x => !File.Exists(x.Value)).Select(x => x.Key))]);
        try
        {
            var authority = await Read<StoryFramesAuthority>(paths[Artifacts[0]], token);
            var index = await Read<StoryFrameIndex>(paths[Artifacts[1]], token);
            var diagnostics = await Read<StoryFrameDiagnostics>(paths[Artifacts[2]], token);
            var validation = await Read<Phase6CommittedValidation>(paths["validation/phase-06-validation.json"], token);
            var errors = Validate(request, authority, index, diagnostics, validation);
            errors.AddRange(await ValidateManifest(paths["phase-manifest.json"], root, validation, token));
            if (errors.Count != 0) return Bad("P6COMMITTED_VALIDATION_INVALID", errors);
            var published = new PublishedStoryFrameAuthority(authority, index, diagnostics, validation.SourcePhase4AggregateId,
                validation.SourcePhase4Checksum, validation.SourceLongChecksum, validation.SourceShortChecksum,
                validation.SourcePhase5PublicationId, Artifacts, ["phase-manifest.json"], ["validation/phase-06-validation.json"],
                authority.AuthorityContractVersion, new Dictionary<string,string>{{"builderType",authority.BuilderType},{"builderVersion",authority.BuilderVersion},{"integrationServiceType",diagnostics.IntegrationServiceType},{"integrationServiceVersion",diagnostics.IntegrationServiceVersion}})
            { ProfileId = validation.Profile, ProfileVersion = validation.ProfileVersion };
            return new(true, published, "P6COMMITTED_VALID", [], inspected);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { return Bad("P6COMMITTED_ARTIFACT_INVALID", [ex.Message]); }
    }

    private static List<string> Validate(Phase6CommittedAuthorityRequest request, StoryFramesAuthority a, StoryFrameIndex i, StoryFrameDiagnostics d, Phase6CommittedValidation v)
    {
        var e = new List<string>();
        void Require(bool condition, string message) { if (!condition) e.Add(message); }
        var committed = v.ReasonCode == "P6AUTH_COMMITTED" && v.Status == "Succeeded" && !v.AlreadyPublished;
        var reused = v.ReasonCode == "P6REUSE_VALID" && v.Status is "Skipped" or "Succeeded" && v.AlreadyPublished;
        Require(v.PhaseNo == 6 && v.PhaseName == "Story Frames Authority", "Validation phase identity is invalid.");
        Require(committed || reused, "Validation outcome fields are incoherent; expected committed or reused canonical evidence.");
        Require(v.ValidationStatus == "Valid" && v.PublicationCommitted && v.CommittedStateValidationPassed, "Committed validation status flags are not valid.");
        Require(v.Errors.Count == 0, "Committed validation contains blocking errors.");
        Require(a.ExecutionId == request.ExecutionId && a.PlanId == request.PlanId && a.EventId == request.EventId && a.Language.Equals(request.Language, StringComparison.OrdinalIgnoreCase), "Committed authority identity does not match request.");
        Require(v.ExecutionId == a.ExecutionId && v.PlanId == a.PlanId && v.EventId == a.EventId && v.Language.Equals(a.Language, StringComparison.OrdinalIgnoreCase), "Validation identity does not match authority.");
        Require(v.Profile == a.Profile && !string.IsNullOrWhiteSpace(v.ProfileVersion), "Validation profile identity does not match authority.");
        Require(StoryFrameContractCompatibility.IsSupported(a.AuthorityContractVersion) && StoryFrameContractCompatibility.IsSupported(i.IndexContractVersion) && StoryFrameContractCompatibility.IsSupported(d.DiagnosticsContractVersion), "Phase 6 runtime contract is unsupported.");
        Require(a.SemanticChecksum == StoryFrameAuthorityChecksum.Authority(a) && v.AuthorityChecksum == a.SemanticChecksum && v.StoryFrameAuthorityChecksum == a.SemanticChecksum && v.AuthorityId == a.AuthorityId && v.StoryFrameAuthorityId == a.AuthorityId, "Authority checksum or identity evidence is invalid.");
        Require(i.Checksum == StoryFrameAuthorityChecksum.Index(i) && v.IndexChecksum == i.Checksum && v.IndexId == i.IndexId && i.SourceStoryFramesAuthorityId == a.AuthorityId && i.SourceStoryFramesChecksum == a.SemanticChecksum, "Index checksum or lineage evidence is invalid.");
        Require(v.SourcePhase4Checksum == a.SourcePhase4Checksum && v.SourceCertificationId == a.SourceCertificationId && v.SourceCertificationChecksum == a.SourceCertificationChecksum && v.SourceEditorialContractId == a.SourceEditorialContractId && v.SourceEditorialContractChecksum == a.SourceEditorialContractChecksum, "Source lineage evidence is invalid.");
        var longs = a.Frames.Count(x => x.Variant.Equals("Long", StringComparison.OrdinalIgnoreCase)); var shorts = a.Frames.Count(x => x.Variant.Equals("Short", StringComparison.OrdinalIgnoreCase));
        Require(a.RequestedVariants.Count == 2 && new[]{"Long","Short"}.All(x => a.RequestedVariants.Contains(x, StringComparer.OrdinalIgnoreCase)) && v.RequestedVariants.Count == 2 && new[]{"Long","Short"}.All(x => v.RequestedVariants.Contains(x, StringComparer.OrdinalIgnoreCase)), "Long and Short variants are required.");
        Require(longs > 0 && shorts > 0 && v.LongStoryFramesRequested && v.ShortStoryFramesRequested && v.LongStoryFramesGenerated && v.ShortStoryFramesGenerated && v.LongStoryFrameCount == longs && v.ShortStoryFrameCount == shorts && v.TotalFrameCount == a.Frames.Count, "Variant counts or coverage evidence is invalid.");
        Require(v.SemanticValidationPassed && v.ChecksumValidationPassed && v.PhysicalChecksumValidationPassed && v.ManifestValidationPassed && v.LineageValidationPassed && v.RelationshipValidationPassed && v.NarrationOwnershipValidationPassed && v.VariantCoverageValidationPassed && v.CanonicalOrderingValidationPassed && v.RuntimeCompatibilityValidationPassed, "One or more committed-state validation gates did not pass.");
        Require(v.ArtifactPaths.SequenceEqual(Artifacts) && v.OutputFiles.SequenceEqual(Artifacts) && v.ArtifactPaths.All(SafePath), "Validation artifact inventory contains missing or unsafe paths.");
        Require(d.ExecutionId == a.ExecutionId && d.SourcePhase4Checksum == a.SourcePhase4Checksum && d.SourceCertificationChecksum == a.SourceCertificationChecksum && d.SourceEditorialContractChecksum == a.SourceEditorialContractChecksum && d.GeneratedFrameCount == a.Frames.Count, "Diagnostics lineage or counts are invalid.");
        return e;
    }

    private static async Task<IEnumerable<string>> ValidateManifest(string manifestPath, string root, Phase6CommittedValidation validation, CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, token)); var errors = new List<string>();
        if (!doc.RootElement.TryGetProperty("phase6Artifacts", out var entries) || entries.ValueKind != JsonValueKind.Array) return ["Manifest phase6Artifacts inventory is missing."];
        var array = entries.EnumerateArray().ToArray();
        foreach (var relative in Artifacts)
        {
            var matches = array.Where(x => x.TryGetProperty("relativePath", out var p) && p.GetString() == relative).ToArray();
            if (matches.Length != 1) { errors.Add($"Manifest entry is missing or duplicated: {relative}."); continue; }
            var entry = matches[0]; var physical = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)); var bytes = await File.ReadAllBytesAsync(physical, token); var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!entry.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.True) errors.Add($"Manifest entry is not required: {relative}.");
            if (!entry.TryGetProperty("physicalSha256", out var h) || !hash.Equals(h.GetString(), StringComparison.OrdinalIgnoreCase)) errors.Add($"Manifest physical hash mismatch: {relative}.");
            if (!entry.TryGetProperty("sizeBytes", out var size) || !size.TryGetInt64(out var expected) || expected != bytes.LongLength) errors.Add($"Manifest physical size mismatch: {relative}.");
            if (!entry.TryGetProperty("contractVersion", out var version) || !StoryFrameContractCompatibility.IsSupported(version.GetString())) errors.Add($"Manifest runtime contract is unsupported: {relative}.");
            foreach (var lineage in new[]{"sourcePhase4Checksum","sourceLongChecksum","sourceShortChecksum","sourceCertificationChecksum","sourceEditorialContractChecksum","sourcePhase5PublicationId"})
                if (!entry.TryGetProperty(lineage, out var value) || string.IsNullOrWhiteSpace(value.GetString())) errors.Add($"Manifest lineage is missing ({lineage}): {relative}.");
        }
        if (array.Length != Artifacts.Length) errors.Add("Manifest Phase 6 artifact inventory is not canonical.");
        return errors;
    }
    private static bool SafePath(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Contains('\\') && !path.Split('/').Any(x => x is "" or "." or "..") && !path.Contains("staging", StringComparison.OrdinalIgnoreCase) && !path.Contains("backup", StringComparison.OrdinalIgnoreCase);
    private static async Task<T> Read<T>(string path, CancellationToken token) { await using var stream = File.OpenRead(path); return (await JsonSerializer.DeserializeAsync<T>(stream, Json, token)) ?? throw new JsonException($"Could not deserialize {Path.GetFileName(path)}."); }
}
