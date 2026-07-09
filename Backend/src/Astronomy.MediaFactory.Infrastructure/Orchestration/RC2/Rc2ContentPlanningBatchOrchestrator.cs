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
        response = ValidateManualPlanExecutionResponse(request, response, requestedPhases);
        if (requestedPhases.Contains(5) && CanRunRc2Overlay(response, 5))
        {
            response = await ExecuteRc2OverlayPhaseAsync(
                response,
                5,
                "Editorial Intelligence Foundation",
                [
                    Combine(response.OutputRoot, "plan-input", "production-event-intelligence.json"),
                    Combine(response.OutputRoot, "question-engine", "question-answer-set.json"),
                    Combine(response.OutputRoot, "question-engine", "question-driven-scene-plan.json")
                ],
                Combine(response.OutputRoot, "editorial", "editorial-diagnostics.json"),
                async () =>
                {
                    var sceneIntentResult = await sceneIntentBuilder.BuildAndWriteDiagnosticsAsync(request, response, cancellationToken);
                    response = ApplyRc2Phase5Response(response, sceneIntentResult);
                    return sceneIntentResult.GeneratedFiles;
                },
                cancellationToken);
        }
        if (requestedPhases.Contains(6) && CanRunRc2Overlay(response, 6))
        {
            response = await ExecuteRc2OverlayPhaseAsync(
                response,
                6,
                "Creative Intelligence / Story Frames",
                [
                    Combine(response.OutputRoot, "editorial", "editorial-contract.json"),
                    Combine(response.OutputRoot, "editorial", "story-graph.json"),
                    Combine(response.OutputRoot, "editorial", "scene-intents.json")
                ],
                Combine(response.OutputRoot, "creative", "creative-diagnostics.json"),
                async () =>
                {
                    var creativeStoryboardResult = await creativeStoryboardBuilder.BuildAndWriteDiagnosticsAsync(request, response, cancellationToken);
                    response = ApplyRc2Phase6Response(response, creativeStoryboardResult);
                    return creativeStoryboardResult.GeneratedFiles;
                },
                cancellationToken);
        }
        if (IsRc2NarrationPhaseRequested(requestedPhases) && CanRunRc2Overlay(response, 7))
        {
            response = await ExecuteRc2OverlayPhaseAsync(
                response,
                7,
                "Narration Studio V5",
                [
                    Combine(response.OutputRoot, "editorial", "editorial-contract.json"),
                    Combine(response.OutputRoot, "creative", "creative-storyboard.json")
                ],
                Combine(response.OutputRoot, "narration-v5", "narration-diagnostics.json"),
                async () =>
                {
                    var narrationResult = await narrationGeneratorV5.BuildAndWriteDiagnosticsAsync(request, response, cancellationToken);
                    response = await ApplyRc2Phase7ResponseAsync(response, narrationResult, cancellationToken);
                    return narrationResult.GeneratedFiles;
                },
                cancellationToken);
        }

        response = ValidateManualPlanExecutionResponse(request, response, requestedPhases);

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

    private static BatchGenerateFromPlansResponse ValidateManualPlanExecutionResponse(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, IReadOnlyList<int> requestedPhases)
    {
        if (!request.PlanId.HasValue) return response;

        var failurePhaseNo = ResolveManualFailurePhaseNo(request, response, requestedPhases);
        var errors = new List<string>();
        var warnings = new List<BatchGenerateFromPlansWarning>();

        if (response.SelectedPlanCount == 0)
        {
            errors.Add("Manual planId was provided but no executable plan was selected.");
            warnings.Add(new BatchGenerateFromPlansWarning(request.PlanId.Value.ToString("D"), false, false, "Manual planId was provided but no executable plan was selected."));
        }

        if (response.Success && string.IsNullOrWhiteSpace(response.OutputRoot))
        {
            errors.Add("Manual planId execution did not resolve an OutputRoot.");
        }

        if (errors.Count == 0) return response;

        return response with
        {
            Success = false,
            FailedPlans = Math.Max(1, response.FailedPlans),
            LastFailedPhaseNo = response.LastFailedPhaseNo ?? failurePhaseNo,
            LastCompletedPhaseNo = response.LastCompletedPhaseNo is null ? null : Math.Min(response.LastCompletedPhaseNo.Value, failurePhaseNo - 1),
            Warnings = response.Warnings.Concat(warnings).ToArray(),
            Errors = response.Errors.Concat(errors).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static int ResolveManualFailurePhaseNo(BatchGenerateFromPlansRequest request, BatchGenerateFromPlansResponse response, IReadOnlyList<int> requestedPhases)
        => request.StartPhaseNo
            ?? response.StartPhaseNo
            ?? response.RequestedStartPhase
            ?? requestedPhases.DefaultIfEmpty(1).Min();

    private async Task<BatchGenerateFromPlansResponse> ExecuteRc2OverlayPhaseAsync(
        BatchGenerateFromPlansResponse response,
        int phaseNo,
        string phaseName,
        IReadOnlyList<string> inputFiles,
        string diagnosticsPath,
        Func<Task<IReadOnlyList<string>>> executeAsync,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var generatedFiles = await executeAsync();
            var currentFiles = generatedFiles.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var status = currentFiles.Length == generatedFiles.Count ? ProductionPhaseStatus.Succeeded : ProductionPhaseStatus.Failed;
            var errors = status == ProductionPhaseStatus.Succeeded
                ? Array.Empty<string>()
                : generatedFiles.Where(path => !File.Exists(path)).Select(path => $"Expected RC2 output was not created in this run: {NormalizePath(path)}").ToArray();
            var phase = await WriteRc2PhaseValidationAsync(response.OutputRoot, phaseNo, phaseName, status, started, inputFiles, currentFiles, diagnosticsPath, [], errors, status == ProductionPhaseStatus.Succeeded ? "Validation passed." : "Validation failed: required output missing.", status != ProductionPhaseStatus.Succeeded, null, cancellationToken);
            response = UpsertResponsePhase(response, phase);
            await UpsertPhaseManifestAsync(response.OutputRoot, phase, cancellationToken);
            return status == ProductionPhaseStatus.Succeeded ? response : MarkResponseFailed(response, phaseNo, errors);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var phase = await WriteRc2PhaseValidationAsync(response.OutputRoot, phaseNo, phaseName, ProductionPhaseStatus.Failed, started, inputFiles, [], diagnosticsPath, [], [ex.Message], ex.Message, true, ex, cancellationToken);
            response = UpsertResponsePhase(response, phase);
            await UpsertPhaseManifestAsync(response.OutputRoot, phase, cancellationToken);
            return MarkResponseFailed(response, phaseNo, [ex.Message]);
        }
    }

    private static bool CanRunRc2Overlay(BatchGenerateFromPlansResponse response, int phaseNo)
        => response.Success
            && response.LastFailedPhaseNo is null
            && !response.Steps.OfType<ProductionPhaseResult>().Any(phase => phase.PhaseNo == phaseNo && phase.Status == ProductionPhaseStatus.Failed);

    private static async Task<ProductionPhaseResult> WriteRc2PhaseValidationAsync(string? outputRoot, int phaseNo, string phaseName, ProductionPhaseStatus status, DateTimeOffset started, IReadOnlyList<string> inputFiles, IReadOnlyList<string> outputFiles, string diagnosticsPath, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, string reason, bool canRetry, Exception? exception, CancellationToken cancellationToken)
    {
        var finished = DateTimeOffset.UtcNow;
        var validationPath = Combine(outputRoot, "validation", $"phase-{phaseNo:00}-validation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(validationPath)!);
        var result = new ProductionPhaseResult(phaseNo, phaseName, status, started, finished, (long)(finished - started).TotalMilliseconds, inputFiles.Select(NormalizePath).ToArray(), outputFiles.Select(NormalizePath).ToArray(), NormalizePath(validationPath), warnings, errors, canRetry, reason);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new
        {
            phaseNo,
            phaseName,
            status = status.ToString(),
            startedUtc = started,
            finishedUtc = finished,
            durationMs = result.DurationMs,
            inputFiles = result.InputFiles,
            outputFiles = result.OutputFiles,
            warnings,
            errors,
            exceptionType = exception?.GetType().Name,
            exceptionMessage = exception?.Message,
            canRetry,
            reason,
            diagnosticsPath = NormalizePath(diagnosticsPath),
            validationScope = "RC2 overlay phase validation; production orchestration result is not overwritten when production phase failed."
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return result;
    }

    private static BatchGenerateFromPlansResponse UpsertResponsePhase(BatchGenerateFromPlansResponse response, ProductionPhaseResult phase)
    {
        var steps = response.Steps.OfType<ProductionPhaseResult>().Any(existing => existing.PhaseNo == phase.PhaseNo)
            ? response.Steps.Select(step => step is ProductionPhaseResult existing && existing.PhaseNo == phase.PhaseNo ? phase : step).ToArray()
            : response.Steps.Concat([phase]).ToArray();
        var results = response.Results?.Select(result => result is ContentPlanProductionExecutionResult execution
                ? execution with { PhaseResults = UpsertPhaseResult(execution.PhaseResults, phase), GeneratedFiles = execution.GeneratedFiles.Concat(phase.OutputFiles.Where(File.Exists)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() }
                : result)
            .ToArray();
        return response with { Steps = steps, Results = results };
    }

    private static BatchGenerateFromPlansResponse MarkResponseFailed(BatchGenerateFromPlansResponse response, int phaseNo, IReadOnlyList<string> errors)
        => response with
        {
            Success = false,
            FailedPlans = Math.Max(1, response.FailedPlans),
            LastFailedPhaseNo = phaseNo,
            LastCompletedPhaseNo = response.LastCompletedPhaseNo is null ? null : Math.Min(response.LastCompletedPhaseNo.Value, phaseNo - 1),
            Errors = response.Errors.Concat(errors).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };

    private static BatchGenerateFromPlansResponse ApplyRc2Phase5Response(BatchGenerateFromPlansResponse response, SceneIntentBuilderResult sceneIntentResult)
    {
        var generatedFiles = sceneIntentResult.GeneratedFiles;
        var steps = response.Steps.Select(step => step is ProductionPhaseResult phase && phase.PhaseNo == 5
                ? phase with { PhaseName = "Editorial Intelligence Foundation", OutputFiles = phase.OutputFiles.Concat(generatedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() }
                : step)
            .ToArray();

        if (!steps.OfType<ProductionPhaseResult>().Any(phase => phase.PhaseNo == 5) && generatedFiles.Count > 0)
        {
            steps = steps.Concat([new ProductionPhaseResult(
                5,
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
                    PhaseResults = execution.PhaseResults?.Select(phase => phase.PhaseNo == 5
                            ? phase with { PhaseName = "Editorial Intelligence Foundation", OutputFiles = phase.OutputFiles.Concat(generatedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() }
                            : phase)
                        .ToArray()
                }
                : result)
            .ToArray();

        return response with { Steps = steps, Results = results };
    }

    private static BatchGenerateFromPlansResponse ApplyRc2Phase6Response(BatchGenerateFromPlansResponse response, CreativeStoryboardBuilderResult creativeStoryboardResult)
    {
        var generatedFiles = creativeStoryboardResult.GeneratedFiles;
        var steps = response.Steps.Select(step => step is ProductionPhaseResult phase && phase.PhaseNo == 6
                ? phase with { PhaseName = "Creative Intelligence / Story Frames", OutputFiles = phase.OutputFiles.Concat(generatedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() }
                : step)
            .ToArray();

        if (!steps.OfType<ProductionPhaseResult>().Any(phase => phase.PhaseNo == 6) && generatedFiles.Count > 0)
        {
            steps = steps.Concat([new ProductionPhaseResult(
                6,
                "Creative Intelligence / Story Frames",
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
                    PhaseResults = execution.PhaseResults is null ? null : execution.PhaseResults.Concat(steps.OfType<ProductionPhaseResult>().Where(phase => phase.PhaseNo == 6 && execution.PhaseResults.All(existing => existing.PhaseNo != 6))).ToArray()
                }
                : result)
            .ToArray();

        return response with { Steps = steps, Results = results };
    }

    private static bool IsRc2NarrationPhaseRequested(IReadOnlyList<int> requestedPhases)
        => requestedPhases.Contains(7);

    private static async Task<BatchGenerateFromPlansResponse> ApplyRc2Phase7ResponseAsync(BatchGenerateFromPlansResponse response, NarrationGeneratorV5Result narrationResult, CancellationToken cancellationToken)
    {
        var generatedFiles = narrationResult.GeneratedFiles;
        if (generatedFiles.Count == 0) return response;

        var phase7 = new ProductionPhaseResult(
            7,
            "Narration Studio V5",
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
            .Any(phase => phase.PhaseNo == 7)
            ? response.Steps.Select(step => step is ProductionPhaseResult phase && phase.PhaseNo == 7 ? phase7 : step).ToArray()
            : response.Steps.Concat([phase7]).ToArray();

        var results = response.Results?.Select(result => result is ContentPlanProductionExecutionResult execution
                ? execution with
                {
                    GeneratedFiles = execution.GeneratedFiles.Concat(generatedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    PhaseResults = UpsertPhaseResult(execution.PhaseResults, phase7)
                }
                : result)
            .ToArray();

        await UpsertPhaseManifestAsync(response.OutputRoot, phase7, cancellationToken);
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
        if (phase.Status == ProductionPhaseStatus.Succeeded)
        {
            manifest["phasesActuallyExecuted"] = BuildManifestPhaseArray(manifest["phasesActuallyExecuted"], phase.PhaseNo);
            manifest["lastCompletedPhaseNo"] = Math.Max(manifest["lastCompletedPhaseNo"]?.GetValue<int>() ?? 0, phase.PhaseNo);
            manifest["lastFailedPhaseNo"] = null;
        }
        else if (phase.Status == ProductionPhaseStatus.Failed)
        {
            manifest["lastFailedPhaseNo"] = phase.PhaseNo;
        }
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    private static JsonArray BuildManifestFileArray(JsonNode? existing, IReadOnlyList<string> additions)
    {
        var values = new List<string>();
        if (existing is JsonArray existingFiles)
        {
            foreach (var node in existingFiles)
            {
                var value = node?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
            }
        }

        values.AddRange(additions.Where(File.Exists).Select(NormalizePath));
        var uniqueFiles = values.Distinct(StringComparer.OrdinalIgnoreCase).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray();
        return new JsonArray(uniqueFiles);
    }

    private static JsonArray BuildManifestPhaseArray(JsonNode? existing, int phaseNo)
    {
        var values = new List<int>();
        if (existing is JsonArray existingPhases)
        {
            foreach (var node in existingPhases)
            {
                if (node is not null) values.Add(node.GetValue<int>());
            }
        }

        values.Add(phaseNo);
        var uniquePhases = values.Distinct().Order().Select(value => (JsonNode?)JsonValue.Create(value)).ToArray();
        return new JsonArray(uniquePhases);
    }

    private static string Combine(string? root, params string[] parts)
        => string.IsNullOrWhiteSpace(root) ? Path.Combine(parts) : Path.Combine(new[] { root }.Concat(parts).ToArray());

    private static string NormalizePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/');
}
