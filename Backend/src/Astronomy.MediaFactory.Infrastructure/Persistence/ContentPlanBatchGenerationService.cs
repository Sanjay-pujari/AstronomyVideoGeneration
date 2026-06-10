using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ContentPlanBatchGenerationService(
    MediaFactoryDbContext db,
    IAstronomyAssetPlanningService assetPlanning,
    IAstronomyAssetProductionJobService assetJobs,
    IVisualAssetGenerationService visualAssets,
    ISceneRenderer sceneRenderer,
    ILogger<ContentPlanBatchGenerationService> logger) : IContentPlanBatchGenerationService
{
    private const int DefaultMaxPlans = 1;
    private const int MaxPlanLimit = 10;
    private static readonly string[] RunnablePlanStatuses = ["Planned", "Approved"];
    private static readonly string[] RunnableStatuses = ["Planned", "ReadyForManualRun"];

    public async Task<BatchGenerateFromPlansResponse> GenerateFromPlansAsync(BatchGenerateFromPlansRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var requestedTitles = request.PlanTitles!
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var titleOrder = requestedTitles.Select((title, index) => new { title, index }).ToDictionary(x => x.title, x => x.index, StringComparer.OrdinalIgnoreCase);
        var maxPlans = Math.Clamp(request.MaxPlans <= 0 ? DefaultMaxPlans : request.MaxPlans, 1, MaxPlanLimit);
        var yearStart = new DateTimeOffset(request.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var yearEnd = yearStart.AddYears(1);

        var candidates = await db.ContentGenerationPlans.AsNoTracking()
            .Where(p => p.RegionId == request.RegionId && p.Language == request.Language)
            .Where(p => p.ScheduledUtc.HasValue && p.ScheduledUtc.Value >= yearStart && p.ScheduledUtc.Value < yearEnd)
            .Where(p => p.Title != null && requestedTitles.Contains(p.Title!))
            .Where(p => RunnablePlanStatuses.Contains(p.PlanStatus) && RunnableStatuses.Contains(p.Status))
            .Where(p => !request.OnlyHighPriority || p.Priority <= 10 || p.PriorityScore >= 7.5m)
            .ToListAsync(cancellationToken);

        var selectedPlans = candidates
            .OrderBy(p => p.Title is not null && titleOrder.TryGetValue(p.Title, out var index) ? index : int.MaxValue)
            .ThenByDescending(p => p.PriorityScore ?? 0m)
            .ThenBy(p => p.Priority)
            .Take(maxPlans)
            .Select(p => new BatchGenerateFromPlansSelectedPlan(
                p.Id,
                p.Title ?? string.Empty,
                p.ContentCategoryCode,
                p.PlannedFormat,
                p.RegionId,
                p.Language,
                p.ScheduledUtc,
                p.Status,
                p.PlanStatus,
                p.Priority,
                p.PriorityScore))
            .ToArray();

        var warnings = BuildSelectionWarnings(requestedTitles, selectedPlans, maxPlans);
        if (selectedPlans.Length == 0)
        {
            warnings.Add("No runnable plans matched the supplied titles, year, region, language, priority, and approved/planned status filters.");
            return new BatchGenerateFromPlansResponse(true, request.DryRun, requestedTitles.Length, 0, maxPlans, selectedPlans, [], warnings, []);
        }

        var planIds = selectedPlans.Select(p => p.ContentGenerationPlanId).ToArray();
        var steps = new List<BatchGenerateFromPlansStepResult>();

        steps.Add(await ExecuteStepAsync(
            "GenerateAssetPlans",
            () => assetPlanning.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(
                RegionId: request.RegionId,
                PlanIds: planIds,
                MaxPlans: selectedPlans.Length,
                DryRun: request.DryRun,
                OverwriteExisting: false), cancellationToken)));

        steps.Add(await ExecuteStepAsync(
            "CreateAssetProductionJobs",
            () => assetJobs.CreateAssetProductionJobsAsync(new AstronomyAssetProductionJobRequest(
                PlanIds: planIds,
                RegionId: request.RegionId,
                MaxPlans: selectedPlans.Length,
                DryRun: request.DryRun), cancellationToken)));

        steps.Add(await ExecuteStepAsync(
            "GenerateVisualAssets",
            () => visualAssets.GenerateVisualAssetsAsync(new VisualAssetGenerationRequest(
                RegionId: request.RegionId,
                PlanIds: planIds,
                MaxPlans: selectedPlans.Length,
                DryRun: request.DryRun,
                OverwriteExisting: false), cancellationToken)));

        if (request.DryRun)
        {
            var now = DateTimeOffset.UtcNow;
            steps.Add(new BatchGenerateFromPlansStepResult(
                "RenderSceneVideos",
                "Skipped",
                now,
                now,
                0,
                "Dry run selected the runnable plans and previewed upstream asset generation inputs; scene video rendering is skipped to avoid invoking media encoders.",
                null,
                new { planIds, dryRun = true }));
        }
        else
        {
            steps.Add(await ExecuteStepAsync(
                "RenderSceneVideos",
                () => sceneRenderer.RenderScenesAsync(new SceneRenderingRequest(
                    RegionId: request.RegionId,
                    PlanIds: planIds,
                    MaxPlans: selectedPlans.Length,
                    DryRun: false,
                    OverwriteExisting: false), cancellationToken)));
        }

        var errors = steps.Where(s => string.Equals(s.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            .Select(s => $"{s.StepName}: {s.ErrorMessage}")
            .ToArray();

        return new BatchGenerateFromPlansResponse(errors.Length == 0, request.DryRun, requestedTitles.Length, selectedPlans.Length, maxPlans, selectedPlans, steps, warnings, errors);
    }

    private async Task<BatchGenerateFromPlansStepResult> ExecuteStepAsync<T>(string stepName, Func<Task<T>> action)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var result = await action();
            var finished = DateTimeOffset.UtcNow;
            return new BatchGenerateFromPlansStepResult(stepName, "Completed", started, finished, (long)(finished - started).TotalMilliseconds, $"{stepName} completed.", null, result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Batch content plan generation step {StepName} failed with a business validation error.", stepName);
            var finished = DateTimeOffset.UtcNow;
            return new BatchGenerateFromPlansStepResult(stepName, "Failed", started, finished, (long)(finished - started).TotalMilliseconds, $"{stepName} failed.", ex.Message, null);
        }
    }

    private static List<string> BuildSelectionWarnings(IReadOnlyList<string> requestedTitles, IReadOnlyList<BatchGenerateFromPlansSelectedPlan> selectedPlans, int maxPlans)
    {
        var warnings = new List<string>();
        var selectedTitles = selectedPlans.Select(p => p.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingTitles = requestedTitles.Where(t => !selectedTitles.Contains(t)).ToArray();
        if (missingTitles.Length > 0)
            warnings.Add($"Skipped unmatched or non-runnable requested plan title(s): {string.Join(", ", missingTitles)}.");
        if (requestedTitles.Count > maxPlans)
            warnings.Add($"Selection was capped to maxPlans={maxPlans}; {requestedTitles.Count - maxPlans} requested title(s) may be left unprocessed.");
        return warnings;
    }

    private static void ValidateRequest(BatchGenerateFromPlansRequest request)
    {
        if (request.Year is < 2000 or > 2100) throw new ArgumentException("Year must be between 2000 and 2100.");
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("RegionId is required.");
        if (string.IsNullOrWhiteSpace(request.Language)) throw new ArgumentException("Language is required.");
        if (request.MaxPlans is < 1 or > MaxPlanLimit) throw new ArgumentException($"MaxPlans must be between 1 and {MaxPlanLimit}.");
        if (request.PlanTitles is not { Count: > 0 } || request.PlanTitles.All(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one explicit plan title is required for safe batch generation.");
    }
}
