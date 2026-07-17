using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Diagnostics;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticExecutionDiagnosticsTests
{
    [Fact]
    public void BuildTrace_FirstStageFailure_PrintsOnlyFailureAndStops()
    {
        var failure = new SemanticLifecycleFailure(
            SemanticLifecycleStage.InputPopulation,
            "Input missing.",
            new Dictionary<string, object?>());

        var trace = SemanticExecutionDiagnostics.BuildTrace(failure);

        Assert.Equal("SemanticLifecycleTrace\nFAIL  InputPopulation\nReason: Input missing.\nExecution stopped.", trace);
        Assert.DoesNotContain("PASS", trace);
        Assert.DoesNotContain("ContextPopulation", trace);
    }

    [Fact]
    public void BuildTrace_MiddleStageFailure_PrintsPreviousStagesAsPassedOnly()
    {
        var failure = new SemanticLifecycleFailure(
            SemanticLifecycleStage.CandidateSelection,
            "No candidate.",
            new Dictionary<string, object?>());

        var trace = SemanticExecutionDiagnostics.BuildTrace(failure);

        Assert.Contains("PASS  InputPopulation", trace);
        Assert.Contains("PASS  ContextPopulation", trace);
        Assert.Contains("PASS  AdapterDiscovery", trace);
        Assert.Contains("PASS  AdapterExecution", trace);
        Assert.Contains("FAIL  CandidateSelection", trace);
        Assert.DoesNotContain("CanonicalResolution", trace);
        Assert.DoesNotContain("CompatibilityProjection", trace);
        Assert.DoesNotContain("BeatRetention", trace);
        Assert.DoesNotContain("NarrationGeneration", trace);
    }

    [Fact]
    public void BuildTrace_FinalStageFailure_PrintsAllPreviousStagesAsPassed()
    {
        var failure = new SemanticLifecycleFailure(
            SemanticLifecycleStage.NarrationGeneration,
            "Narration failed.",
            new Dictionary<string, object?>());

        var trace = SemanticExecutionDiagnostics.BuildTrace(failure);

        Assert.Contains("PASS  InputPopulation", trace);
        Assert.Contains("PASS  ContextPopulation", trace);
        Assert.Contains("PASS  AdapterDiscovery", trace);
        Assert.Contains("PASS  AdapterExecution", trace);
        Assert.Contains("PASS  CandidateSelection", trace);
        Assert.Contains("PASS  CanonicalResolution", trace);
        Assert.Contains("PASS  CompatibilityProjection", trace);
        Assert.Contains("PASS  BeatRetention", trace);
        Assert.Contains("FAIL  NarrationGeneration", trace);
        Assert.EndsWith("Execution stopped.", trace, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTrace_IsDeterministic()
    {
        var failure = new SemanticLifecycleFailure(
            SemanticLifecycleStage.AdapterDiscovery,
            "No adapter was discovered.",
            new Dictionary<string, object?> { ["ignored"] = DateTimeOffset.UnixEpoch });

        var first = SemanticExecutionDiagnostics.BuildTrace(failure);
        var second = SemanticExecutionDiagnostics.BuildTrace(failure);

        Assert.Equal(first, second);
    }
}
