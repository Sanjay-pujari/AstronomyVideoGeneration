using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7SingleAuthorityStaticTests
{
    [Fact]
    public void NarrationGeneratorV5_DoesNotWriteFinalPhase7ValidationArtifact()
    {
        var source = ReadSource("Orchestration", "RC2", "NarrationGeneratorV5.cs");
        Assert.DoesNotContain("phase-07-validation.json", source);
        Assert.DoesNotContain("Path.Combine(outputRoot, \"validation\")", source);
        Assert.Contains("generator-preflight-diagnostics.json", source);
    }

    [Fact]
    public void Rc2Orchestrator_DoesNotExecuteOrReplacePhase7Overlay()
    {
        var source = ReadSource("Orchestration", "RC2", "Rc2ContentPlanningBatchOrchestrator.cs");
        Assert.DoesNotContain("ApplyRc2Phase7ResponseAsync", source);
        Assert.DoesNotContain("narrationGeneratorV5.BuildAndWriteDiagnosticsAsync", source);
        Assert.DoesNotContain("Expected RC2 output was not created in this run", Phase7Blocks(source));
    }

    [Fact]
    public void ProductionPipeline_IsOnlyFinalPhase7ValidationWriter()
    {
        var production = ReadSource("Persistence", "ProductionPipelineExecutionService.cs");
        var rc2 = ReadSource("Orchestration", "RC2", "Rc2ContentPlanningBatchOrchestrator.cs");
        Assert.Contains("WritePhaseValidationAsync(context, phaseNo, phaseName", production);
        Assert.DoesNotContain("phase-07-validation.json", rc2);
    }

    [Fact]
    public void LegacyPhase7Helpers_AreRemoved()
    {
        Assert.Null(typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase7NarrationFilesGenerated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        Assert.Null(typeof(ProductionPipelineExecutionService).GetMethod("PersistPhase7NarrationFilesAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
    }

    private static string ReadSource(params string[] parts)
    {
        var path = RepositoryTestPaths.InfrastructureSource(parts);
        Assert.True(File.Exists(path), $"Expected source path to exist before reading: {path}");
        return File.ReadAllText(path);
    }

    private static string Phase7Blocks(string source)
    {
        var idx = source.IndexOf("Phase 7 is owned exclusively", StringComparison.Ordinal);
        return idx < 0 ? string.Empty : source[idx..Math.Min(source.Length, idx + 500)];
    }
}
