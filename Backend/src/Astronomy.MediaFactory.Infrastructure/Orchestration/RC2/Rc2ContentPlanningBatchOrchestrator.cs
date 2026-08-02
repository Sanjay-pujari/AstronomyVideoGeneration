using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public interface IRc2ContentPlanningBatchOrchestrator
{
    Task<BatchGenerateFromPlansResponse> GenerateFromPlansAsync(BatchGenerateFromPlansRequest request, CancellationToken cancellationToken);
}

public sealed class Rc2ContentPlanningBatchOrchestrator(
    IContentPlanBatchGenerationService v4BatchGeneration,
    Rc2PipelinePhaseRegistry phaseRegistry,
    IRc2CertifiedExecutionStatusReader certifiedExecutionStatusReader,
    ILogger<Rc2ContentPlanningBatchOrchestrator> logger) : IRc2ContentPlanningBatchOrchestrator
{
    public async Task<BatchGenerateFromPlansResponse> GenerateFromPlansAsync(BatchGenerateFromPlansRequest request, CancellationToken cancellationToken)
    {
        // An exact Phase 4 rerun without overwrite is a read-only publication probe: the
        // production pipeline validates and reuses the committed authority rather than
        // rebuilding a completed plan.  Treat that narrowly-scoped request as an allowed
        // completed-plan rerun so callers do not have to opt into destructive rebuilds.
        if (IsPhase4CommittedAuthorityReuse(request))
            request = request with { AllowCompletedPlanRerun = true };

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

        // The caller's range is authoritative. In particular, certification through
        // Phase 4 must not implicitly execute any Phase 5+ compatibility processing.
        var response = await v4BatchGeneration.GenerateFromPlansAsync(request, cancellationToken);
        response = ValidateManualPlanExecutionResponse(request, response, requestedPhases);
        response = await ReconcileEarlyPhaseValidationsAsync(response, requestedPhases, cancellationToken);
        // Phases 5 and 6 are exclusively owned by ProductionPipelineExecutionService.
        // The RC2 API is an observer: running an overlay here used to execute the retired
        // Creative Intelligence owner and overwrite the canonical Phase 6 validation and
        // manifest history after the Story Frame authority had already been committed.
        // Phase 7 is owned exclusively by ProductionPipelineExecutionService; RC2 observes and maps the authoritative result without rerunning NarrationGeneratorV5.


        response = ValidateManualPlanExecutionResponse(request, response, requestedPhases);

        logger.LogInformation(
            "RC2 content planning orchestration completed. Success={Success}; SelectedPlanCount={SelectedPlanCount}; FailedPlans={FailedPlans}; LastCompletedPhaseNo={LastCompletedPhaseNo}; LastFailedPhaseNo={LastFailedPhaseNo}; OutputRoot={OutputRoot}",
            response.Success,
            response.SelectedPlanCount,
            response.FailedPlans,
            response.LastCompletedPhaseNo,
            response.LastFailedPhaseNo,
            response.OutputRoot);

        return response with { Rc2CertifiedExecution = await certifiedExecutionStatusReader.ReadAsync(response, cancellationToken) };
    }

    private static bool IsPhase4CommittedAuthorityReuse(BatchGenerateFromPlansRequest request)
        => request.UseProductionPipeline
            && request.ExecutionMode == ContentPlanExecutionMode.RerunPhase
            && request.StartPhaseNo == 4
            && request.EndPhaseNo == 4
            && !request.OverwriteExisting;

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

    private static async Task<BatchGenerateFromPlansResponse> ReconcileEarlyPhaseValidationsAsync(BatchGenerateFromPlansResponse response, IReadOnlyList<int> requestedPhases, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(response.OutputRoot)) return response;

        // Phases 1, 2, and 3 publish their stable validation reports inside their
        // authoritative transactions. RC2 only reads those reports and reconciles
        // API state; it must never become a second writer for either stable file.
        // Phase 3 authoritative validation owner: Viewer Curiosity Framework publication lifecycle.
        foreach (var phaseNo in requestedPhases.Where(phase => phase is 1 or 2 or 3))
            response = await ReconcileAuthoritativeValidationAsync(response, phaseNo, cancellationToken);
        return response;
    }

    private static async Task<BatchGenerateFromPlansResponse> ReconcileAuthoritativeValidationAsync(BatchGenerateFromPlansResponse response, int phaseNo, CancellationToken cancellationToken)
    {
        var phase = response.Steps.OfType<ProductionPhaseResult>().FirstOrDefault(item => item.PhaseNo == phaseNo)
            ?? response.Results?.OfType<ContentPlanProductionExecutionResult>().SelectMany(item => item.PhaseResults ?? []).FirstOrDefault(item => item.PhaseNo == phaseNo);
        var validationPath = string.IsNullOrWhiteSpace(phase?.ValidationReportPath)
            ? Combine(response.OutputRoot, "validation", $"phase-{phaseNo:00}-validation.json")
            : phase.ValidationReportPath!;
        var errors = new List<string>();
        JsonDocument? document = null;
        if (!File.Exists(validationPath)) errors.Add($"Authoritative Phase {phaseNo} validation report was not published: {NormalizePath(validationPath)}");
        else
        {
            try { document = JsonDocument.Parse(await File.ReadAllTextAsync(validationPath, cancellationToken)); }
            catch (JsonException ex) { errors.Add($"Authoritative Phase {phaseNo} validation report is invalid JSON: {ex.Message}"); }
        }

        if (document is not null)
        {
            using (document)
            {
                var root = document.RootElement;
                if (!TryReadInt(root, "phaseNo", out var physicalPhaseNo) || physicalPhaseNo != phaseNo)
                    errors.Add($"Authoritative validation phase identity mismatch; expected {phaseNo}.");
                var physicalStatus = ReadString(root, "status");
                if (!IsSuccessfulPhysicalStatus(physicalStatus))
                    errors.Add($"Authoritative Phase {phaseNo} physical validation status is '{physicalStatus ?? "missing"}'.");
                if (phaseNo == 3)
                {
                    foreach (var field in new[] { "publicationCommitted", "semanticValidationPassed", "checksumValidationPassed", "manifestValidationPassed", "compatibilityEquivalencePassed", "phase2LineageValidationPassed", "questionPlanReconciliationPassed", "downstreamReady" })
                        if (!TryReadBool(root, field, out var passed) || !passed) errors.Add($"Authoritative Phase 3 validation field '{field}' is not true.");
                    foreach (var field in new[] { "validationStatus", "manifestValidationStatus", "compatibilityValidationStatus" })
                        if (!string.Equals(ReadString(root, field), "Valid", StringComparison.OrdinalIgnoreCase)) errors.Add($"Authoritative Phase 3 validation field '{field}' is not Valid.");
                    var reasonCode = ReadString(root, "reasonCode");
                    if (reasonCode is not ("P3_GENERATED" or "P3_REGENERATED" or "P3_REUSED" or "P3_RECOVERED"))
                        errors.Add("Authoritative Phase 3 validation reasonCode is missing or invalid.");
                }
            }
        }

        if (errors.Count == 0 || phase is null) return errors.Count == 0 ? response : MarkResponseFailed(response, phaseNo, errors);
        var failedPhase = phase with { Status = ProductionPhaseStatus.Failed, Errors = phase.Errors.Concat(errors).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), CanRetry = true, Reason = errors[0] };
        return MarkResponseFailed(UpsertResponsePhase(response, failedPhase), phaseNo, errors);
    }

    private static bool TryReadInt(JsonElement root, string name, out int value)
    {
        value = 0;
        foreach (var property in root.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.TryGetInt32(out value)) return true;
        return false;
    }

    private static bool TryReadBool(JsonElement root, string name, out bool value)
    {
        value = false;
        foreach (var property in root.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            { value = property.Value.GetBoolean(); return true; }
        return false;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
        return null;
    }

    private static bool IsSuccessfulPhysicalStatus(string? status)
        => status is not null && (status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Skipped", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Valid", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Passed", StringComparison.OrdinalIgnoreCase));

    private static BatchGenerateFromPlansResponse UpsertResponsePhase(BatchGenerateFromPlansResponse response, ProductionPhaseResult phase)
    {
        var steps = response.Steps.OfType<ProductionPhaseResult>().Any(existing => existing.PhaseNo == phase.PhaseNo)
            ? response.Steps.Select(step => step is ProductionPhaseResult existing && existing.PhaseNo == phase.PhaseNo ? phase : step).ToArray()
            : response.Steps.Concat([phase]).ToArray();
        var results = response.Results?.Select(result => result is ContentPlanProductionExecutionResult execution
                ? execution with { PhaseResults = UpsertPhaseResult(execution.PhaseResults, phase) }
                : result)
            .ToArray();
        return response with { Steps = steps, Results = results };
    }

    private static IReadOnlyList<ProductionPhaseResult>? UpsertPhaseResult(IReadOnlyList<ProductionPhaseResult>? phases, ProductionPhaseResult phase)
    {
        if (phases is null) return [phase];
        return phases.Any(existing => existing.PhaseNo == phase.PhaseNo)
            ? phases.Select(existing => existing.PhaseNo == phase.PhaseNo ? phase : existing).ToArray()
            : phases.Concat([phase]).ToArray();
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

    private static string Combine(string? root, params string[] parts)
        => string.IsNullOrWhiteSpace(root) ? Path.Combine(parts) : Path.Combine(new[] { root }.Concat(parts).ToArray());

    private static string NormalizePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/');
}
