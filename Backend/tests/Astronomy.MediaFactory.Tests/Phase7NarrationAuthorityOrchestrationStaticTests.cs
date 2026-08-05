using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7NarrationAuthorityOrchestrationStaticTests
{
    [Fact]
    public void Phase7Orchestrator_invokes_provider_free_authority_stages_in_governed_order()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource(
            "DocumentaryBlueprint",
            "Phase7NarrationAuthorityOrchestrator.cs"));
        var order = new[]
        {
            "knowledgeService.ExecuteAsync",
            "knowledgeCommittedStateEvaluator.EvaluateAsync",
            "packetInputEvaluator.EvaluateAsync",
            "packetBuilder.Build",
            "packetValidator.Validate",
            "planningInputEvaluator.EvaluateAsync",
            "planningBuilder.Build",
            "planningValidator.Validate",
            "planningPublicationService.ExecuteAsync",
            "planningCommittedStateEvaluator.EvaluateAsync",
            "draftAuthorityService.ExecuteAsync"
        };
        var previous = -1;
        foreach (var token in order)
        {
            var current = source.IndexOf(token, StringComparison.Ordinal);
            Assert.True(current > previous, $"{token} was not found after the previous governed stage.");
            previous = current;
        }
        Assert.DoesNotContain("NarrationGeneratorV5", source);
        Assert.DoesNotContain("NarrationPromptComposer", source);
        Assert.DoesNotContain("AzureSpeech", source);
        Assert.Contains("new(0,0,0,0,0,0,0)", source);
    }

    [Fact]
    public void Public_phase_7_resolves_single_narration_authority_boundary()
    {
        var pipeline = File.ReadAllText(RepositoryTestPaths.InfrastructureSource(
            "Persistence",
            "ProductionPipelineExecutionService.cs"));
        var registry = File.ReadAllText(RepositoryTestPaths.InfrastructureSource(
            "Orchestration",
            "RC2",
            "Rc2PipelinePhaseRegistry.cs"));
        Assert.Contains("7 => await ExecutePhase7NarrationAuthorityAsync", pipeline);
        Assert.Contains("(7, \"Narration Authority\"", pipeline);
        Assert.Contains("new(7, \"Narration Authority\")", registry);
    }

    [Fact]
    public void Orchestrator_is_registered_once_as_scoped_service()
    {
        var di = File.ReadAllText(RepositoryTestPaths.InfrastructureSource(
            "Extensions",
            "ServiceCollectionExtensions.cs"));
        var matches = Regex.Matches(di, "AddScoped<Astronomy\\.MediaFactory\\.Core\\.DocumentaryBlueprint\\.IPhase7NarrationAuthorityOrchestrator,");
        Assert.Single(matches);
    }
}
