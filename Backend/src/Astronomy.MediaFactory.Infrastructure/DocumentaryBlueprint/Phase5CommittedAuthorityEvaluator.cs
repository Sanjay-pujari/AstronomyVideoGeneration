using System.Security.Cryptography;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public interface IPhase5CommittedAuthorityEvaluator
{
    Task<Phase5CommittedStateEvaluation> EvaluateAsync(string executionRoot, string expectedExecutionId,
        string expectedPlanId, string expectedEventId, string expectedLanguage,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and validates the physically committed Phase 5 authority.  This is the single resume boundary.</summary>
public sealed class Phase5CommittedAuthorityEvaluator : IPhase5CommittedAuthorityEvaluator
{
    private static readonly (string File, string Role)[] Required =
    [
        ("blueprint-validation.json", "SupportingValidation"),
        ("blueprint-certification.json", "CanonicalAuthority"),
        ("editorial-contract.json", "DownstreamContract"),
        ("scene-intents.json", "SupportingProjection"),
        ("coverage-report.json", "SupportingValidation"),
        ("transition-report.json", "SupportingValidation"),
        ("pause-test-report.json", "SupportingValidation")
    ];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Phase5CommittedStateEvaluation> EvaluateAsync(string root, string executionId,
        string planId, string eventId, string language, CancellationToken token = default)
    {
        var inventory = new List<Phase5ArtifactInventoryEntry>();
        Phase5CommittedStateEvaluation Invalid(string code, params string[] errors) => new(false, code, errors, inventory, null);
        try
        {
            var phaseRoot = Path.Combine(root, "05-editorial");
            var authorityPath = Path.Combine(phaseRoot, "blueprint-certification.json");
            if (!File.Exists(authorityPath)) return Invalid("P5REUSE_AUTHORITY_MISSING", "Published Phase 5 authority is missing.");
            foreach (var item in Required)
                if (!File.Exists(Path.Combine(phaseRoot, item.File)))
                    return Invalid("P5REUSE_ARTIFACT_MISSING", $"Required Phase 5 artifact is missing: {item.File}");
            var validationPath = Path.Combine(root, "validation", "phase-05-validation.json");
            if (!File.Exists(validationPath)) return Invalid("P5REUSE_VALIDATION_MISSING", "Phase 5 validation record is missing.");
            var manifestPath = Path.Combine(root, "phase-manifest.json");
            if (!File.Exists(manifestPath)) return Invalid("P5REUSE_MANIFEST_MISSING", "Phase manifest is missing.");

            var certification = await Read<DocumentaryBlueprintCertification>(authorityPath, token);
            var editorial = await Read<DocumentaryBlueprintEditorialContract>(Path.Combine(phaseRoot, "editorial-contract.json"), token);
            var validation = await Read<BlueprintValidationReport>(Path.Combine(phaseRoot, "blueprint-validation.json"), token);
            var intents = await Read<BlueprintSceneIntentProjection>(Path.Combine(phaseRoot, "scene-intents.json"), token);
            var coverage = await Read<BlueprintCoverageReport>(Path.Combine(phaseRoot, "coverage-report.json"), token);
            var transitions = await Read<BlueprintTransitionReport>(Path.Combine(phaseRoot, "transition-report.json"), token);
            var pause = await Read<BlueprintPauseTestReport>(Path.Combine(phaseRoot, "pause-test-report.json"), token);

            if (certification.ExecutionId != executionId || certification.PlanId != planId || certification.EventId != eventId ||
                !string.Equals(certification.Language, language, StringComparison.OrdinalIgnoreCase))
                return Invalid("P5REUSE_IDENTITY_MISMATCH", "Published Phase 5 identity does not match the execution.");
            if (!certification.Passed || certification.CertificationStatus == DocumentaryBlueprintCertificationStatus.Rejected)
                return Invalid("P5REUSE_CERTIFICATION_REJECTED", "Published Phase 5 certification is rejected.");
            if (certification.SemanticChecksum != DocumentaryBlueprintCertificationChecksum.Calculate(certification))
                return Invalid("P5REUSE_CHECKSUM_INVALID", "Certification semantic checksum is invalid.");

            var projections = new (string Aggregate, string Long, string Short, string Actual, string Expected)[]
            {
                (validation.SourceAggregateChecksum, validation.SourceLongChecksum, validation.SourceShortChecksum, validation.SemanticChecksum, Phase5SemanticChecksum.Calculate(validation with { SemanticChecksum = string.Empty })),
                (intents.SourceAggregateChecksum, intents.SourceLongChecksum, intents.SourceShortChecksum, intents.SemanticChecksum, Phase5SemanticChecksum.Calculate(intents with { SemanticChecksum = string.Empty })),
                (coverage.SourceAggregateChecksum, coverage.SourceLongChecksum, coverage.SourceShortChecksum, coverage.SemanticChecksum, Phase5SemanticChecksum.Calculate(coverage with { SemanticChecksum = string.Empty })),
                (transitions.SourceAggregateChecksum, transitions.SourceLongChecksum, transitions.SourceShortChecksum, transitions.SemanticChecksum, Phase5SemanticChecksum.Calculate(transitions with { SemanticChecksum = string.Empty })),
                (pause.SourceAggregateChecksum, pause.SourceLongChecksum, pause.SourceShortChecksum, pause.SemanticChecksum, Phase5SemanticChecksum.Calculate(pause with { SemanticChecksum = string.Empty }))
            };
            if (projections.Any(x => x.Aggregate != certification.SourcePhase4Checksum || x.Long != certification.SourceLongBlueprintChecksum || x.Short != certification.SourceShortBlueprintChecksum))
                return Invalid("P5REUSE_SOURCE_PHASE4_MISMATCH", "Phase 5 projection lineage does not match its certification.");
            if (projections.Any(x => x.Actual != x.Expected) || editorial.Checksum != DocumentaryBlueprintCertificationChecksum.Calculate(editorial))
                return Invalid("P5REUSE_CHECKSUM_INVALID", "A Phase 5 semantic checksum is invalid.");
            if (!validation.OverallValid || !coverage.IsValid || !transitions.IsValid || !pause.IsValid || pause.FailedSceneCount != 0 || pause.PassedSceneCount + pause.FailedSceneCount != pause.Scenes.Count)
                return Invalid("P5REUSE_COMMITTED_STATE_INVALID", "A committed Phase 5 validation report is invalid.");

            using var validationDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(validationPath, token));
            var vr = validationDocument.RootElement;
            if (!IsSuccessfulValidation(vr)) return Invalid("P5REUSE_COMMITTED_STATE_INVALID", "Phase 5 validation record did not pass.");

            using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath, token));
            if (!manifest.RootElement.TryGetProperty("phase5Artifacts", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return Invalid("P5REUSE_MANIFEST_INVALID", "Phase 5 manifest inventory is missing.");
            var manifestEntries = entries.EnumerateArray().ToArray();
            if (manifestEntries.Length != Required.Length) return Invalid("P5REUSE_MANIFEST_INVALID", "Phase 5 manifest inventory must contain seven artifacts.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Required)
            {
                var relative = $"05-editorial/{item.File}";
                var matches = manifestEntries.Where(x => RelativePath(x) == relative).ToArray();
                if (matches.Length != 1 || !seen.Add(relative)) return Invalid("P5REUSE_MANIFEST_INVALID", $"Invalid or duplicate manifest entry: {relative}");
                var entry = matches[0];
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                var physical = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, token))).ToLowerInvariant();
                if (!TryString(entry, "physicalSha256", out var expectedPhysical) || !physical.Equals(expectedPhysical, StringComparison.OrdinalIgnoreCase))
                    return Invalid("P5REUSE_CHECKSUM_INVALID", $"Physical SHA-256 mismatch: {relative}");
                inventory.Add(new(relative, item.Role, TryString(entry, "semanticChecksum", out var semantic) ? semantic : null,
                    physical, new FileInfo(path).Length, certification.SourcePhase4Checksum));
            }
            var authority = new PublishedBlueprintCertification(certification, editorial, validation, intents, coverage,
                transitions, pause, validation.SourcePhase4AggregateId, certification.SourcePhase4Checksum,
                validation.ContractVersion, certification.SemanticChecksum);
            return new(true, "P5REUSE_VALID", [], inventory, authority);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or NotSupportedException)
        { return Invalid("P5REUSE_COMMITTED_STATE_INVALID", ex.Message); }
    }

    private static async Task<T> Read<T>(string path, CancellationToken token) =>
        JsonSerializer.Deserialize<T>(await File.ReadAllBytesAsync(path, token), JsonOptions)
        ?? throw new InvalidDataException($"Artifact is empty: {path}");
    private static bool IsSuccessfulValidation(JsonElement value) =>
        (value.TryGetProperty("validationStatus", out var status) && string.Equals(status.GetString(), "Valid", StringComparison.OrdinalIgnoreCase)
         || value.TryGetProperty("status", out status) && string.Equals(status.GetString(), "Succeeded", StringComparison.OrdinalIgnoreCase))
        && (!value.TryGetProperty("publicationCommitted", out var committed) || committed.ValueKind == JsonValueKind.True);
    private static string RelativePath(JsonElement entry)
    {
        if (!TryString(entry, "relativePath", out var value) && !TryString(entry, "path", out value)) return string.Empty;
        value = value.Replace('\\', '/').TrimStart('/');
        return Path.IsPathRooted(value) ? string.Empty : value;
    }
    private static bool TryString(JsonElement entry, string name, out string value)
    { value = ""; return entry.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && (value = property.GetString() ?? "").Length != 0; }
}
