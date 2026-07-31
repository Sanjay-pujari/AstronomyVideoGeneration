using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed record Phase4CommittedAuthorityEvaluation(bool IsValid, DocumentaryBlueprintAggregate? PublishedAuthority,
    string ReasonCode, IReadOnlyList<Phase4PublicationDiagnostic> Errors, IReadOnlyList<string> ArtifactPaths);

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
            return new(true, aggregate, "P4REUSE_VALID", [], Inventory(root));
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
                using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
                if (document.RootElement.TryGetProperty("phase4Artifacts", out var entries))
                    paths.AddRange(entries.EnumerateArray().Select(x => x.GetProperty("relativePath").GetString() ?? "").Where(x => x.Length != 0));
            }
            catch (JsonException) { }
        }
        var validation = Path.Combine(root, "validation", "phase-04-validation.json");
        if (File.Exists(validation)) paths.Add("validation/phase-04-validation.json");
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
