using System.Reflection;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7FailureDiagnosticsPreservationTests
{
    [Fact]
    public void Phase7OverwriteCleanupPreservesSemanticDiagnosticsAndValidationEvidence()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));
        Assert.Contains("PreservePhase7DiagnosticEvidenceForOverwrite", source);
        Assert.Contains("required-semantic-fact-diagnostics.json", source);
        Assert.Contains("semantic-capability-diagnostics.json", source);
        Assert.Contains("phase-07-validation.json", source);
    }
}
