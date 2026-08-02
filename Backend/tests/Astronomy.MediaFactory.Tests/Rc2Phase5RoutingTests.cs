namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2Phase5RoutingTests
{
    private static string Rc2Source => ReadInfrastructure("Orchestration", "RC2", "Rc2ContentPlanningBatchOrchestrator.cs");
    private static string PipelineSource => ReadInfrastructure("Persistence", "ProductionPipelineExecutionService.cs");

    [Fact]
    public void Rc2Phase5Routing_ExecutesNewPhase5ExactlyOnce()
    {
        Assert.Equal(1, Count(PipelineSource, "5 => await ExecutePhase5Async("));
        Assert.DoesNotContain("sceneIntentBuilder.BuildAndWriteDiagnosticsAsync", Rc2Source);
    }

    [Fact]
    public void Rc2Phase5Routing_DoesNotInvokeLegacyEditorialIntelligence()
    {
        Assert.DoesNotContain("SceneIntentBuilder", Rc2Source);
        Assert.DoesNotContain("Editorial Intelligence", Rc2Source);
        Assert.DoesNotContain("AddScoped<SceneIntentBuilder>", ReadInfrastructure("Extensions", "ServiceCollectionExtensions.cs"));
    }

    [Fact]
    public void Rc2Phase5Routing_DoesNotRequireEditorialStoryGraph()
    {
        var phase5Owner = Slice(PipelineSource, "private async Task<ProductionPhaseResult> ExecutePhase5Async", "private async Task<IReadOnlyList<string>> PhaseCertifyDocumentaryBlueprintAsync");
        Assert.DoesNotContain("story-graph.json", phase5Owner);
        Assert.DoesNotContain("story-graph.json", ReadInfrastructure("Orchestration", "RC2", "Rc2CertifiedExecutionStatusReader.cs"));
    }

    [Fact]
    public void Rc2Phase5Routing_ProducesSinglePhase5Result()
    {
        Assert.DoesNotContain("ApplyRc2Phase5Response", Rc2Source);
        Assert.Equal(1, Count(PipelineSource, "return new ProductionPhaseResult(5, phaseName"));
    }

    [Fact]
    public void Rc2Phase5Routing_PreservesCommittedPhase5Validation()
    {
        Assert.Contains("transaction.Succeeded ? Path.Combine(context.OutputRoot, \"validation\", \"phase-05-validation.json\")", PipelineSource);
        Assert.DoesNotContain("phase-05-validation.json", Rc2Source);
    }

    [Fact]
    public void Rc2Phase5Routing_DoesNotOverwritePhase5ValidationWithGenericFailure()
    {
        Assert.Contains("if (phase.No is not (1 or 4 or 5)) await WritePhaseManifestAsync", PipelineSource);
        Assert.DoesNotContain("WritePhaseValidationAsync", Rc2Source);
    }

    [Fact]
    public void Rc2Phase5Routing_ManifestContainsSingleSuccessfulPhase5Entry()
    {
        Assert.Contains("Phase 5 manifest and validation publication are exclusively owned by its coordinator", PipelineSource);
        Assert.DoesNotContain("ApplyRc2Phase5Response", Rc2Source);
    }

    [Fact]
    public void Rc2Phase5Routing_TopLevelSuccessMatchesNestedSuccess()
    {
        Assert.Contains("var success = CalculatePipelineSuccess(context, phaseResults, errors);", PipelineSource);
        Assert.DoesNotContain("MarkResponseFailed(response, 5", Rc2Source);
    }

    [Fact]
    public void Rc2Phase5Routing_LastCompletedIs5AndNoLastFailure()
    {
        Assert.DoesNotContain("LastFailedPhaseNo = 5", Rc2Source);
        Assert.DoesNotContain("ApplyRc2Phase5Response", Rc2Source);
    }

    [Fact]
    public void Rc2Phase5Routing_CertifiedExecutionIncludesPhase5()
    {
        Assert.Contains("Rc2CertifiedExecution = await certifiedExecutionStatusReader.ReadAsync(response", Rc2Source);
        Assert.Contains("x.PhaseNo is >= 1 and <= 5", ReadInfrastructure("Orchestration", "RC2", "Rc2CertifiedExecutionStatusReader.cs"));
    }

    private static string ReadInfrastructure(params string[] parts) => File.ReadAllText(RepositoryTestPaths.InfrastructureSource(parts));

    private static int Count(string source, string value) => source.Split(value, StringSplitOptions.None).Length - 1;

    private static string Slice(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(first >= 0, $"Missing source marker: {start}");
        var last = source.IndexOf(end, first, StringComparison.Ordinal);
        Assert.True(last > first, $"Missing source marker: {end}");
        return source[first..last];
    }
}
