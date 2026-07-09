using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public sealed class Rc2ContentPlanningBatchOrchestrator(
    IContentPlanBatchGenerationService v4BatchGeneration,
    Rc2PipelinePhaseRegistry phaseRegistry,
    SceneIntentBuilder sceneIntentBuilder,
    CreativeStoryboardBuilder creativeStoryboardBuilder,
    NarrationGeneratorV5 narrationGeneratorV5,
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
        if (requestedPhases.Contains(7))
        {
            var creativeStoryboardResult = await creativeStoryboardBuilder.BuildAndWriteDiagnosticsAsync(request, response, cancellationToken);
            response = ApplyRc2Phase7Response(response, creativeStoryboardResult);
        }
        if (IsRc2NarrationPhaseRequested(requestedPhases))
        {
            var narrationResult = await narrationGeneratorV5.BuildAndWriteDiagnosticsAsync(request, response, cancellationToken);
            response = await ApplyRc2Phase8ResponseAsync(response, narrationResult, cancellationToken);
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

    private static BatchGenerateFromPlansResponse ApplyRc2Phase7Response(BatchGenerateFromPlansResponse response, CreativeStoryboardBuilderResult creativeStoryboardResult)
    {
        var generatedFiles = creativeStoryboardResult.GeneratedFiles;
        var steps = response.Steps.Select(step => step is ProductionPhaseResult phase && phase.PhaseNo == 7
                ? phase with { PhaseName = "Creative Intelligence Foundation", OutputFiles = phase.OutputFiles.Concat(generatedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() }
                : step)
            .ToArray();

        if (!steps.OfType<ProductionPhaseResult>().Any(phase => phase.PhaseNo == 7) && generatedFiles.Count > 0)
        {
            steps = steps.Concat([new ProductionPhaseResult(
                7,
                "Creative Intelligence Foundation",
                ProductionPhaseStatus.Succeeded,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                0,
                [
                    Combine(response.OutputRoot, "editorial", "editorial-contract.json"),
                    Combine(response.OutputRoot, "editorial", "story-graph.json"),
                    Combine(response.OutputRoot, "editorial", "scene-intents.json")
                ],
                generatedFiles,
                Combine(response.OutputRoot, "creative", "creative-diagnostics.json"),
                [],
                [],
                false)])
                .ToArray();
        }

        var results = response.Results?.Select(result => result is ContentPlanProductionExecutionResult execution
                ? execution with
                {
                    GeneratedFiles = execution.GeneratedFiles.Concat(generatedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    PhaseResults = execution.PhaseResults is null ? null : execution.PhaseResults.Concat(steps.OfType<ProductionPhaseResult>().Where(phase => phase.PhaseNo == 7 && execution.PhaseResults.All(existing => existing.PhaseNo != 7))).ToArray()
                }
                : result)
            .ToArray();

        return response with { Steps = steps, Results = results };
    }

    private static bool IsRc2NarrationPhaseRequested(IReadOnlyList<int> requestedPhases)
        => requestedPhases.Contains(8);

    private static async Task<BatchGenerateFromPlansResponse> ApplyRc2Phase8ResponseAsync(BatchGenerateFromPlansResponse response, NarrationGeneratorV5Result narrationResult, CancellationToken cancellationToken)
    {
        var generatedFiles = narrationResult.GeneratedFiles;
        if (generatedFiles.Count == 0) return response;

        var phase8 = new ProductionPhaseResult(
            8,
            "Narration Generator V5",
            ProductionPhaseStatus.Succeeded,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            0,
            [
                Combine(response.OutputRoot, "editorial", "editorial-contract.json"),
                Combine(response.OutputRoot, "creative", "creative-storyboard.json")
            ],
            generatedFiles,
            Combine(response.OutputRoot, "narration-v5", "narration-diagnostics.json"),
            [],
            [],
            false);

        var steps = response.Steps
            .OfType<ProductionPhaseResult>()
            .Any(phase => phase.PhaseNo == 8)
            ? response.Steps.Select(step => step is ProductionPhaseResult phase && phase.PhaseNo == 8 ? phase8 : step).ToArray()
            : response.Steps.Concat([phase8]).ToArray();

        var results = response.Results?.Select(result => result is ContentPlanProductionExecutionResult execution
                ? execution with
                {
                    GeneratedFiles = execution.GeneratedFiles.Concat(generatedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    PhaseResults = UpsertPhaseResult(execution.PhaseResults, phase8)
                }
                : result)
            .ToArray();

        await UpsertPhaseManifestAsync(response.OutputRoot, phase8, cancellationToken);
        return response with { Steps = steps, Results = results };
    }

    private static IReadOnlyList<ProductionPhaseResult>? UpsertPhaseResult(IReadOnlyList<ProductionPhaseResult>? phases, ProductionPhaseResult phase)
    {
        if (phases is null) return [phase];
        return phases.Any(existing => existing.PhaseNo == phase.PhaseNo)
            ? phases.Select(existing => existing.PhaseNo == phase.PhaseNo ? phase : existing).ToArray()
            : phases.Concat([phase]).ToArray();
    }

    private static async Task UpsertPhaseManifestAsync(string? outputRoot, ProductionPhaseResult phase, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputRoot)) return;

        var manifestPath = Path.Combine(outputRoot, "phase-manifest.json");
        if (!File.Exists(manifestPath)) return;

        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken)) as JsonObject ?? new JsonObject();
        var phases = manifest["phases"] as JsonArray ?? [];
        for (var i = phases.Count - 1; i >= 0; i--)
        {
            if (phases[i]?["phaseNo"]?.GetValue<int>() == phase.PhaseNo) phases.RemoveAt(i);
        }

        phases.Add(JsonSerializer.SerializeToNode(phase));
        manifest["phases"] = phases;
        manifest["filesGeneratedThisRun"] = BuildManifestFileArray(manifest["filesGeneratedThisRun"], phase.OutputFiles);
        manifest["executedPhaseNumbers"] = BuildManifestPhaseArray(manifest["executedPhaseNumbers"], phase.PhaseNo);
        manifest["phasesActuallyExecuted"] = BuildManifestPhaseArray(manifest["phasesActuallyExecuted"], phase.PhaseNo);
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static JsonArray BuildManifestFileArray(JsonNode? existing, IReadOnlyList<string> additions)
    {
        var values = (existing as JsonArray)?.Select(node => node?.GetValue<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToList() ?? [];
        values.AddRange(additions.Where(File.Exists).Select(NormalizePath));
        return new JsonArray(values.Distinct(StringComparer.OrdinalIgnoreCase).Select(JsonValue.Create).ToArray<JsonNode?>());
    }

    private static JsonArray BuildManifestPhaseArray(JsonNode? existing, int phaseNo)
    {
        var values = (existing as JsonArray)?.Select(node => node?.GetValue<int>()).ToList() ?? [];
        values.Add(phaseNo);
        return new JsonArray(values.Distinct().Order().Select(JsonValue.Create).ToArray<JsonNode?>());
    }

    private static string Combine(string? root, params string[] parts)
        => string.IsNullOrWhiteSpace(root) ? Path.Combine(parts) : Path.Combine(new[] { root }.Concat(parts).ToArray());

    private static string NormalizePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/');
}

