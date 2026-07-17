using System.Text;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Diagnostics;

internal enum SemanticLifecycleStage
{
    InputPopulation,
    ContextPopulation,
    AdapterDiscovery,
    AdapterExecution,
    CandidateSelection,
    CanonicalResolution,
    CompatibilityProjection,
    BeatRetention,
    NarrationGeneration
}

internal sealed record SemanticLifecycleStep(
    SemanticLifecycleStage Stage,
    bool Passed,
    string? Reason = null);

internal sealed record SemanticLifecycleFailure(
    SemanticLifecycleStage Stage,
    string Reason,
    IReadOnlyDictionary<string, object?> AdditionalContext);

internal static class SemanticExecutionDiagnostics
{
    public static string BuildTrace(SemanticLifecycleFailure failure)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SemanticLifecycleTrace");
        foreach (var stage in Enum.GetValues<SemanticLifecycleStage>())
        {
            if (stage < failure.Stage) builder.AppendLine($"PASS  {stage}");
            else if (stage == failure.Stage) builder.AppendLine($"FAIL  {stage}");
        }

        builder.AppendLine($"Reason: {failure.Reason}");
        builder.Append("Execution stopped.");
        return builder.ToString();
    }
}
