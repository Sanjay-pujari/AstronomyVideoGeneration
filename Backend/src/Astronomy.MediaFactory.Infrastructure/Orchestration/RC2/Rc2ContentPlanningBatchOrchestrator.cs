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
        if (requestedPhases.Contains(6))
        {
            var sceneIntentResult = await sceneIntentBuilder.BuildAndWriteDiagnosticsAsync(request, response, cancellationToken);
            response = ApplyRc2Phase6Response(response, sceneIntentResult);
        }

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

    private static BatchGenerateFromPlansResponse ApplyRc2Phase6Response(BatchGenerateFromPlansResponse response, SceneIntentBuilderResult sceneIntentResult)
    {
        var generatedFiles = sceneIntentResult.GeneratedFiles;
        var steps = response.Steps.Select(step => step is ProductionPhaseResult phase && phase.PhaseNo == 6
                ? phase with { PhaseName = "Editorial Intelligence Foundation", OutputFiles = phase.OutputFiles.Concat(generatedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() }
                : step)
            .ToArray();

        if (!steps.OfType<ProductionPhaseResult>().Any(phase => phase.PhaseNo == 6) && generatedFiles.Count > 0)
        {
            steps = steps.Concat([new ProductionPhaseResult(
                6,
                "Editorial Intelligence Foundation",
                ProductionPhaseStatus.Succeeded,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                0,
                [
                    Combine(response.OutputRoot, "plan-input", "production-event-intelligence.json"),
                    Combine(response.OutputRoot, "question-engine", "question-answer-set.json"),
                    Combine(response.OutputRoot, "question-engine", "question-driven-scene-plan.json")
                ],
                generatedFiles,
                Combine(response.OutputRoot, "editorial", "editorial-diagnostics.json"),
                [],
                [],
                false)])
                .ToArray();
        }

        var results = response.Results?.Select(result => result is ContentPlanProductionExecutionResult execution
                ? execution with
                {
                    GeneratedFiles = execution.GeneratedFiles.Concat(generatedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    PhaseResults = execution.PhaseResults?.Select(phase => phase.PhaseNo == 6
                            ? phase with { PhaseName = "Editorial Intelligence Foundation", OutputFiles = phase.OutputFiles.Concat(generatedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() }
                            : phase)
                        .ToArray()
                }
                : result)
            .ToArray();

        return response with { Steps = steps, Results = results };
    }

    private static string Combine(string? root, params string[] parts)
        => string.IsNullOrWhiteSpace(root) ? Path.Combine(parts) : Path.Combine(new[] { root }.Concat(parts).ToArray());
}

