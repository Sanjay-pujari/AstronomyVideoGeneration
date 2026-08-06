using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7NarrationAuthorityOrchestrationStaticTests
{
    [Fact]
    public void ProductionRequestsCommittedPlanningAndOrchestratorStopsBeforeDraft()
    {
        var orchestrator = File.ReadAllText(RepositoryTestPaths.InfrastructureSource(
            "DocumentaryBlueprint", "Phase7NarrationAuthorityOrchestrator.cs"));
        var production = File.ReadAllText(RepositoryTestPaths.InfrastructureSource(
            "Persistence", "ProductionPipelineExecutionService.cs"));

        var stop = orchestrator.IndexOf("ThroughCommittedPlanning)", StringComparison.Ordinal);
        var draft = orchestrator.IndexOf("draftAuthorityService.ExecuteAsync", StringComparison.Ordinal);
        Assert.True(stop >= 0 && stop < draft);
        Assert.Contains("return Finish(true);", orchestrator[stop..draft]);
        Assert.Contains("ExecutionTarget = Phase7NarrationAuthorityExecutionTarget.ThroughCommittedPlanning", production);
        Assert.Contains("IsRuntimeAuthorityPreparationValid(authorityPreparation)", production);
        Assert.Contains("StageValid(\"NarrationPlanningCommittedState\", requireCommittedState: true)", production);
    }

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
        Assert.DoesNotContain("new(0,0,0,0,0,0,0)", source);
        Assert.Contains("IPhase7ProviderIsolationAudit", source);
        Assert.Contains("builtPlanning.Authority, planningValidation", source);
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
        Assert.DoesNotContain("AlreadyPublished=result.StageResults.All(s=>s.Reused||s.Success)", pipeline);
        Assert.DoesNotContain("CommittedStateValidationPassed=result.Success", pipeline);
        Assert.Contains("P7_NARRATION_AUTHORITY_UNHANDLED_FAILURE", pipeline);
    }

    [Fact]
    public void Orchestrator_is_registered_once_as_scoped_service()
    {
        var di = File.ReadAllText(RepositoryTestPaths.InfrastructureSource(
            "Extensions",
            "ServiceCollectionExtensions.cs"));
        var matches = Regex.Matches(di, "AddScoped<Astronomy\\.MediaFactory\\.Core\\.DocumentaryBlueprint\\.IPhase7NarrationAuthorityOrchestrator,");
        Assert.Single(matches);
        var auditMatches = Regex.Matches(di, "AddScoped<Astronomy\\.MediaFactory\\.Core\\.DocumentaryBlueprint\\.IPhase7ProviderIsolationAudit,");
        Assert.Single(auditMatches);
    }
}
