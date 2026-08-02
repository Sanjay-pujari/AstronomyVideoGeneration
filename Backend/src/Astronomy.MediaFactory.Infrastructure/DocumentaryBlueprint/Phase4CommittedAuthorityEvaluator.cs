using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed record Phase4CommittedAuthorityEvaluation(bool IsValid, DocumentaryBlueprintAggregate? PublishedAuthority,
    string ReasonCode, IReadOnlyList<Phase4PublicationDiagnostic> Errors, IReadOnlyList<string> ArtifactPaths)
{
    public IReadOnlyList<string> CommittedValidationEvidence { get; init; } = [];
    public IReadOnlyList<string> ManifestEvidence { get; init; } = [];
}

public interface IPhase4CommittedAuthorityEvaluator
{
    Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(string executionRoot, string expectedExecutionId,
        string expectedPlanId, string expectedEventId, string expectedLanguage, CancellationToken cancellationToken = default);
}

public sealed class Phase4CommittedAuthorityEvaluator(IPhase4ArtifactSerializer serializer,
    IPhase4CommittedStateValidator committedStateValidator) : IPhase4CommittedAuthorityEvaluator
{
    public async Task<Phase4CommittedAuthorityEvaluation> EvaluateAsync(string root, string executionId,
        string planId, string eventId, string language, CancellationToken token = default)
    {
        try
        {
            var authorityPath = Path.Combine(root, Phase4AuthorityPaths.DirectoryName, Phase4AuthorityPaths.CanonicalFileName);
            if (!File.Exists(authorityPath)) return Invalid("P4REUSE_AUTHORITY_MISSING", "Published Phase 4 authority is missing.");
            var aggregate = serializer.Deserialize<DocumentaryBlueprintAggregate>(await File.ReadAllBytesAsync(authorityPath, token));
            if ((!string.IsNullOrWhiteSpace(executionId) && aggregate.ExecutionId != executionId) ||
                (!string.IsNullOrWhiteSpace(planId) && aggregate.PlanId != planId) ||
                (!string.IsNullOrWhiteSpace(eventId) && aggregate.EventId != eventId) ||
                (!string.IsNullOrWhiteSpace(language) && !string.Equals(aggregate.Language, language, StringComparison.OrdinalIgnoreCase)))
                return Invalid("P4REUSE_IDENTITY_MISMATCH", "Published Phase 4 identity does not match the execution.");
            var errors = await committedStateValidator.ValidateAsync(root, aggregate, token);
            if (errors.Count != 0) return new(false, null, "P4REUSE_COMMITTED_STATE_INVALID", errors, Inventory(root));
            var inventory = Inventory(root);
            return new(true, aggregate, "P4REUSE_VALID", [], inventory)
            {
                CommittedValidationEvidence = inventory.Where(x => x == "validation/phase-04-validation.json").ToArray(),
                ManifestEvidence = inventory.Where(x => x == "phase-manifest.json").ToArray()
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or NotSupportedException)
        { return Invalid("P4REUSE_READ_FAILED", ex.Message); }

        Phase4CommittedAuthorityEvaluation Invalid(string code, string message) =>
            new(false, null, code, [new(code, message)], Inventory(root));
    }

    private static IReadOnlyList<string> Inventory(string root)
    {
        var paths = new List<string>();
        var manifestPath = Path.Combine(root, "phase-manifest.json");

        if (File.Exists(manifestPath))
        {
            paths.Add("phase-manifest.json");

            try
            {
                using var document = JsonDocument.Parse(
                    File.ReadAllBytes(manifestPath));

                if (document.RootElement.TryGetProperty(
                        "phase4Artifacts",
                        out var entries) &&
                    entries.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in entries.EnumerateArray())
                    {
                        if (entry.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        if (!TryGetStringProperty(
                                entry,
                                "relativePath",
                                out var relativePath) ||
                            string.IsNullOrWhiteSpace(relativePath))
                        {
                            continue;
                        }

                        paths.Add(NormalizeRelativePath(relativePath));
                    }
                }
            }
            catch (JsonException)
            {
                // Inventory is supporting status information.
                // Committed-state validation remains authoritative.
            }
            catch (IOException)
            {
                // Do not allow optional inventory reporting to crash
                // committed-authority evaluation.
            }
        }

        var validationPath = Path.Combine(
            root,
            "validation",
            "phase-04-validation.json");

        if (File.Exists(validationPath))
        {
            paths.Add("validation/phase-04-validation.json");
        }

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetStringProperty(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        value = null;

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = property.Value.GetString();
            return true;
        }

        return false;
    }

    private static string NormalizeRelativePath(string path)
    {
        return path
            .Replace('\\', '/')
            .TrimStart('/');
    }
}
