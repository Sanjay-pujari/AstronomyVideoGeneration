using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class Rc2ContentPlanningBatchOrchestrator(
    IContentPlanBatchGenerationService v4BatchGeneration,
    Rc2PipelinePhaseRegistry phaseRegistry,
    SceneIntentBuilder sceneIntentBuilder,
    ILogger<Rc2ContentPlanningBatchOrchestrator> logger)
{
    public async Task<BatchGenerateFromPlansResponse> GenerateFromPlansAsync(BatchGenerateFromPlansRequest request, CancellationToken cancellationToken)
    {
        var context = Rc2PipelineExecutionContext.Create(request);
        var requestedPhases = phaseRegistry.ResolveRequestedPhaseNumbers(request);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["ContentPlanningOrchestration"] = context.OrchestrationVersion,
            ["PlanId"] = request.PlanId,
            ["RegionId"] = request.RegionId,
            ["Language"] = request.Language,
            ["Year"] = request.Year
        });

        logger.LogInformation(
            "RC2 content planning orchestration selected for batch-generate-from-plans. DryRun={DryRun}; UseProductionPipeline={UseProductionPipeline}; ExecutionMode={ExecutionMode}; StartPhaseNo={StartPhaseNo}; EndPhaseNo={EndPhaseNo}; RequestedPhases={RequestedPhases}",
            request.DryRun,
            request.UseProductionPipeline,
            request.ExecutionMode,
            request.StartPhaseNo,
            request.EndPhaseNo,
            requestedPhases.Count == 0 ? "none" : string.Join(',', requestedPhases));

        var response = await v4BatchGeneration.GenerateFromPlansAsync(request, cancellationToken);
        await sceneIntentBuilder.BuildAndWriteDiagnosticsAsync(request, response, cancellationToken);

        logger.LogInformation(
            "RC2 content planning orchestration completed. Success={Success}; SelectedPlanCount={SelectedPlanCount}; FailedPlans={FailedPlans}; LastCompletedPhaseNo={LastCompletedPhaseNo}; LastFailedPhaseNo={LastFailedPhaseNo}; OutputRoot={OutputRoot}",
            response.Success,
            response.SelectedPlanCount,
            response.FailedPlans,
            response.LastCompletedPhaseNo,
            response.LastFailedPhaseNo,
            response.OutputRoot);

        return response;
    }
}
